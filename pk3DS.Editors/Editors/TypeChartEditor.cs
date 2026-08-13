using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>18×18 type-effectiveness matrix: DllBattle.cro in Gen VI and code.bin in Gen VII.</summary>
public static class TypeChartEditor
{
    private const int TypeCount = 18;
    private const int ChartLength = TypeCount * TypeCount;
    private const string Gen6File = "DllBattle.cro";
    private const int XyOffset = 0x000D12A8;
    private const int OrasOffset = 0x000DB428;
    private const int Alignment = 0x200;
    private static readonly byte[] Gen7Signature =
    [
        0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
        0xC3, 0x00, 0x00, 0x00, 0xCB, 0x00, 0x00, 0x00, 0xD3, 0x00, 0x00, 0x00, 0xDB, 0x00, 0x00, 0x00,
        0xF3, 0x00, 0x00, 0x00, 0xFB, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
    ];
    private static readonly int[] ValidValues = [0, 2, 4, 8];

    public static TypeChartTableResponse GetTable(TypeChartTableRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupported(config);
        var chart = config.Generation == 6
            ? ReadGen6(workspace, config)
            : ReadGen7(workspace);
        return new TypeChartTableResponse(config.Version.ToString(), TypeCount, chart,
            Catalogs.Types(config),
            config.Generation == 6
                ? "Gen. VI modifica DllBattle.cro. La salida es un parche RomFS para Luma."
                : "Gen. VII modifica code.bin descomprimido. La salida es un parche ExeFS para Luma.");
    }

    public static ExportResult Export(TypeChartExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var config = EditorSession.OpenReadOnly(workspace, request.Language);
        EnsureSupported(config);
        Validate(request.Chart);

        return config.Generation == 6
            ? EditorSession.ExportLooseFiles(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
                "typechart", [Gen6File], (_, sourceConfig, scratchRomFs) =>
                {
                    EnsureSupported(sourceConfig);
                    var path = Path.Combine(scratchRomFs, Gen6File);
                    var data = File.ReadAllBytes(path);
                    Write(data, FindGen6Offset(sourceConfig), request.Chart);
                    File.WriteAllBytes(path, data);
                })
            : EditorSession.ExportExeFs(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
                "typechart", (_, sourceConfig, code) =>
                {
                    EnsureSupported(sourceConfig);
                    Write(code, FindGen7Offset(code), request.Chart);
                    return code;
                });
    }

    internal static int[] ReadGen6(GameWorkspace workspace, GameConfig config)
    {
        var path = Path.Combine(workspace.RomFsPath, Gen6File);
        if (!File.Exists(path))
            throw new WorkspaceException($"Falta {Gen6File} en el RomFS. Es necesario para editar la tabla de tipos.");
        var data = File.ReadAllBytes(path);
        return Read(data, FindGen6Offset(config));
    }

    internal static int[] ReadGen7(GameWorkspace workspace)
    {
        var code = ReadCode(workspace);
        return Read(code, FindGen7Offset(code));
    }

    internal static int[] Read(byte[] data, int offset)
    {
        if (data is null || offset < 0 || offset + ChartLength > data.Length)
            throw new WorkspaceException("El archivo no contiene una tabla de tipos completa.");
        return data.AsSpan(offset, ChartLength).ToArray().Select(value => (int)value).ToArray();
    }

    internal static void Validate(int[]? chart)
    {
        if (chart is not { Length: ChartLength } || chart.Any(value => !ValidValues.Contains(value)))
            throw new WorkspaceException("La tabla de tipos debe tener 324 celdas y usar solo los valores 0, 2, 4 u 8.");
    }

    internal static int FindGen6Offset(GameConfig config) =>
        config.ORAS ? OrasOffset : XyOffset;

    internal static int FindGen7Offset(byte[] code)
    {
        if (code is null || code.Length == 0 || code.Length % Alignment != 0)
            throw new WorkspaceException("El code.bin debe estar descomprimido y alineado a 0x200 bytes.");
        var found = code.Length > 0x400000 ? code.AsSpan(0x400000).IndexOf(Gen7Signature) : -1;
        var offset = found < 0 ? -1 : 0x400000 + found + Gen7Signature.Length;
        if (offset < 0 || offset + ChartLength > code.Length)
            throw new WorkspaceException("No encuentro la tabla de tipos completa en code.bin.");
        return offset;
    }

    private static void Write(byte[] data, int offset, int[] chart)
    {
        Validate(chart);
        chart.Select(value => (byte)value).ToArray().CopyTo(data, offset);
    }

    private static byte[] ReadCode(GameWorkspace workspace)
    {
        if (workspace.ExeFsPath is null)
            throw new WorkspaceException("Falta ExeFS. Extraé el code.bin descomprimido para editar la tabla de tipos.");
        var path = Directory.EnumerateFiles(workspace.ExeFsPath)
            .FirstOrDefault(file => Path.GetFileName(file).Contains("code", StringComparison.OrdinalIgnoreCase));
        return path is null ? throw new WorkspaceException("No encuentro code.bin dentro de ExeFS.") : File.ReadAllBytes(path);
    }

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation is not (6 or 7))
            throw new WorkspaceException("La tabla de tipos requiere un juego de Gen. VI o Gen. VII.");
    }
}
