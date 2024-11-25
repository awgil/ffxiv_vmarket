using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace Market;
internal class ContextMenu(MainWindow wndMain)
{
    public const int SatisfactionSupplyItemIdx = 0x54;
    public const int SatisfactionSupplyItem1Id = 0x80 + 1 * 0x3C;
    public const int SatisfactionSupplyItem2Id = 0x80 + 2 * 0x3C;
    public const int ContentsInfoDetailContextItemId = 0x17CC;
    public const int RecipeNoteContextItemId = 0x398;
    public const int AgentItemContextItemId = 0x28;
    public const int GatheringNoteContextItemId = 0xA0;
    public const int ItemSearchContextItemId = 0x17D0;
    public const int ChatLogContextMenuType = ChatLogContextItemId + 0x8;
    public const int ChatLogContextItemId = 0x950;

    public const int SubmarinePartsMenuContextItemId = 0x54;
    public const int ShopExchangeItemContextItemId = 0x54;
    public const int ShopContextMenuItemId = 0x54;
    public const int ShopExchangeCurrencyContextItemId = 0x54;
    public const int HWDSupplyContextItemId = 0x38C;
    public const int GrandCompanySupplyListContextItemId = 0x54;
    public const int GrandCompanyExchangeContextItemId = 0x54;

    private MainWindow wndMain = wndMain;

    public void MenuOpened(IMenuOpenedArgs args)
    {
        uint? itemId;
        Service.Log.Debug($"{args.AddonName}");
        Service.Log.Debug($"{(ulong)args.AgentPtr:X}");
        itemId = GetGameObjectItemId(args);
        itemId %= 500000;
        Service.Log.Debug($"{itemId}");

        if (itemId != null && itemId is uint id)
        {
            var menuItem = new MenuItem
            {
                Name = "Open on vmarket",
                PrefixChar = 'V',
                IsEnabled = (Service.LuminaRow<Item>(id)!.Value.ItemSearchCategory.RowId > 0),
            };
            menuItem.OnClicked += clickedArgs => wndMain.OpenWindowToItem(id);
            args.AddMenuItem(menuItem);
        }
    }

    private uint? GetGameObjectItemId(IMenuOpenedArgs args)
    {
        var item = args.AddonName switch
        {
            null => HandleNulls(),
            "Shop" => GetObjectItemId("Shop", ShopContextMenuItemId),
            "GrandCompanySupplyList" => GetObjectItemId("GrandCompanySupplyList", GrandCompanySupplyListContextItemId),
            "GrandCompanyExchange" => GetObjectItemId("GrandCompanyExchange", GrandCompanyExchangeContextItemId),
            "ShopExchangeCurrency" => GetObjectItemId("ShopExchangeCurrency", ShopExchangeCurrencyContextItemId),
            "SubmarinePartsMenu" => GetObjectItemId("SubmarinePartsMenu", SubmarinePartsMenuContextItemId),
            "ShopExchangeItem" => GetObjectItemId("ShopExchangeItem", ShopExchangeItemContextItemId),
            "ContentsInfoDetail" => GetObjectItemId("ContentsInfo", ContentsInfoDetailContextItemId),
            "RecipeNote" => GetObjectItemId("RecipeNote", RecipeNoteContextItemId),
            "RecipeTree" => GetObjectItemId(AgentById(AgentId.RecipeItemContext), AgentItemContextItemId),
            "RecipeMaterialList" => GetObjectItemId(AgentById(AgentId.RecipeItemContext), AgentItemContextItemId),
            "RecipeProductList" => GetObjectItemId(AgentById(AgentId.RecipeItemContext), AgentItemContextItemId),
            "GatheringNote" => GetObjectItemId("GatheringNote", GatheringNoteContextItemId),
            "ItemSearch" => GetObjectItemId(args.AgentPtr, ItemSearchContextItemId),
            "ChatLog" => GetObjectItemId("ChatLog", ChatLogContextItemId),
            _ => null,
        };

        if (args.AddonName == "ChatLog" && (item >= 1500000 || GetObjectItemId("ChatLog", ChatLogContextMenuType) != 3))
        {
            return null;
        }

        if (item == null)
        {
            var guiHoveredItem = Service.GameGui.HoveredItem;
            if (guiHoveredItem >= 2000000 || guiHoveredItem == 0) return null;
            item = (uint)guiHoveredItem % 500_000;
        }

        return item;
    }

    private uint GetObjectItemId(uint itemId)
    {
        if (itemId > 500000)
            itemId -= 500000;

        return itemId;
    }

    private unsafe uint? GetObjectItemId(IntPtr agent, int offset)
        => agent != IntPtr.Zero ? GetObjectItemId(*(uint*)(agent + offset)) : null;

    private uint? GetObjectItemId(string name, int offset)
        => GetObjectItemId(Service.GameGui.FindAgentInterface(name), offset);

    private unsafe uint? HandleSatisfactionSupply()
    {
        var agent = Service.GameGui.FindAgentInterface("SatisfactionSupply");
        if (agent == IntPtr.Zero)
            return null;

        var itemIdx = *(byte*)(agent + SatisfactionSupplyItemIdx);
        return itemIdx switch
        {
            1 => GetObjectItemId(*(uint*)(agent + SatisfactionSupplyItem1Id)),
            2 => GetObjectItemId(*(uint*)(agent + SatisfactionSupplyItem2Id)),
            _ => null,
        };
    }
    private unsafe uint? HandleHWDSupply()
    {
        var agent = Service.GameGui.FindAgentInterface("HWDSupply");
        if (agent == IntPtr.Zero)
            return null;

        return GetObjectItemId(*(uint*)(agent + HWDSupplyContextItemId));
    }

    private uint? HandleNulls()
    {
        var itemId = HandleSatisfactionSupply() ?? HandleHWDSupply();
        return itemId;
    }

    private unsafe IntPtr AgentById(AgentId id)
    {
        var uiModule = (UIModule*)Service.GameGui.GetUIModule();
        var agents = uiModule->GetAgentModule();
        var agent = agents->GetAgentByInternalId(id);
        return (IntPtr)agent;
    }
}
