using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Data;
using Lumina.Excel.Sheets;
using System.ComponentModel;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Market.Widget.Filters;
#nullable disable
internal class SoldByVendorFilter : Filter
{
    public override string Name => "Sold by NPC";
    public override bool IsSet => selectedCurrencyOption != null;

    private readonly HashSet<uint> soldForAnything = [];
    private readonly Dictionary<uint, HashSet<uint>> soldForCurrency = [];

    private enum SpecialCurrency : uint
    {
        [Description("Grand Company Seal")]
        GrandCompany = uint.MaxValue - 10,
        [Description("Beast Tribe Currency")]
        BeastTribe,
    }

    private class CurrencyOption
    {
        public bool Invert;
        public HashSet<uint> ItemHashSet = [];
        public string Name = string.Empty;
        public List<CurrencyOption> SubOptions = [];
        public bool HideIfEmpty = true;
    }

    private uint[] beastTribeCurrencies;

    private bool ready;
    private bool error;

    private readonly CurrencyOption notSoldByNpcOption;
    private readonly CurrencyOption soldByAnyNpcOption;
    private readonly List<CurrencyOption> availableOptions = [];
    private CurrencyOption selectedCurrencyOption;
    private CurrencyOption selectedSubOption;


    public SoldByVendorFilter()
    {
        notSoldByNpcOption = new CurrencyOption { Invert = true, Name = "Not sold by NPC", ItemHashSet = soldForAnything, HideIfEmpty = false };
        soldByAnyNpcOption = new CurrencyOption { Name = "Any Currency", ItemHashSet = soldForAnything, HideIfEmpty = false };


        availableOptions.Add(null); // Not Selected Option
        availableOptions.Add(notSoldByNpcOption);
        availableOptions.Add(soldByAnyNpcOption);

        availableOptions.Add(GetCurrencyOption(1, "Gil"));
        availableOptions.Add(GetCurrencyOption(29, "MGP"));

        soldForCurrency.Add((uint)SpecialCurrency.GrandCompany, []); // Grand Company Seal
        availableOptions.Add(new CurrencyOption
        {
            Name = "Grand Company Seals",
            ItemHashSet = soldForCurrency[(uint)SpecialCurrency.GrandCompany],
            SubOptions = GetGrandCompanyCurrencies()
        });

        soldForCurrency.Add((uint)SpecialCurrency.BeastTribe, []); // Beast Tribe
        availableOptions.Add(new CurrencyOption
        {
            Name = "Beast Tribe Currencies",
            ItemHashSet = soldForCurrency[(uint)SpecialCurrency.BeastTribe],
            SubOptions = GetBeastTribeCurrencies()
        });

        availableOptions.Add(GetCurrencyOption(28, "Poetics"));
        availableOptions.Add(GetCurrencyOption(40, "Allegory"));
        availableOptions.Add(GetCurrencyOption(41, "Revelation"));

        Task.Run(() =>
        {
            try
            {
                foreach (var gilShopItem in Service.LuminaSubsheet<GilShopItem>()!.SelectMany(m => m))
                {
                    if (!soldForAnything.Contains(gilShopItem.Item.RowId)) soldForAnything.Add(gilShopItem.Item.RowId);
                    if (!soldForCurrency[1].Contains(gilShopItem.Item.RowId)) soldForCurrency[1].Add(gilShopItem.Item.RowId);
                }

                foreach (var gcScripShopItem in Service.LuminaSubsheet<GCScripShopItem>()!.SelectMany(m => m))
                {
                    if (!soldForAnything.Contains(gcScripShopItem.Item.RowId)) soldForAnything.Add(gcScripShopItem.Item.RowId);
                    if (!soldForCurrency[(uint)SpecialCurrency.GrandCompany].Contains(gcScripShopItem.Item.RowId)) soldForCurrency[(uint)SpecialCurrency.GrandCompany].Add(gcScripShopItem.Item.RowId);

                    if (Service.LuminaSheet<GCScripShopCategory>().TryGetRow(gcScripShopItem.RowId, out var gcssc))
                    {
                        var grandCompanyID = gcssc.GrandCompany.RowId;
                        if (grandCompanyID < 1 || grandCompanyID > 3) continue;
                        if (!soldForCurrency[19 + grandCompanyID].Contains(gcScripShopItem.Item.RowId)) soldForCurrency[19 + grandCompanyID].Add(gcScripShopItem.Item.RowId);
                    }
                }

                //foreach (var specialShop in Service.LuminaSheet<SpecialShopCustom>()!)
                //{
                //    foreach (var entry in specialShop.Entries)
                //    {
                //        foreach (var c in entry.Cost)
                //        {
                //            if (!soldForCurrency.ContainsKey(c.Item.RowId)) continue;
                //            foreach (var r in entry.Result)
                //            {
                //                if (!soldForAnything.Contains(r.Item.RowId)) soldForAnything.Add(r.Item.RowId);
                //                if (beastTribeCurrencies.Contains(c.Item.RowId))
                //                {
                //                    if (!soldForCurrency[(uint)SpecialCurrency.BeastTribe].Contains(r.Item.RowId)) soldForCurrency[(uint)SpecialCurrency.BeastTribe].Add(r.Item.RowId);
                //                }
                //                if (!soldForCurrency[c.Item.RowId].Contains(r.Item.RowId)) soldForCurrency[c.Item.RowId].Add(r.Item.RowId);
                //            }
                //        }
                //    }
                //}

                availableOptions.RemoveAll(o =>
                {
                    if (o == null) return false;
                    if (!o.HideIfEmpty) return false;
                    return o.ItemHashSet.Count == 0;
                });

                availableOptions.ForEach(o =>
                {
                    if (o == null) return;
                    o.SubOptions.RemoveAll(so =>
                    {
                        if (so == null) return false;
                        if (!so.HideIfEmpty) return false;
                        return so.ItemHashSet.Count == 0;
                    });
                });

                ready = true;

            }
            catch (Exception ex)
            {
                error = true;
                Service.Log.Error($"{ex}");
            }
        });
    }

