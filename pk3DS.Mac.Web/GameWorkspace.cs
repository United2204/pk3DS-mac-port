using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.Mac.Web;

/// <summary>
/// Read-only description of an extracted 3DS game workspace.
/// The source workspace is never edited; operations copy only modified files to LayeredFS output.
/// </summary>
public sealed record GameWorkspace(
    string RootPath,
    string RomFsPath,
    string? ExeFsPath,
    string? ExheaderPath,
    GameVersion Version)
{
    public bool HasExeFs => ExeFsPath is not null;
    public bool HasExheader => ExheaderPath is not null;
    public string? TitleId => ExheaderPath is null ? null : new Exheader(ExheaderPath).TitleID.ToString("X16");

    public static GameWorkspace Open(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new WorkspaceException("Elegí la carpeta extraída del juego.");

        var selected = Path.GetFullPath(workspacePath.Trim());
        if (!Directory.Exists(selected))
            throw new WorkspaceException("La carpeta indicada no existe.");

        var romfs = ResolveRomFs(selected);
        var version = DetectVersion(romfs);
        if (version == GameVersion.Invalid)
            throw new WorkspaceException("No pude identificar el juego. Se admiten dumps completos de Pokémon X/Y, OR/AS, Sol/Luna y Ultrasol/Ultraluna.");

        var root = Directory.Exists(Path.Combine(selected, "a"))
            ? Directory.GetParent(selected)?.FullName ?? selected
            : selected;
        return new GameWorkspace(root, romfs, FindExeFs(root), FindExheader(root), version);
    }

    private static string ResolveRomFs(string selected)
    {
        if (Directory.Exists(Path.Combine(selected, "a")))
            return selected;

        foreach (var name in new[] { "RomFS", "romfs" })
        {
            var candidate = Path.Combine(selected, name);
            if (Directory.Exists(Path.Combine(candidate, "a")))
                return candidate;
        }
        throw new WorkspaceException("No encuentro una carpeta RomFS válida. Debe contener la carpeta ‘a’.");
    }

    private static GameVersion DetectVersion(string romfs)
    {
        var archiveRoot = Path.Combine(romfs, "a");
        var fileCount = Directory.EnumerateFiles(archiveRoot, "*", SearchOption.AllDirectories)
            .Count(path => Path.GetFileName(path).Length == 1);
        return new GameConfig(fileCount).Version;
    }

    private static string? FindExeFs(string root)
    {
        var candidates = new[] { root, Path.Combine(root, "ExeFS"), Path.Combine(root, "exefs") }
            .Where(Directory.Exists);
        foreach (var candidate in candidates)
        {
            if (Directory.EnumerateFiles(candidate).Any(path => Path.GetFileName(path).Contains("code", StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return null;
    }

    private static string? FindExheader(string root) => Directory.EnumerateFiles(root)
        .FirstOrDefault(path =>
        {
            var name = Path.GetFileName(path);
            return (name.StartsWith("exh", StringComparison.OrdinalIgnoreCase) || name.StartsWith("decryptedexh", StringComparison.OrdinalIgnoreCase))
                && new FileInfo(path).Length == 0x800;
        });
}

public sealed record ModuleAvailability(string Id, string Name, string Area, bool SourceAvailable, string Requirement);
