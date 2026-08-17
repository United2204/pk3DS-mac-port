using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace pk3DS.Core.CTR;

/// <summary>
/// Reader for the FARC archive variant used by several 3DS titles. The legacy Windows tool
/// only extracted this format, so this class intentionally does not fabricate a writer for the
/// container's surrounding metadata.
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
        stream.Seek(headerOffset + 0x24, SeekOrigin.Begin);
        var sirRelative = ReadUInt32();
        _ = ReadUInt32(); // Header-specific unknown value.
        var dataRelative = ReadUInt32();
        SirOffset = checked((uint)(headerOffset + sirRelative));
        DataOffset = checked((uint)(headerOffset + dataRelative));

        EnsureRange(SirOffset, 0x08, "FARC SIR header");
        stream.Seek(SirOffset, SeekOrigin.Begin);
        SirMagic = ReadUInt32();
        if (SirMagic != SirMagicValue)
            throw new InvalidDataException("La tabla SIR del FARC no es válida.");

        var metaRelative = ReadUInt32();
        MetaPointer = checked((uint)(SirOffset + metaRelative));
        EnsureRange(MetaPointer, 0x08, "FARC metadata");
        stream.Seek(MetaPointer, SeekOrigin.Begin);
        var tableRelative = ReadUInt32();
        FileCount = ReadUInt32();
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
            Files[i].Name = ReadUtf16String(checked((long)SirOffset + Files[i].NameOffset));
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

    private void EnsureRange(long offset, long length, string label)
    {
        if (offset < 0 || length < 0 || offset > stream.Length || length > stream.Length - offset)
            throw new InvalidDataException($"{label} sale de los límites del FARC.");
    }

    public void Dispose() => stream.Dispose();
}

public sealed class FARCFile
{
    public uint NameOffset;
    public uint Offset;
    public uint Length;
    public string Name { get; internal set; } = string.Empty;
}
