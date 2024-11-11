using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Market.Interop;

[StructLayout(LayoutKind.Explicit, Size = 8)]
public unsafe struct MarketBoardItemListingCount
{
    [FieldOffset(0)] public uint Error;
    [FieldOffset(4)] public byte NumItems;
}

[StructLayout(LayoutKind.Explicit, Size = 0x90)]
public unsafe struct MarketBoardItemListingEntry
{
    [FieldOffset(0x00)] public ulong ListingId;
    [FieldOffset(0x08)] public ulong SellingRetainerContentId;
    [FieldOffset(0x10)] public ulong SellingPlayerContentId;
    [FieldOffset(0x18)] public ulong ArtisanId;
    [FieldOffset(0x20)] public uint UnitPrice;
    [FieldOffset(0x24)] public uint TotalTax;
    [FieldOffset(0x28)] public uint Quantity;
    [FieldOffset(0x2C)] public uint ItemId;
    [FieldOffset(0x30)] public ushort ContainerId;
    [FieldOffset(0x32)] public ushort Durability;
    [FieldOffset(0x34)] public ushort Spiritbond;
    [FieldOffset(0x36)] public fixed ushort Materia[5];
    [FieldOffset(0x46)] public fixed byte RetainerName[32];
    [FieldOffset(0x86)] public byte IsHQ;
    [FieldOffset(0x87)] public byte MateriaCount;
    [FieldOffset(0x89)] public byte TownId;
    [FieldOffset(0x8A)] public byte Stain0Id;
    [FieldOffset(0x8B)] public byte Stain1Id;
}

[StructLayout(LayoutKind.Explicit, Size = 0x90)]
public unsafe struct MarketBoardItemListing
{
    [FieldOffset(0x000)] public fixed byte EntriesRaw[10 * 0x90];
    [FieldOffset(0x5A0)] public byte NextPageIndex;
    [FieldOffset(0x5A1)] public byte FirstPageIndex;
    [FieldOffset(0x5A2)] public byte SequenceIndex;
}

[StructLayout(LayoutKind.Explicit, Size = 0x30)]
public unsafe struct MarketBoardItemListingHistoryEntry
{
    [FieldOffset(0x00)] public uint UnitPrice;
    [FieldOffset(0x04)] public uint SaleUnixTimestamp;
    [FieldOffset(0x08)] public uint Quantity;
    [FieldOffset(0x0C)] public byte IsHQ;
    [FieldOffset(0x0D)] public byte UnkD;
    [FieldOffset(0x0E)] public fixed byte RetainerName[32];
}

[StructLayout(LayoutKind.Explicit, Size = 0x3C8)]
public unsafe struct MarketBoardItemListingHistory
{
    [FieldOffset(0x00)] public uint ItemId;
    [FieldOffset(0x04)] public fixed byte RawEntries[20 * 0x30];
}

// how marketboard requests work:
// - normal flow
// -- client sends 'request item listings' ipc, specifies item id and some unknown search params; InfoProxyItemSearch sets 'waiting for data' to false
// -- server sends 'item listings summary' ipc immediately followed by 'item history' ipc; former contains error message (0 normally) and num listings (max 100), latter contains item id
// --- if item count is > 0, proxy sets 'waiting for data' flag and increments sequence id, then client sends 'request item listings page' ipc (specifies page start 0 and sequence id, no item id!)
// --- server sends 'item listings page' ipc, contains 10 entries, page start index and next page start index (if more data is available) or 0 (if this is the last page)
// --- if more data is available, proxy saves page data in the fields (if 'waiting for data' flag is still set) and sends another 'request item listings page' ipc (with next-page start index and same sequence id)
// --- if no more data is available, the process ends (no more ipcs, 'waiting for data' flags remains set)
// --- if there are no items (item count == 0 in summary), none of the page requests happen: proxy list remains empty, 'waiting for data' flag remains unset
// --- if at any time sequence index of the 'item listings page' does not match what proxy expects, it sends 'request item listings page' ipc with next-page start index == -1 and sequence id from packet
// ---- this seems to be broken and will lead to the infinite request-response loop: server will respond with another page with same sequence id, which will again mismatch what proxy has, so proxy will repeat the same step
// -- at the same time server sends 'item history' ipc, contains item id and 20 history entries
// - second request flow
// -- proxy doesn't have any rate limiting logic - on second request, it again sends 'request item listings' ipc and clears 'waiting for data' flag
// -- server responds with 'item listings summary' ipc with 0 items and non-zero error message; note that there is no 'history' packet, so we don't know the item id
// -- proxy then clears the internal list and does nothing (does not touch sequence id or 'waiting for data' flag)
// -- this means that the response-request sequence for the previously requested item continues uninterrupted until the normal end - however, since 'waiting for data' flag is cleared, data is not added to the proxy lists
// -- if two 'request item listings' ipc are sent one after another, the error response for the subsequent one can arrive before the normal success response for the first request
// TODO: reverse meaning of request params

