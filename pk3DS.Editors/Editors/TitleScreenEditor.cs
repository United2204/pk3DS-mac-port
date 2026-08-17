using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.Editors;

/// <summary>
/// Headless inventory and export for the Gen. VI title-screen resources.
///
/// The portable path reads the GARC/DARC containers and exports original BCLIM payloads. When
/// requested, it also decodes supported BCLIM formats and writes PNG previews without requiring
/// a Windows image stack. The explicit <see cref="Apply"/> operation is the only title-screen
/// action that updates the workspace, and it creates a backup before replacing the source GARC.
/// </summary>
public static class TitleScreenEditor
{
    private static readonly TitleArchiveSpec[] XyArchives = CreateArchives(
        [467, 468, 469, 470, 471, 472, 473, 474, 475, 476, 477, 478, 479, 480],
        ["DE", "ES", "FR", "IT", "JP", "KO", "EN"],
        ["X", "Y"]);

    private static readonly TitleArchiveSpec[] OrasArchives = CreateArchives(
        [1120, 1121, 1122, 1123, 1124, 1125, 1126, 1127, 1128, 1129, 1130, 1131, 1132, 1133, 1134, 1135],
        ["JP1", "DE", "ES", "FR", "IT", "JP", "KO", "EN"],
        ["OR", "AS"]);

    public static TitleScreenCatalogResponse GetCatalog(TitleScreenCatalogRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var catalog = ReadCatalog(workspace);
        return new TitleScreenCatalogResponse(
            workspace.Version.ToString(),
            catalog.Archives.Any(entry => entry.Compressed),
            catalog.GarcPath,
            catalog.Archives.Select(entry => entry.Summary).ToArray(),
            "Inventario de DARC y BCLIM generado en modo de solo lectura; el workspace original no se modifica.");
    }

