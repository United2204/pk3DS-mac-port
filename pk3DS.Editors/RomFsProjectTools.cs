using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace pk3DS.Editors;

public sealed record RomFsUnpackRequest(string InputFile, string? OutputDirectory = null);
public sealed record RomFsUnpackResponse(
    string InputFile,
    string OutputDirectory,
    int DirectoryCount,
    int FileCount,
    long FileBytes,
    string Note);

public sealed record RomFsPackRequest(string InputDirectory, string? OutputFile = null);
public sealed record RomFsPackResponse(
    string InputDirectory,
    string OutputFile,
    int DirectoryCount,
    int FileCount,
    long OutputBytes,
    string Note);

/// <summary>Standalone, staged RomFS import and export operations.</summary>
public static class RomFsProjectTools
{
    public static RomFsUnpackResponse Unpack(RomFsUnpackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputFile))
            throw new WorkspaceException("Elegí un archivo romfs.bin para desempaquetar.");

        var input = Path.GetFullPath(request.InputFile.Trim());
        if (!File.Exists(input))
            throw new WorkspaceException("El archivo RomFS indicado no existe.");

        var output = ResolveUnpackOutput(input, request.OutputDirectory);
        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-romfs-unpack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            RomFsPortableExtractSummary summary;
            try
            {
                summary = RomFsPortable.Unpack(input, staging);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or OverflowException or DecoderFallbackException)
            {
                throw new WorkspaceException($"No pude leer el RomFS: {ex.Message}");
            }

            if (Directory.Exists(output))
                Directory.Delete(output);
            Directory.Move(staging, output);
            return new RomFsUnpackResponse(
                input,
                output,
                summary.DirectoryCount,
                summary.FileCount,
                summary.FileBytes,
                "RomFS desempaquetado. El archivo de origen no se modifica.");
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            throw new WorkspaceException($"No pude publicar los archivos RomFS: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static RomFsPackResponse Pack(RomFsPackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputDirectory))
            throw new WorkspaceException("Elegí una carpeta RomFS para empaquetar.");

        var input = Path.GetFullPath(request.InputDirectory.Trim());
        if (!Directory.Exists(input))
            throw new WorkspaceException("La carpeta RomFS indicada no existe.");

        var summary = InspectSourceTree(input);
        var output = ResolvePackOutput(input, request.OutputFile);
        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var stagedOutput = Path.Combine(parent, $".pk3ds-romfs-pack-{Guid.NewGuid():N}.tmp");
        try
        {
            ProjectTools.BuildRomFs(input, stagedOutput);
            if (!File.Exists(stagedOutput))
                throw new WorkspaceException("El constructor no generó el archivo RomFS.");

            File.Move(stagedOutput, output);
            return new RomFsPackResponse(
                input,
                output,
                summary.DirectoryCount,
                summary.FileCount,
                new FileInfo(output).Length,
                "RomFS empaquetado. La carpeta de origen no se modifica.");
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or OverflowException)
        {
            throw new WorkspaceException($"No pude empaquetar el RomFS: {ex.Message}");
        }
        finally
        {
            if (File.Exists(stagedOutput))
                File.Delete(stagedOutput);
        }
    }

    private static SourceTreeSummary InspectSourceTree(string root)
    {
        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new WorkspaceException("La carpeta RomFS no puede ser un enlace simbólico.");

            var pending = new Stack<string>();
            pending.Push(root);
            var directoryCount = 0;
            var fileCount = 0;
            long fileBytes = 0;
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                directoryCount = checked(directoryCount + 1);
                if (directoryCount > 1_000_000)
                    throw new WorkspaceException("La carpeta contiene demasiados directorios para empaquetar de forma segura.");
                foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new WorkspaceException("No se pueden empaquetar enlaces simbólicos dentro del RomFS.");

                    ValidatePortableName(Path.GetFileName(path));

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(path);
                        continue;
                    }

                    fileCount = checked(fileCount + 1);
                    if (fileCount > 1_000_000)
                        throw new WorkspaceException("La carpeta contiene demasiados archivos para empaquetar de forma segura.");
                    fileBytes = checked(fileBytes + new FileInfo(path).Length);
                }
            }

            return new SourceTreeSummary(directoryCount, fileCount, fileBytes);
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or OverflowException)
        {
            throw new WorkspaceException($"No pude revisar la carpeta RomFS: {ex.Message}");
        }
    }

    private static string ResolveUnpackOutput(string input, string? requested)
    {
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-unpacked")
            : Path.GetFullPath(requested.Trim());
        if (File.Exists(output))
            throw new WorkspaceException("La ruta de salida ya existe como archivo. Elegí una carpeta nueva.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new WorkspaceException("La carpeta de salida ya existe y no está vacía. Elegí una carpeta nueva o vacía.");
        return output;
    }

    private static string ResolvePackOutput(string input, string? requested)
    {
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(Directory.GetParent(input)!.FullName, $"{new DirectoryInfo(input).Name}.romfs.bin")
            : Path.GetFullPath(requested.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo RomFS.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo RomFS de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, input))
            throw new WorkspaceException("El archivo RomFS de salida no puede guardarse dentro de la carpeta de origen.");
        return output;
    }

    private static bool IsInside(string file, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(file);
        return path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void ValidatePortableName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".."
            || name.IndexOfAny(['/', '\\', '\0']) >= 0)
            throw new WorkspaceException("RomFS contiene un nombre de archivo o carpeta no válido.");
        if (Encoding.Unicode.GetByteCount(name) > 1024)
            throw new WorkspaceException("Los nombres RomFS no pueden superar 512 caracteres UTF-16.");
    }

    private sealed record SourceTreeSummary(int DirectoryCount, int FileCount, long FileBytes);
}

