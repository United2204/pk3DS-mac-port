using pk3DS.Core;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// The bulk randomizer: applies every selected module in one pass and exports a single LayeredFS
/// archive, so combined options land in one patch instead of several conflicting ones.
/// </summary>
public static class RandomizerService
{
    private static readonly object RandomizeLock = new();

    /// <summary>
    /// Runs one RNG-backed mutation while holding the same gate used by the combined web
    /// randomizer. A null seed keeps the current RNG stream; a supplied seed replaces it before
    /// the mutation, matching <see cref="Randomize"/>.
    /// </summary>
    internal static T WithRandomizerSeed<T>(int? seed, Func<T> mutation)
    {
        lock (RandomizeLock)
        {
            if (seed is int value)
                Util.ReseedRand(value);
            return mutation();
        }
    }

    // Struggle (165) and Shadow Blast (621) are never valid randomizer output.
    // A fresh array per read: the randomizers take ownership of what they are handed.
    private static int[] AlwaysBannedMoves => [165, 621];
    // USUM exposes the first 67 bits of PersonalInfoSM's 80-bit special-tutor field.
    // The trailing 13 bits are reserved/hidden and must survive bulk compatibility edits.
    private const int UsumBeachTutorCount = 67;
    // Windows uses this fallback list for both Gen. VI Level Up and Egg Move randomizers.
    // The optional ExeFS refresh in those forms only runs when HMs are allowed and therefore
    // does not affect the effective ban list.
    private static readonly int[] Gen6HmMoves = [15, 19, 57, 70, 127, 249, 291];
    private static readonly int[] OrasTutorBitCounts = [15, 17, 16, 15];

    private static readonly (int Species, ushort[] Items)[] MegaOptionsXY =
    [
        (003, [659]), (006, [660, 678]), (009, [661]), (065, [679]), (094, [656]),
        (115, [675]), (127, [671]), (130, [676]), (142, [672]), (150, [662, 663]),
        (181, [658]), (212, [670]), (214, [680]), (229, [666]), (248, [669]),
        (257, [664]), (282, [657]), (303, [681]), (306, [667]), (308, [665]),
        (310, [682]), (354, [668]), (359, [677]), (380, [684]), (381, [685]),
        (445, [683]), (448, [673]), (460, [674]),
    ];

    private static readonly (int Species, ushort[] Items)[] MegaOptionsORAS =
    [
        (015, [770]), (018, [762]), (080, [760]), (208, [761]), (254, [753]),
        (260, [752]), (302, [754]), (319, [759]), (323, [767]), (334, [755]),
        (362, [763]), (373, [769]), (376, [758]), (428, [768]), (475, [756]),
        (531, [757]), (719, [764]),
    ];

    private static readonly HashSet<int> XyStoryMegaTrainerIDs = [188, 263, 276, 277, 519, 520, 521, 526, 599, 600, 601];
    private static readonly HashSet<int> OrasStoryMegaTrainerIDs = [178, 235, 557, 583, 687, 698, 699, 700, 701, 713, 906, 907, 908, 909, 910, 911, 912, 913, 942, 944, 946];

