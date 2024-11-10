namespace Market;

public record struct MarketListing(ulong Id, uint Qty, uint UnitPrice, uint TotalTax, ulong SellerCID, ulong RetainerCID, bool IsHQ, ushort ContainerIndex, byte TownId);

public record class MarketListings(uint ItemId, uint WorldId)
{
    public DateTime FetchTime;
    public uint ExpectedCount;
    public readonly List<MarketListing> Listings = [];

    public bool Incomplete => FetchTime == default || Listings.Count < ExpectedCount;
}
