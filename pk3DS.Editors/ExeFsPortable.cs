using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace pk3DS.Editors;

/// <summary>
/// Platform-neutral reader and writer for the fixed-layout 3DS ExeFS container.
/// The returned entries are kept in their header slot order and never touch the file system.
/// </summary>
public static class ExeFsPortable
{
    public const int HeaderSize = 0x200;
    public const int SlotCount = 10;
    public const int SlotSize = 0x10;
    public const int Alignment = 0x200;
    public const int HashSize = 0x20;

    /// <summary>
    /// Reads an ExeFS image and returns its entries in header slot order.
    /// </summary>
    public static ExeFsEntry[] Unpack(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length < HeaderSize)
            throw new InvalidDataException("ExeFS es demasiado pequeño para contener su cabecera de 0x200 bytes.");
        if (source.Length % Alignment != 0)
            throw new InvalidDataException("El tamaño total de ExeFS debe estar alineado a 0x200 bytes.");

        var payloadLength = source.Length - HeaderSize;
        var entries = new List<ParsedEntry>(SlotCount);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offsets = new HashSet<uint>();
        var metadata = new HashSet<(uint Offset, uint Size)>();
        var sawEmptySlot = false;
        uint previousOffset = 0;
        var hasPreviousOffset = false;

        for (var index = 0; index < SlotCount; index++)
        {
            var slotOffset = checked(index * SlotSize);
            var slot = source.AsSpan(slotOffset, SlotSize);
            var nameBytes = slot[..8];
            var terminator = nameBytes.IndexOf((byte)0);

            if (terminator == 0)
            {
                ValidateEmptySlot(slot, index);
                sawEmptySlot = true;
                continue;
            }

            if (sawEmptySlot)
                throw new InvalidDataException($"La entrada ExeFS {index} aparece después de un slot vacío.");

            var nameLength = terminator < 0 ? nameBytes.Length : terminator;
            if (terminator >= 0)
            {
                for (var nameIndex = terminator + 1; nameIndex < nameBytes.Length; nameIndex++)
                {
                    if (nameBytes[nameIndex] != 0)
                        throw new InvalidDataException($"El nombre del slot ExeFS {index} no está terminado con padding nulo.");
                }
            }

            var name = ReadName(nameBytes[..nameLength], index);
            if (!names.Add(name))
                throw new InvalidDataException($"El nombre ExeFS '{name}' está duplicado.");

            var offset = BinaryPrimitives.ReadUInt32LittleEndian(slot[8..12]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(slot[12..16]);
            if (!metadata.Add((offset, size)))
                throw new InvalidDataException($"El slot ExeFS {index} repite metadata de offset y tamaño.");
            if (!offsets.Add(offset))
                throw new InvalidDataException($"El offset relativo ExeFS 0x{offset:X} está duplicado.");
            if (hasPreviousOffset && offset <= previousOffset)
                throw new InvalidDataException($"Los offsets ExeFS no están estrictamente ordenados en el slot {index}.");
            previousOffset = offset;
            hasPreviousOffset = true;

            if (offset % Alignment != 0)
                throw new InvalidDataException($"El offset del slot ExeFS {index} no está alineado a 0x200 bytes.");
            if (offset > (uint)payloadLength)
                throw new InvalidDataException($"El offset del slot ExeFS {index} sale del bloque de datos.");

            var remaining = (uint)(payloadLength - (int)offset);
            if (size > remaining)
                throw new InvalidDataException($"El rango del slot ExeFS {index} sale del bloque de datos.");

            var padding = (uint)(Alignment - (size % Alignment));
            var paddedLength = checked((ulong)size + padding);
            if (paddedLength > remaining)
            {
                throw new InvalidDataException(
                    $"El slot ExeFS {index} no contiene su bloque completo de datos y padding de 0x200 bytes.");
            }

            var start = checked((int)offset);
            var end = checked(start + checked((int)size));
            var paddedEnd = checked(start + checked((int)paddedLength));
            entries.Add(new ParsedEntry(index, name, offset, size, start, end, paddedEnd));
        }

        var ranges = new List<ParsedEntry>(entries);
        ranges.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
        for (var index = 1; index < ranges.Count; index++)
        {
            var previous = ranges[index - 1];
            var current = ranges[index];
            if (current.Start < previous.PaddedEnd)
            {
                throw new InvalidDataException(
                    $"Los bloques ExeFS de los slots {previous.Index} y {current.Index} se solapan.");
            }
        }

        var result = new ExeFsEntry[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            result[index] = new ExeFsEntry(
                entry.Name,
                source.AsSpan(HeaderSize + entry.Start, checked((int)entry.Size)).ToArray());
        }

        return result;
    }

