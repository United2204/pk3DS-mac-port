using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace pk3DS.Core.CTR;

/// <summary>
/// Safe, platform-neutral reader for ALYT containers. ALYT stores a small label/symbol section
/// before an embedded SARC; this class extracts that payload without relying on BinaryReader's
/// unchecked seeks or the legacy Windows-only tool path.
/// </summary>
public sealed class ALYTPortable
{
    private const int HeaderSize = 0x28;
    private const int SectionAlignment = 0x80;

    public int LabelCount { get; }
    public int SymbolCount { get; }
    public int DataOffset { get; }
    public int DataSize { get; }
    public string[] Labels { get; }
    public string[] Symbols { get; }
    public byte[] Data { get; }

    private ALYTPortable(
        int labelCount, int symbolCount, int dataOffset, int dataSize,
        string[] labels, string[] symbols, byte[] data)
    {
        LabelCount = labelCount;
        SymbolCount = symbolCount;
        DataOffset = dataOffset;
        DataSize = dataSize;
        Labels = labels;
        Symbols = symbols;
        Data = data;
    }

    public static ALYTPortable Read(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length < HeaderSize)
            throw new InvalidDataException("ALYT es demasiado pequeño para contener su cabecera.");
        if (!source.AsSpan(0, 4).SequenceEqual("ALYT"u8))
            throw new InvalidDataException("La cabecera ALYT no es válida.");

        var ltblOffset = ReadInt32(source, 8, "LTBL offset");
        var ltblSize = ReadInt32(source, 12, "LTBL size");
        var lmtlOffset = ReadInt32(source, 16, "LMTL offset");
        var lmtlSize = ReadInt32(source, 20, "LMTL size");
        var lfnlOffset = ReadInt32(source, 24, "LFNL offset");
        var lfnlSize = ReadInt32(source, 28, "LFNL size");
        var dataOffset = ReadInt32(source, 32, "data offset");
        var dataSize = ReadInt32(source, 36, "data size");

        if (dataOffset < HeaderSize || dataSize < 8 || dataOffset > source.Length
            || dataSize > source.Length - dataOffset)
        {
            throw new InvalidDataException("El bloque de datos ALYT sale de los límites del archivo.");
        }

        ValidateTable(source, ltblOffset, ltblSize, "LTBL", dataOffset);
        ValidateTable(source, lmtlOffset, lmtlSize, "LMTL", dataOffset);
        ValidateTable(source, lfnlOffset, lfnlSize, "LFNL", dataOffset);

        var dataEnd = checked(dataOffset + dataSize);
        var cursor = dataOffset;
        var labelCount = ReadCount(source, ref cursor, dataEnd, "etiquetas");
        var labels = ReadNames(source, ref cursor, labelCount, 0x40, dataEnd, "etiquetas");
        var symbolCount = ReadCount(source, ref cursor, dataEnd, "símbolos");
        var symbols = ReadNames(source, ref cursor, symbolCount, 0x20, dataEnd, "símbolos");

        while (cursor < dataEnd && source[cursor] == 0)
            cursor++;
        if (cursor >= dataEnd)
            throw new InvalidDataException("ALYT no contiene un archivo embebido.");

