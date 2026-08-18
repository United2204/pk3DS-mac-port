namespace pk3DS.Editors;

public sealed record RandomizeRequest(
    string WorkspacePath,
    string? OutputDirectory,
    string? TitleId,
    int? Language,
    bool RandomizeAbilities,
    bool RandomizeHeldItems,
    bool RandomizeLearnsets,
    PersonalOptions? Personal = null,
    LearnsetOptions? Learnsets = null,
    EggMoveOptions? EggMoves = null,
    MoveOptions? Moves = null,
    EvolutionOptions? Evolutions = null,
    WildRandomizerOptions? Wild = null,
    TrainerRandomizerOptions? Trainers = null);

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
    decimal SameEggGroupChance = 50,
    bool RemoveEvYields = false,
    bool SetFastGrowth = false,
    int? BaseExperiencePercent = null,
    bool QuickHatch = false,
    int? SetCatchRate = null,
    bool RemoveTutorCompatibility = false,
    bool FullTmCompatibility = false,
    bool FullHmCompatibility = false,
    bool FullMoveTutorCompatibility = false)
{
    public bool HasBulkChanges => RemoveEvYields || SetFastGrowth || BaseExperiencePercent is not null || QuickHatch || SetCatchRate is not null || RemoveTutorCompatibility || FullTmCompatibility || FullHmCompatibility || FullMoveTutorCompatibility;
    public bool HasChanges => RandomizeAbilities || RandomizeHeldItems || RandomizeCatchRate || RandomizeTmCompatibility || RandomizeHmCompatibility || RandomizeTypeTutors || RandomizeMoveTutors || RandomizeStats || ShuffleStats || RandomizeTypes || RandomizeEggGroups || HasBulkChanges;
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

public sealed record EggMoveOptions(
    bool Enabled = false,
    bool Expand = true,
    int MoveCount = 18,
    bool Stab = true,
    decimal StabPercent = 32.1m);

public sealed record MoveOptions(
    bool RandomizeType = false,
    bool RandomizeCategory = false,
    bool MetronomeMode = false)
{
    public bool HasChanges => RandomizeType || RandomizeCategory || MetronomeMode;
}

public enum EvolutionMode { None, Replacements, RemoveTrades, EveryLevel }

public sealed record EvolutionOptions(
    EvolutionMode Mode = EvolutionMode.None,
    bool MatchBst = true,
    bool MatchExperience = false,
    bool MatchType = false,
    bool IncludeLegendary = false,
    bool IncludeMythical = false);

/// <summary>
/// Bulk encounter randomization shared by the Gen. VI and Gen. VII games.
/// </summary>
public sealed record WildRandomizerOptions(
    bool Enabled = false,
    bool RandomizeSpecies = true,
    bool RandomizeLevels = false,
    decimal LevelMultiplier = 1.0m,
    bool HomogeneousHordes = false,
    bool MatchBst = true,
    bool IncludeLegendary = false,
    bool IncludeMythical = false)
{
    public bool HasChanges => Enabled && (RandomizeSpecies || RandomizeLevels);
}

/// <summary>Safe first-pass trainer randomization shared by Gen. VI and Gen. VII.</summary>
public sealed record TrainerRandomizerOptions(
    bool Enabled = false,
    bool RandomizeSpecies = true,
    bool RandomizeLevels = false,
    decimal LevelMultiplier = 1.0m,
    bool RandomizeClasses = false,
    bool RandomizeComposition = false,
    int MinTeamSize = 1,
    int MaxTeamSize = 6,
    bool IgnoreSpecialClasses = true,
    bool OnlySinglesForClasses = false,
    bool RandomizeItems = false,
    bool RandomizeAbilities = false,
    bool RandomizeMoves = false,
    bool MaximizeAI = false,
    bool MaximizeIVs = false,
    bool ForceFullyEvolved = false,
    decimal FullyEvolvedLevel = 30,
    bool RandomizePrizes = false,
    decimal PrizeChance = 15,
    bool FillImportantGen7Teams = false,
    bool ForceHighPower = false,
    decimal HighPowerLevel = 30,
    bool RandomizeNature = false,
    bool RandomizeShiny = false,
    decimal ShinyChance = 3,
    bool RandomizeTypeThemes = false,
    bool AllowMegaForms = false,
    bool IncludeGymTrainerThemes = false)
{
    public bool HasChanges => Enabled && (RandomizeSpecies || RandomizeLevels || RandomizeClasses || RandomizeComposition || RandomizeItems || RandomizeAbilities || RandomizeMoves || MaximizeAI || MaximizeIVs || ForceFullyEvolved || RandomizePrizes || FillImportantGen7Teams || ForceHighPower || RandomizeNature || RandomizeShiny || RandomizeTypeThemes || AllowMegaForms || IncludeGymTrainerThemes);
}
