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
    // Struggle (165) and Shadow Blast (621) are never valid randomizer output.
    // A fresh array per read: the randomizers take ownership of what they are handed.
    private static int[] AlwaysBannedMoves => [165, 621];

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

        return EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "randomizer", wildGarcs.Concat(trainerGarcs), config =>
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
                    RandomizeLearnsets(config, learnsets);
                    GarcWriter.SaveLearnsets(config);
                    changed.Add(config.GetGARCFileName("levelup"));
                }

                if (eggMoves.Enabled)
                {
                    RandomizeEggMoves(config, eggMoves);
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
            ModifyLearnsetMoveTutors = config.ORAS && options.RandomizeMoveTutors,
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
                // Windows keeps HMs (bits after index 100) for story progression.
                for (var i = 0; i < Math.Min(101, species.TMHM.Length); i++)
                    species.TMHM[i] = false;
                Array.Fill(species.TypeTutors, false);
                foreach (var tutorSet in species.SpecialTutors)
                    Array.Fill(tutorSet, false);
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
                Array.Fill(species.TypeTutors, true);
        }
    }

    private static void RandomizeLearnsets(GameConfig config, LearnsetOptions options)
    {
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
                ? [.. AlwaysBannedMoves, .. MoveRandomizer.FixedDamageMoves]
                : AlwaysBannedMoves,
        }.Execute();
    }

    private static void RandomizeEggMoves(GameConfig config, EggMoveOptions options)
    {
        var garc = config.GetGARCData("eggmove");
        EggMoves[] sets = config.Generation == 6
            ? EggMoves6.GetArray(garc.Files)
            : EggMoves7.GetArray(garc.Files);
        // Gen VII additionally bans Celebrate (464) and the Z-Moves, which cannot be inherited.
        int[] banned = config.Generation == 7
            ? [.. AlwaysBannedMoves, 464, .. Legal.Z_Moves]
            : AlwaysBannedMoves;

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
            config.Moves[moveId].PP = moveId switch { 117 => 40, 32 => 1, _ => 0 };
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
        if (options.AllowMegaForms && config.Generation != 6)
            throw new WorkspaceException("Las formas Mega aleatorias solo están disponibles en el flujo de entrenadores de Gen. VI.");
        if (options.AllowMegaForms && !options.RandomizeSpecies)
            throw new WorkspaceException("Las formas Mega aleatorias requieren activar la randomización de especies.");
        if (options.IncludeGymTrainerThemes && config.Generation != 6)
            throw new WorkspaceException("Los temas de entrenadores de gimnasio solo están disponibles en el flujo de entrenadores de Gen. VI.");
        if (options.IncludeGymTrainerThemes && !options.RandomizeTypeThemes)
            throw new WorkspaceException("Los temas de entrenadores de gimnasio requieren activar los temas por tipo.");
        var moveModes = Convert.ToInt32(options.RandomizeMoves) + Convert.ToInt32(options.UseCurrentLearnsetMoves)
            + Convert.ToInt32(options.MetronomeMoves);
        if (moveModes > 1)
            throw new WorkspaceException("Elegí un único modo de movimientos para los entrenadores.");
        if ((options.UseCurrentLearnsetMoves || options.ForceHighPower) && config.Learnsets is null)
            throw new WorkspaceException("Los movimientos por nivel requieren el GARC levelup del workspace.");

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
            Shedinja = options.IncludeShedinja,
            rBST = options.MatchBst,
        };
        species.Initialize();

        var speciesCount = Catalogs.SpeciesCount(config);
        var fallbackSpecies = GetFallbackSpecies(config, speciesCount);
        var trainerClassCount = Catalogs.TrainerClasses(config).Length;
        var itemCount = Catalogs.ItemCount(config);
        var trainerItemPool = config.Info.HeldItems
            .Where(item => item > 0 && item < itemCount)
            .Distinct()
            .ToArray();
        var form = new FormRandomizer(config)
        {
            AllowMega = options.AllowMegaForms,
            AllowAlolanForm = config.Generation == 7,
        };
        var finalSpecies = GetFinalTrainerSpecies(config, speciesCount);
        var teamRange = NormalizeTeamRange(options.MinTeamSize, options.MaxTeamSize);
        var importantGen7 = config.USUM ? Legal.ImportantTrainers_USUM : Legal.ImportantTrainers_SM;
        var typeCount = Math.Clamp(config.GetText(TextName.Types).Length, 1, 18);
        var gen6Themes = options.RandomizeTypeThemes && config.Generation == 6
            ? BuildGen6TrainerThemes(config, typeCount, options.IncludeGymTrainerThemes)
            : null;
        var learnset = options.ForceHighPower || options.UseCurrentLearnsetMoves
            ? config.Learnsets is null
                ? throw new WorkspaceException("Los movimientos por nivel requieren el GARC levelup del workspace.")
                : new LearnsetRandomizer(config, config.Learnsets)
            : null;
        var fallbackMoves = GetFallbackMoves(config);
        var move = config.Moves.Length > config.Info.MaxMoveID
            ? new MoveRandomizer(config)
            {
                rDMG = true,
                rDMGCount = 2,
                rSTAB = true,
                rSTABCount = 2,
                BannedMoves = [165, 621, .. MoveRandomizer.FixedDamageMoves],
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
                if (options.RandomizeMoves || options.UseCurrentLearnsetMoves || options.MetronomeMoves || options.ForceHighPower)
                    trainer.Moves = true;
                if (options.RandomizeClasses)
                    trainer.Class = GetRandomTrainerClass(config, trainerClassCount, trainer.Class, trainer.BattleType, options);
                if (options.RandomizeComposition)
                    RandomizeComposition(trainer, teamRange.Min, teamRange.Max);
                if (options.MaximizeAI)
                    trainer.AI |= 7;
                if (options.RandomizePrizes)
                    RandomizePrize(config, trainer, options.PrizeChance);
                foreach (var pokemon in trainer.Team)
                {
                    if (pokemon.Species <= 0 || pokemon.Species >= speciesCount)
                        continue;
                    if (options.RandomizeSpecies)
                    {
                int? theme = options.RandomizeTypeThemes
                    ? GetGen6TrainerTheme(gen6Themes!, index, typeCount)
                    : null;
                        pokemon.Species = (ushort)(theme is int type
                            ? GetRandomSpeciesType(config, species, fallbackSpecies, pokemon.Species, type)
                            : GetRandomSpecies(config, species, fallbackSpecies, pokemon.Species));
                        pokemon.Form = (ushort)form.GetRandomForme(pokemon.Species);
                        pokemon.Gender = 0;
                    }
                    if (options.RandomizeLevels)
                        pokemon.Level = (ushort)ScaleLevel(pokemon.Level, options.LevelMultiplier);
                    if (options.MaximizeIVs)
                        pokemon.IVs = byte.MaxValue;
                    if (options.RandomizeItems && trainerItemPool.Length > 0)
                        pokemon.Item = trainerItemPool[Util.Rand.Next(trainerItemPool.Length)];
                    if (options.RandomizeAbilities)
                        pokemon.Ability = Util.Rand.Next(1, 4);
                    if (options.RandomizeMoves)
                        pokemon.Moves = GetRandomTrainerMoves(config, move, fallbackMoves, pokemon.Species, pokemon.Form)
                            .Select(value => (ushort)value).ToArray();
                    else if (options.UseCurrentLearnsetMoves)
                        pokemon.Moves = learnset!.GetCurrentMoves(pokemon.Species, pokemon.Form, pokemon.Level, 4)
                            .Select(value => (ushort)value).ToArray();
                    else if (options.MetronomeMoves)
                        pokemon.Moves = [118, 0, 0, 0];
                    ApplyHighPowerMoves(pokemon, options, learnset);
                    ForceFinalEvolution(pokemon, options, finalSpecies, form);
                }
                trdata.SetFile(index, trainer.Write());
                trpoke.SetFile(index, trainer.WriteTeam());
                changed++;
                continue;
            }

            var trainer7 = new TrainerData7(trdata.Files[index], trpoke.Files[index]);
            if (options.RandomizeClasses)
                trainer7.TrainerClass = GetRandomTrainerClass(config, trainerClassCount, trainer7.TrainerClass, (int)trainer7.Mode, options);
            if (options.RandomizeComposition)
                RandomizeComposition(trainer7, teamRange.Min, teamRange.Max);
            if (options.FillImportantGen7Teams && importantGen7.Contains(index))
                FillImportantTeam(trainer7, config, species, fallbackSpecies, form);
            if (options.MaximizeAI)
                trainer7.AI |= 7;
            foreach (var pokemon in trainer7.Pokemon)
            {
                if (pokemon.Species <= 0 || pokemon.Species >= speciesCount)
                    continue;
                if (options.RandomizeSpecies)
                {
                    var theme = options.RandomizeTypeThemes
                        ? Util.Rand.Next(typeCount)
                        : (int?)null;
                    pokemon.Species = theme is int type
                        ? GetRandomSpeciesType(config, species, fallbackSpecies, pokemon.Species, type)
                        : GetRandomSpecies(config, species, fallbackSpecies, pokemon.Species);
                    pokemon.Form = form.GetRandomForme(pokemon.Species);
                    pokemon.Gender = 0;
                }
                if (options.RandomizeLevels)
                    pokemon.Level = ScaleLevel(pokemon.Level, options.LevelMultiplier);
                if (options.MaximizeIVs)
                    pokemon.IVs = [31, 31, 31, 31, 31, 31];
                if (options.RandomizeItems && trainerItemPool.Length > 0)
                    pokemon.Item = trainerItemPool[Util.Rand.Next(trainerItemPool.Length)];
                if (options.RandomizeAbilities)
                    pokemon.Ability = Util.Rand.Next(0, 4);
                if (options.RandomizeNature)
                    pokemon.Nature = Util.Rand.Next(25);
                if (options.RandomizeShiny)
                    pokemon.Shiny = Util.Rand.Next(101) < Math.Clamp(options.ShinyChance, 0, 100);
                if (options.RandomizeMoves)
                    pokemon.Moves = GetRandomTrainerMoves(config, move, fallbackMoves, pokemon.Species, pokemon.Form);
                else if (options.UseCurrentLearnsetMoves)
                    pokemon.Moves = learnset!.GetCurrentMoves(pokemon.Species, pokemon.Form, pokemon.Level, 4);
                else if (options.MetronomeMoves)
                    pokemon.Moves = [118, 0, 0, 0];
                ApplyHighPowerMoves(pokemon, options, learnset);
                ForceFinalEvolution(pokemon, options, finalSpecies, form);
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

    private static int[] GetFinalTrainerSpecies(GameConfig config, int speciesCount)
    {
        var source = config.Generation == 7 ? Legal.FinalEvolutions_7 : Legal.FinalEvolutions_6;
        return source.Where(species => species > 0 && species < speciesCount).Distinct().ToArray();
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

    private static void FillImportantTeam(TrainerData7 trainer, GameConfig config, SpeciesRandomizer species, int[] fallbackSpecies, FormRandomizer form)
    {
        if (trainer.Pokemon.Count == 0)
            return;

        var averageLevel = (int)Math.Round(trainer.Pokemon.Average(pokemon => pokemon.Level));
        while (trainer.Pokemon.Count < 6)
        {
            var pokemon = trainer.Pokemon[^1].Clone();
            pokemon.Species = GetRandomSpecies(config, species, fallbackSpecies, pokemon.Species);
            pokemon.Form = form.GetRandomForme(pokemon.Species);
            pokemon.Level = Math.Clamp(averageLevel, 1, 100);
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
        if (classCount <= 0 || options.OnlySinglesForClasses && battleType != 0)
            return original;

        var classes = config.GetText(TextName.TrainerClasses);
        var special = config.Generation == 6
            ? config.ORAS ? Legal.SpecialClasses_ORAS : Legal.SpecialClasses_XY
            : config.USUM ? Legal.SpecialClasses_USUM : Legal.SpecialClasses_SM;
        var candidates = Enumerable.Range(0, classCount)
            .Where(index => index < classes.Length && !classes[index].StartsWith("[~", StringComparison.Ordinal))
            .Where(index => !options.IgnoreSpecialClasses || !special.Contains(index))
            .Where(index => config.Generation != 7 || index != 82)
            .ToArray();
        if (candidates.Length == 0)
            return original;
        if (options.IgnoreSpecialClasses && special.Contains(original))
            return original;
        return candidates[Util.Rand.Next(candidates.Length)];
    }

    private static void RandomizeComposition(TrainerData6 trainer, int minimum, int maximum)
    {
        if (trainer.Team.Length == 0)
            return;

        var count = Util.Rand.Next(minimum, maximum + 1);
        if (count == trainer.Team.Length)
            return;

        var team = trainer.Team.Take(Math.Min(count, trainer.Team.Length)).ToList();
        while (team.Count < count)
        {
            var source = team[^1];
            team.Add(new TrainerData6.Pokemon(source.Write(trainer.Item, trainer.Moves), trainer.Item, trainer.Moves));
        }
        trainer.Team = team.ToArray();
        trainer.NumPokemon = (byte)count;
    }

    private static void RandomizeComposition(TrainerData7 trainer, int minimum, int maximum)
    {
        if (trainer.Pokemon.Count == 0)
            return;

        var count = Util.Rand.Next(minimum, maximum + 1);
        if (count < trainer.Pokemon.Count)
            trainer.Pokemon.RemoveRange(count, trainer.Pokemon.Count - count);
        while (trainer.Pokemon.Count < count)
            trainer.Pokemon.Add(trainer.Pokemon[^1].Clone());
        trainer.NumPokemon = count;
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
        var garc = config.GetGARCData("encdata");
        var firstMapFile = config.ORAS ? 2 : 1;
        var form = new FormRandomizer(config) { AllowAlolanForm = false };
        var speciesCount = Catalogs.SpeciesCount(config);
        var fallbackSpecies = GetFallbackSpecies(config, speciesCount);
        var changedAreas = 0;

        for (var fileIndex = firstMapFile; fileIndex < garc.Files.Length; fileIndex++)
        {
            var file = garc.Files[fileIndex];
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

            // OR/AS has a packed copy of every area's encounter slots in file 1.
            if (config.ORAS)
            {
                var packed = garc.Files[1];
                var locationIndex = fileIndex - firstMapFile;
                var packedOffset = BitConverter.ToInt32(packed, (locationIndex + 1) * 4) + 0xE;
                var slotBytes = WildGen6Editor.GetSlotCount(oras: true) * 4;
                if (packedOffset < 0 || packedOffset + slotBytes > packed.Length)
                    throw new WorkspaceException("La tabla interna de encuentros de OR/AS no es válida.");
                WildGen6Editor.WriteSlots(packed, packedOffset, slots);
                garc.SetFile(1, packed);
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

    private static int GetRandomSpeciesType(GameConfig config, SpeciesRandomizer randomizer, int[] fallback, int oldSpecies, int type)
    {
        if (oldSpecies <= 0 || oldSpecies >= config.Personal.Table.Length)
            return oldSpecies;
        if (config.Personal.Table.Length <= config.MaxSpeciesID)
        {
            var candidates = fallback
                .Where(species => config.Personal.Table[species].Types.Contains(type))
                .ToArray();
            if (candidates.Length == 0)
                return GetRandomSpecies(config, randomizer, fallback, oldSpecies);
            var replacement = candidates[Util.Rand.Next(candidates.Length)];
            if (candidates.Length > 1)
                for (var attempt = 0; attempt < 32 && replacement == oldSpecies; attempt++)
                    replacement = candidates[Util.Rand.Next(candidates.Length)];
            return replacement;
        }
        return randomizer.GetRandomSpeciesType(oldSpecies, type);
    }

    private static Dictionary<int, int> BuildGen6TrainerThemes(GameConfig config, int typeCount, bool includeGymTrainers)
    {
        var map = new Dictionary<int, int>();
        var groups = config.ORAS ? OrasTrainerThemeGroups : XyTrainerThemeGroups;
        if (includeGymTrainers)
            groups = [.. groups, .. (config.ORAS ? OrasGymTrainerThemeGroups : XyGymTrainerThemeGroups)];
        foreach (var (name, ids) in groups)
        {
            var type = Util.Rand.Next(typeCount);
            foreach (var id in ids)
                map[id] = type;
        }
        return map;
    }

    private static int GetGen6TrainerTheme(IReadOnlyDictionary<int, int> themes, int trainerIndex, int typeCount) =>
        themes.TryGetValue(trainerIndex, out var type) ? type : Util.Rand.Next(typeCount);

    private static readonly (string Name, int[] IDs)[] XyTrainerThemeGroups =
    [
        ("RIVAL1", [130, 184, 329, 332, 335, 338, 341, 435, 519, 604, 575, 578, 581, 584, 587, 590, 593, 596, 599, 607]),
        ("RIVAL2", [131, 185, 330, 333, 336, 339, 342, 436, 520, 605, 576, 579, 582, 585, 588, 591, 594, 597, 600, 608]),
        ("RIVAL3", [132, 186, 331, 334, 337, 340, 343, 437, 521, 606, 577, 580, 583, 586, 589, 592, 595, 598, 601, 609]),
        ("FLAREBOSS", [303, 525, 526]),
        ("FLARE1", [175, 344]), ("FLARE2", [350, 351]), ("FLARE3", [348, 349]),
        ("FLARE4", [346, 347]), ("FLARE5", [345]),
        ("GYM1", [6, 254, 262]), ("GYM2", [76, 261, 279]), ("GYM3", [21, 255, 263, 613]),
        ("GYM4", [22, 256, 264]), ("GYM5", [23, 257, 265]), ("GYM6", [24, 258, 266]),
        ("GYM7", [25, 259, 267]), ("GYM8", [26, 260, 268]),
        ("ELITE1", [269, 273, 507]), ("ELITE2", [271, 275]), ("ELITE3", [187, 272]),
        ("ELITE4", [270, 274]), ("CHAMPION", [276, 277]),
        ("SHAUNA", [321, 322, 323, 137, 138, 139]), ("TREVOR", [325, 439]),
        ("TIERNO", [324, 438, 573]), ("PROFESSOR", [327, 328]), ("ESSENTIA", [503, 504, 505, 511, 512, 513, 514, 515]),
    ];

    private static readonly (string Name, int[] IDs)[] OrasTrainerThemeGroups =
    [
        ("RIVAL1", [289, 292, 295, 298, 527, 530, 674, 677, 699, 906]),
        ("RIVAL2", [290, 293, 296, 299, 528, 531, 675, 678, 700, 907]),
        ("RIVAL3", [291, 294, 297, 300, 529, 532, 676, 679, 701, 908]),
        ("AQUA1", [178, 231, 266]), ("AQUA2", [683, 684, 685, 686, 687]), ("AQUA3", [688, 689, 690]),
        ("MAGMA1", [235, 236, 271]), ("MAGMA2", [694, 695, 696, 697, 698]), ("MAGMA3", [691, 692, 693]),
        ("GYM1", [561]), ("GYM2", [563]), ("GYM3", [567]), ("GYM4", [569]), ("GYM5", [570]),
        ("GYM6", [571]), ("GYM7", [552]), ("GYM8", [572, 943]),
        ("ELITE1", [553, 909]), ("ELITE2", [554, 910]), ("ELITE3", [555, 911]),
        ("ELITE4", [556, 912]), ("CHAMPION", [557, 680, 913, 942]),
        ("WALLY", [518, 583, 944, 945, 946, 947]), ("RIVAL_EXTRA", [1, 4, 2, 5, 3, 6]),
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

    private static int[] GetRandomTrainerMoves(GameConfig config, MoveRandomizer? randomizer, int[] fallback, int species, int form)
    {
        if (randomizer is not null)
            return randomizer.GetRandomMoveset(config.Personal.GetFormIndex(species, form), 4);
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
