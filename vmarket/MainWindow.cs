using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
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

public unsafe class MainWindow : Window, IDisposable
{
    private readonly Interop.MBFetch _mbFetch = new();
    private readonly Interop.MBPurchase _mbPurchase = new();
    private readonly Interop.RetainerBell _rb = new();
    private readonly Widget.ItemList _itemList = new();
    private readonly Widget.ItemListings _itemListings;

    private System.Threading.Tasks.Task? _updateTask;

    public MainWindow() : base("Marketboard")
    {
        _itemListings = new(_mbFetch, _mbPurchase);
    }

    public void Dispose()
    {
        _itemListings.Dispose();
        _rb.Dispose();
        _mbPurchase.Dispose();
        _mbFetch.Dispose();
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
                        _itemListings.Draw(_itemList.SelectedItem, player->CurrentWorld, player->ContentId);
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
        ImGui.TextUnformatted(_rb.ToString());

        var infoProxy = (InfoProxyItemSearch*)InfoModule.Instance()->InfoProxies[(int)InfoProxyId.ItemSearch].Value;
        if (ImGui.Button("Search..."))
        {
            _mbFetch.ExecuteRequest(37094); // nilopala nourishments painting
            //_mbFetch.ExecuteRequest(20042); // abalathian bitterling
            //_mbFetch.ExecuteRequest(5267); // fine sand
            //_mbFetch.ExecuteRequest(4850); // honey
            //Service.Framework.DelayTicks(10).ContinueWith(t =>
            //{
            //    _mbFetch.ExecuteRequest(4850); // honey
            //});
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(_updateTask != null && !_updateTask.IsCompleted))
        {
            if (ImGui.Button("Update retainers..."))
            {
                _updateTask = _rb.Update();
            }
        }

        ImGui.SameLine();

        var rm = RetainerManager.Instance();
        var invmgr = InventoryManager.Instance();
        if (ImGui.Button($"Request retainer info {rm->Ready}##req_ret_info"))
        {
            rm->RequestVenturesTimers();
        }

        ImGui.SameLine();

        //if (ImGui.Button("Open retainer list"))
        //{
        //    OpenRetainerList();
        //}

        //ImGui.SameLine();

        //if (ImGui.Button($"Open retainer 0 {rm->Retainers[0].NameString}###open_ret0"))
        //{
        //    OpenRetainer(rm->Retainers[0].RetainerId);
        //}

        //ImGui.SameLine();

        //if (ImGui.Button($"Close retainer"))
        //{
        //    CloseRetainer();
        //}

        //ImGui.SameLine();

        //if (ImGui.Button("Close retainer list"))
        //{
        //    CloseRetainerList();
        //}

        //ImGui.SameLine();

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

        using (var n = ImRaii.TreeNode($"Retainer cache: {_rb.Retainers.Count} retainers, dirty={_rb.StateDirty}, updating={_rb.UpdateInProgress}###retainer_cache"))
        {
            if (n)
            {
                foreach (var r in _rb.Retainers)
                {
                    using var nr = ImRaii.TreeNode($"Retainer {r.Id:X} '{r.Name}': {r.Listings.Count} listings###ret{r.Id:X}");
                    if (!nr)
                        continue;

                    foreach (var i in r.Listings)
                    {
                        using var nl = ImRaii.TreeNode($"[{i.Slot}]: selling {i.Quantity}x {i.ItemId} @ {i.UnitPrice}");
                    }
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
                        invmgr->MoveFromRetainerMarketToPlayerInventory(InventoryType.RetainerMarket, i, (uint)item.Quantity);
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

        using (var n = ImRaii.TreeNode("Bells"))
        {
            if (n)
            {
                foreach (var obj in Interop.RetainerBell.SummoningBells())
                {
                    using var bn = ImRaii.TreeNode($"[{obj.Value->ObjectIndex}] {(ulong)obj.Value->GetGameObjectId():X} '{obj.Value->NameString}' @ {obj.Value->Position}, d={Interop.Player.DistanceToHitbox(obj.Value)}, interact={Interop.Player.CanInteract(obj)}");
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
}