    public static TitleScreenExportResponse Export(TitleScreenExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var catalog = ReadCatalog(workspace);
        var selected = request.FileNumber is null
            ? catalog.Archives
            : catalog.Archives.Where(entry => entry.Summary.FileNumber == request.FileNumber.Value).ToArray();

        if (selected.Length == 0)
            throw new WorkspaceException($"No existe el archivo de pantalla de título {request.FileNumber} para {workspace.Version}.");

        var invalid = selected.FirstOrDefault(entry => !entry.Summary.Valid);
        if (invalid is not null)
            throw new WorkspaceException($"No pude leer {invalid.Summary.Game}-{invalid.Summary.Language}: {invalid.Summary.Error}");

        var output = ResolveOutputDirectory(workspace, request.OutputDirectory);
        var parent = Directory.GetParent(output)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".pk3ds-title-screen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var files = new List<string>();
            var manifest = new List<object>();
            var assetCount = 0;
            var pngCount = 0;
            foreach (var archive in selected)
            {
                var label = $"{archive.Summary.Game}-{archive.Summary.Language}";
                var archiveDirectory = Path.Combine(staging, label);
                var assetDirectory = Path.Combine(archiveDirectory, "assets");
                Directory.CreateDirectory(assetDirectory);

                var rawDarcName = $"{label}.darc";
                if (request.IncludeRawDarc)
                {
                    File.WriteAllBytes(Path.Combine(archiveDirectory, rawDarcName), archive.DarcData!);
                    files.Add(Path.Combine(label, rawDarcName).Replace(Path.DirectorySeparatorChar, '/'));
                }

                var exportedAssets = new List<string>();
                foreach (var asset in archive.Assets)
                {
                    var assetName = $"{asset.EntryIndex:D3}-{SanitizeFileName(asset.Name)}";
                    var relative = Path.Combine(label, "assets", assetName).Replace(Path.DirectorySeparatorChar, '/');
                    var assetBytes = archive.AssetData[asset.EntryIndex];
                    File.WriteAllBytes(Path.Combine(assetDirectory, assetName), assetBytes);
                    files.Add(relative);
                    string? pngRelative = null;
                    if (request.IncludePng)
                    {
                        try
                        {
                            var image = BCLIMPortable.Read(assetBytes);
                            var pngName = Path.ChangeExtension(assetName, ".png");
                            pngRelative = Path.Combine(label, "assets", pngName).Replace(Path.DirectorySeparatorChar, '/');
                            File.WriteAllBytes(Path.Combine(assetDirectory, pngName),
                                PortablePng.EncodeRgba(image.GetRgbaData(), image.Width, image.Height));
                            files.Add(pngRelative);
                            pngCount++;
                        }
                        catch (Exception ex) when (ex is ArgumentException or EndOfStreamException or InvalidDataException or FormatException or OverflowException)
                        {
                            // Keep the raw BCLIM export useful when an asset is malformed or uses
                            // a format that this portable reader does not cover yet.
                        }
                    }
                    exportedAssets.Add(relative);
                    assetCount++;
                    if (pngRelative is not null)
                        exportedAssets.Add(pngRelative);
                }

                manifest.Add(new
                {
                    game = archive.Summary.Game,
                    language = archive.Summary.Language,
                    fileNumber = archive.Summary.FileNumber,
                    romFsPath = archive.Summary.RomFsPath,
                    compressed = archive.Summary.Compressed,
                    darc = request.IncludeRawDarc ? Path.Combine(label, rawDarcName).Replace(Path.DirectorySeparatorChar, '/') : null,
                    assets = exportedAssets,
                });
            }

            var manifestPath = Path.Combine(staging, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            files.Add("manifest.json");

            Directory.Move(staging, output);
            return new TitleScreenExportResponse(
                workspace.Version.ToString(),
                output,
                selected.Length,
                assetCount,
                pngCount,
                files.ToArray(),
                request.IncludePng
                    ? $"Recursos de pantalla de título exportados; se generaron {pngCount} PNG sin modificar el workspace original."
                    : "Recursos de pantalla de título exportados sin modificar el workspace original.");
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static TitleScreenPreviewResponse Preview(TitleScreenPreviewRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var catalog = ReadCatalog(workspace);
        var archive = catalog.Archives.FirstOrDefault(entry => entry.Summary.FileNumber == request.FileNumber);
        if (archive is null)
            throw new WorkspaceException($"No existe el archivo de pantalla de título {request.FileNumber} para {workspace.Version}.");
        if (!archive.Summary.Valid)
            throw new WorkspaceException($"No puedo leer {archive.Summary.Game}-{archive.Summary.Language}: {archive.Summary.Error}");
        var asset = archive.Summary.Assets.FirstOrDefault(entry => entry.EntryIndex == request.AssetEntryIndex);
        if (asset is null || !archive.AssetData.TryGetValue(request.AssetEntryIndex, out var assetBytes))
            throw new WorkspaceException($"No existe el recurso BCLIM #{request.AssetEntryIndex} dentro de {archive.Summary.Game}-{archive.Summary.Language}.");

        try
        {
            var image = BCLIMPortable.Read(assetBytes);
            var png = PortablePng.EncodeRgba(image.GetRgbaData(), image.Width, image.Height);
            return new TitleScreenPreviewResponse(
                workspace.Version.ToString(),
                archive.Summary.Game,
                archive.Summary.Language,
                request.FileNumber,
                request.AssetEntryIndex,
                asset.Name,
                image.Width,
                image.Height,
                image.Format.ToString(),
                Convert.ToBase64String(png),
                "Vista previa PNG generada en memoria; el workspace original no se modifica.");
        }
        catch (Exception ex) when (ex is ArgumentException or EndOfStreamException or InvalidDataException or FormatException or OverflowException)
        {
            throw new WorkspaceException($"No pude generar una vista previa para {asset.Name}: {ex.Message}");
        }
    }

    public static TitleScreenReplaceResponse Replace(TitleScreenReplaceRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var catalog = ReadCatalog(workspace);
        var archive = catalog.Archives.FirstOrDefault(entry => entry.Summary.FileNumber == request.FileNumber);
        if (archive is null)
            throw new WorkspaceException($"No existe el archivo de pantalla de título {request.FileNumber} para {workspace.Version}.");
        if (!archive.Summary.Valid || archive.DarcData is null)
            throw new WorkspaceException($"No puedo modificar {archive.Summary.Game}-{archive.Summary.Language}: {archive.Summary.Error}");

        var asset = archive.Summary.Assets.FirstOrDefault(entry => entry.EntryIndex == request.AssetEntryIndex);
        if (asset is null || !archive.AssetData.TryGetValue(request.AssetEntryIndex, out var originalBytes))
            throw new WorkspaceException($"No existe el recurso BCLIM #{request.AssetEntryIndex} dentro de {archive.Summary.Game}-{archive.Summary.Language}.");
        var replacementPath = ResolveExistingReplacement(request.ReplacementFile);
        var originalImage = BCLIMPortable.Read(originalBytes);
        var replacement = ReadReplacement(replacementPath, originalImage.Width, originalImage.Height);

        DARC darc;
        try
        {
            darc = new DARC(archive.DarcData);
        }
        catch (Exception ex) when (ex is ArgumentException or EndOfStreamException or IOException or InvalidDataException)
        {
            throw new WorkspaceException($"No pude abrir el DARC para reemplazar la imagen: {ex.Message}");
        }
        if (darc.Header is null || darc.Entries is null || darc.FileNameTable is null || darc.Data is null
            || request.AssetEntryIndex < 0 || request.AssetEntryIndex >= darc.Entries.Length
            || darc.Entries[request.AssetEntryIndex].IsFolder)
            throw new WorkspaceException("La entrada BCLIM seleccionada no es válida dentro del DARC.");
        if (!DARC.InsertFile(ref darc, request.AssetEntryIndex, replacement.Data))
            throw new WorkspaceException("No pude reemplazar el contenido BCLIM dentro del DARC.");

        var output = ResolveReplacementOutput(workspace, archive.Summary, asset.Name, request.OutputFile);
        Directory.CreateDirectory(Directory.GetParent(output)!.FullName);
        var darcBytes = DARC.SetDARC(darc);
        File.WriteAllBytes(output, darcBytes);
        return new TitleScreenReplaceResponse(
            workspace.Version.ToString(),
            archive.Summary.Game,
            archive.Summary.Language,
            request.FileNumber,
            request.AssetEntryIndex,
            asset.Name,
            replacement.Format,
            replacement.BclimFormat,
            output,
            darcBytes.LongLength,
            "DARC nuevo generado con la imagen reemplazada; el workspace original no se modifica.");
    }

    public static TitleScreenGarcReplaceResponse ReplaceGarc(TitleScreenReplaceRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var catalog = ReadCatalog(workspace);
        var archive = catalog.Archives.FirstOrDefault(entry => entry.Summary.FileNumber == request.FileNumber);
        if (archive is null)
            throw new WorkspaceException($"No existe el archivo de pantalla de título {request.FileNumber} para {workspace.Version}.");
        if (!archive.Summary.Valid || archive.DarcData is null)
            throw new WorkspaceException($"No puedo modificar {archive.Summary.Game}-{archive.Summary.Language}: {archive.Summary.Error}");

        var asset = archive.Summary.Assets.FirstOrDefault(entry => entry.EntryIndex == request.AssetEntryIndex);
        if (asset is null || !archive.AssetData.TryGetValue(request.AssetEntryIndex, out var originalBytes))
            throw new WorkspaceException($"No existe el recurso BCLIM #{request.AssetEntryIndex} dentro de {archive.Summary.Game}-{archive.Summary.Language}.");
        var replacementPath = ResolveExistingReplacement(request.ReplacementFile);
        var originalImage = BCLIMPortable.Read(originalBytes);
        var replacement = ReadReplacement(replacementPath, originalImage.Width, originalImage.Height);

        var output = ResolveGarcReplacementOutput(workspace, archive.Summary, request.OutputFile);
        var garcBytes = BuildGarcReplacement(
            catalog.GarcPath,
            request.FileNumber,
            request.AssetEntryIndex,
            archive.DarcData,
            archive.Summary.Compressed,
            replacement.Data);
        Directory.CreateDirectory(Directory.GetParent(output)!.FullName);
        File.WriteAllBytes(output, garcBytes);
        return new TitleScreenGarcReplaceResponse(
            workspace.Version.ToString(),
            archive.Summary.Game,
            archive.Summary.Language,
            request.FileNumber,
            request.AssetEntryIndex,
            asset.Name,
            replacement.Format,
            replacement.BclimFormat,
            archive.Summary.Compressed,
            output,
            garcBytes.LongLength,
            "Copia GARC nueva generada con el DARC reemplazado; el workspace original no se modifica.");
    }

    public static TitleScreenApplyResponse Apply(TitleScreenApplyRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var catalog = ReadCatalog(workspace);
        var archive = catalog.Archives.FirstOrDefault(entry => entry.Summary.FileNumber == request.FileNumber);
        if (archive is null)
            throw new WorkspaceException($"No existe el archivo de pantalla de título {request.FileNumber} para {workspace.Version}.");
        if (!archive.Summary.Valid || archive.DarcData is null)
            throw new WorkspaceException($"No puedo modificar {archive.Summary.Game}-{archive.Summary.Language}: {archive.Summary.Error}");

        var asset = archive.Summary.Assets.FirstOrDefault(entry => entry.EntryIndex == request.AssetEntryIndex);
        if (asset is null || !archive.AssetData.TryGetValue(request.AssetEntryIndex, out var originalBytes))
            throw new WorkspaceException($"No existe el recurso BCLIM #{request.AssetEntryIndex} dentro de {archive.Summary.Game}-{archive.Summary.Language}.");
        var replacementPath = ResolveExistingReplacement(request.ReplacementFile);
        var originalImage = BCLIMPortable.Read(originalBytes);
        var replacement = ReadReplacement(replacementPath, originalImage.Width, originalImage.Height);
        var originalGarc = ReadSourceGarc(catalog.GarcPath);
        var garcBytes = BuildGarcReplacement(
            catalog.GarcPath,
            request.FileNumber,
            request.AssetEntryIndex,
            archive.DarcData,
            archive.Summary.Compressed,
            replacement.Data);
        var backupFile = CreateWorkspaceBackupPath(workspace.RootPath, archive.Summary, catalog.GarcPath);

        try
        {
            Directory.CreateDirectory(Directory.GetParent(backupFile)!.FullName);
            File.WriteAllBytes(backupFile, originalGarc);
            WriteAtomically(catalog.GarcPath, garcBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorkspaceException($"No pude actualizar el GARC del workspace: {ex.Message}");
        }

        return new TitleScreenApplyResponse(
            workspace.Version.ToString(),
            archive.Summary.Game,
            archive.Summary.Language,
            request.FileNumber,
            request.AssetEntryIndex,
            asset.Name,
            replacement.Format,
            replacement.BclimFormat,
            archive.Summary.Compressed,
            catalog.GarcPath,
            backupFile,
            garcBytes.LongLength,
            "GARC del workspace actualizado; se guardó una copia de seguridad antes de escribir y se conservó LZSS cuando correspondía.");
    }

    private static byte[] BuildGarcReplacement(string garcPath, int fileNumber, int assetEntryIndex,
        byte[] darcData, bool compressed, byte[] replacementData)
    {
        DARC darc;
        try
        {
            darc = new DARC(darcData);
        }
        catch (Exception ex) when (ex is ArgumentException or EndOfStreamException or IOException or InvalidDataException)
        {
            throw new WorkspaceException($"No pude abrir el DARC para reemplazar la imagen: {ex.Message}");
        }
        if (darc.Header is null || darc.Entries is null || darc.FileNameTable is null || darc.Data is null
            || assetEntryIndex < 0 || assetEntryIndex >= darc.Entries.Length
            || darc.Entries[assetEntryIndex].IsFolder)
            throw new WorkspaceException("La entrada BCLIM seleccionada no es válida dentro del DARC.");
        if (!DARC.InsertFile(ref darc, assetEntryIndex, replacementData))
            throw new WorkspaceException("No pude reemplazar el contenido BCLIM dentro del DARC.");

        var replacementDarc = DARC.SetDARC(darc);
        var storedArchive = compressed ? CompressLzss(replacementDarc) : replacementDarc;
        try
        {
            var garc = new GARC.MemGARC(File.ReadAllBytes(garcPath));
            if (fileNumber < 0 || fileNumber >= garc.FileCount)
                throw new WorkspaceException($"El GARC no contiene la entrada {fileNumber}.");
            var files = garc.Files;
            files[fileNumber] = storedArchive;
            garc.Files = files;
            return garc.Save();
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or EndOfStreamException or IOException or InvalidDataException or FormatException or OverflowException)
        {
            throw new WorkspaceException($"No pude reconstruir el GARC de pantalla de título: {ex.Message}");
        }
    }

    private static byte[] ReadSourceGarc(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorkspaceException($"No pude leer el GARC original para crear la copia de seguridad: {ex.Message}");
        }
    }

    private static string CreateWorkspaceBackupPath(string workspaceRoot, TitleScreenArchiveSummary archive, string garcPath)
    {
        var directory = Path.Combine(workspaceRoot, ".pk3ds-backups");
        var baseName = $"{archive.Game}-{archive.Language}-{archive.FileNumber}-{Path.GetFileName(garcPath)}";
        var first = Path.Combine(directory, $"{baseName}.bak");
        if (!File.Exists(first))
            return first;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(directory, $"{baseName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{suffix}.bak");
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
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch
            {
                // The replacement has already been written; a stale temp file is harmless.
            }
        }
    }

    private static CatalogReadResult ReadCatalog(GameWorkspace workspace)
    {
        if (workspace.Version is not (GameVersion.XY or GameVersion.ORAS))
            throw new WorkspaceException("La pantalla de título está disponible para X/Y y OR/AS.");

        var spec = workspace.Version == GameVersion.XY
            ? new TitleGarcSpec(165, XyArchives)
            : new TitleGarcSpec(152, OrasArchives);
        var relativePath = $"a/{spec.FileNumber / 100 % 10}/{spec.FileNumber / 10 % 10}/{spec.FileNumber % 10}";
        var garcPath = Path.Combine(workspace.RomFsPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(garcPath))
            throw new WorkspaceException($"Falta el GARC de pantalla de título: {relativePath}.");

        GARC.MemGARC garc;
        try
        {
            garc = new GARC.MemGARC(File.ReadAllBytes(garcPath));
        }
        catch (Exception ex) when (ex is ArgumentException or EndOfStreamException or IOException or InvalidDataException)
        {
            throw new WorkspaceException($"No pude leer el GARC de pantalla de título: {ex.Message}");
        }

        var archives = spec.Archives.Select(entry => InspectArchive(garc, entry, relativePath)).ToArray();
        return new CatalogReadResult(garcPath, archives);
    }

    private static ArchiveReadResult InspectArchive(GARC.MemGARC garc, TitleArchiveSpec spec, string garcPath)
    {
        byte[] source;
        try
        {
            source = garc.GetFile(spec.FileNumber);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException)
        {
            return InvalidArchive(spec, garcPath, false, 0, $"No se encuentra dentro del GARC: {ex.Message}");
        }

        var compressed = source.Length > 0 && source[0] == 0x11;
        byte[] decoded;
        try
        {
            decoded = DecodeArchive(source, compressed);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
        {
            return InvalidArchive(spec, garcPath, true, source.Length, $"La compresión LZSS no es válida: {ex.Message}");
        }

        var darcPosition = FindDarcPosition(decoded);
        if (darcPosition < 0)
            return InvalidArchive(spec, garcPath, compressed, source.Length, "No contiene una cabecera DARC reconocible.");

        var darcData = decoded[darcPosition..];
        DARC darc;
        try
        {
            darc = new DARC(darcData);
        }
        catch (Exception ex) when (ex is ArgumentException or EndOfStreamException or IOException or InvalidDataException)
        {
            return InvalidArchive(spec, garcPath, compressed, source.Length, $"No pude leer el DARC: {ex.Message}");
        }

        if (darc.Header is null || darc.Entries is null || darc.FileNameTable is null || darc.Data is null
            || darc.Entries.Length != darc.FileNameTable.Length)
            return InvalidArchive(spec, garcPath, compressed, source.Length, "La tabla del DARC está incompleta.");

        var assets = new List<TitleScreenAssetSummary>();
        var assetData = new Dictionary<int, byte[]>();
        try
        {
            for (var index = 0; index < darc.Entries.Length; index++)
            {
                var entry = darc.Entries[index];
                var name = darc.FileNameTable[index].FileName;
                if (entry.IsFolder || !name.EndsWith(".bclim", StringComparison.OrdinalIgnoreCase))
                    continue;

                var bytes = ReadDarcEntry(darc, entry);
                assets.Add(new TitleScreenAssetSummary(index, name, bytes.Length));
                assetData[index] = bytes;
            }
        }
        catch (InvalidDataException ex)
        {
            return InvalidArchive(spec, garcPath, compressed, source.Length, ex.Message);
        }

        var actualDarcBytes = darc.Header.FileSize is > 0 and <= int.MaxValue && darc.Header.FileSize <= darcData.Length
            ? darcData[..(int)darc.Header.FileSize]
            : darcData;
        var summary = new TitleScreenArchiveSummary(
            spec.Game,
            spec.Language,
            spec.FileNumber,
            garcPath,
            compressed,
            source.Length,
            actualDarcBytes.Length,
            true,
            null,
            assets.ToArray());
        return new ArchiveReadResult(summary, actualDarcBytes, assetData);
    }

    private static ArchiveReadResult InvalidArchive(TitleArchiveSpec spec, string garcPath, bool compressed,
        int sourceBytes, string error) =>
        new(
            new TitleScreenArchiveSummary(spec.Game, spec.Language, spec.FileNumber, garcPath, compressed,
                sourceBytes, null, false, error, []),
            null,
            new Dictionary<int, byte[]>());

    private static byte[] DecodeArchive(byte[] source, bool compressed)
    {
        if (!compressed)
            return source;

        using var input = new MemoryStream(source, writable: false);
        using var output = new MemoryStream();
        LZSS.Decompress(input, source.Length, output);
        return output.ToArray();
    }

    private static byte[] ReadDarcEntry(DARC darc, DARC.FileTableEntry entry)
    {
        var relative = (long)entry.DataOffset - darc.Header.FileDataOffset;
        if (relative < 0 || entry.DataLength > int.MaxValue || relative + entry.DataLength > darc.Data.Length)
            throw new InvalidDataException("Un archivo BCLIM apunta fuera de los datos del DARC.");
        return darc.Data.AsSpan((int)relative, (int)entry.DataLength).ToArray();
    }

    private static ReplacementData ReadReplacement(string path, int expectedWidth, int expectedHeight)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".bclim", StringComparison.OrdinalIgnoreCase))
        {
            var data = File.ReadAllBytes(path);
            var image = BCLIMPortable.Read(data);
            EnsureDimensions(image.Width, image.Height, expectedWidth, expectedHeight);
            return new ReplacementData(data, "BCLIM", image.Format.ToString());
        }

        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("La imagen de reemplazo debe ser PNG o BCLIM.");
        var png = PortablePng.DecodeRgba(File.ReadAllBytes(path));
        EnsureDimensions(png.Width, png.Height, expectedWidth, expectedHeight);
        return new ReplacementData(
            BCLIMPortable.EncodeRgba(png.Rgba, png.Width, png.Height, XLIMEncoding.RGBA8),
            "PNG",
            XLIMEncoding.RGBA8.ToString());
    }

    private static void EnsureDimensions(int width, int height, int expectedWidth, int expectedHeight)
    {
        if (width != expectedWidth || height != expectedHeight)
            throw new WorkspaceException($"Las dimensiones no coinciden: se esperaba {expectedWidth}×{expectedHeight} y se recibió {width}×{height}.");
    }

    private static string ResolveExistingReplacement(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new WorkspaceException("Elegí un archivo PNG o BCLIM para reemplazar la imagen.");
        var fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath))
            throw new WorkspaceException("El archivo de reemplazo no existe.");
        return fullPath;
    }

    private static string ResolveReplacementOutput(GameWorkspace workspace, TitleScreenArchiveSummary archive,
        string assetName, string? requested)
    {
        var safeAsset = SanitizeFileName(Path.GetFileNameWithoutExtension(assetName));
        var defaultName = $"{archive.Game}-{archive.Language}-{archive.FileNumber}-{safeAsset}-replaced.darc";
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(workspace.RootPath, defaultName)
            : Path.GetFullPath(requested.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .darc.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, workspace.RomFsPath)
            || (workspace.ExeFsPath is not null && IsInside(output, workspace.ExeFsPath)))
            throw new WorkspaceException("El DARC de salida no puede guardarse dentro del RomFS ni del ExeFS de origen.");
        return output;
    }

    private static string ResolveGarcReplacementOutput(GameWorkspace workspace, TitleScreenArchiveSummary archive,
        string? requested)
    {
        var defaultName = $"{archive.Game}-{archive.Language}-titlescreen-replaced.garc";
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(workspace.RootPath, defaultName)
            : Path.GetFullPath(requested.Trim());
        if (Directory.Exists(output))
            throw new WorkspaceException("La salida indicada es una carpeta; elegí un archivo .garc.");
        if (File.Exists(output))
            throw new WorkspaceException("El archivo de salida ya existe. Elegí otro nombre para no sobrescribirlo.");
        if (IsInside(output, workspace.RomFsPath)
            || (workspace.ExeFsPath is not null && IsInside(output, workspace.ExeFsPath)))
            throw new WorkspaceException("El GARC de salida no puede guardarse dentro del RomFS ni del ExeFS de origen.");
        return output;
    }

    private static byte[] CompressLzss(byte[] data)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"pk3ds-title-compress-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var input = Path.Combine(staging, "input.bin");
        var output = Path.Combine(staging, "output.bin");
        try
        {
            File.WriteAllBytes(input, data);
            LZSS.Compress(input, output);
            return File.ReadAllBytes(output);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static int FindDarcPosition(byte[] data)
    {
        for (var index = 0; index <= data.Length - 4; index++)
        {
            if (data[index] == (byte)'d' && data[index + 1] == (byte)'a'
                && data[index + 2] == (byte)'r' && data[index + 3] == (byte)'c')
                return index;
        }
        return -1;
    }

    private static TitleArchiveSpec[] CreateArchives(int[] fileNumbers, string[] languages, string[] games)
    {
        var perGame = languages.Length;
        return fileNumbers.Select((fileNumber, index) =>
            new TitleArchiveSpec(fileNumber, games[index / perGame], languages[index % perGame])).ToArray();
    }

    private static string ResolveOutputDirectory(GameWorkspace workspace, string? requested)
    {
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(workspace.RootPath, "title-screen-export")
            : Path.GetFullPath(requested.Trim());
        if (File.Exists(output))
            throw new WorkspaceException("La salida de pantalla de título ya existe como archivo.");
        if (Directory.Exists(output))
            throw new WorkspaceException("La carpeta de salida ya existe. Elegí una carpeta nueva.");
        if (IsInside(output, workspace.RomFsPath)
            || (workspace.ExeFsPath is not null && IsInside(output, workspace.ExeFsPath)))
            throw new WorkspaceException("La exportación no puede guardarse dentro del RomFS ni del ExeFS de origen.");
        return output;
    }

    private static string SanitizeFileName(string name)
    {
        var fileName = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "asset.bclim";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');
        return fileName;
    }

    private static bool IsInside(string candidate, string source)
    {
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullSource = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullSource, StringComparison.OrdinalIgnoreCase)
            || fullCandidate.StartsWith(fullSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullCandidate.StartsWith(fullSource + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TitleArchiveSpec(int FileNumber, string Game, string Language);
    private sealed record TitleGarcSpec(int FileNumber, TitleArchiveSpec[] Archives);
    private sealed record CatalogReadResult(string GarcPath, ArchiveReadResult[] Archives);
    private sealed record ArchiveReadResult(
        TitleScreenArchiveSummary Summary,
        byte[]? DarcData,
        Dictionary<int, byte[]> AssetData)
    {
        public IEnumerable<TitleScreenAssetSummary> Assets => Summary.Assets;
        public bool Compressed => Summary.Compressed;
    }

    private sealed record ReplacementData(byte[] Data, string Format, string BclimFormat);
}
