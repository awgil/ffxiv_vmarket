using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Text.RegularExpressions;

namespace Market.Widget.Filters;
internal class NameFilter : Filter
{
    public override string Name => "Search";
    public override bool ShowName => false;
    public override bool IsSet => !string.IsNullOrEmpty(searchText);
    public override bool CanBeHidden => false;

    private string searchText;
    private string lastSearchText;
    private Regex? searchRegex;
    private string[] searchTokens = [];
    private string parsedSearchText = string.Empty;

    public NameFilter()
    {
        searchText = string.Empty;
        lastSearchText = string.Empty;
    }

    public override bool HasChanged
    {
        get
        {
            if (searchText != lastSearchText)
            {
                ParseInputText();
                lastSearchText = searchText;
                return true;
            }

            return false;
        }
    }

    public override bool CheckFilter(Item item)
    {
        if (searchRegex != null)
            return searchRegex.IsMatch(item.Name.ExtractText());

        return
            item.Name.ToString().Contains(parsedSearchText, StringComparison.InvariantCultureIgnoreCase)
            || (searchTokens != null && searchTokens.Length > 0 && searchTokens.All(t => item.Name.ToString().Contains(t, StringComparison.InvariantCultureIgnoreCase)))
            || (int.TryParse(parsedSearchText, out var parsedId) && parsedId == item.RowId)
            || (searchText.StartsWith('$') && item.Description.ToString().Contains(parsedSearchText[1..], StringComparison.InvariantCultureIgnoreCase));
    }

    public override void Draw()
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            using var tooltip = ImRaii.Tooltip();
            if (tooltip)
            {
                ImGui.TextUnformatted("Type an item name to search for items by name.");
                ImGui.SameLine();
                ImGui.TextDisabled("\"OMG\"");
                ImGui.TextUnformatted("Type an item id to search for item by its ID.");
                ImGui.SameLine();
                ImGui.TextDisabled("\"23991\"");
                ImGui.TextUnformatted("Start input with '$' to search for an item by its description.");
                ImGui.SameLine();
                ImGui.TextDisabled("\"$Weird.\"");
                ImGui.TextUnformatted("Start and end with '/' to search using regex.");
                ImGui.SameLine();
                ImGui.TextDisabled("\"/^.M.$/\"");
            }
        }
        ImGui.SameLine();
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        var spaceForButton = ImGui.GetStyle().ItemSpacing.X + 32 * ImGui.GetIO().FontGlobalScale;
        ImGui.SetNextItemWidth(-2 * spaceForButton);
        ImGui.InputText("##ItemNameSearchFilter", ref searchText, 256);
    }

    private void ParseInputText()
    {
        searchRegex = null;
        if (searchText.Length >= 3 && searchText.StartsWith('/') && searchText.EndsWith('/'))
        {
            try
            {
                searchRegex = new Regex(searchText[1..^1], RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return;
            }
            catch (Exception)
            {
                searchRegex = null;
            }
        }

        searchTokens = searchText.Trim().ToLower().Split(' ').Where(t => !string.IsNullOrEmpty(t)).ToArray();
        parsedSearchText = searchText.Trim();
    }
}
