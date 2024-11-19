using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace Market.Widget.Filters;
internal class ItemLevelFilter : Filter
{
    public override string Name => "Item Level";
    public override bool IsSet => minLevel != MinLevel || maxLevel != MaxLevel;

    public override bool HasChanged
    {
        get
        {
            if (Modified || minLevel != lastMinLevel || maxLevel != lastMaxLevel)
            {
                lastMaxLevel = maxLevel;
                lastMinLevel = minLevel;
                Modified = false;
                return true;
            }

            return false;
        }
    }

    private readonly int MinLevel = 1;
    private int MaxLevel = 1000;

    private int minLevel;
    private int maxLevel;

    private int lastMinLevel;
    private int lastMaxLevel;

    public ItemLevelFilter()
    {
        minLevel = lastMinLevel = MinLevel;
        MaxLevel = (int)Service.LuminaSheet<Item>()!.OrderByDescending(x => x.LevelItem.RowId).First().LevelItem.RowId;
        maxLevel = lastMaxLevel = MaxLevel;
    }

    public override bool CheckFilter(Item item)
    {
        if (item.LevelItem.RowId > MaxLevel)
        {
            if (maxLevel == MaxLevel) maxLevel = (int)item.LevelItem.RowId;
            MaxLevel = (int)item.LevelItem.RowId;
        }
        return item.LevelItem.RowId >= (minLevel) && item.LevelItem.RowId <= (maxLevel);
    }

    public override void Draw()
    {
        using var c = ImRaii.Child($"{Name}Child", new Vector2(-1, 23 * ImGui.GetIO().FontGlobalScale), false, ImGuiWindowFlags.None);
        if (c)
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragIntRange2("##LevelItemSearchFilterRange", ref minLevel, ref maxLevel, 1f, MinLevel, MaxLevel))
            {
                if (minLevel > maxLevel && minLevel != lastMinLevel) minLevel = maxLevel;
                if (maxLevel < minLevel && maxLevel != lastMaxLevel) maxLevel = minLevel;
                if (minLevel < MinLevel) minLevel = MinLevel;
                if (maxLevel > MaxLevel) maxLevel = MaxLevel;
            }
        }
    }
}
