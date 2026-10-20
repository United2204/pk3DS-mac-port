using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.Editors;

/// <summary>
/// Wild encounters for X/Y and OR/AS. Slots are a flat array of four-byte records
/// (species+form packed into a ushort, then min and max level) grouped by encounter type.
/// </summary>
public static class WildGen6Editor
{
    private const int SlotSize = 4;
    private const int FormShift = 11;
    private const int SpeciesMask = 0x7FF;
    private static readonly System.Text.Json.JsonSerializerOptions BatchJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    public static WildGen6CatalogResponse GetCatalog(WildGen6CatalogRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen6(config);
        var garc = new WildGen6Archive(config);
        var zonedata = garc.GetFile(garc.ZoneDataFileIndex);
        var locations = config.GetText(TextName.metlist_000000);
        var areas = Enumerable.Range(0, garc.AreaCount).Select(locationIndex =>
        {
            var fileIndex = garc.FirstMapFile + locationIndex;
            return new WildGen6AreaSummary(fileIndex, locationIndex, GetAreaName(zonedata, locations, locationIndex),
                TryGetEncounterOffset(garc.GetFile(fileIndex), config.ORAS, out _));
        }).ToArray();
        return new WildGen6CatalogResponse(config.ORAS ? "ORAS" : "XY", areas);
    }

    public static WildGen6TableResponse GetTable(WildGen6TableRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen6(config);
        var garc = new WildGen6Archive(config);
        var fileIndex = RequireFileIndex(garc, request.FileIndex);
        var file = garc.GetFile(fileIndex);
        if (!TryGetEncounterOffset(file, config.ORAS, out var offset))
            throw new WorkspaceException("Esta área no contiene una tabla de encuentros que se pueda editar.");

        var locationIndex = request.FileIndex - garc.FirstMapFile;
        var groups = GetGroups(config.ORAS, ReadSlots(file, offset, GetSlotCount(config.ORAS)));
        var areaName = GetAreaName(garc.GetFile(garc.ZoneDataFileIndex), config.GetText(TextName.metlist_000000), locationIndex);
        return new WildGen6TableResponse(request.FileIndex, areaName, groups, Catalogs.Species(config));
    }

    public static ExportResult Export(WildGen6ExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "wild", ["encdata"], config =>
            {
                Guard.Gen6(config);
                var garc = new WildGen6Archive(config);
                var fileIndex = RequireFileIndex(garc, request.FileIndex);
                var file = garc.GetFile(fileIndex);
                if (!TryGetEncounterOffset(file, config.ORAS, out var offset))
                    throw new WorkspaceException("No se pueden añadir encuentros a un área que no tiene tabla.");

                var slotCount = GetSlotCount(config.ORAS);
                var slots = FlattenGroups(request.Groups, slotCount, Catalogs.SpeciesCount(config));
                WriteSlots(file, offset, slots);
                // OR/AS keeps a second, packed copy of every area's slots in file 537; both must agree
                // or the game reads back the original encounters.
                if (config.ORAS)
                {
                    var packed = garc.GetFile(garc.PackedDataFileIndex);
                    var locationIndex = fileIndex - garc.FirstMapFile;
                    var packedOffset = GetPackedSlotOffset(packed, locationIndex, slotCount * SlotSize);
                    WriteSlots(packed, packedOffset, slots);
                    garc.SetFile(garc.PackedDataFileIndex, packed);
                }

                garc.SetFile(fileIndex, file);
                garc.Save();
                return [config.GetGARCFileName("encdata")];
            });

    /// <summary>Writes read-only JSON reports for every Gen. VI encounter area.</summary>
    public static WildGen6BatchReportResponse ExportBatchReport(WildGen6BatchReportRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen6(config);
        var archive = new WildGen6Archive(config);
        var zoneData = archive.GetFile(archive.ZoneDataFileIndex);
        var locations = config.GetText(TextName.metlist_000000);
        var output = EditorSession.ResolveBatchExportDirectory(workspace, request.OutputDirectory, "wild-gen6-reports");
        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-wild-gen6-reports-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);

