using System.Runtime.InteropServices;
using pk3DS.Core.Structures;

namespace pk3DS.Editors.Tests;

/// <summary>
/// Helpers for pinning binary layouts. A structure that parses a record and writes it back must
/// not disturb bytes it does not own: a wrong offset or width corrupts a ROM silently rather than
/// throwing, so these tests assert exactly which bytes a write is allowed to touch.
/// </summary>
internal static class Bytes
{
    /// <summary>Deterministic filler so a failure is reproducible.</summary>
    public static byte[] Pattern(int length, int seed = 1234)
    {
        var random = new Random(seed);
        var data = new byte[length];
        random.NextBytes(data);
        return data;
    }

    /// <summary>Indexes whose value differs between the two arrays.</summary>
    public static int[] ChangedIndexes(byte[] before, byte[] after) =>
        Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToArray();

    /// <summary>Asserts that <paramref name="mutate"/> only altered bytes inside the given range.</summary>
    public static void OnlyTouches(byte[] data, int start, int length, Action mutate)
    {
        var before = (byte[])data.Clone();
        mutate();
        var changed = ChangedIndexes(before, data);
        Assert.All(changed, index => Assert.InRange(index, start, start + length - 1));
    }
}

public class ItemStructureTests
{
    [Fact]
    public void ParsingAndWritingBackReproducesTheRecord()
    {
        var data = Bytes.Pattern(Marshal.SizeOf<Item>());

        var written = new Item(data).Write();

        Assert.Equal(data, written);
    }

    [Fact]
    public void EditedFieldsSurviveTheRoundTrip()
    {
        var item = new Item(Bytes.Pattern(Marshal.SizeOf<Item>()))
        {
            HeldEffect = 7,
            FlingPower = 90,
            HealValue = 20,
        };

        var reloaded = new Item(item.Write());

        Assert.Equal(7, reloaded.HeldEffect);
        Assert.Equal(90, reloaded.FlingPower);
        Assert.Equal(20, reloaded.HealValue);
    }
}

public class Gen7EncounterLayoutTests
{
    [Fact]
    public void GiftFieldsReadBackWhatWasWritten()
    {
        var gift = new EncounterGift7(Bytes.Pattern(EncounterGift7.SIZE))
        {
            Species = 494,
            Form = 1,
            Level = 50,
            HeldItem = 328,
        };

        var reloaded = new EncounterGift7(gift.Data);

        Assert.Equal(494, reloaded.Species);
        Assert.Equal(1, reloaded.Form);
        Assert.Equal(50, reloaded.Level);
        Assert.Equal(328, reloaded.HeldItem);
    }

    [Fact]
    public void WritingTheGiftSpeciesTouchesOnlyItsTwoBytes()
    {
        var data = Bytes.Pattern(EncounterGift7.SIZE);
        var gift = new EncounterGift7(data);

        Bytes.OnlyTouches(data, 0x0, 2, () => gift.Species = 251);
    }

    [Fact]
    public void WritingTheGiftLevelTouchesOnlyItsByte()
    {
        var data = Bytes.Pattern(EncounterGift7.SIZE);
        var gift = new EncounterGift7(data);

        Bytes.OnlyTouches(data, 0x3, 1, () => gift.Level = 70);
    }

    [Fact]
    public void StaticFieldsReadBackWhatWasWritten()
    {
        var encounter = new EncounterStatic7(Bytes.Pattern(EncounterStatic7.SIZE))
        {
            Species = 800,
            Form = 2,
            Level = 60,
            HeldItem = 1,
        };

        var reloaded = new EncounterStatic7(encounter.Data);

        Assert.Equal(800, reloaded.Species);
        Assert.Equal(2, reloaded.Form);
        Assert.Equal(60, reloaded.Level);
        Assert.Equal(1, reloaded.HeldItem);
    }

    [Fact]
    public void StaticIvsSurviveTheRoundTrip()
    {
        var encounter = new EncounterStatic7(Bytes.Pattern(EncounterStatic7.SIZE))
        {
            IVs = [31, 0, 15, 30, 1, 31],
        };

        Assert.Equal([31, 0, 15, 30, 1, 31], new EncounterStatic7(encounter.Data).IVs);
    }

    [Fact]
    public void TradeFieldsReadBackWhatWasWritten()
    {
        var trade = new EncounterTrade7(Bytes.Pattern(EncounterTrade7.SIZE))
        {
            Species = 132,
            Level = 5,
            HeldItem = 0,
        };

        var reloaded = new EncounterTrade7(trade.Data);

        Assert.Equal(132, reloaded.Species);
        Assert.Equal(5, reloaded.Level);
        Assert.Equal(0, reloaded.HeldItem);
    }

