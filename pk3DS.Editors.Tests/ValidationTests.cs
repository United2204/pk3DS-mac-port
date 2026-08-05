using pk3DS.Editors;

namespace pk3DS.Editors.Tests;

/// <summary>
/// These guards are the last thing between a request payload and a byte write, so each one is
/// pinned against the specific bad input it exists to stop.
/// </summary>
public class WildTableValidationTests
{
    private const int SpeciesCount = 800;

    private static WildEncounterTable Table(int rateEach = 10, int minLevel = 5, int maxLevel = 10) => new(
        minLevel, maxLevel,
        Enumerable.Range(0, 10).Select(_ => new WildEncounterSlot(25, 0, rateEach)).ToArray(),
        Enumerable.Range(0, 7).Select(_ => Enumerable.Range(0, 10).Select(_ => new WildEncounterCompanionSlot(25, 0)).ToArray()).ToArray(),
        Enumerable.Range(0, 6).Select(_ => new WildEncounterCompanionSlot(25, 0)).ToArray());

    [Fact]
    public void AFullyFormedTableIsAccepted() =>
        WildEditor.Validate(Table(), SpeciesCount);

    [Fact]
    public void AnAllZeroRateTableIsAcceptedAsEmpty() =>
        WildEditor.Validate(Table(rateEach: 0), SpeciesCount);

    [Fact]
    public void RatesThatDoNotSumToOneHundredAreRejected() =>
        Assert.Throws<WorkspaceException>(() => WildEditor.Validate(Table(rateEach: 9), SpeciesCount));

    [Fact]
    public void AnInvertedLevelRangeIsRejected() =>
        Assert.Throws<WorkspaceException>(() => WildEditor.Validate(Table(minLevel: 40, maxLevel: 10), SpeciesCount));

    [Fact]
    public void ANullTableIsRejected() =>
        Assert.Throws<WorkspaceException>(() => WildEditor.Validate(null, SpeciesCount));

    [Fact]
    public void AWrongSlotCountIsRejected()
    {
        var table = Table() with { Slots = [new WildEncounterSlot(25, 0, 100)] };

        Assert.Throws<WorkspaceException>(() => WildEditor.Validate(table, SpeciesCount));
    }

    [Fact]
    public void MissingSosGroupsAreRejected()
    {
        var table = Table() with { SosSlots = null };

        Assert.Throws<WorkspaceException>(() => WildEditor.Validate(table, SpeciesCount));
    }

    [Fact]
    public void ASpeciesOutsideTheGameIsRejected()
    {
        var table = Table() with
        {
            Slots = Enumerable.Range(0, 10).Select(_ => new WildEncounterSlot(SpeciesCount + 1, 0, 10)).ToArray(),
        };

        Assert.Throws<WorkspaceException>(() => WildEditor.Validate(table, SpeciesCount));
    }
}

public class StaticEntryValidationTests
{
    private const int Species = 800, Items = 900, Moves = 750;

    [Fact]
    public void AMinimalEntryIsAcceptedAndReturned()
    {
        var entry = new StaticEntry(25, 0, 50, 0);

        Assert.Same(entry, StaticEditor.Validate(entry, Species, Items, Moves));
    }

    [Fact]
    public void ANullEntryIsRejected() =>
        Assert.Throws<WorkspaceException>(() => StaticEditor.Validate(null, Species, Items, Moves));

    [Theory]
    [InlineData(0, 0, 0, 0)]        // level 0 is not a legal encounter
    [InlineData(25, 0, 101, 0)]     // above the level cap
    [InlineData(900, 0, 50, 0)]     // species beyond the game
    [InlineData(25, 0, 50, 1000)]   // item beyond the game
    public void OutOfRangeFieldsAreRejected(int species, int form, int level, int heldItem) =>
        Assert.Throws<WorkspaceException>(() =>
            StaticEditor.Validate(new StaticEntry(species, form, level, heldItem), Species, Items, Moves));

    [Fact]
    public void AnIvArrayOfTheWrongLengthIsRejected()
    {
        var entry = new StaticEntry(25, 0, 50, 0, IVs: [31, 31, 31]);

        Assert.Throws<WorkspaceException>(() => StaticEditor.Validate(entry, Species, Items, Moves));
    }

    [Fact]
    public void RandomIvMarkersAreAccepted()
    {
        // Negative IVs down to -3 are the game's "randomise this stat" markers, not bad input.
        var entry = new StaticEntry(25, 0, 50, 0, IVs: [-1, -2, -3, 31, 0, 15]);

        StaticEditor.Validate(entry, Species, Items, Moves);
    }
}