        var reports = new List<WildGen6BatchReportFile>();
        var withoutEncounters = new List<WildGen6NoEncounterArea>();
        var skipped = new List<WildGen6SkippedArea>();
        try
        {
            var reportDirectory = Path.Combine(staging, "Areas");
            Directory.CreateDirectory(reportDirectory);
            for (var locationIndex = 0; locationIndex < archive.AreaCount; locationIndex++)
            {
                var fileIndex = archive.FirstMapFile + locationIndex;
                var areaName = GetAreaName(zoneData, locations, locationIndex);
                var groups = Array.Empty<WildGen6Group>();
                bool? hasEncounters = null;
                string? noEncounterReason = null;
                string? areaDiagnostic = null;
                try
                {
                    fileIndex = archive.GetAreaFileIndex(locationIndex);
                    var data = archive.GetFile(fileIndex);
                    if (data.Length == 0)
                    {
                        hasEncounters = false;
                        noEncounterReason = "El archivo de área está vacío; no contiene una tabla de encuentros.";
                    }
                    else if (!TryGetEncounterOffset(data, config.ORAS, out var offset, out var tableDiagnostic))
                    {
                        // Keep a marker report for this area; the manifest retains the parse diagnostic.
                        areaDiagnostic = tableDiagnostic;
                    }
                    else
                    {
                        var slots = ReadSlots(data, offset, GetSlotCount(config.ORAS));
                        groups = GetGroups(config.ORAS, slots);
                        hasEncounters = slots.Any(HasEncounterData);
                        if (hasEncounters == false)
                            noEncounterReason = "La tabla es válida, pero todos sus slots están vacíos según los campos interpretados.";
                    }
                }
                catch (Exception exception) when (IsBatchDataException(exception))
                {
                    areaDiagnostic = exception.Message;
                }

                var fileName = Path.Combine("Areas", $"{locationIndex:D3}-{SanitizeFileName(areaName)}.json")
                    .Replace(Path.DirectorySeparatorChar, '/');
                var document = new WildGen6AreaReportDocument(
                    config.Version.ToString(), fileIndex, locationIndex, areaName, hasEncounters, groups, areaDiagnostic);
                var outputPath = Path.Combine(staging, fileName.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllText(outputPath, System.Text.Json.JsonSerializer.Serialize(document, BatchJsonOptions));
                var bytes = checked((int)new FileInfo(outputPath).Length);
                reports.Add(new WildGen6BatchReportFile(
                    fileIndex, locationIndex, areaName, fileName, bytes,
                    groups.Sum(group => group.Slots.Length), groups.Length, hasEncounters));
                if (noEncounterReason is not null)
                    withoutEncounters.Add(new WildGen6NoEncounterArea(
                        fileIndex, locationIndex, areaName, noEncounterReason, fileName));
                if (areaDiagnostic is not null)
                    skipped.Add(new WildGen6SkippedArea(fileIndex, locationIndex, areaName, areaDiagnostic, fileName));
            }

            const string manifestName = "manifest.json";
            var manifest = new
            {
                format = "pk3DS Wild Gen VI encounter reports",
                gameVersion = config.Version.ToString(),
                totalAreas = archive.AreaCount,
                source = new
                {
                    encdata = config.GetGARCFileName("encdata"),
                    note = "Informes de solo lectura; no modifican el workspace original.",
                },
                exported = reports,
                areasWithoutEncounters = withoutEncounters,
                skipped,
            };
            File.WriteAllText(Path.Combine(staging, manifestName),
                System.Text.Json.JsonSerializer.Serialize(manifest, BatchJsonOptions));

            Directory.Move(staging, output);
            var diagnosticsSummary = skipped.Count == 0
                ? null
                : $"Se omitieron {skipped.Count} áreas; el manifiesto contiene los motivos.";
            return new WildGen6BatchReportResponse(
                config.Version.ToString(), output, Path.Combine(output, manifestName), archive.AreaCount,
                reports.ToArray(), withoutEncounters.ToArray(), skipped.ToArray(),
                reports.Sum(report => (long)report.Bytes), diagnosticsSummary);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    internal static int GetSlotCount(bool oras) => oras ? 61 : 94;

    internal static int GetPackedSlotOffset(byte[] packed, int locationIndex, int slotBytes)
    {
        var pointerOffset = ((long)locationIndex + 1) * sizeof(int);
        if (locationIndex < 0 || slotBytes < 0 || pointerOffset < 0 || pointerOffset + sizeof(int) > packed.Length)
            throw new WorkspaceException("La tabla interna de encuentros de OR/AS no es válida.");

        var relativeOffset = BitConverter.ToInt32(packed, (int)pointerOffset);
        if (relativeOffset < 0)
            throw new WorkspaceException("La tabla interna de encuentros de OR/AS no es válida.");

        var slotOffset = (long)relativeOffset + 0xE;
        if (slotOffset < 0 || slotOffset + slotBytes > packed.Length)
            throw new WorkspaceException("La tabla interna de encuentros de OR/AS no es válida.");
        return (int)slotOffset;
    }

    internal static bool TryGetEncounterOffset(byte[] file, bool oras, out int offset)
        => TryGetEncounterOffset(file, oras, out offset, out _);

    private static bool TryGetEncounterOffset(byte[] file, bool oras, out int offset, out string diagnostics)
    {
        offset = 0;
        if (file.Length < 0x18)
        {
            diagnostics = $"El archivo mide {file.Length} bytes; el encabezado de encuentros requiere al menos 24 bytes.";
            return false;
        }

        var relativeOffset = BitConverter.ToInt32(file, 0x10);
        var candidate = (long)relativeOffset + (oras ? 0xE : 0x10);
        var requiredBytes = GetSlotCount(oras) * SlotSize;
        if (relativeOffset < 0 || candidate < 0 || candidate + requiredBytes > file.Length)
        {
            diagnostics = "El offset de la tabla es negativo o la tabla queda truncada fuera del archivo.";
            return false;
        }

        offset = (int)candidate;
        diagnostics = "";
        return true;
    }

    private static bool HasEncounterData(WildGen6Slot slot) =>
        slot.Species != 0 || slot.Form != 0 || slot.MinLevel != 0 || slot.MaxLevel != 0;

    private static bool IsBatchDataException(Exception exception) =>
        exception is WorkspaceException or ArgumentException or EndOfStreamException
            or InvalidDataException or FormatException or OverflowException or IndexOutOfRangeException;

    private static string SanitizeFileName(string name)
    {
        var safe = new string(name.Select(character =>
            char.IsControl(character) || "<>:\"/\\|?*".Contains(character)
                ? character == '?' ? '-' : '_'
                : character).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) ? "Area" : safe;
    }

    internal static WildGen6Slot[] ReadSlots(byte[] data, int offset, int count) => Enumerable.Range(0, count).Select(index =>
    {
        var at = offset + (index * SlotSize);
        var packed = BitConverter.ToUInt16(data, at);
        return new WildGen6Slot(packed & SpeciesMask, packed >> FormShift, data[at + 2], data[at + 3]);
    }).ToArray();

    internal static void WriteSlots(byte[] data, int offset, WildGen6Slot[] slots)
    {
        for (var index = 0; index < slots.Length; index++)
        {
            var at = offset + (index * SlotSize);
            BitConverter.GetBytes((ushort)(slots[index].Species | (slots[index].Form << FormShift))).CopyTo(data, at);
            data[at + 2] = (byte)slots[index].MinLevel;
            data[at + 3] = (byte)slots[index].MaxLevel;
        }
    }

    internal static WildGen6Group[] GetGroups(bool oras, WildGen6Slot[] slots)
    {
        var definitions = oras
            ? new (string Name, int Count)[] { ("Hierba", 12), ("Hierba alta", 12), ("Enjambre", 3), ("Surf", 5), ("Golpe Roca", 5), ("Caña Vieja", 3), ("Caña Buena", 3), ("Caña Super", 3), ("Horda A · 60%", 5), ("Horda B · 35%", 5), ("Horda C · 5%", 5) }
            : new (string Name, int Count)[] { ("Hierba", 12), ("Flores amarillas", 12), ("Flores moradas", 12), ("Flores rojas", 12), ("Terreno rocoso", 12), ("Surf", 5), ("Golpe Roca", 5), ("Caña Vieja", 3), ("Caña Buena", 3), ("Caña Super", 3), ("Horda A · 60%", 5), ("Horda B · 35%", 5), ("Horda C · 5%", 5) };
        var offset = 0;
        return definitions.Select(definition =>
        {
            var group = new WildGen6Group(definition.Name, slots.Skip(offset).Take(definition.Count).ToArray());
            offset += definition.Count;
            return group;
        }).ToArray();
    }

    internal static WildGen6Slot[] FlattenGroups(WildGen6Group[]? groups, int expectedCount, int speciesCount)
    {
        var slots = groups?.SelectMany(group => group.Slots ?? []).ToArray() ?? [];
        if (slots.Length != expectedCount || slots.Any(slot =>
                slot.Species < 0 || slot.Species >= speciesCount
                || slot.Form is < 0 or > 31
                || slot.MinLevel is < 0 or > 100 || slot.MaxLevel is < 0 or > 100
                || slot.MinLevel > slot.MaxLevel))
            throw new WorkspaceException("Los slots de encuentro no son válidos.");
        return slots;
    }

    private static string GetAreaName(byte[] zonedata, string[] locations, int locationIndex)
    {
        var offset = (locationIndex * 56) + 0x1C;
        if (offset + 1 >= zonedata.Length)
            return $"Área {locationIndex:000}";
        var locationId = zonedata[offset] + (0x100 * (zonedata[offset + 1] & 1));
        var name = locationId >= 0 && locationId < locations.Length ? locations[locationId] : "";
        return string.IsNullOrWhiteSpace(name) ? $"Área {locationIndex:000}" : $"{locationIndex:000} · {name}";
    }

    private static int RequireFileIndex(WildGen6Archive garc, int fileIndex) =>
        fileIndex >= garc.FirstMapFile && fileIndex < garc.FirstMapFile + garc.AreaCount
            ? fileIndex
            : throw new WorkspaceException("El área indicada no existe.");
}

/// <summary>
/// Maps encounter-area indices onto the Gen. VI encdata GARC. OR/AS stores its 536 compressed
/// area files first, followed by the zone-name table at 536 and the packed encounter mirror at
/// 537. X/Y instead has a zone-name table at 0 and uncompressed areas starting at 1.
/// </summary>
internal sealed class WildGen6Archive
{
    private const int OrasAreaFileCount = 536;
    private const int OrasZoneDataFileIndex = 536;
    private const int OrasPackedDataFileIndex = 537;

    private readonly bool _oras;
    private readonly GARCFile? _xy;
    private readonly LazyGARCFile? _orasArchive;

    public int FirstMapFile => _oras ? 0 : 1;
    public int AreaCount => _oras ? OrasAreaFileCount : Math.Max(0, _xy!.FileCount - 1);
    public int ZoneDataFileIndex => _oras ? OrasZoneDataFileIndex : 0;
    public int PackedDataFileIndex => _oras ? OrasPackedDataFileIndex : -1;

    internal int GetAreaFileIndex(int locationIndex) => GetAreaFileIndex(_oras, FileCount, locationIndex);

    internal static int GetAreaFileIndex(bool oras, int fileCount, int locationIndex)
    {
        var firstFile = oras ? 0 : 1;
        var areaCount = oras ? OrasAreaFileCount : Math.Max(0, fileCount - firstFile);
        if (oras && fileCount <= OrasPackedDataFileIndex)
            throw new WorkspaceException("El GARC encdata de OR/AS no contiene sus tablas de encuentros completas.");
        if (locationIndex < 0 || locationIndex >= areaCount)
            throw new WorkspaceException("La zona Gen. VI indicada no existe en encdata.");
        return firstFile + locationIndex;
    }

    private int FileCount => _oras ? _orasArchive!.FileCount : _xy!.FileCount;

    public WildGen6Archive(GameConfig config)
    {
        _oras = config.ORAS;
        if (_oras)
        {
            _orasArchive = config.GetlzGARCData("encdata");
            if (_orasArchive.FileCount <= OrasPackedDataFileIndex)
                throw new WorkspaceException("El GARC encdata de OR/AS no contiene sus tablas de encuentros completas.");
        }
        else
        {
            _xy = config.GetGARCData("encdata");
        }
    }

    public byte[] GetFile(int index) => _oras ? _orasArchive![index] : _xy!.GetFile(index);

    public void SetFile(int index, byte[] data)
    {
        if (_oras)
            _orasArchive![index] = data;
        else
            _xy!.SetFile(index, data);
    }

    public void Save()
    {
        if (_oras)
            _orasArchive!.Save();
        else
            _xy!.Save();
    }
}
