using System.IO.Compression;
using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>
/// Shared scaffolding for every editor.
/// <para>
/// Editing never touches the source dump. An export copies the GARCs it needs into a scratch
/// RomFS, mutates that copy, and packs only the changed files into a LayeredFS ZIP. Each editor
/// supplies just the mutation; this class owns the workspace, Title ID, scratch directory and
/// archive so the eleven editors cannot drift apart on any of it.
/// </para>
/// </summary>
public static class EditorSession
{
    /// <summary>
    /// GARCs that <see cref="GameConfig.Initialize"/> always reads, regardless of the editor.
    /// They must exist in the scratch RomFS even when the editor does not modify them.
    /// </summary>
    internal static readonly string[] RequiredGarcs = ["personal", "levelup", "gametext", "move", "evolution", "eggmove"];

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
            var probe = new GameConfig(workspace.Version);
            probe.Initialize(workspace.RomFsPath, Directory.GetParent(scratchRomFs)!.FullName, languageId);
            foreach (var name in RequiredGarcs.Concat(extraGarcs).Distinct(StringComparer.Ordinal))
                CopyRelativeFile(workspace.RomFsPath, scratchRomFs, probe.GetGARCFileName(name));

            var config = new GameConfig(workspace.Version);
            config.Initialize(scratchRomFs, Directory.GetParent(scratchRomFs)!.FullName, languageId);

            var changed = apply(config);
            return CreateLayeredFsArchive(outputDirectory, workspace.RomFsPath, scratchRomFs, titleId, changed, label);
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
            return CreateLayeredFsArchive(outputDirectory, workspace.RomFsPath, scratchRomFs, titleId, copied, label);
        });
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

    private static ExportResult CreateLayeredFsArchive(
        string? outputDirectory, string sourceRomFs, string scratchRomFs,
        string titleId, IEnumerable<string> changedFiles, string labelPrefix)
    {
        var outputBase = ResolveOutputBase(outputDirectory, sourceRomFs);
        var label = $"pk3ds-mac-{labelPrefix}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var outputRoot = Path.Combine(outputBase, label);
        var layeredRomFs = Path.Combine(outputRoot, "luma", "titles", titleId.ToUpperInvariant(), "romfs");
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
