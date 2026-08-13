using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>Gen VII Move Tutor lists and prices stored in <c>Shop.cro</c>.</summary>
public static class TutorEditor
{
    private const string CroFile = "Shop.cro";
    private const int LengthOffset = 0x52D2;
    private const int DataOffset = 0x54DE;
    private static readonly string[] GroupNames = ["Big Wave Beach", "Heahea Beach", "Ula'ula Beach", "Battle Tree"];

    public static TutorTableResponse GetTable(TutorTableRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var config = EditorSession.OpenReadOnly(workspace, request.Language);
        EnsureSupported(config);
        var groups = Read(File.ReadAllBytes(RequireCro(workspace)), config);
        return new TutorTableResponse(config.Version.ToString(), groups, Catalogs.Moves(config),
            "Shop.cro se modifica dentro de RomFS. La salida es un parche LayeredFS.");
    }

    public static ExportResult Export(TutorExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        RequireCro(workspace);
        return EditorSession.ExportLooseFiles(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "tutors", [CroFile], (_, config, scratchRomFs) =>
            {
                EnsureSupported(config);
                var path = Path.Combine(scratchRomFs, CroFile);
                var data = File.ReadAllBytes(path);
                var groups = Validate(request.Groups, Catalogs.MoveCount(config), ReadLengths(data));
                Write(data, groups);
                File.WriteAllBytes(path, data);
            });
    }

    internal static TutorGroup[] Read(byte[] data, GameConfig config)
    {
        var lengths = ReadLengths(data);
        var groups = new TutorGroup[lengths.Length];
        var offset = DataOffset;
        for (var group = 0; group < lengths.Length; group++)
        {
            var entries = new TutorEntry[lengths[group]];
            for (var index = 0; index < entries.Length; index++)
            {
                var at = offset + (index * 4);
                entries[index] = new TutorEntry(BitConverter.ToUInt16(data, at), BitConverter.ToUInt16(data, at + 2));
            }
            groups[group] = new TutorGroup(GroupNames[group], entries);
            offset += entries.Length * 4;
        }
        return groups;
    }

    internal static TutorGroup[] Validate(TutorGroup[]? groups, int moveCount, int[] expectedLengths)
    {
        if (groups is null || expectedLengths.Length != GroupNames.Length || groups.Length != expectedLengths.Length)
            throw new WorkspaceException("Las listas de tutores deben conservar sus cantidades y usar movimientos/precios válidos.");

        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var group = groups[groupIndex];
            if (group is null || group.Entries is null || group.Entries.Length != expectedLengths[groupIndex])
                throw new WorkspaceException("Las listas de tutores deben conservar sus cantidades y usar movimientos/precios válidos.");

            foreach (var entry in group.Entries)
                if (entry.Move < 0 || entry.Move >= moveCount || entry.Price < 0 || entry.Price > ushort.MaxValue)
                    throw new WorkspaceException("Las listas de tutores deben conservar sus cantidades y usar movimientos/precios válidos.");
        }

        return groups;
    }

    private static void Write(byte[] data, TutorGroup[] groups)
    {
        var offset = DataOffset;
        foreach (var group in groups)
        {
            foreach (var entry in group.Entries)
            {
                BitConverter.GetBytes((ushort)entry.Move).CopyTo(data, offset);
                BitConverter.GetBytes((ushort)entry.Price).CopyTo(data, offset + 2);
                offset += 4;
            }
        }
    }

    private static int[] ReadLengths(byte[] data)
    {
        if (data is null || data.Length < DataOffset)
            throw new WorkspaceException("Shop.cro está incompleto.");
        var lengths = data.AsSpan(LengthOffset, 4).ToArray().Select(value => (int)value).ToArray();
        var required = DataOffset + (lengths.Sum(length => length) * 4);
        if (required > data.Length || lengths.Length != GroupNames.Length)
            throw new WorkspaceException("Shop.cro no contiene listas de tutores completas.");
        return lengths;
    }

    private static string RequireCro(GameWorkspace workspace)
    {
        var path = Path.Combine(workspace.RomFsPath, CroFile);
        if (!File.Exists(path))
            throw new WorkspaceException($"Falta {CroFile} en el RomFS. Es necesario para editar tutores.");
        return path;
    }

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation != 7)
            throw new WorkspaceException("El editor de tutores está disponible para Gen. VII.");
    }
}
