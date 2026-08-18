using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// Portable OWSE surface for Gen VI/VII scripts, zone entities and Gen VI map properties.
/// Unknown bytes remain outside the explicitly understood ranges and are preserved on export.
/// </summary>
public static class OverworldEditor
{
    private const int FilesPerWorld = 11;
    private const int ZoneScriptOffset = 7;
    private const int ZoneInfoOffset = 8;
    private const int Gen7EntityOffset = 0;
    private const string ZoneScriptGroup = "zone-script";
    private const string ZoneInfoGroup = "zone-info";
    private const string ZoneScriptIdentifier = "ZS";
    private const string ZoneInfoIdentifier = "ZI";
    private const string Gen7EntityIdentifier = "ED";
    private const string Gen6OverworldGroup = "gen6-overworld";
    private const string Gen6MapScriptGroup = "gen6-map-script";
    private const int Gen6ZoneDataSize = 0x38;
    private const int Gen6FirstZoneFileXy = 1;
    private const int Gen6FirstZoneFileOras = 2;
    private const int Gen6FurnitureSize = 0x14;
    private const int Gen6NpcSize = 0x30;
    private const int Gen6WarpSize = 0x18;
    private const int Gen6TriggerSize = 0x18;
    private const int Gen6MapPropertyOffset = 0x88;
    private const int Gen6MapMatrixDimensionsOffset = 0x04;
    private const int Gen6MapMatrixValuesOffset = 0x08;
    private const int Gen6MapMatrixRawDimensionsOffset = 0x14;
    private const int Gen6MapMatrixRawValuesOffset = 0x18;
    private const int Gen6MapPreviewMaxPixels = 2_000_000;
    private const int Gen6MapPreviewColorShift = 0;
    private const int Gen7EntityPositionOffset = 0x08;
    private const int Gen7EntityRecordSize = 0x3C;
    private const int Gen7EmPositionOffset = 0x08;
    private const int Gen7EmRecordSize = 0x78;
    private const int Gen7EmRecordKind = 1;
    private const int Gen7EiPositionOffset = 0x08;
    private const int Gen7EiRecordSize = 0x5C;
    private const int Gen7EiRecordKind = 10;
    private const int Gen7PrPositionOffset = 0x08;
    private const int Gen7PrKind203 = 203;
    private const int Gen7PrKind204 = 204;
    private const int Gen7EbPositionOffset = 0x08;
    private const int Gen7EbRecordSize = 0x3C;
    private const int Gen7EbRecordKind = 2;
    private const int Gen7EsPositionOffset = 0x08;
    // ES type 4 has a shorter retail record than EP/EB/EA. The four-byte kind
    // marker repeats at 0x04 + (index * 0x38), with XYZ beginning at 0x08.
    private const int Gen7EsRecordSize = 0x38;
    private const int Gen7EsRecordKind = 4;
    private const int Gen7EaPositionOffset = 0x08;
    private const int Gen7EaRecordSize = 0x3C;
    private const int Gen7EaRecordKind = 5;
    private const int Gen7EaKind6PayloadPositionOffset = 0x08;
    private const int Gen7EaKind6PayloadRecordSize = 0x30;
    private const int Gen7EaKind6FirstDescriptorSize = 0x18;
    private const int Gen7EaKind6DescriptorSize = 0x1C;
    private const int Gen7EaKind6RecordKind = 6;
    private const int Gen7EtPositionOffset = 0x08;
    private const int Gen7EtRecordSize = 0x54;
    private const int Gen7EtRecordKind = 7;
    private const int Gen7EtKind9PointHeaderSize = 0x08;
    private const int Gen7EtKind9PointSize = 0x0C;
    private const int Gen7EtKind9FirstDescriptorSize = 0x14;
    private const int Gen7EtKind9DescriptorSize = 0x18;
    private const int Gen7EtKind9RecordKind = 9;
    private const int ScriptHeaderSize = 0x1C;
    private const int MaxDecompressedScriptBytes = 16 * 1024 * 1024;

    public static OverworldCatalogResponse GetCatalog(OverworldCatalogRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        if (config.Generation == 6)
            return GetGen6Catalog(config);

        Guard.Gen7(config, "scripts del mundo");

        var encounterData = config.GetlzGARCData("encdata");
        var zoneData = config.GetlzGARCData("zonedata");
        var locations = config.GetText(TextName.metlist_000000);
        var worldCount = encounterData.FileCount / FilesPerWorld;
        var groups = new List<OverworldScriptGroupSummary>();

        for (var worldIndex = 0; worldIndex < worldCount; worldIndex++)
        {
            AddGroup(groups, encounterData, zoneData.Files, locations, worldIndex,
                ZoneScriptGroup, ZoneScriptOffset, ZoneScriptIdentifier);
            AddGroup(groups, encounterData, zoneData.Files, locations, worldIndex,
                ZoneInfoGroup, ZoneInfoOffset, ZoneInfoIdentifier);
        }

        return new OverworldCatalogResponse(config.Version.ToString(), groups.ToArray());
    }

    public static OverworldScriptEntryResponse GetEntry(OverworldScriptEntryRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        if (config.Generation == 6)
            return GetGen6Entry(config, request);

        Guard.Gen7(config, "scripts del mundo");

        var (offset, identifier) = GroupFormat(request.Group);
        var encounterData = config.GetlzGARCData("encdata");
        var worldCount = encounterData.FileCount / FilesPerWorld;
        if (request.WorldIndex < 0 || request.WorldIndex >= worldCount)
            throw new WorkspaceException("El mundo indicado no existe en encdata.");

        var packed = encounterData[(request.WorldIndex * FilesPerWorld) + offset];
        var scripts = ReadMini(packed, identifier);
        if (request.ScriptIndex < 0 || request.ScriptIndex >= scripts.Length)
            throw new WorkspaceException("El índice de script indicado no existe en este grupo.");

        var zoneData = config.GetlzGARCData("zonedata");
        var locations = config.GetText(TextName.metlist_000000);
        var locationName = GetLocationName(zoneData.Files, locations, request.WorldIndex);
        var zone = GetGen7ZoneSummary(zoneData.Files, encounterData.Files, request.WorldIndex);
        return Describe(request.Group, request.WorldIndex, request.ScriptIndex, locationName,
            scripts[request.ScriptIndex], zone);
    }

    /// <summary>
    /// Reads the editable portion of a Gen. VI zone. The response intentionally exposes only
    /// fields whose offsets are understood; all other bytes stay in the original records when an
    /// export is produced.
    /// </summary>
    public static OverworldGen6ZoneResponse GetGen6Zone(OverworldGen6ZoneRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen6(config);
        var zone = GetGen6Zone(config, request.ZoneIndex, out var locationName);
        var entities = ReadGen6Entities(zone[1]);
        return new OverworldGen6ZoneResponse(
            config.Version.ToString(), request.ZoneIndex, locationName,
            GetGen6ZoneSummary(zone, request.ZoneIndex), entities.Furniture, entities.Npcs,
            entities.Warps, entities.Triggers, entities.UnknownTriggers,
            ReadGen6ZoneMetadata(zone[0]));
    }

    /// <summary>Exports a safe entity-only Gen. VI OWSE edit as a LayeredFS patch.</summary>
    public static ExportResult Export(OverworldGen6ExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "owse-gen6", ["encdata"], config =>
            {
                Guard.Gen6(config);
                var garc = config.GetlzGARCData("encdata");
                var fileIndex = RequireGen6FileIndex(config, garc.FileCount, request.ZoneIndex);
                var zone = ReadMini(garc[fileIndex], "ZO");
                if (zone.Length < 2)
                    throw new WorkspaceException("La zona Gen. VI no contiene el bloque de entidades.");

                var entities = ReadGen6Entities(zone[1]);
                ApplyZoneMetadata(zone[0], request.Metadata);
                ApplyFurniture(zone[1], entities, request.Furniture);
                ApplyNpcs(zone[1], entities, request.Npcs);
                ApplyWarps(zone[1], entities, request.Warps);
                ApplyTriggers(zone[1], entities, request.Triggers, entities.TriggerOffset, "triggers");
                ApplyTriggers(zone[1], entities, request.UnknownTriggers, entities.UnknownTriggerOffset,
                    "triggers desconocidos");
                garc[fileIndex] = Mini.PackMini(zone, "ZO");
                garc.Save();
                return [config.GetGARCFileName("encdata")];
            });

    /// <summary>
    /// Rebuilds one Gen. VI OWSE script after changing instruction values. The instruction count
    /// and script/movement boundary stay fixed; this deliberately does not attempt to recompile
    /// the human-readable parser output.
    /// </summary>
    public static ExportResult ExportScript(OverworldGen6ScriptExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "owse-gen6-script", ["encdata"], config =>
            {
                Guard.Gen6(config);
                var scriptFile = RequireGen6ScriptFile(request.Group);
                var garc = config.GetlzGARCData("encdata");
                var fileIndex = RequireGen6FileIndex(config, garc.FileCount, request.ZoneIndex);
                var zone = ReadMini(garc[fileIndex], "ZO");
                if (zone.Length <= scriptFile)
                    throw new WorkspaceException("La zona Gen. VI no contiene el archivo de script solicitado.");

                byte[] rebuilt;
                if (scriptFile == 1)
                {
                    var entities = ReadGen6Entities(zone[1]);
                    rebuilt = RewriteScript(entities.Script, request.Instructions);
                    zone[1] = [.. zone[1].Take(entities.ScriptOffset), .. rebuilt];
                }
                else
                {
                    rebuilt = RewriteScript(zone[scriptFile], request.Instructions);
                    zone[scriptFile] = rebuilt;
                }

                garc[fileIndex] = Mini.PackMini(zone, "ZO");
                garc.Save();
                return [config.GetGARCFileName("encdata")];
            });

    /// <summary>
    /// Rebuilds one Gen. VII ZS/ZI script after changing instruction values. The instruction
    /// count is fixed so the mini-archive entry remains structurally compatible with the game.
    /// </summary>
    public static ExportResult ExportScript(OverworldScriptExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "owse-gen7-script", ["encdata"], config =>
            {
                Guard.Gen7(config, "scripts del mundo");
                var (offset, identifier) = GroupFormat(request.Group);
                var garc = config.GetlzGARCData("encdata");
                var worldCount = garc.FileCount / FilesPerWorld;
                if (request.WorldIndex < 0 || request.WorldIndex >= worldCount)
                    throw new WorkspaceException("El mundo indicado no existe en encdata.");

                var fileIndex = (request.WorldIndex * FilesPerWorld) + offset;
                var scripts = ReadMini(garc[fileIndex], identifier);
                if (request.ScriptIndex < 0 || request.ScriptIndex >= scripts.Length)
                    throw new WorkspaceException("El índice de script indicado no existe en este grupo.");

                scripts[request.ScriptIndex] = RewriteScript(scripts[request.ScriptIndex], request.Instructions);
                garc[fileIndex] = Mini.PackMini(scripts, identifier);
                garc.Save();
                return [config.GetGARCFileName("encdata")];
            });

    /// <summary>Reads the understood parent-map field from one Gen. VII zonedata record.</summary>
    public static OverworldGen7ZoneResponse GetGen7Zone(OverworldGen7ZoneRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen7(config, "metadatos de zona");
        var (data, offset) = RequireGen7ZoneData(config, request.ZoneIndex);
        var locations = config.GetText(TextName.metlist_000000);
        var parentMap = BitConverter.ToInt32(data, offset + 0x1C);
        var (worldIndex, areaIndex) = ReadGen7ZoneMapping(config, request.ZoneIndex);
        return new OverworldGen7ZoneResponse(
            config.Version.ToString(), request.ZoneIndex, GetGen7ZoneLocation(request.ZoneIndex, parentMap, locations),
            parentMap, data.Length, worldIndex, areaIndex);
    }

