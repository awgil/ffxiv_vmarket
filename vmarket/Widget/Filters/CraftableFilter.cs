using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;
using System.Threading.Tasks;

namespace Market.Widget.Filters;
internal class CraftableFilter : Filter
{
    public override string Name { get; } = "Craftable";
    public override bool IsSet => selectedOption > 0;

    private int selectedOption = 0;
    private readonly string[] options;
    private readonly Dictionary<uint, RecipeLookup> craftableItems;
    private bool finishedLoading = false;

    public CraftableFilter()
    {
        craftableItems = [];
        options = new string[11];

        options[0] = "";
        options[1] = "Not Craftable";
        options[2] = "Craftable: Any";

        Task.Run(() =>
        {
            var cj = Service.LuminaSheet<ClassJob>()!;

            for (uint i = 0; i < 8; i++)
            {
                var job = cj.GetRow(i + 8);
                options[3 + i] = $"Craftable: {job.Abbreviation}";
            }

            foreach (var recipeLookup in Service.LuminaSheet<RecipeLookup>()!)
            {
                craftableItems.Add(recipeLookup.RowId, recipeLookup);
            }

            finishedLoading = true;
            Modified = true;
        });
    }

    public override bool CheckFilter(Item item)
    {
        if (!finishedLoading) return true;

        var isCraftable = craftableItems.ContainsKey(item.RowId);
        return selectedOption switch
        {
            1 => !isCraftable,
            2 => isCraftable,
            3 => isCraftable && craftableItems[item.RowId].CRP.RowId > 0,
            4 => isCraftable && craftableItems[item.RowId].BSM.RowId > 0,
            5 => isCraftable && craftableItems[item.RowId].ARM.RowId > 0,
            6 => isCraftable && craftableItems[item.RowId].GSM.RowId > 0,
            7 => isCraftable && craftableItems[item.RowId].LTW.RowId > 0,
            8 => isCraftable && craftableItems[item.RowId].WVR.RowId > 0,
            9 => isCraftable && craftableItems[item.RowId].ALC.RowId > 0,
            10 => isCraftable && craftableItems[item.RowId].CUL.RowId > 0,
            _ => true
        };
    }

    public override void Draw()
    {
        using var c = ImRaii.Child($"{Name}Child", new Vector2(-1, 23 * ImGui.GetIO().FontGlobalScale), false, ImGuiWindowFlags.None);
        if (c)
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo($"###{nameof(CraftableFilter)}_selection", ref selectedOption, options, options.Length, 14))
                Modified = true;
        }
    }
}
