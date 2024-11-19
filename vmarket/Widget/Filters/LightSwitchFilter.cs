using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace Market.Widget.Filters;
internal class LightSwitchFilter(string name, FontAwesomeIcon icon, Func<Item, bool, bool> checkFunction) : Filter
{
    public override string Name => name;
    public override bool ShowName => false;
    public override bool IsSet => enabled;

    private readonly Func<Item, bool, bool> checkFunction = checkFunction;
    private bool enabled = false;

    public static Func<Item, bool, bool> CheckFunc(string n)
    {
        var p = typeof(Item).GetProperty(n);
        if (p == null)
            Service.Log.Error($"Invalid Check Function for {nameof(LightSwitchFilter)}: {n}");

        return (i, e) =>
        {
            if (p == null) return true;
            var v = (bool)p.GetValue(i)!;
            return e;
        };
    }

    public override bool CheckFilter(Item item) => checkFunction.Invoke(item, enabled);

    public override void Draw()
    {
        using var c = ImRaii.Child($"{nameof(LightSwitchFilter)}-{Name}-Editor", new Vector2(-1, 24 * ImGui.GetIO().FontGlobalScale), false, ImGuiWindowFlags.None);
        if (c)
        {
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            using var colour = ImRaii.PushColor(ImGuiCol.Button, 0xFF5CB85C, enabled).Push(ImGuiCol.ButtonHovered, 0x885CB85C, enabled);
            if (ImGui.Button(icon.ToIconString(), new(32 * ImGui.GetIO().FontGlobalScale, ImGui.GetItemRectSize().Y)))
            {
                enabled ^= true;
                Modified = true;
            }
        }
    }
}
