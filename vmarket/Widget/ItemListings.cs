using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets2;
using System.Threading.Tasks;

namespace Market.Widget;

// list of current listings on marketboard for specific items
public sealed class ItemListings
{
    private readonly Dictionary<(uint ItemId, uint WorldId), MarketListings> _cache = [];
    private Task? _curRequest;
    private ulong _selectedListing;

    public void Draw(uint itemId, uint worldId, Interop.Marketboard mb, ulong playerCID)
    {
        var item = Service.LuminaRow<Item>(itemId);
        if (item == null)
            return;

        ImGui.Image(Service.TextureProvider.GetFromGameIcon((uint)item.Icon).GetWrapOrEmpty().ImGuiHandle, new(40));
        ImGui.SameLine();
        // TODO: bigger font
        ImGui.TextUnformatted($"{item.Name} @ {Service.LuminaRow<World>(worldId)?.Name}");

        var entry = _cache.GetValueOrDefault((itemId, worldId));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(entry != null ? $"Data retrieved {DateTime.Now - entry.FetchTime} ago" : "No data available");
        ImGui.SameLine();
        using (ImRaii.Disabled(_curRequest != null && !_curRequest.IsCompleted))
        {
            if (ImGui.Button("Refresh"))
            {
                _curRequest = Service.Framework.Run(async () =>
                {
                    try
                    {
                        _cache[(itemId, worldId)] = await mb.Request(itemId);
                    }
                    catch (Exception ex)
                    {
                        Service.Log.Error($"Request failed: {ex}");
                    }
                });
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
                                // TODO: this is an async op that should remove listing on success
                                mb.Buy(itemId, l);
                            }
                        }
                    }
                }
            }
        }
    }
}
