using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace pk3DS.Core.CTR;

/// <summary>
/// Simple (?) ARChive
/// </summary>
public sealed class SARC : IDisposable
{
    private const string Identifier = nameof(SARC);

    public string Magic;
    public ushort HeaderSize;
    public ushort Endianness;
    public uint FileSize;
    public uint DataOffset;
    public uint Unknown;

    public SFAT SFAT;
    public SFNT SFNT;

    // Assigned Properties
    public string FileName;
    public string FilePath;
    public string Extension;
    public readonly bool Valid;

    /// <summary>
    /// The required <see cref="Magic"/> matches the first 4 bytes of the file data.
    /// </summary>
    public bool SigMatches => Magic == Identifier;
    private readonly Stream stream;
    private readonly BinaryReader br;

    /// <summary>
    /// Initializes an empty <see cref="SARC"/>.
    /// </summary>
    public SARC()
    {
        SFAT = new SFAT();
        SFNT = new SFNT();
    }

    /// <summary>
    /// Initializes a <see cref="SARC"/> from a file location.
    /// </summary>
    /// <param name="path"></param>
    public SARC(string path)
    {
        SetFileInfo(path);

        stream = File.OpenRead(path);
        br = new BinaryReader(stream);
        ReadSARC();
        Valid = true;
    }

    /// <summary>
    /// Initializes a <see cref="SARC"/> from a provided stream.
    /// </summary>
    /// <param name="fs"></param>
    public SARC(Stream fs)
    {
        stream = fs;
        br = new BinaryReader(stream);
        ReadSARC();
        Valid = true;
    }

    /// <summary>
    /// Initializes a <see cref="SARC"/> from a provided array.
    /// </summary>
    /// <param name="data"></param>
    public SARC(byte[] data)
    {
        stream = new MemoryStream(data);
        br = new BinaryReader(stream);
        ReadSARC();
        Valid = true;
    }

    /// <summary>
    /// Reads the contents of the <see cref="SARC"/> header and file info tables.
    /// </summary>
    private void ReadSARC()
    {
        Magic = new string(br.ReadChars(4));
        if (!SigMatches)
            return;

        HeaderSize = br.ReadUInt16();
        Endianness = br.ReadUInt16();
        FileSize = br.ReadUInt32();
        DataOffset = br.ReadUInt32();
        Unknown = br.ReadUInt32();

        SFAT = new SFAT(br);
        SFNT = new SFNT(br);
    }

    /// <summary>
    /// Sets File information for the original file.
    /// </summary>
    /// <param name="path"></param>
    public void SetFileInfo(string path)
    {
        FileName = Path.GetFileNameWithoutExtension(path);
        FilePath = Path.GetDirectoryName(path);
        Extension = Path.GetExtension(path);
    }

    /// <summary>
    /// Gets the entry filename for a given <see cref="SFATEntry"/>.
    /// </summary>
    /// <param name="entry">Entry to fetch data for</param>
    /// <returns>File Name</returns>
    public string GetFileName(SFATEntry entry) => GetFileName(entry.FileNameOffset);

    /// <summary>
    /// Gets the entry data for a given <see cref="SFATEntry"/>,
    /// </summary>
    /// <param name="entry">Entry to fetch data for</param>
    /// <returns>Data array</returns>
    public byte[] GetData(SFATEntry entry) => GetData(entry.FileDataStart, entry.FileDataLength);

    /// <summary>
    /// Overwrites the entry data, assuming the size is the exact same.
    /// </summary>
    /// <param name="entry">File entry to overwrite</param>
    /// <param name="data">Data to write</param>
    public void SetData(SFATEntry entry, byte[] data)
    {
        if (data.Length != entry.FileDataLength)
            throw new ArgumentException(nameof(data.Length));
        SetData(entry.FileDataStart, data);
    }

    /// <summary>
    /// Exports the entry data for a given <see cref="SFATEntry"/> at a provided path with its assigned <see cref="SFATEntry"/> file name via the <see cref="SFNT"/> name table.
    /// </summary>
    /// <param name="t">Entry to export</param>
    /// <param name="outpath">Path to export to. If left null, will output to the <see cref="SARC"/> FilePath, if it is assigned.</param>
    public string ExportFile(SFATEntry t, string outpath = null)
    {
        outpath ??= FilePath;
        byte[] data = GetData(t);
        string name = GetFileName(t);

        string dir = Path.GetDirectoryName(name) ?? string.Empty;
        string location = Path.Combine(outpath, dir);
        Directory.CreateDirectory(location);

        var filepath = Path.Combine(outpath, name);
        File.WriteAllBytes(filepath, data);
        return filepath;
    }

