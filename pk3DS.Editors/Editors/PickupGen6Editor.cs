using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>Pickup's 18 common and 11 rare item slots in Gen VI ExeFS code.bin.</summary>
public static class PickupGen6Editor
{
    private const int CommonCount = 0x12;
    private const int RareCount = 0xB;
    private const int WordSize = sizeof(ushort);
    private static readonly byte[] Signature =
    [
        0x1E, 0x28, 0x32, 0x3C, 0x46, 0x50, 0x5A, 0x5E, 0x62,
        0x05, 0x0A, 0x0F, 0x14, 0x19, 0x1E, 0x23, 0x28, 0x2D, 0x32,
    ];

    public static PickupGen6TableResponse GetTable(PickupGen6TableRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupported(config);
        var code = ReadCode(workspace);
        var (common, rare) = ReadLists(config, code);
        return new PickupGen6TableResponse(config.Version.ToString(), common, rare, Catalogs.Items(config),
            "El code.bin debe estar descomprimido. La salida es un parche ExeFS para Luma.");
    }

    public static ExportResult Export(PickupGen6ExportRequest request) =>
        EditorSession.ExportExeFs(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "pickup6", (_, config, code) =>
            {
                EnsureSupported(config);
                Validate(request.Common, request.Rare, Catalogs.ItemCount(config));
                var offset = FindOffset(config, code);
                Write(code, offset, request.Common, request.Rare);
                return code;
            });

    internal static (int[] Common, int[] Rare) ReadLists(GameConfig config, byte[] code)
    {
        var offset = FindOffset(config, code);
        var values = Enumerable.Range(0, CommonCount + RareCount)
            .Select(index => (int)BitConverter.ToUInt16(code, offset + (index * WordSize)))
            .ToArray();
        return (values[..CommonCount], values[CommonCount..]);
    }

    internal static int FindOffset(GameConfig config, byte[] code)
    {
        if (code is null || code.Length == 0 || code.Length % 0x200 != 0)
            throw new WorkspaceException("El code.bin debe estar descomprimido y alineado a 0x200 bytes.");
        var found = code.Length > 0x400000 ? Util.IndexOfBytes(code, Signature, 0x400000, 0) : -1;
        var offset = found >= 0 ? found - 0x3A : config.ORAS ? 0x004872FC : 0x004455A8;
        if (offset < 0 || offset + ((CommonCount + RareCount) * WordSize) > code.Length)
            throw new WorkspaceException("No encuentro la tabla Pickup completa en code.bin.");
        return offset;
    }

    internal static void Validate(int[]? common, int[]? rare, int itemCount)
    {
        if (common is not { Length: CommonCount } || rare is not { Length: RareCount }
            || common.Any(item => item < 0 || item >= itemCount)
            || rare.Any(item => item < 0 || item >= itemCount))
            throw new WorkspaceException("Pickup Gen. VI requiere 18 objetos comunes y 11 raros con IDs válidos.");
    }

    private static void Write(byte[] code, int offset, int[] common, int[] rare)
    {
        foreach (var (index, item) in common.Concat(rare).Select((item, index) => (index, item)))
            BitConverter.GetBytes((ushort)item).CopyTo(code, offset + (index * WordSize));
    }

    private static byte[] ReadCode(GameWorkspace workspace)
    {
        if (workspace.ExeFsPath is null)
            throw new WorkspaceException("Falta ExeFS. Extraé el code.bin descomprimido para editar Pickup.");
        var path = Directory.EnumerateFiles(workspace.ExeFsPath)
            .FirstOrDefault(file => Path.GetFileName(file).Contains("code", StringComparison.OrdinalIgnoreCase));
        return path is null ? throw new WorkspaceException("No encuentro code.bin dentro de ExeFS.") : File.ReadAllBytes(path);
    }

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation != 6 || (!config.XY && !config.ORAS))
            throw new WorkspaceException("Pickup Gen. VI está disponible solo para X/Y y OR/AS.");
    }
}
