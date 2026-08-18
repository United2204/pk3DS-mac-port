using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace pk3DS.Core.CTR;

public class DARC
{
    public byte[] Data;
    public DARCHeader Header;
    public FileTableEntry[] Entries;
    public NameTableEntry[] FileNameTable;

    public DARC(byte[] Data = null)
    {
        if (Data == null) return;
        using var br = new BinaryReader(new MemoryStream(Data));
        try
        {
            Header = new DARCHeader(br);
            br.BaseStream.Position = Header.FileTableOffset;
            var root = new FileTableEntry(br);
            Entries = new FileTableEntry[root.DataLength];
            Entries[0] = root;
            for (int i = 1; i < root.DataLength; i++) Entries[i] = new FileTableEntry(br);
            FileNameTable = new NameTableEntry[root.DataLength];
            uint offs = 0;
            for (int i = 0; i < root.DataLength; i++)
            {
                char c; string s = string.Empty;
                while ((c = (char)br.ReadUInt16()) > 0) s += c;

                FileNameTable[i] = new NameTableEntry(offs, s);
                offs += ((uint)s.Length * 2) + 2;
            }
            br.BaseStream.Position = Header.FileDataOffset;
            this.Data = br.ReadBytes((int)(Header.FileSize - Header.FileDataOffset));
        }
        catch (Exception)
        { br.Close(); }
    }

    public class DARCHeader
    {
        public DARCHeader(BinaryReader br = null)
        {
            if (br == null) return;
            Signature = new string(br.ReadChars(4));
            if (Signature != "darc") throw new Exception(Signature);
            Endianness = br.ReadUInt16();
            HeaderSize = br.ReadUInt16();
            Version = br.ReadUInt32();
            FileSize = br.ReadUInt32();
            FileTableOffset = br.ReadUInt32();
            FileTableLength = br.ReadUInt32();
            FileDataOffset = br.ReadUInt32();
        }

        public string Signature;
        public ushort Endianness;
        public ushort HeaderSize;
        public uint Version;
        public uint FileSize;
        public uint FileTableOffset;
        public uint FileTableLength;
        public uint FileDataOffset;
    }

    public class FileTableEntry
    {
        public FileTableEntry(BinaryReader br = null)
        {
            if (br == null) return;
            NameOffset = br.ReadUInt32();
            IsFolder = NameOffset >> 24 == 1;
            NameOffset &= 0xFFFFFF;
            DataOffset = br.ReadUInt32();
            DataLength = br.ReadUInt32();
        }

        public uint NameOffset;
        public bool IsFolder;
        public uint DataOffset; // FOLDER: Parent Entry Index
        public uint DataLength; // FOLDER: Next Folder Index
    }

    public class NameTableEntry(uint offset, string fileName)
    {
        public uint NameOffset = offset;
        public string FileName = fileName;
    }

    // DARC r/w
    public static byte[] SetDARC(DARC darc)
    {
        // Package DARC into a writable array.
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        // Write Header
        bw.Write(Encoding.ASCII.GetBytes(darc.Header.Signature));
        bw.Write(darc.Header.Endianness);
        bw.Write(darc.Header.HeaderSize);
        bw.Write(darc.Header.Version);
        bw.Write(darc.Header.FileSize);
        bw.Write(darc.Header.FileTableOffset);
        bw.Write(darc.Header.FileTableLength);
        bw.Write(darc.Header.FileDataOffset);
        // Write FileTableEntries
        foreach (FileTableEntry entry in darc.Entries)
        {
            bw.Write(entry.NameOffset | (entry.IsFolder ? (uint)1 << 24 : 0));
            bw.Write(entry.DataOffset);
            bw.Write(entry.DataLength);
        }
        foreach (NameTableEntry entry in darc.FileNameTable)
        {
            bw.Write(Encoding.Unicode.GetBytes(entry.FileName + "\0"));
        }
        while (bw.BaseStream.Position < darc.Header.FileDataOffset)
            bw.Write((byte)0);

        // Write Data
        bw.Write(darc.Data);

        return ms.ToArray();
    }