    /// <summary>
    /// Dumps the contents of the <see cref="SARC"/> to a provided folder. If no location is provided, it will dump to the SARC's location.
    /// </summary>
    /// <param name="path">Path to create dump folder in</param>
    /// <param name="folder">Folder to dump contents to</param>
    public IEnumerable<string> Dump(string path = null, string folder = null)
    {
        path ??= FilePath;
        ArgumentNullException.ThrowIfNull(path);
        if (File.Exists(path))
            path = Path.GetDirectoryName(path);
        ArgumentNullException.ThrowIfNull(path);

        folder ??= FileName ?? "sarc";
        string dir = Path.Combine(path, folder);

        Directory.CreateDirectory(dir);

        foreach (SFATEntry t in SFAT.Entries)
            yield return ExportFile(t, dir);
    }

    private string GetFileName(int offset)
    {
        stream.Seek(SFNT.StringOffset, SeekOrigin.Begin);
        stream.Seek((offset & 0x00FFFFFF) * 4, SeekOrigin.Current);
        var bytes = new List<byte>();
        int value;
        while ((value = stream.ReadByte()) > 0)
            bytes.Add((byte)value);
        if (value < 0)
            throw new InvalidDataException("SARC contiene una cadena sin terminador.");

        return Encoding.UTF8.GetString(bytes.ToArray()).Replace('/', Path.DirectorySeparatorChar);
    }

    public void SetFileName(int offset, string value)
    {
        var str = value.Replace(Path.DirectorySeparatorChar, '/');
        stream.Seek(SFNT.StringOffset, SeekOrigin.Begin);
        stream.Seek((offset & 0x00FFFFFF) * 4, SeekOrigin.Current);
        var bytes = Encoding.UTF8.GetBytes(str);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte((byte)'\0');
    }

    private byte[] GetData(int offset, int length)
    {
        if (offset < 0 || length < 0)
            throw new InvalidDataException("SARC contiene offsets negativos.");
        byte[] fileBuffer = new byte[length];
        stream.Seek(offset + DataOffset, SeekOrigin.Begin);
        stream.ReadExactly(fileBuffer, 0, length);
        return fileBuffer;
    }

