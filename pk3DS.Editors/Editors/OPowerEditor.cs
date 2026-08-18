using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>Editable O-Power costs and progression values in Gen VI code.bin.</summary>
public static class OPowerEditor
{
    private const int EntryCount = 65;
    private const int EntrySize = 22;
    private const int SearchStart = 0x400000;
    private static readonly byte[] Signature = [0x34, 0x39, 0x34, 0x36, 0x31, 0x38, 0x34, 0x35, 0x00];

    public static OPowerTableResponse GetTable(OPowerTableRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupported(config);
        var code = ReadCode(workspace);
        var entries = Read(code, FindOffset(code));
        return new OPowerTableResponse(config.Version.ToString(), entries,
            "La aplicación normaliza automáticamente code.bin BLZ. Se editan costos, etapas, duración y eficacia; los campos internos restantes se conservan.");
    }

    public static ExportResult Export(OPowerExportRequest request) =>
        EditorSession.ExportExeFs(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "opowers", (_, config, code) =>
            {
                EnsureSupported(config);
                Validate(request.Entries);
                Write(code, FindOffset(code), request.Entries);
                return code;
            });

    internal static OPowerEntry[] Read(byte[] code, int offset)
    {
        var entries = new OPowerEntry[EntryCount];
        for (var index = 0; index < entries.Length; index++)
        {
            var at = offset + (index * EntrySize);
            entries[index] = new OPowerEntry(
                code[at + 3], code[at + 4], code[at + 0xE], code[at + 0xF],
                BitConverter.ToUInt16(code, at + 0x12), code[at + 0x14], code[at + 1]);
        }
        return entries;
    }

    internal static void Validate(OPowerEntry[]? entries)
    {
        if (entries is not { Length: EntryCount })
            throw new WorkspaceException($"O-Powers requiere exactamente {EntryCount} registros.");
        if (entries.Any(entry => entry.PlayerCost is < 0 or > byte.MaxValue
            || entry.OtherCost is < 0 or > byte.MaxValue
            || entry.Stage is < 0 or > byte.MaxValue
            || entry.LevelUp is < 0 or > byte.MaxValue
            || entry.Efficacy is < 0 or > 999
            || entry.Duration is < 0 or > byte.MaxValue
            || entry.Usability is not (0 or 2 or 254)))
            throw new WorkspaceException("Los valores de O-Powers están fuera del rango del formato Gen. VI.");
    }

    private static void Write(byte[] code, int offset, OPowerEntry[] entries)
    {
        for (var index = 0; index < entries.Length; index++)
        {
            var at = offset + (index * EntrySize);
            var entry = entries[index];
            code[at + 3] = (byte)entry.PlayerCost;
            code[at + 4] = (byte)entry.OtherCost;
            code[at + 0xE] = (byte)entry.Stage;
            code[at + 0xF] = (byte)entry.LevelUp;
            BitConverter.GetBytes((ushort)entry.Efficacy).CopyTo(code, at + 0x12);
            code[at + 0x14] = (byte)entry.Duration;
            code[at + 1] = (byte)entry.Usability;
        }
    }

    internal static int FindOffset(byte[] code)
    {
        if (code is null || code.Length == 0 || code.Length % 0x200 != 0)
            throw new WorkspaceException("El code.bin debe estar descomprimido y alineado a 0x200 bytes.");
        var found = code.Length > SearchStart ? code.AsSpan(SearchStart).IndexOf(Signature) : -1;
        var offset = found < 0 ? -1 : SearchStart + found + Signature.Length;
        while (offset >= 0 && offset < code.Length && code[offset] == 0xFF)
            offset++;
        if (offset < 0 || offset + (EntryCount * EntrySize) > code.Length)
            throw new WorkspaceException("No encuentro la tabla completa de O-Powers en code.bin.");
        return offset;
    }

    private static byte[] ReadCode(GameWorkspace workspace)
        => EditorSession.ReadCode(workspace);

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation != 6)
            throw new WorkspaceException("O-Powers está disponible solo para Gen. VI.");
    }
}