    public static DARC GetDARC(string folderName)
    {
        // Package Folder into a DARC.
        List<FileTableEntry> EntryList = [];
        List<NameTableEntry> NameList = [];
        byte[] Data = [];
        uint nameOffset = 6; // 00 00 + 00 2E 00 00
        #region Build FileTable/NameTables
        {
            // Null First File
            {
                EntryList.Add(new FileTableEntry { DataOffset = 0, DataLength = 0, IsFolder = true, NameOffset = 0 });
                NameList.Add(new NameTableEntry(0, ""));
            }
            // "." Second File
            {
                EntryList.Add(new FileTableEntry { DataOffset = 0, DataLength = 0, IsFolder = true, NameOffset = 2 });
                NameList.Add(new NameTableEntry(6, "."));
            }
            foreach (string folder in Directory.GetDirectories(folderName).OrderBy(Path.GetFileName, StringComparer.Ordinal))
                AddDarcFolder(folder, parentIndex: 1, EntryList, NameList, ref Data, ref nameOffset);
        }
        #endregion

        // Compute Necessary DARC information
        int darcFileCount = NameList.Count;
        int NameListOffset = darcFileCount * 0xC;
        int NameListLength = (int)(nameOffset + NameListOffset);
        int DataOffset = 0x1C + NameListLength;
        DataOffset = DataOffset % 4 == 0 ? DataOffset : DataOffset + (4 - (DataOffset % 4));
        Array.Resize(ref Data, Data.Length % 4 == 0 ? Data.Length : Data.Length + 4 - (Data.Length % 4));
        int FinalSize = DataOffset + Data.Length;

        // Create New DARC
        var darc = new DARC
        {
            Header = new DARCHeader
            {
                Signature = "darc",
                Endianness = 0xFEFF,
                HeaderSize = 0x1C,
                Version = 1,
                FileSize = (uint)FinalSize,
                FileTableOffset = 0x1C,
                FileTableLength = (uint)NameListLength,
                FileDataOffset = (uint)DataOffset,
            },
            Entries = [.. EntryList],
            FileNameTable = [.. NameList],
            Data = Data,
        };
        // Fix the First two folders to specify the number of files
        darc.Entries[0].DataLength = (uint)darcFileCount;
        darc.Entries[1].DataLength = (uint)darcFileCount;

        // Fix the Data Offset of the files to point to actual destination
        foreach (FileTableEntry f in darc.Entries.Where(x => !x.IsFolder))
            f.DataOffset += darc.Header.FileDataOffset;
        return darc;
    }

    private static void AddDarcFolder(string folderPath, int parentIndex,
        List<FileTableEntry> entries, List<NameTableEntry> names, ref byte[] data, ref uint nameOffset)
    {
        var folderIndex = entries.Count;
        var folderName = new DirectoryInfo(folderPath).Name;
        names.Add(new NameTableEntry(nameOffset, folderName));
        entries.Add(new FileTableEntry
        {
            DataOffset = (uint)parentIndex,
            DataLength = 0,
            IsFolder = true,
            NameOffset = nameOffset,
        });
        nameOffset += checked((uint)((folderName.Length + 1) * 2));

        foreach (var childFolder in Directory.GetDirectories(folderPath).OrderBy(Path.GetFileName, StringComparer.Ordinal))
            AddDarcFolder(childFolder, folderIndex, entries, names, ref data, ref nameOffset);

        foreach (var filePath in Directory.GetFiles(folderPath).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var fileInfo = new FileInfo(filePath);
            var fileName = fileInfo.Name;
            names.Add(new NameTableEntry(nameOffset, fileName));
            entries.Add(new FileTableEntry
            {
                DataOffset = (uint)data.Length,
                DataLength = checked((uint)fileInfo.Length),
                IsFolder = false,
                NameOffset = nameOffset,
            });
            data = [.. data, .. File.ReadAllBytes(filePath)];
            nameOffset += checked((uint)((fileName.Length + 1) * 2));
        }

        // Folder ranges are preorder intervals: DataLength points to the first
        // entry after the complete subtree, including nested folders.
        entries[folderIndex].DataLength = checked((uint)entries.Count);
    }

    public static bool Darc2files(string path, string folderName)
    {
        try { return Darc2files(File.ReadAllBytes(path), folderName); }
        catch (Exception) { return false; }
    }