// utility for fetching marketboard listings
// it uses existing code in InfoProxyItemSearch to drive the request-response sequence, hooks its functions and emits events when new data is available
// it also stores the received data internally, which is not affected by interruptions like proxy's built-in list is
// it does not provide any rate limiting, and does not notify about failed requests (since error response does not identify which request failed)
public sealed class MBFetch : IDisposable
{
    public DateTime NextSafeRequest { get; private set; } // minimal time when we expect new request to succeed
    public MarketListings? CurrentRequest { get; private set; } // if non null, this is the request that is being filled with ongoing request-response sequence

    public bool IsRequestLikelyToSucceed => DateTime.Now >= NextSafeRequest;

    public event Action<MarketListings>? RequestComplete; // event emitted when request fully completes

    private unsafe InfoProxyItemSearch* _proxy = (InfoProxyItemSearch*)InfoModule.Instance()->InfoProxies[(int)InfoProxyId.ItemSearch].Value;
    private Hook<InfoProxyItemSearch.Delegates.ProcessRequestResult> _processRequestHook;
    private Hook<InfoProxyItemSearch.Delegates.ProcessItemHistory> _processHistoryHook;
    private Hook<InfoProxyItemSearch.Delegates.AddPage> _addPageHook;
    private int _numExpectedListings = -1; // < 0 if there is no request-response sequence in progress

    private const float RateLimit = 1;
    private const float RequestTimeout = 10;
    private const int MaxRetries = 3; // max number of times a request can be repeated if response is an error before request is abandoned

    public unsafe MBFetch()
    {
        _processRequestHook = Service.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.ProcessRequestResult>(InfoProxyItemSearch.Addresses.ProcessRequestResult.Value, ProcessRequestDetour);
        _processRequestHook.Enable();
        _processHistoryHook = Service.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.ProcessItemHistory>(InfoProxyItemSearch.Addresses.ProcessItemHistory.Value, ProcessHistoryDetour);
        _processHistoryHook.Enable();
        _addPageHook = Service.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.AddPage>((nint)_proxy->VirtualTable->AddPage, AddPageDetour);
        _addPageHook.Enable();
    }

    public void Dispose()
    {
        _addPageHook.Dispose();
        _processHistoryHook.Dispose();
        _processRequestHook.Dispose();
    }

    public unsafe bool ExecuteRequest(uint itemId)
    {
        _proxy->WaitingForListings = false;
        _proxy->SearchItemId = itemId;
        _proxy->Unk_0x24 = 2;
        _proxy->Unk_0x25 = 9;
        _proxy->Unk_0x28 = 0;
        return _proxy->RequestData();
    }

