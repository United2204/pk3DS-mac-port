using System.IO.Compression;
using pk3DS.Editors;

namespace pk3DS.Editors.Tests;

/// <summary>
/// Drives the editors against a real (if synthetic) workspace: open the GARCs, read an entry,
/// export, and inspect the ZIP that comes out.
/// <para>
/// This is the layer that was entirely untested, and it is where the Item Stats and Mega
/// Evolutions exports were broken — both failed because the export never copied their own GARC
/// into the scratch RomFS. <see cref="ExportsProduceALayeredFsArchive"/> is the regression guard.
/// </para>
/// </summary>
public class EditorEndToEndTests : IDisposable
{
    private readonly SyntheticXyWorkspace _workspace = new(speciesCount: 18);

    public void Dispose()
    {
        _workspace.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string[] EntriesOf(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }

    [Fact]
    public void TheFixtureIsRecognisedAsAValidWorkspace()
    {
        var response = WorkspaceInspector.Inspect(new WorkspaceRequest(_workspace.RomFs));

        Assert.Equal("XY", response.GameVersion);
        Assert.NotEmpty(response.Modules);
        Assert.True(response.Modules.Single(module => module.Id == "gift6").SourceAvailable);
        Assert.True(response.Modules.Single(module => module.Id == "tutors6").SourceAvailable);
        Assert.True(response.Modules.Single(module => module.Id == "marts6").SourceAvailable);
    }

    [Fact]
    public void PersonalEntriesCanBeRead()
    {
        var response = PersonalEditor.GetEntry(new PersonalEntryRequest(_workspace.RomFs, SpeciesIndex: 1));

        Assert.Equal(1, response.SpeciesIndex);
        Assert.Equal(6, response.Stats.Length);
        Assert.Equal(2, response.Types.Length);
    }

    [Fact]
    public void ReadingASpeciesOutsideTheTableIsRejected() =>
        Assert.Throws<WorkspaceException>(() =>
            PersonalEditor.GetEntry(new PersonalEntryRequest(_workspace.RomFs, SpeciesIndex: 9999)));

    [Fact]
    public void LearnsetCatalogListsEverySpecies()
    {
        var response = LearnsetEditor.GetCatalog(new LearnsetCatalogRequest(_workspace.RomFs));

        Assert.Equal(_workspace.SpeciesCount, response.Species.Length);
    }

    [Fact]
    public void ItemEntriesCanBeRead()
    {
        var response = ItemEditor.GetEntry(new ItemEntryRequest(_workspace.RomFs, ItemIndex: 1));

        Assert.Equal(1, response.ItemIndex);
    }

    [Fact]
    public void MaisonCatalogAndRecordsWorkOnGen6()
    {
        var catalog = MaisonEditor.GetCatalog(new MaisonCatalogRequest(_workspace.RomFs, "normal"));
        Assert.Equal("XY", catalog.GameVersion);
        Assert.Equal(2, catalog.Trainers.Length);
        Assert.Equal(2, catalog.Pokemon.Length);

        var trainer = MaisonEditor.GetTrainer(new MaisonTrainerRequest(_workspace.RomFs, "normal", 0));
        Assert.Equal([0, 1], trainer.Entry.Choices);
        var pokemon = MaisonEditor.GetPokemon(new MaisonPokemonRequest(_workspace.RomFs, "normal", 0));
        Assert.Equal(1, pokemon.Entry.Species);
        Assert.Equal([1, 2, 3, 4], pokemon.Entry.Moves);
    }

    [Fact]
    public void MaisonGen6ExportsTrainerAndPokemonFiles()
    {
        var trainer = MaisonEditor.ExportTrainer(new MaisonTrainerExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, "normal", 0,
            new MaisonTrainerEntry(2, [1])));
        var pokemon = MaisonEditor.ExportPokemon(new MaisonPokemonExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, "normal", 0,
            new MaisonPokemonEntry(3, 0, 6, 2, [4, 3, 2, 1], [true, true, false, false, false, false])));