internal sealed record RomFsPortableExtractSummary(int DirectoryCount, int FileCount, long FileBytes);

/// <summary>
/// Bounds-checked RomFS reader. The legacy reader writes names from metadata directly to disk;
/// this version validates every referenced record and path component before publishing output.
/// </summary>
internal static class RomFsPortable
{
    private const uint UnusedEntry = 0xFFFFFFFF;
    private const int IvfcHeaderSize = 0x5C;
    private const int RomFsInfoHeaderSize = 0x28;
    private const int MaxNameBytes = 1024;
    private const int MaxEntries = 1_000_000;
    private const int CopyBufferSize = 1024 * 1024;
    private static readonly Encoding StrictUtf16 = new UnicodeEncoding(false, false, true);

    public static RomFsPortableExtractSummary Unpack(string input, string outputDirectory)
    {
        using var source = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.RandomAccess);
        var layout = ReadLayout(source);
        Directory.CreateDirectory(outputDirectory);
        var outputRoot = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var seenDirectories = new HashSet<uint>();
        var seenFiles = new HashSet<uint>();
        var directoryRanges = new List<RecordRange>();
        var fileRanges = new List<RecordRange>();
        var namesByParent = new Dictionary<uint, HashSet<string>>();
        var pending = new Queue<DirectoryNode>();
        var root = ReadDirectory(source, layout, 0, directoryRanges);
        if (root.Name.Length != 0 || root.ParentOffset != 0)
            throw new InvalidDataException("La entrada raíz del RomFS no es válida.");
        seenDirectories.Add(0);
        pending.Enqueue(new DirectoryNode(root, outputDirectory));

        var directoryCount = 0;
        var fileCount = 0;
        long fileBytes = 0;
        var buffer = new byte[CopyBufferSize];
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            directoryCount = checked(directoryCount + 1);
            if (directoryCount > MaxEntries)
                throw new InvalidDataException("RomFS contiene demasiadas carpetas para procesar de forma segura.");