        return new ALYTPortable(
            labelCount,
            symbolCount,
            dataOffset,
            dataSize,
            labels,
            symbols,
            source.AsSpan(cursor, dataEnd - cursor).ToArray());
    }

    /// <summary>
    /// Wraps a SARC in an ALYT container. Labels and symbols use the fixed-width UTF-8 slots
    /// used by the format; omitted lists produce valid empty sections.
    /// </summary>
    public static byte[] Pack(
        byte[] sarc,
        IReadOnlyList<string> labels = null,
        IReadOnlyList<string> symbols = null)
    {
        ArgumentNullException.ThrowIfNull(sarc);
        if (sarc.Length < 4 || !sarc.AsSpan(0, 4).SequenceEqual("SARC"u8))
            throw new InvalidDataException("El contenido ALYT debe ser un SARC válido.");

        labels ??= Array.Empty<string>();
        symbols ??= Array.Empty<string>();
        var labelBytes = EncodeNames(labels, 0x40, "etiquetas");
        var symbolBytes = EncodeNames(symbols, 0x20, "símbolos");

        var ltblOffset = HeaderSize;
        var ltblSize = checked(8 + labels.Count * sizeof(int));
        var lmtlOffset = checked(ltblOffset + ltblSize);
        var lmtlSize = checked(8 + symbols.Count * sizeof(int));
        var lfnlOffset = checked(lmtlOffset + lmtlSize);
        var lfnlSize = 8;
        var dataOffset = Align(checked(lfnlOffset + lfnlSize), SectionAlignment);

        using var dataStream = new MemoryStream();
        using (var dataWriter = new BinaryWriter(dataStream, Encoding.UTF8, leaveOpen: true))
        {
            dataWriter.Write(labels.Count);
            foreach (var value in labelBytes)
                dataWriter.Write(value);
            dataWriter.Write(symbols.Count);
            foreach (var value in symbolBytes)
                dataWriter.Write(value);

            var absoluteSarcOffset = Align(checked(dataOffset + (int)dataStream.Position), SectionAlignment);
            while (checked(dataOffset + dataStream.Position) < absoluteSarcOffset)
                dataWriter.Write((byte)0);
            dataWriter.Write(sarc);
        }

        var data = dataStream.ToArray();
        var dataSize = data.Length;
        var totalLength = Align(checked(dataOffset + dataSize), SectionAlignment);
        var output = new byte[totalLength];
        using var stream = new MemoryStream(output, writable: true);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("ALYT"u8.ToArray());
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(ltblOffset);
        writer.Write(ltblSize);
        writer.Write(lmtlOffset);
        writer.Write(lmtlSize);
        writer.Write(lfnlOffset);
        writer.Write(lfnlSize);
        writer.Write(dataOffset);
        writer.Write(dataSize);
        WriteTableHeader(writer, "LTBL");
        WriteTableValues(writer, labels.Count);
        WriteTableHeader(writer, "LMTL");
        WriteTableValues(writer, symbols.Count);
        WriteTableHeader(writer, "LFNL");
        stream.Position = dataOffset;
        writer.Write(data);
        return output;
    }

    private static byte[][] EncodeNames(IReadOnlyList<string> names, int slotSize, string section)
    {
        if (names.Count > 0x100000)
            throw new InvalidDataException($"La cantidad de {section} ALYT es demasiado grande.");

        var result = new byte[names.Count][];
        for (var index = 0; index < names.Count; index++)
        {
            var value = names[index] ?? throw new InvalidDataException($"La entrada {index} de {section} ALYT es nula.");
            if (value.IndexOf('\0') >= 0)
                throw new InvalidDataException($"La entrada {index} de {section} ALYT contiene un terminador nulo.");
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length >= slotSize)
                throw new InvalidDataException($"La entrada {index} de {section} ALYT supera los {slotSize - 1} bytes UTF-8.");
            result[index] = new byte[slotSize];
            bytes.CopyTo(result[index], 0);
        }
        return result;
    }

    private static void WriteTableHeader(BinaryWriter writer, string magic)
    {
        writer.Write(Encoding.ASCII.GetBytes(magic));
        writer.Write((short)0);
        writer.Write((short)0);
    }

    private static void WriteTableValues(BinaryWriter writer, int count)
    {
        for (var index = 0; index < count; index++)
            writer.Write(0);
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static void ValidateTable(byte[] source, int offset, int size, string magic, int dataOffset)
    {
        if (offset < HeaderSize || size < 8 || (size - 8) % 4 != 0
            || offset > dataOffset || size > dataOffset - offset)
        {
            throw new InvalidDataException($"La tabla {magic} de ALYT sale de los límites.");
        }

        if (!source.AsSpan(offset, 4).SequenceEqual(Encoding.ASCII.GetBytes(magic)))
            throw new InvalidDataException($"La tabla {magic} de ALYT no tiene la firma esperada.");
    }

    private static int ReadCount(byte[] source, ref int cursor, int end, string name)
    {
        if (cursor > end - 4)
            throw new InvalidDataException($"ALYT no contiene la cantidad de {name}.");
        var count = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(cursor, 4));
        cursor += 4;
        if (count < 0 || count > (end - cursor) / 4)
            throw new InvalidDataException($"La cantidad de {name} de ALYT no es válida.");
        return count;
    }

    private static string[] ReadNames(byte[] source, ref int cursor, int count, int slotSize, int end, string name)
    {
        var totalBytes = checked(count * slotSize);
        if (totalBytes > end - cursor)
            throw new InvalidDataException($"La tabla de {name} de ALYT sale de los límites.");

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var names = new string[count];
        for (var index = 0; index < count; index++)
        {
            var slot = source.AsSpan(cursor, slotSize);
            var length = slot.IndexOf((byte)0);
            names[index] = encoding.GetString(slot[..(length >= 0 ? length : slotSize)]);
            cursor += slotSize;
        }
        return names;
    }

    private static int ReadInt32(byte[] source, int offset, string name)
    {
        if (offset < 0 || offset > source.Length - 4)
            throw new InvalidDataException($"No pude leer {name} de ALYT.");
        return BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset, 4));
    }
}
