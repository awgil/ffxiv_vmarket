using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;
using System.Threading.Tasks;

namespace Market.Widget.Filters;
internal class DesynthableFilter : Filter
{
    public override string Name { get; } = "Desynthable";
    public override bool IsSet => selectedOption > 0;

    private int selectedOption = 0;
    private readonly string[] options;
    private bool finishedLoading = false;

    public DesynthableFilter()
    {
        options = new string[11];

        options[0] = "";
        options[1] = "Not Desynthable";
        options[2] = "Desynthable: Any";

        Task.Run(() =>
        {
            var cj = Service.LuminaSheet<ClassJob>()!;

            for (uint i = 0; i < 8; i++)
            {
                var job = cj.GetRow(i + 8);
                options[3 + i] = $"Desynthable: {job.Abbreviation}";
            }

            finishedLoading = true;
            Modified = true;
        });
    }

    public override bool CheckFilter(Item item)
    {
        if (!finishedLoading) return true;

        var isDesynthable = item.Desynth > 0;
        return selectedOption switch
        {
            1 => !isDesynthable,
            2 => isDesynthable,
            3 => isDesynthable && item.ClassJobRepair.RowId == 8,
            4 => isDesynthable && item.ClassJobRepair.RowId == 9,
            5 => isDesynthable && item.ClassJobRepair.RowId == 10,
            6 => isDesynthable && item.ClassJobRepair.RowId == 11,
            7 => isDesynthable && item.ClassJobRepair.RowId == 12,
            8 => isDesynthable && item.ClassJobRepair.RowId == 13,
            9 => isDesynthable && item.ClassJobRepair.RowId == 14,
            10 => isDesynthable && item.ClassJobRepair.RowId == 15,
            _ => true
        };
    }

    public override void Draw()
    {
        using var c = ImRaii.Child($"{Name}Child", new Vector2(-1, 23 * ImGui.GetIO().FontGlobalScale), false, ImGuiWindowFlags.None);
        if (c)
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo($"###{nameof(DesynthableFilter)}_selection", ref selectedOption, options, options.Length, 14))
                Modified = true;
        }
    }
}
