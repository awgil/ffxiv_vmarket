using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using System.Text;

namespace Market.Widget.Filters;
internal class EquipAsFilter : Filter
{
    public override string Name => "Equip as";
    public override bool IsSet => selectedClassJobs.Count >= 1;

    public override bool HasChanged
    {
        get
        {
            if (changed)
            {
                changed = false;
                return true;
            }

            return false;
        }
    }

    private List<uint> selectedClassJobs;
    private readonly ExcelSheet<RawRow> cjcSheet;
    private readonly List<ClassJob> classJobs;
    private bool changed;
    private bool selectingClasses;
    private int selectedMode;

    public EquipAsFilter()
    {
        selectedClassJobs = [];
        cjcSheet = Service.DataManager.GetExcelSheet<RawRow>(null, "ClassJobCategory");
        classJobs = [.. Service.LuminaSheet<ClassJob>()!
            .Where(cj => cj.RowId != 0)
            .OrderBy(cj => {
                return cj.Role switch
                {
                    0 => 3,
                    1 => 0,
                    2 => 2,
                    3 => 2,
                    4 => 1,
                    _ => 4
                };
            })];
        changed = false;
    }

    public override bool CheckFilter(Item item)
    {
        try
        {
            if (item.ClassJobCategory.RowId != 0)
            {
                var row = cjcSheet.GetRow(item.ClassJobCategory.RowId);
                if (selectedMode == 0)
                {
                    foreach (uint cjid in selectedClassJobs)
                        if (row.HasClass(cjid))
                            return true;
                    return false;
                }
                else
                {
                    foreach (uint cjid in selectedClassJobs)
                        if (!row.HasClass(cjid))
                            return false;
                    return true;
                }
            }
            else
                return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string SelectedClassString()
    {
        StringBuilder sb = new StringBuilder();
        bool first = true;
        foreach (var i in selectedClassJobs)
        {
            if (!first)
                sb.Append(", ");

            first = false;
            if (classJobs.TryGetFirst(c => c.RowId == i, out var cj))
                sb.Append(cj.Abbreviation);
        }

        if (first)
            sb.Append("None. Click here to select classes");

        return sb.ToString();
    }

    public override void Draw()
    {
        ImGui.SetNextItemWidth(60 * ImGui.GetIO().FontGlobalScale);
        if (ImGui.Combo("###equipAsSearchFilterModeCombo", ref selectedMode, ["Any", "All"], 2))
            changed = true;

        ImGui.SameLine();

        if (ImGui.SmallButton($"{(selectingClasses ? "Done" : SelectedClassString())}###equipAsChangeClassButton"))
        {
            selectingClasses = !selectingClasses;
            changed = true;
        }

        if (selectingClasses)
        {
            float wWidth = ImGui.GetWindowWidth();
            float firstColumnWith = ImGui.GetColumnWidth(0);

            ImGui.SameLine();

            if (ImGui.SmallButton("Select All"))
            {
                foreach (ClassJob cj in classJobs)
                {
                    if (cj.RowId != 0 && !selectedClassJobs.Contains(cj.RowId))
                    {
                        selectedClassJobs.Add(cj.RowId);
                        changed = true;
                    }
                }
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Select None"))
            {
                selectedClassJobs.Clear();
                changed = true;
            }

            ImGui.Columns(Math.Max(3, (int)(wWidth / (70 * ImGui.GetIO().FontGlobalScale))), "###equipAsClassList", false);
            ImGui.SetColumnWidth(0, firstColumnWith);
            try
            {
                foreach (ClassJob cj in classJobs)
                {
                    if (cj.RowId != 0)
                    {
                        if (ImGui.GetColumnIndex() == 0)
                            ImGui.NextColumn();

                        bool selected = selectedClassJobs.Contains(cj.RowId);
                        if (ImGui.Checkbox(cj.Abbreviation.ExtractText(), ref selected))
                        {
                            if (selected)
                            {
                                if (!selectedClassJobs.Contains(cj.RowId))
                                    selectedClassJobs.Add(cj.RowId);
                            }
                            else
                            {
                                if (selectedClassJobs.Contains(cj.RowId))
                                    selectedClassJobs.Remove(cj.RowId);
                            }

                            changed = true;
                        }

                        ImGui.NextColumn();
                    }
                }
            }
            catch (NullReferenceException nre)
            {
                Service.Log.Error(nre.ToString());
            }

            ImGui.Columns(2);
            ImGui.SetColumnWidth(0, firstColumnWith);
        }
        else if (Service.ClientState.LocalContentId != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Current Class"))
            {
                if (Service.ClientState.LocalPlayer != null)
                {
                    selectedClassJobs.Clear();
                    selectedClassJobs.Add(Service.ClientState.LocalPlayer.ClassJob.RowId);
                    changed = true;
                }
            }
        }
    }
}

public static class ClassExtensions
{
    public static bool HasClass(this RawRow cjc, uint classJobRowId) => cjc.ReadBoolColumn((int)(classJobRowId + 1));
}