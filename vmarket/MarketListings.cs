namespace Market;

public record struct MarketListing(ulong Id, uint Qty, uint UnitPrice, uint TotalTax, ulong SellerCID, ulong RetainerCID, bool IsHQ, ushort ContainerIndex, byte TownId);
public record class MarketListings(DateTime FetchTime, List<MarketListing> Listings);
