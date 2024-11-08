using Dalamud.Hooking;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using System.Runtime.InteropServices;

namespace Market;

[StructLayout(LayoutKind.Explicit, Size = 0x20)]
public unsafe struct RequestHeader
{
    [FieldOffset(0x00)] public uint Opcode;
    [FieldOffset(0x08)] public ulong PayloadSize; // without first 16 bytes
}

[StructLayout(LayoutKind.Explicit, Size = 0x30)]
public unsafe struct RequestInteract
{
    [FieldOffset(0x00)] public RequestHeader Header;
    [FieldOffset(0x20)] public ulong TargetObjectId;
    [FieldOffset(0x28)] public uint EventHandlerId;
}

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
public unsafe class MainWindow : Window, IDisposable
{
    private readonly Interop.Marketboard _mb = new();
    private readonly Widget.ItemList _itemList = new();
    private readonly Widget.ItemListings _itemListings = new();

    private Hook<NetworkModuleProxy.Delegates.ProcessPacketEventPlay> _processPacketEventPlayHook;
    private bool _suppressEventPlay;
    private ushort _curEventStage;

    public MainWindow() : base("Marketboard")
    {
        _processPacketEventPlayHook = Service.Hook.HookFromAddress<NetworkModuleProxy.Delegates.ProcessPacketEventPlay>(NetworkModuleProxy.Addresses.ProcessPacketEventPlay.Value, ProcessPacketEventPlayDetour);
        _processPacketEventPlayHook.Enable();
    }

    public void Dispose()
    {
        _processPacketEventPlayHook.Dispose();
        _mb.Dispose();
    }

    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("tabs");
        if (!tabs)
            return;

        using (var tabMarket = ImRaii.TabItem("Market"))
        {
            if (tabMarket)
            {
                using (var c = ImRaii.Child("itemList", new(267 * ImGui.GetIO().FontGlobalScale, 0), true))
                {
                    if (c)
                    {
                        _itemList.Draw();
                    }
                }
                ImGui.SameLine();

                using var ch = ImRaii.Child("rest");
                if (ch)
                {
                    var player = (Character*)GameObjectManager.Instance()->Objects.IndexSorted[0].Value;
                    if (_itemList.SelectedItem != 0 && player != null)
                    {
                        _itemListings.Draw(_itemList.SelectedItem, player->CurrentWorld, _mb, player->ContentId);
                    }
                }
            }
        }

        using (var tabListings = ImRaii.TabItem("My listings"))
        {
            if (tabListings)
            {
                ImGui.TextUnformatted("hello!");
            }
        }

