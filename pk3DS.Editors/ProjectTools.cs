using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private static readonly object CiaBuildLock = new();

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
            throw new WorkspaceException("Elegí un archivo .cxi, .3ds o .cia para extraer.");

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
                else if (format == "3DS")
                    new NCSD().ExtractFilesFromNCSD(input, staging, log, progress);
                else
                    ExtractFromCia(input, staging, log, progress);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or EndOfStreamException or InvalidDataException or OverflowException)
            {
                throw new WorkspaceException($"No pude extraer el archivo: {ex.Message}");
            }

            // An empty destination is safe to reuse, but Move cannot replace an
            // existing directory. Remove only the directory we already verified
            // is empty, immediately before promoting the completed staging tree.
            if (Directory.Exists(output))
                Directory.Delete(output);
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

    public static RebuildCrrResponse RebuildCrr(RebuildCrrRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        const string crrRelative = ".crr/static.crr";
        var crrPath = EditorSession.GetChildPath(workspace.RomFsPath, crrRelative);
        if (!File.Exists(crrPath))
            throw new WorkspaceException("No encuentro RomFS/.crr/static.crr en el workspace.");

        var croPaths = Directory.EnumerateFiles(workspace.RomFsPath, "*.cro", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (croPaths.Length == 0)
            throw new WorkspaceException("No encuentro archivos CRO en la raíz del RomFS.");

        var originalCros = croPaths.Select(File.ReadAllBytes).ToArray();
        var prepared = new byte[originalCros.Length][];
        for (var index = 0; index < originalCros.Length; index++)
        {
            try
            {
                prepared[index] = CRO.Rehash(originalCros[index]);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
            {
                throw new WorkspaceException($"No pude validar el CRO {Path.GetFileName(croPaths[index])}: {ex.Message}");
            }
        }
        var rebuilt = CRO.RebuildCRR(File.ReadAllBytes(crrPath), prepared);
        var export = EditorSession.ExportCrr(
            workspace.RootPath,
            request.OutputDirectory,
            request.TitleId,
            label: "crr");

        return new RebuildCrrResponse(
            workspace.Version.ToString(),
            export.OutputFolder,
            export.ZipPath,
            export.ChangedFiles,
            croPaths.Length,
            originalCros.Zip(prepared).Count(pair => !pair.First.SequenceEqual(pair.Second)),
            rebuilt.Changed,
            export.ChangedFiles.Length == 0
                ? "El CRR y los hashes internos ya estaban actualizados; se generó un parche vacío y el workspace original no se modificó."
                : "Hashes CRO y static.crr reconstruidos en un parche LayeredFS; el workspace original no se modifica.");
    }

    public static RebuildCiaResponse RebuildCia(RebuildCiaRequest request)
    {
        // The staging flow is synchronous and publishes a single user-selected output file.
        // Serialising CIA builds prevents two rapid clicks from racing past the existence check
        // and turning the second File.Move into an unrelated generic 500 error.
        lock (CiaBuildLock)
            return RebuildCiaCore(request);
    }

    private static RebuildCiaResponse RebuildCiaCore(RebuildCiaRequest request)
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
            var ciaRsf = Path.Combine(stagingDirectory, "decrypted-cia.rsf");
            File.WriteAllText(ciaRsf, "Option:\n  EnableCrypt: false\n");
            RunMakerom(makerom, stagedThreeDs, stagedCia, ciaRsf);
            if (!File.Exists(stagedCia))
                throw new WorkspaceException("makerom terminó sin generar el archivo CIA.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            try
            {
                File.Move(stagedCia, outputFile);
            }
            catch (IOException) when (File.Exists(outputFile))
            {
                throw new WorkspaceException("El archivo CIA de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
            }
            return new RebuildCiaResponse(
                workspace.Version.ToString(),
                outputFile,
                new FileInfo(outputFile).Length,
                request.Trimmed,
                makerom,
                "CIA desencriptado generado mediante makerom ignorando las firmas retail del contenido reconstruido; el workspace original no se modifica.");
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

        var codeBytes = File.ReadAllBytes(codeSource);
        if (BLZCoder.TryDecode(codeBytes, out var decodedCode))
            codeBytes = decodedCode;
        var codeText = Encoding.Unicode.GetString(codeBytes);
        if (codeText.Length > 0 && codeText[0] == '\uFEFF')
            codeText = codeText[1..];
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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                    or FormatException or EndOfStreamException or InvalidDataException or OverflowException)
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

    /// <summary>
    /// Detects the same archive families accepted by the Windows ToolsUI and dispatches to the
    /// format-specific safe unpacker. The source is never renamed, deleted, or overwritten.
    /// </summary>
    public static AutoUnpackResponse UnpackAuto(AutoUnpackRequest request)
        => UnpackAuto(request, recursionDepth: 0);

    private static AutoUnpackResponse UnpackAuto(AutoUnpackRequest request, int recursionDepth)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo de archivo para detectar.");
        var (format, identifier) = DetectAutoArchive(input);
        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? GetAutoUnpackOutput(input, format, identifier)
            : Path.GetFullPath(request.OutputDirectory.Trim());
        var response = format switch
        {
            "GARC" => FromAutoGarc(input, UnpackGarc(new UnpackGarcRequest(
                input, output, request.SkipDecompression)), identifier),
            "DARC" => FromAutoDarc(input, UnpackDarc(new UnpackDarcRequest(input, output)), identifier),
            "SARC" => FromAutoSarc(input, UnpackSarc(new UnpackSarcRequest(input, output)), identifier),
            "ALYT" => FromAutoAlyt(input, UnpackAlyt(new UnpackAlytRequest(input, output)), identifier),
            "Shuffle ARC" => FromAutoShuffle(input, UnpackShuffleArc(new UnpackShuffleArcRequest(input, output)), identifier),
            "GAR" => FromAutoGar(input, UnpackGar(new UnpackGarRequest(input, output)), identifier),
            "FARC" => FromAutoFarc(input, UnpackFarc(new UnpackFarcRequest(input, output)), identifier),
            "Mini" => FromAutoMini(input, UnpackMini(new UnpackMiniRequest(input, identifier!, output)), identifier),
            _ => throw new WorkspaceException($"No puedo desempaquetar automáticamente el formato detectado: {format}.")
        };

        if (request.Recursive && format == "Mini" && recursionDepth < MaxAutoUnpackDepth)
        {
            var nestedArchives = UnpackNestedMiniArchives(
                response.OutputDirectory,
                request.SkipDecompression,
                recursionDepth + 1);
            if (nestedArchives > 0)
            {
                response = response with
                {
                    Bytes = MeasureOutputBytes(response.OutputDirectory),
                    NestedArchives = nestedArchives,
                    Note = response.Note + $" Se abrieron {nestedArchives} archivo(s) contenedor(es) interno(s).",
                };
            }
        }
        return response;
    }

    private const int MaxAutoUnpackDepth = 8;

    private static int UnpackNestedMiniArchives(string directory, bool skipDecompression, int recursionDepth)
    {
        var nestedArchives = 0;
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (var file in files)
        {
            try
            {
                _ = DetectAutoArchive(file);
            }
            catch (WorkspaceException)
            {
                // Mini blocks are often ordinary raw data. Only recognized containers recurse.
                continue;
            }

            var nested = UnpackAuto(new AutoUnpackRequest(
                file,
                Recursive: true,
                SkipDecompression: skipDecompression), recursionDepth);
            nestedArchives += 1 + nested.NestedArchives;
        }
        return nestedArchives;
    }

    private static string GetAutoUnpackOutput(string input, string format, string? identifier)
    {
        var parent = Path.GetDirectoryName(input)!;
        var stem = Path.GetFileNameWithoutExtension(input);
        return format switch
        {
            "GARC" => Path.Combine(parent, $"{stem}_g"),
            "DARC" => Path.Combine(parent, $"{stem}_d"),
            "Mini" when !string.IsNullOrWhiteSpace(identifier) =>
                Path.Combine(parent, $"{stem}_{identifier.ToLowerInvariant()}"),
            _ => Path.Combine(parent, $"{stem}-unpacked"),
        };
    }

    /// <summary>
    /// Detects the Windows ToolsUI folder conventions and delegates to the safe packer. Folders
    /// ending in <c>_g</c> become GARC, folders ending in <c>_d</c> become DARC, and every other
    /// folder must end in an explicit two-letter Mini identifier such as <c>_wd</c> or <c>_zo</c>.
    /// The source folder is never deleted or modified. DARC folders also use a neighboring
    /// same-stem file with no extension, .bin, or .darc as a template when available. Mini
    /// folders also reuse an adjacent same-stem file when it has a compatible padded header.
    /// </summary>
    public static AutoPackResponse PackAuto(AutoPackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputDirectory))
            throw new WorkspaceException("Elegí una carpeta cuyo nombre indique el formato: _g, _d o _XX para Mini.");

        var input = Path.GetFullPath(request.InputDirectory.Trim());
        if (!Directory.Exists(input))
            throw new WorkspaceException("La carpeta de entrada no existe.");

        var folderName = new DirectoryInfo(input).Name;
        if (folderName.EndsWith("_g", StringComparison.OrdinalIgnoreCase))
        {
            var packed = PackGarc(new PackGarcRequest(input, request.OutputFile, request.GarcVersion, request.GarcBytesPadding));
            return new AutoPackResponse(
                input,
                "GARC",
                null,
                packed.OutputFile,
                packed.Files,
                packed.Bytes,
                $"GARC detectado por el sufijo _g. {packed.Note}");
        }

        if (folderName.EndsWith("_d", StringComparison.OrdinalIgnoreCase))
        {
            var template = FindDarcTemplate(input);
            var defaultOutput = GetAutoDarcOutput(input);
            var packed = PackDarc(new PackDarcRequest(
                input,
                request.OutputFile ?? defaultOutput,
                template));
            return new AutoPackResponse(
                input,
                "DARC",
                null,
                packed.OutputFile,
                packed.Files,
                packed.Bytes,
                $"DARC detectado por el sufijo _d. {packed.Note}");
        }

        var separator = folderName.LastIndexOf('_');
        if (separator < 0 || separator == folderName.Length - 1)
            throw new WorkspaceException("No pude detectar el formato de la carpeta. Usá un sufijo _g, _d o _XX para Mini.");

        var identifier = NormalizeMiniIdentifier(folderName[(separator + 1)..]);
        var defaultMiniOutput = Path.Combine(
            Directory.GetParent(input)!.FullName,
            $"{folderName[..separator]}.{identifier.ToLowerInvariant()}");
        var mini = PackMini(new PackMiniRequest(
            input,
            identifier,
            request.OutputFile ?? defaultMiniOutput,
            FindMiniTemplate(input, identifier)));
        return new AutoPackResponse(
            input,
            "Mini",
            identifier,
            mini.OutputFile,
            mini.Files,
            mini.Bytes,
            $"Mini {identifier} detectado por el sufijo de la carpeta. {mini.Note}");
    }

    private static string GetAutoDarcOutput(string input)
    {
        var parent = Directory.GetParent(input)!.FullName;
        var stem = new DirectoryInfo(input).Name[..^2];
        var output = Path.Combine(parent, stem + ".darc");
        if (!File.Exists(output) && !Directory.Exists(output))
            return output;

        var suffix = 1;
        do
        {
            var label = suffix == 1 ? "-repacked" : "-repacked-" + suffix;
            output = Path.Combine(parent, stem + label + ".darc");
            suffix++;
        } while (File.Exists(output) || Directory.Exists(output));
        return output;
    }

    private static string? FindDarcTemplate(string input)
    {
        var parent = Directory.GetParent(input)!.FullName;
        var stem = new DirectoryInfo(input).Name[..^2];
        var candidates = new[]
        {
            Path.Combine(parent, stem),
            Path.Combine(parent, stem + ".bin"),
            Path.Combine(parent, stem + ".darc"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindMiniTemplate(string input, string identifier)
    {
        var parent = Directory.GetParent(input)!.FullName;
        var folderName = new DirectoryInfo(input).Name;
        var separator = folderName.LastIndexOf('_');
        if (separator <= 0)
            return null;

        var stem = folderName[..separator];
        var candidates = new[]
        {
            Path.Combine(parent, stem),
            Path.Combine(parent, stem + ".bin"),
            Path.Combine(parent, stem + "." + identifier.ToLowerInvariant()),
            Path.Combine(parent, stem + "." + identifier.ToUpperInvariant()),
        };
        return candidates.FirstOrDefault(File.Exists);
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

    /// <summary>
    /// Reorders the FATB references of a GARC while leaving its FIMB payload byte-for-byte
    /// untouched. This is the headless equivalent of the Windows GARC Shuffler, but writes a new
    /// file so an accidental shuffle can never damage the source archive.
    /// </summary>
    public static ShuffleGarcResponse ShuffleGarc(ShuffleGarcRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo GARC para reordenar.");
        var output = string.IsNullOrWhiteSpace(request.OutputFile)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-shuffled.garc")
            : Path.GetFullPath(request.OutputFile.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .garc.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("La salida debe ser distinta del GARC original.");

        GARC.GARCFile archive;
        byte[] source;
        lock (GarcToolLock)
        {
            try
            {
                archive = GARC.UnpackGARC(input);
                source = File.ReadAllBytes(input);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or FormatException or EndOfStreamException or InvalidDataException or OverflowException)
            {
                throw new WorkspaceException($"No pude leer el GARC: {ex.Message}");
            }
        }

        var candidates = Enumerable.Range(0, archive.fatb.Entries.Length)
            .Where(index => !archive.fatb.Entries[index].IsFolder)
            .ToArray();
        if (candidates.Length < 2)
            throw new WorkspaceException("El GARC necesita al menos dos entradas sin carpeta para reordenar.");

        var seed = request.Seed ?? Random.Shared.Next();
        var permutation = candidates.ToArray();
        var random = new Random(seed);
        for (var index = permutation.Length - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (permutation[index], permutation[other]) = (permutation[other], permutation[index]);
        }

        var outputBytes = (byte[])source.Clone();
        var fatbOffset = checked((int)archive.HeaderSize + archive.fato.HeaderSize);
        if (fatbOffset < 0 || fatbOffset > outputBytes.Length - archive.fatb.HeaderSize)
            throw new WorkspaceException("La tabla FATB del GARC sale de los límites del archivo.");

        using (var stream = new MemoryStream(outputBytes, writable: true))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            stream.Position = fatbOffset;
            writer.Write(0x46415442u); // BTAF
            writer.Write(archive.fatb.HeaderSize);
            writer.Write(archive.fatb.FileCount);
            for (var index = 0; index < archive.fatb.Entries.Length; index++)
            {
                var entry = archive.fatb.Entries[index];
                if (Array.IndexOf(candidates, index) >= 0)
                    entry = archive.fatb.Entries[permutation[Array.IndexOf(candidates, index)]];

                writer.Write(entry.Vector);
                foreach (var subEntry in entry.SubEntries)
                {
                    if (!subEntry.Exists)
                        continue;
                    writer.Write(subEntry.Start);
                    writer.Write(subEntry.End);
                    writer.Write(subEntry.Length);
                }
            }
        }

        var changedEntries = candidates.Select((destination, position) =>
        {
            var sourceEntry = archive.fatb.Entries[destination];
            var shuffledEntry = archive.fatb.Entries[permutation[position]];
            return !SameFatbEntry(sourceEntry, shuffledEntry);
        }).Count(changed => changed);

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(Path.GetTempPath(), $"pk3ds-garc-shuffle-{Guid.NewGuid():N}.garc");
        try
        {
            File.WriteAllBytes(staging, outputBytes);
            File.Move(staging, output);
        }
        finally
        {
            if (File.Exists(staging))
                File.Delete(staging);
        }

        return new ShuffleGarcResponse(
            input, output, seed, archive.fatb.Entries.Length, candidates.Length, changedEntries,
            outputBytes.LongLength,
            "Referencias FATB reordenadas en una copia; el bloque FIMB y el GARC original permanecen intactos. Usá una copia de seguridad antes de probar archivos sensibles.");
    }

    private static bool SameFatbEntry(GARC.FATB_Entry left, GARC.FATB_Entry right)
    {
        if (left.Vector != right.Vector || left.IsFolder != right.IsFolder)
            return false;
        if (left.SubEntries is null || right.SubEntries is null || left.SubEntries.Length != right.SubEntries.Length)
            return left.SubEntries is null && right.SubEntries is null;
        for (var index = 0; index < left.SubEntries.Length; index++)
        {
            var a = left.SubEntries[index];
            var b = right.SubEntries[index];
            if (a.Exists != b.Exists || a.Start != b.Start || a.End != b.End || a.Length != b.Length)
                return false;
        }
        return true;
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
                throw new WorkspaceException("No pude desempaquetar el DARC. La tabla puede estar dañada o contener rutas inseguras.");

            var files = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Count();
            Directory.Move(staging, output);
            return new UnpackDarcResponse(
                output,
                files,
                "DARC desempaquetado con carpetas anidadas; el original no se modifica.");
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

        var template = string.IsNullOrWhiteSpace(request.TemplateFile)
            ? null
            : ResolveExistingFile(request.TemplateFile, "Elegí un DARC original para usar como plantilla.");
        if (template is not null && IsInside(template, input))
            throw new WorkspaceException("La plantilla DARC no puede estar dentro de la carpeta de entrada.");

        var output = string.IsNullOrWhiteSpace(request.OutputFile)
            ? Path.Combine(Directory.GetParent(input)!.FullName, $"{new DirectoryInfo(input).Name}.darc")
            : Path.GetFullPath(request.OutputFile.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .darc.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, input))
            throw new WorkspaceException("El DARC de salida no puede guardarse dentro de la carpeta de entrada.");
        if (template is not null && string.Equals(output, template, StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("La salida no puede ser el mismo archivo que la plantilla DARC; elegí una copia nueva.");

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
                success = DARC.Files2darc(stagedInput, originalDARC: template, outFile: stagedOutput);
            }
            if (!success || !File.Exists(stagedOutput))
                throw new WorkspaceException(template is null
                    ? "No pude empaquetar el DARC desde la carpeta indicada."
                    : "No pude empaquetar el DARC usando la plantilla. Revisá que conserve las mismas entradas y rutas.");

            File.Move(stagedOutput, output);
            var files = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Count();
            return new PackDarcResponse(
                output,
                files,
                new FileInfo(output).Length,
                template is null
                    ? "DARC empaquetado desde una copia de la carpeta de entrada; el origen no se modifica."
                    : "DARC reconstruido desde una copia usando la plantilla original; se conserva el prefijo del contenedor y el origen no se modifica.");
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

    public static UnpackAlytResponse UnpackAlyt(UnpackAlytRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo ALYT para desempaquetar.");
        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-alyt-unpacked")
            : Path.GetFullPath(request.OutputDirectory.Trim());
        if (File.Exists(output) || Directory.Exists(output))
            throw new WorkspaceException("La carpeta de salida ya existe. Elegí una carpeta nueva.");

        byte[] source;
        try
        {
            source = File.ReadAllBytes(input);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceException($"No pude leer el ALYT: {ex.Message}");
        }

        ALYTPortable alyt;
        try
        {
            alyt = ALYTPortable.Read(source);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
        {
            throw new WorkspaceException($"No pude leer el contenedor ALYT: {ex.Message}");
        }

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-alyt-unpack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            int files;
            lock (GarcToolLock)
            {
                try
                {
                    using var sarc = new SARC(alyt.Data);
                    ValidateSarc(sarc);
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in sarc.SFAT.Entries)
                    {
                        var name = NormalizeArchiveEntryName(sarc.GetFileName(entry), "SARC embebido en ALYT");
                        if (!names.Add(name))
                            throw new WorkspaceException($"El SARC embebido contiene dos entradas con la misma ruta: ‘{name}’.");

                        var target = GetChildPath(staging, name);
                        Directory.CreateDirectory(Directory.GetParent(target)!.FullName);
                        File.WriteAllBytes(target, sarc.GetData(entry));
                    }

                    if (alyt.Labels.Length > 0 || alyt.Symbols.Length > 0)
                    {
                        var metadata = Path.Combine(staging, ".pk3ds-alyt");
                        Directory.CreateDirectory(metadata);
                        if (alyt.Labels.Length > 0)
                            File.WriteAllLines(Path.Combine(metadata, "labels.txt"), alyt.Labels, new UTF8Encoding(false));
                        if (alyt.Symbols.Length > 0)
                            File.WriteAllLines(Path.Combine(metadata, "symbols.txt"), alyt.Symbols, new UTF8Encoding(false));
                    }

                    files = names.Count;
                }
                catch (WorkspaceException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                    or EndOfStreamException or InvalidDataException or FormatException or OverflowException)
                {
                    throw new WorkspaceException($"No pude desempaquetar el SARC embebido en ALYT: {ex.Message}");
                }
            }

            Directory.Move(staging, output);
            return new UnpackAlytResponse(
                input,
                output,
                files,
                alyt.LabelCount,
                alyt.SymbolCount,
                alyt.Data.LongLength,
                $"ALYT desempaquetado extrayendo su SARC embebido; se conservaron {alyt.LabelCount} etiqueta(s) y {alyt.SymbolCount} símbolo(s), y el original no se modificó.");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static PackAlytResponse PackAlyt(PackAlytRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputDirectory))
            throw new WorkspaceException("Elegí una carpeta con los archivos del SARC interno del ALYT.");
        var input = Path.GetFullPath(request.InputDirectory.Trim());
        if (!Directory.Exists(input))
            throw new WorkspaceException("La carpeta de entrada ALYT no existe.");
        if (!HasAlytContent(input))
            throw new WorkspaceException("La carpeta de entrada ALYT no contiene archivos del SARC.");

        var labels = request.Labels ?? ReadAlytMetadata(input, "labels.txt");
        var symbols = request.Symbols ?? ReadAlytMetadata(input, "symbols.txt");

        var output = string.IsNullOrWhiteSpace(request.OutputFile)
            ? Path.Combine(Directory.GetParent(input)!.FullName, $"{new DirectoryInfo(input).Name}.alyt")
            : Path.GetFullPath(request.OutputFile.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .alyt.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, input))
            throw new WorkspaceException("El ALYT de salida no puede guardarse dentro de la carpeta de entrada.");

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-alyt-pack-{Guid.NewGuid():N}");
        var stagedInput = Path.Combine(stagingDirectory, "input");
        var stagedSarc = Path.Combine(stagingDirectory, "embedded.sarc");
        var stagedOutput = Path.Combine(stagingDirectory, "output.alyt");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            CopyDirectory(input, stagedInput);
            var metadata = Path.Combine(stagedInput, ".pk3ds-alyt");
            if (Directory.Exists(metadata))
                Directory.Delete(metadata, recursive: true);

            int files;
            byte[] sarcData;
            lock (GarcToolLock)
            {
                try
                {
                    files = SARC.Pack(stagedInput, stagedSarc);
                    sarcData = File.ReadAllBytes(stagedSarc);
                    using var sarc = new SARC(sarcData);
                    ValidateSarc(sarc);
                }
                catch (WorkspaceException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                    or InvalidDataException or EndOfStreamException or FormatException or OverflowException)
                {
                    throw new WorkspaceException($"No pude empaquetar el SARC interno del ALYT: {ex.Message}");
                }
            }

            byte[] alytData;
            try
            {
                alytData = ALYTPortable.Pack(sarcData, labels, symbols);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
            {
                throw new WorkspaceException($"No pude envolver el SARC en ALYT: {ex.Message}");
            }

            File.WriteAllBytes(stagedOutput, alytData);
            File.Move(stagedOutput, output);
            return new PackAlytResponse(
                output,
                files,
                labels.Length,
                symbols.Length,
                alytData.LongLength,
                "ALYT empaquetado con un SARC interno nuevo; la carpeta de entrada no se modifica.");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static UnpackShuffleArcResponse UnpackShuffleArc(UnpackShuffleArcRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo Shuffle ARC para desempaquetar.");
        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-shuffle-unpacked")
            : Path.GetFullPath(request.OutputDirectory.Trim());
        if (File.Exists(output) || Directory.Exists(output))
            throw new WorkspaceException("La carpeta de salida ya existe. Elegí una carpeta nueva.");

        byte[] source;
        try
        {
            source = File.ReadAllBytes(input);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceException($"No pude leer el Shuffle ARC: {ex.Message}");
        }

        ShuffleArcPortable archive;
        try
        {
            archive = ShuffleArcPortable.Read(source);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
        {
            throw new WorkspaceException($"No pude leer el contenedor Shuffle ARC: {ex.Message}");
        }

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-shuffle-unpack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var digits = Math.Max(1, archive.Entries.Length.ToString().Length);
            long bytes = 0;
            foreach (var entry in archive.Entries)
            {
                var name = $"{entry.Index.ToString($"D{digits}")}.zip";
                File.WriteAllBytes(Path.Combine(staging, name), entry.Data);
                bytes += entry.Data.LongLength;
            }

            Directory.Move(staging, output);
            return new UnpackShuffleArcResponse(
                input,
                output,
                archive.Entries.Length,
                archive.HeaderOffset,
                bytes,
                "Shuffle ARC desempaquetado en fragmentos numerados. Los fragmentos se conservaron como bytes raw y el archivo original no se modifica.");
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            throw new WorkspaceException($"No pude guardar los fragmentos del Shuffle ARC: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static UnpackGarResponse UnpackGar(UnpackGarRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo GAR para desempaquetar.");
        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-gar-unpacked")
            : Path.GetFullPath(request.OutputDirectory.Trim());
        if (File.Exists(output) || Directory.Exists(output))
            throw new WorkspaceException("La carpeta de salida ya existe. Elegí una carpeta nueva.");

        byte[] source;
        try
        {
            source = File.ReadAllBytes(input);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceException($"No pude leer el GAR: {ex.Message}");
        }

        GarPortable archive;
        try
        {
            archive = GarPortable.Read(source);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
        {
            throw new WorkspaceException($"No pude leer el contenedor GAR: {ex.Message}");
        }

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-gar-unpack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long bytes = 0;
            foreach (var entry in archive.Entries)
            {
                var name = NormalizeArchiveEntryName(entry.NameWithExtension, "GAR");
                if (!names.Add(name))
                    throw new WorkspaceException($"El GAR contiene dos entradas con la misma ruta: ‘{name}’.");
                var target = GetChildPath(staging, name);
                Directory.CreateDirectory(Directory.GetParent(target)!.FullName);
                File.WriteAllBytes(target, entry.Data);
                bytes += entry.Data.LongLength;
            }

            Directory.Move(staging, output);
            return new UnpackGarResponse(
                input,
                output,
                archive.Entries.Length,
                bytes,
                "GAR desempaquetado conservando nombres y datos; el archivo original no se modifica.");
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            throw new WorkspaceException($"No pude guardar los archivos del GAR: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
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
            FARCIndexKind indexKind;
            lock (GarcToolLock)
            {
                try
                {
                    using var farc = new FARC(input);
                    if (!farc.Valid || !farc.SigMatches)
                        throw new WorkspaceException("El archivo no es un FARC válido.");
                    indexKind = farc.IndexKind;

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
            var indexMessage = indexKind == FARCIndexKind.Crc32Hash
                ? " La variante indexada por hash no contiene los nombres originales; se usaron nombres sintéticos hash-XXXXXXXX.bin y se puede conservar el índice al volver a empaquetar eligiendo CRC32/hash."
                : " La variante SIR0 con nombres también se puede volver a empaquetar.";
            return new UnpackFarcResponse(
                output,
                files,
                $"FARC desempaquetado conservando los datos.{(indexKind == FARCIndexKind.NamedUtf16 ? " Se conservaron nombres UTF-16 y rutas anidadas." : string.Empty)}{indexMessage}");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static PackFarcResponse PackFarc(PackFarcRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputDirectory))
            throw new WorkspaceException("Elegí una carpeta FARC para empaquetar.");
        var input = Path.GetFullPath(request.InputDirectory.Trim());
        if (!Directory.Exists(input))
            throw new WorkspaceException("La carpeta de entrada no existe.");
        if (!Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Any())
            throw new WorkspaceException("La carpeta de entrada no contiene archivos.");
        if (request.DataAlignment < 4 || request.DataAlignment > 0x1000
            || (request.DataAlignment & (request.DataAlignment - 1)) != 0)
        {
            throw new WorkspaceException("La alineación FARC debe ser una potencia de dos entre 4 y 4096 bytes.");
        }

        var output = string.IsNullOrWhiteSpace(request.OutputFile)
            ? Path.Combine(Directory.GetParent(input)!.FullName, $"{new DirectoryInfo(input).Name}.farc")
            : Path.GetFullPath(request.OutputFile.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .farc.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, input))
            throw new WorkspaceException("El FARC de salida no puede guardarse dentro de la carpeta de entrada.");

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-farc-pack-{Guid.NewGuid():N}");
        var stagedInput = Path.Combine(stagingDirectory, "input");
        var stagedOutput = Path.Combine(stagingDirectory, "output.farc");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            CopyDirectory(input, stagedInput);
            int files;
            lock (GarcToolLock)
            {
                try
                {
                    files = FARC.Pack(stagedInput, stagedOutput, request.DataAlignment, request.IndexKind);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                    or InvalidDataException or OverflowException)
                {
                    throw new WorkspaceException($"No pude empaquetar el FARC: {ex.Message}");
                }
            }

            if (!File.Exists(stagedOutput))
                throw new WorkspaceException("El empaquetador no generó el archivo FARC.");
            File.Move(stagedOutput, output);
            return new PackFarcResponse(
                output,
                files,
                new FileInfo(output).Length,
                request.DataAlignment,
                request.IndexKind == FARCIndexKind.Crc32Hash
                    ? "FARC empaquetado con índice SIR0 por CRC32 desde una copia de la carpeta de entrada; los nombres hash-XXXXXXXX.bin conservan su clave y el origen no se modifica."
                    : "FARC empaquetado con índice SIR0 y nombres UTF-16 desde una copia de la carpeta de entrada; el origen no se modifica.");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static UnpackMiniResponse UnpackMini(UnpackMiniRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo Mini para desempaquetar.");
        var identifier = NormalizeMiniIdentifier(request.Identifier);
        byte[] source;
        try
        {
            source = File.ReadAllBytes(input);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceException($"No pude leer el archivo Mini: {ex.Message}");
        }

        var entries = ReadMiniEntries(source, identifier);
        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-{identifier}-unpacked")
            : Path.GetFullPath(request.OutputDirectory.Trim());
        if (File.Exists(output) || Directory.Exists(output))
            throw new WorkspaceException("La carpeta de salida ya existe. Elegí una carpeta nueva.");

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-mini-unpack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var width = Math.Max(1, entries.Length.ToString().Length);
            long bytes = 0;
            for (var index = 0; index < entries.Length; index++)
            {
                var path = Path.Combine(staging, $"{index.ToString($"D{width}")}.bin");
                File.WriteAllBytes(path, entries[index]);
                bytes += entries[index].LongLength;
            }

            Directory.Move(staging, output);
            return new UnpackMiniResponse(
                input,
                identifier,
                output,
                entries.Length,
                bytes,
                $"Mini {identifier} desempaquetado. Se conservaron los bloques y el archivo original no se modificó.");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static PackMiniResponse PackMini(PackMiniRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputDirectory))
            throw new WorkspaceException("Elegí una carpeta Mini para empaquetar.");
        var input = Path.GetFullPath(request.InputDirectory.Trim());
        if (!Directory.Exists(input))
            throw new WorkspaceException("La carpeta de entrada no existe.");
        var identifier = NormalizeMiniIdentifier(request.Identifier);
        var files = Directory.EnumerateFiles(input, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            throw new WorkspaceException("La carpeta de entrada no contiene archivos.");
        if (files.Length > ushort.MaxValue)
            throw new WorkspaceException("Un archivo Mini no puede contener más de 65535 bloques.");
        if (Directory.EnumerateDirectories(input, "*", SearchOption.TopDirectoryOnly).Any())
            throw new WorkspaceException("La carpeta Mini no puede contener subcarpetas; cada archivo representa un bloque.");

        var output = string.IsNullOrWhiteSpace(request.OutputFile)
            ? Path.Combine(Directory.GetParent(input)!.FullName, $"{new DirectoryInfo(input).Name}-{identifier}.bin")
            : Path.GetFullPath(request.OutputFile.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, input))
            throw new WorkspaceException("El archivo Mini de salida no puede guardarse dentro de la carpeta de entrada.");

        byte[][] data;
        try
        {
            data = files.Select(File.ReadAllBytes).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceException($"No pude leer la carpeta Mini: {ex.Message}");
        }

        byte[] packed;
        var preservedHeader = false;
        try
        {
            packed = Mini.PackMini(data, identifier);
            if (!string.IsNullOrWhiteSpace(request.TemplateFile))
            {
                var template = ResolveExistingFile(request.TemplateFile, "Elegí una plantilla Mini original.");
                if (IsInside(template, input))
                    throw new WorkspaceException("La plantilla Mini no puede estar dentro de la carpeta de entrada.");
                if (string.Equals(output, template, StringComparison.OrdinalIgnoreCase))
                    throw new WorkspaceException("La salida Mini no puede sobrescribir la plantilla original.");

                var templateData = File.ReadAllBytes(template);
                if (templateData.Length < 4
                    || templateData[0] != identifier[0]
                    || templateData[1] != identifier[1]
                    || BitConverter.ToUInt16(templateData, 2) != data.Length
                    || !Mini.TryGetDataStart(templateData, out var templateDataStart))
                    throw new WorkspaceException("La plantilla Mini no coincide con el identificador, la cantidad de bloques o la tabla de offsets.");

                if (Mini.TryGetDataStart(packed, out var packedDataStart) && templateDataStart > packedDataStart)
                {
                    packed = Mini.AdjustMiniHeader(packed, templateDataStart);
                    preservedHeader = true;
                }
            }
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or OverflowException)
        {
            throw new WorkspaceException($"No pude empaquetar el archivo Mini: {ex.Message}");
        }

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-mini-pack-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(staging, packed);
            File.Move(staging, output);
            return new PackMiniResponse(
                output,
                identifier,
                files.Length,
                packed.LongLength,
                preservedHeader
                    ? $"Mini {identifier} empaquetado conservando el padding de la cabecera original; la carpeta de entrada y la plantilla no se modificaron."
                    : $"Mini {identifier} empaquetado desde una copia lógica; la carpeta de entrada no se modificó.");
        }
        finally
        {
            if (File.Exists(staging))
                File.Delete(staging);
        }
    }

    public static ConvertImageResponse ConvertImage(ConvertImageRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí una imagen PNG, BCLIM o BFLIM para convertir.");
        var inputFormat = GetImageFormat(input);
        var output = ResolveImageOutput(input, request.OutputFile, inputFormat, out var outputFormat);
        var bclimFormat = ParsePortableBclimFormat(request.BclimFormat);
        byte[] outputBytes;
        int width;
        int height;
        try
        {
            if (inputFormat == "PNG")
            {
                var image = PortablePng.DecodeRgba(File.ReadAllBytes(input));
                width = image.Width;
                height = image.Height;
                outputBytes = outputFormat == "BCLIM"
                    ? BCLIMPortable.EncodeRgba(image.Rgba, width, height, bclimFormat)
                    : outputFormat == "BFLIM"
                        ? BFLIMPortable.EncodeRgba(image.Rgba, width, height, bclimFormat)
                        : PortablePng.EncodeRgba(image.Rgba, width, height);
            }
            else if (inputFormat == "BCLIM")
            {
                var image = BCLIMPortable.Read(File.ReadAllBytes(input));
                width = image.Width;
                height = image.Height;
                outputBytes = outputFormat == "PNG"
                    ? PortablePng.EncodeRgba(image.GetRgbaData(), width, height)
                    : outputFormat == "BCLIM"
                        ? BCLIMPortable.EncodeRgba(image.GetRgbaData(), width, height, bclimFormat)
                        : BFLIMPortable.EncodeRgba(image.GetRgbaData(), width, height, bclimFormat);
            }
            else
            {
                var image = BFLIMPortable.Read(File.ReadAllBytes(input));
                width = image.Width;
                height = image.Height;
                outputBytes = outputFormat == "PNG"
                    ? PortablePng.EncodeRgba(image.GetRgbaData(), width, height)
                    : outputFormat == "BCLIM"
                        ? BCLIMPortable.EncodeRgba(image.GetRgbaData(), width, height, bclimFormat)
                        : BFLIMPortable.EncodeRgba(image.GetRgbaData(), width, height, bclimFormat);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidDataException or EndOfStreamException or FormatException or OverflowException)
        {
            throw new WorkspaceException($"No pude convertir la imagen: {ex.Message}");
        }

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-image-{Guid.NewGuid():N}");
        var stagedOutput = Path.Combine(stagingDirectory, Path.GetFileName(output));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            File.WriteAllBytes(stagedOutput, outputBytes);
            File.Move(stagedOutput, output);
            return new ConvertImageResponse(
                input,
                output,
                inputFormat,
                outputFormat,
                width,
                height,
                outputBytes.LongLength,
                outputFormat == "BCLIM"
                    ? $"Imagen convertida de {inputFormat} a BCLIM {bclimFormat}; el archivo original no se modifica."
                    : $"Imagen convertida de {inputFormat} a {outputFormat}; el archivo original no se modifica.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorkspaceException($"No pude guardar la imagen convertida: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static SmdhInspectResponse InspectSmdh(SmdhInspectRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var smdh = ReadSmdh(workspace);
        return new SmdhInspectResponse(
            workspace.Version.ToString(),
            "icon.bin",
            smdh.AppInfo.Select((info, index) => new SmdhApplicationInfoResponse(
                index, info.ShortDescription, info.LongDescription, info.Publisher)).ToArray(),
            Convert.ToBase64String(PortablePng.EncodeRgba(
                smdh.GetSmallIconRgba(), SMDHPortable.SmallIconWidth, SMDHPortable.SmallIconHeight)),
            Convert.ToBase64String(PortablePng.EncodeRgba(
                smdh.GetLargeIconRgba(), SMDHPortable.LargeIconWidth, SMDHPortable.LargeIconHeight)),
            "icon.bin se leyó sin modificar el workspace.",
            ToSmdhSettingsResponse(smdh));
    }

    public static SmdhExportResponse ExportSmdh(SmdhExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var smdh = ReadSmdh(workspace);
        var outputDirectory = ResolveOutputDirectory(workspace, request.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var smdhFile = Path.Combine(outputDirectory, "icon.bin");
        var smallIconFile = Path.Combine(outputDirectory, "small-icon.png");
        var largeIconFile = Path.Combine(outputDirectory, "large-icon.png");
        File.WriteAllBytes(smdhFile, smdh.Write());
        File.WriteAllBytes(smallIconFile, PortablePng.EncodeRgba(
            smdh.GetSmallIconRgba(), SMDHPortable.SmallIconWidth, SMDHPortable.SmallIconHeight));
        File.WriteAllBytes(largeIconFile, PortablePng.EncodeRgba(
            smdh.GetLargeIconRgba(), SMDHPortable.LargeIconWidth, SMDHPortable.LargeIconHeight));

        return new SmdhExportResponse(
            workspace.Version.ToString(),
            outputDirectory,
            smdhFile,
            smallIconFile,
            largeIconFile,
            "SMDH e iconos PNG exportados desde una copia lógica; el workspace original no se modifica.");
    }

    public static SmdhUpdateResponse UpdateSmdh(SmdhUpdateRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var iconPath = FindSmdhPath(workspace);
        var smdh = ReadSmdh(workspace);
        var appInfo = request.AppInfo ?? Array.Empty<SmdhApplicationInfoRequest>();
        var slots = new HashSet<int>();
        foreach (var info in appInfo)
        {
            if (info.Slot < 0 || info.Slot >= SMDHPortable.AppInfoCount || !slots.Add(info.Slot))
                throw new WorkspaceException("Cada slot AppInfo debe estar entre 0 y 15 y aparecer una sola vez.");
            smdh.AppInfo[info.Slot] = new SMDHApplicationInfo(
                info.ShortDescription,
                info.LongDescription,
                info.Publisher);
        }
        if (request.Settings is not null)
            ApplySmdhSettings(smdh.Settings, request.Settings);

        try
        {
            if (!string.IsNullOrWhiteSpace(request.SmallIconFile))
                smdh.SetSmallIconRgba(ReadSmdhIcon(request.SmallIconFile, SMDHPortable.SmallIconWidth, SMDHPortable.SmallIconHeight));
            if (!string.IsNullOrWhiteSpace(request.LargeIconFile))
                smdh.SetLargeIconRgba(ReadSmdhIcon(request.LargeIconFile, SMDHPortable.LargeIconWidth, SMDHPortable.LargeIconHeight));
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or EndOfStreamException or InvalidDataException or FormatException or OverflowException)
        {
            throw new WorkspaceException($"No pude leer el icono PNG: {ex.Message}");
        }

        byte[] bytes;
        try
        {
            bytes = smdh.Write();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            throw new WorkspaceException($"No pude validar los metadatos SMDH: {ex.Message}");
        }

        var backupFile = Path.Combine(
            workspace.RootPath,
            ".pk3ds-backups",
            $"icon-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.bin.bak");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
            File.Copy(iconPath, backupFile);
            WriteAtomically(iconPath, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorkspaceException($"No pude actualizar icon.bin: {ex.Message}");
        }

        return new SmdhUpdateResponse(
            workspace.Version.ToString(),
            iconPath,
            backupFile,
            bytes.LongLength,
            "icon.bin actualizado; se guardó una copia de seguridad antes de escribir.");
    }

    public static SmdhImportResponse ImportSmdh(SmdhImportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        if (workspace.ExeFsPath is null)
            throw new WorkspaceException("No encuentro ExeFS. El editor SMDH necesita un ExeFS extraído.");

        var source = ResolveExistingFile(request.SourceFile, "Elegí un archivo icon.bin SMDH para importar.");
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(source);
            SMDHPortable.Read(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidDataException or EndOfStreamException or OverflowException)
        {
            throw new WorkspaceException($"El archivo seleccionado no es un SMDH válido: {ex.Message}");
        }

        var iconPath = Directory.EnumerateFiles(workspace.ExeFsPath, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(file => string.Equals(Path.GetFileName(file), "icon.bin", StringComparison.OrdinalIgnoreCase))
            ?? Path.Combine(workspace.ExeFsPath, "icon.bin");
        var backupFile = Path.Combine(
            workspace.RootPath,
            ".pk3ds-backups",
            $"icon-before-import-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.bin.bak");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
            if (File.Exists(iconPath))
                File.Copy(iconPath, backupFile);
            else
                File.WriteAllBytes(backupFile, Array.Empty<byte>());
            WriteAtomically(iconPath, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorkspaceException($"No pude importar icon.bin: {ex.Message}");
        }

        return new SmdhImportResponse(
            workspace.Version.ToString(),
            iconPath,
            backupFile,
            bytes.LongLength,
            "icon.bin importado; se guardó una copia de seguridad antes de reemplazarlo.");
    }

    public static SmdhBackupsResponse GetSmdhBackups(SmdhBackupsRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var iconPath = FindSmdhPath(workspace);
        var directory = Path.Combine(workspace.RootPath, ".pk3ds-backups");
        var backups = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "icon-*.bin.bak", SearchOption.TopDirectoryOnly)
                .Select(path => new SmdhBackupSummary(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)))
                .OrderByDescending(entry => entry.CreatedUtc)
                .ToArray()
            : [];
        return new SmdhBackupsResponse(
            workspace.Version.ToString(),
            iconPath,
            backups,
            backups.Length == 0
                ? "Todavía no hay copias de icon.bin en este workspace."
                : $"Hay {backups.Length} copia(s) de icon.bin disponibles para restaurar.");
    }

    public static SmdhRestoreResponse RestoreSmdhBackup(SmdhRestoreRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var iconPath = FindSmdhPath(workspace);
        var backupDirectory = Path.Combine(workspace.RootPath, ".pk3ds-backups");
        var backupFile = ResolveSmdhBackupFile(request.BackupFile, backupDirectory);
        byte[] backupBytes;
        try
        {
            backupBytes = File.ReadAllBytes(backupFile);
            SMDHPortable.Read(backupBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidDataException or EndOfStreamException or OverflowException)
        {
            throw new WorkspaceException($"La copia de icon.bin no es un SMDH válido: {ex.Message}");
        }

        var safetyBackup = Path.Combine(
            backupDirectory,
            $"restore-before-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}-icon.bin.bak");
        try
        {
            Directory.CreateDirectory(backupDirectory);
            File.Copy(iconPath, safetyBackup);
            WriteAtomically(iconPath, backupBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorkspaceException($"No pude restaurar icon.bin: {ex.Message}");
        }

        return new SmdhRestoreResponse(
            workspace.Version.ToString(),
            iconPath,
            backupFile,
            safetyBackup,
            backupBytes.LongLength,
            "icon.bin restaurado; el estado anterior también quedó guardado como backup.");
    }

    public static Lz11Response ProcessLz11(Lz11Request request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo para comprimir o descomprimir con LZ11.");
        var operation = request.Operation.Trim().ToLowerInvariant() switch
        {
            "compress" or "comprimir" => "compress",
            "decompress" or "descomprimir" => "decompress",
            _ => throw new WorkspaceException("La operación LZ11 debe ser compress o decompress."),
        };
        var output = ResolveLz11Output(input, request.OutputFile, operation);
        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-lz11-{Guid.NewGuid():N}");
        var stagedOutput = Path.Combine(stagingDirectory, Path.GetFileName(output));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            try
            {
                if (operation == "compress")
                    LZSS.Compress(input, stagedOutput);
                else
                    LZSS.Decompress(input, stagedOutput);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or EndOfStreamException or InvalidDataException or FormatException or OverflowException
                or InputTooLargeException or TooMuchInputException)
            {
                throw new WorkspaceException($"No pude {TranslateLzOperation(operation)} el archivo LZ11: {ex.Message}");
            }

            if (!File.Exists(stagedOutput))
                throw new WorkspaceException("El codec LZ11 no generó el archivo de salida.");
            File.Move(stagedOutput, output);
            return new Lz11Response(
                input,
                output,
                operation,
                new FileInfo(output).Length,
                $"Archivo LZ11 {TranslateLzOperation(operation)} correctamente; el original no se modifica.");
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorkspaceException($"No pude guardar el archivo LZ11: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static BlzResponse ProcessBlz(BlzRequest request)
    {
        var input = ResolveExistingFile(request.InputFile, "Elegí un archivo para comprimir o descomprimir con BLZ.");
        var operation = request.Operation.Trim().ToLowerInvariant() switch
        {
            "compress" or "comprimir" => "compress",
            "decompress" or "descomprimir" => "decompress",
            _ => throw new WorkspaceException("La operación BLZ debe ser compress o decompress."),
        };
        var output = ResolveBlzOutput(input, request.OutputFile, operation);
        byte[] processed;
        try
        {
            processed = operation == "compress"
                ? BLZCoder.Encode(File.ReadAllBytes(input), request.BestCompression, request.Arm9)
                : BLZCoder.Decode(File.ReadAllBytes(input));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidDataException or EndOfStreamException or FormatException or OverflowException
            or InputTooLargeException or InvalidOperationException)
        {
            throw new WorkspaceException($"No pude {TranslateBlzOperation(operation)} el archivo BLZ: {ex.Message}");
        }

        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pk3ds-blz-{Guid.NewGuid():N}");
        var stagedOutput = Path.Combine(stagingDirectory, Path.GetFileName(output));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            File.WriteAllBytes(stagedOutput, processed);
            File.Move(stagedOutput, output);
            return new BlzResponse(
                input,
                output,
                operation,
                processed.LongLength,
                $"Archivo BLZ {TranslateBlzOperation(operation)} correctamente; el original no se modifica.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorkspaceException($"No pude guardar el archivo BLZ: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
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
        if (stream.Length >= 0x20)
        {
            Span<byte> ciaHeader = stackalloc byte[0x20];
            stream.Position = 0;
            if (stream.Read(ciaHeader) == ciaHeader.Length && LooksLikeCia(ciaHeader))
                return "CIA";
        }

        if (stream.Length < 0x104)
            throw new WorkspaceException("El archivo es demasiado pequeño para ser un CXI, 3DS o CIA válido.");
        stream.Position = 0x100;
        Span<byte> bytes = stackalloc byte[4];
        if (stream.Read(bytes) != bytes.Length)
            throw new WorkspaceException("No pude leer la cabecera del archivo.");
        var magic = BitConverter.ToUInt32(bytes);
        return magic switch
        {
            NcchMagic => "CXI",
            NcsdMagic => "3DS",
            _ => throw new WorkspaceException("El archivo no contiene una cabecera CIA, CXI o NCSD reconocible."),
        };
    }

    private static bool LooksLikeCia(ReadOnlySpan<byte> header)
    {
        if (header.Length < 0x20)
            return false;

        var headerSize = BitConverter.ToUInt32(header[0x00..0x04]);
        var certificateSize = BitConverter.ToUInt32(header[0x08..0x0C]);
        var ticketSize = BitConverter.ToUInt32(header[0x0C..0x10]);
        var tmdSize = BitConverter.ToUInt32(header[0x10..0x14]);
        var metaSize = BitConverter.ToUInt32(header[0x14..0x18]);
        var contentSize = BitConverter.ToUInt64(header[0x18..0x20]);

        // Retail and makerom-produced CIAs use the 0x2020-byte header. Accepting the
        // compact 0x20 form also keeps the reader useful for test fixtures and tools
        // that omit the optional content-index bitmask.
        return (headerSize == 0x20 || headerSize == 0x2020) &&
            certificateSize <= 0x10000000 && ticketSize <= 0x10000000 &&
            tmdSize <= 0x10000000 && metaSize <= 0x10000000 && contentSize > 0;
    }

    private static void ExtractFromCia(
        string input, string outputDirectory, RichTextBox log, ProgressBar progress)
    {
        using var source = File.OpenRead(input);
        var header = new byte[0x20];
        source.ReadExactly(header);
        if (!LooksLikeCia(header))
            throw new WorkspaceException("El CIA no tiene una cabecera válida.");

        var headerSize = BitConverter.ToUInt32(header, 0x00);
        var certificateSize = BitConverter.ToUInt32(header, 0x08);
        var ticketSize = BitConverter.ToUInt32(header, 0x0C);
        var tmdSize = BitConverter.ToUInt32(header, 0x10);
        var contentSize = BitConverter.ToUInt64(header, 0x18);
        var contentOffset = AlignCiaSection(headerSize);
        contentOffset = AddCiaSection(contentOffset, certificateSize);
        contentOffset = AddCiaSection(contentOffset, ticketSize);
        contentOffset = AddCiaSection(contentOffset, tmdSize);

        if (contentSize > (ulong)Math.Max(0, source.Length - contentOffset))
            throw new EndOfStreamException("El contenido indicado por el CIA está truncado.");
        if (contentSize < 0x200)
            throw new InvalidDataException("El primer contenido del CIA no puede ser un NCCH completo.");
        if (contentSize > (ulong)long.MaxValue)
            throw new InvalidDataException("El contenido del CIA es demasiado grande para este sistema.");

        source.Position = checked(contentOffset + 0x100);
        Span<byte> magic = stackalloc byte[4];
        source.ReadExactly(magic);
        if (BitConverter.ToUInt32(magic) != NcchMagic)
        {
            throw new WorkspaceException(
                "El CIA no contiene un NCCH desencriptado en su primer contenido. Extraé un CIA desencriptado y completo.");
        }

        new NCCH().ExtractNCCHFromFile(input, outputDirectory, log, progress, contentOffset);
    }

    private static long AlignCiaSection(uint value) => AlignCiaSection((long)value);

    private static long AlignCiaSection(long value) => checked((value + 0x3F) & ~0x3F);

    private static long AddCiaSection(long offset, uint size) =>
        AlignCiaSection(checked(offset + size));

    private static string ResolveExtractionOutput(string input, string? requested)
    {
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-extracted")
            : Path.GetFullPath(requested.Trim());
        if (File.Exists(output))
            throw new WorkspaceException("La ruta de extracción ya existe como archivo. Elegí una carpeta nueva.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new WorkspaceException("La carpeta de extracción ya existe y no está vacía. Elegí una carpeta nueva o vacía.");
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
        if (!output.EndsWith(".cia", StringComparison.OrdinalIgnoreCase))
            output += ".cia";
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
        var bundledArchitecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "macos-arm64",
            Architecture.X64 => "macos-x64",
            _ => null,
        };
        string[] bundled = bundledArchitecture is null
            ? []
            : new[] { Path.Combine(AppContext.BaseDirectory, "tools", bundledArchitecture, "makerom") };
        string?[] candidates =
        [
            requested,
            Environment.GetEnvironmentVariable("PK3DS_MAKEROM"),
            .. bundled,
            Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "makerom.exe" : "makerom"),
            Path.Combine(workspace.RootPath, OperatingSystem.IsWindows() ? "makerom.exe" : "makerom"),
        ];
        var makerom = candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!.Trim()))
            .FirstOrDefault(File.Exists);
        if (makerom is null)
            throw new WorkspaceException("No encuentro makerom compatible. La aplicación busca primero su herramienta incluida; si falta, indicá la ruta al ejecutable o colocá uno junto a la aplicación.");
        return makerom;
    }

    private static void RunMakerom(string makerom, string inputThreeDs, string outputCia, string ciaRsf)
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
        // The workspace comes from a retail dump and the rebuilt NCCH is intentionally
        // unsigned after its contents are edited. makerom must package it without trying
        // to validate those retail signatures; this does not alter the source workspace.
        process.StartInfo.ArgumentList.Add("-ignoresign");
        process.StartInfo.ArgumentList.Add("-rsf");
        process.StartInfo.ArgumentList.Add(ciaRsf);
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

    private static string GetImageFormat(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return "PNG";
        if (extension.Equals(".bclim", StringComparison.OrdinalIgnoreCase))
            return "BCLIM";
        if (extension.Equals(".bflim", StringComparison.OrdinalIgnoreCase))
            return "BFLIM";
        throw new WorkspaceException("La imagen debe tener extensión .png, .bclim o .bflim.");
    }

    private static XLIMEncoding ParsePortableBclimFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return XLIMEncoding.RGBA8;
        if (!Enum.TryParse<XLIMEncoding>(value.Trim(), ignoreCase: true, out var format)
            || format is not (XLIMEncoding.RGBA8 or XLIMEncoding.ETC1 or XLIMEncoding.ETC1A4))
            throw new WorkspaceException("El formato de imagen debe ser RGBA8, ETC1 o ETC1A4.");
        return format;
    }

    private static string ResolveImageOutput(string input, string? requested, string inputFormat, out string outputFormat)
    {
        var defaultExtension = inputFormat == "PNG" ? ".bclim" : ".png";
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.ChangeExtension(input, defaultExtension)
            : Path.GetFullPath(requested.Trim());
        if (string.IsNullOrWhiteSpace(Path.GetExtension(output)))
            output += defaultExtension;

        var extension = Path.GetExtension(output);
        outputFormat = extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? "PNG"
            : extension.Equals(".bclim", StringComparison.OrdinalIgnoreCase)
                ? "BCLIM"
                : extension.Equals(".bflim", StringComparison.OrdinalIgnoreCase)
                    ? "BFLIM"
                    : throw new WorkspaceException("La salida debe tener extensión .png, .bclim o .bflim.");
        if (string.Equals(Path.GetFullPath(input), output, StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("La salida debe ser distinta del archivo original.");
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo de imagen.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        return output;
    }

    private static string ResolveLz11Output(string input, string? requested, string operation)
    {
        var defaultSuffix = operation == "compress" ? ".lz11" : ".decompressed";
        var output = string.IsNullOrWhiteSpace(requested)
            ? input + defaultSuffix
            : Path.GetFullPath(requested.Trim());
        if (string.Equals(Path.GetFullPath(input), output, StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("La salida debe ser distinta del archivo original.");
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        return output;
    }

    private static string TranslateLzOperation(string operation) =>
        operation == "compress" ? "comprimir" : "descomprimir";

    private static string ResolveBlzOutput(string input, string? requested, string operation)
    {
        var defaultSuffix = operation == "compress" ? ".blz" : ".decompressed";
        var output = string.IsNullOrWhiteSpace(requested)
            ? input + defaultSuffix
            : Path.GetFullPath(requested.Trim());
        if (string.Equals(Path.GetFullPath(input), output, StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("La salida debe ser distinta del archivo original.");
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        return output;
    }

    private static string TranslateBlzOperation(string operation) =>
        operation == "compress" ? "comprimir" : "descomprimir";

    private static List<RedirectFile> ResolveRedirectFiles(GameWorkspace workspace, RedirectPatchRequest request)
    {
        var language = request.Language ?? EditorSession.DefaultLanguage;
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

    private static bool HasAlytContent(string input) =>
        Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories)
            .Any(file => !IsAlytMetadataFile(input, file));

    private static bool IsAlytMetadataFile(string input, string file)
    {
        var relative = Path.GetRelativePath(input, file);
        var metadataPrefix = ".pk3ds-alyt" + Path.DirectorySeparatorChar;
        return relative.StartsWith(metadataPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ReadAlytMetadata(string input, string fileName)
    {
        var path = Path.Combine(input, ".pk3ds-alyt", fileName);
        if (!File.Exists(path))
            return Array.Empty<string>();
        try
        {
            return File.ReadAllLines(path, new UTF8Encoding(false, true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new WorkspaceException($"No pude leer la metadata ALYT {fileName}: {ex.Message}");
        }
    }

    private static void ValidateDarcFolder(string input)
    {
        if (Directory.EnumerateFiles(input, "*", SearchOption.TopDirectoryOnly).Any())
            throw new WorkspaceException("La carpeta DARC no puede tener archivos en la raíz; colocalos dentro de una carpeta.");
        var folders = Directory.GetDirectories(input, "*", SearchOption.TopDirectoryOnly);
        if (folders.Length == 0)
            throw new WorkspaceException("La carpeta DARC debe contener al menos una carpeta.");
        foreach (var folder in folders)
            ValidateDarcFolderNode(folder, Path.GetFileName(folder));
    }

    private static void ValidateDarcFolderNode(string folder, string relative)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).ToArray();
        var folders = Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly).ToArray();
        if (files.Length == 0 && folders.Length == 0)
            throw new WorkspaceException($"La carpeta DARC ‘{relative}’ está vacía.");

        foreach (var child in folders)
            ValidateDarcFolderNode(child, Path.Combine(relative, Path.GetFileName(child)));
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

        try
        {
            sarc.ValidateStructure();
        }
        catch (InvalidDataException ex)
        {
            throw new WorkspaceException($"El SARC tiene una estructura inválida: {ex.Message}");
        }
    }

    private static (string Format, string? Identifier) DetectAutoArchive(string input)
    {
        var miniIdentifier = TryGetMiniIdentifier(input);
        if (miniIdentifier is not null)
            return ("Mini", miniIdentifier);

        var signature = TryReadAsciiSignature(input);
        switch (signature)
        {
            case "CRAG":
            case "GARC":
                return ("GARC", null);
            case "SARC":
                return ("SARC", null);
            case "FARC":
                return ("FARC", null);
            case "ALYT":
                return ("ALYT", null);
        }

        string guessed;
        try
        {
            guessed = FileFormat.Guess(input).TrimStart('.').ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or EndOfStreamException or InvalidDataException or OverflowException)
        {
            throw new WorkspaceException($"No pude detectar el formato del archivo: {ex.Message}");
        }

        return guessed switch
        {
            "garc" => ("GARC", null),
            "darc" => ("DARC", null),
            "sarc" => ("SARC", null),
            "alyt" => ("ALYT", null),
            "sharc" => ("Shuffle ARC", null),
            "gar" => ("GAR", null),
            "farc" => ("FARC", null),
            _ => throw new WorkspaceException($"No detecté un contenedor compatible. El formato aparente es ‘.{guessed}’; elegí una herramienta específica si sabés qué contiene.")
        };
    }

    private static string? TryReadAsciiSignature(string input)
    {
        try
        {
            using var stream = File.OpenRead(input);
            Span<byte> signature = stackalloc byte[4];
            if (stream.Read(signature) != signature.Length)
                return null;
            return Encoding.ASCII.GetString(signature);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? TryGetMiniIdentifier(string input)
    {
        try
        {
            var length = new FileInfo(input).Length;
            if (length < 8 || length > int.MaxValue)
                return null;

            using var stream = File.OpenRead(input);
            Span<byte> header = stackalloc byte[4];
            stream.ReadExactly(header);
            var first = header[0];
            var second = header[1];
            if (first is < 0x21 or > 0x7E || second is < 0x21 or > 0x7E)
                return null;

            var count = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
            var tableEnd = checked(8L + (count * sizeof(uint)));
            if (tableEnd > length)
                return null;

            stream.Position = 4;
            Span<byte> firstOffsetBytes = stackalloc byte[sizeof(uint)];
            stream.ReadExactly(firstOffsetBytes);
            var firstOffset = BinaryPrimitives.ReadUInt32LittleEndian(firstOffsetBytes);
            stream.Position = 4L + (count * sizeof(uint));
            Span<byte> finalOffsetBytes = stackalloc byte[sizeof(uint)];
            stream.ReadExactly(finalOffsetBytes);
            var finalOffset = BinaryPrimitives.ReadUInt32LittleEndian(finalOffsetBytes);
            return finalOffset == length && firstOffset >= tableEnd && firstOffset <= finalOffset
                ? new string([(char)first, (char)second]).ToUpperInvariant()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or EndOfStreamException or OverflowException)
        {
            return null;
        }
    }

    private static AutoUnpackResponse FromAutoGarc(string input, UnpackGarcResponse response, string? identifier) =>
        CreateAutoResponse(input, "GARC", identifier, response.OutputDirectory, response.Files, MeasureOutputBytes(response.OutputDirectory), response.Note);

    private static AutoUnpackResponse FromAutoDarc(string input, UnpackDarcResponse response, string? identifier) =>
        CreateAutoResponse(input, "DARC", identifier, response.OutputDirectory, response.Files, MeasureOutputBytes(response.OutputDirectory), response.Note);

    private static AutoUnpackResponse FromAutoSarc(string input, UnpackSarcResponse response, string? identifier) =>
        CreateAutoResponse(input, "SARC", identifier, response.OutputDirectory, response.Files, MeasureOutputBytes(response.OutputDirectory), response.Note);

    private static AutoUnpackResponse FromAutoAlyt(string input, UnpackAlytResponse response, string? identifier) =>
        CreateAutoResponse(input, "ALYT", identifier, response.OutputDirectory, response.Files, response.Bytes, response.Note);

    private static AutoUnpackResponse FromAutoShuffle(string input, UnpackShuffleArcResponse response, string? identifier) =>
        CreateAutoResponse(input, "Shuffle ARC", identifier, response.OutputDirectory, response.Files, response.Bytes, response.Note);

    private static AutoUnpackResponse FromAutoGar(string input, UnpackGarResponse response, string? identifier) =>
        CreateAutoResponse(input, "GAR", identifier, response.OutputDirectory, response.Files, response.Bytes, response.Note);

    private static AutoUnpackResponse FromAutoFarc(string input, UnpackFarcResponse response, string? identifier) =>
        CreateAutoResponse(input, "FARC", identifier, response.OutputDirectory, response.Files, MeasureOutputBytes(response.OutputDirectory), response.Note);

    private static AutoUnpackResponse FromAutoMini(string input, UnpackMiniResponse response, string? identifier) =>
        CreateAutoResponse(input, "Mini", identifier, response.OutputDirectory, response.Files, response.Bytes, response.Note);

    private static AutoUnpackResponse CreateAutoResponse(
        string input, string format, string? identifier, string output, int files, long bytes, string note) =>
        new(input, format, identifier, output, files, bytes,
            $"{format} detectado automáticamente. {note}");

    private static long MeasureOutputBytes(string output)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
            total = checked(total + new FileInfo(file).Length);
        return total;
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

    private static SMDHPortable ReadSmdh(GameWorkspace workspace)
    {
        var path = FindSmdhPath(workspace);

        try
        {
            return SMDHPortable.Read(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or OverflowException)
        {
            throw new WorkspaceException($"No pude leer icon.bin como SMDH: {ex.Message}");
        }
    }

    private static SmdhSettingsResponse ToSmdhSettingsResponse(SMDHPortable smdh) => new(
        (byte[])smdh.Settings.GameRatings.Clone(),
        smdh.Settings.RegionLockout,
        smdh.Settings.MatchMakerId,
        $"0x{smdh.Settings.MatchMakerBitId:X16}",
        smdh.Settings.Flags,
        smdh.Settings.EulaVersion,
        smdh.Settings.Reserved,
        smdh.Settings.AnimationDefaultFrame,
        smdh.Settings.StreetPassId);

    private static void ApplySmdhSettings(SMDHApplicationSettings target, SmdhSettingsRequest source)
    {
        if (source.GameRatings is null || source.GameRatings.Length != SMDHApplicationSettings.GameRatingsCount)
            throw new WorkspaceException($"Los ratings SMDH deben contener exactamente {SMDHApplicationSettings.GameRatingsCount} bytes.");
        if (!float.IsFinite(source.AnimationDefaultFrame) || Math.Abs(source.AnimationDefaultFrame) > 1_000_000)
            throw new WorkspaceException("El frame de animación SMDH debe ser un número finito entre -1000000 y 1000000.");

        source.GameRatings.CopyTo(target.GameRatings, 0);
        target.RegionLockout = source.RegionLockout;
        target.MatchMakerId = source.MatchMakerId;
        target.MatchMakerBitId = ParseSmdhHexUInt64(source.MatchMakerBitId, "MatchMaker BIT ID");
        target.Flags = source.Flags;
        target.EulaVersion = source.EulaVersion;
        target.Reserved = source.Reserved;
        target.AnimationDefaultFrame = source.AnimationDefaultFrame;
        target.StreetPassId = source.StreetPassId;
    }

    private static ulong ParseSmdhHexUInt64(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new WorkspaceException($"El campo SMDH {field} no puede estar vacío.");
        var normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];
        if (normalized.Length == 0 || normalized.Length > 16 || !normalized.All(Uri.IsHexDigit)
            || !ulong.TryParse(normalized, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            throw new WorkspaceException($"El campo SMDH {field} debe ser hexadecimal de hasta 16 dígitos.");
        return parsed;
    }

    private static string FindSmdhPath(GameWorkspace workspace)
    {
        if (workspace.ExeFsPath is null)
            throw new WorkspaceException("No encuentro ExeFS. El editor SMDH necesita el icon.bin extraído.");

        var path = Directory.EnumerateFiles(workspace.ExeFsPath, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(file => string.Equals(Path.GetFileName(file), "icon.bin", StringComparison.OrdinalIgnoreCase));
        return path ?? throw new WorkspaceException("No encuentro icon.bin dentro de ExeFS.");
    }

    private static string ResolveSmdhBackupFile(string? requested, string backupDirectory)
    {
        if (string.IsNullOrWhiteSpace(requested))
            throw new WorkspaceException("Elegí una copia de icon.bin para restaurar.");
        var fullPath = Path.GetFullPath(requested.Trim());
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.Equals(parent?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                backupDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullPath).StartsWith("icon-", StringComparison.OrdinalIgnoreCase)
            || !fullPath.EndsWith(".bin.bak", StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("La copia seleccionada no pertenece a los backups de icon.bin.");
        if (!File.Exists(fullPath))
            throw new WorkspaceException("La copia de icon.bin seleccionada no existe.");
        return fullPath;
    }

    private static byte[] ReadSmdhIcon(string path, int width, int height)
    {
        var input = ResolveExistingFile(path, "Elegí un PNG para reemplazar el icono SMDH.");
        if (!Path.GetExtension(input).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("Los iconos SMDH de reemplazo deben ser archivos PNG.");
        var image = PortablePng.DecodeRgba(File.ReadAllBytes(input));
        if (image.Width != width || image.Height != height)
            throw new WorkspaceException($"El icono debe medir exactamente {width}×{height} píxeles.");
        return image.Rgba;
    }

    private static string NormalizeMiniIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new WorkspaceException("Indicá el identificador Mini de dos letras, por ejemplo WD o ZO.");
        var normalized = identifier.Trim().ToUpperInvariant();
        if (normalized.Length != 2 || normalized.Any(character => character > 0x7F))
            throw new WorkspaceException("El identificador Mini debe tener exactamente dos caracteres ASCII.");
        return normalized;
    }

    private static byte[][] ReadMiniEntries(byte[] source, string identifier)
    {
        if (source.Length < 8)
            throw new WorkspaceException("El archivo Mini es demasiado corto para contener su tabla de offsets.");
        if (source[0] != identifier[0] || source[1] != identifier[1])
            throw new WorkspaceException($"El archivo no tiene el identificador Mini esperado: {identifier}.");

        var count = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(2));
        var headerLength = checked(8 + (count * sizeof(uint)));
        if (source.Length < headerLength)
            throw new WorkspaceException("La tabla de offsets Mini está incompleta.");

        var offsets = new uint[count + 1];
        for (var index = 0; index < offsets.Length; index++)
            offsets[index] = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(4 + (index * sizeof(uint))));

        if (offsets[0] < headerLength)
            throw new WorkspaceException("El primer bloque Mini comienza dentro de la cabecera.");
        for (var index = 0; index < count; index++)
        {
            if (offsets[index] > offsets[index + 1])
                throw new WorkspaceException("La tabla de offsets Mini no está ordenada.");
            if (offsets[index + 1] > source.LongLength)
                throw new WorkspaceException("Un bloque Mini apunta fuera del archivo.");
        }
        if (offsets[^1] != source.LongLength)
            throw new WorkspaceException("El último offset Mini no coincide con el tamaño del archivo.");

        var entries = new byte[count][];
        for (var index = 0; index < count; index++)
        {
            var start = checked((int)offsets[index]);
            var length = checked((int)(offsets[index + 1] - offsets[index]));
            entries[index] = source.AsSpan(start, length).ToArray();
        }
        return entries;
    }

    private static void WriteAtomically(string target, byte[] data)
    {
        var temporary = $"{target}.pk3ds-tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
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
