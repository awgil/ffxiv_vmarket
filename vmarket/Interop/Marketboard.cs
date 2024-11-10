using Dalamud.Hooking;
using Dalamud.Plugin.Services;
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

// how marketboard requests work:
// - normal flow
// -- client sends 'request item listings' ipc, specifies item id and some unknown search params; InfoProxyItemSearch sets 'waiting for data' to false
// -- server sends 'item listings summary' ipc, contains error message (0 normally) and num listings (max 100); note that there is no item id!
// --- if item count is > 0, proxy sets 'waiting for data' flag and increments sequence id, then client sends 'request item listings page' ipc (specifies page start 0 and sequence id, no item id!)
// --- server sends 'item listings page' ipc, contains 10 entries, page start index and next page start index (if more data is available) or 0 (if this is the last page)
// --- if more data is available, proxy saves page data in the fields (if 'waiting for data' flag is still set) and sends another 'request item listings page' ipc (with next-page start index and same sequence id)
// --- if no more data is available, the process ends (no more ipcs, 'waiting for data' flags remains set)
// --- if there are no items (item count == 0 in summary), none of the page requests happen: proxy list remains empty, 'waiting for data' flag remains unset
// --- if at any time sequence index of the 'item listings page' does not match what proxy expects, it sends 'request item listings page' ipc with next-page start index == -1 and sequence id from packet
// ---- this seems to be broken and will lead to the infinite request-response loop: server will respond with another page with same sequence id, which will again mismatch what proxy has, so proxy will repeat the same step
// -- at the same time server sends 'item history' ipc, contains item id and first 20 history entries
// --- similar loop driven by InfoProxyCategorySearch (TODO describe)
// - second request flow
// -- proxy doesn't have any rate limiting logic - on second request, it again sends 'request item listings' ipc and clears 'waiting for data' flag
// -- server responds with 'item listings summary' ipc with 0 items and non-zero error message
// -- proxy then clears the internal list and does nothing (does not touch sequence id or 'waiting for data' flag)
// -- this means that the response-request sequence for the previously requested item continues uninterrupted until the normal end - however, since 'waiting for data' flag is cleared, data is not added to the proxy lists
// -- if two 'request item listings' ipc are sent one after another, the error response for the subsequent one can arrive before the normal success response for the first request
// TODO: reverse meaning of request params

// low-level utility for fetching marketboard listings
// it uses existing code in InfoProxyItemSearch to drive the request-response sequence, hooks its functions and emits events when new data is available
// it does not provide any rate limiting itself, however it exposes fields that can be used to judge whether a request can succeed
// it also stores the received data internally, which is not affected by interruptions like proxy's built-in list is
public unsafe sealed class MarketboardListings : IDisposable
{
    public DateTime NextSafeRequest { get; private set; } // minimal time when we expect new request to succeed
    public MarketListings? CurrentRequest { get; private set; } // if non null, this is the request that is being filled with ongoing request-response sequence

    public bool IsRequestLikelyToSucceed => DateTime.Now >= NextSafeRequest;

    // event emitted whenever some data from server is received
    // first it is emitted for a given request with no listings, when 'summary' ipc is received; the fetch time is set only if request is successful
    // then it is emitted when more listings are received
    public event Action<MarketListings>? ReceivedData;

    private InfoProxyItemSearch* _proxy = (InfoProxyItemSearch*)InfoModule.Instance()->InfoProxies[(int)InfoProxyId.ItemSearch].Value;
    private Hook<InfoProxyItemSearch.Delegates.RequestData> _requestDataHook;
    private Hook<InfoProxyItemSearch.Delegates.ProcessRequestResult> _processRequestHook;
    private Hook<InfoProxyItemSearch.Delegates.AddPage> _addPageHook;
    private readonly List<MarketListings> _submittedRequests = [];
    private bool _waitingForServerResponse;

    private const float RateLimit = 1; // min delay between server requests
    private const float ResponseTimeout = 5; // if any server response does not arrive within this time, consider something is broken and it won't happen

