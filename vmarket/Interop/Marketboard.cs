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
// - client sends 'request item' ipc, gets back MarketBoardItemRequestStart server ipc that contains num items (max 100)
// - client then sends 'request page' ipc (with starting index = 0), gets back MarketBoardOfferings server ipc that contains next page (10 listings) and starting index for next page (0 if it's the last page)
// - InfoProxyItemSearch handles this request-response sequence, 
// - if at any point server ipc 'request sequence id' does not match what infoproxy expects, it abandons the sequence and sends 'request page' ipc with starting index == -1
// TODO: reverse meaning of request params
public unsafe sealed class Marketboard : IDisposable
{
    private const float RateLimit = 1; // min delay between server requests
    private const float ResponseTimeout = 5; // if any server response does not arrive within this time, consider something is broken and it won't happen
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
    private bool _waitingForServerResponse;

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

    private bool ExecuteRequest(uint itemId)
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