    public static ExportResult Randomize(RandomizeRequest request)
    {
        var personal = request.Personal ?? PersonalOptions.FromLegacy(request.RandomizeAbilities, request.RandomizeHeldItems);
        var learnsets = request.Learnsets ?? LearnsetOptions.FromLegacy(request.RandomizeLearnsets);
        var eggMoves = request.EggMoves ?? new EggMoveOptions();
        var moves = request.Moves ?? new MoveOptions();
        var evolutions = request.Evolutions ?? new EvolutionOptions();
        var wild = request.Wild ?? new WildRandomizerOptions();
        var trainers = request.Trainers ?? new TrainerRandomizerOptions();

        if (!personal.HasChanges && !learnsets.Enabled && !eggMoves.Enabled && !moves.HasChanges
            && evolutions.Mode == EvolutionMode.None && !wild.HasChanges && !trainers.HasChanges)
            throw new WorkspaceException("Seleccioná al menos una opción para randomizar.");

        var workspace = GameWorkspace.Open(request.WorkspacePath);
        IEnumerable<string> wildGarcs = wild.HasChanges
            ? workspace.Version is GameVersion.SM or GameVersion.SN or GameVersion.MN or GameVersion.US or GameVersion.UM or GameVersion.USUM
                ? new[] { "encdata", "zonedata", "worlddata" }
                : new[] { "encdata" }
            : Array.Empty<string>();
        IEnumerable<string> trainerGarcs = trainers.HasChanges
            ? new[] { "trdata", "trpoke" }
            : Array.Empty<string>();
        if (trainers.ForceHighPower || trainers.UseCurrentLearnsetMoves)
            trainerGarcs = trainerGarcs.Append("levelup");
        var hmMoves = workspace.Version is GameVersion.XY or GameVersion.ORAS or GameVersion.ORASDEMO ? Gen6HmMoves : Array.Empty<int>();

        return EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "randomizer", wildGarcs.Concat(trainerGarcs), config => RandomizeData(config, request.Seed,
                hmMoves, personal, learnsets, eggMoves, moves, evolutions, wild, trainers));
    }

    private static List<string> RandomizeData(GameConfig config, int? seed,
        int[] hmMoves, PersonalOptions personal, LearnsetOptions learnsets, EggMoveOptions eggMoves, MoveOptions moves,
        EvolutionOptions evolutions, WildRandomizerOptions wild, TrainerRandomizerOptions trainers)
    {
        return WithRandomizerSeed(seed, () =>
        {
            var changed = new List<string>();

            if (personal.HasChanges)
            {
                RandomizePersonal(config, personal);
                ModifyPersonal(config, personal);
                GarcWriter.SavePersonal(config);
                changed.Add(config.GetGARCFileName("personal"));
            }

            if (learnsets.Enabled)
            {
                RandomizeLearnsets(config, learnsets, hmMoves);
                GarcWriter.SaveLearnsets(config);
                changed.Add(config.GetGARCFileName("levelup"));
            }

            if (eggMoves.Enabled)
            {
                RandomizeEggMoves(config, eggMoves, hmMoves);
                changed.Add(config.GetGARCFileName("eggmove"));
            }

            if (moves.HasChanges)
            {
                ModifyMoves(config, moves);
                GarcWriter.SaveMoves(config);
                changed.Add(config.GetGARCFileName("move"));
            }

            if (evolutions.Mode != EvolutionMode.None)
            {
                RandomizeEvolutions(config, evolutions);
                GarcWriter.SaveEvolutions(config);
                changed.Add(config.GetGARCFileName("evolution"));
            }

            if (wild.HasChanges)
            {
                RandomizeWild(config, wild);
                changed.Add(config.GetGARCFileName("encdata"));
            }

            if (trainers.HasChanges)
            {
                RandomizeTrainers(config, trainers);
                changed.Add(config.GetGARCFileName("trdata"));
                changed.Add(config.GetGARCFileName("trpoke"));
            }

            return changed;
        });
    }

    private static void RandomizePersonal(GameConfig config, PersonalOptions options)
    {
        new PersonalRandomizer(config.Personal.Table, config)
        {
            TypeCount = 18,
            ModifyAbilities = options.RandomizeAbilities,
            AllowWonderGuard = options.AllowWonderGuard,
            ModifyHeldItems = options.RandomizeHeldItems,
            ModifyCatchRate = options.RandomizeCatchRate,
            ModifyLearnsetTM = options.RandomizeTmCompatibility,
            ModifyLearnsetHM = options.RandomizeHmCompatibility,
            ModifyLearnsetTypeTutors = options.RandomizeTypeTutors,
            // OR/AS uses the four special-tutor groups; USUM uses its visible Beach Tutor bits.
            // Both are handled by PersonalRandomizer.RandomizeSpecialTutors at 30% per bit.
            ModifyLearnsetMoveTutors = (config.ORAS || config.USUM) && options.RandomizeMoveTutors,
            ModifyStats = options.RandomizeStats,
            ShuffleStats = options.ShuffleStats,
            StatsToRandomize = NormalizeStats(options.StatsToRandomize),
            StatDeviation = Math.Clamp(options.StatDeviation, 1, 95),
            ModifyTypes = options.RandomizeTypes,
            SameTypeChance = Math.Clamp(options.SameTypeChance, 0, 100),
            ModifyEggGroup = options.RandomizeEggGroups,
            SameEggGroupChance = Math.Clamp(options.SameEggGroupChance, 0, 100),
        }.Execute();
    }

    private static bool[] NormalizeStats(bool[]? values) => values is { Length: 6 } ? values : [true, true, true, true, true, true];

    /// <summary>
    /// Ports the non-random "Modify All" enhancements from Personal Stats in pk3DS for Windows.
    /// These run after the optional randomizer so a user can combine both operations in one export.
    /// </summary>
    private static void ModifyPersonal(GameConfig config, PersonalOptions options)
    {
        if (!options.HasBulkChanges)
            return;

        // Index 0 is the empty personal entry, not a Pokémon species.
        foreach (var species in config.Personal.Table.Skip(1))
        {
            if (options.RemoveEvYields)
            {
                species.EV_HP = 0;
                species.EV_ATK = 0;
                species.EV_DEF = 0;
                species.EV_SPE = 0;
                species.EV_SPA = 0;
                species.EV_SPD = 0;
            }

            if (options.SetFastGrowth)
                species.EXPGrowth = 5;
            if (options.BaseExperiencePercent is not null)
                species.BaseEXP = Math.Clamp((int)Math.Round(species.BaseEXP * options.BaseExperiencePercent.Value / 100m), 0, ushort.MaxValue);
            if (options.QuickHatch)
                species.HatchCycles = 1;
            if (options.SetCatchRate is not null)
                species.CatchRate = Math.Clamp(options.SetCatchRate.Value, 0, byte.MaxValue);

            if (options.RemoveTutorCompatibility)
            {
                // Match Windows' visible range: clear compatibility entries through index 100.
                for (var i = 0; i < Math.Min(101, species.TMHM.Length); i++)
                    species.TMHM[i] = false;
                SetVisibleBits(species.TypeTutors, false, VisibleTypeTutorCount(config));
                if (config.ORAS)
                {
                    for (var tutor = 0; tutor < Math.Min(OrasTutorBitCounts.Length, species.SpecialTutors.Length); tutor++)
                        SetVisibleBits(species.SpecialTutors[tutor], false, OrasTutorBitCounts[tutor]);
                }
                else if (config.USUM && species.SpecialTutors.Length > 0)
                {
                    SetVisibleBits(species.SpecialTutors[0], false, UsumBeachTutorCount);
                }
            }
            if (options.FullTmCompatibility)
            {
                for (var i = 0; i < Math.Min(100, species.TMHM.Length); i++)
                    species.TMHM[i] = true;
            }
            if (options.FullHmCompatibility)
            {
                for (var i = 100; i < species.TMHM.Length; i++)
                    species.TMHM[i] = true;
            }
            if (options.FullMoveTutorCompatibility)
                SetVisibleBits(species.TypeTutors, true, VisibleTypeTutorCount(config));
            if (options.FullBeachTutorCompatibility && config.USUM && species.SpecialTutors.Length > 0)
                SetVisibleBits(species.SpecialTutors[0], true, UsumBeachTutorCount);
        }
    }

    private static int VisibleTypeTutorCount(GameConfig config) =>
        config.Generation == 6 && !config.ORAS ? 7 : 8;

    private static void SetVisibleBits(bool[] bits, bool value, int count)
    {
        for (var i = 0; i < Math.Min(count, bits.Length); i++)
            bits[i] = value;
    }

    private static void RandomizeLearnsets(GameConfig config, LearnsetOptions options, int[] hmMoves)
    {
        if (options.MetronomeMode)
        {
            LearnsetEditor.ApplyMetronome(config);
            return;
        }

        int[] banned = config.Generation == 7
            // LevelUpEditor7 also excludes Dark Void (464) and every Z-Move.
            ? [.. AlwaysBannedMoves, 464, .. Legal.Z_Moves]
            : options.AllowHMs
                ? AlwaysBannedMoves
                : [.. AlwaysBannedMoves, .. hmMoves];
        new LearnsetRandomizer(config, config.Learnsets)
        {
            Expand = options.Expand,
            ExpandTo = Math.Clamp(options.MoveCount, 1, 75),
            Spread = options.Spread,
            SpreadTo = Math.Clamp(options.MaxLevel, 1, 100),
            STAB = options.Stab,
            STABPercent = Math.Clamp(options.StabPercent, 0, 100),
            // Windows uses its single “Bias by Type” checkbox for both settings.
            STABFirst = options.Stab,
            OrderByPower = options.OrderByPower,
            Learn4Level1 = options.FourMovesAtLevel1,
            BannedMoves = options.ExcludeFixedDamage
                ? [.. banned, .. MoveRandomizer.FixedDamageMoves]
                : banned,
        }.Execute();
    }

    private static void RandomizeEggMoves(GameConfig config, EggMoveOptions options, int[] hmMoves)
    {
        var garc = config.GetGARCData("eggmove");
        EggMoves[] sets = config.Generation == 6
            ? EggMoves6.GetArray(garc.Files)
            : EggMoves7.GetArray(garc.Files);
        // Gen VII additionally bans Celebrate (464) and the Z-Moves, which cannot be inherited.
        int[] banned = config.Generation == 7
            ? [.. AlwaysBannedMoves, 464, .. Legal.Z_Moves]
            : AlwaysBannedMoves;
        if (!options.AllowHMs)
            banned = [.. banned, .. hmMoves];

        new EggMoveRandomizer(config, sets)
        {
            Expand = options.Expand,
            ExpandTo = Math.Clamp(options.MoveCount, 1, 18),
            STAB = options.Stab,
            STABPercent = Math.Clamp(options.StabPercent, 0, 100),
            BannedMoves = banned,
        }.Execute();

        garc.Files = sets.Select(set => set.Write()).ToArray();
        garc.Save();
    }

    private static void ModifyMoves(GameConfig config, MoveOptions options)
    {
        var random = Util.Rand;
        for (var moveId = 1; moveId < config.Moves.Length; moveId++)
        {
            var move = config.Moves[moveId];
            // The Windows editor leaves Struggle and Curse unchanged.
            if (moveId is 165 or 174)
                continue;

            if (options.RandomizeCategory && move.Category > 0)
                move.Category = random.Next(1, 3);
            if (options.RandomizeType)
                move.Type = random.Next(0, 18);
        }

        if (!options.MetronomeMode)
            return;

        // Same values used by the Windows "Metronome Mode" button.
        for (var moveId = 1; moveId < config.Moves.Length; moveId++)
            config.Moves[moveId].PP = moveId switch { 118 => 40, 33 => 1, _ => 0 };
    }

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

    private static void RandomizeTrainers(GameConfig config, TrainerRandomizerOptions options)
    {
        if (config.Generation is not (6 or 7))
            throw new WorkspaceException("La randomización masiva de entrenadores requiere un juego de Gen. VI o Gen. VII.");
        if (options.RandomizePrizes && config.Generation != 6)
            throw new WorkspaceException("Los premios de entrenador solo existen en el flujo de entrenadores de Gen. VI.");
        if (options.FillImportantGen7Teams && config.Generation != 7)
            throw new WorkspaceException("Los equipos importantes de seis Pokémon solo existen en el flujo de entrenadores de Gen. VII.");
        if ((options.RandomizeNature || options.RandomizeShiny) && config.Generation != 7)
            throw new WorkspaceException("Naturaleza y shiny aleatorios solo están disponibles en el flujo de entrenadores de Gen. VII.");
        if (options.RandomizeTypeThemes && !options.RandomizeSpecies)
            throw new WorkspaceException("Los temas por tipo requieren activar la randomización de especies.");
        if (options.AllowMegaForms && !options.RandomizeSpecies)
            throw new WorkspaceException("Las formas Mega aleatorias requieren activar la randomización de especies.");
        if (options.EnsureStoryMegaEvolutions && !options.RandomizeSpecies)
            throw new WorkspaceException("Las Megaevoluciones de historia requieren activar la randomización de especies.");
        if (options.IncludeGymTrainerThemes && config.Generation != 6)
            throw new WorkspaceException("Los temas de entrenadores de gimnasio solo están disponibles en el flujo de entrenadores de Gen. VI.");
        if (options.IncludeGymTrainerThemes && !options.RandomizeTypeThemes)
            throw new WorkspaceException("Los temas de entrenadores de gimnasio requieren activar los temas por tipo.");
        if ((options.GymEliteOnlyThemes || options.EnsureStoryMegaEvolutions || options.FillImportantGen6Teams) && config.Generation != 6)
            throw new WorkspaceException("Las opciones de gimnasios, Megaevoluciones de historia y equipos importantes solo están disponibles en Gen. VI.");
        if (options.GymEliteOnlyThemes && !options.RandomizeTypeThemes)
            throw new WorkspaceException("El filtro de gimnasios y Alto Mando requiere activar los temas por tipo.");
        var hasAllowedSpeciesGeneration = options.AllowGen1 || options.AllowGen2 || options.AllowGen3
            || options.AllowGen4 || options.AllowGen5 || options.AllowGen6
            || (config.Generation == 7 && options.AllowGen7);
        if ((options.RandomizeSpecies || options.RandomizeComposition || options.FillImportantGen6Teams
                || options.FillImportantGen7Teams || options.ForceFullyEvolved)
            && !hasAllowedSpeciesGeneration)
            throw new WorkspaceException("Seleccioná al menos una generación para el pool de especies.");
        var moveModes = Convert.ToInt32(options.RandomizeMoves) + Convert.ToInt32(options.UseCurrentLearnsetMoves)
            + Convert.ToInt32(options.MetronomeMoves);
        if (moveModes > 1)
            throw new WorkspaceException("Elegí un único modo de movimientos para los entrenadores.");
        if ((options.UseCurrentLearnsetMoves || options.ForceHighPower) && config.Learnsets is null)
            throw new WorkspaceException("Los movimientos por nivel requieren el GARC levelup del workspace.");

        var species = new SpeciesRandomizer(config)
        {
            G1 = options.AllowGen1,
            G2 = options.AllowGen2,
            G3 = options.AllowGen3,
            G4 = options.AllowGen4,
            G5 = options.AllowGen5,
            G6 = options.AllowGen6,
            G7 = config.Generation == 7 && options.AllowGen7,
            L = options.IncludeLegendary,
            E = options.IncludeMythical,
            Shedinja = config.Generation == 6 || options.IncludeShedinja,
            rBST = options.MatchBst,
        };
        species.Initialize();

        var speciesCount = Catalogs.SpeciesCount(config);
        var fallbackSpecies = GetTrainerFallbackSpecies(config, speciesCount, options);
        var trainerClassCount = Catalogs.TrainerClasses(config).Length;
        var itemCount = Catalogs.ItemCount(config);
        var trainerItemPool = GetTrainerItemPool(config, itemCount);
        var form = new FormRandomizer(config)
        {
            AllowMega = options.AllowMegaForms,
            AllowAlolanForm = config.Generation == 7,
        };
        var finalSpecies = GetFinalTrainerSpecies(config, speciesCount, options);
        var teamRange = NormalizeTeamRange(options.MinTeamSize, options.MaxTeamSize);
        var importantGen7 = config.USUM ? Legal.ImportantTrainers_USUM : Legal.ImportantTrainers_SM;
        var importantGen6 = GetImportantGen6TrainerIDs(config);
        var typeCount = Math.Clamp(config.GetText(TextName.Types).Length, 1, config.Generation == 7 ? 17 : 18);
        var megaOptions = GetMegaOptions(config);
        var gen6Themes = options.RandomizeTypeThemes && config.Generation == 6
            ? BuildGen6TrainerThemes(config, typeCount, options.IncludeGymTrainerThemes, options, megaOptions)
            : null;
        var learnset = options.ForceHighPower || options.UseCurrentLearnsetMoves
            ? config.Learnsets is null
                ? throw new WorkspaceException("Los movimientos por nivel requieren el GARC levelup del workspace.")
                : new LearnsetRandomizer(config, config.Learnsets)
            : null;
        int[] trainerMoveBans = config.Generation == 7
            ? [165, 621, 464, .. Legal.Z_Moves]
            : [165, 621];
        if (options.ExcludeFixedDamageMoves)
            trainerMoveBans = [.. trainerMoveBans, .. MoveRandomizer.FixedDamageMoves];
        var fallbackMoves = GetFallbackMoves(config).Where(moveId => !trainerMoveBans.Contains(moveId)).ToArray();
        var move = config.Moves.Length > config.Info.MaxMoveID
            ? new MoveRandomizer(config)
            {
                rDMG = options.EnsureDamagingMoves,
                rDMGCount = Math.Clamp(options.DamagingMoveCount, 0, 4),
                rSTAB = options.EnsureStabMoves,
                rSTABCount = Math.Clamp(options.StabMoveCount, 0, 4),
                BannedMoves = trainerMoveBans,
            }
            : null;
        var trdata = config.GetGARCData("trdata");
        var trpoke = config.GetGARCData("trpoke");
        var changed = 0;

        // Index 0 is the placeholder trainer used by the game for an empty entry.
        for (var index = 1; index < trdata.Files.Length && index < trpoke.Files.Length; index++)
        {
            if (config.Generation == 6)
            {
                var trainer = new TrainerData6(trdata.Files[index], trpoke.Files[index], config.ORAS);
                if (trainer.Team.Length == 0)
                    continue;
                if (options.RandomizeMoves || options.UseCurrentLearnsetMoves || options.MetronomeMoves || options.ForceHighPower)
                    trainer.Moves = true;
                if (options.RandomizeItems)
                    trainer.Item = true;
                if (options.RandomizeClasses)
                    trainer.Class = GetRandomTrainerClass(config, trainerClassCount, trainer.Class, trainer.BattleType, options);
                if (options.RandomizeComposition)
                    AdjustTeamSize(trainer, config, species, fallbackSpecies, options, teamRange.Min, teamRange.Max);
                if (options.FillImportantGen6Teams && importantGen6.Contains(index))
                    FillImportantTeam(trainer, config, species, fallbackSpecies, options);
                if (options.MaximizeAI)
                    trainer.AI |= 7;
                if (options.RandomizePrizes)
                    RandomizePrize(config, trainer, options.PrizeChance);
                var trainerTheme = options.RandomizeTypeThemes
                    ? GetGen6TrainerTheme(gen6Themes!, index, typeCount, config, options.GymEliteOnlyThemes, options.IncludeGymTrainerThemes)
                    : null;
                for (var pokemonIndex = 0; pokemonIndex < trainer.Team.Length; pokemonIndex++)
                {
                    var pokemon = trainer.Team[pokemonIndex];
                    if (pokemon.Species <= 0 || pokemon.Species >= speciesCount)
                        continue;
                    var storyMegaSlot = options.RandomizeSpecies && options.EnsureStoryMegaEvolutions
                        && IsStoryMegaTrainer(config, index) && pokemonIndex == trainer.Team.Length - 1;
                    var forcedStoryMega = false;
                    if (options.RandomizeSpecies)
                    {
                        if (storyMegaSlot)
                        {
                            var storyMega = GetRandomMega(config, megaOptions, options, trainerTheme)
                                ?? throw new WorkspaceException("No hay Megaevoluciones compatibles con las generaciones seleccionadas para garantizar las Megas de historia.");
                            pokemon.Species = (ushort)storyMega.Species;
                            pokemon.Item = storyMega.Item;
                            // The held-item field is controlled by a trainer-level format bit.
                            // Enable it so the forced Mega Stone is included in the exported team.
                            trainer.Item = true;
                            pokemon.Form = 0;
                            forcedStoryMega = true;
                        }
                        else
                        {
                            pokemon.Species = (ushort)(trainerTheme is int type
                                ? GetTrainerRandomSpeciesType(config, species, fallbackSpecies, pokemon.Species, type, options)
                                : GetTrainerRandomSpecies(config, species, fallbackSpecies, pokemon.Species, options));
                            pokemon.Form = (ushort)form.GetRandomForme(pokemon.Species);
                        }
                        pokemon.Gender = 0;
                    }
                    if (options.RandomizeLevels)
                        pokemon.Level = (ushort)ScaleLevel(pokemon.Level, options.LevelMultiplier);
                    if (options.MaximizeIVs)
                        pokemon.IVs = byte.MaxValue;
                    if (options.RandomizeItems && !forcedStoryMega && trainerItemPool.Length > 0)
                        pokemon.Item = (ushort)trainerItemPool[Util.Rand.Next(trainerItemPool.Length)];
                    if (options.RandomizeAbilities)
                        pokemon.Ability = Util.Rand.Next(1, 4);
                    ForceFinalEvolution(pokemon, options, finalSpecies, form);
                    if (options.RandomizeMoves)
                        pokemon.Moves = GetRandomTrainerMoves(config, move, fallbackMoves, pokemon.Species)
                            .Select(value => (ushort)value).ToArray();
                    else if (options.UseCurrentLearnsetMoves)
                        pokemon.Moves = learnset!.GetCurrentMoves(pokemon.Species, pokemon.Form, pokemon.Level, 4)
                            .Select(value => (ushort)value).ToArray();
                    else if (options.MetronomeMoves)
                        pokemon.Moves = [118, 0, 0, 0];
                    ApplyHighPowerMoves(pokemon, options, learnset);
                }
                trdata.SetFile(index, trainer.Write());
                trpoke.SetFile(index, trainer.WriteTeam());
                changed++;
                continue;
            }

            var trainer7 = new TrainerData7(trdata.Files[index], trpoke.Files[index]);
            if (trainer7.Pokemon.Count == 0)
                continue;
            if (options.RandomizeClasses)
                trainer7.TrainerClass = GetRandomTrainerClass(config, trainerClassCount, trainer7.TrainerClass, (int)trainer7.Mode, options);
            var trainer7Profile = GetAverageTeamProfile(
                config,
                trainer7.Pokemon.Select(pokemon => pokemon.Species),
                trainer7.Pokemon.Select(pokemon => pokemon.Level));
            if (options.RandomizeComposition)
                AdjustTeamSize(trainer7, config, species, fallbackSpecies, options, teamRange.Min, teamRange.Max, trainer7Profile);
            if (options.FillImportantGen7Teams && importantGen7.Contains(index))
                FillImportantTeam(trainer7, config, species, fallbackSpecies, options, trainer7Profile);
            if (IsBattleRoyalTrainer(index))
                trainer7.NumPokemon = 1;
            if (options.MaximizeAI)
                trainer7.AI |= (int)(TrainerAI.Basic | TrainerAI.Strong | TrainerAI.Expert | TrainerAI.PokeChange);
            foreach (var pokemon in trainer7.Pokemon)
            {
                if (pokemon.Species <= 0 || pokemon.Species >= speciesCount)
                    continue;
                var megaWasSelected = false;
                if (options.RandomizeSpecies)
                {
                    var megaItem = IsMegaStone(megaOptions, pokemon.Item);
                    var mega = megaItem ? GetRandomMega(config, megaOptions, options) : null;
                    if (mega is { } megaChoice)
                    {
                        pokemon.Species = megaChoice.Species;
                        pokemon.Item = megaChoice.Item;
                        pokemon.Form = 0;
                        megaWasSelected = true;
                    }
                    else
                    {
                        var theme = options.RandomizeTypeThemes
                            ? Util.Rand.Next(typeCount)
                            : (int?)null;
                        pokemon.Species = theme is int type
                            ? GetTrainerRandomSpeciesType(config, species, fallbackSpecies, pokemon.Species, type, options)
                            : GetTrainerRandomSpecies(config, species, fallbackSpecies, pokemon.Species, options);
                        pokemon.Form = form.GetRandomForme(pokemon.Species);
                    }
                    pokemon.Gender = 0;
                }
                if (options.RandomizeLevels)
                    pokemon.Level = ScaleLevel(pokemon.Level, options.LevelMultiplier);
                if (options.MaximizeIVs)
                    pokemon.IVs = [31, 31, 31, 31, 31, 31];
                if (options.RandomizeItems && !megaWasSelected && trainerItemPool.Length > 0)
                    pokemon.Item = trainerItemPool[Util.Rand.Next(trainerItemPool.Length)];
                if (options.RandomizeAbilities)
                    pokemon.Ability = Util.Rand.Next(0, 4);
                if (options.RandomizeNature)
                    pokemon.Nature = Util.Rand.Next(25);
                if (options.RandomizeShiny)
                    pokemon.Shiny = Util.Rand.Next(101) < Math.Clamp(options.ShinyChance, 0, 100);
                ForceFinalEvolution(pokemon, options, finalSpecies, form);
                if (options.RandomizeMoves)
                    pokemon.Moves = GetRandomTrainerMoves(config, move, fallbackMoves, pokemon.Species);
                else if (options.UseCurrentLearnsetMoves)
                    pokemon.Moves = learnset!.GetCurrentMoves(pokemon.Species, pokemon.Form, pokemon.Level, 4);
                else if (options.MetronomeMoves)
                    pokemon.Moves = [118, 0, 0, 0];
                ApplyHighPowerMoves(pokemon, options, learnset);
                if (move is not null && (options.UseCurrentLearnsetMoves || options.MetronomeMoves))
                {
                    var currentMoves = pokemon.Moves;
                    if (move.SanitizeMovesetForBannedMoves(currentMoves, pokemon.Species))
                        pokemon.Moves = currentMoves;
                }
            }
            trainer7.Write(out var data, out var team);
            trdata.SetFile(index, data);
            trpoke.SetFile(index, team);
            changed++;
        }

        if (changed == 0)
            throw new WorkspaceException("No se encontraron equipos de entrenadores editables.");
        trdata.Save();
        trpoke.Save();
    }

    private static void RandomizePrize(GameConfig config, TrainerData6 trainer, decimal chance)
    {
        if (Util.Rand.Next(100) >= Math.Clamp(chance, 0, 100))
        {
            trainer.Prize = 0;
            return;
        }

        var itemPool = config.ORAS ? Legal.Pouch_Items_AO : Legal.Pouch_Items_XY;
        var medicinePool = config.ORAS ? Legal.Pouch_Medicine_AO : Legal.Pouch_Medicine_XY;
        var pool = Util.Rand.Next(10) switch
        {
            < 2 => itemPool,
            < 5 => medicinePool,
            _ => Legal.Pouch_Berry_XY,
        };
        trainer.Prize = pool[Util.Rand.Next(pool.Length)];
    }

    private static int[] GetFinalTrainerSpecies(GameConfig config, int speciesCount, TrainerRandomizerOptions options)
    {
        IEnumerable<int> source = config.Generation == 7 ? Legal.FinalEvolutions_7 : Legal.FinalEvolutions_6;
        if (options.IncludeLegendary)
            source = source.Concat(config.Generation == 7
                ? config.USUM ? Legal.Legendary_USUM : Legal.Legendary_SM
                : Legal.Legendary_6);
        if (options.IncludeMythical)
            source = source.Concat(config.Generation == 7
                ? config.USUM ? Legal.Mythical_USUM : Legal.Mythical_SM
                : Legal.Mythical_6);
        return source.Where(species => species > 0 && species < speciesCount
                && IsTrainerSpeciesGenerationAllowed(species, config.Generation, options))
            .Distinct().ToArray();
    }

    private static int[] GetTrainerFallbackSpecies(GameConfig config, int speciesCount, TrainerRandomizerOptions options)
    {
        var excludedLegendary = options.IncludeLegendary
            ? Array.Empty<int>()
            : config.Generation == 6
                ? Legal.Legendary_6
                : config.USUM ? Legal.Legendary_USUM : Legal.Legendary_SM;
        var excludedMythical = options.IncludeMythical
            ? Array.Empty<int>()
            : config.Generation == 6
                ? Legal.Mythical_6
                : config.USUM ? Legal.Mythical_USUM : Legal.Mythical_SM;

        return GetFallbackSpecies(config, speciesCount)
            .Where(species => IsTrainerSpeciesGenerationAllowed(species, config.Generation, options))
            .Where(species => !excludedLegendary.Contains(species) && !excludedMythical.Contains(species))
            .Where(species => config.Generation == 6 || options.IncludeShedinja || species != 292)
            .ToArray();
    }

    private static bool IsTrainerSpeciesGenerationAllowed(int species, int gameGeneration, TrainerRandomizerOptions options) => species switch
    {
        <= 151 => options.AllowGen1,
        <= 251 => options.AllowGen2,
        <= 386 => options.AllowGen3,
        <= 493 => options.AllowGen4,
        <= 649 => options.AllowGen5,
        <= 721 => options.AllowGen6,
        _ => gameGeneration == 7 && options.AllowGen7,
    };

    private static (int Species, ushort[] Items)[] GetMegaOptions(GameConfig config) => config.Generation == 7 || config.ORAS
        ? [.. MegaOptionsXY, .. MegaOptionsORAS]
        : MegaOptionsXY;

    private static (int Species, ushort Item)? GetRandomMega(GameConfig config,
        (int Species, ushort[] Items)[] megaOptions, TrainerRandomizerOptions options, int? requiredType = null)
    {
        var candidates = megaOptions.Where(option => option.Species > 0 && option.Species < config.Personal.Table.Length
            && IsTrainerSpeciesGenerationAllowed(option.Species, config.Generation, options)).ToArray();
        if (requiredType is int type)
            candidates = candidates.Where(option => config.Personal.Table[option.Species].Types.Contains(type)).ToArray();
        if (candidates.Length == 0)
            return null;

        var selected = candidates[Util.Rand.Next(candidates.Length)];
        return (selected.Species, selected.Items[Util.Rand.Next(selected.Items.Length)]);
    }

    private static bool IsMegaStone((int Species, ushort[] Items)[] megaOptions, int item) =>
        megaOptions.Any(option => option.Items.Contains((ushort)item));

    private static int[] GetTrainerItemPool(GameConfig config, int itemCount)
    {
        IEnumerable<ushort> items = config.Generation == 6
            ? (config.ORAS ? Legal.Pouch_Items_AO : Legal.Pouch_Items_XY)
                .Concat(Legal.Pouch_Berry_XY)
            : Legal.TrainerRandomizerItems_7;
        return items.Where(item => item > 0 && item < itemCount).Select(item => (int)item).ToArray();
    }

    private static HashSet<int> GetImportantGen6TrainerIDs(GameConfig config)
    {
        var groups = config.ORAS ? OrasTrainerThemeGroups : XyTrainerThemeGroups;
        var notImportant = config.ORAS
            ? new HashSet<int> { 1, 2, 3, 4, 5, 6 }
            : new HashSet<int> { 1, 2, 3, 4, 5, 6, 137, 138, 139, 188 };
        return groups.SelectMany(group => group.IDs)
            .Where(id => !notImportant.Contains(id))
            .ToHashSet();
    }

    private static bool IsStoryMegaTrainer(GameConfig config, int trainerIndex) => config.ORAS
        ? OrasStoryMegaTrainerIDs.Contains(trainerIndex)
        : XyStoryMegaTrainerIDs.Contains(trainerIndex);

    private static bool IsBattleRoyalTrainer(int trainerIndex) => trainerIndex is 81 or 82 or 83 or 84 or 185;

    private static (int Species, int Level) GetAverageTeamProfile(GameConfig config, IEnumerable<int> teamSpecies, IEnumerable<int> teamLevels)
    {
        var table = config.Personal.Table;
        var levels = teamLevels.ToArray();
        var validSpecies = teamSpecies.Where(species => species > 0 && species < table.Length).ToArray();
        var averageLevel = levels.Length > 0 ? (int)levels.Average() : 1;
        if (validSpecies.Length == 0)
            return (table.Length > 1 ? 1 : 0, Math.Clamp(averageLevel, 1, 100));

        var averageBst = (int)validSpecies.Average(species => table[species].BST);
        var speciesLimit = Math.Min(config.MaxSpeciesID, table.Length - 1);
        if (speciesLimit < 1)
            return (validSpecies[0], Math.Clamp(averageLevel, 1, 100));
        var averageSpecies = Enumerable.Range(1, Math.Max(1, speciesLimit))
            .MinBy(species => Math.Abs(table[species].BST - averageBst));
        return (averageSpecies, Math.Clamp(averageLevel, 1, 100));
    }

    private static void AdjustTeamSize(TrainerData6 trainer, GameConfig config, SpeciesRandomizer species, int[] fallbackSpecies,
        TrainerRandomizerOptions options, int minimum, int maximum)
    {
        if (trainer.Team.Length == 0 || trainer.BattleType == 4)
            return;

        var count = Math.Clamp(trainer.Team.Length, minimum, maximum);
        if (count == trainer.Team.Length)
            return;

        var profile = GetAverageTeamProfile(config, trainer.Team.Select(pokemon => (int)pokemon.Species), trainer.Team.Select(pokemon => (int)pokemon.Level));
        var team = trainer.Team.Take(count).ToList();
        while (team.Count < count)
        {
            var source = team[^1];
            var pokemon = new TrainerData6.Pokemon(source.Write(trainer.Item, trainer.Moves), trainer.Item, trainer.Moves)
            {
                Species = (ushort)GetTrainerRandomSpecies(config, species, fallbackSpecies, profile.Species, options),
                Level = (ushort)profile.Level,
            };
            team.Add(pokemon);
        }
        trainer.Team = team.ToArray();
        trainer.NumPokemon = (byte)count;
    }

    private static void AdjustTeamSize(TrainerData7 trainer, GameConfig config, SpeciesRandomizer species, int[] fallbackSpecies,
        TrainerRandomizerOptions options, int minimum, int maximum, (int Species, int Level) profile)
    {
        if (trainer.Pokemon.Count == 0)
            return;

        var count = Math.Clamp(trainer.Pokemon.Count, minimum, maximum);
        if (count == trainer.Pokemon.Count)
            return;

        if (count < trainer.Pokemon.Count)
            trainer.Pokemon.RemoveRange(count, trainer.Pokemon.Count - count);
        while (trainer.Pokemon.Count < count)
        {
            var pokemon = new TrainerPoke7
            {
                Species = GetTrainerRandomSpecies(config, species, fallbackSpecies, profile.Species, options),
                Level = profile.Level,
            };
            trainer.Pokemon.Add(pokemon);
        }
        trainer.NumPokemon = count;
    }

    private static void FillImportantTeam(TrainerData6 trainer, GameConfig config, SpeciesRandomizer species, int[] fallbackSpecies,
        TrainerRandomizerOptions options)
    {
        if (trainer.Team.Length == 0 || trainer.Team.Length >= 6)
            return;

        var profile = GetAverageTeamProfile(config, trainer.Team.Select(pokemon => (int)pokemon.Species), trainer.Team.Select(pokemon => (int)pokemon.Level));
        var team = trainer.Team.ToList();
        while (team.Count < 6)
        {
            var source = team[^1];
            team.Add(new TrainerData6.Pokemon(source.Write(trainer.Item, trainer.Moves), trainer.Item, trainer.Moves)
            {
                Species = (ushort)GetTrainerRandomSpecies(config, species, fallbackSpecies, profile.Species, options),
                Level = (ushort)profile.Level,
            });
        }
        trainer.Team = team.ToArray();
        trainer.NumPokemon = 6;
    }

    private static void ForceFinalEvolution(TrainerData6.Pokemon pokemon, TrainerRandomizerOptions options, int[] finalSpecies, FormRandomizer form)
    {
        if (!options.ForceFullyEvolved || pokemon.Level < ClampTrainerLevel(options.FullyEvolvedLevel) || finalSpecies.Length == 0 || finalSpecies.Contains(pokemon.Species))
            return;

        pokemon.Species = (ushort)finalSpecies[Util.Rand.Next(finalSpecies.Length)];
        pokemon.Form = (ushort)form.GetRandomForme(pokemon.Species);
    }

    private static void ForceFinalEvolution(TrainerPoke7 pokemon, TrainerRandomizerOptions options, int[] finalSpecies, FormRandomizer form)
    {
        if (!options.ForceFullyEvolved || pokemon.Level < ClampTrainerLevel(options.FullyEvolvedLevel) || finalSpecies.Length == 0 || finalSpecies.Contains(pokemon.Species))
            return;

        pokemon.Species = finalSpecies[Util.Rand.Next(finalSpecies.Length)];
        pokemon.Form = form.GetRandomForme(pokemon.Species);
    }

    private static int ClampTrainerLevel(decimal level) => (int)Math.Clamp(level, 1, 100);

    private static void ApplyHighPowerMoves(TrainerData6.Pokemon pokemon, TrainerRandomizerOptions options, LearnsetRandomizer? learnset)
    {
        if (!options.ForceHighPower || learnset is null || pokemon.Level < ClampTrainerLevel(options.HighPowerLevel))
            return;

        pokemon.Moves = learnset.GetHighPoweredMoves(pokemon.Species, pokemon.Form, 4)
            .Select(move => (ushort)move).ToArray();
    }

    private static void ApplyHighPowerMoves(TrainerPoke7 pokemon, TrainerRandomizerOptions options, LearnsetRandomizer? learnset)
    {
        if (!options.ForceHighPower || learnset is null || pokemon.Level < ClampTrainerLevel(options.HighPowerLevel))
            return;

        pokemon.Moves = learnset.GetHighPoweredMoves(pokemon.Species, pokemon.Form, 4);
    }

    private static void FillImportantTeam(TrainerData7 trainer, GameConfig config, SpeciesRandomizer species, int[] fallbackSpecies,
        TrainerRandomizerOptions options, (int Species, int Level) profile)
    {
        if (trainer.Pokemon.Count == 0)
            return;

        while (trainer.Pokemon.Count < 6)
        {
            var pokemon = new TrainerPoke7
            {
                Species = GetTrainerRandomSpecies(config, species, fallbackSpecies, profile.Species, options),
                Level = profile.Level,
            };
            trainer.Pokemon.Add(pokemon);
        }
        trainer.NumPokemon = 6;
    }

    private static (int Min, int Max) NormalizeTeamRange(int minimum, int maximum)
    {
        var min = Math.Clamp(minimum, 1, 6);
        var max = Math.Clamp(maximum, min, 6);
        return (min, max);
    }

    private static int GetRandomTrainerClass(GameConfig config, int classCount, int original, int battleType, TrainerRandomizerOptions options)
    {
        if (classCount <= 0)
            return original;

        var classes = config.GetText(TextName.TrainerClasses);
        var special = config.Generation == 6
            ? config.ORAS ? Legal.SpecialClasses_ORAS : Legal.SpecialClasses_XY
            : config.USUM ? Legal.SpecialClasses_USUM : Legal.SpecialClasses_SM;
        if (options.IgnoreSpecialClasses && special.Contains(original))
            return original;

        if (config.Generation == 6)
        {
            var modelClasses = config.ORAS ? Legal.Model_AO : Legal.Model_XY;
            if (modelClasses.Contains(original) && (!options.IgnoreSpecialClasses || !special.Contains(original)))
            {
                var modelCandidates = modelClasses.Where(index => index >= 0 && index < classCount).ToArray();
                return modelCandidates.Length == 0 ? original : modelCandidates[Util.Rand.Next(modelCandidates.Length)];
            }
            if (options.OnlySinglesForClasses && battleType != 0)
                return original;
        }

        var candidates = Enumerable.Range(0, classCount)
            .Where(index => index < classes.Length && !classes[index].StartsWith("[~", StringComparison.Ordinal))
            .Where(index => !options.IgnoreSpecialClasses || !special.Contains(index))
            .Where(index => config.Generation != 7 || index != 82)
            .Where(index => config.Generation != 6 || !(config.ORAS && index <= 63) && index is not (>= 68 and <= 126))
            .ToArray();
        if (candidates.Length == 0)
            return original;
        return candidates[Util.Rand.Next(candidates.Length)];
    }

    private static void RandomizeWild(GameConfig config, WildRandomizerOptions options)
    {
        var species = new SpeciesRandomizer(config)
        {
            G1 = true,
            G2 = true,
            G3 = true,
            G4 = true,
            G5 = true,
            G6 = true,
            G7 = config.Generation == 7,
            L = options.IncludeLegendary,
            E = options.IncludeMythical,
            Shedinja = false,
            rBST = options.MatchBst,
        };
        species.Initialize();

        if (config.Generation == 6)
        {
            RandomizeWildGen6(config, options, species);
            return;
        }

        if (config.Generation == 7)
        {
            RandomizeWildGen7(config, options, species);
            return;
        }

        throw new WorkspaceException("Los encuentros salvajes solo están implementados para juegos de las generaciones VI y VII.");
    }

    private static void RandomizeWildGen6(GameConfig config, WildRandomizerOptions options, SpeciesRandomizer species)
    {
        var garc = new WildGen6Archive(config);
        var form = new FormRandomizer(config) { AllowAlolanForm = false };
        var speciesCount = Catalogs.SpeciesCount(config);
        var fallbackSpecies = GetFallbackSpecies(config, speciesCount);
        var changedAreas = 0;

        for (var fileIndex = garc.FirstMapFile; fileIndex < garc.FirstMapFile + garc.AreaCount; fileIndex++)
        {
            var file = garc.GetFile(fileIndex);
            if (!WildGen6Editor.TryGetEncounterOffset(file, config.ORAS, out var offset))
                continue;

            var slots = WildGen6Editor.ReadSlots(file, offset, WildGen6Editor.GetSlotCount(config.ORAS));
            var changed = false;
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var slot = slots[slotIndex];
                if (slot.Species <= 0 || slot.Species >= speciesCount)
                    continue;

                if (options.RandomizeSpecies)
                {
                    var newSpecies = GetRandomSpecies(config, species, fallbackSpecies, slot.Species);
                    var newForm = newSpecies < config.Personal.Table.Length
                        ? form.GetRandomForme(newSpecies)
                        : 0;
                    slot = slot with { Species = newSpecies, Form = newForm };
                    changed = true;
                }

                if (options.RandomizeLevels && slot.MaxLevel > 1)
                {
                    var level = ScaleLevel(slot.MaxLevel, options.LevelMultiplier);
                    slot = slot with { MinLevel = level, MaxLevel = level };
                    changed = true;
                }

                slots[slotIndex] = slot;
            }

            if (options.HomogeneousHordes && options.RandomizeSpecies)
                HomogenizeHordes(slots);

            if (!changed)
                continue;

            WildGen6Editor.WriteSlots(file, offset, slots);
            garc.SetFile(fileIndex, file);
            changedAreas++;

            // OR/AS has a packed copy of every area's encounter slots in file 537.
            if (config.ORAS)
            {
                var packed = garc.GetFile(garc.PackedDataFileIndex);
                var locationIndex = fileIndex - garc.FirstMapFile;
                var slotBytes = WildGen6Editor.GetSlotCount(oras: true) * 4;
                var packedOffset = WildGen6Editor.GetPackedSlotOffset(packed, locationIndex, slotBytes);
                WildGen6Editor.WriteSlots(packed, packedOffset, slots);
                garc.SetFile(garc.PackedDataFileIndex, packed);
            }
        }

        if (changedAreas == 0)
            throw new WorkspaceException("No se encontraron tablas de encuentros salvajes editables en encdata.");
        garc.Save();
    }

    private static void RandomizeWildGen7(GameConfig config, WildRandomizerOptions options, SpeciesRandomizer species)
    {
        var encdata = config.GetlzGARCData("encdata");
        var zones = config.GetlzGARCData("zonedata");
        var worlds = config.GetlzGARCData("worlddata");
        var areas = Area7.GetArray(encdata, zones, worlds, config.GetText(TextName.metlist_000000));
        var form = new FormRandomizer(config);
        var fallbackSpecies = GetFallbackSpecies(config, Catalogs.SpeciesCount(config));
        var changedAreas = 0;

        foreach (var area in areas.Where(area => area.HasTables))
        {
            var changed = false;
            foreach (var table in area.Tables)
            {
                if (options.RandomizeLevels)
                {
                    table.MinLevel = ScaleLevel(table.MinLevel, options.LevelMultiplier);
                    table.MaxLevel = ScaleLevel(table.MaxLevel, options.LevelMultiplier);
                    if (table.MinLevel > table.MaxLevel)
                        (table.MinLevel, table.MaxLevel) = (table.MaxLevel, table.MinLevel);
                    changed = true;
                }

                if (options.RandomizeSpecies)
                {
                    foreach (var encounterSet in table.Encounter7s)
                    foreach (var encounter in encounterSet)
                    {
                        if (encounter.Species == 0)
                            continue;
                        encounter.Species = (uint)GetRandomSpecies(config, species, fallbackSpecies, (int)encounter.Species);
                        encounter.Forme = (uint)form.GetRandomForme((int)encounter.Species);
                        changed = true;
                    }
                }

                if (changed)
                    table.Write();
            }

            if (!changed)
                continue;

            encdata[area.FileNumber] = Area7.GetDayNightTableBinary(area.Tables);
            changedAreas++;
        }

        if (changedAreas == 0)
            throw new WorkspaceException("No se encontraron tablas de encuentros salvajes editables en encdata.");
        encdata.Save();
    }

    private static int GetRandomSpecies(GameConfig config, SpeciesRandomizer randomizer, int[] fallback, int oldSpecies)
    {
        if (oldSpecies <= 0 || oldSpecies >= config.Personal.Table.Length)
            return oldSpecies;
        if (config.Personal.Table.Length <= config.MaxSpeciesID)
        {
            // Synthetic and partial workspaces do not contain the complete national dex used by
            // SpeciesRandomizer. Keep those fixtures useful while real dumps use the Windows list.
            if (fallback.Length == 0)
                return oldSpecies;
            var replacement = oldSpecies;
            for (var attempt = 0; attempt < 32 && replacement == oldSpecies; attempt++)
                replacement = fallback[Util.Rand.Next(fallback.Length)];
            return replacement;
        }
        return randomizer.GetRandomSpecies(oldSpecies);
    }

    private static int GetTrainerRandomSpecies(GameConfig config, SpeciesRandomizer randomizer, int[] fallback, int oldSpecies,
        TrainerRandomizerOptions options)
    {
        if (oldSpecies <= 0 || oldSpecies >= config.Personal.Table.Length)
            return oldSpecies;
        if (config.Personal.Table.Length > config.MaxSpeciesID)
            return randomizer.GetRandomSpecies(oldSpecies);

        IEnumerable<int> candidates = fallback;
        if (options.MatchBst)
        {
            var oldBst = config.Personal.Table[oldSpecies].BST;
            var minimumBst = oldBst * 10 / 11;
            var maximumBst = oldBst * 11 / 10;
            var similarBst = fallback.Where(species =>
            {
                var bst = config.Personal.Table[species].BST;
                return bst >= minimumBst && bst <= maximumBst;
            }).ToArray();
            if (similarBst.Length > 0)
                candidates = similarBst;
        }
        return PickTrainerSpecies(candidates, oldSpecies);
    }

    private static int GetTrainerRandomSpeciesType(GameConfig config, SpeciesRandomizer randomizer, int[] fallback, int oldSpecies,
        int type, TrainerRandomizerOptions options)
    {
        if (oldSpecies <= 0 || oldSpecies >= config.Personal.Table.Length)
            return oldSpecies;
        if (config.Personal.Table.Length > config.MaxSpeciesID)
        {
            var preserveOriginalType = randomizer.rType;
            randomizer.rType = config.Generation == 6;
            try
            {
                return randomizer.GetRandomSpeciesType(oldSpecies, type);
            }
            finally
            {
                randomizer.rType = preserveOriginalType;
            }
        }

        var originalTypes = config.Personal.Table[oldSpecies].Types;
        var typeCompatibleCandidates = config.Generation == 6
            ? fallback.Where(species => originalTypes.Any(originalType => config.Personal.Table[species].Types.Contains(originalType))).ToArray()
            : fallback;
        if (typeCompatibleCandidates.Length == 0)
            typeCompatibleCandidates = fallback;

        var themedCandidates = typeCompatibleCandidates
            .Where(species => config.Personal.Table[species].Types.Contains(type))
            .ToArray();
        IEnumerable<int> candidates = themedCandidates.Length > 0 ? themedCandidates : typeCompatibleCandidates;
        if (options.MatchBst)
        {
            var oldBst = config.Personal.Table[oldSpecies].BST;
            var minimumBst = oldBst * 10 / 11;
            var maximumBst = oldBst * 11 / 10;
            var similarBst = candidates.Where(species =>
            {
                var bst = config.Personal.Table[species].BST;
                return bst >= minimumBst && bst <= maximumBst;
            }).ToArray();
            if (similarBst.Length > 0)
                candidates = similarBst;
        }
        return PickTrainerSpecies(candidates, oldSpecies);
    }

    private static int PickTrainerSpecies(IEnumerable<int> candidates, int oldSpecies)
    {
        var pool = candidates.ToArray();
        if (pool.Length == 0)
            return oldSpecies;
        var alternatives = pool.Where(species => species != oldSpecies).ToArray();
        var selection = alternatives.Length > 0 ? alternatives : pool;
        return selection[Util.Rand.Next(selection.Length)];
    }

    private static Dictionary<int, int> BuildGen6TrainerThemes(GameConfig config, int typeCount, bool includeGymTrainers,
        TrainerRandomizerOptions options, (int Species, ushort[] Items)[] megaOptions)
    {
        var map = new Dictionary<int, int>();
        var groupTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = config.ORAS ? OrasTrainerThemeGroups : XyTrainerThemeGroups;
        if (includeGymTrainers)
            groups = [.. groups, .. (config.ORAS ? OrasGymTrainerThemeGroups : XyGymTrainerThemeGroups)];
        var megaTypes = options.EnsureStoryMegaEvolutions
            ? megaOptions
                .Where(option => option.Species > 0 && option.Species < config.Personal.Table.Length
                    && IsTrainerSpeciesGenerationAllowed(option.Species, config.Generation, options))
                .SelectMany(option => config.Personal.Table[option.Species].Types)
                .Where(type => type >= 0 && type < typeCount)
                .Distinct()
                .ToArray()
            : Array.Empty<int>();
        var gymEliteGroups = groups
            .Select(group => group.Name)
            .Where(IsGen6GymEliteThemeName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var availableGymEliteTypes = options.EnsureStoryMegaEvolutions
            ? megaTypes.ToList()
            : Enumerable.Range(0, typeCount).ToList();
        if (availableGymEliteTypes.Count < gymEliteGroups.Length)
        {
            throw new WorkspaceException(
                $"Hay {availableGymEliteTypes.Count} tipo(s) elegible(s), pero se necesitan {gymEliteGroups.Length} distintos para conservar temas únicos en gimnasios y Alto Mando.");
        }

        foreach (var (name, ids) in groups)
        {
            if (!groupTypes.TryGetValue(name, out var type))
            {
                if (IsGen6GymEliteThemeName(name))
                {
                    var poolIndex = Util.Rand.Next(availableGymEliteTypes.Count);
                    type = availableGymEliteTypes[poolIndex];
                    availableGymEliteTypes.RemoveAt(poolIndex);
                }
                else if (options.EnsureStoryMegaEvolutions && ids.Any(id => IsStoryMegaTrainer(config, id)))
                {
                    if (megaTypes.Length == 0)
                        throw new WorkspaceException("No hay tipos con Mega-Evoluciones elegibles para los equipos de historia seleccionados.");
                    type = megaTypes[Util.Rand.Next(megaTypes.Length)];
                }
                else
                {
                    type = Util.Rand.Next(typeCount);
                }
                groupTypes.Add(name, type);
            }
            foreach (var id in ids)
                map[id] = type;
        }
        return map;
    }

    private static int? GetGen6TrainerTheme(IReadOnlyDictionary<int, int> themes, int trainerIndex, int typeCount,
        GameConfig config, bool gymEliteOnly, bool includeGymTrainers)
    {
        if (gymEliteOnly && !IsGen6GymEliteTrainer(config, trainerIndex))
            return null;
        return themes.TryGetValue(trainerIndex, out var type) ? type : Util.Rand.Next(typeCount);
    }

    private static bool IsGen6GymEliteTrainer(GameConfig config, int trainerIndex)
    {
        var groups = config.ORAS ? OrasTrainerThemeGroups : XyTrainerThemeGroups;
        return groups.Any(group => IsGen6GymEliteThemeName(group.Name) && group.IDs.Contains(trainerIndex));
    }

    private static bool IsGen6GymEliteThemeName(string name) =>
        name.StartsWith("GYM", StringComparison.Ordinal)
        || name.StartsWith("ELITE", StringComparison.Ordinal)
        || name == "CHAMPION";

    private static readonly (string Name, int[] IDs)[] XyTrainerThemeGroups =
    [
        ("RIVAL1", [130, 184, 329, 332, 335, 338, 341, 435, 519, 604, 575, 578, 581, 584, 587, 590, 593, 596, 599, 607]),
        ("RIVAL2", [131, 185, 330, 333, 336, 339, 342, 436, 520, 605, 576, 579, 582, 585, 588, 591, 594, 597, 600, 608]),
        ("RIVAL3", [132, 186, 331, 334, 337, 340, 343, 437, 521, 606, 577, 580, 583, 586, 589, 592, 595, 598, 601, 609]),
        ("FLAREBOSS", [303, 525, 526]),
        ("FLARE1", [175, 344]), ("FLARE2", [350, 351]), ("FLARE3", [348, 349]),
        ("FLARE4", [346, 347]), ("FLARE5", [345]),
        ("GYM1", [6, 254, 262]), ("GYM2", [76, 261, 279]), ("GYM3", [21, 255, 263, 613, 188]),
        ("GYM4", [22, 256, 264]), ("GYM5", [23, 257, 265]), ("GYM6", [24, 258, 266]),
        ("GYM7", [25, 259, 267]), ("GYM8", [26, 260, 268]),
        ("ELITE1", [269, 273, 507]), ("ELITE2", [271, 275]), ("ELITE3", [187, 272]),
        ("ELITE4", [270, 274]), ("CHAMPION", [276, 277]),
        ("SHAUNA", [321, 322, 323, 137, 138, 139]), ("TREVOR", [325, 439]),
        ("TIERNO", [324, 438, 573]), ("PROFESSOR", [327, 328]), ("ESSENTIA", [503, 504, 505, 511, 512, 513, 514, 515]),
        ("AZ", [602]), ("CHATELAINE1", [559]), ("CHATELAINE2", [560]), ("CHATELAINE3", [561]), ("CHATELAINE4", [562]),
    ];

    private static readonly (string Name, int[] IDs)[] OrasTrainerThemeGroups =
    [
        ("RIVAL1", [1, 4, 289, 292, 295, 298, 527, 530, 674, 677, 699, 906]),
        ("RIVAL2", [2, 5, 290, 293, 296, 299, 528, 531, 675, 678, 700, 907]),
        ("RIVAL3", [3, 6, 291, 294, 297, 300, 529, 532, 676, 679, 701, 908]),
        ("AQUA1", [178, 231, 266]), ("AQUA2", [683, 684, 685, 686, 687]), ("AQUA3", [688, 689, 690]),
        ("MAGMA1", [235, 236, 271]), ("MAGMA2", [694, 695, 696, 697, 698]), ("MAGMA3", [691, 692, 693]),
        ("GYM1", [561]), ("GYM2", [563]), ("GYM3", [567]), ("GYM4", [569]), ("GYM5", [570]),
        ("GYM6", [571]), ("GYM7", [552]), ("GYM8", [572, 943]),
        ("ELITE1", [553, 909]), ("ELITE2", [554, 910]), ("ELITE3", [555, 911]),
        ("ELITE4", [556, 912]), ("CHAMPION", [557, 680, 913, 942]),
        ("WALLY", [518, 583, 944, 945, 946, 947]), ("LOREKEEPER", [713, 898]), ("EXPERT", [856, 857]),
    ];

    private static readonly (string Name, int[] IDs)[] XyGymTrainerThemeGroups =
    [
        ("GYM1", [39, 40, 48]), ("GYM2", [64, 63, 106, 105]),
        ("GYM3", [83, 147, 84, 146]), ("GYM4", [123, 121, 124, 122]),
        ("GYM5", [461, 462, 463, 464, 465, 466, 28, 29, 30, 467, 468, 469]),
        ("GYM6", [245, 250, 248, 243]), ("GYM7", [170, 171, 172, 365, 366]),
        ("GYM8", [169, 32, 168, 31]),
    ];

    private static readonly (string Name, int[] IDs)[] OrasGymTrainerThemeGroups =
    [
        ("GYM1", [562, 22, 667]), ("GYM2", [60, 56, 59]),
        ("GYM3", [34, 568, 614, 35]), ("GYM4", [81, 824, 83, 615, 823, 613, 85]),
        ("GYM5", [63, 67, 64, 68, 65, 69, 66]), ("GYM6", [115, 517, 516, 118, 730]),
        ("GYM7", [157, 226, 320, 159, 225, 158]),
        ("GYM8", [647, 342, 594, 646, 338, 339, 340, 341]),
    ];

    private static int[] GetFallbackSpecies(GameConfig config, int speciesCount) =>
        Enumerable.Range(1, Math.Max(0, Math.Min(speciesCount - 1, config.Personal.Table.Length - 1))).ToArray();

    private static int[] GetFallbackMoves(GameConfig config) =>
        Enumerable.Range(1, Math.Max(0, Math.Min(config.Moves.Length - 1, Catalogs.MoveCount(config) - 1))).ToArray();

    private static int[] GetRandomTrainerMoves(GameConfig config, MoveRandomizer? randomizer, int[] fallback, int species)
    {
        if (randomizer is not null)
            return randomizer.GetRandomMoveset(species, 4);
        if (fallback.Length == 0)
            return [0, 0, 0, 0];
        if (fallback.Length >= 4)
            return fallback.OrderBy(_ => Util.Rand.Next()).Take(4).ToArray();
        return Enumerable.Range(0, 4).Select(_ => fallback[Util.Rand.Next(fallback.Length)]).ToArray();
    }

    private static int ScaleLevel(int level, decimal multiplier) =>
        Math.Clamp((int)(level * Math.Clamp(multiplier, 0.01m, 10m)), 1, 100);

    private static void HomogenizeHordes(WildGen6Slot[] slots)
    {
        const int hordeStart = 46;
        const int hordeSize = 5;
        for (var index = 0; index < 15 && hordeStart + index < slots.Length; index++)
        {
            var source = slots[hordeStart + (index % hordeSize)];
            var target = slots[hordeStart + index];
            if (source.Species <= 0 || target.Species <= 0)
                continue;
            slots[hordeStart + index] = target with { Species = source.Species, Form = source.Form };
        }
    }
}