    /// <summary>
    /// Packs a directory tree into a SARC archive. Names are stored as UTF-8 paths with forward
    /// slashes and every string/data start is aligned to the boundary expected by the SFNT/SFAT
    /// tables. The generated archive is immediately readable by <see cref="SARC"/>.
    /// </summary>
    public static int Pack(string folderPath, string sarcPath, int dataAlignment = 0x10)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);
        if (dataAlignment < 4 || dataAlignment > 0x1000 || (dataAlignment & (dataAlignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(dataAlignment), "SARC alignment must be a power of two between 4 and 4096.");

        var sourceFiles = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
            .Select(path => new SarcSourceFile(
                Path.GetRelativePath(folderPath, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllBytes(path)))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        if (sourceFiles.Length == 0)
            throw new InvalidDataException("SARC input folder is empty.");
        if (sourceFiles.Length > ushort.MaxValue)
            throw new InvalidDataException("SARC admite como máximo 65535 archivos.");

        const uint hashMultiplier = 0x65;
        var stringBytes = new List<byte>();
        var nameOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in sourceFiles)
        {
            nameOffsets[file.Name] = stringBytes.Count / 4;
            stringBytes.AddRange(Encoding.UTF8.GetBytes(file.Name));
            stringBytes.Add(0);
            while (stringBytes.Count % 4 != 0)
                stringBytes.Add(0);
        }

        var metadataLength = checked(0x14 + 0x0C + (sourceFiles.Length * 0x10) + 0x08 + stringBytes.Count);
        var dataOffset = Align(metadataLength, dataAlignment);
        var entries = sourceFiles
            .Select(file => new SarcPackEntry(
                file.Name,
                HashName(file.Name, hashMultiplier),
                nameOffsets[file.Name],
                0,
                0,
                file.Data))
            .OrderBy(entry => entry.Hash)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        var dataPosition = 0;
        foreach (var entry in entries)
        {
            dataPosition = Align(dataPosition, 4);
            entry.Start = dataPosition;
            dataPosition = checked(dataPosition + entry.Data.Length);
            entry.End = dataPosition;
        }

        var fileSize = checked(dataOffset + dataPosition);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sarcPath))!);
        using var stream = File.Open(sarcPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        bw.Write(Encoding.ASCII.GetBytes("SARC"));
        bw.Write((ushort)0x14);
        bw.Write((ushort)0xFEFF);
        bw.Write((uint)fileSize);
        bw.Write((uint)dataOffset);
        bw.Write(1u);

        bw.Write(Encoding.ASCII.GetBytes("SFAT"));
        bw.Write((ushort)0x0C);
        bw.Write((ushort)entries.Length);
        bw.Write(hashMultiplier);
        foreach (var entry in entries)
        {
            bw.Write(entry.Hash);
            bw.Write(entry.NameOffset);
            bw.Write(entry.Start);
            bw.Write(entry.End);
        }

        bw.Write(Encoding.ASCII.GetBytes("SFNT"));
        bw.Write((ushort)0x08);
        bw.Write((ushort)0);
        bw.Write(stringBytes.ToArray());
        while (bw.BaseStream.Position < dataOffset)
            bw.Write((byte)0);

        dataPosition = 0;
        foreach (var entry in entries)
        {
            var aligned = Align(dataPosition, 4);
            while (dataPosition < aligned)
            {
                bw.Write((byte)0);
                dataPosition++;
            }
            bw.Write(entry.Data);
            dataPosition += entry.Data.Length;
        }

        return entries.Length;
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static uint HashName(string name, uint multiplier)
    {
        uint hash = 0;
        foreach (var value in Encoding.UTF8.GetBytes(name))
            hash = unchecked((hash * multiplier) + value);
        return hash;
    }

    private sealed record SarcSourceFile(string Name, byte[] Data);

    private sealed class SarcPackEntry(string name, uint hash, int nameOffset, int start, int end, byte[] data)
    {
        public string Name { get; } = name;
        public uint Hash { get; } = hash;
        public int NameOffset { get; } = nameOffset;
        public int Start { get; set; } = start;
        public int End { get; set; } = end;
        public byte[] Data { get; } = data;
    }

    private void SetData(int offset, byte[] data)
    {
        stream.Seek(offset + DataOffset, SeekOrigin.Begin);
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Disposes of the <see cref="stream"/> and <see cref="br"/> objects and frees the <see cref="FileName"/> if originally loaded from that location.
    /// </summary>
    public void Dispose()
    {
        stream?.Dispose();
        br?.Dispose();
    }
}

/// <summary>
/// <see cref="SARC"/> File Access Table
/// </summary>
public class SFAT
{
    public const string Identifier = nameof(SFAT);

    /// <summary>
    /// The required <see cref="Magic"/> matches the first 4 bytes of the file data.
    /// </summary>
    public bool SigMatches => Magic == Identifier;

    public string Magic;
    public ushort HeaderSize;
    public ushort EntryCount;
    public uint HashMult;
    public List<SFATEntry> Entries;

    public SFAT() { }

    public SFAT(BinaryReader br)
    {
        Magic = new string(br.ReadChars(4));
        if (!SigMatches)
            throw new FormatException(nameof(SFAT));

        HeaderSize = br.ReadUInt16();
        EntryCount = br.ReadUInt16();
        HashMult = br.ReadUInt32();
        Entries = [];

        for (int i = 0; i < EntryCount; i++)
            Entries.Add(new SFATEntry(br));
    }
}

/// <summary>
/// <see cref="SARC"/> File Name Table
/// </summary>
public class SFNT
{
    public const string Identifier = nameof(SFNT);

    /// <summary>
    /// The required <see cref="Magic"/> matches the first 4 bytes of the file data.
    /// </summary>
    public bool SigMatches => Magic == Identifier;

    public string Magic;
    public ushort HeaderSize;
    public ushort Unknown;
    public uint StringOffset;

    public SFNT() { }

    public SFNT(BinaryReader br)
    {
        Magic = new string(br.ReadChars(4));
        if (!SigMatches)
            throw new FormatException(nameof(SFNT));

        HeaderSize = br.ReadUInt16();
        Unknown = br.ReadUInt16();
        StringOffset = (uint)br.BaseStream.Position;
    }
}

/// <summary>
/// <see cref="SARC"/> File Access Table (<see cref="SFAT"/>) Entry
/// </summary>
public class SFATEntry(BinaryReader br)
{
    public uint FileNameHash = br.ReadUInt32();
    public int FileNameOffset = br.ReadInt32();
    public int FileDataStart = br.ReadInt32();
    public int FileDataEnd = br.ReadInt32();

    public int FileDataLength => FileDataEnd - FileDataStart;
}