    public static bool Darc2files(byte[] darc, string folderName)
    {
        // Save all contents of a DARC to a folder, including nested folders.
        try
        {
            // Clear existing contents
            string root = folderName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);

            // Create new DARC object from input data
            var DARC = new DARC(darc);
            ValidateDarcTree(DARC);
            Directory.CreateDirectory(root);
            UnpackDarcFolder(DARC, folderIndex: 1, firstChild: 2,
                endExclusive: checked((int)DARC.Entries[1].DataLength), root);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void ValidateDarcTree(DARC darc)
    {
        if (darc.Header is null || darc.Entries is null || darc.FileNameTable is null || darc.Data is null
            || darc.Header.Signature != "darc" || darc.Entries.Length < 2
            || darc.FileNameTable.Length != darc.Entries.Length
            || !darc.Entries[0].IsFolder || !darc.Entries[1].IsFolder)
            throw new InvalidDataException("La tabla DARC no tiene una raíz válida.");

        var rootEnd = checked((int)darc.Entries[1].DataLength);
        if (rootEnd < 2 || rootEnd > darc.Entries.Length)
            throw new InvalidDataException("El rango de la raíz DARC sale de la tabla.");

        ValidateDarcFolderRange(darc, folderIndex: 1, firstChild: 2, endExclusive: rootEnd);
    }

    private static void ValidateDarcFolderRange(DARC darc, int folderIndex, int firstChild, int endExclusive)
    {
        if (firstChild < 0 || firstChild > endExclusive || endExclusive > darc.Entries.Length)
            throw new InvalidDataException("Un rango de carpeta DARC sale de la tabla.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = firstChild; index < endExclusive;)
        {
            var entry = darc.Entries[index];
            var name = darc.FileNameTable[index].FileName;
            ValidateDarcName(name);
            if (!names.Add(name))
                throw new InvalidDataException("Una carpeta DARC contiene nombres duplicados.");

            if (entry.IsFolder)
            {
                if (entry.DataOffset != (uint)folderIndex)
                    throw new InvalidDataException("La relación padre/hijo de una carpeta DARC es inválida.");
                var childEnd = checked((int)entry.DataLength);
                if (childEnd <= index || childEnd > endExclusive)
                    throw new InvalidDataException("El rango de una carpeta DARC es inválido.");
                ValidateDarcFolderRange(darc, index, index + 1, childEnd);
                index = childEnd;
            }
            else
            {
                ValidateDarcDataRange(darc, entry);
                index++;
            }
        }
    }

    private static void UnpackDarcFolder(DARC darc, int folderIndex, int firstChild,
        int endExclusive, string parentPath)
    {
        for (var index = firstChild; index < endExclusive;)
        {
            var entry = darc.Entries[index];
            var name = darc.FileNameTable[index].FileName;
            var path = Path.Combine(parentPath, name);
            if (entry.IsFolder)
            {
                var childEnd = checked((int)entry.DataLength);
                Directory.CreateDirectory(path);
                UnpackDarcFolder(darc, index, index + 1, childEnd, path);
                index = childEnd;
            }
            else
            {
                var relative = checked((long)entry.DataOffset - darc.Header.FileDataOffset);
                var length = checked((int)entry.DataLength);
                var data = darc.Data.AsSpan(checked((int)relative), length).ToArray();
                Directory.CreateDirectory(parentPath);
                File.WriteAllBytes(path, data);
                index++;
            }
        }
    }

    private static void ValidateDarcDataRange(DARC darc, FileTableEntry entry)
    {
        var relative = (long)entry.DataOffset - darc.Header.FileDataOffset;
        if (relative < 0 || entry.DataLength > int.MaxValue || relative + entry.DataLength > darc.Data.LongLength)
            throw new InvalidDataException("Una entrada DARC apunta fuera del bloque de datos.");
    }

    private static void ValidateDarcName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains('/') || name.Contains('\\')
            || name.Contains(':') || name.Any(char.IsControl))
            throw new InvalidDataException("Una entrada DARC tiene un nombre de ruta inseguro.");
    }