public class StaticGen6OffsetTests
{
    [Theory]
    [InlineData(false, 0xD)]
    [InlineData(true, 0x3B)]
    public void EntryCountDependsOnGame(bool oras, int expected) =>
        Assert.Equal(expected, StaticGen6Editor.GetCount(oras));

    [Theory]
    [InlineData(false, 0, 0xEE46C)]
    [InlineData(false, 1, 0xEE46C + 0xC)]
    [InlineData(true, 0, 0xF1B20)]
    [InlineData(true, 2, 0xF1B20 + 0x18)]
    public void EntriesAreTwelveBytesApart(bool oras, int index, int expected) =>
        Assert.Equal(expected, StaticGen6Editor.GetOffset(oras, index));

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0xD)]
    [InlineData(true, 0x3B)]
    public void IndexesOutsideTheTableAreRejected(bool oras, int index) =>
        Assert.Throws<WorkspaceException>(() => StaticGen6Editor.GetOffset(oras, index));

    [Fact]
    public void ReadingPastTheEndOfTheCroIsRejected() =>
        Assert.Throws<WorkspaceException>(() => StaticGen6Editor.Read(new byte[0x100], oras: false, entryIndex: 0));
}

public class TrainerValidationTests
{
    private const int Classes = 200, Species = 800, Items = 900, Moves = 750;

    private static TrainerPokemonEntry Pokemon(int species = 25, int level = 50) =>
        new(species, 0, level, 0, [1, 2, 3, 4], 0, 0, 0, false, [31, 31, 31, 31, 31, 31], [0, 0, 0, 0, 0, 0]);

    private static TrainerEntry Trainer(params TrainerPokemonEntry[] team) =>
        new(1, 0, [0, 0, 0, 0], 0, false, 10, team.Length == 0 ? [Pokemon()] : team);

    [Fact]
    public void AWellFormedTrainerIsAcceptedAndReturned()
    {
        var entry = Trainer();

        Assert.Same(entry, TrainerEditor.Validate(entry, Classes, Species, Items, Moves));
    }

    [Fact]
    public void ANullTrainerIsRejected() =>
        Assert.Throws<WorkspaceException>(() => TrainerEditor.Validate(null, Classes, Species, Items, Moves));

    [Fact]
    public void AnEmptyTeamIsRejected() =>
        Assert.Throws<WorkspaceException>(() =>
            TrainerEditor.Validate(Trainer() with { Team = [] }, Classes, Species, Items, Moves));

    [Fact]
    public void ATeamLargerThanSixIsRejected() =>
        Assert.Throws<WorkspaceException>(() =>
            TrainerEditor.Validate(Trainer(Enumerable.Range(0, 7).Select(_ => Pokemon()).ToArray()), Classes, Species, Items, Moves));

    [Fact]
    public void ALevelZeroPokemonIsRejected() =>
        Assert.Throws<WorkspaceException>(() =>
            TrainerEditor.Validate(Trainer(Pokemon(level: 0)), Classes, Species, Items, Moves));

    [Fact]
    public void AMovesetOfTheWrongLengthIsRejected()
    {
        var entry = Trainer(Pokemon() with { Moves = [1, 2] });

        Assert.Throws<WorkspaceException>(() => TrainerEditor.Validate(entry, Classes, Species, Items, Moves));
    }

    [Fact]
    public void AnItemSlotArrayOfTheWrongLengthIsRejected() =>
        Assert.Throws<WorkspaceException>(() =>
            TrainerEditor.Validate(Trainer() with { Items = [0, 0] }, Classes, Species, Items, Moves));
}

public class RandomizerOptionTests
{
    [Fact]
    public void DefaultOptionsRequestNoChanges()
    {
        Assert.False(new PersonalOptions().HasChanges);
        Assert.False(new PersonalOptions().HasBulkChanges);
        Assert.False(new MoveOptions().HasChanges);
    }

    [Fact]
    public void ABulkOnlyChangeStillCountsAsAChange()
    {
        var options = new PersonalOptions(QuickHatch: true);

        Assert.True(options.HasBulkChanges);
        Assert.True(options.HasChanges);
    }

    [Fact]
    public void LegacyFlagsMapOntoTheStructuredOptions()
    {
        var options = PersonalOptions.FromLegacy(abilities: true, heldItems: false);

        Assert.True(options.RandomizeAbilities);
        Assert.False(options.RandomizeHeldItems);
        Assert.True(options.HasChanges);
    }

    [Fact]
    public void MetronomeModeAloneCountsAsAMoveChange() =>
        Assert.True(new MoveOptions(MetronomeMode: true).HasChanges);
}