    public async Task<MarketListings> RequestAsync(uint itemId, CancellationToken cancel = default)
    {
        for (int i = 0; i < MaxRetries; ++i)
        {
            // rate limit (note that we might get new external requests while waiting)
            while (NextSafeRequest - DateTime.Now is var rateLimit && rateLimit > TimeSpan.Zero)
                await Task.Delay(rateLimit, cancel);
            cancel.ThrowIfCancellationRequested();
            if (!ExecuteRequest(itemId))
                throw new Exception("Failed to execute request"); // this should not really happen, unless network module is shut down

            var tcs = new TaskCompletionSource<MarketListings>();
            void completionHandler(MarketListings result)
            {
                if (result.ItemId == itemId)
                    tcs.SetResult(result);
            }
            RequestComplete += completionHandler;
            using var unregister = new RAII(() => RequestComplete -= completionHandler);

            try
            {
                // TODO: consider timing out after each packet instead (the entire sequence can take several seconds normally)
                return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(RequestTimeout), cancel);
            }
            catch (TimeoutException)
            {
                Service.Log.Error($"Request for {itemId} timed out on attempt #{i}");
            }
        }
        throw new Exception($"Request for {itemId} timed out");
    }

    private unsafe void ProcessRequestDetour(InfoProxyItemSearch* self, byte numItems, uint errorMessageId)
    {
        var oriNextSeq = self->NextRequestId;
        _processRequestHook.Original(self, numItems, errorMessageId);

        NextSafeRequest = DateTime.Now.AddSeconds(RateLimit);
        LogAssert((self->NextRequestId != oriNextSeq) == (numItems > 0), $"ProcessRequestDetour: got {numItems} items ({errorMessageId} error), proxy sequence change is {oriNextSeq}->{self->NextRequestId}");
        LogAssert(errorMessageId == 0 || numItems == 0, $"ProcessRequestDetour: got {numItems} errors with {errorMessageId} error");
        LogAssert(errorMessageId != 0 || _numExpectedListings < 0, $"ProcessRequestDetour: successful request for {numItems} despite another request not being finished");
        if (errorMessageId == 0)
        {
            _numExpectedListings = numItems; // at very least we wait for history packet
        }
        // else: error happened, we don't really care...
    }

    private unsafe void ProcessHistoryDetour(InfoProxyItemSearch* self, nint packet)
    {
        _processHistoryHook.Original(self, packet);

        var data = (MarketBoardItemListingHistory*)packet;
        NextSafeRequest = DateTime.Now.AddSeconds(RateLimit);
        LogAssert(_numExpectedListings >= 0, $"ProcessHistoryDetour: received history without preceeding successful result packet");
        LogAssert(CurrentRequest == null, $"ProcessHistoryDetour: received history while previous request was not completed");

        CurrentRequest = new(data->ItemId, Player.GetCurrentWorldId(), DateTime.Now);
        if (_numExpectedListings == 0)
        {
            // complete immediately
            RequestComplete?.Invoke(CurrentRequest);
            CurrentRequest = null;
            _numExpectedListings = -1;
        }
    }

    private unsafe void AddPageDetour(InfoProxyItemSearch* self, nint packet)
    {
        _addPageHook.Original(self, packet);

        var data = (MarketBoardItemListing*)packet;
        NextSafeRequest = DateTime.Now.AddSeconds(RateLimit);
        LogAssert(_numExpectedListings >= 0, $"AddPageDetour: received listings data without preceeding successful result packet");
        LogAssert(CurrentRequest != null, $"AddPageDetour: received listings data without preceeding history packet");
        LogAssert(data->SequenceIndex == self->CurrentRequestId, $"AddPageDetour: expected sequence {self->CurrentRequestId}, got {data->SequenceIndex}, shit's broken...");
        if (CurrentRequest == null)
            return;

        var entry = (MarketBoardItemListingEntry*)data->EntriesRaw;
        for (int i = 0; i < 10; ++i, ++entry)
        {
            if (entry->ItemId == 0)
                break; // no more entries

            if (!LogAssert(entry->ItemId == CurrentRequest.ItemId, $"AddPageDetour: unexpected listing for {entry->ItemId}, expected {CurrentRequest.ItemId}"))
            {
                // bail
                CurrentRequest = null;
                return;
            }

            CurrentRequest.Listings.Add(new(entry->ListingId, entry->Quantity, entry->UnitPrice, entry->TotalTax, entry->SellingPlayerContentId, entry->SellingRetainerContentId, entry->IsHQ != 0, entry->ContainerId, entry->TownId));
        }

        if (data->NextPageIndex == 0)
        {
            // complete
            RequestComplete?.Invoke(CurrentRequest);
            CurrentRequest = null;
            _numExpectedListings = -1;
        }
    }

    private static bool LogAssert(bool condition, string message)
    {
        if (!condition)
            Service.Log.Error($"Assertion failed: {message}");
        return condition;
    }
}