    public static bool Files2darc(string folderName, bool delete = false, string originalDARC = null, string outFile = null)
    {
        // Save all contents of a folder to a darc.
        try
        {
            byte[] darcData;
            DARC orig;
            string root = folderName;
            if (originalDARC != null)
            {
                // Fetch offset of DARC within file.
                byte[] darc = File.ReadAllBytes(originalDARC);
                int darcPos = GetDARCposition(darc);
                if (darcPos < 0) return false;
                byte[] origData = darc[darcPos..];

                orig = new DARC(origData);
                if (orig.Header is null || orig.Data is null)
                    return false;
                var declaredDarcLength = orig.Header.FileSize is > 0 and <= int.MaxValue && orig.Header.FileSize <= origData.Length
                    ? (int)orig.Header.FileSize
                    : origData.Length;
                var suffix = origData[declaredDarcLength..];
                orig = InsertFiles(orig, folderName);
                byte[] newDARC = SetDARC(orig);
                darcData = darc[..darcPos].Concat(newDARC).Concat(suffix).ToArray();
            }
            else // no existing darc to get
            {
                orig = GetDARC(folderName);
                darcData = SetDARC(orig);
            }

            // Fetch final name if not specified
            outFile ??= originalDARC ?? new DirectoryInfo(folderName).Name.Replace("_d", "") + ".darc";

            if (darcData == null) return false;
            File.WriteAllBytes(outFile, darcData);

            if (Directory.Exists(root) && delete)
                Directory.Delete(root, true);
            return true;
        }
        catch (Exception) { return false; }
    }

    // DARC Utility
    public static int GetDARCposition(byte[] data)
    {
        int pos = 0;
        while (BitConverter.ToUInt32(data, pos) != 0x63726164)
        { pos += 4; if (pos >= data.Length) return -1; }
        return pos;
    }

    public static bool InsertFile(ref DARC orig, int index, string path)
    {
        try { return InsertFile(ref orig, index, File.ReadAllBytes(path)); }
        catch (Exception) { return false; }
    }

    public static bool InsertFile(ref DARC orig, int index, byte[] data)
    {
        if (index < 0) return false;

        try
        {
            uint oldLength = orig.Entries[index].DataLength;
            uint offset = orig.Entries[index].DataOffset - orig.Header.FileDataOffset;
            long diff = checked((long)data.Length - oldLength);

            // Insert into Data Block
            byte[] pre = orig.Data.Take((int)offset).ToArray();
            byte[] post = orig.Data.Skip((int)(offset + oldLength)).ToArray();

            // Reassemble data
            orig.Data = [.. pre, .. data, .. post];

            // Fix absolute data offsets of files that follow the replaced payload. Folder entries
            // store tree indexes in DataOffset and must never be adjusted as file byte offsets.
            var absoluteThreshold = checked((long)orig.Header.FileDataOffset + offset + oldLength);
            foreach (var x in orig.Entries.Where(x => !x.IsFolder && x.DataOffset >= absoluteThreshold))
                x.DataOffset = checked((uint)((long)x.DataOffset + diff));
            orig.Entries[index].DataLength = (uint)data.Length;
            orig.Header.FileSize = checked(orig.Header.FileDataOffset + (uint)orig.Data.Length);
            return true;
        }
        catch (Exception) { return false; }
    }

    public static DARC InsertFiles(DARC orig, string folderName)
    {
        string[] fileNames = new string[orig.Entries.Length];
        for (int i = 0; i < fileNames.Length; i++)
            fileNames[i] = orig.FileNameTable[i].FileName;

        string[] files = Directory.GetFiles(folderName, "*", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            var fi = new FileInfo(file);
            string FileName = fi.Name;

            // Get Index of file
            int index = Array.IndexOf(fileNames, FileName);
            if (orig.Entries[index].IsFolder)
                throw new Exception(file + " is not a valid file to reinsert!");

            InsertFile(ref orig, index, file);
        }
        // Fix Data layout
        Array.Resize(ref orig.Data, orig.Data.Length % 4 == 0 ? orig.Data.Length : orig.Data.Length + 4 - (orig.Data.Length % 4));
        orig.Header.FileSize = (uint)(orig.Data.Length + orig.Header.FileDataOffset);
        return orig;
    }
}