    public MarketboardListings()
    {
        _requestDataHook = Service.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.RequestData>((nint)_proxy->VirtualTable->RequestData, RequestDataDetour);
        _requestDataHook.Enable();
        _processRequestHook = Service.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.ProcessRequestResult>(InfoProxyItemSearch.Addresses.ProcessRequestResult.Value, ProcessRequestResultDetour);
        _processRequestHook.Enable();
        _addPageHook = Service.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.AddPage>((nint)_proxy->VirtualTable->AddPage, AddPageDetour);
        _addPageHook.Enable();
    }

    public void Dispose()
    {
        _addPageHook.Dispose();
        _processRequestHook.Dispose();
        _requestDataHook.Dispose();
    }

    public MarketListings Request(uint itemId)
    {
        if (!ExecuteRequest(itemId))
            throw new Exception($"Failed to send listing request for {itemId}");
        return _submittedRequests[^1];
    }

    public bool ExecuteRequest(uint itemId)
    {
        _proxy->WaitingForListings = false;
        _proxy->SearchItemId = itemId;
        _proxy->Unk_0x24 = 2;
        _proxy->Unk_0x25 = 9;
        _proxy->Unk_0x28 = 0;
        return _proxy->RequestData();
    }

    private bool RequestDataDetour(InfoProxyItemSearch* self)
    {
        if (!_requestDataHook.Original(self))
            return false; // something went wrong, the request was not sent
        NextSafeRequest = DateTime.Now.AddSeconds(ResponseTimeout);
        _submittedRequests.Add(new(self->SearchItemId, Player.GetCurrentWorldId()));
        _waitingForServerResponse = true;
        return true;
    }

    private void ProcessRequestResultDetour(InfoProxyItemSearch* self, byte numItems, uint errorMessageId)
    {
        var oriNextSeq = self->NextRequestId;
        _processRequestHook.Original(self, numItems, errorMessageId);
        _waitingForServerResponse = self->NextRequestId != oriNextSeq;
        NextSafeRequest = DateTime.Now.AddSeconds(_waitingForServerResponse ? ResponseTimeout : RateLimit); // prevent sending next request immediately

        if (_waitingForServerResponse != (numItems > 0))
        {
            // this can happen if network module is null, but this probably means we're fucked anyway
            Service.Log.Error($"Assertion failure: ProcessRequestResultDetour got {numItems} items ({errorMessageId} error), proxy sequence change is {oriNextSeq}->{self->NextRequestId}");
        }

        if (_submittedRequests.Count == 0)
        {
            // this can theoretically happen if plugin is enabled between original request and this callback
            Service.Log.Error($"Assertion failure: unexpected ProcessRequestResultDetour, no requests found");
            return;
        }

        if (errorMessageId != 0 && !_waitingForServerResponse)
        {
            // we've got an error, and are not expecting any more server communications
            // try to guess which request this error corresponds to: use second if possible (since errors often arrive before summary for previous successful request)
            // note that this is a heuristic, and can fail in some cases, for example - we send request 2 just as request 1 completes (it will fail because of rate limiting), and then immediately send request 3
            // with unlucky timings, we may end up getting error for request 2 when the queue contains requests 2 & 3 (so we'll remove 2 instead), and then 3 will actually succeed (and we'll fill out data for 2 instead)
            // TODO: think of a better way to handle that
            var req = _submittedRequests[_submittedRequests.Count > 1 ? 1 : 0];
            if (req.FetchTime == default)
            {
                ReceivedData?.Invoke(req);
                _submittedRequests.Remove(req);
            }
            else
            {
                Service.Log.Error($"Assertion failure: selected request that is marked as in-progress to cancel on error ({req.ItemId}, {req.Listings.Count}/{req.ExpectedCount} listings)");
            }
            return;
        }

        // we've got a successful request
        if (CurrentRequest != null)
        {
            Service.Log.Error($"Assertion failure: new successful request while previous was not complete");
            // not sure what to do here, this shouldn't really happen
            _submittedRequests.Remove(CurrentRequest);
            CurrentRequest.FetchTime = default;
            CurrentRequest.ExpectedCount = 0;
            CurrentRequest.Listings.Clear();
            ReceivedData?.Invoke(CurrentRequest);
            CurrentRequest = null;
        }

        // assume we're getting data for oldest request
        CurrentRequest = _submittedRequests[0];
        CurrentRequest.FetchTime = DateTime.Now;
        CurrentRequest.ExpectedCount = numItems;
        ReceivedData?.Invoke(CurrentRequest);

        if (!_waitingForServerResponse)
        {
            // the request is finished immediately
            _submittedRequests.Remove(CurrentRequest);
            CurrentRequest = null;
        }
    }

