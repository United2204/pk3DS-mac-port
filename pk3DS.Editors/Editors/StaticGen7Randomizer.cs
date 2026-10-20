using pk3DS.Core;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// Ports the Gen. VII Static Encounter Editor randomization actions. Gifts, static encounters and
/// trades share the <c>encounterstatic</c> GARC, while their record layouts remain independent.
/// </summary>
public static class StaticGen7Randomizer
{
    private const string Archive = "encounterstatic";

    private static readonly int[] Totem = [020, 105, 735, 738, 743, 746, 752, 754, 758, 777, 778, 784];
    private static readonly int[] UnevolvedLegend = [772, 789, 803];

    // Struggle, Celebrate and Shadow Blast are not valid gift moves. Z-Moves are also excluded,
    // matching the Windows editor's banned list.
    private static readonly int[] BannedGiftMoves = [165, 464, 621, .. Legal.Z_Moves];

    public static StaticGen7RandomizeResponse Randomize(StaticGen7RandomizeRequest request)
    {
        ValidateRequest(request);

        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var sourceConfig = EditorSession.OpenReadOnly(workspace, request.Language);
        Guard.Gen7(sourceConfig, "encuentros estáticos");
        RequireArchive(workspace, sourceConfig);

        string[] extraGarcs = request.UpdateStarterText ? [Archive, "storytext"] : [Archive];
        var storyTextUpdated = false;
        var result = EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "static7-randomize", extraGarcs, config =>
            {
                Guard.Gen7(config, "encuentros estáticos");
                var garc = config.GetGARCData(Archive);
                ValidateArchive(garc);

                storyTextUpdated = RandomizerService.WithRandomizerSeed(request.Seed, () =>
                {
                    return Mutate(config, garc, request);
                });
                garc.Save();
                var changedFiles = new List<string> { config.GetGARCFileName(Archive) };
                if (storyTextUpdated)
                    changedFiles.Add(config.GetGARCFileName("storytext"));
                return changedFiles;
            });
        return new StaticGen7RandomizeResponse(result.OutputFolder, result.ZipPath, result.ChangedFiles,
            storyTextUpdated);
    }

    private static bool Mutate(GameConfig config, GARCFile garc, StaticGen7RandomizeRequest options)
    {
        var files = garc.Files;
        var storyTextUpdated = false;
        var species = CreateSpeciesRandomizer(config, options);
        var finalEvolutions = GetFinalEvolutions(config, options);
        var replaceLegendary = GetReplaceLegendary(config);
        var itemPool = GetItemPool();
        var formRandomizer = new FormRandomizer(config)
        {
            AllowMega = options.AllowMegaForms,
            AllowAlolanForm = true,
        };

        if (options.RandomizeStartersOnly)
        {
            var oldStarters = RandomizeStarters(config, files[0], species, formRandomizer, itemPool, options);
            var newStarters = Enumerable.Range(0, oldStarters.Length)
                .Select(index => ReadGift(files[0], index).Species)
                .ToArray();
            if (options.UpdateStarterText && !oldStarters.SequenceEqual(newStarters))
            {
                UpdateStarterText(config, oldStarters, files[0]);
                storyTextUpdated = true;
            }
        }
        else
        {
            RandomizeGifts(config, files[0], species, formRandomizer, itemPool, replaceLegendary,
                finalEvolutions, options);
            RandomizeEncounters(config, files[1], species, formRandomizer, itemPool, replaceLegendary,
                finalEvolutions, options);
            RandomizeTrades(config, files[4], species, formRandomizer, itemPool, finalEvolutions, options);
        }

        // Assign the complete array once so unknown files and bytes outside known record fields
        // survive the GARC round-trip.
        garc.Files = files;
        return storyTextUpdated;
    }

    private static int[] RandomizeStarters(GameConfig config, byte[] file, SpeciesRandomizer species,
        FormRandomizer formRandomizer, int[] itemPool, StaticGen7RandomizeRequest options)
    {
        var oldStarters = new int[3];
        for (var index = 0; index < oldStarters.Length; index++)
            oldStarters[index] = ReadGift(file, index).Species;

        for (var index = 0; index < oldStarters.Length; index++)
        {
            var gift = ReadGift(file, index);
            var newSpecies = options.BasicStarter
                ? Legal.BasicStarters_7[RandomIndex(Legal.BasicStarters_7.Length)]
                : species.GetRandomSpecies(oldStarters[index]);

            gift.Species = newSpecies;
            RandomizeGiftFields(config, gift, formRandomizer, itemPool, options, starter: true);
            WriteGift(file, index, gift);
        }

        return oldStarters;
    }

    private static void UpdateStarterText(GameConfig config, int[] oldStarters, byte[] giftFile)
    {
        if (!config.SM && !config.USUM)
            throw new WorkspaceException("La actualización de texto de iniciales solo está disponible para Sol/Luna y Ultrasol/Ultraluna.");

        var storyGarc = config.GetGARCData("storytext");
        var storyFiles = storyGarc.Files;
        var storyTableIndex = config.USUM ? 39 : 41;
        if (storyFiles.Length <= storyTableIndex)
            throw new WorkspaceException("El archivo storytext no contiene la tabla de texto de iniciales esperada.");

        var lines = TextFile.GetStrings(config, storyFiles[storyTableIndex]);
        var requiredLineCount = config.USUM ? 17 : 4;
        if (lines.Length < requiredLineCount)
            throw new WorkspaceException("No pude leer las líneas de iniciales del archivo storytext seleccionado.");

        var speciesNames = config.GetText(TextName.SpeciesNames);
        var typeNames = config.GetText(TextName.Types);
        for (var index = 0; index < oldStarters.Length; index++)
        {
            var oldSpecies = oldStarters[index];
            var gift = ReadGift(giftFile, index);
            var newSpecies = gift.Species;
            if (oldSpecies <= 0 || oldSpecies >= speciesNames.Length
                || newSpecies <= 0 || newSpecies >= speciesNames.Length)
            {
                throw new WorkspaceException("Un inicial tiene una especie fuera de la tabla de nombres del juego.");
            }

            var lineIndex = 1 + index;
            var line = lines[lineIndex].Replace(speciesNames[oldSpecies], speciesNames[newSpecies], StringComparison.Ordinal);
            if (config.SM)
            {
                var form = gift.Form;
                var oldFormIndex = config.Personal.GetFormIndex(oldSpecies, form);
                var newFormIndex = config.Personal.GetFormIndex(newSpecies, form);
                var oldType = config.Personal[oldFormIndex].Types[0];
                var newType = config.Personal[newFormIndex].Types[0];
                if (oldType < 0 || oldType >= typeNames.Length || newType < 0 || newType >= typeNames.Length)
                    throw new WorkspaceException("No pude resolver los tipos del inicial para actualizar storytext.");
                line = line.Replace(typeNames[oldType], typeNames[newType], StringComparison.Ordinal);
            }
            else if (config.USUM)
            {
                lines[14 + index] = speciesNames[newSpecies];
            }
            lines[lineIndex] = line;
        }

        storyGarc.SetFile(storyTableIndex, TextFile.GetBytes(config, lines));
        storyGarc.Save();
    }

    private static void RandomizeGifts(GameConfig config, byte[] file, SpeciesRandomizer species,
        FormRandomizer formRandomizer, int[] itemPool, int[] replaceLegendary, int[] finalEvolutions,
        StaticGen7RandomizeRequest options)
    {
        var count = file.Length / EncounterGift7.SIZE;
        for (var index = 3; index < count; index++)
        {
            var gift = ReadGift(file, index);
            var newSpecies = options.ReplaceLegendary && replaceLegendary.Contains(gift.Species)
                || UnevolvedLegend.Contains(gift.Species)
                ? replaceLegendary[RandomIndex(replaceLegendary.Length)]
                : species.GetRandomSpecies(gift.Species);

            gift.Species = newSpecies;
            RandomizeGiftFields(config, gift, formRandomizer, itemPool, options, starter: false,
                finalEvolutions);
            WriteGift(file, index, gift);
        }
    }

    private static void RandomizeGiftFields(GameConfig config, EncounterGift7 gift,
        FormRandomizer formRandomizer, int[] itemPool, StaticGen7RandomizeRequest options,
        bool starter, int[]? finalEvolutions = null)
    {
        if (options.RandomizeHeldItems)
            gift.HeldItem = itemPool[RandomIndex(itemPool.Length)];
        if (options.RandomizeLevels)
            gift.Level = GetModifiedLevel(gift.Level, options.LevelMultiplier);
        if (options.RemoveShinyLocks)
            gift.ShinyLock = false;

        if (options.RandomizeSpecialMoves)
        {
            // Windows leaves the existing gift move untouched for starters in Metronome Mode;
            // Randomize All removes it from non-starter gifts so Surf Pikachu is cleared.
            if (!options.MetronomeMode)
                gift.SpecialMove = GetRandomGiftMove(config);
            else if (!starter)
                gift.SpecialMove = 0;
        }

        if (options.RandomizeAbilities)
            gift.Ability = (sbyte)Util.Rand.Next(0, 3);

        if (!starter && options.ForceFullyEvolved && gift.Level >= options.FullyEvolvedLevel
            && finalEvolutions is { Length: > 0 } && !finalEvolutions.Contains(gift.Species))
        {
            gift.Species = finalEvolutions[RandomIndex(finalEvolutions.Length)];
        }

        gift.Form = formRandomizer.GetRandomForme(gift.Species);
        gift.Nature = -1;
    }

    private static void RandomizeEncounters(GameConfig config, byte[] file, SpeciesRandomizer species,
        FormRandomizer formRandomizer, int[] itemPool, int[] replaceLegendary, int[] finalEvolutions,
        StaticGen7RandomizeRequest options)
    {
        var learnset = new LearnsetRandomizer(config, config.Learnsets);
        var count = file.Length / EncounterStatic7.SIZE;
        for (var index = 0; index < count; index++)
        {
            var encounter = ReadEncounter(file, index);
            var newSpecies = options.ReplaceLegendary && replaceLegendary.Contains(encounter.Species)
                ? replaceLegendary[RandomIndex(replaceLegendary.Length)]
                : options.ForceFullyEvolvedTotems && Totem.Contains(encounter.Species)
                    ? finalEvolutions[RandomIndex(finalEvolutions.Length)]
                    : species.GetRandomSpecies(encounter.Species);

            encounter.Species = newSpecies;
            if (options.RandomizeHeldItems)
                encounter.HeldItem = itemPool[RandomIndex(itemPool.Length)];
            if (options.RandomizeLevels)
                encounter.Level = GetModifiedLevel(encounter.Level, options.LevelMultiplier);
            if (options.RemoveShinyLocks)
                encounter.ShinyLock = false;
            if (options.RandomizeAura && encounter.Aura != 0)
                encounter.Aura = Util.Rand.Next(1, 19);
            if (options.RandomizeAbilities)
                encounter.Ability = Util.Rand.Next(1, 4);
            if (options.ForceFullyEvolved && encounter.Level >= options.FullyEvolvedLevel
                && finalEvolutions.Length > 0 && !finalEvolutions.Contains(encounter.Species))
            {
                encounter.Species = finalEvolutions[RandomIndex(finalEvolutions.Length)];
            }

            encounter.IVs = encounter.IV3
                ? [-4, -1, -1, -1, -1, -1]
                : [-1, -1, -1, -1, -1, -1];
            encounter.EVs = [0, 0, 0, 0, 0, 0];
            encounter.Form = formRandomizer.GetRandomForme(encounter.Species);
            encounter.Gender = 0;
            encounter.Nature = 0;
            encounter.RelearnMoves = options.MetronomeMode
                ? [118, 0, 0, 0]
                : learnset.GetCurrentMoves(encounter.Species, encounter.Form, encounter.Level, 4);
            WriteEncounter(file, index, encounter);
        }
    }

    private static void RandomizeTrades(GameConfig config, byte[] file, SpeciesRandomizer species,
        FormRandomizer formRandomizer, int[] itemPool, int[] finalEvolutions,
        StaticGen7RandomizeRequest options)
    {
        var count = file.Length / EncounterTrade7.SIZE;
        for (var index = 0; index < count; index++)
        {
            var trade = ReadTrade(file, index);
            trade.Species = species.GetRandomSpecies(trade.Species);
            trade.TradeRequestSpecies = species.GetRandomSpecies(trade.TradeRequestSpecies);

            if (options.RandomizeHeldItems)
                trade.HeldItem = itemPool[RandomIndex(itemPool.Length)];
            if (options.RandomizeLevels)
                trade.Level = GetModifiedLevel(trade.Level, options.LevelMultiplier);
            if (options.RandomizeAbilities)
                trade.Ability = Util.Rand.Next(0, 3);
            if (options.ForceFullyEvolved && trade.Level >= options.FullyEvolvedLevel
                && finalEvolutions.Length > 0 && !finalEvolutions.Contains(trade.Species))
            {
                trade.Species = finalEvolutions[RandomIndex(finalEvolutions.Length)];
            }

            trade.Form = formRandomizer.GetRandomForme(trade.Species);
            trade.Nature = (int)(Util.Random32() % 25);
            trade.IVs = [-1, -1, -1, -1, -1, -1];
            WriteTrade(file, index, trade);
        }
    }

    private static SpeciesRandomizer CreateSpeciesRandomizer(GameConfig config,
        StaticGen7RandomizeRequest options)
    {
        var randomizer = new SpeciesRandomizer(config)
        {
            G1 = options.AllowGen1,
            G2 = options.AllowGen2,
            G3 = options.AllowGen3,
            G4 = options.AllowGen4,
            G5 = options.AllowGen5,
            G6 = options.AllowGen6,
            G7 = options.AllowGen7,
            E = options.IncludeMythical,
            L = options.IncludeLegendary,
            rBST = options.MatchBst,
        };
        randomizer.Initialize();
        return randomizer;
    }

    private static int[] GetFinalEvolutions(GameConfig config, StaticGen7RandomizeRequest options)
    {
        var legendary = config.USUM ? Legal.Legendary_USUM : Legal.Legendary_SM;
        var mythical = config.USUM ? Legal.Mythical_USUM : Legal.Mythical_SM;
        return Legal.FinalEvolutions_7
            .Concat(options.IncludeLegendary ? legendary : Array.Empty<int>())
            .Concat(options.IncludeMythical ? mythical : Array.Empty<int>())
            .Where(species => species > 0 && species < config.Personal.Table.Length)
            .Distinct()
            .ToArray();
    }

    private static int[] GetReplaceLegendary(GameConfig config) =>
        (config.USUM ? Legal.Legendary_USUM : Legal.Legendary_SM)
        .Concat(config.USUM ? Legal.Mythical_USUM : Legal.Mythical_SM)
        .Where(species => species > 0 && species < config.Personal.Table.Length)
        .Distinct()
        .ToArray();

    private static int[] GetItemPool() =>
        Legal.TrainerRandomizerItems_7
            .Where(item => item != 0)
            .Select(item => (int)item)
            .ToArray();

    private static int GetRandomGiftMove(GameConfig config)
    {
        if (config.Moves.Length <= 1)
            throw new WorkspaceException("La tabla de movimientos no tiene movimientos válidos para los regalos.");

        int move;
        do
        {
            move = Util.Rand.Next(1, config.Moves.Length);
        }
        while (BannedGiftMoves.Contains(move));
        return move;
    }

    private static int GetModifiedLevel(int level, decimal multiplier) =>
        Math.Clamp((int)(level * multiplier), 1, 100);

    private static int RandomIndex(int length) => (int)(Util.Random32() % (uint)length);

    private static EncounterGift7 ReadGift(byte[] file, int index) =>
        new(file.Skip(index * EncounterGift7.SIZE).Take(EncounterGift7.SIZE).ToArray());

    private static EncounterStatic7 ReadEncounter(byte[] file, int index) =>
        new(file.Skip(index * EncounterStatic7.SIZE).Take(EncounterStatic7.SIZE).ToArray());

    private static EncounterTrade7 ReadTrade(byte[] file, int index) =>
        new(file.Skip(index * EncounterTrade7.SIZE).Take(EncounterTrade7.SIZE).ToArray());

    private static void WriteGift(byte[] file, int index, EncounterGift7 value) =>
        Array.Copy(value.Data, 0, file, index * EncounterGift7.SIZE, EncounterGift7.SIZE);

    private static void WriteEncounter(byte[] file, int index, EncounterStatic7 value) =>
        Array.Copy(value.Data, 0, file, index * EncounterStatic7.SIZE, EncounterStatic7.SIZE);

    private static void WriteTrade(byte[] file, int index, EncounterTrade7 value) =>
        Array.Copy(value.Data, 0, file, index * EncounterTrade7.SIZE, EncounterTrade7.SIZE);

    private static void ValidateRequest(StaticGen7RandomizeRequest request)
    {
        if (request.UpdateStarterText && !request.RandomizeStartersOnly)
            throw new WorkspaceException("La actualización de storytext solo puede usarse al randomizar los tres iniciales.");
        if (request.LevelMultiplier < 0m || request.LevelMultiplier > 3m)
            throw new WorkspaceException("El multiplicador de nivel debe estar entre 0.00 y 3.00.");
        if (request.FullyEvolvedLevel is < 1 or > 100)
            throw new WorkspaceException("El nivel para forzar evoluciones debe estar entre 1 y 100.");
    }

    private static void RequireArchive(GameWorkspace workspace, GameConfig config)
    {
        var path = Path.Combine(workspace.RomFsPath, config.GetGARCFileName(Archive));
        if (!File.Exists(path))
            throw new WorkspaceException("Falta el archivo de encuentros estáticos en el RomFS.");

        ValidateArchive(config.GetGARCData(Archive));
    }

    private static void ValidateArchive(GARCFile garc)
    {
        if (garc.Files.Length <= 4)
            throw new WorkspaceException("El archivo de encuentros estáticos no tiene el formato esperado.");

        var files = garc.Files;
        if (files[0].Length < EncounterGift7.SIZE * 3)
            throw new WorkspaceException("El archivo de regalos no contiene los tres iniciales esperados.");
        if (files[0].Length % EncounterGift7.SIZE != 0
            || files[1].Length % EncounterStatic7.SIZE != 0
            || files[4].Length % EncounterTrade7.SIZE != 0)
        {
            throw new WorkspaceException("El archivo de encuentros estáticos contiene registros incompletos.");
        }
    }
}
