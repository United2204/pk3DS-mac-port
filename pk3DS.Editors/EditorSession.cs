using System.IO.Compression;
using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.Editors;

/// <summary>
/// Shared scaffolding for every editor.
/// <para>
/// Editing never touches the source dump. An export copies the GARCs it needs into a scratch
/// RomFS, mutates that copy, and packs only the changed files into a LayeredFS ZIP. Each editor
/// supplies just the mutation; this class owns the workspace, Title ID, scratch directory and
/// archive so the individual editors cannot drift apart on any of it.
/// </para>
/// </summary>
public static class EditorSession
{
    /// <summary>
    /// GARCs that <see cref="GameConfig.Initialize"/> always reads, regardless of the editor.
    /// They must exist in the scratch RomFS even when the editor does not modify them.
    /// </summary>
    internal static readonly string[] RequiredGarcs = ["personal", "levelup", "gametext", "move", "evolution", "eggmove"];

    /// <summary>
    /// Gen VII picks between the Sun and Moon GARC tables by checking whether <c>encdata</c> is
    /// empty, which means <see cref="GameConfig.Initialize"/> stats that file before reading
    /// anything. A scratch RomFS without it fails with <see cref="FileNotFoundException"/>, so it
    /// has to be copied even when the editor never touches encounters.
    /// </summary>
    private static readonly string[] Gen7RequiredGarcs = ["encdata"];

    /// <summary>Opens the workspace and builds a config that reads straight from the source dump.</summary>
    public static (GameWorkspace Workspace, GameConfig Config) OpenReadOnly(string workspacePath, int? language)
    {
        var workspace = GameWorkspace.Open(workspacePath);
        return (workspace, OpenReadOnly(workspace, language));
    }

    public static GameConfig OpenReadOnly(GameWorkspace workspace, int? language)
    {
        var config = new GameConfig(workspace.Version);
        config.Initialize(workspace.RomFsPath, workspace.RootPath, NormalizeLanguage(language));
        return config;
    }

    internal static int NormalizeLanguage(int? language)
    {
        var value = language ?? 1;
        if (value is < 0 or > 11)
            throw new WorkspaceException("El idioma debe estar entre 0 y 11.");
        return value;
    }

    /// <summary>
    /// Resolves the Title ID to write the LayeredFS tree under, preferring an explicit request
    /// over the one detected from <c>exheader.bin</c>.
    /// </summary>
    internal static string ResolveTitleId(GameWorkspace workspace, string? requested)
    {
        var titleId = requested ?? workspace.TitleId;
        if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("No pude detectar un Title ID válido. Seleccioná la carpeta completa que contiene exheader.bin.");
        return titleId;
    }

    /// <summary>
    /// Runs <paramref name="apply"/> against a scratch copy of the RomFS and packs the result.
    /// </summary>
    /// <param name="extraGarcs">GARCs this editor needs on top of <see cref="RequiredGarcs"/>.</param>
    /// <param name="apply">Mutates the scratch config and returns the relative paths it changed.</param>
    public static ExportResult Export(
        string workspacePath,
        string? outputDirectory,
        string? requestedTitleId,
        int? language,
        string label,
        IEnumerable<string> extraGarcs,
        Func<GameConfig, IEnumerable<string>> apply)
    {
        var workspace = GameWorkspace.Open(workspacePath);
        var titleId = ResolveTitleId(workspace, requestedTitleId);
        var languageId = NormalizeLanguage(language);

        return InScratchRomFs(label, scratchRomFs =>
        {
            // The probe reads the untouched source dump; it exists to resolve GARC paths for the
            // copy below, which is what makes the scratch config initialisable in the first place.
            var probe = new GameConfig(workspace.Version);
            probe.Initialize(workspace.RomFsPath, Directory.GetParent(scratchRomFs)!.FullName, languageId);

            var needed = RequiredGarcs
                .Concat(probe.Generation == 7 ? Gen7RequiredGarcs : [])
                .Concat(extraGarcs)
                .Distinct(StringComparer.Ordinal);
            foreach (var name in needed)
                CopyRelativeFile(workspace.RomFsPath, scratchRomFs, probe.GetGARCFileName(name));

            var config = new GameConfig(workspace.Version);
            config.Initialize(scratchRomFs, Directory.GetParent(scratchRomFs)!.FullName, languageId);

            var changed = apply(config);
            return CreateLayeredFsArchive(outputDirectory, workspace.RomFsPath, scratchRomFs, titleId, "romfs", changed, label);
        });
    }

