using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;
using static Market.Widget.Filters.SexRaceExtensions;

namespace Market.Widget.Filters;
internal class SexRaceFilter : Filter
{
    public override string Name => "Sex / Race";
    public override bool IsSet => selectedOption > 0;

    public override bool HasChanged
    {
        get
        {
            if (lastIndex == selectedOption) return false;
            lastIndex = selectedOption;
            return true;
        }
    }

    private int selectedOption;
    private int lastIndex;
    private readonly List<(string text, uint raceId, CharacterSex sex)> options;
    private readonly List<EquipRaceCategory> equipRaceCategories;

    public SexRaceFilter()
    {
        equipRaceCategories = [.. Service.LuminaSheet<EquipRaceCategory>()];

        options = [("Not Selected", 0, CharacterSex.Female)];

        foreach (var race in Service.LuminaSheet<Race>()!.ToList())
        {
            if (race.RSEMBody.RowId > 0 && race.RSEFBody.RowId > 0)
            {
                string male = $"Male {race.Masculine}";
                string female = $"Female {race.Feminine}";
                options.Add((male, race.RowId, CharacterSex.Male));
                options.Add((female, race.RowId, CharacterSex.Female));
            }
            else if (race.RSEMBody.RowId > 0)
                options.Add((race.Masculine.ExtractText(), race.RowId, CharacterSex.Male));
            else if (race.RSEFBody.RowId > 0)
                options.Add((race.Feminine.ExtractText(), race.RowId, CharacterSex.Female));
        }
    }

    public override bool CheckFilter(Item item)
    {
        try
        {
            var (_, raceId, sex) = options[selectedOption];
            var erc = equipRaceCategories[item.EquipRestriction];
            return erc.AllowsRaceSex(raceId, sex);
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex.ToString());
            return true;
        }
    }

    public override void Draw()
    {
        using var c = ImRaii.Child($"{Name}Child", new Vector2(-1, 23 * ImGui.GetIO().FontGlobalScale), false, ImGuiWindowFlags.None);
        if (c)
        {
            if (Service.ClientState.LocalContentId != 0)
                ImGui.SetNextItemWidth(-80 * ImGui.GetIO().FontGlobalScale);
            else
                ImGui.SetNextItemWidth(-1);

            ImGui.Combo("##RaceSexSearchFilter", ref selectedOption, options.Select(a => a.text).ToArray(), options.Count);
            if (Service.ClientState.LocalContentId != 0)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Current"))
                {
                    if (Service.ClientState?.LocalPlayer != null)
                    {
                        var race = Service.ClientState.LocalPlayer.Customize[(int)CustomizeIndex.Race];
                        var sex = Service.ClientState.LocalPlayer.Customize[(int)CustomizeIndex.Gender] == 0 ? CharacterSex.Male : CharacterSex.Female;

                        for (var i = 0; i < options.Count; i++)
                        {
                            if (options[i].sex == sex && options[i].raceId == race)
                            {
                                selectedOption = i;
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}

public static class SexRaceExtensions
{
    public enum CharacterSex
    {
        Male = 0,
        Female = 1,
        Either = 2,
        Both = 3
    };

    public static bool AllowsRaceSex(this EquipRaceCategory erc, uint raceId, CharacterSex sex)
    {
        return sex switch
        {
            CharacterSex.Both when (erc.Male == false || erc.Female == false) => false,
            CharacterSex.Either when (erc.Male == false && erc.Female == false) => false,
            CharacterSex.Female when erc.Female == false => false,
            CharacterSex.Male when erc.Male == false => false,
            _ => raceId switch
            {
                0 => false,
                1 => erc.Hyur,
                2 => erc.Elezen,
                3 => erc.Lalafell,
                4 => erc.Miqote,
                5 => erc.Roegadyn,
                6 => erc.AuRa,
                7 => erc.Unknown0, // Hrothgar
                8 => erc.Unknown1, // Viera
                _ => false
            }
        };
    }
}