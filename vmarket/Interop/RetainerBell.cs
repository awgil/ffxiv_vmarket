using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.Interop;
using System.Threading.Tasks;

namespace Market.Interop;

// retainer flow
// 1. click on bell
// 1.a. ClientIPC 0x15B + 0x7F + ExecuteCommand 3 (??? some generic interaction stuff), set cond OccupiedSummoningBell
// 1.b. ServerIPC ActorControl-ToggleWeapon + EventStart + PlayerStateFlags + EventPlay(handler=0x000B0220, scene=0, args=[])
// 1.c. lua CmnDefRetainerBell.OnScene00000 -> RetainerEventHandler::RequestRetainerList -> ServerCallback Request 2 -> ClientIPC ExecuteCommand 9003/listener-id/2/0/0, lua yields
// 1.d. ServerIPC RetainerInformation x10 + RetainerSummary -> RetainerManager::onRetainerInformation -> lua resume
// 1.e. lua -> RetainerEventHandler::WaitForRetainerTaskLoaded -> yield/resume...
// 1.f. lua -> RetainerEventHandler::LoadRetainerTaskData -> yield/resume...
// 1.g. lua OnScene_CallRetainer -> RetainerEventHandler::OpenRetainerList -> AgentRetainerList::open -> yield (shows menu)
// 2. click exit
// 2.a. -> hide -> proxy.exec(0, 0, 0)
// 2.b. lua return -> ClientIPC EventData2(....)
// 2.c. ServerIPC EventFinish + PlayerStateFlags
// 3. click retainer
// 3.a. ReceiveEvent kind=0, p0=2 -> proxy.exec(1, idHi, idLo) -> lua resume (1, idHi, idLo, retainer->isAvailable)
// 3.b. lua -> RetainerEventHandler::SetCurrentRetainerId(idHi, idLo) -> OnScene00000 returns [idHi, idLo]
// 3.c. lua return -> ClientIPC EventData2([RetainerEventHandler::id==]0x000B0220.0, p2=0, nargs=2, args=idHi idLo)
// 3.d. ServerIPC 375 -> set some agent update flag
// 3.e. ServerIPC RetainerState -> show no longer selling message, + a whole bunch of ItemInfo/ContainerInfo/CurrencyCrystalInfo/ItemMarketBoardInfo/ItemMarketBoardSummary/375/ACS/EventPlay/MapUpdate packets
// 3.f. ServerIPC EventPlay(handler=0x000B0220, scene=2, args=[retainerEntityId, x, pixieENPCId, x])
// 3.g. lua CmnDefRetainerBell.OnScene00002 -> RetainerEventHandler::SetCurrentRetainerEntityId, BindRetainer, pixie setup etc
// 3.h. lua RetainerEventHandler::SelectRetainerMenu -> ServerCallback Request 4 -> ClientIPC ExecuteCommand 9003/listener-id/4/0/0, lua yields
// 3.i. ServerIPC ACS Response 4 [0,0,0,0] -> show retainer menu
// 4. click market
// 4.a. ??? -> lua resume (RETAINER_MENU_MARKET_1)
// 4.b. lua -> RetainerEventHandler::OpenMarketFromPlayer -> AgentRetainer::open -> yield
// 4.c. ??? (list/delist/adjust price)
// 5. exit market
// 5.a. ??? -> lua resume -> go back to RetainerEventHandler::SelectRetainerMenu (3.h)
// 6. click exit
// 6.a. ??? -> lua resume -> function cleans up -> OnScene00002 returns []
// 6.b. lua return -> ClientIPC EventData2(0x000B0220.2, p2=0, nargs=0)
// 6.c. ServerIPC 375 + RetainerState (-> show selling message) + EventPlay(0x000B0220, scene=1, args=[idHi idLo])
// 6.d. lua CmnDefRetainerBell.OnScene00001 -> RetainerEventHandler::RequestRetainerSingleData -> ServerCallback Request 3 -> ClientIPC ExecuteCommand 9003/listener-id/3/lo/hi, lua yields
// 6.e. ServerIPC DespawnCharacter + RetainerInformation + RtainerSummary -> RetainerManager::onRetainerInformation -> lua resume
// 6.f. lua OnScene_CallRetainer -> ... (1.g)

// utilities to interact with retainer bells and retainers
public sealed class RetainerBell : IDisposable
{
    public record struct RetainerListing(ushort Slot, uint ItemId, int Quantity, uint UnitPrice);

    public record class RetainerState(ulong Id, string Name)
    {
        public readonly List<RetainerListing> Listings = [];
    }

    public readonly List<RetainerState> Retainers = [];
    public bool StateDirty { get; private set; } = true; // if set, state is best guess and can't really be trusted
    public bool UpdateInProgress { get; private set; }
    private ushort _curEventStage;
    private Hook<PacketDispatcher.Delegates.HandleEventPlayPacket> _processPacketEventPlayHook;
    private TaskCompletionSource? _transition;