    [Theory]
    [InlineData(0x14)]
    [InlineData(0x38)]
    [InlineData(0x34)]
    public void RecordSizesAreTheOnesTheEditorSlicesBy(int size) =>
        Assert.Contains(size, new[] { EncounterGift7.SIZE, EncounterStatic7.SIZE, EncounterTrade7.SIZE });
}

public class EncounterStatic6Tests
{
    private const int Size = 0xC;

    [Fact]
    public void ParsingAndWritingBackReproducesTheRecord()
    {
        var data = Bytes.Pattern(Size);

        Assert.Equal(data, new EncounterStatic6(data).Write());
    }

    [Fact]
    public void EditedFieldsSurviveTheRoundTrip()
    {
        var encounter = new EncounterStatic6(Bytes.Pattern(Size))
        {
            Species = 380,
            Form = 0,
            Level = 30,
            HeldItem = 0,
            ShinyLock = true,
            IV3 = true,
        };

        var reloaded = new EncounterStatic6(encounter.Write());

        Assert.Equal(380, reloaded.Species);
        Assert.Equal(30, reloaded.Level);
        Assert.True(reloaded.ShinyLock);
        Assert.True(reloaded.IV3);
    }

    [Fact]
    public void TheWrittenRecordKeepsTheOriginalSize() =>
        Assert.Equal(Size, new EncounterStatic6(Bytes.Pattern(Size)).Write().Length);
}

public class MegaEvolutionTests
{
    /// <summary>Four ushorts per entry, and the parser rejects anything under 0x10.</summary>
    private static byte[] Table(params (ushort Form, ushort Method, ushort Argument, ushort Aux)[] entries)
    {
        var data = new byte[entries.Length * 8];
        for (var i = 0; i < entries.Length; i++)
        {
            BitConverter.GetBytes(entries[i].Form).CopyTo(data, (i * 8) + 0);
            BitConverter.GetBytes(entries[i].Method).CopyTo(data, (i * 8) + 2);
            BitConverter.GetBytes(entries[i].Argument).CopyTo(data, (i * 8) + 4);
            BitConverter.GetBytes(entries[i].Aux).CopyTo(data, (i * 8) + 6);
        }
        return data;
    }

    [Fact]
    public void EntriesWithAMethodRoundTripUnchanged()
    {
        var data = Table((1, 1, 656, 0), (2, 1, 657, 0));

        Assert.Equal(data, new MegaEvolutions(data).Write());
    }

    [Fact]
    public void AnEntryWithoutAMethodIsClearedOnWrite()
    {
        // Documented behaviour of MegaEvolutions.Write: with no trigger, form and argument are
        // wiped so a leftover form cannot be reached. An editor must not treat this as corruption.
        var data = Table((3, 0, 999, 0), (1, 1, 656, 0));

        var written = new MegaEvolutions(data).Write();
        var reloaded = new MegaEvolutions(written);

        Assert.Equal(0, reloaded.Form[0]);
        Assert.Equal(0, reloaded.Argument[0]);
        Assert.Equal(1, reloaded.Form[1]);
        Assert.Equal(656, reloaded.Argument[1]);
    }

    [Theory]
    [InlineData(8)]    // below the 0x10 minimum
    [InlineData(20)]   // not a multiple of 8
    public void MalformedTablesLeaveTheArraysUnpopulated(int length)
    {
        // The parser returns early instead of throwing, so every consumer has to check. This test
        // exists to catch it if that contract ever changes.
        var mega = new MegaEvolutions(new byte[length]);

        Assert.Null(mega.Form);
    }
}

public class EggMovesTests
{
    private static byte[] Encode(params ushort[] moves)
    {
        var data = new byte[2 + (moves.Length * 2)];
        BitConverter.GetBytes((ushort)moves.Length).CopyTo(data, 0);
        for (var i = 0; i < moves.Length; i++)
            BitConverter.GetBytes(moves[i]).CopyTo(data, 2 + (i * 2));
        return data;
    }

    [Fact]
    public void Gen6MovesRoundTrip()
    {
        var data = Encode(33, 45, 98);

        var set = new EggMoves6(data);

        Assert.Equal([33, 45, 98], set.Moves);
        Assert.Equal(data, set.Write());
    }

    [Fact]
    public void AnEmptyGen6SetWritesAnEmptyFile()
    {
        var set = new EggMoves6(Encode());

        Assert.Empty(set.Moves);
        Assert.Empty(set.Write());
    }

    [Fact]
    public void EditedGen6MovesArePersisted()
    {
        var set = new EggMoves6(Encode(33));
        set.Moves = [1, 2, 3, 4];

        Assert.Equal([1, 2, 3, 4], new EggMoves6(set.Write()).Moves);
    }
}

