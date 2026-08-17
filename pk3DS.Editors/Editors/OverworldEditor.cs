using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// First portable slice of OWSE: inspect Gen VI/VII zone scripts without touching the source dump.
/// The original WinForms editor also exposed map and entity data, but those formats are not yet
/// safe to write from the Mac port, so this surface deliberately remains read-only.
/// </summary>
public static class OverworldEditor
{
    private const int FilesPerWorld = 11;
    private const int ZoneScriptOffset = 7;
    private const int ZoneInfoOffset = 8;
    private const string ZoneScriptGroup = "zone-script";
    private const string ZoneInfoGroup = "zone-info";
    private const string ZoneScriptIdentifier = "ZS";
    private const string ZoneInfoIdentifier = "ZI";
    private const string Gen6OverworldGroup = "gen6-overworld";
    private const string Gen6MapScriptGroup = "gen6-map-script";
    private const int Gen6ZoneDataSize = 0x38;
    private const int Gen6FirstZoneFileXy = 1;
    private const int Gen6FirstZoneFileOras = 2;
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
        var zone = GetGen7ZoneSummary(zoneData.Files, request.WorldIndex);
        return Describe(request.Group, request.WorldIndex, request.ScriptIndex, locationName,
            scripts[request.ScriptIndex], zone);
    }

    private static OverworldCatalogResponse GetGen6Catalog(GameConfig config)
    {
        Guard.Gen6(config);
        var encounterData = config.GetGARCData("encdata");
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

        var encounterData = config.GetGARCData("encdata");
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
        if (data is null || data.Length < 12)
            throw new WorkspaceException("El bloque de entidades Gen. VI no tiene una cabecera completa.");

        var furniture = data[4];
        var npcs = data[5];
        var warps = data[6];
        var triggers = data[7];
        var unknown = BitConverter.ToInt32(data, 8);
        if (unknown < 0)
            throw new WorkspaceException("El bloque de entidades Gen. VI declara una cantidad inválida.");

        const int furnitureSize = 0x14;
        const int npcSize = 0x30;
        const int warpSize = 0x18;
        const int triggerSize = 0x18;
        var scriptOffset = checked(12L
            + (furniture * furnitureSize)
            + (npcs * npcSize)
            + (warps * warpSize)
            + (triggers * triggerSize)
            + (unknown * triggerSize));
        if (scriptOffset > data.Length - sizeof(int))
            throw new WorkspaceException("El bloque de entidades Gen. VI no alcanza su script.");

        var length = BitConverter.ToInt32(data, (int)scriptOffset);
        if (length < 0 || length > data.Length - scriptOffset)
            throw new WorkspaceException("El script de overworld Gen. VI tiene una longitud inválida.");
        return data.Skip((int)scriptOffset).Take(length).ToArray();
    }

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

    private static OverworldZoneSummary GetGen7ZoneSummary(byte[][] zoneFiles, int worldIndex)
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

        return new OverworldZoneSummary(
            zoneIndex, ZoneData7.SIZE, FilesPerWorld,
            ParentMap: BitConverter.ToInt32(zoneData, (int)offset + 0x1C));
    }

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
