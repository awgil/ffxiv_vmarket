using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace Market.Widget.Filters;
internal class EquipLevelFilter : Filter
{
    public override string Name => "Equip Level";
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

    private const int MinLevel = 1;
    private const int MaxLevel = 100;

    private int minLevel;
    private int maxLevel;

    private int lastMinLevel;
    private int lastMaxLevel;

    public EquipLevelFilter()
    {
        minLevel = lastMinLevel = MinLevel;
        maxLevel = lastMaxLevel = MaxLevel;
    }

    public override bool CheckFilter(Item item) => item.LevelEquip >= minLevel && item.LevelEquip <= maxLevel;

    public override void Draw()
    {
        using var c = ImRaii.Child($"{Name}Child", new Vector2(-1, 23 * ImGui.GetIO().FontGlobalScale), false, ImGuiWindowFlags.None);
        if (c)
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragIntRange2("##LevelEquipSearchFilterRange", ref minLevel, ref maxLevel, 1f, MinLevel, MaxLevel))
            {
                if (minLevel > maxLevel && minLevel != lastMinLevel) minLevel = maxLevel;
                if (maxLevel < minLevel && maxLevel != lastMaxLevel) maxLevel = minLevel;
                if (minLevel < MinLevel) minLevel = MinLevel;
                if (maxLevel > MaxLevel) maxLevel = MaxLevel;
            }
        }
    }
}
