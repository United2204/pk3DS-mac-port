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

    public static WildGen6CatalogResponse GetCatalog(WildGen6CatalogRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen6(config);
        var garc = config.GetGARCData("encdata");
        var firstMapFile = FirstMapFile(config.ORAS);
        var zonedata = garc.Files[0];
        var locations = config.GetText(TextName.metlist_000000);
        var areas = Enumerable.Range(firstMapFile, garc.Files.Length - firstMapFile).Select(fileIndex =>
        {
            var locationIndex = fileIndex - firstMapFile;
            return new WildGen6AreaSummary(fileIndex, locationIndex, GetAreaName(zonedata, locations, locationIndex),
                TryGetEncounterOffset(garc.Files[fileIndex], config.ORAS, out _));
        }).ToArray();
        return new WildGen6CatalogResponse(config.ORAS ? "ORAS" : "XY", areas);
    }

    public static WildGen6TableResponse GetTable(WildGen6TableRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen6(config);
        var garc = config.GetGARCData("encdata");
        var file = garc.Files[RequireFileIndex(config, garc, request.FileIndex)];
        if (!TryGetEncounterOffset(file, config.ORAS, out var offset))
            throw new WorkspaceException("Esta área no contiene una tabla de encuentros que se pueda editar.");

        var locationIndex = request.FileIndex - FirstMapFile(config.ORAS);
        var groups = GetGroups(config.ORAS, ReadSlots(file, offset, GetSlotCount(config.ORAS)));
        var areaName = GetAreaName(garc.Files[0], config.GetText(TextName.metlist_000000), locationIndex);
        return new WildGen6TableResponse(request.FileIndex, areaName, groups, Catalogs.Species(config));
    }

    public static ExportResult Export(WildGen6ExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "wild", ["encdata"], config =>
            {
                Guard.Gen6(config);
                var garc = config.GetGARCData("encdata");
                var fileIndex = RequireFileIndex(config, garc, request.FileIndex);
                var file = garc.Files[fileIndex];
                if (!TryGetEncounterOffset(file, config.ORAS, out var offset))
                    throw new WorkspaceException("No se pueden añadir encuentros a un área que no tiene tabla.");

                var slotCount = GetSlotCount(config.ORAS);
                var slots = FlattenGroups(request.Groups, slotCount, Catalogs.SpeciesCount(config));
                WriteSlots(file, offset, slots);
                garc.SetFile(fileIndex, file);

                // OR/AS keeps a second, packed copy of every area's slots in file 1; both must agree
                // or the game reads back the original encounters.
                if (config.ORAS)
                {
                    var packed = garc.Files[1];
                    var locationIndex = fileIndex - FirstMapFile(oras: true);
                    var packedOffset = BitConverter.ToInt32(packed, (locationIndex + 1) * 4) + 0xE;
                    if (packedOffset < 0 || packedOffset + (slotCount * SlotSize) > packed.Length)
                        throw new WorkspaceException("La tabla interna de encuentros de OR/AS no es válida.");
                    WriteSlots(packed, packedOffset, slots);
                    garc.SetFile(1, packed);
                }

                garc.Save();
                return [config.GetGARCFileName("encdata")];
            });

    // XY starts its map files at index 1; OR/AS reserves index 1 for the packed slot table.
    private static int FirstMapFile(bool oras) => oras ? 2 : 1;

    internal static int GetSlotCount(bool oras) => oras ? 61 : 94;

    internal static bool TryGetEncounterOffset(byte[] file, bool oras, out int offset)
    {
        offset = 0;
        if (file.Length < 0x18)
            return false;
        offset = BitConverter.ToInt32(file, 0x10) + (oras ? 0xE : 0x10);
        return offset >= 0 && offset + (GetSlotCount(oras) * SlotSize) <= file.Length;
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

    private static int RequireFileIndex(GameConfig config, GARCFile garc, int fileIndex) =>
        fileIndex >= FirstMapFile(config.ORAS) && fileIndex < garc.Files.Length
            ? fileIndex
            : throw new WorkspaceException("El área indicada no existe.");
}
