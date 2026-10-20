using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.Editors;

/// <summary>Describes an extracted 3DS game workspace and its detected components.</summary>
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

    internal static string ResolveRomFs(string selected)
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
        var exactVersion = new GameConfig(fileCount).Version;
        if (exactVersion != GameVersion.Invalid)
            return exactVersion;

        // A retail RomFS can omit an unused, zero-content archive entry. Keep the exact-count
        // mapping above authoritative, but accept one missing slot when the generation-specific
        // randomizer/editor GARCs still identify that game unambiguously.
        return fileCount switch
        {
            270 when HasGarcFiles(romfs, 12, 38, 39, 40, 212, 213, 214, 215, 218) => GameVersion.XY,
            298 when HasGarcFiles(romfs, 13, 36, 37, 38, 189, 190, 191, 192, 195) => GameVersion.ORAS,
            310 when HasAnyGarcFile(romfs, 82, 83)
                && HasGarcFiles(romfs, 11, 12, 13, 14, 17, 104, 105, 106) => GameVersion.SM,
            332 when HasAnyGarcFile(romfs, 82, 83)
                && HasGarcFiles(romfs, 11, 12, 13, 14, 17, 105, 106, 107) => GameVersion.USUM,
            _ => GameVersion.Invalid,
        };
    }

    private static bool HasGarcFiles(string romFs, params int[] fileNumbers) =>
        fileNumbers.All(fileNumber => HasGarcFile(romFs, fileNumber));

    private static bool HasAnyGarcFile(string romFs, params int[] fileNumbers) =>
        fileNumbers.Any(fileNumber => HasGarcFile(romFs, fileNumber));

    private static bool HasGarcFile(string romFs, int fileNumber)
    {
        var path = Path.Combine(romFs, "a", (fileNumber / 100 % 10).ToString(),
            (fileNumber / 10 % 10).ToString(), (fileNumber % 10).ToString());
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static string? FindExeFs(string root)
    {
        var candidates = new[] { root, Path.Combine(root, "ExeFS"), Path.Combine(root, "exefs") }
            .Where(Directory.Exists);
        foreach (var candidate in candidates)
        {
            if (FindCodeFile(candidate) is not null)
                return candidate;
        }
        return null;
    }

    internal static string? FindCodeFile(string exeFsPath)
    {
        var codeFiles = Directory.EnumerateFiles(exeFsPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), "code", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return codeFiles.Length switch
        {
            0 => null,
            1 => codeFiles[0],
            _ => throw new WorkspaceException("El ExeFS contiene más de un archivo code.bin; quitá la ambigüedad antes de continuar."),
        };
    }

    private static string? FindExheader(string root)
    {
        var candidates = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return (name.StartsWith("exh", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("decryptedexh", StringComparison.OrdinalIgnoreCase))
                    && new FileInfo(path).Length == 0x800;
            })
            .ToArray();

        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new WorkspaceException("El workspace contiene más de un exheader.bin válido; dejá un único archivo antes de continuar."),
        };
    }
}