public class EvolutionSetTests
{
    [Fact]
    public void Gen6SetsRoundTripUnchanged()
    {
        var data = Bytes.Pattern(EvolutionSet6.SIZE);

        Assert.Equal(data, new EvolutionSet6(data).Write());
    }

    [Fact]
    public void AWrongSizedGen6SetLeavesTheEntriesUnpopulated()
    {
        // Same early-return contract as MegaEvolutions: callers must not assume a parsed table.
        Assert.Null(new EvolutionSet6(new byte[3]).PossibleEvolutions);
    }

    [Fact]
    public void EditedGen6EvolutionsArePersisted()
    {
        var set = new EvolutionSet6(new byte[EvolutionSet6.SIZE]);
        set.PossibleEvolutions[0] = new EvolutionMethod { Method = 4, Argument = 0, Species = 3, Form = -1, Level = 32 };

        var reloaded = new EvolutionSet6(set.Write());

        Assert.Equal(4, reloaded.PossibleEvolutions[0].Method);
        Assert.Equal(3, reloaded.PossibleEvolutions[0].Species);
    }

    [Fact]
    public void Gen6EvolutionsDiscardFormAndLevel()
    {
        // Gen VI packs three ushorts per entry (method, argument, species) in six bytes; form and
        // level simply do not exist in the format. The editor accepts and validates both fields
        // for every generation, so on X/Y and OR/AS they are silently dropped on export.
        Assert.Equal(6 * 8, EvolutionSet6.SIZE);

        var set = new EvolutionSet6(new byte[EvolutionSet6.SIZE]);
        set.PossibleEvolutions[0] = new EvolutionMethod { Method = 4, Species = 3, Form = -1, Level = 32 };

        var reloaded = new EvolutionSet6(set.Write());

        Assert.Equal(0, reloaded.PossibleEvolutions[0].Level);
        // Form comes back as EvolutionMethod's own default of -1 ("no specific form"), never as
        // the value that was set — further proof the field never reached the file.
        Assert.Equal(-1, reloaded.PossibleEvolutions[0].Form);
    }

    [Fact]
    public void Gen7EvolutionsKeepFormAndLevel()
    {
        // Gen VII adds a byte each for form and level, which is why the same editor payload
        // round-trips here but not in Gen VI.
        Assert.Equal(8 * 8, EvolutionSet7.SIZE);

        var set = new EvolutionSet7(new byte[EvolutionSet7.SIZE]);
        set.PossibleEvolutions[0] = new EvolutionMethod { Method = 4, Species = 3, Form = -1, Level = 32 };

        var reloaded = new EvolutionSet7(set.Write());

        Assert.Equal(32, reloaded.PossibleEvolutions[0].Level);
        Assert.Equal(-1, reloaded.PossibleEvolutions[0].Form);
        Assert.Equal(3, reloaded.PossibleEvolutions[0].Species);
    }

    [Fact]
    public void Gen7SetsRoundTripUnchanged()
    {
        var data = Bytes.Pattern(EvolutionSet7.SIZE);

        Assert.Equal(data, new EvolutionSet7(data).Write());
    }
}

public class LearnsetTests
{
    /// <summary>Gen VI learnsets are (move, level) ushort pairs closed by a 0xFFFF sentinel.</summary>
    private static byte[] Encode(params (ushort Move, ushort Level)[] entries)
    {
        var data = new byte[(entries.Length * 4) + 4];
        for (var i = 0; i < entries.Length; i++)
        {
            BitConverter.GetBytes(entries[i].Move).CopyTo(data, i * 4);
            BitConverter.GetBytes(entries[i].Level).CopyTo(data, (i * 4) + 2);
        }
        BitConverter.GetBytes((ushort)0xFFFF).CopyTo(data, entries.Length * 4);
        BitConverter.GetBytes((ushort)0xFFFF).CopyTo(data, (entries.Length * 4) + 2);
        return data;
    }

    [Fact]
    public void MovesAndLevelsRoundTrip()
    {
        var data = Encode((33, 1), (45, 4), (98, 9));

        var set = new Learnset6(data);

        Assert.Equal([33, 45, 98], set.Moves);
        Assert.Equal([1, 4, 9], set.Levels);
        Assert.Equal(data, set.Write());
    }

    [Fact]
    public void AnEmptyLearnsetRoundTrips()
    {
        var data = Encode();

        Assert.Equal(data, new Learnset6(data).Write());
    }

    [Fact]
    public void EditedEntriesArePersisted()
    {
        var set = new Learnset6(Encode((33, 1)));
        set.Moves = [1, 2];
        set.Levels = [10, 20];

        var reloaded = new Learnset6(set.Write());

        Assert.Equal([1, 2], reloaded.Moves);
        Assert.Equal([10, 20], reloaded.Levels);
    }
}