            var nextFile = current.Entry.FileOffset;
            while (nextFile != UnusedEntry)
            {
                if (fileCount >= MaxEntries || !seenFiles.Add(nextFile))
                    throw new InvalidDataException("La lista de archivos RomFS contiene una repetición o un ciclo.");
                var entry = ReadFile(source, layout, nextFile, fileRanges);
                if (entry.ParentOffset != current.Entry.Offset)
                    throw new InvalidDataException("Un archivo RomFS apunta a una carpeta padre distinta.");
                RegisterName(namesByParent, current.Entry.Offset, entry.Name);
                var path = Path.GetFullPath(Path.Combine(current.Path, entry.Name));
                if (!path.StartsWith(outputRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidDataException("Un nombre RomFS intenta salir de la carpeta de destino.");

                using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.SequentialScan))
                {
                    CopyRange(source, destination, checked(layout.FileDataOffset + (long)entry.DataOffset), checked((long)entry.DataSize), buffer);
                }

                fileBytes = checked(fileBytes + (long)entry.DataSize);
                fileCount++;
                nextFile = entry.SiblingOffset;
            }

            var nextDirectory = current.Entry.ChildOffset;
            while (nextDirectory != UnusedEntry)
            {
                if (directoryCount + pending.Count >= MaxEntries || !seenDirectories.Add(nextDirectory))
                    throw new InvalidDataException("La lista de carpetas RomFS contiene una repetición o un ciclo.");
                var child = ReadDirectory(source, layout, nextDirectory, directoryRanges);
                if (child.ParentOffset != current.Entry.Offset)
                    throw new InvalidDataException("Una carpeta RomFS apunta a una carpeta padre distinta.");
                RegisterName(namesByParent, current.Entry.Offset, child.Name);
                var path = Path.GetFullPath(Path.Combine(current.Path, child.Name));
                if (!path.StartsWith(outputRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidDataException("Un nombre RomFS intenta salir de la carpeta de destino.");
                Directory.CreateDirectory(path);
                pending.Enqueue(new DirectoryNode(child, path));
                nextDirectory = child.SiblingOffset;
            }
        }

        EnsureNoOverlappingRecords(directoryRanges, "carpetas");
        EnsureNoOverlappingRecords(fileRanges, "archivos");
        return new RomFsPortableExtractSummary(directoryCount, fileCount, fileBytes);
    }

    private static RomFsLayout ReadLayout(FileStream source)
    {
        if (source.Length < IvfcHeaderSize)
            throw new InvalidDataException("RomFS no contiene la cabecera IVFC completa.");

        Span<byte> header = stackalloc byte[IvfcHeaderSize];
        ReadExactlyAt(source, 0, header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x43465649
            || BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != 0x10000)
            throw new InvalidDataException("La cabecera IVFC de RomFS no es válida.");

        var masterHashSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var levels = new IvfcLevel[3];
        for (var index = 0; index < levels.Length; index++)
        {
            var position = 0x0C + (index * 0x18);
            var hashOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[position..]);
            var dataLength = BinaryPrimitives.ReadUInt64LittleEndian(header[(position + 8)..]);
            var exponent = BinaryPrimitives.ReadUInt32LittleEndian(header[(position + 16)..]);
            if (exponent is < 9 or > 20)
                throw new InvalidDataException($"El nivel IVFC {index} declara un tamaño de bloque no admitido.");
            levels[index] = new IvfcLevel(
                checked((long)hashOffset),
                checked((long)dataLength),
                1L << checked((int)exponent));
        }

        if (masterHashSize == 0 || levels[2].DataLength <= 0)
            throw new InvalidDataException("La cabecera IVFC no declara datos RomFS válidos.");
        var bodyOffset = Align(checked(levels[0].HashOffset + masterHashSize), levels[2].BlockSize);
        if (!IsRange(bodyOffset, levels[2].DataLength, source.Length))
            throw new InvalidDataException("El bloque RomFS declarado por IVFC sale del archivo.");

