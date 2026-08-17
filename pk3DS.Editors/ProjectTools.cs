using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.Editors;

/// <summary>
/// Safe, headless project operations for an extracted game.
/// </summary>
public static class ProjectTools
{
    private const uint NcchMagic = 0x4843434E;
    private const uint NcsdMagic = 0x4453434E;

    // RomFS.BuildRomFS uses legacy static state and a temporary filename. Serialising calls keeps
    // two simultaneous web requests from interleaving their metadata or temporary output.
    private static readonly object RomFsBuildLock = new();
    private static readonly object GarcToolLock = new();

    public static BuildFileSystemsResponse BuildFileSystems(BuildFileSystemsRequest request)
    {
        if (!request.IncludeRomFs && !request.IncludeExeFs)
            throw new WorkspaceException("Seleccioná al menos RomFS o ExeFS para construir.");

        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var outputDirectory = ResolveOutputDirectory(workspace, request.OutputDirectory);

        string[]? exeFsSources = null;
        if (request.IncludeExeFs)
        {
            if (workspace.ExeFsPath is null)
                throw new WorkspaceException("No encuentro un ExeFS extraído con code.bin.");

            exeFsSources = Directory.GetFiles(workspace.ExeFsPath, "*", SearchOption.TopDirectoryOnly);
            if (exeFsSources.Length == 0)
                throw new WorkspaceException("El ExeFS está vacío.");
            if (exeFsSources.Length > 10)
                throw new WorkspaceException("El formato ExeFS admite como máximo diez archivos.");
        }

        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            string? stagedRomFsFile = null;
            if (request.IncludeRomFs)
            {
                stagedRomFsFile = Path.Combine(stagingDirectory, "romfs.bin");
                BuildRomFs(workspace.RomFsPath, stagedRomFsFile);
            }

            string? stagedExeFsFile = null;
            if (exeFsSources is not null)
            {
                stagedExeFsFile = Path.Combine(stagingDirectory, "exefs.bin");
                if (!ExeFS.PackExeFS(exeFsSources, stagedExeFsFile))
                    throw new WorkspaceException("No pude empaquetar el ExeFS. Revisá que sus archivos sean legibles.");
            }

            Directory.CreateDirectory(outputDirectory);
            string? romFsFile = null;
            long? romFsBytes = null;
            if (stagedRomFsFile is not null)
            {
                romFsFile = Path.Combine(outputDirectory, "romfs.bin");
                File.Move(stagedRomFsFile, romFsFile, overwrite: true);
                romFsBytes = new FileInfo(romFsFile).Length;
            }

            string? exeFsFile = null;
            long? exeFsBytes = null;
            if (stagedExeFsFile is not null)
            {
                exeFsFile = Path.Combine(outputDirectory, "exefs.bin");
                File.Move(stagedExeFsFile, exeFsFile, overwrite: true);
                exeFsBytes = new FileInfo(exeFsFile).Length;
            }

            return new BuildFileSystemsResponse(
                workspace.Version.ToString(),
                outputDirectory,
                romFsFile,
                romFsBytes,
                exeFsFile,
                exeFsBytes,
                "Los binarios se construyeron desde copias lógicas del workspace; el origen no se modifica.");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static ExtractProjectResponse ExtractProject(ExtractProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
            throw new WorkspaceException("Elegí un archivo .cxi o .3ds para extraer.");

        var input = Path.GetFullPath(request.InputPath.Trim());
        if (!File.Exists(input))
            throw new WorkspaceException("El archivo indicado no existe.");

        var format = DetectFormat(input);
        var output = ResolveExtractionOutput(input, request.OutputDirectory);
        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var progress = new ProgressBar();
            var log = new RichTextBox();
            try
            {
                if (format == "CXI")
                    new NCCH().ExtractNCCHFromFile(input, staging, log, progress);
                else
                    new NCSD().ExtractFilesFromNCSD(input, staging, log, progress);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or EndOfStreamException)
            {
                throw new WorkspaceException($"No pude extraer el archivo: {ex.Message}");
            }

            Directory.Move(staging, output);
            var files = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(output, path).Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return new ExtractProjectResponse(
                format,
                output,
                files,
                "La extracción terminó. El archivo original no se modifica.");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static RebuildRomResponse RebuildRom(RebuildRomRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        if (workspace.ExeFsPath is null)
            throw new WorkspaceException("Falta ExeFS. Extraé el code.bin antes de reconstruir la ROM.");
        if (workspace.ExheaderPath is null)
            throw new WorkspaceException("Falta exheader.bin. Seleccioná el workspace completo del juego.");

        var outputFile = ResolveRomOutputFile(workspace, request.OutputFile);
        var exeFsSources = Directory.GetFiles(workspace.ExeFsPath, "*", SearchOption.TopDirectoryOnly);
        if (exeFsSources.Length == 0)
            throw new WorkspaceException("El ExeFS está vacío.");
        if (exeFsSources.Length > 10)
            throw new WorkspaceException("El formato ExeFS admite como máximo diez archivos.");

        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-rom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var romFsFile = Path.Combine(stagingDirectory, "romfs.bin");
            var exeFsFile = Path.Combine(stagingDirectory, "exefs.bin");
            BuildRomFs(workspace.RomFsPath, romFsFile);
            if (!ExeFS.PackExeFS(exeFsSources, exeFsFile))
                throw new WorkspaceException("No pude empaquetar el ExeFS. Revisá que sus archivos sean legibles.");

            var serial = string.IsNullOrWhiteSpace(request.SerialText)
                ? new Exheader(workspace.ExheaderPath).GetPokemonSerial()
                : request.SerialText.Trim();
            var stagedOutput = Path.Combine(stagingDirectory, "rebuilt.3ds");
            bool success;
            try
            {
                success = CTRUtil.BuildROM(
                    Card2: false,
                    LOGO_NAME: "Nintendo",
                    EXEFS_PATH: exeFsFile,
                    ROMFS_PATH: romFsFile,
                    EXHEADER_PATH: workspace.ExheaderPath,
                    SERIAL_TEXT: serial,
                    SAVE_PATH: stagedOutput,
                    trimmed: request.Trimmed,
                    PB_Show: new ProgressBar(),
                    TB_Progress: new RichTextBox());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                throw new WorkspaceException($"No pude reconstruir la ROM: {ex.Message}");
            }

            if (!success || !File.Exists(stagedOutput))
                throw new WorkspaceException("El ensamblador no pudo generar la ROM.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            File.Move(stagedOutput, outputFile, overwrite: true);
            return new RebuildRomResponse(
                workspace.Version.ToString(),
                outputFile,
                new FileInfo(outputFile).Length,
                request.Trimmed,
                request.Trimmed
                    ? "ROM recortada generada. El origen no se modifica."
                    : "ROM con padding de tarjeta generado. El origen no se modifica.");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static RebuildCiaResponse RebuildCia(RebuildCiaRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var outputFile = ResolveCiaOutputFile(workspace, request.OutputFile);
        var makerom = ResolveMakeromPath(workspace, request.MakeromPath);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-cia-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var stagedThreeDs = Path.Combine(stagingDirectory, "rebuilt.3ds");
            RebuildRom(new RebuildRomRequest(
                workspace.RootPath,
                stagedThreeDs,
                request.Trimmed,
                request.SerialText));

            var stagedCia = Path.Combine(stagingDirectory, "rebuilt.cia");
            RunMakerom(makerom, stagedThreeDs, stagedCia);
            if (!File.Exists(stagedCia))
                throw new WorkspaceException("makerom terminó sin generar el archivo CIA.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            File.Move(stagedCia, outputFile);
            return new RebuildCiaResponse(
                workspace.Version.ToString(),
                outputFile,
                new FileInfo(outputFile).Length,
                request.Trimmed,
                makerom,
                "CIA generado mediante makerom a partir de una ROM reconstruida; el workspace original no se modifica.");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Creates the redirect patch that the original Windows tool called “Redirect [CIA]”.
    /// The result is intentionally a patch folder, not a fabricated CIA: assembling a CIA also
    /// requires TMD, ticket, certificate and signing material that is not present in this port.
    /// </summary>
    public static RedirectPatchResponse CreateRedirectPatch(RedirectPatchRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        if (workspace.ExeFsPath is null)
            throw new WorkspaceException("Falta ExeFS. Extraé el code.bin antes de crear un parche.");

        var codeSource = Directory.GetFiles(workspace.ExeFsPath, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path).Contains("code", StringComparison.OrdinalIgnoreCase));
        if (codeSource is null)
            throw new WorkspaceException("No encuentro .code.bin en el ExeFS.");

        var paths = ResolveRedirectFiles(workspace, request);
        if (paths.Count == 0)
            throw new WorkspaceException("Elegí al menos un GARC o una ruta de RomFS para redirigir.");

        var codeText = File.ReadAllText(codeSource, Encoding.Unicode);
        if (!codeText.Contains("rom2:", StringComparison.Ordinal))
            throw new WorkspaceException("El .code.bin no es parcheable: no contiene la referencia rom2:.");

        var replacements = new List<RedirectFile>(paths.Count);
        var redirected = 0;
        foreach (var path in paths)
        {
            var found = false;
            foreach (var variant in GetRedirectVariants(path.RelativePath, path.RedirectedPath))
            {
                if (codeText.Contains(variant.Old, StringComparison.Ordinal))
                {
                    codeText = codeText.Replace(variant.Old, variant.New, StringComparison.Ordinal);
                    found = true;
                }

                if (codeText.Contains(variant.Patched, StringComparison.Ordinal))
                {
                    codeText = codeText.Replace(variant.Patched, variant.New + "\0", StringComparison.Ordinal);
                    found = true;
                }
            }

            if (found)
            {
                replacements.Add(path);
                redirected++;
            }
        }

        if (redirected == 0)
            throw new WorkspaceException("No encontré ninguna de las rutas seleccionadas dentro de .code.bin.");

        var outputDirectory = ResolvePatchOutput(workspace, request.OutputDirectory);
        var parent = Directory.GetParent(outputDirectory)!.FullName;
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(parent, $".pk3ds-patch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var codeOutput = Path.Combine(stagingDirectory, ".code.bin");
            File.WriteAllText(codeOutput, codeText, Encoding.Unicode);
            var copiedFiles = new List<string> { ".code.bin" };
            foreach (var path in replacements)
            {
                var destination = GetChildPath(stagingDirectory, path.RedirectedPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(path.SourcePath, destination);
                copiedFiles.Add(path.RedirectedPath);
            }

            Directory.Move(stagingDirectory, outputDirectory);
            return new RedirectPatchResponse(
                workspace.Version.ToString(),
                outputDirectory,
                Path.Combine(outputDirectory, ".code.bin"),
                copiedFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                redirected,
                "Parche de redirección creado. Todavía hay que integrarlo en un CIA con herramientas de firma compatibles; el workspace original no se modifica.");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static UnpackGarcResponse UnpackGarc(UnpackGarcRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo GARC para desempaquetar.");
        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-unpacked")
            : Path.GetFullPath(request.OutputDirectory.Trim());
        if (File.Exists(output) || Directory.Exists(output))
            throw new WorkspaceException("La carpeta de salida ya existe. Elegí una carpeta nueva.");

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-garc-unpack-{Guid.NewGuid():N}");
        try
        {
            int files;
            lock (GarcToolLock)
            {
                try
                {
                    files = GARC.GarcUnpack(input, staging, request.SkipDecompression);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or EndOfStreamException)
                {
                    throw new WorkspaceException($"No pude desempaquetar el GARC: {ex.Message}");
                }
            }

            Directory.Move(staging, output);
            return new UnpackGarcResponse(
                output,
                files,
                request.SkipDecompression
                    ? "GARC desempaquetado sin intentar descomprimir entradas LZSS; el original no se modifica."
                    : "GARC desempaquetado y entradas LZSS descomprimidas cuando fue posible; el original no se modifica.");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static PackGarcResponse PackGarc(PackGarcRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputDirectory))
            throw new WorkspaceException("Elegí una carpeta con archivos numerados para empaquetar.");
        var input = Path.GetFullPath(request.InputDirectory.Trim());
        if (!Directory.Exists(input))
            throw new WorkspaceException("La carpeta de entrada no existe.");
        if (!Directory.EnumerateFileSystemEntries(input).Any())
            throw new WorkspaceException("La carpeta de entrada está vacía.");

        var version = request.Version switch
        {
            4 or 0x0400 => GARC.VER_4,
            6 or 0x0600 => GARC.VER_6,
            _ => throw new WorkspaceException("La versión GARC debe ser 4 o 6."),
        };
        if (request.BytesPadding is <= 0 or > 0x1000)
            throw new WorkspaceException("El padding del GARC debe estar entre 1 y 4096 bytes.");

        var output = string.IsNullOrWhiteSpace(request.OutputFile)
            ? Path.Combine(Directory.GetParent(input)!.FullName, $"{new DirectoryInfo(input).Name}.garc")
            : Path.GetFullPath(request.OutputFile.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .garc.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, input))
            throw new WorkspaceException("El GARC de salida no puede guardarse dentro de la carpeta de entrada.");

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-garc-pack-{Guid.NewGuid():N}");
        var stagedInput = Path.Combine(stagingDirectory, "input");
        var stagedOutput = Path.Combine(stagingDirectory, "output.garc");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            CopyDirectory(input, stagedInput);
            int files;
            lock (GarcToolLock)
            {
                try
                {
                    files = GARC.PackGARC(stagedInput, stagedOutput, version, request.BytesPadding);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
                {
                    throw new WorkspaceException($"No pude empaquetar el GARC: {ex.Message}");
                }
            }

            if (!File.Exists(stagedOutput))
                throw new WorkspaceException("El empaquetador no generó el archivo GARC.");
            File.Move(stagedOutput, output);
            return new PackGarcResponse(
                output,
                files,
                new FileInfo(output).Length,
                request.Version is 0x0400 or 4 ? 4 : 6,
                "GARC empaquetado desde una copia de la carpeta de entrada; el origen no se modifica.");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static UnpackDarcResponse UnpackDarc(UnpackDarcRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo DARC para desempaquetar.");
        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-unpacked")
            : Path.GetFullPath(request.OutputDirectory.Trim());
        if (File.Exists(output) || Directory.Exists(output))
            throw new WorkspaceException("La carpeta de salida ya existe. Elegí una carpeta nueva.");

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-darc-unpack-{Guid.NewGuid():N}");
        try
        {
            bool success;
            lock (GarcToolLock)
            {
                success = DARC.Darc2files(input, staging);
            }
            if (!success || !Directory.Exists(staging))
                throw new WorkspaceException("No pude desempaquetar el DARC. Este port admite DARC con una sola capa de carpetas.");

            var files = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Count();
            Directory.Move(staging, output);
            return new UnpackDarcResponse(
                output,
                files,
                "DARC desempaquetado. Se admite la estructura habitual de una capa; el original no se modifica.");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static PackDarcResponse PackDarc(PackDarcRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputDirectory))
            throw new WorkspaceException("Elegí una carpeta DARC para empaquetar.");
        var input = Path.GetFullPath(request.InputDirectory.Trim());
        if (!Directory.Exists(input))
            throw new WorkspaceException("La carpeta de entrada no existe.");
        ValidateDarcFolder(input);

        var output = string.IsNullOrWhiteSpace(request.OutputFile)
            ? Path.Combine(Directory.GetParent(input)!.FullName, $"{new DirectoryInfo(input).Name}.darc")
            : Path.GetFullPath(request.OutputFile.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .darc.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, input))
            throw new WorkspaceException("El DARC de salida no puede guardarse dentro de la carpeta de entrada.");

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-darc-pack-{Guid.NewGuid():N}");
        var stagedInput = Path.Combine(stagingDirectory, "input");
        var stagedOutput = Path.Combine(stagingDirectory, "output.darc");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            CopyDirectory(input, stagedInput);
            bool success;
            lock (GarcToolLock)
            {
                success = DARC.Files2darc(stagedInput, outFile: stagedOutput);
            }
            if (!success || !File.Exists(stagedOutput))
                throw new WorkspaceException("No pude empaquetar el DARC. Este port admite la estructura habitual de una capa.");

            File.Move(stagedOutput, output);
            var files = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Count();
            return new PackDarcResponse(
                output,
                files,
                new FileInfo(output).Length,
                "DARC empaquetado desde una copia de la carpeta de entrada; el origen no se modifica.");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static UnpackSarcResponse UnpackSarc(UnpackSarcRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo SARC para desempaquetar.");
        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-unpacked")
            : Path.GetFullPath(request.OutputDirectory.Trim());
        if (File.Exists(output) || Directory.Exists(output))
            throw new WorkspaceException("La carpeta de salida ya existe. Elegí una carpeta nueva.");

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-sarc-unpack-{Guid.NewGuid():N}");
        try
        {
            int files;
            lock (GarcToolLock)
            {
                try
                {
                    using var sarc = new SARC(input);
                    ValidateSarc(sarc);
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in sarc.SFAT.Entries)
                    {
                        var name = NormalizeArchiveEntryName(sarc.GetFileName(entry), "SARC");
                        if (!names.Add(name))
                            throw new WorkspaceException($"El SARC contiene dos entradas con la misma ruta: ‘{name}’.");

                        var target = GetChildPath(staging, name);
                        Directory.CreateDirectory(Directory.GetParent(target)!.FullName);
                        File.WriteAllBytes(target, sarc.GetData(entry));
                    }

                    files = names.Count;
                }
                catch (WorkspaceException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or EndOfStreamException or InvalidDataException or FormatException or OverflowException)
                {
                    throw new WorkspaceException($"No pude desempaquetar el SARC: {ex.Message}");
                }
            }

            Directory.Move(staging, output);
            return new UnpackSarcResponse(
                output,
                files,
                "SARC desempaquetado conservando las rutas raíz y anidadas; el original no se modifica.");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static PackSarcResponse PackSarc(PackSarcRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputDirectory))
            throw new WorkspaceException("Elegí una carpeta SARC para empaquetar.");
        var input = Path.GetFullPath(request.InputDirectory.Trim());
        if (!Directory.Exists(input))
            throw new WorkspaceException("La carpeta de entrada no existe.");
        if (!Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Any())
            throw new WorkspaceException("La carpeta de entrada no contiene archivos.");
        if (request.DataAlignment < 4 || request.DataAlignment > 0x1000 || (request.DataAlignment & (request.DataAlignment - 1)) != 0)
            throw new WorkspaceException("La alineación SARC debe ser una potencia de dos entre 4 y 4096 bytes.");

        var output = string.IsNullOrWhiteSpace(request.OutputFile)
            ? Path.Combine(Directory.GetParent(input)!.FullName, $"{new DirectoryInfo(input).Name}.sarc")
            : Path.GetFullPath(request.OutputFile.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .sarc.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, input))
            throw new WorkspaceException("El SARC de salida no puede guardarse dentro de la carpeta de entrada.");

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-sarc-pack-{Guid.NewGuid():N}");
        var stagedInput = Path.Combine(stagingDirectory, "input");
        var stagedOutput = Path.Combine(stagingDirectory, "output.sarc");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            CopyDirectory(input, stagedInput);
            int files;
            lock (GarcToolLock)
            {
                try
                {
                    files = SARC.Pack(stagedInput, stagedOutput, request.DataAlignment);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or OverflowException)
                {
                    throw new WorkspaceException($"No pude empaquetar el SARC: {ex.Message}");
                }
            }

            if (!File.Exists(stagedOutput))
                throw new WorkspaceException("El empaquetador no generó el archivo SARC.");
            File.Move(stagedOutput, output);
            return new PackSarcResponse(
                output,
                files,
                new FileInfo(output).Length,
                request.DataAlignment,
                "SARC empaquetado desde una copia de la carpeta de entrada; el origen no se modifica.");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static UnpackFarcResponse UnpackFarc(UnpackFarcRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo FARC para desempaquetar.");
        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-unpacked")
            : Path.GetFullPath(request.OutputDirectory.Trim());
        if (File.Exists(output) || Directory.Exists(output))
            throw new WorkspaceException("La carpeta de salida ya existe. Elegí una carpeta nueva.");

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-farc-unpack-{Guid.NewGuid():N}");
        try
        {
            int files;
            lock (GarcToolLock)
            {
                try
                {
                    using var farc = new FARC(input);
                    if (!farc.Valid || !farc.SigMatches)
                        throw new WorkspaceException("El archivo no es un FARC válido.");

                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in farc.Files)
                    {
                        var name = NormalizeArchiveEntryName(farc.GetFileName(entry), "FARC");
                        if (!names.Add(name))
                            throw new WorkspaceException($"El FARC contiene dos entradas con la misma ruta: ‘{name}’.");

                        var target = GetChildPath(staging, name);
                        Directory.CreateDirectory(Directory.GetParent(target)!.FullName);
                        File.WriteAllBytes(target, farc.GetData(entry));
                    }

                    files = names.Count;
                }
                catch (WorkspaceException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or EndOfStreamException or InvalidDataException or FormatException or OverflowException)
                {
                    throw new WorkspaceException($"No pude desempaquetar el FARC: {ex.Message}");
                }
            }

            Directory.Move(staging, output);
            return new UnpackFarcResponse(
                output,
                files,
                "FARC desempaquetado en modo de solo lectura; el formato heredado todavía no tiene empaquetador.");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static void BuildRomFs(string sourceDirectory, string outputFile)
    {
        lock (RomFsBuildLock)
        {
            try
            {
                RomFS.BuildRomFS(sourceDirectory, outputFile, new RichTextBox(), new ProgressBar());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new WorkspaceException($"No pude empaquetar el RomFS: {ex.Message}");
            }
        }
    }

    private static string DetectFormat(string input)
    {
        using var stream = File.OpenRead(input);
        if (stream.Length < 0x104)
            throw new WorkspaceException("El archivo es demasiado pequeño para ser un CXI o 3DS válido.");
        stream.Position = 0x100;
        Span<byte> bytes = stackalloc byte[4];
        if (stream.Read(bytes) != bytes.Length)
            throw new WorkspaceException("No pude leer la cabecera del archivo.");
        var magic = BitConverter.ToUInt32(bytes);
        return magic switch
        {
            NcchMagic => "CXI",
            NcsdMagic => "3DS",
            _ => throw new WorkspaceException("El archivo no contiene una cabecera CXI o NCSD reconocible."),
        };
    }

    private static string ResolveExtractionOutput(string input, string? requested)
    {
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-extracted")
            : Path.GetFullPath(requested.Trim());
        if (File.Exists(output) || Directory.Exists(output))
            throw new WorkspaceException("La carpeta de extracción ya existe. Elegí una carpeta nueva para evitar mezclar archivos.");
        return output;
    }

    private static string ResolveRomOutputFile(GameWorkspace workspace, string? requested)
    {
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(workspace.RootPath, "newROM.3ds")
            : Path.GetFullPath(requested.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .3ds.");
        if (IsInside(output, workspace.RomFsPath)
            || (workspace.ExeFsPath is not null && IsInside(output, workspace.ExeFsPath)))
        {
            throw new WorkspaceException("La ROM de salida no puede guardarse dentro del RomFS ni del ExeFS de origen.");
        }
        return output;
    }

    private static string ResolveCiaOutputFile(GameWorkspace workspace, string? requested)
    {
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(workspace.RootPath, "newROM.cia")
            : Path.GetFullPath(requested.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .cia.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo CIA de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, workspace.RomFsPath)
            || (workspace.ExeFsPath is not null && IsInside(output, workspace.ExeFsPath)))
        {
            throw new WorkspaceException("El CIA de salida no puede guardarse dentro del RomFS ni del ExeFS de origen.");
        }
        return output;
    }

    private static string ResolveMakeromPath(GameWorkspace workspace, string? requested)
    {
        var candidates = new[]
        {
            requested,
            Environment.GetEnvironmentVariable("PK3DS_MAKEROM"),
            Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "makerom.exe" : "makerom"),
            Path.Combine(workspace.RootPath, OperatingSystem.IsWindows() ? "makerom.exe" : "makerom"),
        };
        var makerom = candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!.Trim()))
            .FirstOrDefault(File.Exists);
        if (makerom is null)
            throw new WorkspaceException("No encuentro makerom. Indicá la ruta al ejecutable makerom o colocá uno junto a la aplicación.");
        return makerom;
    }

    private static void RunMakerom(string makerom, string inputThreeDs, string outputCia)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = makerom,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("cia");
        process.StartInfo.ArgumentList.Add("-ccitocia");
        process.StartInfo.ArgumentList.Add(inputThreeDs);
        process.StartInfo.ArgumentList.Add("-o");
        process.StartInfo.ArgumentList.Add(outputCia);

        try
        {
            if (!process.Start())
                throw new WorkspaceException("No pude iniciar makerom.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
                throw new WorkspaceException("makerom superó el tiempo máximo de diez minutos y fue detenido.");
            }
            Task.WaitAll(stdoutTask, stderrTask);
            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                detail = string.IsNullOrWhiteSpace(detail) ? "sin detalles" : detail.Trim();
                throw new WorkspaceException($"makerom no pudo crear el CIA: {detail}");
            }
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            throw new WorkspaceException($"No pude ejecutar makerom: {ex.Message}");
        }
    }

    private static string ResolvePatchOutput(GameWorkspace workspace, string? requested)
    {
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(workspace.RootPath, $"Patch ({DateTime.Now:yy-MM-dd@HH-mm-ss})")
            : Path.GetFullPath(requested.Trim());
        if (File.Exists(output) || Directory.Exists(output))
            throw new WorkspaceException("La carpeta de parche ya existe. Elegí una carpeta nueva para no sobrescribirla.");
        if (IsInside(output, workspace.RomFsPath)
            || (workspace.ExeFsPath is not null && IsInside(output, workspace.ExeFsPath)))
        {
            throw new WorkspaceException("La carpeta de parche no puede estar dentro del RomFS ni del ExeFS de origen.");
        }
        return output;
    }

    private static string ResolveExistingFile(string? path, string message)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new WorkspaceException(message);
        var fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath))
            throw new WorkspaceException("El archivo indicado no existe.");
        return fullPath;
    }

    private static List<RedirectFile> ResolveRedirectFiles(GameWorkspace workspace, RedirectPatchRequest request)
    {
        var language = request.Language ?? 1;
        if (language is < 0 or > 11)
            throw new WorkspaceException("El idioma debe estar entre 0 y 11.");

        var paths = new List<RedirectFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (request.GarcNames ?? []).Concat(request.AdditionalPaths ?? []))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var value = raw.Trim();
            if (value.Contains('/') || value.Contains('\\'))
            {
                AddRedirectPath(workspace, value, paths, seen);
                continue;
            }

            var references = GetGarcReferenceCandidates(workspace, value, language, request.IncludeAllLanguageVariants);
            if (references.Count == 0)
                throw new WorkspaceException($"No conozco el GARC ‘{value}’ para este juego.");
            foreach (var reference in references)
                AddRedirectPath(workspace, reference, paths, seen);
        }
        return paths;
    }

    private static List<string> GetGarcReferenceCandidates(
        GameWorkspace workspace,
        string name,
        int language,
        bool includeAllLanguageVariants)
    {
        var matching = GetGarcReferences(workspace.Version)
            .Where(reference => string.Equals(reference.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matching.Length == 0)
            return [];

        var candidates = new List<string>();
        foreach (var reference in matching)
        {
            var languageCount = reference.LanguageVariant && includeAllLanguageVariants ? 8 : 1;
            for (var index = 0; index < languageCount; index++)
            {
                var resolved = reference.LanguageVariant
                    ? reference.GetRelativeGARC(includeAllLanguageVariants ? index : language, reference.Name)
                    : reference;
                var relative = resolved.Reference.Replace(Path.DirectorySeparatorChar, '/');
                var source = Path.Combine(workspace.RomFsPath, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(source))
                    candidates.Add(relative);
            }
        }

        var distinct = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (string.Equals(name, "encdata", StringComparison.OrdinalIgnoreCase) && distinct.Count > 1)
        {
            var nonEmpty = distinct.Where(relative => new FileInfo(Path.Combine(workspace.RomFsPath, relative.Replace('/', Path.DirectorySeparatorChar))).Length > 0).ToList();
            if (nonEmpty.Count > 0)
                return nonEmpty;
            return [distinct[0]];
        }
        return distinct;
    }

    private static IEnumerable<GARCReference> GetGarcReferences(GameVersion version) => version switch
    {
        GameVersion.XY => GARCReference.GARCReference_XY,
        GameVersion.ORASDEMO or GameVersion.ORAS => GARCReference.GARCReference_AO,
        GameVersion.SMDEMO => GARCReference.GARCReference_SMDEMO,
        GameVersion.SN or GameVersion.MN or GameVersion.SM => GARCReference.GARCReference_SN.Concat(GARCReference.GARCReference_MN),
        GameVersion.US or GameVersion.UM or GameVersion.USUM => GARCReference.GARCReference_US.Concat(GARCReference.GARCReference_UM),
        _ => [],
    };

    private static void AddRedirectPath(
        GameWorkspace workspace,
        string relative,
        ICollection<RedirectFile> paths,
        ISet<string> seen)
    {
        var normalized = NormalizeRomFsPath(relative);
        var source = GetChildPath(workspace.RomFsPath, normalized);
        if (!File.Exists(source))
            throw new WorkspaceException($"No encuentro el archivo RomFS ‘{normalized}’.");
        var redirected = GetRedirectedPath(normalized);
        if (seen.Add(normalized))
            paths.Add(new RedirectFile(source, normalized, redirected));
    }

    private static string NormalizeRomFsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new WorkspaceException("La ruta de RomFS está vacía.");
        var normalized = path.Trim().Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith("a/", StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException($"La ruta ‘{path}’ no pertenece a la carpeta a del RomFS.");
        return normalized;
    }

    private static string GetRedirectedPath(string relative)
    {
        if (!relative.StartsWith("a/", StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException($"No puedo redirigir la ruta ‘{relative}’.");
        return "a" + relative[2..];
    }

    private static string GetChildPath(string root, string relative)
    {
        var candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(candidate, root))
            throw new WorkspaceException("La ruta indicada sale de la carpeta permitida.");
        return candidate;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void ValidateDarcFolder(string input)
    {
        if (Directory.EnumerateFiles(input, "*", SearchOption.TopDirectoryOnly).Any())
            throw new WorkspaceException("La carpeta DARC no puede tener archivos en la raíz; colocalos dentro de una carpeta.");
        var folders = Directory.GetDirectories(input, "*", SearchOption.TopDirectoryOnly);
        if (folders.Length == 0)
            throw new WorkspaceException("La carpeta DARC debe contener al menos una carpeta.");
        foreach (var folder in folders)
        {
            if (Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly).Any())
                throw new WorkspaceException("La estructura DARC debe tener una sola capa de carpetas.");
            if (!Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).Any())
                throw new WorkspaceException($"La carpeta DARC ‘{Path.GetFileName(folder)}’ está vacía.");
        }
    }

    private static void ValidateSarc(SARC sarc)
    {
        if (!sarc.Valid || !sarc.SigMatches || sarc.SFAT is null || !sarc.SFAT.SigMatches
            || sarc.SFAT.Entries is null || sarc.SFNT is null || !sarc.SFNT.SigMatches)
        {
            throw new WorkspaceException("El archivo no es un SARC válido o sus tablas SFAT/SFNT están incompletas.");
        }

        if (sarc.SFAT.EntryCount != sarc.SFAT.Entries.Count)
            throw new WorkspaceException("La tabla SFAT del SARC no coincide con su cantidad de entradas.");
    }

    private static string NormalizeArchiveEntryName(string name, string format)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new WorkspaceException($"El {format} contiene una entrada sin nombre.");

        var normalized = name.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/') || (normalized.Length >= 2 && normalized[1] == ':'))
            throw new WorkspaceException($"El {format} contiene una ruta absoluta no permitida: ‘{name}’.");

        var parts = normalized.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".."))
            throw new WorkspaceException($"El {format} contiene una ruta insegura: ‘{name}’.");

        return normalized;
    }

    private static IEnumerable<RedirectVariant> GetRedirectVariants(string original, string redirected)
    {
        foreach (var separator in new[] { '/', '\\' })
        {
            var oldPath = original.Replace('/', separator);
            var newPath = redirected.Replace('/', separator);
            yield return new RedirectVariant($"rom:{separator}{oldPath}", $"rom2:{separator}{oldPath}", $"rom2:{separator}{newPath}");
            yield return new RedirectVariant($"rom:{oldPath}", $"rom2:{oldPath}", $"rom2:{newPath}");
        }
    }

    private sealed record RedirectFile(string SourcePath, string RelativePath, string RedirectedPath);
    private sealed record RedirectVariant(string Old, string Patched, string New);

    private static string ResolveOutputDirectory(GameWorkspace workspace, string? requested)
    {
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(workspace.RootPath, "pk3ds-mac-build")
            : Path.GetFullPath(requested.Trim());

        if (File.Exists(output))
            throw new WorkspaceException("La salida indicada ya existe como archivo, no como carpeta.");

        if (IsInside(output, workspace.RomFsPath)
            || (workspace.ExeFsPath is not null && IsInside(output, workspace.ExeFsPath)))
        {
            throw new WorkspaceException("La carpeta de salida no puede estar dentro del RomFS ni del ExeFS de origen.");
        }

        return output;
    }

    private static bool IsInside(string candidate, string source)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullSource = Path.GetFullPath(source)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullSource, StringComparison.OrdinalIgnoreCase)
            || fullCandidate.StartsWith(fullSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullCandidate.StartsWith(fullSource + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
