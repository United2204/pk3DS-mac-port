using System;
using System.Linq;

namespace pk3DS.Core;

public class Script
{
    public int Length => BitConverter.ToInt32(Raw, 0x00);
    public uint Magic => BitConverter.ToUInt32(Raw, 0x04);
    // case 0x0A0AF1E0: code = read_code_block(f); break;
    // case 0x0A0AF1EF: debug = read_debug_block(f); break;
    public bool Debug => Magic == 0x0A0AF1EF;

    public ushort PtrOffset => BitConverter.ToUInt16(Raw, 0x08);
    public ushort PtrCount => BitConverter.ToUInt16(Raw, 0x0A);

    public int ScriptInstructionStart => BitConverter.ToInt32(Raw, 0x0C);
    public int ScriptMovementStart => BitConverter.ToInt32(Raw, 0x10);
    public int FinalOffset => BitConverter.ToInt32(Raw, 0x14);
    public int AllocatedMemory => BitConverter.ToInt32(Raw, 0x18);

    // Generated Attributes
    public int CompressedLength => Length - ScriptInstructionStart;
    public byte[] CompressedBytes => Raw.Skip(ScriptInstructionStart).ToArray();
    public int DecompressedLength => FinalOffset - ScriptInstructionStart;
    public uint[] DecompressedInstructions => Scripts.QuickDecompress(CompressedBytes, DecompressedLength / 4);

    public uint[] ScriptCommands => DecompressedInstructions.Take((ScriptMovementStart - ScriptInstructionStart) / 4).ToArray();
    public uint[] MoveCommands => DecompressedInstructions.Skip((ScriptMovementStart - ScriptInstructionStart) / 4).ToArray();
    public string[] ParseScript => Scripts.ParseScript(ScriptCommands);
    public string[] ParseMoves => Scripts.ParseMovement(MoveCommands);

    public string Info => "Data Start: 0x" + ScriptInstructionStart.ToString("X4")
                                           + Environment.NewLine + "Movement Offset: 0x" + ScriptMovementStart.ToString("X4")
                                           + Environment.NewLine + "Total Used Size: 0x" + FinalOffset.ToString("X4")
                                           + Environment.NewLine + "Reserved Size: 0x" + AllocatedMemory.ToString("X4")
                                           + Environment.NewLine + "Compressed Len: 0x" + CompressedLength.ToString("X4")
                                           + Environment.NewLine + "Decompressed Len: 0x" + DecompressedLength.ToString("X4")
                                           + Environment.NewLine + "Compression Ratio: " +
                                           ((DecompressedLength - CompressedLength) / (decimal)DecompressedLength).ToString("p1");

    public byte[] Raw;

    public Script(byte[] data = null)
    {
        Raw = data ?? [];

        // sub_51AAFC
        if ((Raw[8] & 1) != 0)
            throw new ArgumentException("Multi-environment script!?");
    }

    public byte[] Write()
    {
        return Raw;
    }

    /// <summary>
    /// Rebuilds the compressed payload after changing instruction values while preserving the
    /// script layout. The instruction count must remain unchanged so movement offsets and any
    /// references outside this structure remain valid.
    /// </summary>
    public byte[] WriteInstructions(uint[] instructions)
    {
        if (Raw is null || Raw.Length < 0x1C || ScriptInstructionStart < 0x1C ||
            ScriptInstructionStart > Raw.Length || DecompressedLength < 0 || DecompressedLength % 4 != 0)
            throw new ArgumentException("El script no tiene un encabezado serializable.");
        if (instructions is null || instructions.Length != DecompressedLength / 4)
            throw new ArgumentException("La cantidad de instrucciones debe permanecer sin cambios.");

        var compressed = Scripts.CompressScript(Scripts.GetBytes(instructions));
        if (compressed is null)
            throw new ArgumentException("No se pudo comprimir el bloque de instrucciones.");

        var result = new byte[ScriptInstructionStart + compressed.Length];
        Buffer.BlockCopy(Raw, 0, result, 0, ScriptInstructionStart);
        BitConverter.GetBytes(result.Length).CopyTo(result, 0);
        Buffer.BlockCopy(compressed, 0, result, ScriptInstructionStart, compressed.Length);
        return result;
    }
}
