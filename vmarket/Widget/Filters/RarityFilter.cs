using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace Market.Widget.Filters;
internal class RarityFilter : Filter
{
    public override string Name => "Rarity";
    public override bool IsSet => selectedValue > 0;

    private uint selectedValue = 0;

    public Dictionary<uint, Vector4> rarityColorMap = new() {
        {1, new Vector4(1f, 1f, 1f, 1)},
        {2, new Vector4(0.6F, 0.76F, 0.56F, 1)},
        {3, new Vector4(0.36F, 0.52F, 0.82F, 1)},
        {4, new Vector4(0.56F, 0.47F, 0.75F, 1)},
        {7, new Vector4(0.83F, 0.51F, 0.64F, 1)},
    };

    public override bool CheckFilter(Item item) => item.Rarity == selectedValue;

    public override void Draw()
    {
        ImGui.SetNextItemWidth(100 * ImGui.GetIO().FontGlobalScale);

        var setBg = false;
        if (selectedValue != 0 && rarityColorMap.ContainsKey(selectedValue))
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, rarityColorMap[selectedValue]);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, rarityColorMap[selectedValue]);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, rarityColorMap[selectedValue]);
            setBg = true;
        }

        if (ImGui.BeginCombo("###raritySelect", selectedValue == 0 ? "Any" : "", ImGuiComboFlags.HeightLargest))
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

            ImGui.BeginChild($"###colorBoxNone", new Vector2(-1, 20 * ImGui.GetIO().FontGlobalScale), false);
            if (ImGui.Selectable($"Any###optionNone", selectedValue == 0, ImGuiSelectableFlags.None, new Vector2(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X, 20 * ImGui.GetIO().FontGlobalScale)))
            {
                selectedValue = 0;
                Modified = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndChild();

            foreach (var v in rarityColorMap)
            {

                var x = ImGui.GetCursorPos();
                if (ImGui.Selectable($"###optionRarity{v.Key}", selectedValue == v.Key, ImGuiSelectableFlags.None, new Vector2(100, 20) * ImGui.GetIO().FontGlobalScale))
                {
                    selectedValue = v.Key;
                    Modified = true;
                }

                var h = ImGui.IsItemHovered();

                ImGui.SameLine(0);
                ImGui.SetCursorPos(x);
                if (h)
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, v.Value - new Vector4(0, 0, 0, 0.2f));
                else
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, v.Value);

                ImGui.BeginChild($"###colorBox{v.Key}", new Vector2(100, 20) * ImGui.GetIO().FontGlobalScale, false, ImGuiWindowFlags.NoMouseInputs);

                ImGui.EndChild();
                ImGui.PopStyleColor();
            }

            ImGui.PopStyleVar();
            ImGui.EndCombo();
        }

        if (setBg)
            ImGui.PopStyleColor(3);
    }
}
