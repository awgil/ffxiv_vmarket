using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Market.Widget.Filters;
using System.Numerics;

namespace Market.Widget;

// searchable/filterable list of items, with recents and favourites
// TODO: save recents & favourites to external config
public sealed class ItemList
{
    private uint _selectedItem;
    public event Action<uint>? SelectedItemChanged;
    public uint SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem != value)
            {
                _selectedItem = value;
                SelectedItemChanged?.Invoke(value);
            }
        }
    }

    private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Item> _sheet;
    private readonly Dictionary<uint, string> _items;
    private readonly List<uint> _allItems = []; // sorted by name
    private readonly List<uint> _recentItems = [];
    private readonly List<uint> _favouriteItems = [];

    private List<uint> _curSourceList;
    private readonly List<uint> _curFilteredItems = [];
    private string _curSearchFilter = "";
    private readonly List<Filter> _filters;

    private bool extraFiltersExpanded = false;

    public ItemList()
    {
        // find all sellable items (== ones with non-zero ItemSearchCategory)
        _sheet = Service.LuminaSheet<Lumina.Excel.Sheets.Item>()!;
        _items = _sheet.Where(item => item.ItemSearchCategory.Value.RowId > 0).ToDictionary(i => i.RowId, i => i.Name.ToString());
        _allItems = [.. _items.Keys.OrderBy(id => _items[id])];

        _curSourceList = _allItems;
        ApplyFilter();

        _filters =
        [
            new NameFilter(),
            new LightSwitchFilter("Favourites", FontAwesomeIcon.Star, (i, e) => _favouriteItems.Contains(i.RowId)),
            new LightSwitchFilter("History", FontAwesomeIcon.History, (i, e) => _recentItems.Contains(i.RowId)),

            new UICategoryFilter(),
            new EquipLevelFilter(),
            new ItemLevelFilter(),
            new RarityFilter(),
            new EquipAsFilter(),
            new SexRaceFilter(),
            new CraftableFilter(),
            new DesynthableFilter(),
            new SoldByVendorFilter(),
            new BooleanFilter("Can Be HQ", "Has HQ", "No HQ", BooleanFilter.CheckFunc("CanBeHq")),
            new BooleanFilter("Unique", "Unique", "Not Unique", BooleanFilter.CheckFunc("IsUnique")),
        ];
    }

    public void Draw()
    {
        var filterDirty = false;
        if (_filters.Any(x => x.HasChanged))
        {
            var _filteredList = _filters.Where(filter => filter.IsSet).Aggregate(_allItems, (current, filter) => current.Where(x => filter.CheckFilter(_sheet.GetRow(x)!)).ToList());
            var active = _curSourceList == _filteredList;
            _curSourceList = active ? _allItems : _filteredList;
            filterDirty |= !active;
        }

        if (filterDirty)
            ApplyFilter();

        DrawFilters();

        ImGui.Separator();
        DrawFilteredItems();
    }

    private void DrawFilters()
    {
        using (var bar = ImRaii.Group())
        {
            foreach (var filter in _filters.Where(x => !x.ShowName))
            {
                filter.Draw();
                ImGui.SameLine();
            }
        }
        ImGui.Columns(2);
        var filterNameMax = _filters.Select(x => { x._nameWidth = ImGui.CalcTextSize(x.Name).X; return x._nameWidth; }).Max();
        ImGui.SetColumnWidth(0, filterNameMax + ImGui.GetStyle().ItemSpacing.X * 2);
        var filterInUseColour = new Vector4(0, 1, 0, 1);

        foreach (var filter in _filters.Where(x => x.ShowName))
        {
            if (!extraFiltersExpanded && filter.CanBeHidden && !filter.IsSet) continue;
            ImGui.SetCursorPosX((filterNameMax + ImGui.GetStyle().ItemSpacing.X) - filter._nameWidth);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3);
            if (filter.IsSet)
                ImGui.TextColored(filterInUseColour, $"{filter.Name}: ");
            else
                ImGui.TextUnformatted($"{filter.Name}: ");

            ImGui.NextColumn();
            using (var group = ImRaii.Group())
                filter.Draw();
            while (ImGui.GetColumnIndex() != 0)
                ImGui.NextColumn();
        }
        ImGui.Columns(1);

        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(0, -5 * ImGui.GetIO().FontGlobalScale)).Push(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.Button($"{(extraFiltersExpanded ? (char)FontAwesomeIcon.CaretUp : (char)FontAwesomeIcon.CaretDown)}", new Vector2(-1, 10 * ImGui.GetIO().FontGlobalScale)))
            extraFiltersExpanded = !extraFiltersExpanded;
    }

    private void DrawFilteredItems()
    {
        using var list = ImRaii.Child("list", default, false, ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysHorizontalScrollbar);
        if (!list)
            return;

        Action? postIteration = null;
        using var style = ImRaii.PushIndent(0.5f);
        foreach (var id in _curFilteredItems)
        {
            // TODO: print item link if double clicked
            if (ImGui.Selectable(_items[id], SelectedItem == id))
            {
                SelectedItem = id;
                if (_curSourceList != _recentItems)
                {
                    _recentItems.Remove(id);
                    _recentItems.Insert(0, id);
                }
            }

            ImGui.OpenPopupOnItemClick($"itemContextMenu{id}", ImGuiPopupFlags.MouseButtonRight);
            using var ctx = ImRaii.ContextPopupItem($"itemContextMenu{id}");
            if (ctx)
            {
                if (_favouriteItems.Contains(id))
                {
                    if (ImGui.MenuItem("Remove from favourites"))
                    {
                        _favouriteItems.Remove(id);
                        if (_curSourceList == _favouriteItems)
                            postIteration += ApplyFilter;
                    }
                }
                else
                {
                    if (ImGui.MenuItem("Add to favourites"))
                    {
                        _favouriteItems.Add(id);
                        if (_curSourceList == _favouriteItems)
                            postIteration += ApplyFilter;
                    }
                }
            }
        }
        postIteration?.Invoke();
    }

    private void ApplyFilter()
    {
        _curFilteredItems.Clear();
        var terms = _curSearchFilter.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool match(string name) => terms.All(name.Contains);
        _curFilteredItems.AddRange(_curSourceList.Where(id => match(_items[id].ToLowerInvariant())));
    }
}
