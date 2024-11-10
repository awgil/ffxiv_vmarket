using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace Market.Interop;

public static unsafe class Player
{
    public static uint GetCurrentWorldId()
    {
        var player = (Character*)GameObjectManager.Instance()->Objects.IndexSorted[0].Value;
        return player != null ? player->CurrentWorld : 0u;
    }
}
