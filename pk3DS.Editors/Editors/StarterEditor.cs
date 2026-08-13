using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>Gen VI starter triplets mirrored in DllPoke3Select.cro and DllField.cro.</summary>
public static class StarterEditor
{
    private const string PokeFile = "DllPoke3Select.cro";
    private const string FieldFile = "DllField.cro";
    private const int GroupSize = 3;
    private const int PokeHeaderOffset = 0xB8;
    private const int PokeRecordSize = 0x54;
    private const int XyFieldOffset = 0xF805C;
    private const int OrasFieldOffset = 0xF906C;
    private const int XyFieldSize = 0x18;
    private const int OrasFieldSize = 0x24;
    private static readonly int[] XyFieldEntries = [0, 1, 2, 3, 4, 5];
    private static readonly int[] OrasFieldEntries = [0, 1, 2, 28, 29, 30, 31, 32, 33, 34, 35, 36];
    private static readonly string[] XyNames = ["Gen 6 Starters", "Gen 1 Starters"];
    private static readonly string[] OrasNames = ["Gen 3 Starters", "Gen 2 Starters", "Gen 4 Starters", "Gen 5 Starters"];

    public static StarterTableResponse GetTable(StarterTableRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var config = EditorSession.OpenReadOnly(workspace, request.Language);
        EnsureSupported(config);
        var data = ReadSource(workspace, PokeFile);
        var groups = ReadGroups(data, config);
        return new StarterTableResponse(config.Version.ToString(), groups, Catalogs.Species(config),
            "Los cambios se escriben en DllPoke3Select.cro y DllField.cro. La salida es un parche RomFS para Luma.");
    }

    public static ExportResult Export(StarterExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        RequireFiles(workspace);
        return EditorSession.ExportLooseFiles(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "starters", [PokeFile, FieldFile], (_, config, scratchRomFs) =>
            {
                EnsureSupported(config);
                Validate(request.Groups, Catalogs.SpeciesCount(config), config);
                var pokePath = Path.Combine(scratchRomFs, PokeFile);
                var fieldPath = Path.Combine(scratchRomFs, FieldFile);
                var poke = File.ReadAllBytes(pokePath);
                var field = File.ReadAllBytes(fieldPath);
                var pokeOffset = FindPokeOffset(poke, config);
                var fieldLayout = FindFieldLayout(field, config);
                WritePoke(poke, pokeOffset, request.Groups);
                WriteField(field, fieldLayout, request.Groups);
                File.WriteAllBytes(pokePath, poke);
                File.WriteAllBytes(fieldPath, field);
            });
    }

    internal static StarterGroup[] ReadGroups(byte[] data, GameConfig config)
    {
        var count = config.ORAS ? OrasNames.Length : XyNames.Length;
        var offset = FindPokeOffset(data, config);
        var names = config.ORAS ? OrasNames : XyNames;
        var groups = new StarterGroup[count];
        for (var group = 0; group < count; group++)
        {
            var species = new int[GroupSize];
            for (var index = 0; index < GroupSize; index++)
                species[index] = BitConverter.ToUInt16(data, offset + (((group * GroupSize) + index) * PokeRecordSize));
            groups[group] = new StarterGroup(names[group], species);
        }
        return groups;
    }

    internal static void Validate(StarterGroup[]? groups, int speciesCount, GameConfig config)
    {
        var expected = config.ORAS ? OrasNames.Length : XyNames.Length;
        if (groups is null || groups.Length != expected)
            throw new WorkspaceException($"El juego requiere {expected} grupos de starters.");
        if (groups.Any(group => group is null || group.Species is null || group.Species.Length != GroupSize
            || group.Species.Any(species => species < 0 || species >= speciesCount)))
            throw new WorkspaceException("Cada grupo debe tener tres especies válidas.");
    }

    private static void WritePoke(byte[] data, int offset, StarterGroup[] groups)
    {
        for (var group = 0; group < groups.Length; group++)
        for (var index = 0; index < GroupSize; index++)
            BitConverter.GetBytes((ushort)groups[group].Species[index]).CopyTo(data, offset + (((group * GroupSize) + index) * PokeRecordSize));
    }

    private static void WriteField(byte[] data, FieldLayout layout, StarterGroup[] groups)
    {
        for (var group = 0; group < groups.Length; group++)
        for (var index = 0; index < GroupSize; index++)
        {
            var starterIndex = (group * GroupSize) + index;
            var at = layout.Offset + (layout.Entries[starterIndex] * layout.RecordSize);
            BitConverter.GetBytes((ushort)groups[group].Species[index]).CopyTo(data, at);
        }
    }

    private static int FindPokeOffset(byte[] data, GameConfig config)
    {
        if (data is null || data.Length < PokeHeaderOffset + sizeof(int))
            throw new WorkspaceException($"{PokeFile} está incompleto.");
        var offset = BitConverter.ToInt32(data, PokeHeaderOffset) + (config.ORAS ? 0 : 0x10);
        var required = (config.ORAS ? OrasNames.Length : XyNames.Length) * GroupSize * PokeRecordSize;
        if (offset < 0 || offset + required > data.Length)
            throw new WorkspaceException($"{PokeFile} no contiene la tabla de starters completa.");
        return offset;
    }

    private static FieldLayout FindFieldLayout(byte[] data, GameConfig config)
    {
        var entries = config.ORAS ? OrasFieldEntries : XyFieldEntries;
        var offset = config.ORAS ? OrasFieldOffset : XyFieldOffset;
        var recordSize = config.ORAS ? OrasFieldSize : XyFieldSize;
        var last = offset + ((entries.Max() + 1) * recordSize);
        if (data is null || offset < 0 || last > data.Length)
            throw new WorkspaceException($"{FieldFile} no contiene la tabla de starters completa.");
        return new FieldLayout(offset, recordSize, entries);
    }

    private static byte[] ReadSource(GameWorkspace workspace, string file)
    {
        var path = Path.Combine(workspace.RomFsPath, file);
        if (!File.Exists(path))
            throw new WorkspaceException($"Falta {file} en el RomFS. Es necesario para editar starters.");
        return File.ReadAllBytes(path);
    }

    private static void RequireFiles(GameWorkspace workspace)
    {
        ReadSource(workspace, PokeFile);
        ReadSource(workspace, FieldFile);
    }

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation != 6 || (!config.XY && !config.ORAS))
            throw new WorkspaceException("El editor de starters está disponible solo para X/Y y OR/AS.");
    }

    private sealed record FieldLayout(int Offset, int RecordSize, int[] Entries);
}