        using (var tabDebug = ImRaii.TabItem("Debug"))
        {
            if (tabDebug)
            {
                DrawDebug();
            }
        }
    }

    private void DrawDebug()
    {
        ImGui.Checkbox($"Suppress EventPlay packet delivery (stage={_curEventStage})###suppress", ref _suppressEventPlay);

        var infoProxy = (InfoProxyItemSearch*)InfoModule.Instance()->InfoProxies[(int)InfoProxyId.ItemSearch].Value;
        if (ImGui.Button("Search..."))
        {
            _mb.Request(4850); // honey
        }

        ImGui.SameLine();

        var rm = RetainerManager.Instance();
        var invmgr = InventoryManager.Instance();
        if (ImGui.Button($"Request retainer info {rm->Ready}##req_ret_info"))
        {
            rm->RequestVenturesTimers();
        }

        ImGui.SameLine();

        if (ImGui.Button("Open retainer list"))
        {
            OpenRetainerList();
        }

        ImGui.SameLine();

        if (ImGui.Button($"Open retainer 0 {rm->Retainers[0].NameString}###open_ret0"))
        {
            OpenRetainer(rm->Retainers[0].RetainerId);
        }

        ImGui.SameLine();

        if (ImGui.Button($"Close retainer"))
        {
            CloseRetainer();
        }

        ImGui.SameLine();

        if (ImGui.Button("Close retainer list"))
        {
            CloseRetainerList();
        }

        ImGui.SameLine();

        if (ImGui.Button("List honey"))
        {
            var (invType, slot) = FindSlotWithItem(4850);
            var dstSlot = FindSlotWithItem(InventoryType.RetainerMarket, 0);
            if (slot >= 0 && dstSlot >= 0)
                invmgr->MoveToRetainerMarket(invType, (ushort)slot, InventoryType.RetainerMarket, (ushort)dstSlot, 1, 13);
        }

        using (var n = ImRaii.TreeNode($"Current listings: {infoProxy->ListingCount}, req={infoProxy->CurrentRequestId}/{infoProxy->NextRequestId}, waiting={infoProxy->WaitingForListings}###listings"))
        {
            if (n)
            {
                for (int i = 0; i < infoProxy->ListingCount; ++i)
                {
                    ref var l = ref infoProxy->Listings[i];
                    if (ImGui.Button($"Buy###{l.ListingId}"))
                    {
                        infoProxy->SetLastPurchasedItem(infoProxy->Listings.GetPointer(i));
                        infoProxy->SendPurchaseRequestPacket();
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"[{i}] {l.ListingId:X}: {l.Quantity}x {l.ItemId} '{Service.LuminaRow<Item>(l.ItemId)?.Name}'{(l.IsHqItem ? " (hq)" : "")} @ {l.UnitPrice} + {l.TotalTax}");
                }
            }
        }

        using (var n = ImRaii.TreeNode($"InfoProxy retainers"))
        {
            if (n)
            {
                for (int i = 0; i < infoProxy->PlayerRetainerCount; ++i)
                {
                    ref var r = ref infoProxy->PlayerRetainers[i];
                    using var rn = ImRaii.TreeNode($"Retainer {i}: {r.RetainerId:X} '{r.Name}' - {(r.SellingItems ? "selling" : "idle")}, unks {r.Unk_0x0A} {r.Unk_0x0C}");
                    if (rn)
                    {
                    }
                }
            }
        }

        using (var n = ImRaii.TreeNode($"RM retainers"))
        {
            if (n)
            {
                int i = 0;
                foreach (ref var r in rm->Retainers)
                {
                    using var sn = ImRaii.TreeNode($"[{i++}] {r.RetainerId:X} '{r.NameString}' mic={r.MarketItemCount}");
                }
            }
        }

        var inv = invmgr->GetInventoryContainer(InventoryType.RetainerMarket);
        using (var n = ImRaii.TreeNode($"Market inventory: size={inv->Size}, loaded={inv->Loaded}###inv"))
        {
            if (n)
            {
                for (ushort i = 0; i < inv->Size; ++i)
                {
                    ref var item = ref inv->Items[i];
                    using var id = ImRaii.PushId(0);
                    if (ImGui.Button("+1"))
                        invmgr->ModifyRetainerMarketPrice(i, (uint)invmgr->RetainerMarketUnitPrice[i] + 1);
                    ImGui.SameLine();
                    if (ImGui.Button("Delist"))
                        invmgr->MoveFromRetainerMarketToPlayerInventory(InventoryType.RetainerMarket, i, item.Quantity);
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"[{i}] {item.Quantity}x {item.ItemId} '{Service.LuminaRow<Item>(item.ItemId)?.Name}' @ {invmgr->RetainerMarketUnitPrice[i]} u={invmgr->RetainerMarketF18[i]}");
                }
            }
        }

        using (var n = ImRaii.TreeNode($"AgentRetainer"))
        {
            if (n)
            {
                var agentItems = (byte*)AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer) + 0x4B90;
                for (int i = 0; i < 20; ++i)
                {
                    ImGui.TextUnformatted($"[{i}] {*(Utf8String*)(agentItems + 8)} q={*(int*)(agentItems + 0x70)} {*(Utf8String*)(agentItems + 0x78)} {*(Utf8String*)(agentItems + 0xE0)} slot={*(ushort*)(agentItems + 0x148)}");
                    agentItems += 0x168;
                }
            }
        }
    }

    private (InventoryType, int) FindSlotWithItem(uint itemId)
    {
        Span<InventoryType> invs = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
        foreach (var t in invs)
        {
            var slot = FindSlotWithItem(t, itemId);
            if (slot >= 0)
                return (t, slot);
        }
        return (default, -1);
    }

    private int FindSlotWithItem(InventoryType inv, uint itemId)
    {
        var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
        for (int i = 0; i < cont->Size; ++i)
            if (cont->Items[i].ItemId == itemId)
                return i;
        return -1;
    }

    private void OpenRetainerList()
    {
        RequestInteract req = new();
        req.Header.Opcode = 0x15B; // see Client::Game::Event::EventFramework::interactWithSpecificHandler
        req.Header.PayloadSize = 0x20;
        req.TargetObjectId = /*0x1001FA50B;*/0x1004431D6; // object id of summoning bell
        req.EventHandlerId = 0xB0220; // B = CustomTalk
        Framework.Instance()->NetworkModuleProxy->SendPacket2(&req, 0, 0);
    }

    private void CloseRetainerList()
    {
        NetworkModuleProxy.SendEventDataPacket(0xB0220, _curEventStage, 0, null, 0, null);
    }

    private void OpenRetainer(ulong retainerId)
    {
        Span<uint> args = [(uint)(retainerId >> 32), (uint)retainerId];
        NetworkModuleProxy.SendEventDataPacket(0xB0220, _curEventStage, 0, args.GetPointer(0), 2, null);
    }

    private void CloseRetainer()
    {
        NetworkModuleProxy.SendEventDataPacket(0xB0220, _curEventStage, 0, null, 0, null);
    }

    private void ProcessPacketEventPlayDetour(ulong objectId, uint eventId, ushort state, ulong a4, uint* payload, byte payloadSize)
    {
        if (!_suppressEventPlay)
            _processPacketEventPlayHook.Original(objectId, eventId, state, a4, payload, payloadSize);
        if (eventId == 0xB0220)
            _curEventStage = state;
    }
}
