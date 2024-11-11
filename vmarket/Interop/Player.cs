using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.Interop;

namespace Market.Interop;

public static unsafe class Player
{
    public static Pointer<GameObject> Object(int index) => GameObjectManager.Instance()->Objects.IndexSorted[index];
    public static Character* Character() => (Character*)Object(0).Value;

    public static uint GetCurrentWorldId()
    {
        var player = Character();
        return player != null ? player->CurrentWorld : 0u;
    }

    public static bool IsEventObject(Pointer<GameObject> obj, uint handlerId) => obj.Value != null && obj.Value->EventHandler != null && obj.Value->EventHandler->Info.EventId.Id == handlerId;

    public static IEnumerable<Pointer<GameObject>> EventObjects(uint handlerId)
    {
        for (int i = 299; i < 439; ++i)
        {
            var obj = Object(i);
            if (IsEventObject(obj, handlerId))
            {
                yield return obj;
            }
        }
    }

    public static float DistanceToHitbox(GameObject* obj)
    {
        var pc = Character();
        if (pc == null || obj == null)
            return float.MaxValue;

        var p1 = pc->GetPosition();
        var p2 = obj->GetPosition();
        var d = *p1 - *p2;
        return MathF.Sqrt(d.X * d.X + d.Z * d.Z) - pc->GetRadius() - obj->GetRadius();
    }

    public static Pointer<GameObject> ClosestEventObject(uint handlerId)
    {
        var gom = GameObjectManager.Instance();
        var pc = gom->Objects.IndexSorted[0].Value;
        if (pc == null)
            return null;

        float maxDistSq = float.MaxValue;
        GameObject* closest = null;
        foreach (var candidate in EventObjects(handlerId))
        {
            var offset = candidate.Value->Position - pc->Position;
            var distSq = offset.X * offset.X + offset.Z * offset.Z;
            if (distSq < maxDistSq)
            {
                maxDistSq = distSq;
                closest = candidate;
            }
        }
        return closest;
    }

    public static bool CanInteract(Pointer<GameObject> obj) => obj.Value != null && EventFramework.Instance()->CheckInteractRange(Object(0), obj.Value, 1, false);
    public static void Interact(Pointer<GameObject> obj) => TargetSystem.Instance()->InteractWithObject(obj.Value);
}
