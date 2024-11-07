namespace Market.Widget;

// list of current listings on marketboard for specific items
public sealed class MarketListings
{
    private record struct Listing(ulong Id, int Qty, int UnitPrice, int TotalTax, ulong SellerCID);

    private class Listings
    {
        public DateTime FetchTime;
        public readonly List<Listing> Entries = [];
    }

    private readonly Dictionary<(uint ItemId, uint WorldId), Listings> _cache = [];

    public void Draw(uint itemId, uint worldId)
    {
        _cache.TryGetValue((itemId, worldId), out var entry);
    }
}
