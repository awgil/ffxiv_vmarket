using Dalamud.Common;
using Dalamud.Game;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Reflection;

namespace Market;

public sealed class Plugin : IDalamudPlugin
{
    private WindowSystem WindowSystem = new("vmarket");
    private MainWindow _wndMain;
    private ICommandManager _cmd;
    private ContextMenu _ctx;

    public Plugin(IDalamudPluginInterface dalamud, ISigScanner sigScanner, ICommandManager commandManager)
    {
        if (!dalamud.ConfigDirectory.Exists)
            dalamud.ConfigDirectory.Create();

        var dalamudRoot = dalamud.GetType().Assembly.
                GetType("Dalamud.Service`1", true)!.MakeGenericType(dalamud.GetType().Assembly.GetType("Dalamud.Dalamud", true)!).
                GetMethod("Get")!.Invoke(null, BindingFlags.Default, null, [], null);
        var dalamudStartInfo = dalamudRoot?.GetType().GetProperty("StartInfo", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(dalamudRoot) as DalamudStartInfo;
        var gameVersion = dalamudStartInfo?.GameVersion?.ToString() ?? "unknown";
        InteropGenerator.Runtime.Resolver.GetInstance.Setup(sigScanner.SearchBase, gameVersion, new(dalamud.ConfigDirectory.FullName + "/cs.json"));
        FFXIVClientStructs.Interop.Generated.Addresses.Register();
        InteropGenerator.Runtime.Resolver.GetInstance.Resolve();

        dalamud.Create<Service>();

        _wndMain = new();
        WindowSystem.AddWindow(_wndMain);

        _cmd = commandManager;
        commandManager.AddHandler("/vmarket", new((_, _) => _wndMain.IsOpen ^= true));

        dalamud.UiBuilder.Draw += WindowSystem.Draw;
        dalamud.UiBuilder.OpenConfigUi += () => _wndMain.IsOpen = true;

        _ctx = new(_wndMain);
        Service.ContextMenu.OnMenuOpened += _ctx.MenuOpened;
    }

    public void Dispose()
    {
        Service.ContextMenu.OnMenuOpened -= _ctx.MenuOpened;
        _cmd.RemoveHandler("/vmarket");
        WindowSystem.RemoveAllWindows();
        _wndMain.Dispose();
    }
}
