using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>
/// Creates and restores one immutable set of original workspace files.
/// <para>
/// The manifest is deliberately kept in the workspace. Its source paths are relative to the
/// workspace root, so moving the extracted project does not invalidate a baseline. A process-wide
/// lock protects the manifest and its files from concurrent list, create, and restore operations.
/// </para>
/// </summary>
public static class WorkspaceBackupTools
{
    private const int ManifestVersion = 1;
    private const string RestoreDirectoryName = "workspace-restore";
    private const string ManifestFileName = "manifest.json";
    private static readonly object ManifestLock = new();
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static WorkspaceBackupsResponse List(WorkspaceBackupsRequest request)
    {
        lock (ManifestLock)
        {
            return RunStorage("No pude listar las copias del workspace.", () =>
            {
                if (request is null)
                    throw new WorkspaceException("No se indicó el workspace.");

                var workspace = GameWorkspace.Open(request.WorkspacePath);
                var manifest = ReadManifest(workspace, allowMissing: true);
                if (manifest is null)
                {
                    return new WorkspaceBackupsResponse(
                        workspace.Version.ToString(),
                        false,
                        [],
                        "Todavía no existe una línea base de archivos para este workspace.");
                }

                var entries = ValidateManifest(workspace, manifest);
                var backups = entries
                    .OrderBy(entry => entry.Manifest.Category, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Manifest.SourcePath, StringComparer.Ordinal)
                    .Select(entry => new WorkspaceBackupSummary(
                        entry.Manifest.Id,
                        entry.Manifest.Category,
                        entry.Manifest.SourcePath,
                        entry.Manifest.Bytes,
                        File.Exists(entry.SourcePath)))
                    .ToArray();

                return new WorkspaceBackupsResponse(
                    workspace.Version.ToString(),
                    true,
                    backups,
                    backups.Length == 0
                        ? "La línea base no contiene archivos respaldados."
                        : $"Hay {backups.Length} archivo(s) originales disponibles para restaurar.");
            });
        }
    }

    public static CreateWorkspaceBackupResponse CreateBaseline(CreateWorkspaceBackupRequest request)
    {
        lock (ManifestLock)
        {
            return RunStorage("No pude crear la línea base del workspace.", () =>
            {
                if (request is null)
                    throw new WorkspaceException("No se indicó el workspace.");

                var workspace = GameWorkspace.Open(request.WorkspacePath);
                var manifest = ReadManifest(workspace, allowMissing: true);
                var createdManifest = manifest is null;
                manifest ??= NewManifest(workspace);

                var trustedEntries = ValidateManifest(workspace, manifest);
                var entries = manifest.Files!;
                var knownIds = trustedEntries.ToDictionary(entry => entry.Manifest.Id, StringComparer.Ordinal);
                var knownSources = new HashSet<string>(
                    trustedEntries.Select(entry => entry.Manifest.SourcePath),
                    GetPathComparer());
                var language = EditorSession.NormalizeLanguage(request.Language);
                var candidates = DiscoverSources(workspace, language);
                var created = 0;
                var skipped = 0;

                foreach (var candidate in candidates)
                {
                    var id = ComputeId(candidate.Category, candidate.RelativePath);
                    if (knownIds.ContainsKey(id))
                    {
                        skipped++;
                        continue;
                    }
                    if (!knownSources.Add(candidate.RelativePath))
                    {
                        skipped++;
                        continue;
                    }

                    if (!File.Exists(candidate.SourcePath))
                    {
                        skipped++;
                        continue;
                    }

                    var backupRelativePath = GetBackupRelativePath(candidate.Category, id);
                    var backupPath = ResolveBackupPath(workspace, backupRelativePath, candidate.Category, id);
                    if (File.Exists(backupPath) || Directory.Exists(backupPath))
                    {
                        throw new WorkspaceException(
                            $"La copia base para «{candidate.RelativePath}» ya existe, pero falta su entrada de manifest.");
                    }

                    var backupDirectory = Path.GetDirectoryName(backupPath)!;
                    Directory.CreateDirectory(backupDirectory);
                    var temporary = Path.Combine(backupDirectory, $".{id}.{Guid.NewGuid():N}.tmp");
                    try
                    {
                        try
                        {
                            // The destination is intentionally absent and the move has no overwrite
                            // option. An existing baseline can never be replaced by a later scan.
                            File.Copy(candidate.SourcePath, temporary, overwrite: false);
                        }
                        catch (FileNotFoundException)
                        {
                            skipped++;
                            continue;
                        }
                        catch (DirectoryNotFoundException)
                        {
                            skipped++;
                            continue;
                        }

                        var bytes = new FileInfo(temporary).Length;
                        var hash = ComputeFileHash(temporary);
                        File.Move(temporary, backupPath);

                        var entry = new ManifestEntry
                        {
                            Id = id,
                            Category = candidate.Category,
                            SourcePath = candidate.RelativePath,
                            BackupPath = backupRelativePath,
                            Bytes = bytes,
                            Sha256 = hash,
                        };
                        entries.Add(entry);
                        knownIds.Add(id, new ResolvedEntry(entry, candidate.SourcePath, backupPath));
                        created++;
                    }
                    finally
                    {
                        if (File.Exists(temporary))
                            File.Delete(temporary);
                    }
                }

                if (createdManifest || created > 0)
                    WriteManifestAtomically(workspace, manifest);

                var note = created == 0
                    ? skipped == 0
                        ? "No se encontraron archivos originales disponibles para respaldar."
                        : $"No se agregaron archivos; {skipped} ya estaban respaldados o no estaban disponibles."
                    : $"Se agregaron {created} archivo(s) a la línea base; {skipped} se omitieron porque ya estaban respaldados o no estaban disponibles.";
                return new CreateWorkspaceBackupResponse(
                    workspace.Version.ToString(),
                    created,
                    skipped,
                    note);
            });
        }
    }