    private void AddPageDetour(InfoProxyItemSearch* self, nint packet)
    {
        var data = (MarketBoardItemListing*)packet;
        _waitingForServerResponse = data->SequenceIndex == self->CurrentRequestId && data->NextPageIndex != 0;
        NextSafeRequest = DateTime.Now.AddSeconds(_waitingForServerResponse ? ResponseTimeout : RateLimit); // prevent sending next request immediately

        _addPageHook.Original(self, packet);

        if (data->SequenceIndex != self->CurrentRequestId)
        {
            Service.Log.Error($"Assertion failure: expected sequence {self->CurrentRequestId}, got {data->SequenceIndex}, shit's broken...");
        }

        if (CurrentRequest != null)
        {
            var entry = (MarketBoardItemListingEntry*)data->EntriesRaw;
            for (int i = 0; i < 10; ++i, ++entry)
            {
                if (entry->ItemId == 0)
                    break; // no more entries

                if (entry->ItemId != CurrentRequest.ItemId)
                {

                }

                CurrentRequest.Listings.Add(new(entry->ListingId, entry->Quantity, entry->UnitPrice, entry->TotalTax, entry->SellingPlayerContentId, entry->SellingRetainerContentId, entry->IsHQ != 0, entry->ContainerId, entry->TownId));
            }

            if (data->NextPageIndex == 0)
            {
                
            }
        }
        else
        {
            // this can theoretically happen if plugin is enabled mid request-response sequence
            Service.Log.Error($"Assertion failure: unexpected AddPageDetour, no current request");
        }

        if (_requestInProgress != null && !_waitingForServerResponse)
        {
            if (data->SequenceIndex != self->CurrentRequestId)
            {
                FailRequest($"Unexpected sequence id: expected {self->CurrentRequestId}, got {data->SequenceIndex}"); // no more data will be sent...
            }
            else
            {
                List<MarketListing> listings = [];
                var allItemsExpected = true;
                for (int i = 0; i < self->ListingCount; ++i)
                {
                    ref var l = ref self->Listings[i];
                    allItemsExpected &= l.ItemId == _requestInProgress.ItemId;
                    listings.Add(new(l.ListingId, l.Quantity, l.UnitPrice, l.TotalTax, l.SellingPlayerContentId, l.SellingRetainerContentId, l.IsHqItem, l.ContainerIndex, l.TownId));
                }

                if (allItemsExpected)
                    CompleteRequest(listings);
                else
                    FailRequest("Some of the listings have unexpected item id");
            }
        }
    }
}

public unsafe sealed class Marketboard : IDisposable
{
    private const int MaxRetries = 3; // max number of times a request can be repeated if response is an error before request is abandoned

    private record class RequestState(uint ItemId, CancellationToken Cancel)
    {
        public DateTime RequestTime;
        public uint RequestSequence = uint.MaxValue;
        public int RetriesLeft = MaxRetries;
        public TaskCompletionSource<MarketListings> Result = new();
    }

    private InfoProxyItemSearch* _proxy = (InfoProxyItemSearch*)InfoModule.Instance()->InfoProxies[(int)InfoProxyId.ItemSearch].Value;
    private Hook<InfoProxyItemSearch.Delegates.RequestData> _requestDataHook;
    private Hook<InfoProxyItemSearch.Delegates.ProcessRequestResult> _processRequestHook;
    private Hook<InfoProxyItemSearch.Delegates.AddPage> _addPageHook;
    private DateTime _requestRateLimit; // if current time is less than this value, we disallow sending new requests
    private readonly List<RequestState> _requests = [];
    private RequestState? _requestInProgress;

