using System.IO;

namespace pk3DS.Editors;

public sealed record ExeFsUnpackRequest(string InputFile, string? OutputDirectory = null);
public sealed record ExeFsUnpackResponse(
    string InputFile,
    string OutputDirectory,
    int EntryCount,
    string[] Files,
    string Message);

public sealed record ExeFsPackRequest(string InputDirectory, string? OutputFile = null);
public sealed record ExeFsPackResponse(
    string InputDirectory,
    string OutputFile,
    int EntryCount,
    long OutputBytes,
    string Message);

/// <summary>
/// Standalone ExeFS import and export operations for the project tools page.
/// </summary>
public static class ExeFsProjectTools
{
    public static ExeFsUnpackResponse Unpack(ExeFsUnpackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputFile))
            throw new WorkspaceException("Elegí un archivo exefs.bin para desempaquetar.");

        var input = Path.GetFullPath(request.InputFile.Trim());
        if (!File.Exists(input))
            throw new WorkspaceException("El archivo ExeFS indicado no existe.");

        ExeFsEntry[] entries;
        try
        {
            entries = ExeFsPortable.Unpack(File.ReadAllBytes(input));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or OverflowException)
        {
            throw new WorkspaceException($"No pude leer el ExeFS: {ex.Message}");
        }

        var output = ResolveUnpackOutput(input, request.OutputDirectory);
        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-exefs-unpack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var files = new string[entries.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                // Names are strictly validated by ExeFsPortable and remain flat ExeFS stems.
                var fileName = $"{entries[index].Name}.bin";
                files[index] = fileName;
                File.WriteAllBytes(Path.Combine(staging, fileName), entries[index].Data);
            }

            if (Directory.Exists(output))
                Directory.Delete(output);
            Directory.Move(staging, output);
            return new ExeFsUnpackResponse(
                input,
                output,
                entries.Length,
                files,
                "ExeFS desempaquetado. El archivo de origen no se modifica.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            throw new WorkspaceException($"No pude publicar los archivos ExeFS: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static ExeFsPackResponse Pack(ExeFsPackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputDirectory))
            throw new WorkspaceException("Elegí una carpeta con archivos ExeFS para empaquetar.");

        var input = Path.GetFullPath(request.InputDirectory.Trim());
        if (!Directory.Exists(input))
            throw new WorkspaceException("La carpeta de entrada ExeFS no existe.");

        var sources = Directory.EnumerateFiles(input, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal)
            .ToArray();
        if (sources.Length == 0)
            throw new WorkspaceException("La carpeta ExeFS está vacía.");
        if (sources.Length > ExeFsPortable.SlotCount)
            throw new WorkspaceException("El formato ExeFS admite como máximo diez archivos.");

        var output = ResolvePackOutput(input, request.OutputFile);
        var entries = new ExeFsEntry[sources.Length];
        try
        {
            for (var index = 0; index < sources.Length; index++)
            {
                entries[index] = new ExeFsEntry(
                    Path.GetFileNameWithoutExtension(sources[index]),
                    File.ReadAllBytes(sources[index]));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorkspaceException($"No pude leer los archivos ExeFS: {ex.Message}");
        }

        byte[] packed;
        try
        {
            packed = ExeFsPortable.Pack(entries);
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentException)
        {
            throw new WorkspaceException($"No pude empaquetar el ExeFS: {ex.Message}");
        }

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var stagedOutput = Path.Combine(parent, $".pk3ds-exefs-pack-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(stagedOutput, packed);
            File.Move(stagedOutput, output);
            return new ExeFsPackResponse(
                input,
                output,
                entries.Length,
                packed.LongLength,
                "ExeFS empaquetado. La carpeta de origen no se modifica.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            throw new WorkspaceException($"No pude guardar el ExeFS: {ex.Message}");
        }
        finally
        {
            if (File.Exists(stagedOutput))
                File.Delete(stagedOutput);
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
            ? Path.Combine(Directory.GetParent(input)!.FullName, $"{new DirectoryInfo(input).Name}.exefs.bin")
            : Path.GetFullPath(requested.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo ExeFS.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo ExeFS de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, input))
            throw new WorkspaceException("El archivo ExeFS de salida no puede guardarse dentro de la carpeta de entrada.");
        return output;
    }

    private static bool IsInside(string file, string directory)
    {
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullFile = Path.GetFullPath(file);
        return fullFile.StartsWith(fullDirectory,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