    /// <summary>
    /// Exports the understood Gen. VII zone routing fields to a LayeredFS patch. ParentMap lives
    /// in zonedata; AreaIndex lives in the selected world's WD mapping table. All other bytes are
    /// preserved.
    /// </summary>
    public static ExportResult ExportGen7Zone(OverworldGen7ZoneExportRequest request)
    {
        // ParentMap only needs zonedata. Keep the legacy partial-workspace flow working, and
        // require the additional tables only when the caller asks to reroute encounters.
        var extraGarcs = request.AreaIndex is null
            ? new[] { "zonedata" }
            : new[] { "zonedata", "worlddata", "encdata" };
        return EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "owse-gen7-zone", extraGarcs, config =>
            {
                Guard.Gen7(config, "metadatos de zona");
                var (data, offset) = RequireGen7ZoneData(config, request.ZoneIndex);
                var locations = config.GetText(TextName.metlist_000000);
                if (request.ParentMap < 0 || request.ParentMap >= locations.Length)
                    throw new WorkspaceException($"El mapa padre debe estar entre 0 y {locations.Length - 1}.");

                BitConverter.GetBytes(request.ParentMap).CopyTo(data, offset + 0x1C);
                var zoneGarc = config.GetlzGARCData("zonedata");
                zoneGarc[0] = data;
                zoneGarc.Save();

                if (request.AreaIndex is null)
                    return [config.GetGARCFileName("zonedata")];

                var encounterData = config.GetlzGARCData("encdata");
                var areaCount = encounterData.FileCount / FilesPerWorld;
                if (request.AreaIndex < 0 || request.AreaIndex >= areaCount)
                    throw new WorkspaceException($"El área de encuentros debe estar entre 0 y {Math.Max(0, areaCount - 1)}.");

                var worldData = config.GetlzGARCData("worlddata");
                var worldIndex = ReadGen7WorldIndex(zoneGarc.Files, request.ZoneIndex);
                ApplyGen7AreaIndex(worldData, worldIndex, request.ZoneIndex, request.AreaIndex.Value);
                worldData.Save();
                return [config.GetGARCFileName("zonedata"), config.GetGARCFileName("worlddata")];
            });
    }

    /// <summary>
    /// Reads the confirmed position vectors from the fixed-size records in the Gen. VII ED
    /// containers. Unknown fields and unsupported ED variants remain diagnostic-only.
    /// </summary>
    public static OverworldGen7EntityResponse GetGen7Entities(OverworldGen7EntityRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen7(config, "entidades del mundo");
        var encounterData = config.GetlzGARCData("encdata");
        var diagnostics = (string?)null;
        var blocks = ReadGen7EntityBlocks(encounterData.Files, request.WorldIndex, ref diagnostics) ?? [];
        var positions = ReadGen7EntityPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        var emPositions = ReadGen7EmPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        var eiPositions = ReadGen7EiPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        var prPositions = ReadGen7PrPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        var ebPositions = ReadGen7EbPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        var esPositions = ReadGen7EsPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        var eaPositions = ReadGen7EaPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        var etPositions = ReadGen7EtPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        return new OverworldGen7EntityResponse(
            config.Version.ToString(), request.WorldIndex, positions, emPositions, ebPositions, esPositions, eaPositions, etPositions, blocks,
            diagnostics, eiPositions, prPositions);
    }

    /// <summary>Exports only confirmed Gen. VII ED position vectors while preserving all other bytes.</summary>
    public static ExportResult ExportGen7Entities(OverworldGen7EntityExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "owse-gen7-entities", ["encdata"], config =>
            {
                Guard.Gen7(config, "entidades del mundo");
                var garc = config.GetlzGARCData("encdata");
                var fileIndex = RequireGen7WorldFileIndex(garc.FileCount, request.WorldIndex);
                var ed = ReadMini(garc[fileIndex], Gen7EntityIdentifier);
                var epIndex = FindMiniBlock(ed, "EP");
                if (epIndex < 0)
                    throw new WorkspaceException("La zona Gen. VII no contiene un bloque EP editable.");

                var entries = ReadMini(ed[epIndex], "EP");
                ApplyGen7EntityPositions(entries, request.Positions ?? []);
                ed[epIndex] = Mini.PackMini(entries, "EP");

                var emIndex = FindMiniBlock(ed, "EM");
                if (request.EmPositions is null)
                {
                    // Older clients can export EP without sending the newer EM field.
                }
                else if (emIndex < 0)
                {
                    throw new WorkspaceException("La zona Gen. VII no contiene un bloque EM editable.");
                }
                else
                {
                    var emEntries = ReadMini(ed[emIndex], "EM");
                    ApplyGen7EmPositions(emEntries, request.EmPositions);
                    ed[emIndex] = Mini.PackMini(emEntries, "EM");
                }

                if (request.EiPositions is not null)
                    ApplyGen7EiPositions(ed, request.EiPositions);

                if (request.PrPositions is not null)
                    ApplyGen7PrPositions(ed, request.PrPositions);

                if (request.EbPositions is null)
                {
                    // Older clients can export EP/EM without sending the newer EB field.
                }
                else
                {
                    ApplyGen7EbPositions(ed, request.EbPositions);
                }

                if (request.EsPositions is null)
                {
                    // Older clients can export EP/EM/EB without sending the newer ES field.
                }
                else
                {
                    ApplyGen7EsPositions(ed, request.EsPositions);
                }

                if (request.EaPositions is null)
                {
                    // Older clients can export EP/EM/EB/ES without sending the newer EA field.
                }
                else
                {
                    ApplyGen7EaPositions(ed, request.EaPositions);
                }

                if (request.EtPositions is null)
                {
                    // Older clients can export EP/EM/EB/ES/EA without sending the newer ET field.
                }
                else
                {
                    ApplyGen7EtPositions(ed, request.EtPositions);
                }

                garc[fileIndex] = Mini.PackMini(ed, Gen7EntityIdentifier);
                garc.Save();
                return [config.GetGARCFileName("encdata")];
            });

    /// <summary>
    /// Exports the decompressed ED container and every nested block/entry exactly as read. This
    /// is intentionally diagnostic-only: it makes unsupported Gen. VII variants available for
    /// inspection without guessing their record layout or changing the source workspace.
    /// </summary>
    public static OverworldGen7EntityRawExportResponse ExportGen7EntityRaw(OverworldGen7EntityRawExportRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen7(config, "entidades del mundo");
        var encounterData = config.GetlzGARCData("encdata");
        var fileIndex = RequireGen7WorldFileIndex(encounterData.FileCount, request.WorldIndex);
        var ed = encounterData[fileIndex] ?? [];
        if (ed.Length == 0)
            throw new WorkspaceException("La zona Gen. VII no contiene datos ED para exportar.");

        byte[][] blocks;
        try
        {
            blocks = ReadMini(ed, Gen7EntityIdentifier);
        }
        catch (WorkspaceException exception)
        {
            throw new WorkspaceException($"El bloque ED no se pudo exportar: {exception.Message}");
        }

        var output = ResolveRawEntityOutputDirectory(workspace, request.OutputDirectory, request.WorldIndex);
        var files = new List<OverworldGen7EntityRawFile>();
        var diagnostics = (string?)null;

        const string edName = "ed.bin";
        File.WriteAllBytes(Path.Combine(output, edName), ed);
        files.Add(new OverworldGen7EntityRawFile(edName, Gen7EntityIdentifier, null, null, ed.Length));

        var manifestBlocks = new List<object>(blocks.Length);
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            var block = blocks[blockIndex];
            var identifier = block.Length >= 2
                ? new string([(char)block[0], (char)block[1]])
                : "??";
            var blockName = $"ed-{blockIndex:D3}.bin";
            File.WriteAllBytes(Path.Combine(output, blockName), block);
            files.Add(new OverworldGen7EntityRawFile(blockName, identifier, blockIndex, null, block.Length));

            var entryFiles = new List<OverworldGen7EntityRawFile>();
            try
            {
                var entries = ReadMini(block, identifier);
                for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                {
                    var entryName = $"ed-{blockIndex:D3}-entry-{entryIndex:D3}.bin";
                    var entry = entries[entryIndex];
                    File.WriteAllBytes(Path.Combine(output, entryName), entry);
                    var file = new OverworldGen7EntityRawFile(entryName, identifier, blockIndex, entryIndex, entry.Length);
                    files.Add(file);
                    entryFiles.Add(file);
                }
            }
            catch (WorkspaceException exception)
            {
                diagnostics = AppendError(diagnostics,
                    $"El bloque {identifier} no se pudo detallar; se exportó intacto: {exception.Message}");
            }

            manifestBlocks.Add(new
            {
                blockIndex,
                identifier,
                bytes = block.Length,
                entries = entryFiles,
            });
        }

        var manifest = new
        {
            format = "pk3DS OWSE Gen VII raw ED",
            gameVersion = config.Version.ToString(),
            worldIndex = request.WorldIndex,
            source = new
            {
                garc = config.GetGARCFileName("encdata"),
                fileIndex,
                note = "Contenido descomprimido leído desde encdata; no es un parche LayeredFS.",
            },
            blocks = manifestBlocks,
            files,
            diagnostics,
        };
        var manifestName = "manifest.json";
        File.WriteAllText(Path.Combine(output, manifestName),
            System.Text.Json.JsonSerializer.Serialize(manifest,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                }));

        return new OverworldGen7EntityRawExportResponse(
            config.Version.ToString(), request.WorldIndex, fileIndex, config.GetGARCFileName("encdata"),
            output, Path.Combine(output, manifestName), files.ToArray(), files.Sum(file => (long)file.Bytes), diagnostics);
    }

    /// <summary>Reads the Gen. VI movement/property grid and map matrix referenced by a zone.</summary>
    public static OverworldGen6MapResponse GetGen6Map(OverworldGen6MapRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen6(config);
        var zone = GetGen6Zone(config, request.ZoneIndex, out _);
        var metadata = ReadGen6ZoneMetadata(zone[0])
            ?? throw new WorkspaceException("La zona Gen. VI no expone metadatos de mapa.");
        var mapGr = config.GetlzGARCData("mapGR");
        var map = ReadGen6Map(mapGr, metadata.MapArea);
        var matrix = ReadGen6MapMatrix(config.GetlzGARCData("mapMatrix"), metadata.MapMatrix);
        var preview = BuildGen6MapPreview(mapGr, matrix);
        return new OverworldGen6MapResponse(
            config.Version.ToString(), request.ZoneIndex, metadata.MapArea, metadata.MapMatrix,
            map.Width, map.Height, map.Properties, matrix.Width, matrix.Height, matrix.Values,
            JoinDiagnostics(map.Diagnostics, matrix.Diagnostics), preview);
    }

    /// <summary>Exports the understood Gen. VI map-property grid and/or matrix entries to a LayeredFS patch.</summary>
    public static ExportResult ExportMap(OverworldGen6MapExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "owse-gen6-map", ["encdata", "mapGR", "mapMatrix"], config =>
            {
                Guard.Gen6(config);
                if (request.Properties is null && request.MatrixValues is null)
                    throw new WorkspaceException("Indicá cambios para la grilla GR o la matriz MM.");

                var zone = GetGen6Zone(config, request.ZoneIndex, out _);
                var metadata = ReadGen6ZoneMetadata(zone[0])
                    ?? throw new WorkspaceException("La zona Gen. VI no expone metadatos de mapa.");
                var changed = new List<string>();

                if (request.Properties is not null)
                {
                    var mapGarc = config.GetlzGARCData("mapGR");
                    var map = ReadGen6Map(mapGarc, metadata.MapArea);
                    if (map.Properties.Length == 0)
                        throw new WorkspaceException(map.Diagnostics ?? "La grilla GR no expone propiedades editables.");
                    if (request.Properties.Length != map.Properties.Length)
                        throw new WorkspaceException($"La grilla del mapa debe conservar {map.Properties.Length} celdas.");

                    var raw = mapGarc[metadata.MapArea];
                    for (var index = 0; index < request.Properties.Length; index++)
                        BitConverter.GetBytes(request.Properties[index]).CopyTo(raw, Gen6MapPropertyOffset + (index * sizeof(uint)));
                    mapGarc[metadata.MapArea] = raw;
                    mapGarc.Save();
                    changed.Add(config.GetGARCFileName("mapGR"));
                }

                if (request.MatrixValues is not null)
                {
                    var matrixGarc = config.GetlzGARCData("mapMatrix");
                    var matrix = ReadGen6MapMatrix(matrixGarc, metadata.MapMatrix);
                    if (matrix.FirstEntry is null)
                        throw new WorkspaceException(matrix.Diagnostics ?? "La matriz MM no se puede editar.");
                    if (request.MatrixValues.Length != matrix.Values.Length)
                        throw new WorkspaceException($"La matriz MM debe conservar {matrix.Values.Length} celdas interpretadas.");

                    for (var index = 0; index < request.MatrixValues.Length; index++)
                        BitConverter.GetBytes(request.MatrixValues[index]).CopyTo(
                            matrix.FirstEntry, matrix.ValuesOffset + (index * sizeof(ushort)));
                    matrixGarc[metadata.MapMatrix] = matrix.Entries is null
                        ? matrix.FirstEntry
                        : Mini.PackMini(matrix.Entries, "MM");
                    matrixGarc.Save();
                    changed.Add(config.GetGARCFileName("mapMatrix"));
                }

                return changed;
            });

    private static OverworldCatalogResponse GetGen6Catalog(GameConfig config)
    {
        Guard.Gen6(config);
        var encounterData = config.GetlzGARCData("encdata");
        var files = encounterData.Files;
        var locations = config.GetText(TextName.metlist_000000);
        var firstZoneFile = config.ORAS ? Gen6FirstZoneFileOras : Gen6FirstZoneFileXy;
        var groups = new List<OverworldScriptGroupSummary>();

        for (var zoneIndex = 0; zoneIndex < files.Length - firstZoneFile; zoneIndex++)
        {
            var zone = ReadMiniOrEmpty(files[firstZoneFile + zoneIndex], "ZO");
            if (zone.Length == 0)
                continue;

            var locationName = GetGen6LocationName(files[0], locations, zoneIndex);
            AddGen6Group(groups, zone, locationName, zoneIndex, Gen6OverworldGroup);
            AddGen6Group(groups, zone, locationName, zoneIndex, Gen6MapScriptGroup);
        }

        return new OverworldCatalogResponse(config.Version.ToString(), groups.ToArray());
    }

    private static OverworldScriptEntryResponse GetGen6Entry(GameConfig config, OverworldScriptEntryRequest request)
    {
        Guard.Gen6(config);
        if (request.ScriptIndex != 0)
            throw new WorkspaceException("En Gen. VI cada grupo de zona expone un único script.");

        var encounterData = config.GetlzGARCData("encdata");
        var files = encounterData.Files;
        var firstZoneFile = config.ORAS ? Gen6FirstZoneFileOras : Gen6FirstZoneFileXy;
        if (request.WorldIndex < 0 || request.WorldIndex >= files.Length - firstZoneFile)
            throw new WorkspaceException("La zona indicada no existe en encdata.");

        var zone = ReadMini(files[firstZoneFile + request.WorldIndex], "ZO");
        var raw = GetGen6Script(zone, request.Group);
        var locations = config.GetText(TextName.metlist_000000);
        var locationName = GetGen6LocationName(files[0], locations, request.WorldIndex);
        var zoneSummary = GetGen6ZoneSummary(zone, request.WorldIndex);
        return Describe(request.Group, request.WorldIndex, request.ScriptIndex, locationName, raw, zoneSummary);
    }

    private static byte[][] GetGen6Zone(GameConfig config, int zoneIndex, out string locationName)
    {
        var garc = config.GetlzGARCData("encdata");
        var fileIndex = RequireGen6FileIndex(config, garc.FileCount, zoneIndex);
        var zone = ReadMini(garc[fileIndex], "ZO");
        if (zone.Length < 2)
            throw new WorkspaceException("La zona Gen. VI no contiene el bloque de entidades.");

        locationName = GetGen6LocationName(garc.Files[0], config.GetText(TextName.metlist_000000), zoneIndex);
        return zone;
    }

    private static int RequireGen6FileIndex(GameConfig config, int fileCount, int zoneIndex)
    {
        var firstZoneFile = config.ORAS ? Gen6FirstZoneFileOras : Gen6FirstZoneFileXy;
        if (zoneIndex < 0 || zoneIndex >= fileCount - firstZoneFile)
            throw new WorkspaceException("La zona Gen. VI indicada no existe en encdata.");
        return firstZoneFile + zoneIndex;
    }

    private static int RequireGen6ScriptFile(string group) => group switch
    {
        Gen6OverworldGroup => 1,
        Gen6MapScriptGroup => 2,
        _ => throw new WorkspaceException("El grupo de script Gen. VI debe ser gen6-overworld o gen6-map-script."),
    };

    private static byte[] RewriteScript(byte[] raw, uint[]? instructions)
    {
        if (raw is null || raw.Length < ScriptHeaderSize)
            throw new WorkspaceException("El script Gen. VI no tiene un encabezado completo.");
        try
        {
            return new Script(raw).WriteInstructions(instructions ?? []);
        }
        catch (ArgumentException exception)
        {
            throw new WorkspaceException(exception.Message);
        }
    }

    private static Gen6MapGrid ReadGen6Map(LazyGARCFile garc, int mapArea)
    {
        if (mapArea < 0 || mapArea >= garc.FileCount)
            throw new WorkspaceException($"El área de mapa {mapArea} no existe en mapGR.");
        var data = garc[mapArea];
        if (data is null || data.Length < 4)
            throw new WorkspaceException($"El área de mapa {mapArea} no contiene una cabecera GR completa.");
        if (data[0] != 'G' || data[1] != 'R')
            throw new WorkspaceException($"El área de mapa {mapArea} no tiene formato GR.");

        if (TryReadGen6MapTile(data, out _, out _))
            return new(0, 0, [], $"El área GR {mapArea} es un contenedor Mini; sus propiedades de movimiento no se editan como grilla plana.");

        if (data.Length < Gen6MapPropertyOffset)
            throw new WorkspaceException($"El área de mapa {mapArea} no contiene una cabecera GR completa.");

        var width = BitConverter.ToUInt16(data, 0x80);
        var height = BitConverter.ToUInt16(data, 0x82);
        var count = checked((long)width * height);
        if (width == 0 || height == 0 || count > 4_000_000 ||
            Gen6MapPropertyOffset + (count * sizeof(uint)) > data.Length)
            throw new WorkspaceException($"La grilla GR del área {mapArea} tiene dimensiones inválidas.");

        var cellCount = checked((int)count);
        var properties = new uint[cellCount];
        Buffer.BlockCopy(data, Gen6MapPropertyOffset, properties, 0, checked(cellCount * sizeof(uint)));
        return new Gen6MapGrid(width, height, properties, null);
    }

    private static Gen6MapMatrix ReadGen6MapMatrix(LazyGARCFile garc, int mapMatrix)
    {
        if (mapMatrix < 0 || mapMatrix >= garc.FileCount)
            return InvalidGen6MapMatrix($"La matriz {mapMatrix} no existe en mapMatrix.");
        var data = garc[mapMatrix];
        if (data is null || data.Length < 4 ||
            data[0] != 'M' || data[1] != 'M')
            return InvalidGen6MapMatrix($"La entrada {mapMatrix} no tiene formato MM reconocible.");

        byte[][]? entries = null;
        try
        {
            entries = Mini.UnpackMini(data, "MM");
        }
        catch
        {
            // A few Gen. VI fixtures use the legacy flat MM layout. Fall through to it
            // before reporting a malformed matrix.
        }

        if (entries is { Length: > 0 } && TryReadMatrixDimensions(
                entries[0], Gen6MapMatrixDimensionsOffset, out var width, out var height))
            return BuildGen6MapMatrix(width, height, entries[0], Gen6MapMatrixValuesOffset, entries);

        if (!TryReadMatrixDimensions(data, Gen6MapMatrixRawDimensionsOffset, out width, out height))
            return InvalidGen6MapMatrix($"La entrada {mapMatrix} no contiene un encabezado MM reconocible.");

        return BuildGen6MapMatrix(width, height, data, Gen6MapMatrixRawValuesOffset, null);
    }

    private static Gen6MapMatrix BuildGen6MapMatrix(
        int width, int height, byte[] firstEntry, int valuesOffset, byte[][]? entries)
    {
        var count = checked((long)width * height);
        if (width == 0 || height == 0 || count > 1_000_000)
            return InvalidGen6MapMatrix("La matriz MM tiene dimensiones inválidas.");

        var available = Math.Max(0, (firstEntry.Length - valuesOffset) / sizeof(ushort));
        var valueCount = checked((int)Math.Min(count, available));
        var values = new ushort[valueCount];
        for (var index = 0; index < values.Length; index++)
            values[index] = BitConverter.ToUInt16(firstEntry, valuesOffset + (index * sizeof(ushort)));
        var diagnostics = values.Length == count ? null :
            $"La matriz expone {values.Length} de {count} celdas en su sección inicial; se conserva el resto sin interpretar.";
        return new(width, height, values, diagnostics, entries, firstEntry, valuesOffset);
    }

    private static bool TryReadMatrixDimensions(byte[] data, int offset, out int width, out int height)
    {
        width = height = 0;
        if (data is null || offset < 0 || offset + 4 > data.Length)
            return false;
        width = BitConverter.ToUInt16(data, offset);
        height = BitConverter.ToUInt16(data, offset + 2);
        return width > 0 && height > 0 && (long)width * height <= 1_000_000;
    }

    private static Gen6MapMatrix InvalidGen6MapMatrix(string diagnostics) =>
        new(0, 0, [], diagnostics, null, null, 0);

    private sealed record Gen6MapGrid(int Width, int Height, uint[] Properties, string? Diagnostics);
    private sealed record Gen6MapMatrix(
        int Width, int Height, ushort[] Values, string? Diagnostics,
        byte[][]? Entries, byte[]? FirstEntry, int ValuesOffset);

    private sealed record Gen6MapTile(int Width, int Height, uint[] Tiles);

    private static string? JoinDiagnostics(params string?[] diagnostics)
    {
        var messages = diagnostics.Where(message => !string.IsNullOrWhiteSpace(message)).Distinct().ToArray();
        return messages.Length == 0 ? null : string.Join(" ", messages);
    }

    private static OverworldGen6MapPreview? BuildGen6MapPreview(
        LazyGARCFile mapGr, Gen6MapMatrix matrix)
    {
        if (matrix.Width <= 0 || matrix.Height <= 0 || matrix.Values.Length == 0)
            return null;

        var tiles = new Gen6MapTile?[matrix.Values.Length];
        var diagnostics = new List<string>();
        var tileWidth = 0;
        var tileHeight = 0;
        for (var index = 0; index < matrix.Values.Length; index++)
        {
            var entryIndex = matrix.Values[index];
            if (entryIndex == ushort.MaxValue)
                continue;
            if (entryIndex >= mapGr.FileCount)
            {
                diagnostics.Add($"MM {entryIndex} no existe en mapGR.");
                continue;
            }

            var packed = mapGr[entryIndex];
            if (!TryReadGen6MapTile(packed, out var tile, out var diagnostic))
            {
                diagnostics.Add($"GR {entryIndex}: {diagnostic}");
                continue;
            }

            if (tileWidth == 0)
            {
                tileWidth = tile.Width;
                tileHeight = tile.Height;
            }
            else if (tile.Width != tileWidth || tile.Height != tileHeight)
            {
                diagnostics.Add($"GR {entryIndex} tiene dimensiones {tile.Width}x{tile.Height}; se esperaba {tileWidth}x{tileHeight}.");
                continue;
            }

            tiles[index] = tile;
        }

        if (tileWidth == 0 || tileHeight == 0)
            return new(null, 0, 0, diagnostics.Count == 0
                ? "No se encontraron entradas GR visualizables en la matriz MM."
                : string.Join(" ", diagnostics.Distinct()));

        var width = checked(matrix.Width * tileWidth);
        var height = checked(matrix.Height * tileHeight);
        if ((long)width * height > Gen6MapPreviewMaxPixels)
            return new(null, 0, 0,
                $"La previsualización ocuparía {width}x{height} píxeles y se omitió por seguridad.");

        var rgba = new byte[checked(width * height * 4)];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            rgba[(pixel * 4) + 0] = 16;
            rgba[(pixel * 4) + 1] = 23;
            rgba[(pixel * 4) + 2] = 29;
            rgba[(pixel * 4) + 3] = 255;
        }

        for (var index = 0; index < tiles.Length; index++)
        {
            var tile = tiles[index];
            if (tile is null)
                continue;

            var originX = (index % matrix.Width) * tileWidth;
            var originY = (index / matrix.Width) * tileHeight;
            for (var tileIndex = 0; tileIndex < tile.Tiles.Length; tileIndex++)
            {
                var colorValue = tile.Tiles[tileIndex] == 0x01000021
                    ? 0xFF000000u
                    : LCRNG32.Advance(tile.Tiles[tileIndex], Gen6MapPreviewColorShift) | 0xFF000000u;
                var x = originX + (tileIndex % tileWidth);
                var y = originY + (tileIndex / tileWidth);
                var pixel = ((y * width) + x) * 4;
                rgba[pixel + 0] = (byte)(colorValue >> 16);
                rgba[pixel + 1] = (byte)(colorValue >> 8);
                rgba[pixel + 2] = (byte)colorValue;
                rgba[pixel + 3] = (byte)(colorValue >> 24);
            }
        }

        var diagnosticText = diagnostics.Count == 0
            ? null
            : $"Previsualización parcial: {string.Join(" ", diagnostics.Distinct())}";
        return new(
            Convert.ToBase64String(PortablePng.EncodeRgba(rgba, width, height)),
            width, height, diagnosticText);
    }

    private static bool TryReadGen6MapTile(
        byte[] packed, out Gen6MapTile tile, out string diagnostic)
    {
        tile = null!;
        diagnostic = "no es un contenedor Mini GR.";
        byte[][]? entries;
        try
        {
            entries = Mini.UnpackMini(packed, "GR");
        }
        catch (Exception exception)
        {
            diagnostic = exception.Message;
            return false;
        }

        if (entries is null || entries.Length == 0 || entries[0] is null)
            return false;
        var data = entries[0];
        if (data.Length < 4)
        {
            diagnostic = "la entrada visual está truncada.";
            return false;
        }

        var width = BitConverter.ToUInt16(data, 0);
        var height = BitConverter.ToUInt16(data, 2);
        var count = (long)width * height;
        if (width == 0 || height == 0 || count > 40_000 || 4 + (count * sizeof(uint)) > data.Length)
        {
            diagnostic = "la entrada visual tiene dimensiones inválidas.";
            return false;
        }

        var values = new uint[(int)count];
        for (var index = 0; index < values.Length; index++)
            values[index] = BitConverter.ToUInt32(data, 4 + (index * sizeof(uint)));
        tile = new(width, height, values);
        diagnostic = string.Empty;
        return true;
    }

    private static void AddGen6Group(
        ICollection<OverworldScriptGroupSummary> groups, byte[][] zone, string locationName,
        int zoneIndex, string group)
    {
        try
        {
            var raw = GetGen6Script(zone, group);
            if (raw.Length == 0)
                return;
            groups.Add(new OverworldScriptGroupSummary(
                group,
                group == Gen6OverworldGroup ? $"Overworld · {locationName}" : $"Script de mapa · {locationName}",
                zoneIndex,
                locationName,
                1,
                raw.Length));
        }
        catch (WorkspaceException)
        {
            // An experimental zone should not hide other valid zones from the catalog.
        }
    }

    private static byte[] GetGen6Script(byte[][] zone, string group)
    {
        if (zone is null || zone.Length <= 2)
            throw new WorkspaceException("El mini-archivo ZO no contiene los scripts esperados.");

        return group switch
        {
            Gen6MapScriptGroup => zone[2] ?? [],
            Gen6OverworldGroup => ExtractGen6OverworldScript(zone[1]),
            _ => throw new WorkspaceException("El grupo OWSE Gen. VI indicado no es compatible."),
        };
    }

    private static byte[] ExtractGen6OverworldScript(byte[] data)
    {
        return ReadGen6Entities(data).Script;
    }

    private static Gen6Entities ReadGen6Entities(byte[] data)
    {
        if (data is null || data.Length < 12)
            throw new WorkspaceException("El bloque de entidades Gen. VI no tiene una cabecera completa.");

        var entityLength = BitConverter.ToInt32(data, 0);
        var furnitureCount = data[4];
        var npcCount = data[5];
        var warpCount = data[6];
        var triggerCount = data[7];
        var unknownCount = BitConverter.ToInt32(data, 8);
        if (entityLength < 8 || unknownCount < 0)
            throw new WorkspaceException("El bloque de entidades Gen. VI declara una cantidad inválida.");

        var furnitureOffset = 12;
        var npcOffset = checked(furnitureOffset + (furnitureCount * Gen6FurnitureSize));
        var warpOffset = checked(npcOffset + (npcCount * Gen6NpcSize));
        var triggerOffset = checked(warpOffset + (warpCount * Gen6WarpSize));
        var unknownTriggerOffset = checked(triggerOffset + (triggerCount * Gen6TriggerSize));
        var scriptOffset = checked(unknownTriggerOffset + (unknownCount * Gen6TriggerSize));
        if (scriptOffset > data.Length - sizeof(int))
            throw new WorkspaceException("El bloque de entidades Gen. VI no alcanza su script.");

        var scriptLength = BitConverter.ToInt32(data, scriptOffset);
        if (scriptLength < 0 || scriptLength > data.Length - scriptOffset)
            throw new WorkspaceException("El script de overworld Gen. VI tiene una longitud inválida.");

        return new Gen6Entities(
            furnitureOffset, npcOffset, warpOffset, triggerOffset, unknownTriggerOffset, scriptOffset,
            ReadFurniture(data, furnitureOffset, furnitureCount),
            ReadNpcs(data, npcOffset, npcCount),
            ReadWarps(data, warpOffset, warpCount),
            ReadTriggers(data, triggerOffset, triggerCount),
            ReadTriggers(data, unknownTriggerOffset, unknownCount),
            data.Skip(scriptOffset).Take(scriptLength).ToArray());
    }

    private static OverworldFurnitureEntry[] ReadFurniture(byte[] data, int offset, int count) =>
        Enumerable.Range(0, count).Select(index =>
        {
            var at = offset + (index * Gen6FurnitureSize);
            return new OverworldFurnitureEntry(
                ReadU16(data, at), ReadU16(data, at + 0x08), ReadU16(data, at + 0x0A),
                ReadI16(data, at + 0x0C), ReadI16(data, at + 0x0E));
        }).ToArray();

    private static OverworldNpcEntry[] ReadNpcs(byte[] data, int offset, int count) =>
        Enumerable.Range(0, count).Select(index =>
        {
            var at = offset + (index * Gen6NpcSize);
            return new OverworldNpcEntry(
                ReadU16(data, at), ReadU16(data, at + 0x02), ReadU16(data, at + 0x08),
                ReadU16(data, at + 0x0A), ReadU16(data, at + 0x0C), ReadU16(data, at + 0x0E),
                ReadU16(data, at + 0x28), ReadU16(data, at + 0x2A), ReadU16(data, at + 0x04),
                ReadU16(data, at + 0x06));
        }).ToArray();

    private static OverworldWarpEntry[] ReadWarps(byte[] data, int offset, int count) =>
        Enumerable.Range(0, count).Select(index =>
        {
            var at = offset + (index * Gen6WarpSize);
            return new OverworldWarpEntry(
                ReadU16(data, at), ReadU16(data, at + 0x02), ReadI16(data, at + 0x08),
                ReadI16(data, at + 0x0C));
        }).ToArray();

    private static OverworldTriggerEntry[] ReadTriggers(byte[] data, int offset, int count) =>
        Enumerable.Range(0, count).Select(index =>
        {
            var at = offset + (index * Gen6TriggerSize);
            return new OverworldTriggerEntry(
                ReadU16(data, at), ReadU16(data, at + 0x04), ReadU16(data, at + 0x06),
                ReadU16(data, at + 0x08), ReadU16(data, at + 0x0C), ReadU16(data, at + 0x0E),
                ReadI16(data, at + 0x10), ReadI16(data, at + 0x12));
        }).ToArray();

    private static void ApplyFurniture(byte[] data, Gen6Entities entities, OverworldFurnitureEntry[]? entries)
    {
        if (entries is null)
            return;
        RequireCount(entries.Length, entities.Furniture.Length, "muebles");
        for (var index = 0; index < entries.Length; index++)
        {
            var at = entities.FurnitureOffset + (index * Gen6FurnitureSize);
            WriteU16(data, at, entries[index].Script, "script del mueble");
            WriteU16(data, at + 0x08, entries[index].X, "X del mueble");
            WriteU16(data, at + 0x0A, entries[index].Y, "Y del mueble");
            WriteI16(data, at + 0x0C, entries[index].Width, "ancho del mueble");
            WriteI16(data, at + 0x0E, entries[index].Height, "alto del mueble");
        }
    }

    private static OverworldGen6ZoneMetadata? ReadGen6ZoneMetadata(byte[] data)
    {
        if (data is null || data.Length < 0x20)
            return null;
        var movementFlags = data.Length >= 0x24 ? BitConverter.ToUInt32(data, 0x20) : 0u;
        var cameraFlags = data.Length >= 0x2A ? BitConverter.ToUInt32(data, 0x26) : 0u;
        return new OverworldGen6ZoneMetadata(
            ReadU16(data, 0x02), ReadU16(data, 0x04), ReadU16(data, 0x06),
            ReadU16(data, 0x18), ReadU16(data, 0x1C) & 0x3FF, ReadU16(data, 0x1E) & 0x1F,
            MapType: data[0], MapMove: data[1],
            BgmSpring: data.Length >= 0x0C ? ReadU32(data, 0x08) : null,
            BgmSummer: data.Length >= 0x10 ? ReadU32(data, 0x0C) : null,
            BgmAutumn: data.Length >= 0x14 ? ReadU32(data, 0x10) : null,
            BgmWinter: data.Length >= 0x18 ? ReadU32(data, 0x14) : null,
            TownMapGroup: data.Length >= 0x1C ? ReadU16(data, 0x1A) : null,
            OlValue: ReadU16(data, 0x1C) >> 10,
            SkyBoxEnabled: (data[0x1E] & 0x20) != 0,
            RollerSkateEnabled: (data[0x1E] & 0x40) != 0,
            BattleBackground: (ReadU16(data, 0x1E) >> 7) & 0x7F,
            MapChange: (int)(movementFlags & 0x1F),
            BicycleEnabled: (movementFlags & (1u << 10)) != 0,
            RunEnabled: (movementFlags & (1u << 11)) != 0,
            EscapeRopeEnabled: (movementFlags & (1u << 12)) != 0,
            FlyEnabled: (movementFlags & (1u << 13)) != 0,
            BgmEnabled: (movementFlags & (1u << 14)) != 0,
            UnknownFlag: (movementFlags & (1u << 15)) != 0,
            Camera1: data.Length >= 0x24 ? ReadU16(data, 0x22) : null,
            Camera2: data.Length >= 0x26 ? ReadU16(data, 0x24) : null,
            CameraFlags: data.Length >= 0x2A ? cameraFlags : null,
            StartX: data.Length >= 0x30 ? ReadI16(data, 0x2C) / 18f : null,
            StartY: data.Length >= 0x34 ? ReadI16(data, 0x30) / 18f : null,
            StartZ: data.Length >= 0x30 ? ReadI16(data, 0x2E) : null,
            EndX: data.Length >= 0x34 ? ReadI16(data, 0x32) / 18f : null,
            EndY: data.Length >= 0x38 ? ReadI16(data, 0x36) / 18f : null,
            EndZ: data.Length >= 0x36 ? ReadI16(data, 0x34) : null);
    }

    private static void ApplyZoneMetadata(byte[] data, OverworldGen6ZoneMetadata? metadata)
    {
        if (metadata is null)
            return;
        if (data is null || data.Length < 0x20)
            throw new WorkspaceException("El bloque de datos de zona Gen. VI no tiene los offsets editables.");

        WriteU16(data, 0x02, metadata.MapArea, "área de mapa");
        WriteU16(data, 0x04, metadata.MapMatrix, "matriz de mapa");
        WriteU16(data, 0x06, metadata.TextFile, "archivo de texto");
        WriteU16(data, 0x18, metadata.ScriptFile, "archivo de script");
        if (metadata.ParentMap is < 0 or > 0x3FF)
            throw new WorkspaceException("El mapa padre debe estar entre 0 y 1023.");
        if (metadata.Weather is < 0 or > 0x1F)
            throw new WorkspaceException("El clima debe estar entre 0 y 31.");
        var parent = ReadU16(data, 0x1C);
        BitConverter.GetBytes((ushort)((parent & ~0x3FF) | metadata.ParentMap)).CopyTo(data, 0x1C);
        var weather = ReadU16(data, 0x1E);
        BitConverter.GetBytes((ushort)((weather & ~0x1F) | metadata.Weather)).CopyTo(data, 0x1E);

        if (metadata.MapType is { } mapType)
            WriteByte(data, 0x00, mapType, "tipo de mapa");
        if (metadata.MapMove is { } mapMove)
            WriteByte(data, 0x01, mapMove, "movimiento de mapa");
        if (metadata.BgmSpring is { } bgmSpring)
            WriteU32(data, 0x08, bgmSpring, "BGM de primavera");
        if (metadata.BgmSummer is { } bgmSummer)
            WriteU32(data, 0x0C, bgmSummer, "BGM de verano");
        if (metadata.BgmAutumn is { } bgmAutumn)
            WriteU32(data, 0x10, bgmAutumn, "BGM de otoño");
        if (metadata.BgmWinter is { } bgmWinter)
            WriteU32(data, 0x14, bgmWinter, "BGM de invierno");
        if (metadata.TownMapGroup is { } townMapGroup)
            WriteU16(data, 0x1A, townMapGroup, "grupo de town map");

        var packedFlags = ReadU16(data, 0x1C);
        if (metadata.OlValue is { } olValue)
        {
            if (olValue is < 0 or > 0x3F)
                throw new WorkspaceException("El valor OL debe estar entre 0 y 63.");
            packedFlags = (packedFlags & 0x03FF) | (olValue << 10);
            BitConverter.GetBytes((ushort)packedFlags).CopyTo(data, 0x1C);
        }

        var zoneFlags = (ushort)ReadU16(data, 0x1E);
        SetFlag(ref zoneFlags, 5, metadata.SkyBoxEnabled);
        SetFlag(ref zoneFlags, 6, metadata.RollerSkateEnabled);
        if (metadata.BattleBackground is { } battleBackground)
        {
            if (battleBackground is < 0 or > 0x7F)
                throw new WorkspaceException("El fondo de batalla debe estar entre 0 y 127.");
            zoneFlags = (ushort)((zoneFlags & ~0x3F80) | (battleBackground << 7));
        }
        BitConverter.GetBytes(zoneFlags).CopyTo(data, 0x1E);

        var movementFlags = data.Length >= 0x24 ? ReadU32(data, 0x20) : 0u;
        if (metadata.MapChange is { } mapChange)
        {
            if (mapChange is < 0 or > 0x1F)
                throw new WorkspaceException("El cambio de mapa debe estar entre 0 y 31.");
            movementFlags = (movementFlags & ~0x1Fu) | (uint)mapChange;
        }
        SetFlag(ref movementFlags, 10, metadata.BicycleEnabled);
        SetFlag(ref movementFlags, 11, metadata.RunEnabled);
        SetFlag(ref movementFlags, 12, metadata.EscapeRopeEnabled);
        SetFlag(ref movementFlags, 13, metadata.FlyEnabled);
        SetFlag(ref movementFlags, 14, metadata.BgmEnabled);
        SetFlag(ref movementFlags, 15, metadata.UnknownFlag);
        if (data.Length >= 0x24)
            WriteU32(data, 0x20, movementFlags, "flags de movimiento");

        if (metadata.Camera1 is { } camera1)
            WriteU16(data, 0x22, camera1, "cámara 1");
        if (metadata.Camera2 is { } camera2)
            WriteU16(data, 0x24, camera2, "cámara 2");
        if (metadata.CameraFlags is { } cameraFlags)
            WriteU32(data, 0x26, cameraFlags, "flags de cámara");
        WriteScaledCoordinate(data, 0x2C, metadata.StartX, "X inicial");
        if (metadata.StartZ is { } startZ)
            WriteI16(data, 0x2E, startZ, "Z inicial");
        WriteScaledCoordinate(data, 0x30, metadata.StartY, "Y inicial");
        WriteScaledCoordinate(data, 0x32, metadata.EndX, "X final");
        if (metadata.EndZ is { } endZ)
            WriteI16(data, 0x34, endZ, "Z final");
        WriteScaledCoordinate(data, 0x36, metadata.EndY, "Y final");
    }

    private static void ApplyNpcs(byte[] data, Gen6Entities entities, OverworldNpcEntry[]? entries)
    {
        if (entries is null)
            return;
        RequireCount(entries.Length, entities.Npcs.Length, "NPC");
        for (var index = 0; index < entries.Length; index++)
        {
            var at = entities.NpcOffset + (index * Gen6NpcSize);
            var entry = entries[index];
            WriteU16(data, at, entry.Id, "ID del NPC");
            WriteU16(data, at + 0x02, entry.Model, "modelo del NPC");
            WriteU16(data, at + 0x04, entry.MovePermissions, "movimiento del NPC");
            WriteU16(data, at + 0x06, entry.MovePermissions2, "movimiento secundario del NPC");
            WriteU16(data, at + 0x08, entry.SpawnFlag, "flag de aparición del NPC");
            WriteU16(data, at + 0x0A, entry.Script, "script del NPC");
            WriteU16(data, at + 0x0C, entry.FaceDirection, "dirección del NPC");
            WriteU16(data, at + 0x0E, entry.SightRange, "rango de visión del NPC");
            WriteU16(data, at + 0x28, entry.X, "X del NPC");
            WriteU16(data, at + 0x2A, entry.Y, "Y del NPC");
        }
    }

    private static void ApplyWarps(byte[] data, Gen6Entities entities, OverworldWarpEntry[]? entries)
    {
        if (entries is null)
            return;
        RequireCount(entries.Length, entities.Warps.Length, "warps");
        for (var index = 0; index < entries.Length; index++)
        {
            var at = entities.WarpOffset + (index * Gen6WarpSize);
            var entry = entries[index];
            WriteU16(data, at, entry.DestinationMap, "mapa destino del warp");
            WriteU16(data, at + 0x02, entry.DestinationTileIndex, "tile destino del warp");
            WriteI16(data, at + 0x08, entry.X, "X del warp");
            WriteI16(data, at + 0x0C, entry.Y, "Y del warp");
        }
    }

    private static void ApplyTriggers(byte[] data, Gen6Entities entities, OverworldTriggerEntry[]? entries,
        int offset, string label)
    {
        if (entries is null)
            return;
        var count = label == "triggers" ? entities.Triggers.Length : entities.UnknownTriggers.Length;
        RequireCount(entries.Length, count, label);
        for (var index = 0; index < entries.Length; index++)
        {
            var at = offset + (index * Gen6TriggerSize);
            var entry = entries[index];
            WriteU16(data, at, entry.Script, $"script de {label}");
            WriteU16(data, at + 0x04, entry.Constant, $"constante de {label}");
            WriteU16(data, at + 0x06, entry.Type, $"tipo de {label}");
            WriteU16(data, at + 0x08, entry.Flags, $"flags de {label}");
            WriteU16(data, at + 0x0C, entry.X, $"X de {label}");
            WriteU16(data, at + 0x0E, entry.Y, $"Y de {label}");
            WriteI16(data, at + 0x10, entry.Width, $"ancho de {label}");
            WriteI16(data, at + 0x12, entry.Height, $"alto de {label}");
        }
    }

    private static void RequireCount(int actual, int expected, string label)
    {
        if (actual != expected)
            throw new WorkspaceException($"La cantidad de {label} no coincide con la zona original ({expected}).");
    }

    private static int ReadU16(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);
    private static int ReadI16(byte[] data, int offset) => BitConverter.ToInt16(data, offset);
    private static uint ReadU32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);

    private static void WriteByte(byte[] data, int offset, int value, string label)
    {
        if (value is < 0 or > byte.MaxValue)
            throw new WorkspaceException($"El valor de {label} debe estar entre 0 y 255.");
        data[offset] = (byte)value;
    }

    private static void WriteU16(byte[] data, int offset, int value, string label)
    {
        if (value is < 0 or > ushort.MaxValue)
            throw new WorkspaceException($"El valor de {label} debe estar entre 0 y 65535.");
        BitConverter.GetBytes((ushort)value).CopyTo(data, offset);
    }

    private static void WriteI16(byte[] data, int offset, int value, string label)
    {
        if (value is < short.MinValue or > short.MaxValue)
            throw new WorkspaceException($"El valor de {label} debe estar entre -32768 y 32767.");
        BitConverter.GetBytes((short)value).CopyTo(data, offset);
    }

    private static void WriteU32(byte[] data, int offset, uint value, string label)
    {
        if (offset < 0 || offset + sizeof(uint) > data.Length)
            throw new WorkspaceException($"El campo de {label} sale del bloque de zona.");
        BitConverter.GetBytes(value).CopyTo(data, offset);
    }

    private static void SetFlag(ref ushort value, int bit, bool? state)
    {
        if (state is null)
            return;
        var mask = (ushort)(1 << bit);
        value = state.Value ? (ushort)(value | mask) : (ushort)(value & ~mask);
    }

    private static void SetFlag(ref uint value, int bit, bool? state)
    {
        if (state is null)
            return;
        var mask = 1u << bit;
        value = state.Value ? value | mask : value & ~mask;
    }

    private static void WriteScaledCoordinate(byte[] data, int offset, float? value, string label)
    {
        if (value is null)
            return;
        if (!float.IsFinite(value.Value))
            throw new WorkspaceException($"La coordenada {label} debe ser finita.");
        var scaled = MathF.Truncate(value.Value * 18f);
        if (scaled < short.MinValue || scaled > short.MaxValue)
            throw new WorkspaceException($"La coordenada {label} sale del rango del formato.");
        WriteI16(data, offset, (int)scaled, label);
    }

    private sealed record Gen6Entities(
        int FurnitureOffset, int NpcOffset, int WarpOffset, int TriggerOffset, int UnknownTriggerOffset,
        int ScriptOffset,
        OverworldFurnitureEntry[] Furniture, OverworldNpcEntry[] Npcs, OverworldWarpEntry[] Warps,
        OverworldTriggerEntry[] Triggers, OverworldTriggerEntry[] UnknownTriggers, byte[] Script);

    private static string GetGen6LocationName(byte[] masterZoneData, string[] locations, int zoneIndex)
    {
        if (zoneIndex < 0 || masterZoneData is null || locations is null)
            return $"Área {zoneIndex:000}";

        var offset = (long)zoneIndex * Gen6ZoneDataSize;
        if (offset < 0 || offset + 0x1E > masterZoneData.Length)
            return $"Área {zoneIndex:000}";

        var parentMap = BitConverter.ToUInt16(masterZoneData, (int)offset + 0x1C) & 0x3FF;
        var location = parentMap < locations.Length ? locations[parentMap] : string.Empty;
        return string.IsNullOrWhiteSpace(location)
            ? $"Área {zoneIndex:000}"
            : $"{zoneIndex:000} · {location}";
    }

    private static void AddGroup(
        ICollection<OverworldScriptGroupSummary> groups,
        LazyGARCFile encounterData,
        byte[][] zoneFiles,
        string[] locations,
        int worldIndex,
        string group,
        int offset,
        string identifier)
    {
        var index = (worldIndex * FilesPerWorld) + offset;
        var scripts = ReadMiniOrEmpty(encounterData[index], identifier);
        if (scripts.Length == 0)
            return;

        var locationName = GetLocationName(zoneFiles, locations, worldIndex);
        groups.Add(new OverworldScriptGroupSummary(
            group,
            group == ZoneScriptGroup ? $"Scripts de zona · {locationName}" : $"Información de zona · {locationName}",
            worldIndex,
            locationName,
            scripts.Length,
            scripts.Sum(script => (long)script.Length)));
    }

    private static (int Offset, string Identifier) GroupFormat(string group) => group switch
    {
        ZoneScriptGroup => (ZoneScriptOffset, ZoneScriptIdentifier),
        ZoneInfoGroup => (ZoneInfoOffset, ZoneInfoIdentifier),
        _ => throw new WorkspaceException("El grupo OWSE indicado no es compatible. Usá zone-script o zone-info."),
    };

    private static OverworldZoneSummary GetGen6ZoneSummary(byte[][] zone, int zoneIndex)
    {
        var zoneData = zone.Length > 0 ? zone[0] ?? [] : [];
        var diagnostics = (string?)null;
        int? parentMap = null;
        int? mapArea = null;
        int? mapMatrix = null;
        int? textFile = null;
        int? scriptFile = null;
        int? weather = null;

        if (zoneData.Length >= Gen6ZoneDataSize)
        {
            mapArea = BitConverter.ToUInt16(zoneData, 0x02);
            mapMatrix = BitConverter.ToUInt16(zoneData, 0x04);
            textFile = BitConverter.ToUInt16(zoneData, 0x06);
            scriptFile = BitConverter.ToUInt16(zoneData, 0x18);
            parentMap = BitConverter.ToUInt16(zoneData, 0x1C) & 0x3FF;
            weather = BitConverter.ToUInt16(zoneData, 0x1E) & 0x1F;
        }
        else
        {
            diagnostics = "El bloque de datos de zona Gen. VI es menor que 0x38 bytes.";
        }

        int? furniture = null;
        int? npcs = null;
        int? warps = null;
        int? triggers = null;
        int? unknown = null;
        var entities = zone.Length > 1 ? zone[1] ?? [] : [];
        if (entities.Length >= 12)
        {
            furniture = entities[4];
            npcs = entities[5];
            warps = entities[6];
            triggers = entities[7];
            var declaredUnknown = BitConverter.ToInt32(entities, 8);
            if (declaredUnknown >= 0)
                unknown = declaredUnknown;
            else
                diagnostics = AppendError(diagnostics, "La cabecera de entidades declara una cantidad negativa.");
        }
        else
        {
            diagnostics = AppendError(diagnostics, "El bloque de entidades Gen. VI no tiene una cabecera completa.");
        }

        return new OverworldZoneSummary(
            zoneIndex, zoneData.Length, zone.Length, parentMap, mapArea, mapMatrix, textFile, scriptFile,
            weather, FurnitureCount: furniture, NpcCount: npcs, WarpCount: warps,
            TriggerCount: triggers, UnknownEntityCount: unknown, Diagnostics: diagnostics);
    }

    private static OverworldZoneSummary GetGen7ZoneSummary(byte[][] zoneFiles, byte[][] encounterFiles, int worldIndex)
    {
        var zoneData = zoneFiles.Length > 0 ? zoneFiles[0] ?? [] : [];
        var zoneIndex = FindGen7ZoneIndex(zoneFiles, worldIndex);
        if (zoneIndex < 0)
        {
            return new OverworldZoneSummary(
                -1, 0, FilesPerWorld, Diagnostics: "No se encontró un registro de zona Gen. VII para este mundo.");
        }

        var offset = (long)zoneIndex * ZoneData7.SIZE;
        if (offset < 0 || offset + 0x20 > zoneData.Length)
        {
            var available = offset >= 0 && offset < zoneData.Length
                ? zoneData.Length - (int)offset
                : 0;
            return new OverworldZoneSummary(
                zoneIndex, available, FilesPerWorld,
                Diagnostics: "La tabla zonedata Gen. VII no alcanza el registro de zona.");
        }

        var diagnostics = (string?)null;
        var entityBlocks = ReadGen7EntityBlocks(encounterFiles, worldIndex, ref diagnostics);
        return new OverworldZoneSummary(
            zoneIndex, ZoneData7.SIZE, FilesPerWorld,
            ParentMap: BitConverter.ToInt32(zoneData, (int)offset + 0x1C),
            Diagnostics: diagnostics, EntityBlocks: entityBlocks);
    }

    private static OverworldGen7EntityBlockSummary[]? ReadGen7EntityBlocks(
        byte[][] encounterFiles, int worldIndex, ref string? diagnostics)
    {
        var fileIndex = (long)worldIndex * FilesPerWorld + Gen7EntityOffset;
        if (worldIndex < 0 || fileIndex < 0 || fileIndex >= encounterFiles.Length)
        {
            diagnostics = AppendError(diagnostics, "No se encontró el bloque ED de entidades Gen. VII.");
            return null;
        }

        var data = encounterFiles[(int)fileIndex] ?? [];
        if (data.Length == 0)
            return [];

        byte[][] blocks;
        try
        {
            blocks = ReadMini(data, Gen7EntityIdentifier);
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque ED no se pudo describir: {exception.Message}");
            return null;
        }

        var summaries = new List<OverworldGen7EntityBlockSummary>(blocks.Length);
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            var block = blocks[blockIndex];
            var identifier = block.Length >= 2
                ? new string([(char)block[0], (char)block[1]])
                : "??";
            var isMiniArchive = IsMiniArchive(block);
            var entryCount = isMiniArchive ? BitConverter.ToUInt16(block, 2) : 0;
            OverworldGen7EntityEntrySummary[]? entries = null;
            if (isMiniArchive)
            {
                try
                {
                    entries = ReadMini(block, identifier)
                        .Select((entry, entryIndex) => DescribeGen7EntityEntry(identifier, entry, entryIndex))
                        .ToArray();
                }
                catch (WorkspaceException exception)
                {
                    diagnostics = AppendError(diagnostics,
                        $"El bloque {identifier} no se pudo detallar: {exception.Message}");
                }
            }

            summaries.Add(new OverworldGen7EntityBlockSummary(
                identifier, block.Length, entryCount, isMiniArchive, entries, blockIndex));
        }

        return summaries.ToArray();
    }

    private static OverworldGen7EntityEntrySummary DescribeGen7EntityEntry(
        string identifier, byte[] entry, int entryIndex)
    {
        var recordCount = entry.Length >= sizeof(int) ? BitConverter.ToInt32(entry, 0) : (int?)null;
        var recordKind = identifier == "EP" || entry.Length < (2 * sizeof(int))
            ? (int?)null
            : BitConverter.ToInt32(entry, sizeof(int));

        var (schema, stride, positionOffset) = GetGen7EntitySchema(identifier, recordKind, entry, recordCount);
        return new OverworldGen7EntityEntrySummary(
            entryIndex, entry.Length, recordCount, recordKind, HexPreview(entry),
            schema, stride, positionOffset);
    }

    private static (string? Schema, int? RecordStride, int? PositionOffset) GetGen7EntitySchema(
        string identifier, int? recordKind, byte[] entry, int? recordCount)
    {
        if (entry.Length == 0)
            return ("vacía", null, null);

        if (identifier == "EP")
            return HasRecordRange(entry, recordCount, Gen7EntityPositionOffset, Gen7EntityRecordSize)
                ? ("EP primaria", Gen7EntityRecordSize, Gen7EntityPositionOffset)
                : ("EP: cabecera o stride no confirmado", null, null);

        if (identifier == "EM" && recordKind == Gen7EmRecordKind)
            return HasRecordRange(entry, recordCount, Gen7EmPositionOffset, Gen7EmRecordSize)
                ? ("EM principal", Gen7EmRecordSize, Gen7EmPositionOffset)
                : ("EM tipo 1: rango no confirmado", null, null);

        if (identifier == "EM" && recordKind == 3)
            return ("EM tipo 3: tabla anidada variable no confirmada", null, null);

        if (identifier == "EI" && recordKind == Gen7EiRecordKind)
            return HasGen7EiRecordRange(entry, recordCount)
                ? ("EI tipo 10", Gen7EiRecordSize, Gen7EiPositionOffset)
                : ("EI tipo 10: rango no confirmado", null, null);

        if (identifier == "EB" && recordKind == Gen7EbRecordKind)
            return HasRecordRange(entry, recordCount, Gen7EbPositionOffset, Gen7EbRecordSize)
                ? ("EB tipo 2", Gen7EbRecordSize, Gen7EbPositionOffset)
                : ("EB tipo 2: rango no confirmado", null, null);

        if (identifier == "ES" && recordKind == Gen7EsRecordKind)
            return HasRecordRange(entry, recordCount, Gen7EsPositionOffset, Gen7EsRecordSize)
                ? ("ES tipo 4", Gen7EsRecordSize, Gen7EsPositionOffset)
                : ("ES tipo 4: rango no confirmado", null, null);

        if (identifier == "ES")
            return ("ES: variante corta o tipo no confirmado", null, null);

        if (identifier == "EA" && recordKind == Gen7EaRecordKind)
            return HasRecordRange(entry, recordCount, Gen7EaPositionOffset, Gen7EaRecordSize)
                ? ("EA tipo 5", Gen7EaRecordSize, Gen7EaPositionOffset)
                : ("EA tipo 5: rango no confirmado", null, null);

        if (identifier == "EA" && recordKind == Gen7EaKind6RecordKind)
            return recordCount is not null && TryGetGen7EaKind6PayloadOffsets(entry, recordCount.Value, out _)
                ? ("EA tipo 6", Gen7EaKind6PayloadRecordSize, Gen7EaKind6PayloadPositionOffset)
                : ("EA tipo 6: rango no confirmado", null, null);

        if (identifier == "ET" && recordKind == Gen7EtRecordKind)
            return HasRecordRange(entry, recordCount, Gen7EtPositionOffset, Gen7EtRecordSize)
                ? ("ET tipo 7", Gen7EtRecordSize, Gen7EtPositionOffset)
                : ("ET tipo 7: rango no confirmado", null, null);

        if (identifier == "ET" && recordKind == 9)
            return recordCount is not null &&
                   TryGetGen7EtKind9PositionOffsets(entry, recordCount.Value, out _)
                ? ("ET tipo 9 (tabla de puntos)", Gen7EtKind9PointSize, Gen7EtKind9PointHeaderSize)
                : ("ET tipo 9: esquema variable no confirmado", null, null);

        if (identifier == "PR" && recordKind is Gen7PrKind203 or Gen7PrKind204)
            return recordCount == 1 && entry.Length >= Gen7PrPositionOffset + (sizeof(float) * 3)
                ? ($"PR tipo {recordKind}", null, Gen7PrPositionOffset)
                : ($"PR tipo {recordKind}: rango no confirmado", null, null);

        if (identifier == "FS" && recordKind == 12)
            return ("FS tipo 12: estructura interna variable no confirmada", null, null);

        if (identifier == "FS" && recordKind == 13)
            return ("FS tipo 13: estructura variable no confirmada", null, null);

        return ($"{identifier} tipo {recordKind?.ToString() ?? "desconocido"}: sin esquema confirmado", null, null);
    }

    private static bool HasRecordRange(byte[] entry, int? recordCount, int positionOffset, int recordStride)
    {
        if (recordCount is null or <= 0 or > 4096)
            return false;
        return (long)positionOffset + (recordCount.Value * recordStride) <= entry.Length;
    }

    private static bool HasGen7EiRecordRange(byte[] entry, int? recordCount)
    {
        if (recordCount is null or <= 0 or > 4096)
            return false;

        var recordsEnd = 4L + (recordCount.Value * Gen7EiRecordSize);
        if (recordsEnd > entry.Length)
            return false;

        // The retail entry stores the kind marker in the first record. Subsequent records
        // continue at the same stride but their first field is payload data, not another kind.
        return BitConverter.ToInt32(entry, sizeof(int)) == Gen7EiRecordKind;
    }

    private static string? HexPreview(byte[] data)
    {
        if (data is null || data.Length == 0)
            return null;

        var length = Math.Min(data.Length, 32);
        var preview = Convert.ToHexString(data, 0, length);
        return string.Join(' ', Enumerable.Range(0, (length + 1) / 2)
            .Select(index => preview.Substring(index * 2, 2)))
            + (data.Length > length ? " …" : string.Empty);
    }

    private static string ResolveRawEntityOutputDirectory(GameWorkspace workspace, string? requested, int worldIndex)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(workspace.RootPath, "owse-gen7-ed-export")
            : Path.GetFullPath(requested.Trim());
        if (File.Exists(baseDirectory))
            throw new WorkspaceException("La salida del diagnóstico ED ya existe como archivo.");
        if (IsInside(baseDirectory, workspace.RomFsPath)
            || (workspace.ExeFsPath is not null && IsInside(baseDirectory, workspace.ExeFsPath)))
            throw new WorkspaceException("La exportación ED no puede guardarse dentro del RomFS ni del ExeFS de origen.");

        Directory.CreateDirectory(baseDirectory);
        var name = $"world-{worldIndex:D3}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var output = Path.Combine(baseDirectory, name);
        if (Directory.Exists(output))
            output = Path.Combine(baseDirectory, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        return output;
    }

    private static bool IsInside(string candidate, string source)
    {
        var root = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.Ordinal);
    }

    private static OverworldGen7PositionEntry[] ReadGen7EntityPositions(
        byte[][] encounterFiles, int worldIndex, ref string? diagnostics)
    {
        var fileIndex = (long)worldIndex * FilesPerWorld + Gen7EntityOffset;
        if (worldIndex < 0 || fileIndex < 0 || fileIndex >= encounterFiles.Length)
            return [];

        var data = encounterFiles[(int)fileIndex] ?? [];
        if (data.Length == 0)
            return [];

        byte[][] blocks;
        try
        {
            blocks = ReadMini(data, Gen7EntityIdentifier);
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque ED no se pudo leer: {exception.Message}");
            return [];
        }

        var epIndex = FindMiniBlock(blocks, "EP");
        if (epIndex < 0)
            return [];

        byte[][] entries;
        try
        {
            entries = ReadMini(blocks[epIndex], "EP");
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque EP no se pudo leer: {exception.Message}");
            return [];
        }

        var positions = new List<OverworldGen7PositionEntry>();
        for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
        {
            var entry = entries[containerEntry];
            if (entry.Length < Gen7EntityPositionOffset)
                continue;

            var recordCount = BitConverter.ToInt32(entry, 0);
            var recordsEnd = (long)Gen7EntityPositionOffset + (recordCount * Gen7EntityRecordSize);
            if (recordCount <= 0)
                continue;
            if (recordCount > 4096 || recordsEnd > entry.Length)
            {
                diagnostics = AppendError(diagnostics,
                    $"El contenedor EP {containerEntry} declara registros fuera de rango.");
                continue;
            }

            for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                var offset = Gen7EntityPositionOffset + (recordIndex * Gen7EntityRecordSize);
                positions.Add(new OverworldGen7PositionEntry(
                    containerEntry, recordIndex,
                    BitConverter.ToSingle(entry, offset),
                    BitConverter.ToSingle(entry, offset + 4),
                    BitConverter.ToSingle(entry, offset + 8)));
            }
        }
        return positions.ToArray();
    }

    private static void ApplyGen7EntityPositions(byte[][] entries, OverworldGen7PositionEntry[] positions)
    {
        var expected = ReadGen7EntityPositionsFromEntries(entries);
        if (positions.Length != expected.Count)
            throw new WorkspaceException($"La cantidad de posiciones debe conservarse en {expected.Count} registros.");

        var expectedKeys = expected.Select(position => (position.ContainerEntry, position.RecordIndex)).ToHashSet();
        var requestedKeys = new HashSet<(int ContainerEntry, int RecordIndex)>();
        foreach (var position in positions)
        {
            if (!expectedKeys.Contains((position.ContainerEntry, position.RecordIndex)) ||
                !requestedKeys.Add((position.ContainerEntry, position.RecordIndex)))
                throw new WorkspaceException("La lista de posiciones EP no coincide con los registros originales.");
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
                Math.Abs(position.X) > 1_000_000 || Math.Abs(position.Y) > 1_000_000 || Math.Abs(position.Z) > 1_000_000)
                throw new WorkspaceException("Las coordenadas EP deben ser números finitos entre -1000000 y 1000000.");

            var entry = entries[position.ContainerEntry];
            var offset = Gen7EntityPositionOffset + (position.RecordIndex * Gen7EntityRecordSize);
            BitConverter.GetBytes(position.X).CopyTo(entry, offset);
            BitConverter.GetBytes(position.Y).CopyTo(entry, offset + 4);
            BitConverter.GetBytes(position.Z).CopyTo(entry, offset + 8);
        }
    }

    /// <summary>
    /// Reads the stable primary EM records. The secondary EM schema varies between zones, so it
    /// remains diagnostic-only; primary records have a fixed 0x78-byte stride and preserve all
    /// fields other than their confirmed position vector.
    /// </summary>
    private static OverworldGen7EmPositionEntry[] ReadGen7EmPositions(
        byte[][] encounterFiles, int worldIndex, ref string? diagnostics)
    {
        var fileIndex = (long)worldIndex * FilesPerWorld + Gen7EntityOffset;
        if (worldIndex < 0 || fileIndex < 0 || fileIndex >= encounterFiles.Length)
            return [];

        var data = encounterFiles[(int)fileIndex] ?? [];
        if (data.Length == 0)
            return [];

        byte[][] blocks;
        try
        {
            blocks = ReadMini(data, Gen7EntityIdentifier);
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque ED no se pudo leer: {exception.Message}");
            return [];
        }

        var emIndex = FindMiniBlock(blocks, "EM");
        if (emIndex < 0)
            return [];

        byte[][] entries;
        try
        {
            entries = ReadMini(blocks[emIndex], "EM");
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque EM no se pudo leer: {exception.Message}");
            return [];
        }

        var positions = new List<OverworldGen7EmPositionEntry>();
        for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
        {
            var entry = entries[containerEntry];
            if (entry.Length < Gen7EmPositionOffset + sizeof(int))
                continue;

            var recordCount = BitConverter.ToInt32(entry, 0);
            var recordKind = BitConverter.ToInt32(entry, sizeof(int));
            if (recordCount <= 0 || recordKind != Gen7EmRecordKind)
                continue;

            var recordsEnd = (long)Gen7EmPositionOffset + (recordCount * Gen7EmRecordSize);
            if (recordCount > 4096 || recordsEnd > entry.Length)
            {
                diagnostics = AppendError(diagnostics,
                    $"El contenedor EM {containerEntry} declara registros fuera de rango.");
                continue;
            }

            for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                var offset = Gen7EmPositionOffset + (recordIndex * Gen7EmRecordSize);
                positions.Add(new OverworldGen7EmPositionEntry(
                    containerEntry, recordIndex,
                    BitConverter.ToSingle(entry, offset),
                    BitConverter.ToSingle(entry, offset + 4),
                    BitConverter.ToSingle(entry, offset + 8)));
            }
        }
        return positions.ToArray();
    }

    private static void ApplyGen7EmPositions(byte[][] entries, OverworldGen7EmPositionEntry[] positions)
    {
        var expected = ReadGen7EmPositionsFromEntries(entries);
        if (positions.Length != expected.Count)
            throw new WorkspaceException($"La cantidad de posiciones EM debe conservarse en {expected.Count} registros.");

        var expectedKeys = expected.Select(position => (position.ContainerEntry, position.RecordIndex)).ToHashSet();
        var requestedKeys = new HashSet<(int ContainerEntry, int RecordIndex)>();
        foreach (var position in positions)
        {
            if (!expectedKeys.Contains((position.ContainerEntry, position.RecordIndex)) ||
                !requestedKeys.Add((position.ContainerEntry, position.RecordIndex)))
                throw new WorkspaceException("La lista de posiciones EM no coincide con los registros originales.");
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
                Math.Abs(position.X) > 1_000_000 || Math.Abs(position.Y) > 1_000_000 || Math.Abs(position.Z) > 1_000_000)
                throw new WorkspaceException("Las coordenadas EM deben ser números finitos entre -1000000 y 1000000.");

            var entry = entries[position.ContainerEntry];
            var offset = Gen7EmPositionOffset + (position.RecordIndex * Gen7EmRecordSize);
            BitConverter.GetBytes(position.X).CopyTo(entry, offset);
            BitConverter.GetBytes(position.Y).CopyTo(entry, offset + 4);
            BitConverter.GetBytes(position.Z).CopyTo(entry, offset + 8);
        }
    }

    private static List<OverworldGen7EmPositionEntry> ReadGen7EmPositionsFromEntries(byte[][] entries)
    {
        var positions = new List<OverworldGen7EmPositionEntry>();
        for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
        {
            var entry = entries[containerEntry];
            if (entry.Length < Gen7EmPositionOffset + sizeof(int))
                continue;

            var recordCount = BitConverter.ToInt32(entry, 0);
            var recordKind = BitConverter.ToInt32(entry, sizeof(int));
            var recordsEnd = (long)Gen7EmPositionOffset + (recordCount * Gen7EmRecordSize);
            if (recordCount <= 0 || recordKind != Gen7EmRecordKind || recordCount > 4096 || recordsEnd > entry.Length)
                continue;

            for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                positions.Add(new OverworldGen7EmPositionEntry(containerEntry, recordIndex, 0, 0, 0));
        }
        return positions;
    }

    /// <summary>
    /// Reads the confirmed EI type-10 records. Real UM entries use a four-byte count followed by
    /// fixed 0x5C-byte records; the first record carries kind 10 and every position vector starts
    /// at record offset 0x04 (entry offset 0x08). All trailing bytes remain untouched by export.
    /// </summary>
    private static OverworldGen7EiPositionEntry[] ReadGen7EiPositions(
        byte[][] encounterFiles, int worldIndex, ref string? diagnostics)
    {
        var fileIndex = (long)worldIndex * FilesPerWorld + Gen7EntityOffset;
        if (worldIndex < 0 || fileIndex < 0 || fileIndex >= encounterFiles.Length)
            return [];

        var data = encounterFiles[(int)fileIndex] ?? [];
        if (data.Length == 0)
            return [];

        byte[][] blocks;
        try
        {
            blocks = ReadMini(data, Gen7EntityIdentifier);
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque ED no se pudo leer: {exception.Message}");
            return [];
        }

        var positions = new List<OverworldGen7EiPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "EI"))
                continue;

            byte[][] entries;
            try
            {
                entries = ReadMini(blocks[blockIndex], "EI");
            }
            catch (WorkspaceException exception)
            {
                diagnostics = AppendError(diagnostics,
                    $"El bloque EI {blockIndex} no se pudo leer: {exception.Message}");
                continue;
            }

            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (entry.Length < sizeof(int))
                    continue;

                var recordCount = BitConverter.ToInt32(entry, 0);
                if (recordCount <= 0)
                    continue;

                if (!HasGen7EiRecordRange(entry, recordCount))
                {
                    diagnostics = AppendError(diagnostics,
                        $"El contenedor EI {blockIndex}/{containerEntry} declara registros fuera de rango.");
                    continue;
                }

                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    var offset = 4 + (recordIndex * Gen7EiRecordSize) + Gen7EiPositionOffset - 4;
                    positions.Add(new OverworldGen7EiPositionEntry(
                        blockIndex, containerEntry, recordIndex,
                        BitConverter.ToSingle(entry, offset),
                        BitConverter.ToSingle(entry, offset + 4),
                        BitConverter.ToSingle(entry, offset + 8)));
                }
            }
        }
        return positions.ToArray();
    }

    private static void ApplyGen7EiPositions(byte[][] blocks, OverworldGen7EiPositionEntry[] positions)
    {
        var expected = ReadGen7EiPositionsFromBlocks(blocks);
        if (positions.Length != expected.Count)
            throw new WorkspaceException($"La cantidad de posiciones EI debe conservarse en {expected.Count} registros.");

        var expectedKeys = expected
            .Select(position => (position.BlockEntry, position.ContainerEntry, position.RecordIndex))
            .ToHashSet();
        var requestedKeys = new HashSet<(int BlockEntry, int ContainerEntry, int RecordIndex)>();
        foreach (var position in positions)
        {
            var key = (position.BlockEntry, position.ContainerEntry, position.RecordIndex);
            if (!expectedKeys.Contains(key) || !requestedKeys.Add(key))
                throw new WorkspaceException("La lista de posiciones EI no coincide con los registros originales.");
            ValidateGen7Coordinates(position.X, position.Y, position.Z, "EI");
        }

        foreach (var blockIndex in positions.Select(position => position.BlockEntry).Distinct())
        {
            var entries = ReadMini(blocks[blockIndex], "EI");
            foreach (var position in positions.Where(position => position.BlockEntry == blockIndex))
            {
                var entry = entries[position.ContainerEntry];
                var offset = 4 + (position.RecordIndex * Gen7EiRecordSize) + Gen7EiPositionOffset - 4;
                BitConverter.GetBytes(position.X).CopyTo(entry, offset);
                BitConverter.GetBytes(position.Y).CopyTo(entry, offset + 4);
                BitConverter.GetBytes(position.Z).CopyTo(entry, offset + 8);
            }
            blocks[blockIndex] = Mini.PackMini(entries, "EI");
        }
    }

    private static List<OverworldGen7EiPositionEntry> ReadGen7EiPositionsFromBlocks(byte[][] blocks)
    {
        var positions = new List<OverworldGen7EiPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "EI"))
                continue;
            var entries = ReadMini(blocks[blockIndex], "EI");
            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (entry.Length < sizeof(int))
                    continue;

                var recordCount = BitConverter.ToInt32(entry, 0);
                if (recordCount <= 0 || !HasGen7EiRecordRange(entry, recordCount))
                    continue;

                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                    positions.Add(new OverworldGen7EiPositionEntry(blockIndex, containerEntry, recordIndex, 0, 0, 0));
            }
        }
        return positions;
    }

    /// <summary>
    /// Reads the confirmed PR variants whose first record is a single XYZ vector. Types 203 and
    /// 204 keep variable payloads after that vector, so export changes only the twelve bytes at
    /// 0x08 and leaves the rest of each entry untouched. PR type 364 remains diagnostic-only.
    /// </summary>
    private static OverworldGen7PrPositionEntry[] ReadGen7PrPositions(
        byte[][] encounterFiles, int worldIndex, ref string? diagnostics)
    {
        var fileIndex = (long)worldIndex * FilesPerWorld + Gen7EntityOffset;
        if (worldIndex < 0 || fileIndex < 0 || fileIndex >= encounterFiles.Length)
            return [];

        var data = encounterFiles[(int)fileIndex] ?? [];
        if (data.Length == 0)
            return [];

        byte[][] blocks;
        try
        {
            blocks = ReadMini(data, Gen7EntityIdentifier);
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque ED no se pudo leer: {exception.Message}");
            return [];
        }

        var positions = new List<OverworldGen7PrPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "PR"))
                continue;

            byte[][] entries;
            try
            {
                entries = ReadMini(blocks[blockIndex], "PR");
            }
            catch (WorkspaceException exception)
            {
                diagnostics = AppendError(diagnostics,
                    $"El bloque PR {blockIndex} no se pudo leer: {exception.Message}");
                continue;
            }

            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (!TryGetGen7PrFixedPosition(entry, out _, out var x, out var y, out var z))
                    continue;

                positions.Add(new OverworldGen7PrPositionEntry(
                    blockIndex, containerEntry, 0, x, y, z));
            }
        }
        return positions.ToArray();
    }

    private static void ApplyGen7PrPositions(byte[][] blocks, OverworldGen7PrPositionEntry[] positions)
    {
        var expected = ReadGen7PrPositionsFromBlocks(blocks);
        if (positions.Length != expected.Count)
            throw new WorkspaceException($"La cantidad de posiciones PR debe conservarse en {expected.Count} registros.");

        var expectedKeys = expected
            .Select(position => (position.BlockEntry, position.ContainerEntry, position.RecordIndex))
            .ToHashSet();
        var requestedKeys = new HashSet<(int BlockEntry, int ContainerEntry, int RecordIndex)>();
        foreach (var position in positions)
        {
            var key = (position.BlockEntry, position.ContainerEntry, position.RecordIndex);
            if (!expectedKeys.Contains(key) || !requestedKeys.Add(key))
                throw new WorkspaceException("La lista de posiciones PR no coincide con los registros originales.");
            ValidateGen7Coordinates(position.X, position.Y, position.Z, "PR");
        }

        foreach (var blockIndex in positions.Select(position => position.BlockEntry).Distinct())
        {
            var entries = ReadMini(blocks[blockIndex], "PR");
            foreach (var position in positions.Where(position => position.BlockEntry == blockIndex))
            {
                var entry = entries[position.ContainerEntry];
                if (!TryGetGen7PrFixedPosition(entry, out _, out _, out _, out _))
                    throw new WorkspaceException("La entrada PR dejó de tener una variante fija válida.");

                BitConverter.GetBytes(position.X).CopyTo(entry, Gen7PrPositionOffset);
                BitConverter.GetBytes(position.Y).CopyTo(entry, Gen7PrPositionOffset + sizeof(float));
                BitConverter.GetBytes(position.Z).CopyTo(entry, Gen7PrPositionOffset + (sizeof(float) * 2));
            }
            blocks[blockIndex] = Mini.PackMini(entries, "PR");
        }
    }

    private static List<OverworldGen7PrPositionEntry> ReadGen7PrPositionsFromBlocks(byte[][] blocks)
    {
        var positions = new List<OverworldGen7PrPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "PR"))
                continue;
            var entries = ReadMini(blocks[blockIndex], "PR");
            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                if (!TryGetGen7PrFixedPosition(entries[containerEntry], out _, out _, out _, out _))
                    continue;
                positions.Add(new OverworldGen7PrPositionEntry(blockIndex, containerEntry, 0, 0, 0, 0));
            }
        }
        return positions;
    }

    private static bool TryGetGen7PrFixedPosition(
        byte[] entry, out int kind, out float x, out float y, out float z)
    {
        kind = 0;
        x = y = z = 0;
        if (entry is null || entry.Length < Gen7PrPositionOffset + (sizeof(float) * 3))
            return false;

        var recordCount = BitConverter.ToInt32(entry, 0);
        kind = BitConverter.ToInt32(entry, sizeof(int));
        if (recordCount != 1 || kind is not (Gen7PrKind203 or Gen7PrKind204))
            return false;

        x = BitConverter.ToSingle(entry, Gen7PrPositionOffset);
        y = BitConverter.ToSingle(entry, Gen7PrPositionOffset + sizeof(float));
        z = BitConverter.ToSingle(entry, Gen7PrPositionOffset + (sizeof(float) * 2));
        return float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z);
    }

    /// <summary>
    /// Reads the stable primary EB records. EB can appear more than once in ED; only child
    /// entries tagged with kind 2 and the fixed 0x3C-byte record stride are exposed. Trailing
    /// payloads and all other EB variants remain untouched.
    /// </summary>
    private static OverworldGen7EbPositionEntry[] ReadGen7EbPositions(
        byte[][] encounterFiles, int worldIndex, ref string? diagnostics)
    {
        var fileIndex = (long)worldIndex * FilesPerWorld + Gen7EntityOffset;
        if (worldIndex < 0 || fileIndex < 0 || fileIndex >= encounterFiles.Length)
            return [];

        var data = encounterFiles[(int)fileIndex] ?? [];
        if (data.Length == 0)
            return [];

        byte[][] blocks;
        try
        {
            blocks = ReadMini(data, Gen7EntityIdentifier);
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque ED no se pudo leer: {exception.Message}");
            return [];
        }

        var positions = new List<OverworldGen7EbPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "EB"))
                continue;

            byte[][] entries;
            try
            {
                entries = ReadMini(blocks[blockIndex], "EB");
            }
            catch (WorkspaceException exception)
            {
                diagnostics = AppendError(diagnostics,
                    $"El bloque EB {blockIndex} no se pudo leer: {exception.Message}");
                continue;
            }

            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (!TryGetGen7EbRecordCount(entry, out var recordCount))
                    continue;

                if (recordCount <= 0)
                    continue;
                if (recordCount > 4096 || (long)Gen7EbPositionOffset + (recordCount * Gen7EbRecordSize) > entry.Length)
                {
                    diagnostics = AppendError(diagnostics,
                        $"El contenedor EB {blockIndex}/{containerEntry} declara registros fuera de rango.");
                    continue;
                }

                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    var offset = Gen7EbPositionOffset + (recordIndex * Gen7EbRecordSize);
                    positions.Add(new OverworldGen7EbPositionEntry(
                        blockIndex, containerEntry, recordIndex,
                        BitConverter.ToSingle(entry, offset),
                        BitConverter.ToSingle(entry, offset + 4),
                        BitConverter.ToSingle(entry, offset + 8)));
                }
            }
        }
        return positions.ToArray();
    }

    private static void ApplyGen7EbPositions(byte[][] blocks, OverworldGen7EbPositionEntry[] positions)
    {
        var expected = ReadGen7EbPositionsFromBlocks(blocks);
        if (positions.Length != expected.Count)
            throw new WorkspaceException($"La cantidad de posiciones EB debe conservarse en {expected.Count} registros.");

        var expectedKeys = expected
            .Select(position => (position.BlockEntry, position.ContainerEntry, position.RecordIndex))
            .ToHashSet();
        var requestedKeys = new HashSet<(int BlockEntry, int ContainerEntry, int RecordIndex)>();
        foreach (var position in positions)
        {
            var key = (position.BlockEntry, position.ContainerEntry, position.RecordIndex);
            if (!expectedKeys.Contains(key) || !requestedKeys.Add(key))
                throw new WorkspaceException("La lista de posiciones EB no coincide con los registros originales.");
            ValidateGen7Coordinates(position.X, position.Y, position.Z, "EB");
        }

        // Repack each EB block once, so a request with many records cannot repeatedly replace
        // the block and accidentally discard edits made to another child entry.
        foreach (var blockIndex in positions.Select(position => position.BlockEntry).Distinct())
        {
            var entries = ReadMini(blocks[blockIndex], "EB");
            foreach (var position in positions.Where(position => position.BlockEntry == blockIndex))
            {
                var entry = entries[position.ContainerEntry];
                var offset = Gen7EbPositionOffset + (position.RecordIndex * Gen7EbRecordSize);
                BitConverter.GetBytes(position.X).CopyTo(entry, offset);
                BitConverter.GetBytes(position.Y).CopyTo(entry, offset + 4);
                BitConverter.GetBytes(position.Z).CopyTo(entry, offset + 8);
            }
            blocks[blockIndex] = Mini.PackMini(entries, "EB");
        }
    }

    private static List<OverworldGen7EbPositionEntry> ReadGen7EbPositionsFromBlocks(byte[][] blocks)
    {
        var positions = new List<OverworldGen7EbPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "EB"))
                continue;
            var entries = ReadMini(blocks[blockIndex], "EB");
            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (!TryGetGen7EbRecordCount(entry, out var recordCount) || recordCount <= 0 ||
                    recordCount > 4096 ||
                    (long)Gen7EbPositionOffset + (recordCount * Gen7EbRecordSize) > entry.Length)
                    continue;
                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                    positions.Add(new OverworldGen7EbPositionEntry(blockIndex, containerEntry, recordIndex, 0, 0, 0));
            }
        }
        return positions;
    }

    private static bool TryGetGen7EbRecordCount(byte[] entry, out int recordCount)
    {
        recordCount = 0;
        if (entry is null || entry.Length < Gen7EbPositionOffset ||
            BitConverter.ToInt32(entry, sizeof(int)) != Gen7EbRecordKind)
            return false;
        recordCount = BitConverter.ToInt32(entry, 0);
        return true;
    }

    private static bool HasIdentifier(byte[] data, string identifier) => data.Length >= 2 &&
        data[0] == identifier[0] && data[1] == identifier[1];

    private static void ValidateGen7Coordinates(float x, float y, float z, string label)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
            Math.Abs(x) > 1_000_000 || Math.Abs(y) > 1_000_000 || Math.Abs(z) > 1_000_000)
            throw new WorkspaceException($"Las coordenadas {label} deben ser números finitos entre -1000000 y 1000000.");
    }

    /// <summary>
    /// Reads the stable ES type-4 records. Shorter ES variants are deliberately skipped because
    /// they do not contain the complete 0x38-byte retail record stride.
    /// </summary>
    private static OverworldGen7EsPositionEntry[] ReadGen7EsPositions(
        byte[][] encounterFiles, int worldIndex, ref string? diagnostics)
    {
        var fileIndex = (long)worldIndex * FilesPerWorld + Gen7EntityOffset;
        if (worldIndex < 0 || fileIndex < 0 || fileIndex >= encounterFiles.Length)
            return [];

        var data = encounterFiles[(int)fileIndex] ?? [];
        if (data.Length == 0)
            return [];

        byte[][] blocks;
        try
        {
            blocks = ReadMini(data, Gen7EntityIdentifier);
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque ED no se pudo leer: {exception.Message}");
            return [];
        }

        var positions = new List<OverworldGen7EsPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "ES"))
                continue;

            byte[][] entries;
            try
            {
                entries = ReadMini(blocks[blockIndex], "ES");
            }
            catch (WorkspaceException exception)
            {
                diagnostics = AppendError(diagnostics,
                    $"El bloque ES {blockIndex} no se pudo leer: {exception.Message}");
                continue;
            }

            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (!TryGetGen7EsRecordCount(entry, out var recordCount) || recordCount <= 0)
                    continue;
                if (recordCount > 4096 ||
                    (long)Gen7EsPositionOffset + (recordCount * Gen7EsRecordSize) > entry.Length)
                    continue;

                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    var offset = Gen7EsPositionOffset + (recordIndex * Gen7EsRecordSize);
                    positions.Add(new OverworldGen7EsPositionEntry(
                        blockIndex, containerEntry, recordIndex,
                        BitConverter.ToSingle(entry, offset),
                        BitConverter.ToSingle(entry, offset + 4),
                        BitConverter.ToSingle(entry, offset + 8)));
                }
            }
        }
        return positions.ToArray();
    }

    private static void ApplyGen7EsPositions(byte[][] blocks, OverworldGen7EsPositionEntry[] positions)
    {
        var expected = ReadGen7EsPositionsFromBlocks(blocks);
        if (positions.Length != expected.Count)
            throw new WorkspaceException($"La cantidad de posiciones ES debe conservarse en {expected.Count} registros.");

        var expectedKeys = expected
            .Select(position => (position.BlockEntry, position.ContainerEntry, position.RecordIndex))
            .ToHashSet();
        var requestedKeys = new HashSet<(int BlockEntry, int ContainerEntry, int RecordIndex)>();
        foreach (var position in positions)
        {
            var key = (position.BlockEntry, position.ContainerEntry, position.RecordIndex);
            if (!expectedKeys.Contains(key) || !requestedKeys.Add(key))
                throw new WorkspaceException("La lista de posiciones ES no coincide con los registros originales.");
            ValidateGen7Coordinates(position.X, position.Y, position.Z, "ES");
        }

        foreach (var blockIndex in positions.Select(position => position.BlockEntry).Distinct())
        {
            var entries = ReadMini(blocks[blockIndex], "ES");
            foreach (var position in positions.Where(position => position.BlockEntry == blockIndex))
            {
                var entry = entries[position.ContainerEntry];
                var offset = Gen7EsPositionOffset + (position.RecordIndex * Gen7EsRecordSize);
                BitConverter.GetBytes(position.X).CopyTo(entry, offset);
                BitConverter.GetBytes(position.Y).CopyTo(entry, offset + 4);
                BitConverter.GetBytes(position.Z).CopyTo(entry, offset + 8);
            }
            blocks[blockIndex] = Mini.PackMini(entries, "ES");
        }
    }

    private static List<OverworldGen7EsPositionEntry> ReadGen7EsPositionsFromBlocks(byte[][] blocks)
    {
        var positions = new List<OverworldGen7EsPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "ES"))
                continue;
            var entries = ReadMini(blocks[blockIndex], "ES");
            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (!TryGetGen7EsRecordCount(entry, out var recordCount) || recordCount <= 0 ||
                    recordCount > 4096 ||
                    (long)Gen7EsPositionOffset + (recordCount * Gen7EsRecordSize) > entry.Length)
                    continue;
                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                    positions.Add(new OverworldGen7EsPositionEntry(blockIndex, containerEntry, recordIndex, 0, 0, 0));
            }
        }
        return positions;
    }

    private static bool TryGetGen7EsRecordCount(byte[] entry, out int recordCount)
    {
        recordCount = 0;
        if (entry is null || entry.Length < Gen7EsPositionOffset ||
            BitConverter.ToInt32(entry, sizeof(int)) != Gen7EsRecordKind)
            return false;
        recordCount = BitConverter.ToInt32(entry, 0);
        return true;
    }

    /// <summary>
    /// Reads the stable EA type-5 records and the type-6 descriptor/payload records. Type 6 has
    /// a variable descriptor table followed by one 0x30-byte payload per descriptor; only the
    /// confirmed XYZ vector in each payload is exposed.
    /// </summary>
    private static OverworldGen7EaPositionEntry[] ReadGen7EaPositions(
        byte[][] encounterFiles, int worldIndex, ref string? diagnostics)
    {
        var fileIndex = (long)worldIndex * FilesPerWorld + Gen7EntityOffset;
        if (worldIndex < 0 || fileIndex < 0 || fileIndex >= encounterFiles.Length)
            return [];

        var data = encounterFiles[(int)fileIndex] ?? [];
        if (data.Length == 0)
            return [];

        byte[][] blocks;
        try
        {
            blocks = ReadMini(data, Gen7EntityIdentifier);
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque ED no se pudo leer: {exception.Message}");
            return [];
        }

        var positions = new List<OverworldGen7EaPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "EA"))
                continue;

            byte[][] entries;
            try
            {
                entries = ReadMini(blocks[blockIndex], "EA");
            }
            catch (WorkspaceException exception)
            {
                diagnostics = AppendError(diagnostics,
                    $"El bloque EA {blockIndex} no se pudo leer: {exception.Message}");
                continue;
            }

            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (!TryGetGen7EaRecordShape(entry, out var recordCount, out var recordKind) || recordCount <= 0)
                    continue;
                int[] offsets;
                if (recordKind == Gen7EaKind6RecordKind)
                {
                    if (!TryGetGen7EaKind6PayloadOffsets(entry, recordCount, out var payloadOffsets))
                        continue;
                    offsets = payloadOffsets
                        .Select(offset => offset + Gen7EaKind6PayloadPositionOffset)
                        .ToArray();
                }
                else
                {
                    if (recordCount > 4096 ||
                        (long)Gen7EaPositionOffset + (recordCount * Gen7EaRecordSize) > entry.Length ||
                        (long)Gen7EaPositionOffset + ((recordCount - 1) * Gen7EaRecordSize) + (3 * sizeof(float)) > entry.Length)
                        continue;
                    offsets = Enumerable.Range(0, recordCount)
                        .Select(index => Gen7EaPositionOffset + (index * Gen7EaRecordSize))
                        .ToArray();
                }

                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    var offset = offsets[recordIndex];
                    positions.Add(new OverworldGen7EaPositionEntry(
                        blockIndex, containerEntry, recordIndex,
                        BitConverter.ToSingle(entry, offset),
                        BitConverter.ToSingle(entry, offset + 4),
                        BitConverter.ToSingle(entry, offset + 8)));
                }
            }
        }
        return positions.ToArray();
    }

    private static void ApplyGen7EaPositions(byte[][] blocks, OverworldGen7EaPositionEntry[] positions)
    {
        var expected = ReadGen7EaPositionsFromBlocks(blocks);
        if (positions.Length != expected.Count)
            throw new WorkspaceException($"La cantidad de posiciones EA debe conservarse en {expected.Count} registros.");

        var expectedKeys = expected
            .Select(position => (position.BlockEntry, position.ContainerEntry, position.RecordIndex))
            .ToHashSet();
        var requestedKeys = new HashSet<(int BlockEntry, int ContainerEntry, int RecordIndex)>();
        foreach (var position in positions)
        {
            var key = (position.BlockEntry, position.ContainerEntry, position.RecordIndex);
            if (!expectedKeys.Contains(key) || !requestedKeys.Add(key))
                throw new WorkspaceException("La lista de posiciones EA no coincide con los registros originales.");
            ValidateGen7Coordinates(position.X, position.Y, position.Z, "EA");
        }

        foreach (var blockIndex in positions.Select(position => position.BlockEntry).Distinct())
        {
            var entries = ReadMini(blocks[blockIndex], "EA");
            foreach (var position in positions.Where(position => position.BlockEntry == blockIndex))
            {
                var entry = entries[position.ContainerEntry];
                var recordKind = BitConverter.ToInt32(entry, sizeof(int));
                var offset = recordKind == Gen7EaKind6RecordKind
                    ? GetGen7EaKind6PayloadPositionOffset(entry, position.RecordIndex)
                    : Gen7EaPositionOffset + (position.RecordIndex * Gen7EaRecordSize);
                BitConverter.GetBytes(position.X).CopyTo(entry, offset);
                BitConverter.GetBytes(position.Y).CopyTo(entry, offset + 4);
                BitConverter.GetBytes(position.Z).CopyTo(entry, offset + 8);
            }
            blocks[blockIndex] = Mini.PackMini(entries, "EA");
        }
    }

    private static List<OverworldGen7EaPositionEntry> ReadGen7EaPositionsFromBlocks(byte[][] blocks)
    {
        var positions = new List<OverworldGen7EaPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "EA"))
                continue;
            var entries = ReadMini(blocks[blockIndex], "EA");
            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (!TryGetGen7EaRecordShape(entry, out var recordCount, out var recordKind) || recordCount <= 0)
                    continue;
                if (recordKind == Gen7EaKind6RecordKind)
                {
                    if (!TryGetGen7EaKind6PayloadOffsets(entry, recordCount, out _))
                        continue;
                }
                else if (recordCount > 4096 ||
                         (long)Gen7EaPositionOffset + (recordCount * Gen7EaRecordSize) > entry.Length)
                    continue;
                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                    positions.Add(new OverworldGen7EaPositionEntry(blockIndex, containerEntry, recordIndex, 0, 0, 0));
            }
        }
        return positions;
    }

    private static bool TryGetGen7EaRecordShape(byte[] entry, out int recordCount, out int recordKind)
    {
        recordCount = 0;
        recordKind = 0;
        if (entry is null || entry.Length < Gen7EaPositionOffset + sizeof(int))
            return false;
        recordCount = BitConverter.ToInt32(entry, 0);
        recordKind = BitConverter.ToInt32(entry, sizeof(int));
        if (recordKind is not (Gen7EaRecordKind or Gen7EaKind6RecordKind))
            return false;
        return true;
    }

    private static bool TryGetGen7EaKind6PayloadOffsets(byte[] entry, int recordCount, out int[] offsets)
    {
        offsets = [];
        if (recordCount <= 0 || recordCount > 4096)
            return false;

        // The first descriptor is 0x18 bytes; every following descriptor is 0x1C.
        // Each descriptor ends in an absolute offset to a 0x30-byte payload.
        var tableEnd = checked(sizeof(int) * 2L + Gen7EaKind6FirstDescriptorSize
            + ((recordCount - 1L) * Gen7EaKind6DescriptorSize));
        if (tableEnd > entry.Length)
            return false;

        var payloadOffsets = new int[recordCount];
        for (var index = 0; index < recordCount; index++)
        {
            var descriptorOffset = index == 0
                ? sizeof(int) * 2
                : checked(sizeof(int) * 2 + Gen7EaKind6FirstDescriptorSize
                    + ((index - 1) * Gen7EaKind6DescriptorSize));
            var pointerOffset = checked(descriptorOffset
                + (index == 0 ? Gen7EaKind6FirstDescriptorSize : Gen7EaKind6DescriptorSize)
                - sizeof(int));
            var payloadOffset = BitConverter.ToInt32(entry, pointerOffset);
            if (payloadOffset < tableEnd ||
                (long)payloadOffset + Gen7EaKind6PayloadRecordSize > entry.Length)
                return false;
            payloadOffsets[index] = payloadOffset;
        }

        var ranges = payloadOffsets
            .Select(offset => (Start: offset, End: offset + Gen7EaKind6PayloadRecordSize))
            .OrderBy(range => range.Start)
            .ToArray();
        for (var index = 1; index < ranges.Length; index++)
        {
            if (ranges[index].Start < ranges[index - 1].End)
                return false;
        }

        offsets = payloadOffsets;
        return true;
    }

    private static int GetGen7EaKind6PayloadPositionOffset(byte[] entry, int recordIndex)
    {
        if (!TryGetGen7EaKind6PayloadOffsets(entry, BitConverter.ToInt32(entry, 0), out var offsets) ||
            recordIndex < 0 || recordIndex >= offsets.Length)
            throw new WorkspaceException("La tabla EA tipo 6 no contiene un payload de posición válido.");
        return checked(offsets[recordIndex] + Gen7EaKind6PayloadPositionOffset);
    }

    /// <summary>Reads the fixed ET type-7 records and the point tables used by ET type 9.</summary>
    private static OverworldGen7EtPositionEntry[] ReadGen7EtPositions(
        byte[][] encounterFiles, int worldIndex, ref string? diagnostics)
    {
        var fileIndex = (long)worldIndex * FilesPerWorld + Gen7EntityOffset;
        if (worldIndex < 0 || fileIndex < 0 || fileIndex >= encounterFiles.Length)
            return [];

        var data = encounterFiles[(int)fileIndex] ?? [];
        if (data.Length == 0)
            return [];

        byte[][] blocks;
        try
        {
            blocks = ReadMini(data, Gen7EntityIdentifier);
        }
        catch (WorkspaceException exception)
        {
            diagnostics = AppendError(diagnostics, $"El bloque ED no se pudo leer: {exception.Message}");
            return [];
        }

        var positions = new List<OverworldGen7EtPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "ET"))
                continue;

            byte[][] entries;
            try
            {
                entries = ReadMini(blocks[blockIndex], "ET");
            }
            catch (WorkspaceException exception)
            {
                diagnostics = AppendError(diagnostics,
                    $"El bloque ET {blockIndex} no se pudo leer: {exception.Message}");
                continue;
            }

            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (entry.Length < 2 * sizeof(int))
                    continue;

                var recordKind = BitConverter.ToInt32(entry, sizeof(int));
                var recordCount = BitConverter.ToInt32(entry, 0);
                if (recordKind == Gen7EtKind9RecordKind)
                {
                    if (!TryGetGen7EtKind9PositionOffsets(entry, recordCount, out var pointOffsets))
                        continue;
                    for (var recordIndex = 0; recordIndex < pointOffsets.Length; recordIndex++)
                    {
                        var offset = pointOffsets[recordIndex];
                        positions.Add(new OverworldGen7EtPositionEntry(
                            blockIndex, containerEntry, recordIndex,
                            BitConverter.ToSingle(entry, offset),
                            BitConverter.ToSingle(entry, offset + 4),
                            BitConverter.ToSingle(entry, offset + 8)));
                    }
                    continue;
                }

                if (!TryGetGen7EtRecordCount(entry, out recordCount) || recordCount <= 0)
                    continue;
                if (recordCount > 4096 ||
                    (long)Gen7EtPositionOffset + (recordCount * Gen7EtRecordSize) > entry.Length)
                    continue;

                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    var offset = Gen7EtPositionOffset + (recordIndex * Gen7EtRecordSize);
                    positions.Add(new OverworldGen7EtPositionEntry(
                        blockIndex, containerEntry, recordIndex,
                        BitConverter.ToSingle(entry, offset),
                        BitConverter.ToSingle(entry, offset + 4),
                        BitConverter.ToSingle(entry, offset + 8)));
                }
            }
        }
        return positions.ToArray();
    }

    private static void ApplyGen7EtPositions(byte[][] blocks, OverworldGen7EtPositionEntry[] positions)
    {
        var expected = ReadGen7EtPositionsFromBlocks(blocks);
        if (positions.Length != expected.Count)
            throw new WorkspaceException($"La cantidad de posiciones ET debe conservarse en {expected.Count} registros.");

        var expectedKeys = expected
            .Select(position => (position.BlockEntry, position.ContainerEntry, position.RecordIndex))
            .ToHashSet();
        var requestedKeys = new HashSet<(int BlockEntry, int ContainerEntry, int RecordIndex)>();
        foreach (var position in positions)
        {
            var key = (position.BlockEntry, position.ContainerEntry, position.RecordIndex);
            if (!expectedKeys.Contains(key) || !requestedKeys.Add(key))
                throw new WorkspaceException("La lista de posiciones ET no coincide con los registros originales.");
            ValidateGen7Coordinates(position.X, position.Y, position.Z, "ET");
        }

        foreach (var blockIndex in positions.Select(position => position.BlockEntry).Distinct())
        {
            var entries = ReadMini(blocks[blockIndex], "ET");
            foreach (var position in positions.Where(position => position.BlockEntry == blockIndex))
            {
                var entry = entries[position.ContainerEntry];
                var recordKind = BitConverter.ToInt32(entry, sizeof(int));
                var offset = recordKind == Gen7EtKind9RecordKind
                    ? GetGen7EtKind9PositionOffset(entry, position.RecordIndex)
                    : Gen7EtPositionOffset + (position.RecordIndex * Gen7EtRecordSize);
                BitConverter.GetBytes(position.X).CopyTo(entry, offset);
                BitConverter.GetBytes(position.Y).CopyTo(entry, offset + 4);
                BitConverter.GetBytes(position.Z).CopyTo(entry, offset + 8);
            }
            blocks[blockIndex] = Mini.PackMini(entries, "ET");
        }
    }

    private static List<OverworldGen7EtPositionEntry> ReadGen7EtPositionsFromBlocks(byte[][] blocks)
    {
        var positions = new List<OverworldGen7EtPositionEntry>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (!HasIdentifier(blocks[blockIndex], "ET"))
                continue;
            var entries = ReadMini(blocks[blockIndex], "ET");
            for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
            {
                var entry = entries[containerEntry];
                if (entry.Length < 2 * sizeof(int))
                    continue;

                var recordKind = BitConverter.ToInt32(entry, sizeof(int));
                var recordCount = BitConverter.ToInt32(entry, 0);
                if (recordKind == Gen7EtKind9RecordKind)
                {
                    if (!TryGetGen7EtKind9PositionOffsets(entry, recordCount, out var pointOffsets))
                        continue;
                    for (var recordIndex = 0; recordIndex < pointOffsets.Length; recordIndex++)
                        positions.Add(new OverworldGen7EtPositionEntry(
                            blockIndex, containerEntry, recordIndex, 0, 0, 0));
                    continue;
                }

                if (!TryGetGen7EtRecordCount(entry, out recordCount) || recordCount <= 0 ||
                    recordCount > 4096 ||
                    (long)Gen7EtPositionOffset + (recordCount * Gen7EtRecordSize) > entry.Length)
                    continue;
                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                    positions.Add(new OverworldGen7EtPositionEntry(blockIndex, containerEntry, recordIndex, 0, 0, 0));
            }
        }
        return positions;
    }

    /// <summary>
    /// ET type 9 stores a variable descriptor table followed by one or more point tables. The
    /// first descriptor is 0x14 bytes; subsequent descriptors carry their kind marker and are
    /// 0x18 bytes. Descriptor offsets are absolute and point tables start with two uint32 values
    /// followed by XYZ float triples. The second descriptor pointer is intentionally validated
    /// as well: it prevents mistaking arbitrary type-9 data for an editable point table.
    /// </summary>
    private static bool TryGetGen7EtKind9PositionOffsets(
        byte[] entry, int descriptorCount, out int[] offsets)
    {
        offsets = [];
        if (entry is null || descriptorCount <= 0 || descriptorCount > 4096)
            return false;

        var tableEnd = checked(2L * sizeof(int) + Gen7EtKind9FirstDescriptorSize
            + ((descriptorCount - 1L) * Gen7EtKind9DescriptorSize));
        if (tableEnd > entry.Length)
            return false;

        var positionOffsets = new List<int>();
        var ranges = new List<(int Start, int End)>();
        for (var descriptorIndex = 0; descriptorIndex < descriptorCount; descriptorIndex++)
        {
            var descriptorOffset = descriptorIndex == 0
                ? 2 * sizeof(int)
                : checked(2 * sizeof(int) + Gen7EtKind9FirstDescriptorSize
                    + ((descriptorIndex - 1) * Gen7EtKind9DescriptorSize));
            if (descriptorIndex > 0 &&
                BitConverter.ToInt32(entry, descriptorOffset) != Gen7EtKind9RecordKind)
                return false;

            var dataPointerOffset = descriptorOffset + (descriptorIndex == 0 ? 0 : sizeof(int));
            var tailPointerOffset = descriptorOffset + (descriptorIndex == 0 ? 8 : 12);
            var dataOffset = BitConverter.ToInt32(entry, dataPointerOffset);
            var tailOffset = BitConverter.ToInt32(entry, tailPointerOffset);
            if (dataOffset < tableEnd || dataOffset < 0 ||
                (long)dataOffset + Gen7EtKind9PointHeaderSize > entry.Length ||
                tailOffset < dataOffset || tailOffset < 0 || tailOffset > entry.Length)
                return false;

            var pointCountWord = BitConverter.ToUInt32(entry, dataOffset);
            var pointCount = (int)(pointCountWord & ushort.MaxValue);
            if (pointCount > 4096)
                return false;

            var pointStart = checked(dataOffset + Gen7EtKind9PointHeaderSize);
            var pointEnd = checked((long)pointStart + (pointCount * Gen7EtKind9PointSize));
            if (pointEnd > entry.Length || pointEnd > tailOffset)
                return false;

            ranges.Add((dataOffset, checked((int)pointEnd)));
            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
                positionOffsets.Add(checked(pointStart + (pointIndex * Gen7EtKind9PointSize)));
        }

        var orderedRanges = ranges.OrderBy(range => range.Start).ToArray();
        for (var index = 1; index < orderedRanges.Length; index++)
        {
            if (orderedRanges[index].Start < orderedRanges[index - 1].End)
                return false;
        }

        offsets = positionOffsets.ToArray();
        return true;
    }

    private static int GetGen7EtKind9PositionOffset(byte[] entry, int recordIndex)
    {
        if (!TryGetGen7EtKind9PositionOffsets(entry, BitConverter.ToInt32(entry, 0), out var offsets) ||
            recordIndex < 0 || recordIndex >= offsets.Length)
            throw new WorkspaceException("La tabla ET tipo 9 no contiene un punto de posición válido.");
        return offsets[recordIndex];
    }

    private static bool TryGetGen7EtRecordCount(byte[] entry, out int recordCount)
    {
        recordCount = 0;
        if (entry is null || entry.Length < Gen7EtPositionOffset ||
            BitConverter.ToInt32(entry, sizeof(int)) != Gen7EtRecordKind)
            return false;
        recordCount = BitConverter.ToInt32(entry, 0);
        return true;
    }

    private static List<OverworldGen7PositionEntry> ReadGen7EntityPositionsFromEntries(byte[][] entries)
    {
        var positions = new List<OverworldGen7PositionEntry>();
        for (var containerEntry = 0; containerEntry < entries.Length; containerEntry++)
        {
            var entry = entries[containerEntry];
            if (entry.Length < Gen7EntityPositionOffset)
                continue;
            var recordCount = BitConverter.ToInt32(entry, 0);
            var recordsEnd = (long)Gen7EntityPositionOffset + (recordCount * Gen7EntityRecordSize);
            if (recordCount <= 0 || recordCount > 4096 || recordsEnd > entry.Length)
                continue;
            for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                positions.Add(new OverworldGen7PositionEntry(containerEntry, recordIndex, 0, 0, 0));
        }
        return positions;
    }

    private static int FindMiniBlock(byte[][] blocks, string identifier) =>
        Array.FindIndex(blocks, block => block.Length >= 2 &&
            block[0] == identifier[0] && block[1] == identifier[1]);

    private static int RequireGen7WorldFileIndex(int fileCount, int worldIndex)
    {
        if (worldIndex < 0 || worldIndex >= fileCount / FilesPerWorld)
            throw new WorkspaceException("El mundo indicado no existe en encdata.");
        return checked(worldIndex * FilesPerWorld + Gen7EntityOffset);
    }

    private static bool IsMiniArchive(byte[] data)
    {
        if (data is null || data.Length < 8)
            return false;
        var count = BitConverter.ToUInt16(data, 2);
        var tableEnd = 8L + (count * sizeof(int));
        if (tableEnd > data.Length)
            return false;

        var previous = BitConverter.ToInt32(data, 4);
        if (previous < tableEnd || previous > data.Length)
            return false;
        for (var index = 0; index < count; index++)
        {
            var current = BitConverter.ToInt32(data, 8 + (index * sizeof(int)));
            if (current < previous || current > data.Length)
                return false;
            previous = current;
        }
        return true;
    }

    private static (byte[] Data, int Offset) RequireGen7ZoneData(GameConfig config, int zoneIndex)
    {
        var garc = config.GetlzGARCData("zonedata");
        if (garc.FileCount == 0)
            throw new WorkspaceException("El GARC zonedata Gen. VII está vacío.");
        var data = garc[0];
        var offset = checked((long)zoneIndex * ZoneData7.SIZE);
        if (zoneIndex < 0 || data is null || offset < 0 || offset + ZoneData7.SIZE > data.Length)
            throw new WorkspaceException("La tabla zonedata Gen. VII no alcanza la zona indicada.");
        return (data, checked((int)offset));
    }

    private static (int WorldIndex, int AreaIndex) ReadGen7ZoneMapping(GameConfig config, int zoneIndex)
    {
        try
        {
            var zoneData = config.GetlzGARCData("zonedata");
            var worldIndex = ReadGen7WorldIndex(zoneData.Files, zoneIndex);
            var worldData = config.GetlzGARCData("worlddata");
            var areaIndex = ReadGen7AreaIndex(worldData, worldIndex, zoneIndex);
            return (worldIndex, areaIndex);
        }
        catch (WorkspaceException)
        {
            // ParentMap remains useful even when an experimental dump lacks a complete
            // worlddata mapping table.
            return (-1, -1);
        }
    }

    private static int ReadGen7WorldIndex(byte[][] zoneFiles, int zoneIndex)
    {
        if (zoneFiles.Length < 2)
            throw new WorkspaceException("El zonedata Gen. VII no contiene la tabla zona→mundo.");
        var mapping = zoneFiles[1] ?? [];
        var offset = checked(zoneIndex * sizeof(ushort));
        if (zoneIndex < 0 || offset < 0 || offset + sizeof(ushort) > mapping.Length)
            throw new WorkspaceException("La tabla zona→mundo no alcanza la zona indicada.");
        return BitConverter.ToUInt16(mapping, offset);
    }

    private static int ReadGen7AreaIndex(LazyGARCFile worldData, int worldIndex, int zoneIndex)
    {
        if (worldIndex < 0 || worldIndex >= worldData.FileCount)
            throw new WorkspaceException("El worlddata Gen. VII no contiene el mundo indicado.");
        var worlds = ReadMini(worldData[worldIndex], "WD");
        if (worlds.Length == 0)
            throw new WorkspaceException("El mundo Gen. VII no contiene su tabla de áreas.");
        var world = worlds[0];
        if (world.Length < 0x0C)
            throw new WorkspaceException("La tabla WD Gen. VII es demasiado pequeña.");
        var mappingOffset = BitConverter.ToInt32(world, 0x08);
        if (mappingOffset < 0x0C || mappingOffset > world.Length || ((world.Length - mappingOffset) % 4) != 0)
            throw new WorkspaceException("La tabla WD Gen. VII tiene un offset de áreas inválido.");
        for (var offset = mappingOffset; offset + 4 <= world.Length; offset += 4)
        {
            if (BitConverter.ToUInt16(world, offset) == zoneIndex)
                return BitConverter.ToUInt16(world, offset + 2);
        }
        throw new WorkspaceException("El mundo Gen. VII no tiene una entrada para la zona indicada.");
    }

    private static void ApplyGen7AreaIndex(LazyGARCFile worldData, int worldIndex, int zoneIndex, int areaIndex)
    {
        if (worldIndex < 0 || worldIndex >= worldData.FileCount)
            throw new WorkspaceException("El worlddata Gen. VII no contiene el mundo indicado.");
        var worlds = ReadMini(worldData[worldIndex], "WD");
        if (worlds.Length == 0)
            throw new WorkspaceException("El mundo Gen. VII no contiene su tabla de áreas.");
        var world = worlds[0];
        if (world.Length < 0x0C)
            throw new WorkspaceException("La tabla WD Gen. VII es demasiado pequeña.");
        var mappingOffset = BitConverter.ToInt32(world, 0x08);
        if (mappingOffset < 0x0C || mappingOffset > world.Length || ((world.Length - mappingOffset) % 4) != 0)
            throw new WorkspaceException("La tabla WD Gen. VII tiene un offset de áreas inválido.");
        for (var offset = mappingOffset; offset + 4 <= world.Length; offset += 4)
        {
            if (BitConverter.ToUInt16(world, offset) != zoneIndex)
                continue;
            BitConverter.GetBytes((ushort)areaIndex).CopyTo(world, offset + 2);
            worldData[worldIndex] = Mini.PackMini(worlds, "WD");
            return;
        }
        throw new WorkspaceException("El mundo Gen. VII no tiene una entrada para la zona indicada.");
    }

    private static string GetGen7ZoneLocation(int zoneIndex, int parentMap, string[] locations) =>
        parentMap >= 0 && parentMap < locations.Length && !string.IsNullOrWhiteSpace(locations[parentMap])
            ? $"{zoneIndex:000} · {locations[parentMap]}"
            : $"Área {zoneIndex:000}";

    private static string GetLocationName(byte[][] zoneFiles, string[] locations, int worldIndex)
    {
        var zoneData = zoneFiles.Length > 0 ? zoneFiles[0] : [];
        var zoneCount = zoneData.Length / ZoneData7.SIZE;
        var zoneIndex = FindGen7ZoneIndex(zoneFiles, worldIndex);
        if (zoneIndex < 0 || zoneIndex >= zoneCount)
            return $"Área {worldIndex:000}";

        var parentMap = new ZoneData7(zoneData, zoneIndex).ParentMap;
        var location = parentMap >= 0 && parentMap < locations.Length ? locations[parentMap] : string.Empty;
        return string.IsNullOrWhiteSpace(location)
            ? $"Área {zoneIndex:000}"
            : $"{zoneIndex:000} · {location}";
    }

    private static int FindGen7ZoneIndex(byte[][] zoneFiles, int worldIndex)
    {
        var zoneData = zoneFiles.Length > 0 ? zoneFiles[0] : [];
        var worldIndexes = zoneFiles.Length > 1 ? zoneFiles[1] : [];
        var zoneCount = zoneData.Length / ZoneData7.SIZE;
        for (var index = 0; index + 1 < worldIndexes.Length; index += 2)
        {
            if (BitConverter.ToUInt16(worldIndexes, index) == worldIndex)
                return index / 2;
        }

        return worldIndex >= 0 && worldIndex < zoneCount ? worldIndex : -1;
    }

    private static byte[][] ReadMiniOrEmpty(byte[] data, string identifier)
    {
        if (data is null || data.Length == 0)
            return [];

        try
        {
            return ReadMini(data, identifier);
        }
        catch (WorkspaceException)
        {
            // Catalogs should still show valid groups when an unrelated experimental entry is
            // malformed. Opening that malformed group remains an explicit error.
            return [];
        }
    }

    private static byte[][] ReadMini(byte[] data, string identifier)
    {
        if (data is null || data.Length == 0)
            return [];
        if (data.Length < 8 || data[0] != identifier[0] || data[1] != identifier[1])
            throw new WorkspaceException($"El archivo OWSE no tiene la firma {identifier} esperada.");

        var count = BitConverter.ToUInt16(data, 2);
        var tableEnd = checked(8 + (count * sizeof(int)));
        if (tableEnd > data.Length)
            throw new WorkspaceException("El mini-archivo OWSE tiene una tabla de offsets incompleta.");

        var previous = BitConverter.ToInt32(data, 4);
        if (previous < tableEnd || previous > data.Length)
            throw new WorkspaceException("El primer offset del mini-archivo OWSE es inválido.");

        for (var index = 0; index < count; index++)
        {
            var current = BitConverter.ToInt32(data, 8 + (index * sizeof(int)));
            if (current < previous || current > data.Length)
                throw new WorkspaceException("El mini-archivo OWSE contiene offsets inválidos.");
            previous = current;
        }

        return Mini.UnpackMini(data, identifier) ?? [];
    }

    private static OverworldScriptEntryResponse Describe(
        string group, int worldIndex, int scriptIndex, string locationName, byte[] raw,
        OverworldZoneSummary? zone = null)
    {
        var rawHex = HexLines(raw);
        if (raw.Length < ScriptHeaderSize)
            return new OverworldScriptEntryResponse(
                group, worldIndex, scriptIndex, locationName, raw.Length, 0, false,
                0, 0, 0, 0, 0, 0, [], [],
                "El script no contiene el encabezado mínimo de 0x1C bytes.", rawHex, zone);

        Script script;
        try
        {
            script = new Script(raw);
        }
        catch (Exception exception)
        {
            return new OverworldScriptEntryResponse(
                group, worldIndex, scriptIndex, locationName, raw.Length,
                BitConverter.ToUInt32(raw, 4), BitConverter.ToUInt32(raw, 4) == 0x0A0AF1EF,
                BitConverter.ToInt32(raw, 0x0C), BitConverter.ToInt32(raw, 0x10),
                BitConverter.ToInt32(raw, 0x14), BitConverter.ToInt32(raw, 0x18), 0, 0, [], [],
                $"No se pudo leer el encabezado del script: {exception.Message}", rawHex, zone);
        }

        var instructionStart = script.ScriptInstructionStart;
        var movementStart = script.ScriptMovementStart;
        var finalOffset = script.FinalOffset;
        var decompressedBytes = finalOffset >= instructionStart ? finalOffset - instructionStart : 0;
        // Mini archives align entries to four bytes. The script header's Length excludes that
        // padding, so report the meaningful compressed length while RawBytes remains the exact
        // byte count read from the archive.
        var compressedBytes = script.Length >= instructionStart
            ? script.CompressedLength
            : 0;
        var error = ValidateHeader(instructionStart, movementStart, finalOffset, decompressedBytes);
        uint[] instructions = [];
        string[] parsedLines = [];

        if (error is null)
        {
            try
            {
                instructions = script.DecompressedInstructions;
            }
            catch (Exception exception)
            {
                error = $"No se pudo descomprimir el script: {exception.Message}";
            }
        }

        if (instructions.Length > 0)
        {
            try
            {
                parsedLines = script.ParseScript;
            }
            catch (Exception exception)
            {
                error = AppendError(error, $"No se pudo interpretar el script: {exception.Message}");
            }
        }

        return new OverworldScriptEntryResponse(
            group, worldIndex, scriptIndex, locationName, raw.Length, script.Magic, script.Debug,
            instructionStart, movementStart, finalOffset, script.AllocatedMemory,
            compressedBytes, decompressedBytes, instructions, parsedLines, error, rawHex, zone);
    }

    private static string? ValidateHeader(int instructionStart, int movementStart, int finalOffset, int decompressedBytes)
    {
        if (instructionStart < ScriptHeaderSize)
            return "El offset de instrucciones apunta dentro del encabezado.";
        if (finalOffset < instructionStart || decompressedBytes % sizeof(uint) != 0)
            return "El rango descomprimido del script no es válido.";
        if (movementStart < instructionStart || movementStart > finalOffset ||
            (movementStart - instructionStart) % sizeof(uint) != 0)
            return "El offset de movimiento del script no es válido.";
        return decompressedBytes > MaxDecompressedScriptBytes
            ? "El script declara más de 16 MiB descomprimidos; se omitió para proteger el inspector."
            : null;
    }

    private static string AppendError(string? current, string next) =>
        string.IsNullOrWhiteSpace(current) ? next : $"{current} {next}";

    private static string[] HexLines(byte[] data) => data
        .Chunk(16)
        .Select(chunk => string.Join(' ', chunk.Select(value => value.ToString("X2"))))
        .ToArray();
}