    public Marketboard()
    {
        Service.Framework.Update += Tick;
        _requestDataHook = Service.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.RequestData>((nint)_proxy->VirtualTable->RequestData, RequestDataDetour);
        _requestDataHook.Enable();
        _processRequestHook = Service.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.ProcessRequestResult>(InfoProxyItemSearch.Addresses.ProcessRequestResult.Value, ProcessRequestResultDetour);
        _processRequestHook.Enable();
        _addPageHook = Service.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.AddPage>((nint)_proxy->VirtualTable->AddPage, AddPageDetour);
        _addPageHook.Enable();
    }

    public void Dispose()
    {
        _addPageHook.Dispose();
        _processRequestHook.Dispose();
        _requestDataHook.Dispose();
        Service.Framework.Update -= Tick;
    }

    public Task<MarketListings> Request(uint itemId, CancellationToken cancel = default)
    {
        var request = new RequestState(itemId, cancel);
        _requests.Add(request);
        TryExecuteNextRequest(DateTime.Now); // no point waiting if we're idle
        return request.Result.Task;
    }

    public void Buy(uint itemId, in MarketListing listing)
    {
        _proxy->LastPurchasedMarketboardItem.SellingRetainerContentId = listing.RetainerCID;
        _proxy->LastPurchasedMarketboardItem.ListingId = listing.Id;
        _proxy->LastPurchasedMarketboardItem.ItemId = itemId;
        _proxy->LastPurchasedMarketboardItem.Quantity = listing.Qty;
        _proxy->LastPurchasedMarketboardItem.UnitPrice = listing.UnitPrice;
        _proxy->LastPurchasedMarketboardItem.TotalTax = listing.TotalTax;
        _proxy->LastPurchasedMarketboardItem.ContainerIndex = listing.ContainerIndex;
        _proxy->LastPurchasedMarketboardItem.IsHqItem = listing.IsHQ;
        _proxy->LastPurchasedMarketboardItem.TownId = listing.TownId;
        _proxy->SendPurchaseRequestPacket();
        // TODO: track response...
    }

    public bool ExecuteRequest(uint itemId)
    {
        _proxy->WaitingForListings = false;
        _proxy->SearchItemId = itemId;
        _proxy->Unk_0x24 = 2;
        _proxy->Unk_0x25 = 9;
        _proxy->Unk_0x28 = 0;
        return _proxy->RequestData();
    }

    private void TryExecuteNextRequest(DateTime now)
    {
        if (now > _requestRateLimit && _requests.Count > 0 && _requestInProgress == null)
        {
            var req = _requests[0];
            var success = ExecuteRequest(req.ItemId);
            _requestInProgress = req;
            if (!success)
                FailRequest("Failed to execute request");
        }
    }

    private void CompleteRequest(List<MarketListing> data)
    {
        if (_requestInProgress == null)
            return;
        var result = new MarketListings(_requestInProgress.RequestTime, data);
        _requestInProgress.Result.SetResult(result);
        _requests.Remove(_requestInProgress);
        // and complete other requests asking for same item
        for (int i = 0; i < _requests.Count; ++i)
        {
            if (_requests[i].ItemId == _requestInProgress.ItemId)
            {
                _requests[i].Result.SetResult(result);
                _requests.RemoveAt(i--);
            }
        }
        _requestInProgress = null;
    }

    private void FailRequest(string error)
    {
        if (_requestInProgress == null)
            return;
        _requestInProgress.Result.SetException(new Exception($"Request for item {_requestInProgress.ItemId} failed: {error} (seq {_requestInProgress.RequestSequence}, {_requestInProgress.RetriesLeft} retries left)"));
        _requests.Remove(_requestInProgress);
        _requestInProgress = null;
    }