    public unsafe RetainerBell()
    {
        _processPacketEventPlayHook = Service.Hook.HookFromAddress<PacketDispatcher.Delegates.HandleEventPlayPacket>(PacketDispatcher.Addresses.HandleEventPlayPacket.Value, ProcessPacketEventPlayDetour);
        _processPacketEventPlayHook.Enable();
    }

    public void Dispose()
    {
        _processPacketEventPlayHook.Dispose();
    }

    // interact with closest bell, summon each retainer, update internal inventory cache, optionally execute update operations, finally exit
    // should not be executed if another interation is in progress
    // TODO: update ops argument
    public async Task Update()
    {
        if (UpdateInProgress)
            throw new Exception("Another update operation is in progress");
        if (Service.Conditions[ConditionFlag.OccupiedSummoningBell])
            throw new Exception("Already interacting with summoning bell");

        var bell = FindClosestSummoningBell();
        if (!Player.CanInteract(bell))
            throw new Exception("Not in range of the bell");

        UpdateInProgress = true;
        using var raiiFlag = new RAII(() => UpdateInProgress = false);

        Player.Interact(bell);
        if (!Service.Conditions[ConditionFlag.OccupiedSummoningBell])
            throw new Exception("Failed to interact with summoning bell");
        await WaitForEventPlay();

        // update retainer list
        RequestRetainerListUpdate();
        await WaitWhile(RetainerUpdateInProgress);
        RebuildRetainers();
        StateDirty = false; // clear dirty flag now, so that if something sells mid update loop, we get dirty flag back

        // summon & update each retainer
        foreach (var r in Retainers)
        {
            OpenRetainer(r.Id);
            await WaitForEventPlay();

            RebuildRetainerInventory(r);
            // TODO: execute update operations, if any

            CloseRetainer();
            await WaitForEventPlay();
        }

        // close retainer list
        CloseRetainerList();
        await WaitWhile(() => Service.Conditions[ConditionFlag.OccupiedSummoningBell]);
    }

    public override string ToString() => $"State: {(UpdateInProgress ? "in progress" : "idle")}, event stage: {_curEventStage}";

    public static IEnumerable<Pointer<GameObject>> SummoningBells() => Player.EventObjects(0xB0220);
    public static Pointer<GameObject> FindClosestSummoningBell() => Player.ClosestEventObject(0xB0220);

    private unsafe static void RequestRetainerListUpdate() => RetainerManager.Instance()->RequestVenturesTimers();
    private unsafe static bool RetainerUpdateInProgress() => RetainerManager.Instance()->Ready == 0;

    private unsafe void RebuildRetainers()
    {
        Retainers.Clear();
        foreach (ref var r in RetainerManager.Instance()->Retainers)
            if (r.RetainerId != 0)
                Retainers.Add(new(r.RetainerId, r.NameString));
    }

    private unsafe void RebuildRetainerInventory(RetainerState r)
    {
        var mgr = InventoryManager.Instance();
        var inv = mgr->GetInventoryContainer(InventoryType.RetainerMarket);
        for (ushort i = 0; i < inv->Size; ++i)
            if (inv->Items[i].ItemId != 0)
                r.Listings.Add(new(i, inv->Items[i].ItemId, inv->Items[i].Quantity, (uint)mgr->RetainerMarketUnitPrice[i]));
    }

    private unsafe void CloseRetainerList()
    {
        Service.Log.Debug("Closing retainer list");
        PacketDispatcher.SendEventCompletePacket(0xB0220, _curEventStage, 0, null, 0, null);
    }

    private unsafe void OpenRetainer(ulong retainerId)
    {
        Service.Log.Debug($"Opening retainer {retainerId:X}");
        Span<uint> args = [(uint)(retainerId >> 32), (uint)retainerId];
        PacketDispatcher.SendEventCompletePacket(0xB0220, _curEventStage, 0, args.GetPointer(0), 2, null);
    }

    private unsafe void CloseRetainer()
    {
        Service.Log.Debug("Closing retainer");
        PacketDispatcher.SendEventCompletePacket(0xB0220, _curEventStage, 0, null, 0, null);
    }

    private async Task WaitForEventPlay()
    {
        _transition = new();
        await _transition.Task;
        _transition = null;
        await Service.Framework.DelayTicks(1); // don't execute stuff until we fully finish event processing
    }

    private async Task WaitWhile(Func<bool> condition)
    {
        while (condition())
            await Service.Framework.DelayTicks(1);
    }

    private unsafe void ProcessPacketEventPlayDetour(ulong objectId, uint eventId, ushort state, ulong a4, uint* payload, byte payloadSize)
    {
        if (eventId == 0xB0220)
        {
            _curEventStage = state;
            if (UpdateInProgress)
            {
                Service.Log.Debug($"Got event play {eventId:X}.{state:X}");
                _transition?.SetResult();
                _transition = null;
                return; // do not call original here, we don't want animations to play out
            }
        }

        _processPacketEventPlayHook.Original(objectId, eventId, state, a4, payload, payloadSize);
    }
}
