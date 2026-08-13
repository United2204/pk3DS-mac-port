using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>Gen VI tutor move lists stored after the tutor resource path in ExeFS code.bin.</summary>
public static class TutorGen6Editor
{
    private const int SearchStart = 0x400000;
    private const int Alignment = 0x200;
    private static readonly int[] Lengths = [0xF, 0x11, 0x10, 0xF];
    private static readonly string[] Names = ["Tutor 1", "Tutor 2", "Tutor 3", "Tutor 4"];
    private static readonly byte[] VanillaSignature =
    [
        0x00, 0x46, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x54, 0x79, 0x70, 0x65, 0x00, 0x00, 0x45, 0x64, 0x67,
        0x65, 0x49, 0x44, 0x00, 0xFF,
    ];
    private static readonly byte[] PatchedSignature =
    [
        0x00, 0x46, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x54, 0x79, 0x70, 0x65, 0x00, 0x00, 0x45, 0x64, 0x67,
        0x65, 0x49, 0x44, 0x00, 0x00, 0x63, 0x3A, 0x5C, 0x72, 0x65, 0x76, 0x69, 0x73, 0x69, 0x6F, 0x6E,
        0x31, 0x5F, 0x73, 0x61, 0x6E, 0x67, 0x6F, 0x5C, 0x73, 0x61, 0x6E, 0x67, 0x6F, 0x5F, 0x70, 0x72,
        0x6F, 0x6A, 0x65, 0x63, 0x74, 0x5C, 0x70, 0x72, 0x6F, 0x67, 0x5C, 0x73, 0x72, 0x63, 0x2F, 0x73,
        0x79, 0x73, 0x74, 0x65, 0x6D, 0x2F, 0x6D, 0x6F, 0x74, 0x69, 0x6F, 0x6E, 0x2F, 0x4D, 0x6F, 0x74,
        0x69, 0x6F, 0x6E, 0x2E, 0x63, 0x70, 0x70, 0x00, 0x00,
    ];

    public static TutorGen6TableResponse GetTable(TutorGen6TableRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupported(config);
        return new TutorGen6TableResponse(config.Version.ToString(), Read(ReadCode(workspace)), Catalogs.Moves(config),
            "Gen. VI modifica las cuatro listas de tutores en code.bin. La salida es un parche ExeFS para Luma.");
    }

    public static ExportResult Export(TutorGen6ExportRequest request) =>
        EditorSession.ExportExeFs(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "tutors6", (_, config, code) =>
            {
                EnsureSupported(config);
                var groups = Validate(request.Groups, Catalogs.MoveCount(config));
                Write(code, groups);
                return code;
            });

    internal static TutorGen6Group[] Read(byte[] code)
    {
        var offset = FindDataOffset(code);
        var groups = new TutorGen6Group[Lengths.Length];
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var moves = new int[Lengths[groupIndex]];
            for (var index = 0; index < moves.Length; index++)
                moves[index] = BitConverter.ToUInt16(code, offset + (index * sizeof(ushort)));
            groups[groupIndex] = new TutorGen6Group(Names[groupIndex], moves);
            offset += (moves.Length + 1) * sizeof(ushort);
        }
        return groups;
    }

    internal static TutorGen6Group[] Validate(TutorGen6Group[]? groups, int moveCount)
    {
        if (groups is null || groups.Length != Lengths.Length)
            throw new WorkspaceException("Las listas de tutores Gen. VI deben conservar su estructura original.");
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            if (groups[groupIndex] is null || groups[groupIndex].Moves is null || groups[groupIndex].Moves.Length != Lengths[groupIndex]
                || groups[groupIndex].Moves.Any(move => move < 0 || move >= moveCount))
                throw new WorkspaceException("Las listas de tutores Gen. VI deben conservar su estructura y usar movimientos válidos.");
        return groups;
    }

    internal static int FindDataOffset(byte[] code)
    {
        if (code is null || code.Length == 0 || code.Length % Alignment != 0)
            throw new WorkspaceException("El code.bin debe estar descomprimido y alineado a 0x200 bytes.");
        var start = Math.Min(SearchStart, code.Length);
        var found = code.AsSpan(start).IndexOf(VanillaSignature);
        var length = VanillaSignature.Length;
        if (found < 0)
        {
            found = code.AsSpan(start).IndexOf(PatchedSignature);
            length = PatchedSignature.Length;
        }
        var offset = found < 0 ? -1 : start + found + length;
        var required = Lengths.Sum(lengthValue => (lengthValue + 1) * sizeof(ushort));
        if (offset < 0 || offset + required > code.Length)
            throw new WorkspaceException("No encuentro las listas de tutores completas en code.bin.");
        return offset;
    }

    private static void Write(byte[] code, TutorGen6Group[] groups)
    {
        var offset = FindDataOffset(code);
        foreach (var group in groups)
        {
            foreach (var move in group.Moves)
            {
                BitConverter.GetBytes((ushort)move).CopyTo(code, offset);
                offset += sizeof(ushort);
            }
            offset += sizeof(ushort); // Preserve each list's end cap.
        }
    }

    private static byte[] ReadCode(GameWorkspace workspace)
    {
        if (workspace.ExeFsPath is null)
            throw new WorkspaceException("Falta ExeFS. Extraé el code.bin descomprimido para editar tutores Gen. VI.");
        var path = Directory.EnumerateFiles(workspace.ExeFsPath)
            .FirstOrDefault(file => Path.GetFileName(file).Contains("code", StringComparison.OrdinalIgnoreCase));
        return path is null ? throw new WorkspaceException("No encuentro code.bin dentro de ExeFS.") : File.ReadAllBytes(path);
    }

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation != 6 || (!config.XY && !config.ORAS))
            throw new WorkspaceException("El editor de tutores está disponible solo para X/Y y OR/AS.");
    }
}