        Span<byte> infoHeader = stackalloc byte[RomFsInfoHeaderSize];
        ReadExactlyAt(source, bodyOffset, infoHeader);
        var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(infoHeader);
        if (headerLength != RomFsInfoHeaderSize)
            throw new InvalidDataException("La cabecera de metadatos RomFS no tiene el tamaño esperado.");

        var sections = new RomFsSection[4];
        long previousEnd = headerLength;
        for (var index = 0; index < sections.Length; index++)
        {
            var position = 4 + (index * 8);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(infoHeader[position..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(infoHeader[(position + 4)..]);
            if (offset < previousEnd || !IsRange(offset, size, levels[2].DataLength))
                throw new InvalidDataException("Una sección de metadatos RomFS está fuera de rango o se superpone.");
            sections[index] = new RomFsSection(offset, size);
            previousEnd = checked((long)offset + size);
        }

        var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(infoHeader[0x24..]);
        if (dataOffset < previousEnd || dataOffset > levels[2].DataLength)
            throw new InvalidDataException("El inicio de los datos RomFS no sigue a los metadatos.");
        if (sections[1].Size < 0x18)
            throw new InvalidDataException("La tabla de carpetas RomFS no contiene la entrada raíz.");

        return new RomFsLayout(
            bodyOffset,
            levels[2].DataLength,
            checked(bodyOffset + dataOffset),
            sections[1].Offset,
            sections[1].Size,
            sections[3].Offset,
            sections[3].Size,
            checked(levels[2].DataLength - dataOffset));
    }

    private static RomFsDirectory ReadDirectory(FileStream source, RomFsLayout layout, uint offset, List<RecordRange> ranges)
    {
        if (offset % 4 != 0 || offset > layout.DirectoryMetadataSize - 0x18)
            throw new InvalidDataException("Una entrada de carpeta RomFS apunta fuera de su tabla.");
        var absolute = checked(layout.BodyOffset + layout.DirectoryMetadataOffset + offset);
        Span<byte> header = stackalloc byte[0x18];
        ReadExactlyAt(source, absolute, header);
        var nameLength = BinaryPrimitives.ReadUInt32LittleEndian(header[0x14..]);
        var paddedNameLength = Align4(nameLength);
        var recordLength = checked(0x18L + paddedNameLength);
        if (!IsRange(offset, recordLength, layout.DirectoryMetadataSize))
            throw new InvalidDataException("Una entrada de carpeta RomFS tiene un nombre fuera de rango.");
        var name = ReadName(source, checked(absolute + 0x18), nameLength, allowEmpty: offset == 0);
        if (offset != 0)
            ValidateMetadataName(name);
        ranges.Add(new RecordRange(offset, checked(offset + (uint)recordLength)));
        return new RomFsDirectory(
            offset,
            BinaryPrimitives.ReadUInt32LittleEndian(header),
            BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[0x0C..]),
            name);
    }

    private static RomFsFile ReadFile(FileStream source, RomFsLayout layout, uint offset, List<RecordRange> ranges)
    {
        if (layout.FileMetadataSize < 0x20 || offset % 4 != 0 || offset > layout.FileMetadataSize - 0x20)
            throw new InvalidDataException("Una entrada de archivo RomFS apunta fuera de su tabla.");
        var absolute = checked(layout.BodyOffset + layout.FileMetadataOffset + offset);
        Span<byte> header = stackalloc byte[0x20];
        ReadExactlyAt(source, absolute, header);
        var dataOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
        var dataSize = BinaryPrimitives.ReadUInt64LittleEndian(header[0x10..]);
        var nameLength = BinaryPrimitives.ReadUInt32LittleEndian(header[0x1C..]);
        var paddedNameLength = Align4(nameLength);
        var recordLength = checked(0x20L + paddedNameLength);
        if (!IsRange(offset, recordLength, layout.FileMetadataSize))
            throw new InvalidDataException("Una entrada de archivo RomFS tiene un nombre fuera de rango.");
        if (dataOffset > (ulong)layout.FileDataSize || dataSize > (ulong)layout.FileDataSize - dataOffset)
            throw new InvalidDataException("El contenido de un archivo RomFS sale del bloque de datos.");
        var name = ReadName(source, checked(absolute + 0x20), nameLength, allowEmpty: false);
        ValidateMetadataName(name);
        ranges.Add(new RecordRange(offset, checked(offset + (uint)recordLength)));
        return new RomFsFile(
            offset,
            BinaryPrimitives.ReadUInt32LittleEndian(header),
            BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
            checked((long)dataOffset),
            checked((long)dataSize),
            name);
    }