        ExportAssertions.AssertContentDiffersFromSource(trainer, _workspace);
        ExportAssertions.AssertContentDiffersFromSource(pokemon, _workspace);
    }

    [Fact]
    public void TrainerGen6CatalogAndRecordsWork()
    {
        var catalog = TrainerEditor.GetCatalog(new TrainerCatalogRequest(_workspace.RomFs));
        Assert.Equal(2, catalog.Trainers.Length);
        Assert.Equal(6, catalog.Classes.Length);

        var entry = TrainerEditor.GetEntry(new TrainerEntryRequest(_workspace.RomFs, TrainerIndex: 1));
        Assert.Equal(1, entry.Entry.Mode);
        Assert.True(entry.Entry.Flag);
        Assert.Equal(true, entry.Entry.HasItems);
        Assert.Equal(true, entry.Entry.HasMoves);
        Assert.Equal("Entrenador1", entry.Entry.Name);
        Assert.Equal("Clase1", entry.Entry.ClassName);
        Assert.Equal(2, entry.Entry.Team.Length);
        Assert.Equal(3, entry.Entry.Team[0].Species);
        Assert.Equal(20, entry.Entry.Team[0].IVs[0]);
    }

    [Fact]
    public void TrainerGen6ExportsBothTrainerGarcs()
    {
        var result = TrainerEditor.Export(new TrainerExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            TrainerIndex: 1,
            Entry: new TrainerEntry(
                TrainerClass: 2, Mode: 2, Items: [1, 2, 3, 4], AI: 3, Flag: false, Money: 20,
                Team:
                [
                    new TrainerPokemonEntry(5, 0, 50, 2, [1, 2, 3, 4], 4, 3, 0, false,
                        [99, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0]),
                ],
                Name: "Entrenador editado", ClassName: "Clase editada")));

        Assert.Equal(3, result.ChangedFiles.Length);
        Assert.Contains("a/0/7/3", result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void TmHmGen6ReadsAndExportsCodeBin()
    {
        var table = TmHmEditor.GetTable(new TmHmTableRequest(_workspace.RomFs));
        Assert.Equal("XY", table.GameVersion);
        Assert.Equal(100, table.TMs.Length);
        Assert.Equal(5, table.HMs.Length);
        Assert.Equal(1, table.TMs[0]);

        var result = TmHmEditor.Export(new TmHmExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            TMs: Enumerable.Repeat(2, 100).ToArray(), HMs: Enumerable.Repeat(3, 5).ToArray()));
        Assert.Equal(["code.bin"], result.ChangedFiles);
        ExportAssertions.AssertExeFsContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void PickupGen6ReadsAndExportsCodeBin()
    {
        var table = PickupGen6Editor.GetTable(new PickupGen6TableRequest(_workspace.RomFs));
        Assert.Equal("XY", table.GameVersion);
        Assert.Equal(18, table.Common.Length);
        Assert.Equal(11, table.Rare.Length);

        var result = PickupGen6Editor.Export(new PickupGen6ExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            Common: Enumerable.Repeat(2, 18).ToArray(), Rare: Enumerable.Repeat(3, 11).ToArray()));
        Assert.Equal(["code.bin"], result.ChangedFiles);
        ExportAssertions.AssertExeFsContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void ShinyRateReadsAndExportsCodeBin()
    {
        var table = ShinyRateEditor.GetTable(new ShinyRateTableRequest(_workspace.RomFs));

        Assert.Equal("XY", table.GameVersion);
        Assert.Equal(0, table.Rerolls);
        Assert.False(table.EverythingShiny);
        Assert.Contains(100, table.SupportedRerolls);

        var result = ShinyRateEditor.Export(new ShinyRateExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            Rerolls: 100, EverythingShiny: true));

        Assert.Equal(["code.bin"], result.ChangedFiles);
        ExportAssertions.AssertExeFsContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void OPowersReadAndExportCodeBin()
    {
        var table = OPowerEditor.GetTable(new OPowerTableRequest(_workspace.RomFs));

        Assert.Equal("XY", table.GameVersion);
        Assert.Equal(65, table.Entries.Length);
        Assert.Equal(10, table.Entries[0].PlayerCost);
        Assert.Equal(100, table.Entries[0].Efficacy);

        var edited = table.Entries.ToArray();
        edited[1] = edited[1] with { PlayerCost = 3, OtherCost = 4, Efficacy = 250, Duration = 255 };
        var result = OPowerEditor.Export(new OPowerExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, edited));

        Assert.Equal(["code.bin"], result.ChangedFiles);
        ExportAssertions.AssertExeFsContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void TypeChartGen6ReadsAndExportsCro()
    {
        var table = TypeChartEditor.GetTable(new TypeChartTableRequest(_workspace.RomFs));

        Assert.Equal("XY", table.GameVersion);
        Assert.Equal(18, table.TypeCount);
        Assert.Equal(324, table.Chart.Length);
        Assert.Equal(18, table.Types.Length);
        Assert.Equal(0, table.Chart[0]);

        var edited = table.Chart.ToArray();
        edited[0] = 8;
        var result = TypeChartEditor.Export(new TypeChartExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, edited));

        Assert.Equal(["DllBattle.cro"], result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void StarterGen6ReadsAndExportsBothCros()
    {
        var table = StarterEditor.GetTable(new StarterTableRequest(_workspace.RomFs));

        Assert.Equal("XY", table.GameVersion);
        Assert.Equal(2, table.Groups.Length);
        Assert.Equal([1, 2, 3], table.Groups[0].Species);
        Assert.Equal(18, table.Species.Length);

        var edited = table.Groups.Select((group, groupIndex) => group with
        {
            Species = group.Species.Select((_, index) => 6 + (groupIndex * 3) + index).ToArray(),
        }).ToArray();
        var result = StarterEditor.Export(new StarterExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, edited));

        Assert.Equal(["DllPoke3Select.cro", "DllField.cro"], result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void GiftGen6ReadsAndExportsDllFieldCro()
    {
        var catalog = GiftGen6Editor.GetCatalog(new GiftGen6CatalogRequest(_workspace.RomFs));

        Assert.Equal("XY", catalog.Game);
        Assert.Equal(0x13, catalog.Count);
        Assert.Equal(18, catalog.Species.Length);
        Assert.Equal(25, catalog.Natures.Length);

        var original = GiftGen6Editor.GetEntry(new GiftGen6EntryRequest(_workspace.RomFs, EntryIndex: 6));
        Assert.Equal(7, original.Entry.Species);

        var edited = original.Entry with
        {
            Species = 8,
            Level = 50,
            HeldItem = 1,
            Ability = 2,
            Nature = 4,
            IVs = [31, 31, 31, 31, 31, 31],
        };
        var result = GiftGen6Editor.Export(new GiftGen6ExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 6, edited));

        Assert.Equal(["DllField.cro"], result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void TutorGen6ReadsAndExportsCodeBin()
    {
        var table = TutorGen6Editor.GetTable(new TutorGen6TableRequest(_workspace.RomFs));

        Assert.Equal("XY", table.GameVersion);
        Assert.Equal([15, 17, 16, 15], table.Groups.Select(group => group.Moves.Length));
        Assert.Equal(1, table.Groups[0].Moves[0]);

        var edited = table.Groups.Select((group, groupIndex) => group with
        {
            Moves = group.Moves.Select((move, index) => groupIndex == 0 && index == 0 ? 2 : move).ToArray(),
        }).ToArray();
        var result = TutorGen6Editor.Export(new TutorGen6ExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, edited));

        Assert.Equal(["code.bin"], result.ChangedFiles);
        ExportAssertions.AssertExeFsContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void MartGen6ReadsAndExportsCodeBin()
    {
        var table = MartGen6Editor.GetTable(new MartTableRequest(_workspace.RomFs));

        Assert.Equal("XY", table.GameVersion);
        Assert.Equal(26, table.Regular.Length);
        Assert.Empty(table.BattlePoints);
        Assert.Equal(1, table.Regular[0].Entries[0].Item);

        var edited = table.Regular.Select((group, groupIndex) => group with
        {
            Entries = group.Entries.Select((entry, index) => groupIndex == 0 && index == 0 ? entry with { Item = 2 } : entry).ToArray(),
        }).ToArray();
        var result = MartGen6Editor.Export(new MartExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, edited, []));

        Assert.Equal(["code.bin"], result.ChangedFiles);
        ExportAssertions.AssertExeFsContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void ReadingItemZeroIsRejectedBecauseItIsTheEmptySlot() =>
        Assert.Throws<WorkspaceException>(() =>
            ItemEditor.GetEntry(new ItemEntryRequest(_workspace.RomFs, ItemIndex: 0)));

    [Fact]
    public void EvolutionTablesCanBeRead()
    {
        var response = EvolutionEditor.GetTable(new EvolutionTableRequest(_workspace.RomFs, SpeciesIndex: 1));

        Assert.Equal(8, response.Entries.Length);
    }

    /// <summary>
    /// One case per editor that writes a GARC. Each asserts the archive actually contains the file
    /// the editor claims to have changed — the exact check that would have caught the Item Stats
    /// and Mega Evolutions bugs, which failed before producing anything.
    /// </summary>
    [Theory]
    [MemberData(nameof(Exports))]
    public void ExportsProduceALayeredFsArchive(string name, Func<SyntheticXyWorkspace, ExportResult> export)
    {
        Assert.NotNull(name);

        var result = export(_workspace);

        Assert.Single(result.ChangedFiles.Distinct());
        // Every case below writes values distinct from the fixture's zeroed records, so the
        // archived GARC has to differ from the source. Merely containing the file is not enough:
        // an editor that failed to persist its edit would still ship an identical copy.
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);
    }

    public static TheoryData<string, Func<SyntheticXyWorkspace, ExportResult>> Exports() => new()
    {
        {
            "personal", w => PersonalEditor.Export(new PersonalExportRequest(
                w.RomFs, w.OutputDirectory, SyntheticWorkspace.TitleId, SpeciesIndex: 1,
                Stats: [50, 50, 50, 50, 50, 50], Types: [0, 0], CatchRate: 45,
                Abilities: [1, 0, 0], Items: [0, 0, 0], EggGroups: [1, 1]))
        },
        {
            "levelup", w => LearnsetEditor.Export(new LearnsetExportRequest(
                w.RomFs, w.OutputDirectory, SyntheticWorkspace.TitleId, SpeciesIndex: 1,
                Entries: [new LearnsetEntry(5, 1), new LearnsetEntry(10, 2)]))
        },
        {
            "eggmove", w => EggMoveEditor.Export(new EggMoveExportRequest(
                w.RomFs, w.OutputDirectory, SyntheticWorkspace.TitleId, SpeciesIndex: 1, Moves: [1, 2, 3]))
        },
        {
            // The first entry carries real values; an all-zero payload onto a zeroed fixture would
            // be indistinguishable from an export that wrote nothing.
            "evolution", w => EvolutionEditor.Export(new EvolutionExportRequest(
                w.RomFs, w.OutputDirectory, SyntheticWorkspace.TitleId, SpeciesIndex: 1,
                Entries: [new EvolutionEntry(Method: 4, Argument: 0, Species: 3, Form: 0, Level: 32),
                    .. Enumerable.Range(0, 7).Select(_ => new EvolutionEntry(0, 0, 0, 0, 0))]))
        },
        {
            "move", w => MoveEditor.Export(new MoveExportRequest(
                w.RomFs, w.OutputDirectory, SyntheticWorkspace.TitleId, MoveIndex: 1,
                Type: 0, Category: 1, Power: 40, Accuracy: 100, PP: 35, Priority: 0))
        },
        {
            // Regression: this export used to fail because the item GARC was never copied.
            "item", w => ItemEditor.Export(new ItemExportRequest(
                w.RomFs, w.OutputDirectory, SyntheticWorkspace.TitleId, ItemIndex: 1,
                BuyPrice: 200, HeldEffect: 0, HeldArgument: 0, FlingPower: 30,
                EffectField: 0, EffectBattle: 0, HealValue: 0))
        },
        {
            // Regression: same missing-GARC bug as item. The fixture gives each species a minimal
            // 0x10-byte table, which is two entries, and the editor requires an exact match.
            "megaevo", w => MegaEditor.Export(new MegaExportRequest(
                w.RomFs, w.OutputDirectory, SyntheticWorkspace.TitleId, SpeciesIndex: 1,
                Entries: [new MegaEntry(1, 1, 656, 0), new MegaEntry(0, 0, 0, 0)]))
        },
    };

    [Fact]
    public void TheSourceDumpIsNeverModifiedByAnExport()
    {
        var before = Directory.GetFiles(Path.Combine(_workspace.RomFs, "a"), "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.GetLastWriteTimeUtc);

        ItemEditor.Export(new ItemExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, ItemIndex: 1,
            BuyPrice: 999, HeldEffect: 1, HeldArgument: 1, FlingPower: 1,
            EffectField: 1, EffectBattle: 1, HealValue: 1));

        var after = Directory.GetFiles(Path.Combine(_workspace.RomFs, "a"), "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.GetLastWriteTimeUtc);

        Assert.Equal(before, after);
    }

    [Fact]
    public void AnEditedPersonalEntryComesBackFromTheExportedArchive()
    {
        var result = PersonalEditor.Export(new PersonalExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, SpeciesIndex: 1,
            Stats: [123, 45, 67, 89, 12, 34], Types: [3, 5], CatchRate: 200,
            Abilities: [7, 8, 9], Items: [1, 2, 3], EggGroups: [4, 6]));

        // The export writes into a scratch copy, so the source dump must still read as it did.
        var source = PersonalEditor.GetEntry(new PersonalEntryRequest(_workspace.RomFs, 1));
        Assert.Equal(0, source.CatchRate);
        Assert.NotEmpty(result.ChangedFiles);
    }

    [Fact]
    public void ExportingWithoutAValidTitleIdIsRejected() =>
        Assert.Throws<WorkspaceException>(() => ItemEditor.Export(new ItemExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, TitleId: "nope", ItemIndex: 1,
            BuyPrice: 0, HeldEffect: 0, HeldArgument: 0, FlingPower: 0,
            EffectField: 0, EffectBattle: 0, HealValue: 0)));

    [Fact]
    public void TheRandomizerExportsOnAGen6Workspace()
    {
        var result = RandomizerService.Randomize(new RandomizeRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, Language: null,
            RandomizeAbilities: false, RandomizeHeldItems: false, RandomizeLearnsets: false,
            Moves: new MoveOptions(MetronomeMode: true)));

        Assert.True(File.Exists(result.ZipPath));
        Assert.NotEmpty(result.ChangedFiles);
    }

    [Fact]
    public void TheRandomizerRefusesAnExportWithNothingSelected() =>
        Assert.Throws<WorkspaceException>(() => RandomizerService.Randomize(new RandomizeRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, Language: null,
            RandomizeAbilities: false, RandomizeHeldItems: false, RandomizeLearnsets: false)));

    [Fact]
    public void Gen7OnlyEditorsRefuseAGen6Workspace()
    {
        Assert.Throws<WorkspaceException>(() =>
            WildEditor.GetCatalog(new WildAreaCatalogRequest(_workspace.RomFs)));
        Assert.Throws<WorkspaceException>(() =>
            StaticEditor.GetCatalog(new StaticCatalogRequest(_workspace.RomFs)));
        Assert.Throws<WorkspaceException>(() =>
            PickupEditor.GetTable(new PickupTableRequest(_workspace.RomFs)));
        Assert.Throws<WorkspaceException>(() =>
            TutorEditor.GetTable(new TutorTableRequest(_workspace.RomFs)));
        Assert.Throws<WorkspaceException>(() =>
            MartEditor.GetTable(new MartTableRequest(_workspace.RomFs)));
    }
}
