using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace pk3DS.Core.CTR;

/// <summary>
/// Reader for the legacy numbered "Shuffle ARC" container used by the Windows tools.
/// The archive stores raw chunks and does not guarantee that every chunk is a ZIP stream.
/// </summary>
public sealed class ShuffleArcPortable
{
    private const uint HeaderMagic = 0x0000000B;
    private const int HeaderSize = 0x18;
    private const int EntrySize = 0x30;

    public int HeaderOffset { get; }
    public uint FileNameCheck { get; }
    public uint Unknown { get; }
    public uint Unknown2 { get; }
    public uint Padding { get; }
    public ShuffleArcEntry[] Entries { get; }

    private ShuffleArcPortable(
        int headerOffset, uint fileNameCheck, uint unknown, uint unknown2,
        uint padding, ShuffleArcEntry[] entries)
    {
        HeaderOffset = headerOffset;
        FileNameCheck = fileNameCheck;
        Unknown = unknown;
        Unknown2 = unknown2;
        Padding = padding;
        Entries = entries;
    }

    public static bool HasHeader(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && (ReadUInt32(data, 0) == HeaderMagic ||
            (data.Length >= 0x104 && ReadUInt32(data, 0x100) == HeaderMagic));

    public static ShuffleArcPortable Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var headerOffset = FindHeaderOffset(data);
        var span = data.AsSpan();
        var fileNameCheck = ReadUInt32(span, headerOffset + 4);
        var unknown = ReadUInt32(span, headerOffset + 8);
        var unknown2 = ReadUInt32(span, headerOffset + 12);
        var count = ReadUInt32(span, headerOffset + 16);
        var padding = ReadUInt32(span, headerOffset + 20);
        if (count > 1_000_000)
            throw new InvalidDataException("El Shuffle ARC declara demasiados fragmentos.");

        var countInt = checked((int)count);
        var tableLength = checked((long)HeaderSize + ((long)countInt * EntrySize));
        if (headerOffset + tableLength > data.Length)
            throw new InvalidDataException("La tabla del Shuffle ARC sale de los límites del archivo.");

        var ranges = new List<(int Start, int End)>(countInt);
        var entries = new ShuffleArcEntry[countInt];
        for (var index = 0; index < countInt; index++)
        {
            var entryOffset = checked(headerOffset + HeaderSize + ((int)index * EntrySize));
            var length = ReadUInt32(span, entryOffset + 8);
            var relativeOffset = ReadUInt32(span, entryOffset + 12);
            if (length > int.MaxValue || relativeOffset > int.MaxValue)
                throw new InvalidDataException($"El fragmento {index} del Shuffle ARC es demasiado grande.");

            var start = checked(headerOffset + (int)relativeOffset);
            var end = checked(start + (int)length);
            if (start < headerOffset + tableLength || end < start || end > data.Length)
                throw new InvalidDataException($"El fragmento {index} del Shuffle ARC sale de los límites.");
            ranges.Add((start, end));
            entries[index] = new ShuffleArcEntry(index, start, (int)length, data.AsSpan(start, (int)length).ToArray());
        }

        ranges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        for (var index = 1; index < ranges.Count; index++)
        {
            if (ranges[index].Start < ranges[index - 1].End)
                throw new InvalidDataException("Los fragmentos del Shuffle ARC se solapan.");
        }

        return new ShuffleArcPortable(headerOffset, fileNameCheck, unknown, unknown2, padding, entries);
    }

    private static int FindHeaderOffset(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4 && ReadUInt32(data, 0) == HeaderMagic)
            return 0;
        if (data.Length >= 0x104 && ReadUInt32(data, 0x100) == HeaderMagic)
            return 0x100;
        throw new InvalidDataException("La cabecera del Shuffle ARC no es válida.");
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - sizeof(uint))
            throw new InvalidDataException("La cabecera del Shuffle ARC está incompleta.");
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }
}

public sealed record ShuffleArcEntry(int Index, int Offset, int Length, byte[] Data);
