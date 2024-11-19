using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Market;

#nullable disable
[Sheet("SpecialShop")]
public unsafe readonly struct SpecialShopCustom(ExcelPage page, uint offset, uint row) : IExcelRow<SpecialShop>
{
    public SpecialShop SpecialShop => new(page, offset, row);
    public uint RowId => row;
    public static SpecialShop Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);

    public Entry[] Entries
    {
        get
        {
            foreach (var i in Enumerable.Range(0, 60))
            {
                Entries[i] = new Entry
                {
                    Result = [
                    new ResultEntry {
                        Item = new RowRef<Item>(Service.DataManager.Excel, page.ReadUInt32((nuint)(1 + i))),
                        Count = page.ReadUInt32((nuint)(61 + i)),
                        SpecialShopItemCategory = new RowRef<SpecialShopItemCategory>(Service.DataManager.Excel, page.ReadUInt32((nuint)(121 + i))),
                        HQ = page.ReadBool((nuint)(181 + i))
                    },
                    new ResultEntry {
                        Item = new RowRef<Item>(Service.DataManager.Excel, page.ReadUInt32((nuint)(241 + i))),
                        Count = page.ReadUInt32((nuint)(301 + i)),
                        SpecialShopItemCategory = new RowRef<SpecialShopItemCategory>(Service.DataManager.Excel, page.ReadUInt32((nuint)(361 + i))),
                        HQ = page.ReadBool((nuint)(421 + i))
                    }
                ],
                    Cost = [
                    new CostEntry {
                        Item = new RowRef<Item>(Service.DataManager.Excel, page.ReadUInt32((nuint)(481 + i))),
                        Count = page.ReadUInt32((nuint)(541 + i)),
                        HQ = page.ReadBool((nuint)(601 + i)),
                        Collectability = page.ReadUInt16((nuint)(661 + i))
                    },
                    new CostEntry {
                        Item = new RowRef<Item>(Service.DataManager.Excel, page.ReadUInt32((nuint)(721 + i))),
                        Count = page.ReadUInt32((nuint)(781 + i)),
                        HQ = page.ReadBool((nuint)(841 + i)),
                        Collectability = page.ReadUInt16((nuint)(901 + i))
                    },
                    new CostEntry {
                        Item = new RowRef<Item>(Service.DataManager.Excel, page.ReadUInt32((nuint)(961 + i))),
                        Count = page.ReadUInt32((nuint)(1021 + i)),
                        HQ = page.ReadBool((nuint)(1081 + i)),
                        Collectability = page.ReadUInt16((nuint)(1141 + i))
                    }
                ],
                    Quest = new RowRef<Quest>(Service.DataManager.Excel, page.ReadUInt32((nuint)(1201 + i))),
                    Unknown6 = page.ReadInt32((nuint)(1261 + i)),
                    Unknown7 = page.ReadUInt8((nuint)(1321 + i)),
                    Unknown8 = page.ReadUInt8((nuint)(1381 + i)),
                    AchievementUnlock = new RowRef<Achievement>(Service.DataManager.Excel, page.ReadUInt32((nuint)(1441 + i))),
                    Unknown10 = page.ReadInt8((nuint)(1501 + i)),
                    PatchNumber = page.ReadUInt16((nuint)(1561 + i))
                };
            }
            return Entries;
        }
    }

    public struct Entry
    {
        public ResultEntry[] Result;
        public CostEntry[] Cost;
        public RowRef<Quest> Quest;
        public int Unknown6;
        public byte Unknown7;
        public byte Unknown8;
        public RowRef<Achievement> AchievementUnlock;
        public int Unknown10;
        public ushort PatchNumber;
    }

    public struct ResultEntry
    {
        public RowRef<Item> Item;
        public uint Count;
        public RowRef<SpecialShopItemCategory> SpecialShopItemCategory;
        public bool HQ;
    }

    public struct CostEntry
    {
        public RowRef<Item> Item;
        public uint Count;
        public bool HQ;
        public ushort Collectability;
    }
}