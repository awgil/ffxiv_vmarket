using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin.Services;

namespace Market;

public class Service
{
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider Hook { get; private set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static ICondition Conditions { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static IKeyState KeyState { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;

    public static Lumina.GameData LuminaGameData => DataManager.GameData;
    public static Lumina.Excel.ExcelSheet<T>? LuminaSheet<T>() where T : struct, Lumina.Excel.IExcelRow<T> => LuminaGameData?.GetExcelSheet<T>(Lumina.Data.Language.English);
    public static Lumina.Excel.SubrowExcelSheet<T>? LuminaSubsheet<T>() where T : struct, Lumina.Excel.IExcelSubrow<T> => LuminaGameData?.GetSubrowExcelSheet<T>(Lumina.Data.Language.English);
    public static T? LuminaRow<T>(uint row) where T : struct, Lumina.Excel.IExcelRow<T> => LuminaSheet<T>()?.GetRowOrDefault(row);
    public static T? LuminaRow<T>(uint row, ushort subRow) where T : struct, Lumina.Excel.IExcelSubrow<T> => LuminaSubsheet<T>()?.GetSubrowOrDefault(row, subRow);
    public static T? FindRow<T>(Func<T, bool> predicate) where T : struct, Lumina.Excel.IExcelRow<T> => LuminaSheet<T>()?.FirstOrDefault(predicate);
    public static T? FindRow<T>(Func<T, bool> predicate, ClientLanguage? language = null) where T : struct, Lumina.Excel.IExcelSubrow<T> => LuminaSubsheet<T>()?.SelectMany(m => m).Cast<T?>().FirstOrDefault(t => predicate(t!.Value));
    public static T[]? FindRows<T>(Func<T, bool> predicate) where T : struct, Lumina.Excel.IExcelRow<T> => LuminaSheet<T>()?.Where(predicate).ToArray() ?? null;
    public static T?[]? FindSubrows<T>(Func<T, bool> predicate) where T : struct, Lumina.Excel.IExcelSubrow<T> => LuminaSubsheet<T>()?.SelectMany(m => m).Cast<T?>().Where(t => predicate(t!.Value)).ToArray() ?? null;
}