    /// <summary>
    /// Variant for editors that patch loose RomFS files instead of GARCs (currently
    /// <c>DllField.cro</c>). The config still reads from the source dump, because the loose file
    /// is copied on its own and no GARC is involved.
    /// </summary>
    public static ExportResult ExportLooseFiles(
        string workspacePath,
        string? outputDirectory,
        string? requestedTitleId,
        int? language,
        string label,
        IEnumerable<string> files,
        Action<GameWorkspace, GameConfig, string> apply)
    {
        var workspace = GameWorkspace.Open(workspacePath);
        var titleId = ResolveTitleId(workspace, requestedTitleId);
        var config = OpenReadOnly(workspace, language);
        var copied = files.Distinct(StringComparer.Ordinal).ToArray();

        return InScratchRomFs(label, scratchRomFs =>
        {
            foreach (var file in copied)
                CopyRelativeFile(workspace.RomFsPath, scratchRomFs, file);
            apply(workspace, config, scratchRomFs);
            var changed = copied.ToList();
            RebuildCrrIfPresent(workspace.RomFsPath, scratchRomFs, copied, changed);
            return CreateLayeredFsArchive(outputDirectory, workspace.RomFsPath, scratchRomFs, titleId, "romfs", changed, label);
        });
    }

    /// <summary>
    /// Runs an ExeFS editor against a scratch copy of <c>code.bin</c> and emits an ExeFS
    /// LayeredFS patch. The loaded ROM config is supplied so the editor can validate IDs against
    /// the game's own text tables.
    /// </summary>
    public static ExportResult ExportExeFs(
        string workspacePath,
        string? outputDirectory,
        string? requestedTitleId,
        int? language,
        string label,
        Func<GameWorkspace, GameConfig, byte[], byte[]> apply)
    {
        var workspace = GameWorkspace.Open(workspacePath);
        var titleId = ResolveTitleId(workspace, requestedTitleId);
        var languageId = NormalizeLanguage(language);
        var codePath = FindCodeBin(workspace);
        var config = OpenReadOnly(workspace, languageId);
        var scratch = Path.Combine(Path.GetTempPath(), $"pk3ds-{label}-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(scratch);
            var edited = apply(workspace, config, File.ReadAllBytes(codePath));
            if (edited is null || edited.Length == 0)
                throw new WorkspaceException("El editor no produjo un code.bin válido.");
            var codeName = Path.GetFileName(codePath);
            File.WriteAllBytes(Path.Combine(scratch, codeName), edited);
            return CreateLayeredFsArchive(outputDirectory, workspace.ExeFsPath!, scratch, titleId,
                "exefs", [codeName], label);
        }
        finally
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
    }

