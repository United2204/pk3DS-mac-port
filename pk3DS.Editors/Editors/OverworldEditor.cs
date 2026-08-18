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
    private const int Gen6MapMatrixDimensionsOffset = 0x14;
    private const int Gen7EntityPositionOffset = 0x08;
    private const int Gen7EntityRecordSize = 0x3C;
    private const int Gen7EmPositionOffset = 0x08;
    private const int Gen7EmRecordSize = 0x78;
    private const int Gen7EmRecordKind = 1;
    private const int Gen7EbPositionOffset = 0x08;
    private const int Gen7EbRecordSize = 0x3C;
    private const int Gen7EbRecordKind = 2;
    private const int Gen7EsPositionOffset = 0x08;
    private const int Gen7EsRecordSize = 0x3C;
    private const int Gen7EsRecordKind = 4;
    private const int Gen7EaPositionOffset = 0x08;
    private const int Gen7EaRecordSize = 0x3C;
    private const int Gen7EaRecordKind = 5;
    private const int Gen7EtPositionOffset = 0x08;
    private const int Gen7EtRecordSize = 0x54;
    private const int Gen7EtRecordKind = 7;
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
        return new OverworldGen7ZoneResponse(
            config.Version.ToString(), request.ZoneIndex, GetGen7ZoneLocation(request.ZoneIndex, parentMap, locations),
            parentMap, data.Length);
    }

    /// <summary>Exports only the understood Gen. VII parent-map field to a LayeredFS patch.</summary>
    public static ExportResult ExportGen7Zone(OverworldGen7ZoneExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "owse-gen7-zone", ["zonedata"], config =>
            {
                Guard.Gen7(config, "metadatos de zona");
                var (data, offset) = RequireGen7ZoneData(config, request.ZoneIndex);
                var locations = config.GetText(TextName.metlist_000000);
                if (request.ParentMap < 0 || request.ParentMap >= locations.Length)
                    throw new WorkspaceException($"El mapa padre debe estar entre 0 y {locations.Length - 1}.");

                BitConverter.GetBytes(request.ParentMap).CopyTo(data, offset + 0x1C);
                var garc = config.GetlzGARCData("zonedata");
                garc[0] = data;
                garc.Save();
                return [config.GetGARCFileName("zonedata")];
            });

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
        var ebPositions = ReadGen7EbPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        var esPositions = ReadGen7EsPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        var eaPositions = ReadGen7EaPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        var etPositions = ReadGen7EtPositions(encounterData.Files, request.WorldIndex, ref diagnostics);
        return new OverworldGen7EntityResponse(
            config.Version.ToString(), request.WorldIndex, positions, emPositions, ebPositions, esPositions, eaPositions, etPositions, blocks, diagnostics);
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

    /// <summary>Reads the Gen. VI movement/property grid referenced by a zone.</summary>
    public static OverworldGen6MapResponse GetGen6Map(OverworldGen6MapRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen6(config);
        var zone = GetGen6Zone(config, request.ZoneIndex, out _);
        var metadata = ReadGen6ZoneMetadata(zone[0])
            ?? throw new WorkspaceException("La zona Gen. VI no expone metadatos de mapa.");
        var map = ReadGen6Map(config.GetlzGARCData("mapGR"), metadata.MapArea);
        var matrix = ReadGen6MapMatrix(config.GetlzGARCData("mapMatrix"), metadata.MapMatrix);
        return new OverworldGen6MapResponse(
            config.Version.ToString(), request.ZoneIndex, metadata.MapArea, metadata.MapMatrix,
            map.Width, map.Height, map.Properties, matrix.Width, matrix.Height, matrix.Values,
            matrix.Diagnostics);
    }

    /// <summary>Exports only the understood Gen. VI map-property grid to a LayeredFS patch.</summary>
    public static ExportResult ExportMap(OverworldGen6MapExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "owse-gen6-map", ["encdata", "mapGR"], config =>
            {
                Guard.Gen6(config);
                var zone = GetGen6Zone(config, request.ZoneIndex, out _);
                var metadata = ReadGen6ZoneMetadata(zone[0])
                    ?? throw new WorkspaceException("La zona Gen. VI no expone metadatos de mapa.");
                var mapGarc = config.GetlzGARCData("mapGR");
                var map = ReadGen6Map(mapGarc, metadata.MapArea);
                if (request.Properties is null || request.Properties.Length != map.Properties.Length)
                    throw new WorkspaceException($"La grilla del mapa debe conservar {map.Properties.Length} celdas.");

                var raw = mapGarc[metadata.MapArea];
                for (var index = 0; index < request.Properties.Length; index++)
                    BitConverter.GetBytes(request.Properties[index]).CopyTo(raw, Gen6MapPropertyOffset + (index * sizeof(uint)));
                mapGarc[metadata.MapArea] = raw;
                mapGarc.Save();
                return [config.GetGARCFileName("mapGR")];
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
        if (data is null || data.Length < Gen6MapPropertyOffset)
            throw new WorkspaceException($"El área de mapa {mapArea} no contiene una cabecera GR completa.");
        if (data[0] != 'G' || data[1] != 'R')
            throw new WorkspaceException($"El área de mapa {mapArea} no tiene formato GR.");

        var width = BitConverter.ToUInt16(data, 0x80);
        var height = BitConverter.ToUInt16(data, 0x82);
        var count = checked((long)width * height);
        if (width == 0 || height == 0 || count > 4_000_000 ||
            Gen6MapPropertyOffset + (count * sizeof(uint)) > data.Length)
            throw new WorkspaceException($"La grilla GR del área {mapArea} tiene dimensiones inválidas.");

        var cellCount = checked((int)count);
        var properties = new uint[cellCount];
        Buffer.BlockCopy(data, Gen6MapPropertyOffset, properties, 0, checked(cellCount * sizeof(uint)));
        return new Gen6MapGrid(width, height, properties);
    }

    private static Gen6MapMatrix ReadGen6MapMatrix(LazyGARCFile garc, int mapMatrix)
    {
        if (mapMatrix < 0 || mapMatrix >= garc.FileCount)
            return new(0, 0, [], $"La matriz {mapMatrix} no existe en mapMatrix.");
        var data = garc[mapMatrix];
        if (data is null || data.Length < Gen6MapMatrixDimensionsOffset + 4 ||
            data[0] != 'M' || data[1] != 'M')
            return new(0, 0, [], $"La entrada {mapMatrix} no tiene formato MM reconocible.");

        var width = BitConverter.ToUInt16(data, Gen6MapMatrixDimensionsOffset);
        var height = BitConverter.ToUInt16(data, Gen6MapMatrixDimensionsOffset + 2);
        var count = checked((long)width * height);
        if (width == 0 || height == 0 || count > 1_000_000)
            return new(0, 0, [], $"La matriz {mapMatrix} tiene dimensiones inválidas.");

        var available = Math.Max(0, (data.Length - 0x18) / sizeof(ushort));
        var valueCount = checked((int)Math.Min(count, available));
        var values = new ushort[valueCount];
        for (var index = 0; index < values.Length; index++)
            values[index] = BitConverter.ToUInt16(data, 0x18 + (index * sizeof(ushort)));
        var diagnostics = values.Length == count ? null :
            $"La matriz expone {values.Length} de {count} celdas en su sección inicial; se conserva el resto sin interpretar.";
        return new(width, height, values, diagnostics);
    }

    private sealed record Gen6MapGrid(int Width, int Height, uint[] Properties);
    private sealed record Gen6MapMatrix(int Width, int Height, ushort[] Values, string? Diagnostics);

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
        return new OverworldGen6ZoneMetadata(
            ReadU16(data, 0x02), ReadU16(data, 0x04), ReadU16(data, 0x06),
            ReadU16(data, 0x18), ReadU16(data, 0x1C) & 0x3FF, ReadU16(data, 0x1E) & 0x1F);
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
                        .Select((entry, entryIndex) => new OverworldGen7EntityEntrySummary(
                            entryIndex,
                            entry.Length,
                            entry.Length >= sizeof(int) ? BitConverter.ToInt32(entry, 0) : null,
                            identifier == "EP" || entry.Length < (2 * sizeof(int))
                                ? null
                                : BitConverter.ToInt32(entry, sizeof(int))))
                        .ToArray();
                }
                catch (WorkspaceException exception)
                {
                    diagnostics = AppendError(diagnostics,
                        $"El bloque {identifier} no se pudo detallar: {exception.Message}");
                }
            }

            summaries.Add(new OverworldGen7EntityBlockSummary(
                identifier, block.Length, entryCount, isMiniArchive, entries));
        }

        return summaries.ToArray();
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
    /// they do not contain the complete 0x3C-byte record stride.
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
    /// Reads the stable EA type-5 records. The type-6 EA variant combines references with
    /// nested records and remains diagnostic-only until its complete schema is confirmed.
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
                if (!TryGetGen7EaRecordCount(entry, out var recordCount) || recordCount <= 0)
                    continue;
                if (recordCount > 4096 ||
                    (long)Gen7EaPositionOffset + (recordCount * Gen7EaRecordSize) > entry.Length)
                    continue;

                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    var offset = Gen7EaPositionOffset + (recordIndex * Gen7EaRecordSize);
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
                var offset = Gen7EaPositionOffset + (position.RecordIndex * Gen7EaRecordSize);
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
                if (!TryGetGen7EaRecordCount(entry, out var recordCount) || recordCount <= 0 ||
                    recordCount > 4096 ||
                    (long)Gen7EaPositionOffset + (recordCount * Gen7EaRecordSize) > entry.Length)
                    continue;
                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                    positions.Add(new OverworldGen7EaPositionEntry(blockIndex, containerEntry, recordIndex, 0, 0, 0));
            }
        }
        return positions;
    }

    private static bool TryGetGen7EaRecordCount(byte[] entry, out int recordCount)
    {
        recordCount = 0;
        if (entry is null || entry.Length < Gen7EaPositionOffset ||
            BitConverter.ToInt32(entry, sizeof(int)) != Gen7EaRecordKind)
            return false;
        recordCount = BitConverter.ToInt32(entry, 0);
        return true;
    }

    /// <summary>Reads the stable ET type-7 position records; ET type 9 remains diagnostic-only.</summary>
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
                if (!TryGetGen7EtRecordCount(entry, out var recordCount) || recordCount <= 0)
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
                var offset = Gen7EtPositionOffset + (position.RecordIndex * Gen7EtRecordSize);
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
                if (!TryGetGen7EtRecordCount(entry, out var recordCount) || recordCount <= 0 ||
                    recordCount > 4096 ||
                    (long)Gen7EtPositionOffset + (recordCount * Gen7EtRecordSize) > entry.Length)
                    continue;
                for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
                    positions.Add(new OverworldGen7EtPositionEntry(blockIndex, containerEntry, recordIndex, 0, 0, 0));
            }
        }
        return positions;
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
