using System.IO.Compression;
using pk3DS.Core;
using pk3DS.Core.Structures;
using pk3DS.Editors;

namespace pk3DS.Editors.Tests;

/// <summary>
/// The Gen VII-only editors — trainers and static encounters — driven against a Sun/Moon fixture.
/// Until this existed they were covered only by their generation guards, never by their actual
/// read and write paths.
/// </summary>
public class Gen7EditorEndToEndTests : IDisposable
{
    private readonly SyntheticSunMoonWorkspace _workspace = new();

    public void Dispose()
    {
        _workspace.Dispose();
        GC.SuppressFinalize(this);
    }

    private void AssertArchiveContainsChangedFiles(ExportResult result) =>
        ExportAssertions.AssertContainsChangedFiles(result);

    private void AssertArchiveDiffersFromSource(ExportResult result) =>
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);

    [Fact]
    public void TheFixtureIsDetectedAsSunMoon()
    {
        var response = WorkspaceInspector.Inspect(new WorkspaceRequest(_workspace.RomFs));

        Assert.Equal("SM", response.GameVersion);
        Assert.True(response.Modules.Single(module => module.Id == "pickup").SourceAvailable);
        Assert.True(response.Modules.Single(module => module.Id == "static").SourceAvailable);
        Assert.True(response.Modules.Single(module => module.Id == "trainers").SourceAvailable);
        Assert.True(response.Modules.Single(module => module.Id == "tutors").SourceAvailable);
        Assert.True(response.Modules.Single(module => module.Id == "marts").SourceAvailable);
        Assert.True(response.Modules.Single(module => module.Id == "typechart").SourceAvailable);
        Assert.True(response.Modules.Single(module => module.Id == "owse").SourceAvailable);
    }

    [Fact]
    public void MissingGen7ZoneDataDisablesWildEncountersButKeepsStaticEncounters()
    {
        var garcPath = Path.Combine(_workspace.RomFs, "a", "0", "7", "7");
        var original = File.ReadAllBytes(garcPath);
        try
        {
            File.WriteAllBytes(garcPath, []);

            var response = WorkspaceInspector.Inspect(new WorkspaceRequest(_workspace.Root));

            Assert.False(response.Modules.Single(module => module.Id == "wild").SourceAvailable);
            Assert.True(response.Modules.Single(module => module.Id == "static").SourceAvailable);
            Assert.Equal("GARC encdata + zonedata + worlddata", response.Modules.Single(module => module.Id == "wild").Requirement);
        }
        finally
        {
            File.WriteAllBytes(garcPath, original);
        }
    }

    [Fact]
    public void MissingGen7ZoneDataDisablesOwseToo()
    {
        var garcPath = Path.Combine(_workspace.RomFs, "a", "0", "7", "7");
        var original = File.ReadAllBytes(garcPath);
        try
        {
            File.WriteAllBytes(garcPath, []);

            var response = WorkspaceInspector.Inspect(new WorkspaceRequest(_workspace.Root));

            var owse = response.Modules.Single(module => module.Id == "owse");
            Assert.False(owse.SourceAvailable);
            Assert.Equal("GARC encdata + zonedata + worlddata", owse.Requirement);
        }
        finally
        {
            File.WriteAllBytes(garcPath, original);
        }
    }

    [Fact]
    public void MissingGen7TrainerTeamDisablesTrainerRandomizer()
    {
        var trpokePath = Path.Combine(_workspace.RomFs, "a", "1", "0", "6");
        var original = File.ReadAllBytes(trpokePath);
        try
        {
            File.WriteAllBytes(trpokePath, []);

            var response = WorkspaceInspector.Inspect(new WorkspaceRequest(_workspace.Root));

            Assert.False(response.Modules.Single(module => module.Id == "trainers").SourceAvailable);
            Assert.Equal("GARC trclass + trdata + trpoke + gametext", response.Modules.Single(module => module.Id == "trainers").Requirement);
        }
        finally
        {
            File.WriteAllBytes(trpokePath, original);
        }
    }

    // Trainers -----------------------------------------------------------------

    [Fact]
    public void TrainerCatalogSkipsThePlaceholderTrainer()
    {
        var response = TrainerEditor.GetCatalog(new TrainerCatalogRequest(_workspace.RomFs));

        // Index 0 is the placeholder the game never battles.
        Assert.Equal(_workspace.TrainerCount - 1, response.Trainers.Length);
        Assert.Equal(_workspace.TrainerClassCount, response.Classes.Length);
        Assert.Equal(_workspace.SpeciesCount, response.Species.Length);
    }

    [Fact]
    public void TmGen7ReadsAndExportsCodeBin()
    {
        var table = TmHmEditor.GetTable(new TmHmTableRequest(_workspace.RomFs));
        Assert.Equal("SM", table.GameVersion);
        Assert.Equal(100, table.TMs.Length);
        Assert.Empty(table.HMs);

        var result = TmHmEditor.Export(new TmHmExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            TMs: Enumerable.Repeat(4, 100).ToArray(), HMs: []));
        ExportAssertions.AssertExeFsContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void TypeChartGen7ReadsAndExportsCodeBin()
    {
        var table = TypeChartEditor.GetTable(new TypeChartTableRequest(_workspace.RomFs));

        Assert.Equal("SM", table.GameVersion);
        Assert.Equal(324, table.Chart.Length);
        Assert.Equal(2, table.Chart[0]);

        var edited = table.Chart.ToArray();
        edited[0] = 0;
        var result = TypeChartEditor.Export(new TypeChartExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, edited));

        Assert.Equal(["code.bin"], result.ChangedFiles);
        ExportAssertions.AssertExeFsContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void TutorTablesReadAndExportShopCro()
    {
        var table = TutorEditor.GetTable(new TutorTableRequest(_workspace.RomFs));

        Assert.Equal("SM", table.GameVersion);
        Assert.Equal([4, 5, 6, 7], table.Groups.Select(group => group.Entries.Length).ToArray());
        Assert.Equal(1, table.Groups[0].Entries[0].Move);
        Assert.Equal(101, table.Groups[0].Entries[0].Price);

        var edited = table.Groups.Select((group, groupIndex) => group with
        {
            Entries = group.Entries.Select((entry, entryIndex) => entry with
            {
                Move = 2,
                Price = 500 + groupIndex + entryIndex,
            }).ToArray(),
        }).ToArray();
        var result = TutorEditor.Export(new TutorExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, edited));

        Assert.Equal(["Shop.cro"], result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void MartTablesReadAndExportShopCro()
    {
        var table = MartEditor.GetTable(new MartTableRequest(_workspace.RomFs));

        Assert.Equal("SM", table.GameVersion);
        Assert.Equal(23, table.Regular.Length);
        Assert.Equal(6, table.BattlePoints.Length);
        Assert.Equal(9, table.Regular[0].Entries.Length);
        Assert.Null(table.Regular[0].Entries[0].Price);
        Assert.Equal(1001, table.BattlePoints[0].Entries[0].Price);

        var regular = table.Regular.ToArray();
        regular[0] = regular[0] with
        {
            Entries = regular[0].Entries.Select((entry, index) => entry with { Item = (index % 3) + 1 }).ToArray(),
        };
        var battlePoints = table.BattlePoints.ToArray();
        battlePoints[0] = battlePoints[0] with
        {
            Entries = battlePoints[0].Entries.Select((entry, index) => entry with { Item = 2, Price = 700 + index }).ToArray(),
        };
        var result = MartEditor.Export(new MartExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, regular, battlePoints));

        Assert.Equal(["Shop.cro"], result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void PickupGen6RefusesGen7Workspace()
    {
        Assert.Throws<WorkspaceException>(() => PickupGen6Editor.GetTable(new PickupGen6TableRequest(_workspace.RomFs)));
    }

    [Fact]
    public void TrainerEntriesExposeTheWholeTeam()
    {
        var response = TrainerEditor.GetEntry(new TrainerEntryRequest(_workspace.RomFs, TrainerIndex: 1));

        Assert.Equal(1, response.TrainerIndex);
        Assert.Equal(_workspace.PokemonPerTrainer, response.Entry.Team.Length);
        Assert.Equal(4, response.Entry.Items.Length);
    }

    [Fact]
    public void ReadingATrainerOutsideTheTableIsRejected() =>
        Assert.Throws<WorkspaceException>(() =>
            TrainerEditor.GetEntry(new TrainerEntryRequest(_workspace.RomFs, TrainerIndex: 9999)));

    [Fact]
    public void ExportingATrainerWritesBothTrainerGarcs()
    {
        var result = TrainerEditor.Export(new TrainerExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            TrainerIndex: 1, Entry: SampleTrainer()));

        // trdata and trpoke are written as a pair; a patch with only one of them is broken.
        Assert.Equal(2, result.ChangedFiles.Length);
        AssertArchiveDiffersFromSource(result);
    }

    [Fact]
    public void AnInvalidTrainerPayloadIsRejectedBeforeAnythingIsWritten()
    {
        var entry = SampleTrainer() with { TrainerClass = 9999 };

        Assert.Throws<WorkspaceException>(() => TrainerEditor.Export(new TrainerExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            TrainerIndex: 1, Entry: entry)));
    }

    private TrainerEntry SampleTrainer() => new(
        TrainerClass: 1, Mode: 0, Items: [0, 0, 0, 0], AI: 1, Flag: false, Money: 10,
        Team: Enumerable.Range(0, _workspace.PokemonPerTrainer)
            .Select(_ => new TrainerPokemonEntry(
                Species: 1, Form: 0, Level: 25, Item: 0, Moves: [1, 2, 3, 4],
                Ability: 1, Gender: 0, Nature: 0, Shiny: false,
                IVs: [31, 31, 31, 31, 31, 31], EVs: [0, 0, 0, 0, 0, 0]))
            .ToArray());

    // Static encounters --------------------------------------------------------

    [Fact]
    public void StaticCatalogReportsEveryGroup()
    {
        var response = StaticEditor.GetCatalog(new StaticCatalogRequest(_workspace.RomFs));

        Assert.Equal(3, response.Groups.Length);
        Assert.Equal(_workspace.GiftCount, response.Groups.Single(g => g.Id == "gift").Count);
        Assert.Equal(_workspace.StaticCount, response.Groups.Single(g => g.Id == "static").Count);
        Assert.Equal(_workspace.TradeCount, response.Groups.Single(g => g.Id == "trade").Count);
    }

    [Theory]
    [InlineData("gift")]
    [InlineData("static")]
    [InlineData("trade")]
    public void StaticEntriesCanBeReadFromEveryGroup(string group)
    {
        var response = StaticEditor.GetEntry(new StaticEntryRequest(_workspace.RomFs, group, EntryIndex: 0));

        Assert.Equal(group, response.Group);
        Assert.Equal(0, response.EntryIndex);
    }

    [Fact]
    public void AnUnknownStaticGroupIsRejected() =>
        Assert.Throws<WorkspaceException>(() =>
            StaticEditor.GetEntry(new StaticEntryRequest(_workspace.RomFs, "nope", EntryIndex: 0)));

    [Fact]
    public void AStaticEntryPastTheEndOfItsGroupIsRejected() =>
        Assert.Throws<WorkspaceException>(() =>
            StaticEditor.GetEntry(new StaticEntryRequest(_workspace.RomFs, "gift", EntryIndex: 999)));

    [Theory]
    [InlineData("gift")]
    [InlineData("static")]
    [InlineData("trade")]
    public void EditingAStaticEncounterSurvivesTheExport(string group)
    {
        var result = StaticEditor.Export(new StaticExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            group, EntryIndex: 0, Entry: new StaticEntry(Species: 3, Form: 0, Level: 40, HeldItem: 1)));

        AssertArchiveDiffersFromSource(result);
    }

    [Fact]
    public void AdvancedStaticEncounterFieldsCanBeExported()
    {
        var result = StaticEditor.Export(new StaticExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            "static", EntryIndex: 0,
            Entry: new StaticEntry(
                Species: 3, Form: 1, Level: 40, HeldItem: 1, Gender: 2, Shiny: true,
                ShinyLock: true, Map: 42, Aura: 2, Allies: 2, Ally1: 3, Ally2: 4,
                RelearnMoves: [1, 2, 3, 4], IVs: [31, 30, 29, 28, 27, 26], EVs: [1, 2, 3, 4, 5, 6])));

        AssertArchiveDiffersFromSource(result);
    }

    [Fact]
    public void AStaticEntryWithAnImpossibleLevelIsRejected() =>
        Assert.Throws<WorkspaceException>(() => StaticEditor.Export(new StaticExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            "gift", EntryIndex: 0, Entry: new StaticEntry(Species: 3, Form: 0, Level: 0, HeldItem: 0))));

    // Wild encounters ----------------------------------------------------------

    [Fact]
    public void WildCatalogListsEveryAreaThatHasTables()
    {
        var response = WildEditor.GetCatalog(new WildAreaCatalogRequest(_workspace.RomFs));

        Assert.Equal(_workspace.AreaCount, response.Areas.Length);
        Assert.All(response.Areas, area => Assert.Equal(_workspace.TablesPerArea, area.TableCount));
    }

    [Fact]
    public void WildTablesExposeTheDayNightPairWithEverySlotGroup()
    {
        var response = WildEditor.GetTable(new WildTableRequest(
            _workspace.RomFs, _workspace.AreaFileNumber(0), TableIndex: 0));

        foreach (var table in new[] { response.Day, response.Night })
        {
            Assert.Equal(5, table.MinLevel);
            Assert.Equal(15, table.MaxLevel);
            Assert.Equal(10, table.Slots.Length);
            Assert.Equal(100, table.Slots.Sum(slot => slot.Rate));
            Assert.Equal(7, table.SosSlots!.Length);
            Assert.All(table.SosSlots, group => Assert.Equal(10, group.Length));
            Assert.Equal(6, table.WeatherSlots!.Length);
        }
    }

    [Fact]
    public void AnAreaWithoutTablesIsRejected() =>
        Assert.Throws<WorkspaceException>(() =>
            WildEditor.GetTable(new WildTableRequest(_workspace.RomFs, FileNumber: 4, TableIndex: 0)));

    [Fact]
    public void ATableIndexPastTheEndOfTheAreaIsRejected() =>
        Assert.Throws<WorkspaceException>(() =>
            WildEditor.GetTable(new WildTableRequest(
                _workspace.RomFs, _workspace.AreaFileNumber(0), TableIndex: 99)));

    [Fact]
    public void EditedWildSlotsSurviveTheExport()
    {
        var original = WildEditor.GetTable(new WildTableRequest(
            _workspace.RomFs, _workspace.AreaFileNumber(0), TableIndex: 0));
        var edited = original.Day with
        {
            MinLevel = 20,
            MaxLevel = 30,
            Slots = original.Day.Slots.Select((slot, i) => slot with { Species = i + 1 }).ToArray(),
        };

        var result = WildEditor.Export(new WildExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            _workspace.AreaFileNumber(0), TableIndex: 0, Day: edited, Night: original.Night));

        AssertArchiveDiffersFromSource(result);
    }

    [Fact]
    public void ATableWhoseRatesDoNotSumToOneHundredIsRejected()
    {
        var original = WildEditor.GetTable(new WildTableRequest(
            _workspace.RomFs, _workspace.AreaFileNumber(0), TableIndex: 0));
        var broken = original.Day with
        {
            Slots = original.Day.Slots.Select(slot => slot with { Rate = 9 }).ToArray(),
        };

        Assert.Throws<WorkspaceException>(() => WildEditor.Export(new WildExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            _workspace.AreaFileNumber(0), TableIndex: 0, Day: broken, Night: original.Night)));
    }

    // Shared editors on a Gen VII layout ---------------------------------------

    [Fact]
    public void PersonalEntriesUseTheLargerGen7Record()
    {
        var response = PersonalEditor.GetEntry(new PersonalEntryRequest(_workspace.RomFs, SpeciesIndex: 1));

        Assert.Equal(6, response.Stats.Length);
        Assert.Equal(3, response.Abilities.Length);
    }

    [Fact]
    public void MovesAreReadThroughTheMiniArchive()
    {
        // Gen VII packs every move into one "WD" mini archive, unlike the loose files X/Y uses.
        var response = MoveEditor.GetEntry(new MoveEntryRequest(_workspace.RomFs, MoveIndex: 1));

        Assert.Equal(1, response.MoveIndex);
    }

    [Fact]
    public void ExportingAMoveRepacksTheMiniArchive()
    {
        var result = MoveEditor.Export(new MoveExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            MoveIndex: 1, Type: 3, Category: 2, Power: 80, Accuracy: 95, PP: 15, Priority: 1));

        AssertArchiveDiffersFromSource(result);
    }

    [Fact]
    public void EggMovesKeepTheGen7FormTableIndex()
    {
        var result = EggMoveEditor.Export(new EggMoveExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            SpeciesIndex: 1, Moves: [1, 2], FormTableIndex: 7));

        AssertArchiveDiffersFromSource(result);
    }

    [Fact]
    public void EvolutionsUseTheEightByteGen7Record()
    {
        var response = EvolutionEditor.GetTable(new EvolutionTableRequest(_workspace.RomFs, SpeciesIndex: 1));

        Assert.Equal(8, response.Entries.Length);
    }

    [Fact]
    public void Gen6OnlyEditorsRefuseAGen7Workspace()
    {
        Assert.Throws<WorkspaceException>(() =>
            WildGen6Editor.GetCatalog(new WildGen6CatalogRequest(_workspace.RomFs)));
        Assert.Throws<WorkspaceException>(() =>
            OPowerEditor.GetTable(new OPowerTableRequest(_workspace.RomFs)));
    }

    // Pickup -------------------------------------------------------------------

    [Fact]
    public void PickupTableExposesItemsAndLevelBands()
    {
        var response = PickupEditor.GetTable(new PickupTableRequest(_workspace.RomFs));

        Assert.Equal(2, response.Entries.Length);
        Assert.Equal(10, response.Entries[0].Rates.Length);
        Assert.Equal(_workspace.ItemCount, response.Items.Length);
    }

    [Fact]
    public void EditingPickupRepackagesItsGarc()
    {
        var response = PickupEditor.GetTable(new PickupTableRequest(_workspace.RomFs));
        var edited = response.Entries.ToArray();
        edited[0] = response.Entries[0] with { Item = 3, Rates = [100, 100, 100, 100, 100, 100, 100, 100, 100, 100] };
        edited[1] = response.Entries[1] with { Item = 4, Rates = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0] };

        var result = PickupEditor.Export(new PickupExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, edited));

        AssertArchiveDiffersFromSource(result);
    }

    [Fact]
    public void PickupRatesMustSumToOneHundredPerBand()
    {
        var response = PickupEditor.GetTable(new PickupTableRequest(_workspace.RomFs));
        var broken = response.Entries.ToArray();
        broken[0] = response.Entries[0] with { Rates = [49, 50, 50, 50, 50, 50, 50, 50, 50, 50] };

        Assert.Throws<WorkspaceException>(() => PickupEditor.Export(new PickupExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, broken)));
    }

    // Battle Tree / Royal ------------------------------------------------------

    [Theory]
    [InlineData("normal")]
    [InlineData("special")]
    public void MaisonCatalogSupportsBothGen7Variants(string variant)
    {
        var response = MaisonEditor.GetCatalog(new MaisonCatalogRequest(_workspace.RomFs, variant));

        Assert.Equal("SM", response.GameVersion);
        Assert.Equal(2, response.Trainers.Length);
        Assert.Equal(2, response.Pokemon.Length);
        Assert.Equal(25, response.Natures.Length);
    }

    [Fact]
    public void MaisonGen7PokemonExportSurvives()
    {
        var original = MaisonEditor.GetPokemon(new MaisonPokemonRequest(_workspace.RomFs, "special", 0));
        var result = MaisonEditor.ExportPokemon(new MaisonPokemonExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, "special", 0,
            original.Entry with { Species = 4, Form = 2, Nature = 7, Item = 3 }));

        AssertArchiveDiffersFromSource(result);
    }

    [Fact]
    public void MaisonRejectsAnUnknownVariant() =>
        Assert.Throws<WorkspaceException>(() => MaisonEditor.GetCatalog(new MaisonCatalogRequest(_workspace.RomFs, "unknown")));

    /// <summary>
    /// Regression for the bug that broke every Gen VII export: <c>GameConfig.GetGameData</c> stats
    /// encdata to choose between the Sun and Moon GARC tables, so a scratch RomFS built without it
    /// threw before a single byte was written. The randomiser is the headline feature it took down.
    /// </summary>
    [Fact]
    public void TheRandomizerExportsOnAGen7Workspace()
    {
        var result = RandomizerService.Randomize(new RandomizeRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, Language: null,
            RandomizeAbilities: false, RandomizeHeldItems: false, RandomizeLearnsets: false,
            Moves: new MoveOptions(MetronomeMode: true)));

        AssertArchiveContainsChangedFiles(result);
    }

    [Fact]
    public void TheRandomizerExportsGen7WildEncounters()
    {
        var result = RandomizerService.Randomize(new RandomizeRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, Language: null,
            RandomizeAbilities: false, RandomizeHeldItems: false, RandomizeLearnsets: false,
            Wild: new WildRandomizerOptions(
                Enabled: true, RandomizeSpecies: true, RandomizeLevels: true,
                LevelMultiplier: 1.25m)));

        AssertArchiveContainsChangedFiles(result);
        Assert.Contains(result.ChangedFiles, file => file == "a/0/8/2");
        AssertArchiveDiffersFromSource(result);
    }

    [Fact]
    public void TheRandomizerExportsGen7TrainerTeams()
    {
        var result = RandomizerService.Randomize(new RandomizeRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, Language: null,
            RandomizeAbilities: false, RandomizeHeldItems: false, RandomizeLearnsets: false,
            Trainers: new TrainerRandomizerOptions(
                Enabled: true, RandomizeSpecies: true, RandomizeLevels: true,
                LevelMultiplier: 1.25m, RandomizeClasses: true, RandomizeComposition: true, RandomizeItems: true, RandomizeAbilities: true, RandomizeMoves: true, MaximizeAI: true,
                MinTeamSize: 1, MaxTeamSize: 1, MaximizeIVs: true, ForceFullyEvolved: true, FullyEvolvedLevel: 1, ForceHighPower: true, HighPowerLevel: 1, RandomizeNature: true, RandomizeShiny: true, ShinyChance: 100, RandomizeTypeThemes: true)));

        AssertArchiveContainsChangedFiles(result);
        Assert.Contains("a/1/0/5", result.ChangedFiles);
        Assert.Contains("a/1/0/6", result.ChangedFiles);
        ExportAssertions.AssertChangedFileDiffers(result, _workspace, "a/1/0/6");
        var teamData = ExportAssertions.ReadGarcFile(result, "a/1/0/6", 1);
        Assert.Equal(TrainerPoke7.SIZE, teamData.Length);
        var team = new TrainerPoke7(teamData);
        Assert.Equal([31, 31, 31, 31, 31, 31], team.IVs);
        Assert.InRange(team.Item, 1, 7);
        Assert.Equal(1, team.Moves[0]);
        Assert.InRange(team.Nature, 0, 24);
        Assert.True(team.Shiny);
        Assert.Contains(team.Species, Legal.FinalEvolutions_7.Where(species => species < _workspace.SpeciesCount));
    }

    [Fact]
    public void ImportantGen7TeamsCanBeFilledToSix()
    {
        using var workspace = new SyntheticSunMoonWorkspace(trainerCount: 14, pokemonPerTrainer: 2);
        var result = RandomizerService.Randomize(new RandomizeRequest(
            workspace.RomFs, workspace.OutputDirectory, SyntheticWorkspace.TitleId, Language: null,
            RandomizeAbilities: false, RandomizeHeldItems: false, RandomizeLearnsets: false,
            Trainers: new TrainerRandomizerOptions(
                Enabled: true, RandomizeSpecies: false, FillImportantGen7Teams: true)));

        var importantTeam = ExportAssertions.ReadGarcFile(result, "a/1/0/6", 12);
        Assert.Equal(6 * TrainerPoke7.SIZE, importantTeam.Length);
    }

    [Fact]
    public void MegaTrainerFormsAreRejectedForGen7()
    {
        Assert.Throws<WorkspaceException>(() => RandomizerService.Randomize(new RandomizeRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, Language: null,
            RandomizeAbilities: false, RandomizeHeldItems: false, RandomizeLearnsets: false,
            Trainers: new TrainerRandomizerOptions(
                Enabled: true, RandomizeSpecies: true, AllowMegaForms: true))));
    }

    [Fact]
    public void Gen6GymTrainerThemesAreRejectedForGen7()
    {
        Assert.Throws<WorkspaceException>(() => RandomizerService.Randomize(new RandomizeRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, Language: null,
            RandomizeAbilities: false, RandomizeHeldItems: false, RandomizeLearnsets: false,
            Trainers: new TrainerRandomizerOptions(
                Enabled: true, RandomizeSpecies: true, RandomizeTypeThemes: true, IncludeGymTrainerThemes: true))));
    }
}