    private void Tick(IFramework fwk)
    {
        var now = DateTime.Now;

        if (_waitingForServerResponse && now > _requestRateLimit)
        {
            // we're not getting response in time - suspect something is really bad, fail all outstanding requests
            foreach (var r in _requests)
                r.Result.SetException(new Exception("Timed out waiting for server response"));
            _requests.Clear();
            _requestInProgress = null;
            _waitingForServerResponse = false;
        }

        // react to cancellation of any outstanding tasks
        for (int i = 0; i < _requests.Count; ++i)
        {
            if (_requests[i].Cancel.IsCancellationRequested)
            {
                // task explicitly cancelled
                _requests[i].Result.SetCanceled();
                if (_requestInProgress == _requests[i])
                    _requestInProgress = null;
                _requests.RemoveAt(i--);
            }
        }

        // kick off new request if possible
        TryExecuteNextRequest(now);
    }

    private bool RequestDataDetour(InfoProxyItemSearch* self)
    {
        // TODO: consider checking rate-limit here, even for external requests?..
        if (_requestInProgress != null)
            return false; // prevent any external requests from running while we're trying to make requests, to avoid interference
        if (!_requestDataHook.Original(self))
            return false; // something went wrong, the request was not sent
        _waitingForServerResponse = true;
        _requestRateLimit = DateTime.Now.AddSeconds(ResponseTimeout); // prevent sending more requests immediately, wait for response first...
        return true;
    }

    private void ProcessRequestResultDetour(InfoProxyItemSearch* self, byte numItems, uint errorMessageId)
    {
        var oriNextSeq = self->NextRequestId;
        _processRequestHook.Original(self, numItems, errorMessageId);
        _waitingForServerResponse = self->NextRequestId != oriNextSeq;

        var now = DateTime.Now;
        _requestRateLimit = now.AddSeconds(_waitingForServerResponse ? ResponseTimeout : RateLimit); // prevent sending next request immediately
        if (_requestInProgress != null)
        {
            _requestInProgress.RequestTime = now;
            if (_waitingForServerResponse)
            {
                // ok, we're expecting data...
                _requestInProgress.RequestSequence = self->CurrentRequestId;
            }
            else if (errorMessageId == 0)
            {
                // no listings available, so no new messages are expected
                CompleteRequest([]);
            }
            else if (_requestInProgress.RetriesLeft-- <= 0)
            {
                // error happened, and no more retries available
                FailRequest($"Failed to retrieve results: {errorMessageId:X}");
            }
            else
            {
                // retry again after rate-limit timer expires
                _requestInProgress = null;
            }
        }
    }

    private void AddPageDetour(InfoProxyItemSearch* self, nint packet)
    {
        var data = (MarketBoardItemListing*)packet;
        _waitingForServerResponse = data->SequenceIndex == self->CurrentRequestId && data->NextPageIndex != 0;
        _requestRateLimit = DateTime.Now.AddSeconds(_waitingForServerResponse ? ResponseTimeout : RateLimit); // prevent sending next request immediately

        _addPageHook.Original(self, packet);
        if (_requestInProgress != null && !_waitingForServerResponse)
        {
            if (data->SequenceIndex != self->CurrentRequestId)
            {
                FailRequest($"Unexpected sequence id: expected {self->CurrentRequestId}, got {data->SequenceIndex}"); // no more data will be sent...
            }
            else
            {
                List<MarketListing> listings = [];
                var allItemsExpected = true;
                for (int i = 0; i < self->ListingCount; ++i)
                {
                    ref var l = ref self->Listings[i];
                    allItemsExpected &= l.ItemId == _requestInProgress.ItemId;
                    listings.Add(new(l.ListingId, l.Quantity, l.UnitPrice, l.TotalTax, l.SellingPlayerContentId, l.SellingRetainerContentId, l.IsHqItem, l.ContainerIndex, l.TownId));
                }

                if (allItemsExpected)
                    CompleteRequest(listings);
                else
                    FailRequest("Some of the listings have unexpected item id");
            }
        }
    }
}