    public static RestoreWorkspaceBackupResponse Restore(RestoreWorkspaceBackupRequest request)
    {
        lock (ManifestLock)
        {
            return RunStorage("No pude restaurar las copias del workspace.", () =>
            {
                if (request is null)
                    throw new WorkspaceException("No se indicó el workspace.");

                var ids = NormalizeRequestedIds(request.BackupIds);
                var workspace = GameWorkspace.Open(request.WorkspacePath);
                var manifest = ReadManifest(workspace, allowMissing: false)
                    ?? throw new WorkspaceException("Todavía no existe una línea base para restaurar.");
                var entries = ValidateManifest(workspace, manifest);
                var byId = entries.ToDictionary(entry => entry.Manifest.Id, StringComparer.Ordinal);
                var selected = new List<ResolvedEntry>(ids.Length);
                foreach (var id in ids)
                {
                    if (!byId.TryGetValue(id, out var entry))
                        throw new WorkspaceException($"La copia seleccionada «{id}» no existe en la línea base confiable.");
                    selected.Add(entry);
                }

                // Read and validate every baseline before creating safety copies or touching a
                // source. If one file is damaged, the workspace remains unchanged.
                foreach (var entry in selected)
                    ValidateBackupContent(entry);

                foreach (var entry in selected)
                {
                    if (Directory.Exists(entry.SourcePath))
                        throw new WorkspaceException($"El destino «{entry.Manifest.SourcePath}» es una carpeta y no se puede restaurar.");
                }

                var safetyFolder = Path.Combine(
                    GetRestoreRoot(workspace),
                    "safety",
                    $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
                EnsureInside(workspace.RootPath, safetyFolder, "La carpeta de seguridad quedó fuera del workspace.");
                EnsureNoReparsePoints(workspace.RootPath, safetyFolder);
                Directory.CreateDirectory(safetyFolder);

                // All safety copies are made before the first target write. They are deliberately
                // retained if a later copy or atomic replacement fails.
                foreach (var entry in selected)
                {
                    if (!File.Exists(entry.SourcePath))
                        continue;

                    var safetyPath = ResolveSafetyPath(workspace, safetyFolder, entry.Manifest.SourcePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(safetyPath)!);
                    File.Copy(entry.SourcePath, safetyPath, overwrite: false);
                }

                var restored = 0;
                foreach (var entry in selected)
                {
                    var parent = Path.GetDirectoryName(entry.SourcePath)
                        ?? throw new WorkspaceException($"No pude resolver la carpeta de «{entry.Manifest.SourcePath}».");
                    Directory.CreateDirectory(parent);
                    EnsureNoReparsePoints(workspace.RootPath, entry.SourcePath);
                    var temporary = Path.Combine(parent, $".{Path.GetFileName(entry.SourcePath)}.{Guid.NewGuid():N}.tmp");
                    try
                    {
                        File.Copy(entry.BackupPath, temporary, overwrite: false);
                        // The temp file shares the target directory, making this replacement atomic
                        // on the filesystems supported by the host.
                        File.Move(temporary, entry.SourcePath, overwrite: true);
                        restored++;
                    }
                    finally
                    {
                        if (File.Exists(temporary))
                            File.Delete(temporary);
                    }
                }

                return new RestoreWorkspaceBackupResponse(
                    workspace.Version.ToString(),
                    restored,
                    safetyFolder,
                    $"Se restauraron {restored} archivo(s) originales; las copias del estado anterior quedaron en la carpeta de seguridad.");
            });
        }
    }

    private static T RunStorage<T>(string message, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            throw new WorkspaceException($"{message} {ex.Message}");
        }
    }

