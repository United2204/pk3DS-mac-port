using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace pk3DS.Core.CTR;

/// <summary>
/// Reader and writer for the SIR0-backed FARC variants used by several 3DS titles.
/// Named archives store UTF-16 paths; hash-indexed archives expose deterministic synthetic
/// names when unpacked because the original paths are not present in the archive.
/// </summary>
public sealed class FARC : IDisposable
{
    private const uint MagicValue = 0x43524146; // FARC
    private const uint SirMagicValue = 0x30524953; // SIR0
    private const long MaximumHeaderScan = 0x100000;

    private readonly Stream stream;

    public uint Magic;
    public uint SirMagic;
    public uint SirOffset;
    public uint HeaderOffset;
    public uint MetaPointer;
    public uint NamesOffset;
    public uint TableOffset;
    public uint DataOffset;
    public uint FileCount;
    public FARCIndexKind IndexKind { get; private set; } = FARCIndexKind.NamedUtf16;
    public List<FARCFile> Files { get; } = [];

    public string FileName { get; private set; }
    public string FilePath { get; private set; }
    public string Extension { get; private set; }
    public bool Valid { get; }

    /// <summary>The archive header contains the expected FARC magic.</summary>
    public bool SigMatches => Magic == MagicValue;

    public FARC(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("FARC path is empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("FARC file was not found.", path);

        FileName = Path.GetFileNameWithoutExtension(path);
        FilePath = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Extension = Path.GetExtension(path);
        stream = File.OpenRead(path);
        ReadFarc();
        Valid = true;
    }

    public FARC(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        stream = new MemoryStream(data, writable: false);
        ReadFarc();
        Valid = true;
    }

    public FARC(Stream source)
    {
        stream = source ?? throw new ArgumentNullException(nameof(source));
        if (!stream.CanSeek || !stream.CanRead)
            throw new ArgumentException("FARC streams must be readable and seekable.", nameof(source));
        ReadFarc();
        Valid = true;
    }

    public string GetFileName(FARCFile file) => file.Name;

    public byte[] GetData(FARCFile file)
    {
        var absoluteOffset = checked((long)DataOffset + file.Offset);
        EnsureRange(absoluteOffset, file.Length, "FARC file data");
        if (file.Length > int.MaxValue)
            throw new InvalidDataException("FARC entry is too large for the current process.");

        stream.Seek(absoluteOffset, SeekOrigin.Begin);
        var data = new byte[(int)file.Length];
        stream.ReadExactly(data, 0, data.Length);
        return data;
    }

    /// <summary>
    /// Packs a directory tree into a SIR0-backed FARC variant understood by this class.
    /// Names are stored as UTF-16LE paths with forward slashes and data entries are aligned to
    /// <paramref name="dataAlignment"/>. The generated archive is immediately readable by
    /// <see cref="FARC(string)"/>.
    /// </summary>
    public static int Pack(string folderPath, string farcPath, int dataAlignment = 0x80,
        FARCIndexKind indexKind = FARCIndexKind.NamedUtf16)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);
        if (string.IsNullOrWhiteSpace(farcPath))
            throw new ArgumentException("FARC output path is empty.", nameof(farcPath));
        if (dataAlignment < 4 || dataAlignment > 0x1000 || (dataAlignment & (dataAlignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(dataAlignment), "FARC alignment must be a power of two between 4 and 4096.");
        if (indexKind is not FARCIndexKind.NamedUtf16 and not FARCIndexKind.Crc32Hash)
            throw new ArgumentOutOfRangeException(nameof(indexKind), "FARC index kind is not supported.");

        var sourceFiles = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
            .Select(path => new FarcSourceFile(
                Path.GetRelativePath(folderPath, path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'),
                File.ReadAllBytes(path)))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        if (sourceFiles.Length == 0)
            throw new InvalidDataException("FARC input folder is empty.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new HashSet<uint>();
        foreach (var file in sourceFiles)
        {
            ValidatePackedName(file.Name);
            if (indexKind == FARCIndexKind.NamedUtf16 && !names.Add(file.Name))
                throw new InvalidDataException($"FARC contains duplicate path: {file.Name}");
        }

        const long headerSize = 0x30;
        const long tableOffset = 0x20;
        const int tableEntrySize = 0x10;
        var tableSize = checked((long)sourceFiles.Length * tableEntrySize);
        var namesOffset = Align(tableOffset + tableSize, 0x10);
        var nameBytes = new List<byte>();
        var entries = new List<FarcPackEntry>(sourceFiles.Length);

        foreach (var file in sourceFiles)
        {
            if (indexKind == FARCIndexKind.NamedUtf16)
            {
                var nameOffset = checked(namesOffset + nameBytes.Count);
                var encodedName = Encoding.Unicode.GetBytes(file.Name);
                nameBytes.AddRange(encodedName);
                nameBytes.Add(0);
                nameBytes.Add(0);
                entries.Add(new FarcPackEntry(file.Name, ToUInt32(nameOffset, "FARC name offset"), file.Data));
            }
            else
            {
                var hash = GetPackedHash(file.Name);
                if (!hashes.Add(hash))
                    throw new InvalidDataException($"FARC contains duplicate name hash: 0x{hash:X8}");
                entries.Add(new FarcPackEntry(file.Name, hash, file.Data));
            }
        }

        var sirOffset = headerSize;
        var dataAbsoluteOffset = Align(sirOffset + namesOffset + nameBytes.Count, dataAlignment);
        var dataPosition = 0L;
        foreach (var entry in entries)
        {
            dataPosition = Align(dataPosition, dataAlignment);
            entry.Offset = ToUInt32(dataPosition, "FARC data offset");
            dataPosition = checked(dataPosition + entry.Data.LongLength);
        }

        var totalLength = checked(dataAbsoluteOffset + dataPosition);
        _ = ToUInt32(dataAbsoluteOffset, "FARC data base offset");
        _ = ToUInt32(totalLength, "FARC file length");

        var output = Path.GetFullPath(farcPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var stream = File.Open(output, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

        writer.Write(Encoding.ASCII.GetBytes("FARC"));
        WriteZeros(writer, 0x20);
        writer.Write(ToUInt32(sirOffset, "FARC SIR offset"));
        writer.Write(0u); // Variant-specific header field preserved as zero.
        writer.Write(ToUInt32(dataAbsoluteOffset, "FARC data offset"));

        writer.Write(Encoding.ASCII.GetBytes("SIR0"));
        writer.Write(0x10u); // Metadata block relative to the SIR0 header.
        writer.Write(0u);
        writer.Write(0u);

        writer.Write(ToUInt32(tableOffset, "FARC table offset"));
        writer.Write((uint)entries.Count);
        writer.Write((uint)indexKind);
        writer.Write(0u);

        foreach (var entry in entries)
        {
            writer.Write(entry.NameOffset);
            writer.Write(entry.Offset);
            writer.Write((uint)entry.Data.Length);
            writer.Write(0u);
        }

        var namesAbsoluteOffset = checked(sirOffset + namesOffset);
        WriteZeros(writer, namesAbsoluteOffset - writer.BaseStream.Position);
        writer.Write(nameBytes.ToArray());
        WriteZeros(writer, dataAbsoluteOffset - writer.BaseStream.Position);

        dataPosition = 0;
        foreach (var entry in entries)
        {
            var aligned = Align(dataPosition, dataAlignment);
            WriteZeros(writer, aligned - dataPosition);
            writer.Write(entry.Data);
            dataPosition = checked(aligned + entry.Data.LongLength);
        }

        return entries.Count;
    }

    private void ReadFarc()
    {
        if (stream.Length < 4)
            throw new InvalidDataException("El archivo FARC es demasiado pequeño.");

        var scanLength = Math.Min(stream.Length - 4, MaximumHeaderScan);
        long headerOffset = -1;
        for (long offset = 0; offset <= scanLength; offset += 4)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            if (ReadUInt32() == MagicValue)
            {
                headerOffset = offset;
                break;
            }
        }

        if (headerOffset < 0)
            throw new InvalidDataException("No encontré la cabecera FARC.");
        HeaderOffset = checked((uint)headerOffset);
        Magic = MagicValue;

        EnsureRange(headerOffset + 0x24, 0x0C, "FARC header");
        var sirRelative = ReadUInt32At(headerOffset + 0x24);
        _ = ReadUInt32At(headerOffset + 0x28); // SIR0 length.
        var dataRelative = ReadUInt32At(headerOffset + 0x2C);
        SirOffset = checked((uint)(headerOffset + sirRelative));
        DataOffset = checked((uint)(headerOffset + dataRelative));

        EnsureRange(SirOffset, 0x08, "FARC SIR header");
        stream.Seek(SirOffset, SeekOrigin.Begin);
        SirMagic = ReadUInt32();
        if (SirMagic != SirMagicValue)
            throw new InvalidDataException("La variante FARC no usa un índice SIR0 compatible; las variantes hash basadas en SIR0 usan el tipo de índice CRC32.");

        var metaRelative = ReadUInt32();
        MetaPointer = checked((uint)(SirOffset + metaRelative));
        EnsureRange(MetaPointer, 0x08, "FARC metadata");
        stream.Seek(MetaPointer, SeekOrigin.Begin);
        var tableRelative = ReadUInt32();
        FileCount = ReadUInt32();
        var indexKind = ReadUInt32();
        if (indexKind is not 0 and not 1)
            throw new InvalidDataException($"La tabla SIR del FARC usa un tipo de índice no soportado: {indexKind}.");
        IndexKind = indexKind == 1 ? FARCIndexKind.Crc32Hash : FARCIndexKind.NamedUtf16;
        TableOffset = checked((uint)(SirOffset + tableRelative));
        if (FileCount > (stream.Length - TableOffset) / 0x10)
            throw new InvalidDataException("La cantidad de entradas FARC sale del archivo.");

        EnsureRange(TableOffset, checked((long)FileCount * 0x10), "FARC file table");
        stream.Seek(TableOffset, SeekOrigin.Begin);
        for (var i = 0u; i < FileCount; i++)
        {
            var file = new FARCFile
            {
                NameOffset = ReadUInt32(),
                Offset = ReadUInt32(),
                Length = ReadUInt32(),
            };
            _ = ReadUInt32(); // Table entry padding.
            Files.Add(file);
        }

        for (var i = 0; i < Files.Count; i++)
        {
            if (IndexKind == FARCIndexKind.NamedUtf16)
                Files[i].Name = ReadUtf16String(checked((long)SirOffset + Files[i].NameOffset));
            else
            {
                Files[i].NameHash = Files[i].NameOffset;
                Files[i].Name = $"hash-{Files[i].NameHash:X8}.bin";
            }
            _ = checked((long)DataOffset + Files[i].Offset + Files[i].Length);
            EnsureRange(checked((long)DataOffset + Files[i].Offset), Files[i].Length, "FARC file data");
        }
    }

    private string ReadUtf16String(long offset)
    {
        EnsureRange(offset, 2, "FARC file name");
        stream.Seek(offset, SeekOrigin.Begin);
        var bytes = new List<byte>();
        while (true)
        {
            var low = stream.ReadByte();
            var high = stream.ReadByte();
            if (low < 0 || high < 0)
                throw new InvalidDataException("FARC contiene un nombre sin terminador.");
            if (low == 0 && high == 0)
                break;
            bytes.Add((byte)low);
            bytes.Add((byte)high);
        }

        return Encoding.Unicode.GetString(bytes.ToArray()).Replace('/', Path.DirectorySeparatorChar);
    }

    private uint ReadUInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BitConverter.ToUInt32(buffer);
    }

    private uint ReadUInt32At(long offset)
    {
        EnsureRange(offset, sizeof(uint), "FARC offset");
        stream.Seek(offset, SeekOrigin.Begin);
        return ReadUInt32();
    }

    private void EnsureRange(long offset, long length, string label)
    {
        if (offset < 0 || length < 0 || offset > stream.Length || length > stream.Length - offset)
            throw new InvalidDataException($"{label} sale de los límites del FARC.");
    }

    private static void ValidatePackedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Contains('\0'))
            throw new InvalidDataException("FARC contains an invalid file name.");
        if (name.Contains('\\'))
            throw new InvalidDataException("FARC paths must use forward slashes.");

        var parts = name.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException($"FARC contains an unsafe path: {name}");
    }

    private static uint GetPackedHash(string name)
    {
        var leaf = name[(name.LastIndexOf('/') + 1)..];
        const string prefix = "hash-";
        const string suffix = ".bin";
        if (leaf.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && leaf.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            var token = leaf[prefix.Length..^suffix.Length];
            if (token.Length == 8 && uint.TryParse(token, NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var preservedHash))
                return preservedHash;
        }

        return ComputeCrc32(Encoding.Unicode.GetBytes(name));
    }

    private static uint ComputeCrc32(byte[] data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = unchecked((uint)-(int)(crc & 1));
                crc = (crc >> 1) ^ (0xEDB88320u & mask);
            }
        }
        return ~crc;
    }

    private static long Align(long value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static uint ToUInt32(long value, string label)
    {
        if (value < 0 || value > uint.MaxValue)
            throw new InvalidDataException($"{label} exceeds the 32-bit FARC limit.");
        return (uint)value;
    }

    private static void WriteZeros(BinaryWriter writer, long count)
    {
        if (count < 0)
            throw new InvalidDataException("FARC layout offsets are inconsistent.");

        var zeros = new byte[0x10000];
        while (count > 0)
        {
            var chunk = (int)Math.Min(count, zeros.Length);
            writer.Write(zeros, 0, chunk);
            count -= chunk;
        }
    }

    private sealed record FarcSourceFile(string Name, byte[] Data);

    private sealed class FarcPackEntry(string name, uint nameOffset, byte[] data)
    {
        public string Name { get; } = name;
        public uint NameOffset { get; } = nameOffset;
        public uint Offset { get; set; }
        public byte[] Data { get; } = data;
    }

    public void Dispose() => stream.Dispose();
}

public sealed class FARCFile
{
    public uint NameOffset;
    public uint NameHash;
    public uint Offset;
    public uint Length;
    public string Name { get; internal set; } = string.Empty;
}

public enum FARCIndexKind
{
    NamedUtf16,
    Crc32Hash,
}
