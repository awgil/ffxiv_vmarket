using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;

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

    private readonly Dictionary<uint, string> _items;
    private readonly List<uint> _allItems = []; // sorted by name
    private readonly List<uint> _recentItems = [];
    private readonly List<uint> _favouriteItems = [];

    private List<uint> _curSourceList;
    private readonly List<uint> _curFilteredItems = [];
    private string _curSearchFilter = "";

    public ItemList()
    {
        // find all sellable items (== ones with non-zero ItemSearchCategory)
        _items = Service.LuminaSheet<Lumina.Excel.GeneratedSheets.Item>()!.Where(item => item.ItemSearchCategory.Row > 0).ToDictionary(i => i.RowId, i => i.Name.ToString());
        _allItems = [.. _items.Keys.OrderBy(id => _items[id])];

        _curSourceList = _allItems;
        ApplyFilter();
    }

    public void Draw()
    {
        var spaceForButton = ImGui.GetStyle().ItemSpacing.X + 32 * ImGui.GetIO().FontGlobalScale;
        ImGui.SetNextItemWidth(-2 * spaceForButton);
        var filterDirty = ImGui.InputTextWithHint("###filter", "Search...", ref _curSearchFilter, 256);
        ImGui.SameLine();
        filterDirty |= DrawSourceSelectorButton(FontAwesomeIcon.History, _recentItems);
        ImGui.SameLine();
        filterDirty |= DrawSourceSelectorButton(FontAwesomeIcon.Star, _favouriteItems);

        if (filterDirty)
            ApplyFilter();

        ImGui.Separator();
        DrawFilteredItems();
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

    private bool DrawSourceSelectorButton(FontAwesomeIcon icon, List<uint> source)
    {
        var active = _curSourceList == source;
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        using var c1 = ImRaii.PushColor(ImGuiCol.Button, 0xFF5CB85C, active);
        using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, 0x885CB85C, active);
        if (!ImGui.Button(icon.ToIconString(), new(32 * ImGui.GetIO().FontGlobalScale, ImGui.GetItemRectSize().Y)))
            return false;
        _curSourceList = active ? _allItems : source;
        return true;
    }

    private void ApplyFilter()
    {
        _curFilteredItems.Clear();
        var terms = _curSearchFilter.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool match(string name) => terms.All(name.Contains);
        _curFilteredItems.AddRange(_curSourceList.Where(id => match(_items[id].ToLowerInvariant())));
    }
}
