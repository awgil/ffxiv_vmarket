using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace Market.Interop;

// utility for purchasing items from marketboard
// TODO: BuyAsync
public sealed class MBPurchase : IDisposable
{
    public event Action<uint, ulong>? PurchaseComplete; // event emitted when purchase completes successfully

    private unsafe InfoProxyItemSearch* _proxy = (InfoProxyItemSearch*)InfoModule.Instance()->InfoProxies[(int)InfoProxyId.ItemSearch].Value;
    private Hook<InfoProxyItemSearch.Delegates.ProcessPurchaseResponse> _processPurchaseHook;

    public unsafe MBPurchase()
    {
        _processPurchaseHook = Service.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.ProcessPurchaseResponse>(InfoProxyItemSearch.Addresses.ProcessPurchaseResponse.Value, ProcessPurchaseDetour);
        _processPurchaseHook.Enable();
    }

    public void Dispose()
    {
        _processPurchaseHook.Dispose();
    }

    public unsafe bool ExecuteBuy(uint itemId, in MarketListing listing)
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
        return _proxy->SendPurchaseRequestPacket();
    }

    private unsafe void ProcessPurchaseDetour(InfoProxyItemSearch* self, uint itemId, uint errorMessageId)
    {
        if (errorMessageId == 0 && itemId == self->LastPurchasedMarketboardItem.ItemId)
        {
            PurchaseComplete?.Invoke(itemId, self->LastPurchasedMarketboardItem.ListingId);
        }
        _processPurchaseHook.Original(self, itemId, errorMessageId);
    }
}
