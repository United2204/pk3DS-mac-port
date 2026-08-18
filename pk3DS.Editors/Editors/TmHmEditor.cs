using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>
/// TMs and HMs stored in the decompressed ExeFS <c>code.bin</c>. Gen VI stores 100 TMs and
/// 5/7 HMs in a split table; Gen VII stores 100 TMs and no HM table. The editor preserves the
/// original code file and exports only the patched ExeFS file for Luma.
/// </summary>
public static class TmHmEditor
{
    private const int WordSize = sizeof(ushort);
    private const int Gen6TmCount = 100;
    private const int Gen6XyHmCount = 5;
    private const int Gen6OrasHmCount = 7;
    private const int Gen7TmCount = 100;
    private const int Alignment = 0x200;

    private static readonly byte[] Gen6Signature = [0xD4, 0x00, 0xAE, 0x02, 0xAF, 0x02, 0xB0, 0x02];
    private static readonly byte[] Gen7Signature = [0x03, 0x40, 0x03, 0x41, 0x03, 0x42, 0x03, 0x43, 0x03];

    public static TmHmTableResponse GetTable(TmHmTableRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupported(config);
        var code = ReadCode(workspace);
        var lists = ReadLists(config, code);
        return new TmHmTableResponse(config.Version.ToString(), lists.TMs, lists.HMs,
            Catalogs.Moves(config), Warning(config));
    }

    public static ExportResult Export(TmHmExportRequest request) =>
        EditorSession.ExportExeFs(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "tmhm", (_, config, code) =>
            {
                EnsureSupported(config);
                var lists = Validate(request.TMs, request.HMs, Catalogs.MoveCount(config), config);
                var offset = FindTableOffset(config, code);
                WriteLists(config, code, offset, lists.TMs, lists.HMs);
                return code;
            });

    internal static (int[] TMs, int[] HMs) ReadLists(GameConfig config, byte[] code)
    {
        var offset = FindTableOffset(config, code);
        var raw = new ushort[RawSlotCount(config)];
        for (var index = 0; index < raw.Length; index++)
            raw[index] = BitConverter.ToUInt16(code, offset + (index * WordSize));

        var tms = raw.Take(Gen6TmCount).Select(value => (int)value).ToArray();
        if (config.Generation == 7)
            return (tms, []);

        var hms = raw.Skip(Gen6TmCount - 8).Take(Gen6XyHmCount).Select(value => (int)value).ToList();
        if (config.ORAS)
        {
            // OR/AS puts HM06 at raw[97] and HM07 at raw[106], around TM93..TM100.
            hms = raw.Skip(92).Take(5).Select(value => (int)value).ToList();
            hms.Add(raw[97]);
            hms.Add(raw[106]);
        }
        return (tms, hms.ToArray());
    }

    internal static int FindTableOffset(GameConfig config, byte[] code)
    {
        if (code is null || code.Length == 0 || code.Length % Alignment != 0)
            throw new WorkspaceException("El code.bin debe estar descomprimido y alineado a 0x200 bytes.");

        var signature = config.Generation == 6 ? Gen6Signature : Gen7Signature;
        var found = code.Length > 0x400000
            ? Util.IndexOfBytes(code, signature, 0x400000, 0)
            : -1;
        var offset = found >= 0
            ? found + signature.Length
            : DefaultOffset(config);
        if (config.Generation == 7 && config.USUM && found >= 0)
            offset += 0x22;

        var required = RawSlotCount(config) * WordSize;
        if (offset < 0 || offset + required > code.Length)
            throw new WorkspaceException("No encuentro una tabla TMs/HMs completa en code.bin.");
        return offset;
    }

    private static void WriteLists(GameConfig config, byte[] code, int offset, int[] tms, int[] hms)
    {
        var raw = new ushort[RawSlotCount(config)];
        for (var index = 0; index < Gen7TmCount; index++)
            raw[index] = (ushort)tms[index];
        if (config.Generation == 6)
        {
            for (var index = 0; index < 5; index++)
                raw[92 + index] = (ushort)hms[index];
            if (config.ORAS)
            {
                raw[97] = (ushort)hms[5];
                for (var index = 98; index < 106; index++)
                    raw[index] = (ushort)tms[index - 6];
                raw[106] = (ushort)hms[6];
            }
            else
            {
                for (var index = 97; index < 105; index++)
                    raw[index] = (ushort)tms[index - 5];
            }
        }

        for (var index = 0; index < raw.Length; index++)
            BitConverter.GetBytes(raw[index]).CopyTo(code, offset + (index * WordSize));
    }

    internal static (int[] TMs, int[] HMs) Validate(int[]? tms, int[]? hms, int moveCount, GameConfig config)
    {
        var expectedHms = config.Generation == 7 ? 0 : config.ORAS ? Gen6OrasHmCount : Gen6XyHmCount;
        if (tms is not { Length: Gen7TmCount }
            || hms is null || hms.Length != expectedHms
            || tms.Any(move => move < 0 || move >= moveCount)
            || hms.Any(move => move < 0 || move >= moveCount))
            throw new WorkspaceException($"La tabla debe tener 100 TMs y {expectedHms} HMs con IDs de movimiento válidos.");
        return (tms, hms);
    }

    private static int RawSlotCount(GameConfig config) => config.Generation == 7
        ? Gen7TmCount
        : config.ORAS ? 107 : 105;

    private static int DefaultOffset(GameConfig config) => config.Generation == 7
        ? 0x0059795A
        : config.ORAS ? 0x004A67EE : 0x00464796;

    private static byte[] ReadCode(GameWorkspace workspace)
        => EditorSession.ReadCode(workspace);

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation is not (6 or 7))
            throw new WorkspaceException("El editor de TMs/HMs requiere un juego de Gen. VI o Gen. VII.");
    }

    private static string Warning(GameConfig config) => config.Generation == 6
        ? "La aplicación normaliza automáticamente code.bin BLZ. La salida es un parche ExeFS para Luma; los HMs de Gen. VI también se editan."
        : "La aplicación normaliza automáticamente code.bin BLZ. Gen. VII almacena 100 TMs y no tiene tabla HM separada.";
}
