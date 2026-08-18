using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace pk3DS.Core.CTR;

/// <summary>
/// Reader for the legacy GAR container exposed by the Windows archive tools.
/// GAR is distinct from Pokémon's GARC format and stores absolute offsets plus two name tables.
/// </summary>
public sealed class GarPortable
{
    private const uint Magic = 0x02524147;
    private const int MinimumHeaderSize = 0x3C;
    private const int MetaEntrySize = 0x0C;

    public uint FileLength { get; }
    public uint Unknown { get; }
    public uint HeaderLength { get; }
    public uint FileMetaOffset { get; }
    public uint FileOffsetsOffset { get; }
    public uint FileCountOffset { get; }
    public uint CtxbOffset { get; }
    public uint DataOffset { get; }
    public GarEntry[] Entries { get; }

    private GarPortable(
        uint fileLength,
        uint unknown,
        uint headerLength,
        uint fileMetaOffset,
        uint fileOffsetsOffset,
        uint fileCountOffset,
        uint ctxbOffset,
        uint dataOffset,
        GarEntry[] entries)
    {
        FileLength = fileLength;
        Unknown = unknown;
        HeaderLength = headerLength;
        FileMetaOffset = fileMetaOffset;
        FileOffsetsOffset = fileOffsetsOffset;
        FileCountOffset = fileCountOffset;
        CtxbOffset = ctxbOffset;
        DataOffset = dataOffset;
        Entries = entries;
    }

    public static bool HasHeader(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && ReadUInt32(data, 0) == Magic;

    public static GarPortable Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < MinimumHeaderSize)
            throw new InvalidDataException("La cabecera GAR está incompleta.");

        var span = data.AsSpan();
        if (ReadUInt32(span, 0) != Magic)
            throw new InvalidDataException("La cabecera GAR no es válida.");
        var fileLength = ReadUInt32(span, 4);
        if (fileLength != data.Length)
            throw new InvalidDataException("El tamaño declarado del GAR no coincide con el archivo.");

        var unknown = ReadUInt32(span, 8);
        var headerLength = ReadUInt32(span, 12);
        var fileMetaOffset = ReadUInt32(span, 16);
        var fileOffsetsOffset = ReadUInt32(span, 20);
        var fileCountOffset = ReadUInt32(span, 0x34);
        var ctxbOffset = ReadUInt32(span, 0x38);

        if (fileMetaOffset > data.Length || fileOffsetsOffset > data.Length)
            throw new InvalidDataException("Las tablas GAR salen de los límites del archivo.");

        var dataOffset = ReadUInt32At(span, fileOffsetsOffset);
        if (dataOffset < fileOffsetsOffset || dataOffset > data.Length
            || ((dataOffset - fileOffsetsOffset) % sizeof(uint)) != 0)
            throw new InvalidDataException("La tabla de offsets GAR no es válida.");

        var count = checked((int)((dataOffset - fileOffsetsOffset) / sizeof(uint)));
        if (count > 1_000_000)
            throw new InvalidDataException("El GAR declara demasiados archivos.");
        var metadataEnd = checked((long)fileMetaOffset + ((long)count * MetaEntrySize));
        var offsetsEnd = checked((long)fileOffsetsOffset + ((long)count * sizeof(uint)));
        if (metadataEnd > data.Length || offsetsEnd > data.Length)
            throw new InvalidDataException("Las tablas GAR están incompletas.");

        var offsets = new int[count];
        for (var index = 0; index < count; index++)
        {
            var offset = ReadUInt32At(span, checked(fileOffsetsOffset + ((uint)index * sizeof(uint))));
            if (offset < dataOffset || offset > data.Length)
                throw new InvalidDataException($"El archivo GAR {index} apunta fuera de los datos.");
            if (index > 0 && offset < offsets[index - 1])
                throw new InvalidDataException("La tabla de offsets GAR no está ordenada.");
            offsets[index] = checked((int)offset);
        }

        var entries = new GarEntry[count];
        for (var index = 0; index < count; index++)
        {
            var metadataOffset = checked(fileMetaOffset + ((uint)index * MetaEntrySize));
            var length = ReadUInt32At(span, metadataOffset);
            var nameOffset = ReadUInt32At(span, metadataOffset + 4);
            var nameWithExtensionOffset = ReadUInt32At(span, metadataOffset + 8);
            var start = offsets[index];
            var next = index + 1 < count ? offsets[index + 1] : data.Length;
            if (length > int.MaxValue || start > next || checked(start + (int)length) > next)
                throw new InvalidDataException($"El archivo GAR {index} tiene un rango inválido.");

            var name = ReadString(span, nameOffset, $"nombre GAR {index}");
            var nameWithExtension = ReadString(span, nameWithExtensionOffset, $"nombre GAR {index}");
            entries[index] = new GarEntry(index, name, nameWithExtension, start, checked((int)length),
                data.AsSpan(start, checked((int)length)).ToArray());
        }

        return new GarPortable(
            fileLength,
            unknown,
            headerLength,
            fileMetaOffset,
            fileOffsetsOffset,
            fileCountOffset,
            ctxbOffset,
            dataOffset,
            entries);
    }

    private static string ReadString(ReadOnlySpan<byte> data, uint offset, string label)
    {
        if (offset >= data.Length)
            throw new InvalidDataException($"El {label} apunta fuera del archivo.");
        var end = checked((int)offset);
        while (end < data.Length && data[end] != 0)
            end++;
        if (end == data.Length)
            throw new InvalidDataException($"El {label} no termina en NUL.");
        return Encoding.UTF8.GetString(data[(int)offset..end]);
    }

    private static uint ReadUInt32At(ReadOnlySpan<byte> data, uint offset)
    {
        if (offset > data.Length - sizeof(uint))
            throw new InvalidDataException("Una referencia GAR sale de los límites del archivo.");
        return BinaryPrimitives.ReadUInt32LittleEndian(data[(int)offset..]);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - sizeof(uint))
            throw new InvalidDataException("La cabecera GAR está incompleta.");
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }
}

public sealed record GarEntry(
    int Index,
    string Name,
    string NameWithExtension,
    int Offset,
    int Length,
    byte[] Data);