    private List<CurrencyOption> GetBeastTribeCurrencies()
    {
        var l = new List<CurrencyOption>() { null };
        var a = new List<uint>();
        var btSheet = Service.LuminaSheet<BeastTribe>()!;
        foreach (var bt in btSheet)
        {
            if (bt.CurrencyItem.RowId == 0) continue;
            var co = GetCurrencyOption(bt.CurrencyItem.RowId);
            if (co == null) continue;
            string name = bt.Name.ExtractText();
            //if (btSheet.RequestedLanguage == Language.English)
            //    name = $"{name[..1].ToUpper()}{name[1..]}";

            co.Name = $"{name} / {co.Name}";

            a.Add(bt.CurrencyItem.RowId);
            l.Add(co);
        }

        beastTribeCurrencies = [.. a];
        return l;
    }

    private List<CurrencyOption> GetGrandCompanyCurrencies()
    {
        var l = new List<CurrencyOption>() { null };
        foreach (var gc in Service.LuminaSheet<GrandCompany>()!)
        {
            if (gc.RowId == 0) continue;
            var co = GetCurrencyOption(19 + gc.RowId, gc.Name.ExtractText());
            if (co == null) continue;
            l.Add(co);
        }
        return l;
    }

    private CurrencyOption GetCurrencyOption(uint itemId, string forceName = null)
    {
        try
        {
            if (!soldForCurrency.ContainsKey(itemId))
                soldForCurrency.Add(itemId, []);
            var sheet = Service.LuminaSheet<Item>()!;
            if (sheet.TryGetRow(itemId, out var item))
                return new CurrencyOption() { Name = forceName ?? itemId.ToString(), ItemHashSet = soldForCurrency[itemId] };
            return new CurrencyOption() { Name = forceName ?? item.Name.ExtractText(), ItemHashSet = soldForCurrency[itemId] };
        }
        catch (Exception ex)
        {
            Service.Log.Error($"Failed to get Currency Option for {itemId}\n{ex}");
            return null;
        }

    }

    public override bool CheckFilter(Item item)
    {
        while (!ready && !error) Thread.Sleep(1);
        if (error) return true;
        var option = selectedCurrencyOption;

        if (selectedSubOption != null)
            option = selectedSubOption;

        if (option == null) return true;
        if (option.Invert)
            return !option.ItemHashSet.Contains(item.RowId);
        else
            return option.ItemHashSet.Contains(item.RowId);
    }

    public override void Draw()
    {
        if (error)
        {
            ImGui.TextUnformatted("Error");
            return;
        }
        if (!ready)
        {
            ImGui.TextUnformatted("Loading...");
            return;
        }
        using var c = ImRaii.Child($"{Name}Child", new Vector2(-1, 23 * ImGui.GetIO().FontGlobalScale), false, ImGuiWindowFlags.None);
        if (c)
        {
            if (selectedCurrencyOption != null && selectedCurrencyOption.SubOptions.Count > 0)
                ImGui.SetNextItemWidth(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X / 2);
            else
                ImGui.SetNextItemWidth(-1);

            using var selection = ImRaii.Combo("###SoldbyNPCSearchFilter_selection", selectedCurrencyOption?.Name ?? string.Empty);
            if (selection)
            {
                foreach (var option in availableOptions)
                {
                    if (ImGui.Selectable(option?.Name ?? "Not Selected", selectedCurrencyOption == option))
                    {
                        selectedCurrencyOption = option;
                        selectedSubOption = null;
                        Modified = true;
                    }
                }
            }

            if (selectedCurrencyOption != null && selectedCurrencyOption.SubOptions.Count > 0)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("###SoldbyNPCSearchFilter_subselection", selectedSubOption?.Name ?? "Any"))
                {
                    foreach (var option in selectedCurrencyOption.SubOptions)
                    {
                        if (ImGui.Selectable(option?.Name ?? "Any", selectedCurrencyOption == option))
                        {
                            selectedSubOption = option;
                            Modified = true;
                        }
                    }
                    ImGui.EndCombo();
                }
            }
        }
    }
}
