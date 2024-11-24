using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;
using Dalamud.Interface.Utility;

namespace Market.Widget;

// list of current listings on marketboard for specific items
#pragma warning disable SeStringRenderer
public sealed class ItemListings : IDisposable
{
    private Interop.MBFetch _mbFetch;
    private Interop.MBPurchase _mbPurchase;
    private readonly Dictionary<(uint ItemId, uint WorldId), MarketListings> _cache = [];
    private Task? _curRequest;
    private ulong _selectedListing;

    public ItemListings(Interop.MBFetch mbFetch, Interop.MBPurchase mbPurchase)
    {
        _mbFetch = mbFetch;
        _mbPurchase = mbPurchase;
        _mbFetch.RequestComplete += UpdateCache;
        _mbPurchase.PurchaseComplete += RemoveListing;
    }

    public void Dispose()
    {
        _mbFetch.RequestComplete -= UpdateCache;
        _mbPurchase.PurchaseComplete -= RemoveListing;
    }

    public void Draw(uint itemId, uint worldId, ulong playerCID)
    {
        var item = Service.LuminaRow<Item>(itemId);
        if (item == null)
            return;

        ImGui.Image(Service.TextureProvider.GetFromGameIcon((uint)item.Value.Icon).GetWrapOrEmpty().ImGuiHandle, new(40));
        ImGui.SameLine();
        using (var _ = ImRaii.Group())
        {
            // TODO: bigger font
            ImGui.TextUnformatted($"{item.Value.Name} @ {Service.LuminaRow<World>(worldId)?.Name}");
            ImGuiHelpers.SeStringWrapped(item.Value.Description);
        }
        if (ImGui.IsItemClicked())
        {
            var payloadList = new List<Payload> {
                new UIForegroundPayload((ushort) (0x223 + item.Value.Rarity * 2)),
                new UIGlowPayload((ushort) (0x224 + item.Value.Rarity * 2)),
                new ItemPayload(item.Value.RowId, item.Value.CanBeHq && Service.KeyState[0x11]),
                new UIForegroundPayload(500),
                new UIGlowPayload(501),
                new TextPayload($"{(char) SeIconChar.LinkMarker}"),
                new UIForegroundPayload(0),
                new UIGlowPayload(0),
                new TextPayload(item.Value.Name.ExtractText() + (item.Value.CanBeHq && Service.KeyState[0x11] ? $" {(char)SeIconChar.HighQuality}" : "")),
                new RawPayload([0x02, 0x27, 0x07, 0xCF, 0x01, 0x01, 0x01, 0xFF, 0x01, 0x03]),
                new RawPayload([0x02, 0x13, 0x02, 0xEC, 0x03])
            };
            Service.Chat.Print(new XivChatEntry() { Message = new SeString(payloadList) });
        }

        var entry = _mbFetch.CurrentRequest != null && _mbFetch.CurrentRequest.ItemId == itemId && _mbFetch.CurrentRequest.WorldId == worldId ? _mbFetch.CurrentRequest : _cache.GetValueOrDefault((itemId, worldId));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(entry != null ? $"Data retrieved {DateTime.Now - entry.FetchTime} ago" : "No data available");
        ImGui.SameLine();
        using (ImRaii.Disabled(_curRequest != null && !_curRequest.IsCompleted))
        {
            if (ImGui.Button("Refresh"))
            {
                _curRequest = _mbFetch.RequestAsync(itemId);
            }
        }

        if (entry != null)
        {
            var listingsHeight = ImGui.GetContentRegionAvail().Y; // TODO: space for history
            using (var listings = ImRaii.Table("listings", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new(0, listingsHeight)))
            {
                if (listings)
                {
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 20);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("Unit Price", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Total Price", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Actions");
                    ImGui.TableHeadersRow();
                    foreach (var l in entry.Listings)
                    {
                        ImGui.TableNextColumn();
                        var hqText = l.IsHQ ? $"{(char)SeIconChar.HighQuality}" : "";
                        if (ImGui.Selectable($"{hqText}###listing{l.Id:X}", _selectedListing == l.Id, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap))
                            _selectedListing = l.Id;
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(l.Qty.ToString());
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(l.UnitPrice.ToString("N0"));
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted((l.UnitPrice * l.Qty + l.TotalTax).ToString("N0"));
                        ImGui.TableNextColumn();
                        if (l.SellerCID == playerCID)
                        {
                            // TODO: delist, adjust price buttons
                            // TODO: custom background color for my listings
                        }
                        else
                        {
                            if (ImGui.SmallButton("Buy"))
                            {
                                // TODO: track completion...
                                _mbPurchase.ExecuteBuy(itemId, l);
                            }
                        }
                    }
                }
            }
        }
    }

    private void UpdateCache(MarketListings data) => _cache[(data.ItemId, data.WorldId)] = data;
    private void RemoveListing(uint itemId, ulong listingId) => _cache.GetValueOrDefault((itemId, Interop.Player.GetCurrentWorldId()))?.Listings.RemoveAll(l => l.Id == listingId);
}
