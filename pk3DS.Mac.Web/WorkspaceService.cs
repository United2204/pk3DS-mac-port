using System.IO.Compression;
using pk3DS.Core;
using pk3DS.Core.Randomizers;

namespace pk3DS.Mac.Web;

public sealed class WorkspaceService
{
    private static readonly string[] RequiredGarcs = ["personal", "levelup", "gametext", "move", "evolution"];

    public InspectResponse Inspect(WorkspaceRequest request)
    {
        var romfs = ResolveRomFs(request.WorkspacePath);
        var config = DetectConfig(romfs);
        if (config.Version == GameVersion.Invalid)
            throw new WorkspaceException("No pude identificar el juego. Por ahora se admiten RomFS completos de Pokémon X/Y, OR/AS, Sol/Luna y US/UM.");

        return new InspectResponse(
            romfs,
            config.Version.ToString(),
            Directory.Exists(Path.Combine(romfs, "a")),
            "El origen sólo se leerá. La salida será un ZIP LayeredFS para Luma.");
    }

    public RandomizeResponse Randomize(RandomizeRequest request)
    {
        var romfs = ResolveRomFs(request.WorkspacePath);
        if (request.Language is < 0 or > 11)
            throw new WorkspaceException("El idioma debe estar entre 0 y 11.");
        if (request.TitleId.Length != 16 || request.TitleId.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("El Title ID debe tener 16 dígitos hexadecimales.");
        var personal = request.Personal ?? PersonalOptions.FromLegacy(request.RandomizeAbilities, request.RandomizeHeldItems);
        var learnsets = request.Learnsets ?? LearnsetOptions.FromLegacy(request.RandomizeLearnsets);
        var evolutions = request.Evolutions ?? new EvolutionOptions();
        if (!personal.HasChanges && !learnsets.Enabled && evolutions.Mode == EvolutionMode.None)
            throw new WorkspaceException("Seleccioná al menos una opción para randomizar.");

        var probe = DetectConfig(romfs);
        if (probe.Version == GameVersion.Invalid)
            throw new WorkspaceException("No pude identificar el juego desde la carpeta RomFS indicada.");

        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-{Guid.NewGuid():N}");
        var temporaryRomFs = Path.Combine(temporaryRoot, "romfs");
        try
        {
            Directory.CreateDirectory(temporaryRomFs);
            probe.Initialize(romfs, temporaryRoot, request.Language);
            foreach (var garcName in RequiredGarcs)
                CopyRelativeFile(romfs, temporaryRomFs, probe.GetGARCFileName(garcName));

            var config = new GameConfig(probe.Version);
            config.Initialize(temporaryRomFs, temporaryRoot, request.Language);

            var changed = new List<string>();
            if (personal.HasChanges)
            {
                RandomizePersonal(config, personal);
                SavePersonal(config);
                changed.Add(config.GetGARCFileName("personal"));
            }

            if (learnsets.Enabled)
            {
                var randomizer = new LearnsetRandomizer(config, config.Learnsets)
                {
                    Expand = learnsets.Expand,
                    ExpandTo = Math.Clamp(learnsets.MoveCount, 1, 75),
                    Spread = learnsets.Spread,
                    SpreadTo = Math.Clamp(learnsets.MaxLevel, 1, 100),
                    STAB = learnsets.Stab,
                    STABPercent = Math.Clamp(learnsets.StabPercent, 0, 100),
                    STABFirst = learnsets.StabFirst,
                    OrderByPower = learnsets.OrderByPower,
                    Learn4Level1 = learnsets.FourMovesAtLevel1,
                    BannedMoves = learnsets.ExcludeFixedDamage
                        ? [165, 621, .. MoveRandomizer.FixedDamageMoves]
                        : [165, 621],
                };
                randomizer.Execute();
                SaveLearnsets(config);
                changed.Add(config.GetGARCFileName("levelup"));
            }

            if (evolutions.Mode != EvolutionMode.None)
            {
                RandomizeEvolutions(config, evolutions);
                SaveEvolutions(config);
                changed.Add(config.GetGARCFileName("evolution"));
            }

            var outputBase = ResolveOutputBase(request.OutputDirectory, romfs);
            var label = $"pk3ds-mac-{DateTime.Now:yyyyMMdd-HHmmss}";
            var outputRoot = Path.Combine(outputBase, label);
            var layeredRomFs = Path.Combine(outputRoot, "luma", "titles", request.TitleId.ToUpperInvariant(), "romfs");
            foreach (var relativePath in changed.Distinct(StringComparer.Ordinal))
                CopyRelativeFile(temporaryRomFs, layeredRomFs, relativePath);

            var zipPath = Path.Combine(outputBase, $"{label}-LayeredFS.zip");
            ZipFile.CreateFromDirectory(outputRoot, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
            return new RandomizeResponse(outputRoot, zipPath, changed.Select(path => path.Replace(Path.DirectorySeparatorChar, '/')).ToArray());
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void RandomizePersonal(GameConfig config, PersonalOptions options)
    {
        var randomizer = new PersonalRandomizer(config.Personal.Table, config)
        {
            TypeCount = 18,
            ModifyAbilities = options.RandomizeAbilities,
            AllowWonderGuard = options.AllowWonderGuard,
            ModifyHeldItems = options.RandomizeHeldItems,
            ModifyCatchRate = options.RandomizeCatchRate,
            ModifyLearnsetTM = options.RandomizeTmCompatibility,
            ModifyLearnsetHM = options.RandomizeHmCompatibility,
            ModifyLearnsetTypeTutors = options.RandomizeTypeTutors,
            ModifyLearnsetMoveTutors = config.ORAS && options.RandomizeMoveTutors,
            ModifyStats = options.RandomizeStats,
            ShuffleStats = options.ShuffleStats,
            StatsToRandomize = NormalizeStats(options.StatsToRandomize),
            StatDeviation = Math.Clamp(options.StatDeviation, 1, 95),
            ModifyTypes = options.RandomizeTypes,
            SameTypeChance = Math.Clamp(options.SameTypeChance, 0, 100),
            ModifyEggGroup = options.RandomizeEggGroups,
            SameEggGroupChance = Math.Clamp(options.SameEggGroupChance, 0, 100),
        };
        randomizer.Execute();
    }

    private static bool[] NormalizeStats(bool[]? values) => values is { Length: 6 } ? values : [true, true, true, true, true, true];

    private static void RandomizeEvolutions(GameConfig config, EvolutionOptions options)
    {
        var randomizer = new EvolutionRandomizer(config, config.Evolutions)
        {
            Randomizer =
            {
                rBST = options.MatchBst,
                rEXP = options.MatchExperience,
                rType = options.MatchType,
                L = options.IncludeLegendary,
                E = options.IncludeMythical,
            },
        };
        randomizer.Randomizer.Initialize();
        switch (options.Mode)
        {
            case EvolutionMode.Replacements:
                randomizer.Execute();
                break;
            case EvolutionMode.RemoveTrades:
                randomizer.ExecuteTrade();
                break;
            case EvolutionMode.EveryLevel:
                randomizer.ExecuteEvolveEveryLevel();
                randomizer.Execute();
                break;
        }
    }

    private static void SavePersonal(GameConfig config)
    {
        var files = config.GARCPersonal.Files;
        for (var i = 0; i < files.Length - 1; i++)
            config.Personal.Table[i].Write().CopyTo(files[^1], i * files[i].Length);
        config.GARCPersonal.Files = files;
        config.GARCPersonal.Save();
    }

    private static void SaveLearnsets(GameConfig config)
    {
        var files = config.GARCLearnsets.Files;
        for (var i = 0; i < files.Length; i++)
            files[i] = config.Learnsets[i].Write();
        config.GARCLearnsets.Files = files;
        config.GARCLearnsets.Save();
    }

    private static void SaveEvolutions(GameConfig config)
    {
        var evolutionGarc = config.GetGARCData("evolution");
        evolutionGarc.Files = config.Evolutions.Select(evolution => evolution.Write()).ToArray();
        evolutionGarc.Save();
    }

    private static GameConfig DetectConfig(string romfs)
    {
        var archiveRoot = Path.Combine(romfs, "a");
        if (!Directory.Exists(archiveRoot))
            throw new WorkspaceException("No encuentro la carpeta ‘a’ dentro del RomFS. Indicá la carpeta RomFS extraída, no el archivo .cxi.");

        var fileCount = Directory.EnumerateFiles(archiveRoot, "*", SearchOption.AllDirectories)
            .Count(path => Path.GetFileName(path).Length == 1);
        return new GameConfig(fileCount);
    }

    private static string ResolveRomFs(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new WorkspaceException("Indicá la carpeta RomFS extraída.");

        var expanded = workspacePath.Trim();
        var fullPath = Path.GetFullPath(expanded);
        if (!Directory.Exists(fullPath))
            throw new WorkspaceException("La carpeta indicada no existe.");

        if (Directory.Exists(Path.Combine(fullPath, "a")))
            return fullPath;
        foreach (var candidate in new[] { "RomFS", "romfs" })
        {
            var nested = Path.Combine(fullPath, candidate);
            if (Directory.Exists(Path.Combine(nested, "a")))
                return nested;
        }
        throw new WorkspaceException("No encuentro un RomFS válido en esa carpeta.");
    }

    private static string ResolveOutputBase(string? outputDirectory, string romfs)
    {
        var target = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Directory.GetParent(romfs)!.FullName, "pk3ds-mac-output")
            : Path.GetFullPath(outputDirectory.Trim());
        Directory.CreateDirectory(target);
        return target;
    }

    private static void CopyRelativeFile(string sourceRoot, string destinationRoot, string relativePath)
    {
        var source = GetChildPath(sourceRoot, relativePath);
        if (!File.Exists(source))
            throw new WorkspaceException($"Falta un archivo necesario en el RomFS: {relativePath}");
        var destination = GetChildPath(destinationRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static string GetChildPath(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!path.StartsWith(normalizedRoot, StringComparison.Ordinal))
            throw new WorkspaceException("La ruta de archivo no es válida.");
        return path;
    }
}

public sealed record WorkspaceRequest(string WorkspacePath);
public sealed record RandomizeRequest(
    string WorkspacePath,
    string? OutputDirectory,
    string TitleId,
    int Language,
    bool RandomizeAbilities,
    bool RandomizeHeldItems,
    bool RandomizeLearnsets,
    PersonalOptions? Personal = null,
    LearnsetOptions? Learnsets = null,
    EvolutionOptions? Evolutions = null);
public sealed record InspectResponse(string RomFsPath, string GameVersion, bool IsComplete, string Note);
public sealed record RandomizeResponse(string OutputFolder, string ZipPath, string[] ChangedFiles);
public sealed class WorkspaceException(string message) : Exception(message);

public sealed record PersonalOptions(
    bool RandomizeAbilities = false,
    bool AllowWonderGuard = true,
    bool RandomizeHeldItems = false,
    bool RandomizeCatchRate = false,
    bool RandomizeTmCompatibility = false,
    bool RandomizeHmCompatibility = false,
    bool RandomizeTypeTutors = false,
    bool RandomizeMoveTutors = false,
    bool RandomizeStats = false,
    bool ShuffleStats = false,
    bool[]? StatsToRandomize = null,
    decimal StatDeviation = 25,
    bool RandomizeTypes = false,
    decimal SameTypeChance = 50,
    bool RandomizeEggGroups = false,
    decimal SameEggGroupChance = 50)
{
    public bool HasChanges => RandomizeAbilities || RandomizeHeldItems || RandomizeCatchRate || RandomizeTmCompatibility || RandomizeHmCompatibility || RandomizeTypeTutors || RandomizeMoveTutors || RandomizeStats || ShuffleStats || RandomizeTypes || RandomizeEggGroups;
    public static PersonalOptions FromLegacy(bool abilities, bool heldItems) => new(RandomizeAbilities: abilities, RandomizeHeldItems: heldItems);
}

public sealed record LearnsetOptions(
    bool Enabled = false,
    bool Expand = true,
    int MoveCount = 25,
    bool Spread = true,
    int MaxLevel = 75,
    bool Stab = true,
    decimal StabPercent = 52.3m,
    bool StabFirst = true,
    bool OrderByPower = true,
    bool FourMovesAtLevel1 = false,
    bool ExcludeFixedDamage = false)
{
    public static LearnsetOptions FromLegacy(bool enabled) => new(Enabled: enabled);
}

public enum EvolutionMode { None, Replacements, RemoveTrades, EveryLevel }

public sealed record EvolutionOptions(
    EvolutionMode Mode = EvolutionMode.None,
    bool MatchBst = true,
    bool MatchExperience = false,
    bool MatchType = false,
    bool IncludeLegendary = false,
    bool IncludeMythical = false);
