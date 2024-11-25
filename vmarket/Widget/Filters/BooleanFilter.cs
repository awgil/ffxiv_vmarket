using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace Market.Widget.Filters;
internal class BooleanFilter(string name, string trueString, string falseString, Func<Item, bool, bool, bool> checkFunction) : Filter
{
    public override string Name { get; } = name;
    public override bool IsSet => showTrue == false || showFalse == false;

    private readonly string trueString = trueString;
    private readonly string falseString = falseString;
    private readonly Func<Item, bool, bool, bool> checkFunction = checkFunction;

    private bool showTrue = true;
    private bool showFalse = true;

    private static float _trueWidth;

    public static Func<Item, bool, bool, bool> CheckFunc(string n, bool invert = false)
    {
        var p = typeof(Item).GetProperty(n);
        if (p == null)
            Service.Log.Error($"Invalid Check Function for {nameof(BooleanFilter)}: {n}");

        return (i, t, f) =>
        {
            if (p == null) return true;
            var v = (bool)p.GetValue(i)!;
            return (invert ? !v : v) ? t : f;
        };
    }

    public override bool CheckFilter(Item item) => checkFunction.Invoke(item, showTrue, showFalse);

    public override void Draw()
    {
        using var c = ImRaii.Child($"{nameof(BooleanFilter)}-{Name}-Editor", new Vector2(-1, 24 * ImGui.GetIO().FontGlobalScale), false, ImGuiWindowFlags.None);
        if (c)
        {
            var x = ImGui.GetCursorPosX();
            if (ImGui.Checkbox(trueString, ref showTrue))
            {
                if (!showTrue) showFalse = true;
                Modified = true;
            }

            ImGui.SameLine();

            var x2 = ImGui.GetCursorPosX() - x;
            if (x2 > _trueWidth)
                _trueWidth = x2;

            ImGui.SetCursorPosX(x + _trueWidth);
            if (ImGui.Checkbox(falseString, ref showFalse))
            {
                if (!showFalse) showTrue = true;
                Modified = true;
            }
        }
    }
}