    private static bool IsStorageError(Exception ex) => ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or InvalidDataException
        or JsonException
        or NotSupportedException
        or SecurityException
        or FormatException
        or OverflowException;

    private static BackupManifest? ReadManifest(GameWorkspace workspace, bool allowMissing)
    {
        var manifestPath = GetManifestPath(workspace);
        EnsureNoReparsePoints(workspace.RootPath, manifestPath);
        if (!File.Exists(manifestPath))
        {
            if (Directory.Exists(manifestPath))
                throw new WorkspaceException("El archivo manifest.json de los backups es una carpeta.");
            if (allowMissing)
                return null;
            throw new WorkspaceException("Todavía no existe una línea base para restaurar.");
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(json, ManifestJsonOptions)
            ?? throw new WorkspaceException("El manifest de los backups está vacío.");
        return manifest;
    }

    private static BackupManifest NewManifest(GameWorkspace workspace) => new()
    {
        Version = ManifestVersion,
        Game = workspace.Version.ToString(),
        Files = [],
    };

    private static void WriteManifestAtomically(GameWorkspace workspace, BackupManifest manifest)
    {
        var restoreRoot = GetRestoreRoot(workspace);
        EnsureNoReparsePoints(workspace.RootPath, restoreRoot);
        Directory.CreateDirectory(restoreRoot);
        var manifestPath = GetManifestPath(workspace);
        var temporary = Path.Combine(restoreRoot, $".{ManifestFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
            File.WriteAllText(temporary, json, Encoding.UTF8);
            if (File.Exists(manifestPath))
                File.Move(temporary, manifestPath, overwrite: true);
            else
                File.Move(temporary, manifestPath);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static ResolvedEntry[] ValidateManifest(GameWorkspace workspace, BackupManifest manifest)
    {
        if (manifest.Version != ManifestVersion)
            throw new WorkspaceException("El manifest de los backups tiene una versión no compatible.");
        if (!string.Equals(manifest.Game, workspace.Version.ToString(), StringComparison.Ordinal))
            throw new WorkspaceException("El manifest de los backups pertenece a otro juego.");
        if (manifest.Files is null)
            throw new WorkspaceException("El manifest de los backups no contiene una lista de archivos.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var sources = new HashSet<string>(GetPathComparer());
        var backupPaths = new HashSet<string>(GetPathComparer());
        var result = new List<ResolvedEntry>(manifest.Files.Count);
        foreach (var entry in manifest.Files)
        {
            if (entry is null)
                throw new WorkspaceException("El manifest de los backups contiene una entrada vacía.");
            if (!IsCategory(entry.Category))
                throw new WorkspaceException($"El manifest de los backups contiene una categoría inválida: «{entry.Category}».");
            if (!IsBackupId(entry.Id) || !ids.Add(entry.Id))
                throw new WorkspaceException("El manifest de los backups contiene IDs vacíos, inválidos o repetidos.");

            var source = NormalizeRelativePath(entry.SourcePath, "El manifest contiene una ruta de origen inválida.");
            if (!string.Equals(source, entry.SourcePath, StringComparison.Ordinal)
                || !sources.Add(source)
                || !string.Equals(entry.Id, ComputeId(entry.Category, source), StringComparison.Ordinal))
                throw new WorkspaceException("El manifest de los backups contiene una ruta de origen o un ID alterado.");
            if (entry.Bytes < 0)
                throw new WorkspaceException("El manifest de los backups contiene un tamaño inválido.");
            if (!string.IsNullOrWhiteSpace(entry.Sha256) && !IsSha256(entry.Sha256))
                throw new WorkspaceException("El manifest de los backups contiene un hash inválido.");

            var sourcePath = ResolveSourcePath(workspace, entry.Category, source);
            var backup = NormalizeRelativePath(entry.BackupPath, "El manifest contiene una ruta de backup inválida.");
            if (!string.Equals(backup, entry.BackupPath, StringComparison.Ordinal)
                || !backupPaths.Add(backup)
                || !string.Equals(backup, GetBackupRelativePath(entry.Category, entry.Id), StringComparison.Ordinal))
                throw new WorkspaceException("El manifest de los backups contiene una ruta de almacenamiento alterada.");

            var backupPath = ResolveBackupPath(workspace, backup, entry.Category, entry.Id);
            if (!File.Exists(backupPath) || Directory.Exists(backupPath))
                throw new WorkspaceException($"Falta el archivo respaldado para «{source}».");
            if (new FileInfo(backupPath).Length != entry.Bytes)
                throw new WorkspaceException($"El tamaño del backup de «{source}» no coincide con el manifest.");

            result.Add(new ResolvedEntry(entry, sourcePath, backupPath));
        }

        return result.ToArray();
    }

    private static IReadOnlyList<Candidate> DiscoverSources(GameWorkspace workspace, int language)
    {
        var candidates = new List<Candidate>();
        var relativePaths = new HashSet<string>(GetPathComparer());

        void Add(string category, string sourcePath)
        {
            var fullPath = Path.GetFullPath(sourcePath);
            var relative = NormalizeRelativePath(
                Path.GetRelativePath(workspace.RootPath, fullPath),
                "El workspace contiene una ruta de backup fuera de su raíz.");
            if (!relativePaths.Add(relative))
                return;
            EnsureInside(workspace.RootPath, fullPath, "El archivo a respaldar quedó fuera del workspace.");
            EnsureNoReparsePoints(workspace.RootPath, fullPath);
            candidates.Add(new Candidate(category, relative, fullPath));
        }

        foreach (var reference in GetConfiguredGarcs(workspace, language))
        {
            var effective = reference.LanguageVariant
                ? reference.GetRelativeGARC(language, reference.Name)
                : reference;
            Add("GARC", Path.Combine(workspace.RomFsPath, effective.Reference));
        }

        if (workspace.ExeFsPath is not null && Directory.Exists(workspace.ExeFsPath))
        {
            foreach (var file in Directory.EnumerateFiles(workspace.ExeFsPath, "*", SearchOption.TopDirectoryOnly))
                Add("ExeFS", file);
        }

        if (Directory.Exists(workspace.RomFsPath))
        {
            foreach (var file in Directory.EnumerateFiles(workspace.RomFsPath, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (name.Contains("Dll", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetExtension(name), ".crs", StringComparison.OrdinalIgnoreCase))
                    Add("CRO", file);
            }
        }

        var crrRoot = Path.Combine(workspace.RomFsPath, ".crr");
        if (Directory.Exists(crrRoot))
        {
            foreach (var file in Directory.EnumerateFiles(crrRoot, "*", SearchOption.TopDirectoryOnly))
                Add("CRR", file);
        }

        return candidates
            .OrderBy(candidate => candidate.Category, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static GARCReference[] GetConfiguredGarcs(GameWorkspace workspace, int language) => workspace.Version switch
    {
        GameVersion.XY => GARCReference.GARCReference_XY,
        GameVersion.ORAS or GameVersion.ORASDEMO => GARCReference.GARCReference_AO,
        GameVersion.SMDEMO => GARCReference.GARCReference_SMDEMO,
        GameVersion.SM => SelectGenericTable(workspace, language,
            GARCReference.GARCReference_SN,
            GARCReference.GARCReference_MN),
        GameVersion.USUM => SelectGenericTable(workspace, language,
            GARCReference.GARCReference_US,
            GARCReference.GARCReference_UM),
        _ => throw new WorkspaceException("El juego detectado no tiene una tabla de archivos compatible.")
    };

    private static GARCReference[] SelectGenericTable(
        GameWorkspace workspace,
        int language,
        GARCReference[] primary,
        GARCReference[] alternate)
    {
        var primaryPath = GetGarcPath(workspace, primary.First(reference => reference.Name == "encdata"), language);
        if (IsNonEmptyFile(primaryPath))
            return primary;

        var alternatePath = GetGarcPath(workspace, alternate.First(reference => reference.Name == "encdata"), language);
        if (IsNonEmptyFile(alternatePath))
            return alternate;

        // Keep the same default as GameConfig when both candidates are absent or empty.
        return primary;
    }

    private static string GetGarcPath(GameWorkspace workspace, GARCReference reference, int language)
    {
        var effective = reference.LanguageVariant
            ? reference.GetRelativeGARC(language, reference.Name)
            : reference;
        return Path.Combine(workspace.RomFsPath, effective.Reference);
    }

    private static bool IsNonEmptyFile(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    private static string[] NormalizeRequestedIds(string[]? requested)
    {
        if (requested is null || requested.Length == 0)
            throw new WorkspaceException("Elegí al menos un archivo original para restaurar.");

        var ids = requested.Select(id => id?.Trim() ?? string.Empty).ToArray();
        if (ids.Any(id => !IsBackupId(id)))
            throw new WorkspaceException("La restauración recibió un ID de backup inválido.");
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new WorkspaceException("La restauración no admite IDs repetidos.");
        return ids;
    }

    private static void ValidateBackupContent(ResolvedEntry entry)
    {
        if (!File.Exists(entry.BackupPath))
            throw new WorkspaceException($"Falta el archivo respaldado para «{entry.Manifest.SourcePath}».");
        var info = new FileInfo(entry.BackupPath);
        if (info.Length != entry.Manifest.Bytes)
            throw new WorkspaceException($"El tamaño del backup de «{entry.Manifest.SourcePath}» no coincide con el manifest.");
        if (!string.IsNullOrWhiteSpace(entry.Manifest.Sha256)
            && !string.Equals(ComputeFileHash(entry.BackupPath), entry.Manifest.Sha256, StringComparison.Ordinal))
            throw new WorkspaceException($"El contenido del backup de «{entry.Manifest.SourcePath}» no coincide con el hash del manifest.");
    }

    private static string ResolveSourcePath(GameWorkspace workspace, string category, string relativePath)
    {
        var categoryRoot = GetCategoryRoot(workspace, category, relativePath);
        var sourcePath = ResolveInside(workspace.RootPath, relativePath,
            "El manifest contiene una ruta de origen fuera del workspace.");
        EnsureInside(categoryRoot, sourcePath,
            $"El manifest contiene un destino de {category} fuera de su carpeta.");

        if (category == "GARC")
            EnsureInside(Path.Combine(categoryRoot, "a"), sourcePath,
                "El manifest contiene un GARC fuera de la carpeta de archivos configurados.");

        if (category is "CRO" or "CRR")
        {
            var expectedParent = Path.GetFullPath(categoryRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var actualParent = Path.GetDirectoryName(sourcePath)?.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(expectedParent, actualParent, GetPathComparison()))
                throw new WorkspaceException($"El manifest contiene un destino de {category} que no es de nivel superior.");
        }

        if (category == "ExeFS")
        {
            var expectedParent = Path.GetFullPath(categoryRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var actualParent = Path.GetDirectoryName(sourcePath)?.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(expectedParent, actualParent, GetPathComparison()))
                throw new WorkspaceException("El manifest contiene un archivo ExeFS que no está en el nivel superior.");
        }

        if (IsPathInside(sourcePath, GetRestoreRoot(workspace)))
            throw new WorkspaceException("El manifest no puede restaurar archivos dentro de la carpeta de backups.");
        EnsureNoReparsePoints(workspace.RootPath, sourcePath);
        return sourcePath;
    }

    private static string GetCategoryRoot(GameWorkspace workspace, string category, string relativePath)
    {
        return category switch
        {
            "GARC" or "CRO" => workspace.RomFsPath,
            "CRR" => Path.Combine(workspace.RomFsPath, ".crr"),
            "ExeFS" => GetExeFsRoot(workspace, relativePath),
            _ => throw new WorkspaceException($"La categoría de backup «{category}» no es compatible."),
        };
    }

    private static string GetExeFsRoot(GameWorkspace workspace, string relativePath)
    {
        if (workspace.ExeFsPath is not null)
            return workspace.ExeFsPath;

        var firstSegment = relativePath.Split('/')[0];
        if (string.Equals(firstSegment, "ExeFS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(firstSegment, "exefs", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(workspace.RootPath, firstSegment);
        if (string.Equals(firstSegment, "RomFS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(firstSegment, "romfs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(firstSegment, "a", StringComparison.OrdinalIgnoreCase)
            || string.Equals(firstSegment, ".pk3ds-backups", StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("El manifest contiene un archivo ExeFS fuera de su carpeta.");
        return workspace.RootPath;
    }

    private static string ResolveBackupPath(GameWorkspace workspace, string relativePath, string category, string id)
    {
        var restoreRoot = GetRestoreRoot(workspace);
        var backupPath = ResolveInside(restoreRoot, relativePath,
            "El manifest contiene una ruta de backup fuera de su carpeta.");
        var categoryRoot = Path.Combine(restoreRoot, "files", category);
        EnsureInside(categoryRoot, backupPath,
            "El manifest contiene un archivo de backup fuera de su categoría.");
        if (!string.Equals(Path.GetFileName(backupPath), $"{id}.bin", StringComparison.Ordinal))
            throw new WorkspaceException("El manifest contiene un nombre de backup que no coincide con su ID.");
        EnsureNoReparsePoints(workspace.RootPath, backupPath);
        return backupPath;
    }

    private static string ResolveSafetyPath(GameWorkspace workspace, string safetyFolder, string relativePath)
    {
        var safetyPath = ResolveInside(safetyFolder, relativePath,
            "La copia de seguridad quedó fuera de su carpeta.");
        EnsureInside(GetRestoreRoot(workspace), safetyPath,
            "La carpeta de seguridad quedó fuera de los backups del workspace.");
        EnsureNoReparsePoints(workspace.RootPath, safetyPath);
        return safetyPath;
    }

    private static string GetBackupRelativePath(string category, string id) => $"files/{category}/{id}.bin";

    private static string GetRestoreRoot(GameWorkspace workspace) => Path.Combine(
        workspace.RootPath,
        ".pk3ds-backups",
        RestoreDirectoryName);

    private static string GetManifestPath(GameWorkspace workspace) => Path.Combine(
        GetRestoreRoot(workspace),
        ManifestFileName);

    private static string ResolveInside(string root, string relativePath, string error)
    {
        var normalized = NormalizeRelativePath(relativePath, error);
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInside(fullRoot, fullPath, error);
        return fullPath;
    }

    private static void EnsureInside(string root, string candidate, string error)
    {
        if (!IsPathInside(candidate, root))
            throw new WorkspaceException(error);
    }

    private static bool IsPathInside(string candidate, string root)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = GetPathComparison();
        if (string.Equals(fullRoot, fullCandidate, comparison))
            return true;
        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison)
            || fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, comparison);
    }

    private static void EnsureNoReparsePoints(string workspaceRoot, string candidate)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var fullCandidate = Path.GetFullPath(candidate);
        EnsureInside(fullRoot, fullCandidate, "La ruta contiene un destino fuera del workspace.");
        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        if (relative == ".")
            return;

        var current = fullRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                break;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new WorkspaceException("Las rutas de backup no pueden atravesar enlaces simbólicos.");
        }
    }

    private static string NormalizeRelativePath(string? value, string error)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new WorkspaceException(error);
        var normalized = value.Trim().Replace('\\', '/');
        if (normalized.Length == 0
            || normalized.StartsWith('/')
            || normalized.StartsWith("//", StringComparison.Ordinal)
            || (normalized.Length > 1 && normalized[1] == ':'))
            throw new WorkspaceException(error);

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new WorkspaceException(error);
        return string.Join('/', segments);
    }

    private static string ComputeId(string category, string relativePath)
    {
        var input = Encoding.UTF8.GetBytes($"{category}\n{relativePath}");
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsBackupId(string? value) => value is not null
        && value.Length == 64
        && value.All(Uri.IsHexDigit)
        && value == value.ToLowerInvariant();

    private static bool IsSha256(string? value) => value is not null
        && value.Length == 64
        && value.All(Uri.IsHexDigit);

    private static bool IsCategory(string? category) => category is "GARC" or "ExeFS" or "CRO" or "CRR";

    private static StringComparison GetPathComparison() => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer GetPathComparer() => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record Candidate(string Category, string RelativePath, string SourcePath);

    private sealed record ResolvedEntry(ManifestEntry Manifest, string SourcePath, string BackupPath);

    private sealed class BackupManifest
    {
        public int Version { get; set; }
        public string? Game { get; set; }
        public List<ManifestEntry>? Files { get; set; }
    }

    private sealed class ManifestEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
        public long Bytes { get; set; }
        public string? Sha256 { get; set; }
    }
}