    /// <summary>
    /// Packs entries in their supplied order using the retail ExeFS header, hashes and padding.
    /// </summary>
    public static byte[] Pack(IReadOnlyList<ExeFsEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > SlotCount)
            throw new InvalidDataException("ExeFS no puede contener más de 10 entradas.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long relativeOffset = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index]
                ?? throw new InvalidDataException($"La entrada ExeFS {index} es nula.");
            ValidateName(entry.Name, index);
            if (!names.Add(entry.Name))
                throw new InvalidDataException($"El nombre ExeFS '{entry.Name}' está duplicado.");
            if (entry.Data is null)
                throw new InvalidDataException($"Los datos de la entrada ExeFS {index} son nulos.");

            if (relativeOffset > uint.MaxValue)
                throw new InvalidDataException("Los offsets ExeFS superan el límite de uint32.");

            var padding = Alignment - (entry.Data.Length % Alignment);
            relativeOffset = checked(relativeOffset + entry.Data.Length + padding);
        }

        var totalLength = checked((long)HeaderSize + relativeOffset);
        if (totalLength > int.MaxValue)
            throw new InvalidDataException("El ExeFS resultante es demasiado grande para un arreglo de bytes.");

        var output = new byte[(int)totalLength];
        long payloadCursor = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var slotOffset = checked(index * SlotSize);
            var slot = output.AsSpan(slotOffset, SlotSize);
            WriteName(slot[..8], entry.Name, index);

            BinaryPrimitives.WriteUInt32LittleEndian(slot[8..12], checked((uint)payloadCursor));
            BinaryPrimitives.WriteUInt32LittleEndian(slot[12..16], checked((uint)entry.Data.Length));

            var hash = SHA256.HashData(entry.Data);
            var hashOffset = checked(HeaderSize - (HashSize * (index + 1)));
            hash.CopyTo(output.AsSpan(hashOffset, HashSize));

            var destination = checked((int)((long)HeaderSize + payloadCursor));
            Buffer.BlockCopy(entry.Data, 0, output, destination, entry.Data.Length);
            var paddingLength = Alignment - (entry.Data.Length % Alignment);
            payloadCursor = checked(payloadCursor + entry.Data.Length + paddingLength);
        }

        return output;
    }

    private static void ValidateEmptySlot(ReadOnlySpan<byte> slot, int index)
    {
        for (var nameIndex = 1; nameIndex < 8; nameIndex++)
        {
            if (slot[nameIndex] != 0)
                throw new InvalidDataException($"El slot ExeFS vacío {index} contiene bytes de nombre.");
        }

        var offset = BinaryPrimitives.ReadUInt32LittleEndian(slot[8..12]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(slot[12..16]);
        if (offset != 0 || size != 0)
            throw new InvalidDataException($"El slot ExeFS vacío {index} contiene metadata de datos.");
    }

    private static string ReadName(ReadOnlySpan<byte> bytes, int index)
    {
        if (bytes.Length == 0)
            throw new InvalidDataException($"El slot ExeFS {index} tiene un nombre vacío.");

        var chars = new char[bytes.Length];
        for (var nameIndex = 0; nameIndex < bytes.Length; nameIndex++)
        {
            var value = bytes[nameIndex];
            if (value < 0x20 || value > 0x7E || value == (byte)'/' || value == (byte)'\\')
            {
                throw new InvalidDataException(
                    $"El nombre del slot ExeFS {index} contiene un carácter no ASCII o no seguro.");
            }
            chars[nameIndex] = (char)value;
        }

        var name = new string(chars);
        if (name is "." or "..")
            throw new InvalidDataException($"El nombre del slot ExeFS {index} no es un nombre de archivo válido.");
        return name;
    }

    private static void ValidateName(string name, int index)
    {
        if (string.IsNullOrEmpty(name))
            throw new InvalidDataException($"La entrada ExeFS {index} tiene un nombre vacío.");
        if (name.Length > 8)
            throw new InvalidDataException($"El nombre de la entrada ExeFS {index} supera los 8 bytes ASCII.");
        if (name is "." or "..")
            throw new InvalidDataException($"El nombre de la entrada ExeFS {index} no es un nombre de archivo válido.");

        foreach (var value in name)
        {
            if (value < 0x20 || value > 0x7E || value is '/' or '\\')
            {
                throw new InvalidDataException(
                    $"El nombre de la entrada ExeFS {index} debe usar solo caracteres ASCII seguros.");
            }
        }
    }

    private static void WriteName(Span<byte> destination, string name, int index)
    {
        ValidateName(name, index);
        for (var nameIndex = 0; nameIndex < name.Length; nameIndex++)
            destination[nameIndex] = checked((byte)name[nameIndex]);
    }

    private readonly record struct ParsedEntry(
        int Index,
        string Name,
        uint Offset,
        uint Size,
        int Start,
        int End,
        int PaddedEnd);
}

/// <summary>
/// One flat ExeFS file stem and its payload bytes.
/// </summary>
public sealed record ExeFsEntry(string Name, byte[] Data);