    private static ExportResult InScratchRomFs(string label, Func<string, ExportResult> body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pk3ds-{label}-{Guid.NewGuid():N}");
        var scratchRomFs = Path.Combine(root, "romfs");
        try
        {
            Directory.CreateDirectory(scratchRomFs);
            return body(scratchRomFs);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string FindCodeBin(GameWorkspace workspace)
    {
        if (workspace.ExeFsPath is null)
            throw new WorkspaceException("Falta ExeFS. Extraé el code.bin descomprimido para editar este módulo.");
        return Directory.EnumerateFiles(workspace.ExeFsPath)
            .FirstOrDefault(file => Path.GetFileName(file).Contains("code", StringComparison.OrdinalIgnoreCase))
            ?? throw new WorkspaceException("No encuentro code.bin dentro de ExeFS.");
    }

    private static ExportResult CreateLayeredFsArchive(
        string? outputDirectory, string sourceRomFs, string scratchRomFs,
        string titleId, string layer, IEnumerable<string> changedFiles, string labelPrefix)
    {
        var outputBase = ResolveOutputBase(outputDirectory, sourceRomFs);
        var label = $"pk3ds-mac-{labelPrefix}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var outputRoot = Path.Combine(outputBase, label);
        var layeredRomFs = Path.Combine(outputRoot, "luma", "titles", titleId.ToUpperInvariant(), layer);
        var changed = changedFiles.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var relativePath in changed)
            CopyRelativeFile(scratchRomFs, layeredRomFs, relativePath);

        var zipPath = Path.Combine(outputBase, $"{label}-LayeredFS.zip");
        ZipFile.CreateFromDirectory(outputRoot, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        return new ExportResult(outputRoot, zipPath, changed.Select(path => path.Replace(Path.DirectorySeparatorChar, '/')).ToArray());
    }

    private static string ResolveOutputBase(string? outputDirectory, string romfs)
    {
        var target = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Directory.GetParent(romfs)!.FullName, "pk3ds-mac-output")
            : Path.GetFullPath(outputDirectory.Trim());
        Directory.CreateDirectory(target);
        return target;
    }

    internal static void CopyRelativeFile(string sourceRoot, string destinationRoot, string relativePath)
    {
        var source = GetChildPath(sourceRoot, relativePath);
        if (!File.Exists(source))
            throw new WorkspaceException($"Falta un archivo necesario en el RomFS: {relativePath}");
        var destination = GetChildPath(destinationRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    /// <summary>
    /// CRO edits normally need the matching static CRR hash table. If the source dump contains
    /// one, rebuild it in the scratch output together with the changed CROs; the source dump is
    /// never touched. Dumps without a CRR continue to produce the ordinary CRO-only patch.
    /// </summary>
    private static void RebuildCrrIfPresent(string sourceRoot, string scratchRoot, string[] copiedFiles, ICollection<string> changed)
    {
        const string crrRelative = ".crr/static.crr";
        var sourceCrr = GetChildPath(sourceRoot, crrRelative);
        if (!File.Exists(sourceCrr))
            return;

        var croPaths = Directory.EnumerateFiles(sourceRoot, "*.cro", SearchOption.TopDirectoryOnly).ToArray();
        if (croPaths.Length == 0)
            return;

        var copiedNames = copiedFiles
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prepared = new List<byte[]>(croPaths.Length);
        foreach (var sourceCro in croPaths)
        {
            var name = Path.GetFileName(sourceCro);
            var input = copiedNames.Contains(name) ? GetChildPath(scratchRoot, name) : sourceCro;
            var original = File.ReadAllBytes(input);
            var fixedCro = CRO.Rehash(original);
            prepared.Add(fixedCro);
            if (copiedNames.Contains(name) || !fixedCro.SequenceEqual(original))
            {
                File.WriteAllBytes(GetChildPath(scratchRoot, name), fixedCro);
                if (!changed.Contains(name, StringComparer.OrdinalIgnoreCase))
                    changed.Add(name);
            }
        }

        var rebuilt = CRO.RebuildCRR(File.ReadAllBytes(sourceCrr), prepared);
        if (!rebuilt.Changed)
            return;
        CopyRelativeFile(sourceRoot, scratchRoot, crrRelative);
        File.WriteAllBytes(GetChildPath(scratchRoot, crrRelative), rebuilt.Crr);
        if (!changed.Contains(crrRelative, StringComparer.OrdinalIgnoreCase))
            changed.Add(crrRelative);
    }

    /// <summary>
    /// Resolves <paramref name="relativePath"/> under <paramref name="root"/>, refusing any path
    /// that escapes it. Relative paths reach here from request payloads, so this is a trust boundary.
    /// </summary>
    internal static string GetChildPath(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!path.StartsWith(normalizedRoot, StringComparison.Ordinal))
            throw new WorkspaceException("La ruta de archivo no es válida.");
        return path;
    }
}
