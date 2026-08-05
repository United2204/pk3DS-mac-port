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

        if (!personal.HasChanges && !learnsets.Enabled && !eggMoves.Enabled && !moves.HasChanges && evolutions.Mode == EvolutionMode.None)
            throw new WorkspaceException("Seleccioná al menos una opción para randomizar.");

        return EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "randomizer", [], config =>
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
}
