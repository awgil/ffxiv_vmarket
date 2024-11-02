using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using ImGuiNET;

namespace Market;

public unsafe class MainWindow : Window, IDisposable
{
    public MainWindow() : base("Marketboard")
    {
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        // TODO: search sidebar
        uint itemId = 4850; // honey

        var infoProxy = (InfoProxyItemSearch*)InfoModule.Instance()->InfoProxies[(int)InfoProxyId.ItemSearch].Value;

        ImGui.TextUnformatted("Hi");
    }
}
