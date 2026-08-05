using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// Wild encounters for Gen VII. Each area holds paired day/night tables: ten regular slots plus
/// seven SOS call groups and six weather slots.
/// </summary>
public static class WildEditor
{
    private const int SlotsPerTable = 10;
    private const int SosGroups = 7;
    private const int WeatherSlots = 6;
    private static readonly string[] EncounterGarcs = ["encdata", "zonedata", "worlddata"];

    public static WildAreaCatalogResponse GetCatalog(WildAreaCatalogRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen7(config, "encuentros");
        var areas = GetAreas(config);
        return new WildAreaCatalogResponse(areas
            .Where(area => area.HasTables)
            .Select(area => new WildAreaSummary(area.FileNumber, area.Name, area.Tables.Count / 2))
            .ToArray());
    }

    public static WildTableResponse GetTable(WildTableRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen7(config, "encuentros");
        var area = FindArea(config, request.FileNumber);
        var index = RequireTable(area, request.TableIndex);
        return new WildTableResponse(area.FileNumber, area.Name, request.TableIndex,
            ToTable(area.Tables[index * 2]), ToTable(area.Tables[(index * 2) + 1]), Catalogs.Species(config));
    }

    public static ExportResult Export(WildExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "wild", EncounterGarcs, config =>
            {
                Guard.Gen7(config, "encuentros");
                var encdata = config.GetlzGARCData("encdata");
                var area = FindArea(config, request.FileNumber, encdata);
                var index = RequireTable(area, request.TableIndex);

                var speciesCount = Catalogs.SpeciesCount(config);
                ApplyTable(area.Tables[index * 2], request.Day, speciesCount);
                ApplyTable(area.Tables[(index * 2) + 1], request.Night, speciesCount);
                encdata[area.FileNumber] = Area7.GetDayNightTableBinary(area.Tables);
                encdata.Save();
                return [config.GetGARCFileName("encdata")];
            });

    private static Area7[] GetAreas(GameConfig config, LazyGARCFile? encounterData = null) => Area7.GetArray(
        encounterData ?? config.GetlzGARCData("encdata"),
        config.GetlzGARCData("zonedata"),
        config.GetlzGARCData("worlddata"),
        config.GetText(TextName.metlist_000000));

    private static Area7 FindArea(GameConfig config, int fileNumber, LazyGARCFile? encounterData = null) =>
        GetAreas(config, encounterData).FirstOrDefault(area => area.FileNumber == fileNumber && area.HasTables)
        ?? throw new WorkspaceException("El área indicada no contiene tablas de encuentros.");

    private static int RequireTable(Area7 area, int tableIndex) =>
        tableIndex >= 0 && tableIndex < area.Tables.Count / 2
            ? tableIndex
            : throw new WorkspaceException("La tabla indicada no existe en esta área.");

    private static WildEncounterTable ToTable(EncounterTable table) => new(
        table.MinLevel, table.MaxLevel,
        table.Encounter7s[0].Select((slot, index) => new WildEncounterSlot((int)slot.Species, (int)slot.Forme, table.Rates[index])).ToArray(),
        table.Encounter7s.Skip(1).Take(SosGroups).Select(group => group.Select(slot => new WildEncounterCompanionSlot((int)slot.Species, (int)slot.Forme)).ToArray()).ToArray(),
        table.AdditionalSOS.Select(slot => new WildEncounterCompanionSlot((int)slot.Species, (int)slot.Forme)).ToArray());

    private static void ApplyTable(EncounterTable target, WildEncounterTable? source, int speciesCount)
    {
        Validate(source, speciesCount);

        target.MinLevel = source!.MinLevel;
        target.MaxLevel = source.MaxLevel;
        for (var i = 0; i < source.Slots.Length; i++)
        {
            target.Encounter7s[0][i].Species = (uint)source.Slots[i].Species;
            target.Encounter7s[0][i].Forme = (uint)source.Slots[i].Form;
            target.Rates[i] = source.Slots[i].Rate;
        }
        for (var group = 0; group < source.SosSlots!.Length; group++)
        for (var slot = 0; slot < source.SosSlots[group].Length; slot++)
        {
            target.Encounter7s[group + 1][slot].Species = (uint)source.SosSlots[group][slot].Species;
            target.Encounter7s[group + 1][slot].Forme = (uint)source.SosSlots[group][slot].Form;
        }
        for (var slot = 0; slot < source.WeatherSlots!.Length; slot++)
        {
            target.AdditionalSOS[slot].Species = (uint)source.WeatherSlots[slot].Species;
            target.AdditionalSOS[slot].Forme = (uint)source.WeatherSlots[slot].Form;
        }
        target.Write();
    }

    internal static void Validate(WildEncounterTable? source, int speciesCount)
    {
        if (source?.Slots is not { Length: SlotsPerTable })
            throw new WorkspaceException("Cada tabla debe incluir exactamente diez slots.");
        if (source.MinLevel is < 1 or > 100 || source.MaxLevel is < 1 or > 100 || source.MinLevel > source.MaxLevel)
            throw new WorkspaceException("Los niveles deben estar entre 1 y 100, con el mínimo no mayor que el máximo.");
        if (source.Slots.Any(slot => slot.Species < 0 || slot.Species >= speciesCount || slot.Form is < 0 or > 31 || slot.Rate is < 0 or > 100))
            throw new WorkspaceException("Hay una especie, forma o probabilidad de encuentro inválida.");
        if (source.SosSlots is not { Length: SosGroups } || source.SosSlots.Any(group => group is not { Length: SlotsPerTable })
            || source.WeatherSlots is not { Length: WeatherSlots })
            throw new WorkspaceException("Los slots SOS o de clima están incompletos.");
        if (source.SosSlots.SelectMany(group => group).Concat(source.WeatherSlots)
            .Any(slot => slot.Species < 0 || slot.Species >= speciesCount || slot.Form is < 0 or > 31))
            throw new WorkspaceException("Hay una especie o forma inválida en los slots SOS o de clima.");

        // An empty table is written as all-zero rates; anything else has to be a full 100%.
        var rateTotal = source.Slots.Sum(slot => slot.Rate);
        if (rateTotal is not 0 and not 100)
            throw new WorkspaceException("Las probabilidades de cada tabla deben sumar 100% (o 0% para una tabla vacía).");
    }
}
