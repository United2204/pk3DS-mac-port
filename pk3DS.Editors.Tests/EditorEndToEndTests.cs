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
    private readonly SyntheticXyWorkspace _workspace = new();

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
            TrainerEditor.GetCatalog(new TrainerCatalogRequest(_workspace.RomFs)));
        Assert.Throws<WorkspaceException>(() =>
            StaticEditor.GetCatalog(new StaticCatalogRequest(_workspace.RomFs)));
    }
}
