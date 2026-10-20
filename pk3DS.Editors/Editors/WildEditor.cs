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
    private const int MiniGroupSize = 11;
    private const int TablePairHeaderSize = 4;
    private const int EncounterTableSize = 0x164;
    private static readonly string[] EncounterGarcs = ["encdata", "zonedata", "worlddata"];
    private static readonly System.Text.Json.JsonSerializerOptions BatchJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

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

    /// <summary>Exports every Gen. VII encounter area as a read-only JSON report.</summary>
    public static WildGen7BatchReportResponse ExportBatchReport(WildGen7BatchReportRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen7(config, "encuentros");
        var encounterData = config.GetlzGARCData("encdata");
        var zoneArchive = config.GetlzGARCData("zonedata");
        var worldArchive = config.GetlzGARCData("worlddata");
        var areaCount = encounterData.FileCount / MiniGroupSize;
        var unassignedFileCount = encounterData.FileCount % MiniGroupSize;
        var unassignedFileIndices = Enumerable.Range(areaCount * MiniGroupSize, unassignedFileCount).ToArray();

        var zones = Array.Empty<ZoneData7>();
        string? metadataDiagnostics = null;
        try
        {
            var worlds = worldArchive.Files.Select(file => Mini.UnpackMini(file, "WD")[0]).ToArray();
            var zoneFiles = zoneArchive.Files;
            zones = ZoneData7.GetZoneData7Array(
                zoneFiles[0], zoneFiles[1], config.GetText(TextName.metlist_000000), worlds);
        }
        catch (Exception exception) when (IsBatchDataException(exception))
        {
            // Zone labels are helpful but not required to preserve the encounter tables.
            metadataDiagnostics = $"No se pudieron resolver los nombres de zona: {exception.Message}";
        }

        var output = EditorSession.ResolveBatchExportDirectory(workspace, request.OutputDirectory, "wild-gen7-reports");
        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-wild-gen7-reports-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);

        var reports = new List<WildGen7BatchReportFile>();
        var withoutTables = new List<WildGen7AreaWithoutTables>();
        var skipped = new List<WildGen7SkippedArea>();
        try
        {
            var reportDirectory = Path.Combine(staging, "Areas");
            Directory.CreateDirectory(reportDirectory);
            for (var areaIndex = 0; areaIndex < areaCount; areaIndex++)
            {
                var fileNumber = 9 + (MiniGroupSize * areaIndex);
                var areaZones = zones.Where(zone => zone.AreaIndex == areaIndex)
                    .OrderBy(zone => zone.Index).ToArray();
                var zoneNames = areaZones.Select(zone => zone.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
                var areaName = zoneNames.Length == 0 ? $"Área {areaIndex:000}" : string.Join(" / ", zoneNames);
                var tables = new List<WildGen7TablePairReport>();
                bool? hasTables = null;
                string? noTableReason = null;
                string? areaDiagnostic = metadataDiagnostics;
                var parseFailed = false;
                try
                {
                    var data = encounterData[fileNumber];
                    if (data.Length == 0)
                    {
                        hasTables = false;
                        noTableReason = "El archivo de área está vacío; no contiene tablas EA.";
                    }
                    else
                    {
                        var packedTables = Mini.UnpackMini(data, "EA");
                        hasTables = packedTables.Any(table => table.Length > 0);
                        if (hasTables == false)
                            noTableReason = "El grupo EA está vacío; no contiene tablas día/noche.";
                        else
                        {
                            for (var tableIndex = 0; tableIndex < packedTables.Length; tableIndex++)
                            {
                                var packedTable = packedTables[tableIndex];
                                if (packedTable.Length < TablePairHeaderSize + (2 * EncounterTableSize))
                                    throw new InvalidDataException(
                                        $"La tabla EA {tableIndex} mide {packedTable.Length} bytes; se esperaban al menos {TablePairHeaderSize + (2 * EncounterTableSize)}.");

                                var dayData = packedTable.Skip(TablePairHeaderSize).Take(EncounterTableSize).ToArray();
                                var nightData = packedTable.Skip(TablePairHeaderSize + EncounterTableSize).ToArray();
                                var day = new EncounterTable(dayData);
                                var night = new EncounterTable(nightData);
                                var headerHex = Convert.ToHexString(packedTable.Take(TablePairHeaderSize).ToArray());
                                tables.Add(new WildGen7TablePairReport(
                                    tableIndex, headerHex, ToTable(day), ToTable(night),
                                    Convert.ToHexString(dayData), Convert.ToHexString(nightData)));
                            }
                        }
                    }
                }
                catch (Exception exception) when (IsBatchDataException(exception))
                {
                    hasTables = null;
                    tables.Clear();
                    parseFailed = true;
                    areaDiagnostic = JoinDiagnostics(areaDiagnostic, exception.Message);
                }

                var fileName = Path.Combine("Areas", $"{areaIndex:D3}-{SanitizeBatchFileName(areaName)}.json")
                    .Replace(Path.DirectorySeparatorChar, '/');
                var document = new WildGen7AreaReportDocument(
                    config.Version.ToString(), areaIndex, fileNumber, areaName, zoneNames,
                    hasTables, tables.ToArray(), areaDiagnostic);
                var outputPath = Path.Combine(staging, fileName.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllText(outputPath, System.Text.Json.JsonSerializer.Serialize(document, BatchJsonOptions));
                var bytes = checked((int)new FileInfo(outputPath).Length);
                reports.Add(new WildGen7BatchReportFile(
                    areaIndex, fileNumber, areaName, fileName, bytes, tables.Count, hasTables));
                if (noTableReason is not null)
                    withoutTables.Add(new WildGen7AreaWithoutTables(
                        areaIndex, fileNumber, areaName, noTableReason, fileName));
                if (parseFailed)
                    skipped.Add(new WildGen7SkippedArea(areaIndex, fileNumber, areaName, areaDiagnostic!, fileName));
            }

            const string manifestName = "manifest.json";
            var manifest = new
            {
                format = "pk3DS Wild Gen VII encounter reports",
                gameVersion = config.Version.ToString(),
                totalAreas = areaCount,
                unassignedFileCount,
                unassignedFileIndices,
                source = new
                {
                    encdata = config.GetGARCFileName("encdata"),
                    zonedata = config.GetGARCFileName("zonedata"),
                    worlddata = config.GetGARCFileName("worlddata"),
                    note = "Informes de solo lectura; no modifican el workspace original.",
                },
                exported = reports,
                areasWithoutTables = withoutTables,
                skipped,
                diagnostics = metadataDiagnostics,
            };
            File.WriteAllText(Path.Combine(staging, manifestName),
                System.Text.Json.JsonSerializer.Serialize(manifest, BatchJsonOptions));

            Directory.Move(staging, output);
            var diagnosticsSummary = JoinDiagnostics(
                skipped.Count == 0 ? null : $"Se omitieron {skipped.Count} áreas dañadas; el manifiesto contiene los motivos.",
                unassignedFileCount == 0 ? null : $"Hay {unassignedFileCount} archivos de encdata fuera de grupos completos.",
                metadataDiagnostics);
            return new WildGen7BatchReportResponse(
                config.Version.ToString(), output, Path.Combine(output, manifestName), areaCount,
                reports.ToArray(), withoutTables.ToArray(), skipped.ToArray(), unassignedFileCount,
                unassignedFileIndices, reports.Sum(report => (long)report.Bytes), diagnosticsSummary);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

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

    private static bool IsBatchDataException(Exception exception) =>
        exception is WorkspaceException or ArgumentException or EndOfStreamException
            or InvalidDataException or FormatException or OverflowException or IndexOutOfRangeException;

    private static string SanitizeBatchFileName(string name)
    {
        var safe = new string(name.Select(character =>
            char.IsControl(character) || "<>:\"/\\|?*".Contains(character)
                ? character == '?' ? '-' : '_'
                : character).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) ? "Area" : safe;
    }

    private static string? JoinDiagnostics(params string?[] values)
    {
        var nonEmpty = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return nonEmpty.Length == 0 ? null : string.Join(" ", nonEmpty);
    }

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
