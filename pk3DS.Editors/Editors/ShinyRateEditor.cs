using pk3DS.Core;
using pk3DS.Core.Properties;

namespace pk3DS.Editors;

/// <summary>Shiny reroll and optional always-shiny patches in the decompressed ExeFS code.</summary>
public static class ShinyRateEditor
{
    private const int Alignment = 0x200;
    private static readonly byte[] RerollPattern = [0x01, 0x50, 0x85, 0xE2, 0x05, 0x00, 0x50, 0xE1, 0xDE, 0xFF, 0xFF, 0xCA];
    private static readonly byte[] AlwaysShinyPattern = [0x00, 0x20, 0x22, 0xE0, 0x02, 0x30, 0x21, 0xE2, 0x03, 0x20, 0x92, 0xE1, 0x1C, 0x00, 0x00];
    private static readonly byte[] OriginalRerollInstruction = [0x23, 0x00, 0xD4, 0xE5];
    private static readonly RerollInstruction[] Instructions = LoadInstructions();

    public static ShinyRateTableResponse GetTable(ShinyRateTableRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupported(config);
        var code = ReadCode(workspace);
        var rerollOffset = FindRerollOffset(code);
        var rerolls = ReadRerolls(code, rerollOffset);
        var always = ReadAlwaysShiny(code);
        return new ShinyRateTableResponse(config.Version.ToString(), rerolls, always,
            Instructions.Select(instruction => instruction.Value).ToArray(),
            "La aplicación normaliza automáticamente code.bin BLZ. La salida es un parche ExeFS para Luma; los valores no incluidos se redondean al siguiente valor soportado.");
    }

    public static ExportResult Export(ShinyRateExportRequest request) =>
        EditorSession.ExportExeFs(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "shiny-rate", (_, config, code) =>
            {
                EnsureSupported(config);
                var rerollOffset = FindRerollOffset(code);
                WriteRerolls(code, rerollOffset, request.Rerolls);
                WriteAlwaysShiny(code, request.EverythingShiny);
                return code;
            });

    internal static int ReadRerolls(byte[] code, int offset)
    {
        if (code.AsSpan(offset, OriginalRerollInstruction.Length).SequenceEqual(OriginalRerollInstruction))
            return 0;
        var arg = BitConverter.ToUInt16(code, offset);
        return Instructions.FirstOrDefault(instruction => instruction.Argument == arg)?.Value ?? 0;
    }

    internal static bool ReadAlwaysShiny(byte[] code)
    {
        var pattern = Find(code, AlwaysShinyPattern);
        var opcode = pattern < 0 ? (byte)0 : code[pattern + AlwaysShinyPattern.Length];
        if (pattern >= 0 && opcode is not (0x0A or 0xEA))
            throw new WorkspaceException("La rutina de brillo de code.bin fue modificada y no puedo identificar su estado.");
        return opcode == 0xEA;
    }

    internal static int FindRerollOffset(byte[] code)
    {
        if (code is null || code.Length == 0 || code.Length % Alignment != 0)
            throw new WorkspaceException("El code.bin debe estar descomprimido y alineado a 0x200 bytes.");
        var pattern = Find(code, RerollPattern);
        var offset = pattern - OriginalRerollInstruction.Length;
        if (pattern < OriginalRerollInstruction.Length || offset + OriginalRerollInstruction.Length > code.Length)
            throw new WorkspaceException("No encuentro la rutina de generación de PID en code.bin.");
        return offset;
    }

    internal static void WriteRerolls(byte[] code, int offset, int rerolls)
    {
        if (rerolls < 0 || rerolls > ushort.MaxValue)
            throw new WorkspaceException("La cantidad de rerolls debe estar entre 0 y 65535.");
        if (rerolls == 0)
        {
            OriginalRerollInstruction.CopyTo(code, offset);
            return;
        }

        var instruction = Instructions.FirstOrDefault(option => option.Value >= rerolls) ?? Instructions[^1];
        BitConverter.GetBytes(instruction.Argument).CopyTo(code, offset);
        code[offset + 2] = 0xA0;
        code[offset + 3] = 0xE3;
    }

    internal static void WriteAlwaysShiny(byte[] code, bool enabled)
    {
        var pattern = Find(code, AlwaysShinyPattern);
        if (pattern < 0)
            return;
        var offset = pattern + AlwaysShinyPattern.Length;
        if (code[offset] is not (0x0A or 0xEA))
            throw new WorkspaceException("La rutina de brillo de code.bin fue modificada y no puedo cambiar su estado.");
        code[offset] = enabled ? (byte)0xEA : (byte)0x0A;
    }

    private static RerollInstruction[] LoadInstructions()
    {
        var raw = Resources.asm_mov;
        if (raw is null || raw.Length == 0 || raw.Length % 4 != 0)
            throw new InvalidOperationException("El catálogo de instrucciones de reroll está incompleto.");
        return Enumerable.Range(0, raw.Length / 4)
            .Select(index => new RerollInstruction(
                BitConverter.ToUInt16(raw, index * 4),
                BitConverter.ToUInt16(raw, (index * 4) + 2)))
            .ToArray();
    }

    private static int Find(byte[] data, byte[] pattern) => data.AsSpan().IndexOf(pattern);

    private static byte[] ReadCode(GameWorkspace workspace)
        => EditorSession.ReadCode(workspace);

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation is not (6 or 7))
            throw new WorkspaceException("Shiny Rate requiere un juego de Gen. VI o Gen. VII.");
    }

    private sealed record RerollInstruction(int Value, ushort Argument);
}