    private static string ReadName(FileStream source, long offset, uint byteLength, bool allowEmpty)
    {
        if ((byteLength & 1) != 0 || byteLength > MaxNameBytes || (!allowEmpty && byteLength == 0))
            throw new InvalidDataException("Una entrada RomFS contiene un nombre vacío o demasiado largo.");
        var bytes = new byte[checked((int)byteLength)];
        ReadExactlyAt(source, offset, bytes);
        try
        {
            return StrictUtf16.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Un nombre RomFS contiene texto UTF-16 inválido.", ex);
        }
    }

    private static void ValidateMetadataName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".."
            || name.IndexOfAny(['/', '\\', '\0']) >= 0)
            throw new InvalidDataException("Un nombre RomFS no es un componente de ruta válido.");
    }

    private static void RegisterName(Dictionary<uint, HashSet<string>> namesByParent, uint parent, string name)
    {
        if (!namesByParent.TryGetValue(parent, out var names))
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            namesByParent.Add(parent, names);
        }
        if (!names.Add(name))
            throw new InvalidDataException($"RomFS contiene dos entradas llamadas '{name}' en la misma carpeta.");
    }

    private static void EnsureNoOverlappingRecords(List<RecordRange> ranges, string kind)
    {
        ranges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        for (var index = 1; index < ranges.Count; index++)
        {
            if (ranges[index].Start < ranges[index - 1].End)
                throw new InvalidDataException($"Las entradas de {kind} RomFS se superponen.");
        }
    }

    private static void CopyRange(FileStream source, FileStream destination, long sourceOffset, long length, byte[] buffer)
    {
        source.Position = sourceOffset;
        while (length > 0)
        {
            var count = (int)Math.Min(buffer.Length, length);
            source.ReadExactly(buffer.AsSpan(0, count));
            destination.Write(buffer, 0, count);
            length -= count;
        }
    }

    private static void ReadExactlyAt(Stream source, long offset, Span<byte> data)
    {
        if (!IsRange(offset, data.Length, source.Length))
            throw new InvalidDataException("Una sección RomFS sale del archivo de origen.");
        source.Position = offset;
        source.ReadExactly(data);
    }

    private static long Align(long value, long alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    private static uint Align4(uint value) => checked((value + 3u) & ~3u);

    private static bool IsRange(long offset, long size, long limit) =>
        offset >= 0 && size >= 0 && offset <= limit && size <= limit - offset;

    private static bool IsRange(uint offset, uint size, long limit) =>
        offset <= limit && size <= limit - offset;

    private sealed record IvfcLevel(long HashOffset, long DataLength, long BlockSize);
    private sealed record RomFsSection(uint Offset, uint Size);
    private sealed record RomFsLayout(
        long BodyOffset,
        long BodyLength,
        long FileDataOffset,
        uint DirectoryMetadataOffset,
        uint DirectoryMetadataSize,
        uint FileMetadataOffset,
        uint FileMetadataSize,
        long FileDataSize);
    private sealed record RomFsDirectory(
        uint Offset,
        uint ParentOffset,
        uint SiblingOffset,
        uint ChildOffset,
        uint FileOffset,
        string Name);
    private sealed record RomFsFile(uint Offset, uint ParentOffset, uint SiblingOffset, long DataOffset, long DataSize, string Name);
    private sealed record DirectoryNode(RomFsDirectory Entry, string Path);
    private sealed record RecordRange(uint Start, uint End);
}
