using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace Market.Widget.Filters;
internal class UICategoryFilter : Filter
{
    public override string Name => "Category";
    public override bool IsSet => selectedCategory != 0;

    public override bool HasChanged
    {
        get
        {
            if (lastCategory != selectedCategory)
            {
                lastCategory = selectedCategory;
                return true;
            }

            return false;
        }
    }

    public override bool CheckFilter(Item item) => item.ItemUICategory.RowId == uiCategories[selectedCategory].RowId;

    private readonly List<ItemUICategory> uiCategories = [];
    private readonly string[] uiCategoriesArray;

    private int selectedCategory;
    private int lastCategory;
    private string categorySearchInput = string.Empty;
    private bool focused;
    private readonly Vector2 popupSize = new(-1, 120);

    public UICategoryFilter()
    {
        uiCategories.AddRange(Service.LuminaSheet<ItemUICategory>()!.ToList().Where(x => !string.IsNullOrEmpty(x.Name.ExtractText())).OrderBy(x => x.Name.ToString()));
        uiCategoriesArray = ["All", .. uiCategories.Select(x => x.Name.ToString().Replace("\u0002\u001F\u0001\u0003", "-")).ToArray()];
    }

    public override void Draw()
    {
        ImGui.SetNextItemWidth(-1);
        using var combo = ImRaii.Combo("##ItemUiCategorySearchFilterBox", uiCategoriesArray[selectedCategory]);
        if (combo)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("###ItemUiCategorySearchFilterFilter", "Filter", ref categorySearchInput, 60);
            var isFocused = ImGui.IsItemActive();
            if (!focused)
                ImGui.SetKeyboardFocusHere();

            using var child = ImRaii.Child("###ItemUiCategorySearchFilterDisplay", popupSize, true);
            if (child)
            {
                if (!focused)
                {
                    ImGui.SetScrollY(0);
                    focused = true;
                }

                var c = 0;
                var l = 0;
                for (var i = 0; i < uiCategoriesArray.Length; i++)
                {
                    if (i > 0 && categorySearchInput.Length > 0 && !uiCategoriesArray[i].Contains(categorySearchInput, StringComparison.InvariantCultureIgnoreCase)) continue;
                    if (i != 0)
                    {
                        c++;
                        l = i;
                    }
                    if (!ImGui.Selectable(uiCategoriesArray[i], selectedCategory == i)) continue;
                    selectedCategory = i;

                    ImGui.CloseCurrentPopup();
                }
                if (!isFocused && c <= 1)
                {
                    selectedCategory = l;
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        else if (focused)
        {
            focused = false;
            categorySearchInput = string.Empty;
        }
    }
}
