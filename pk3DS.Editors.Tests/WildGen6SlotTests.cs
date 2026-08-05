using pk3DS.Editors;

namespace pk3DS.Editors.Tests;

/// <summary>
/// Gen VI encounter slots are packed by hand into four-byte records, so a wrong shift or a
/// truncated write silently corrupts a table instead of failing. These tests pin the layout.
/// </summary>
public class WildGen6SlotTests
{
    [Theory]
    [InlineData(false, 94)]
    [InlineData(true, 61)]
    public void SlotCountDependsOnGame(bool oras, int expected) =>
        Assert.Equal(expected, WildGen6Editor.GetSlotCount(oras));

    [Fact]
    public void WrittenSlotsReadBackIdentically()
    {
        var slots = new WildGen6Slot[]
        {
            new(1, 0, 2, 5),
            new(0x7FF, 31, 100, 100),   // maximum species and form the packing allows
            new(0, 0, 0, 0),
            new(493, 3, 17, 42),
        };
        var data = new byte[slots.Length * 4];

        WildGen6Editor.WriteSlots(data, 0, slots);

        Assert.Equal(slots, WildGen6Editor.ReadSlots(data, 0, slots.Length));
    }

    [Fact]
    public void WritingAtAnOffsetLeavesSurroundingBytesUntouched()
    {
        var data = new byte[16];
        Array.Fill(data, (byte)0xAB);

        WildGen6Editor.WriteSlots(data, 4, [new WildGen6Slot(25, 1, 5, 9)]);

        // Only the four bytes at the offset may change.
        Assert.Equal([0xAB, 0xAB, 0xAB, 0xAB], data[..4]);
        Assert.All(data[8..], b => Assert.Equal(0xAB, b));
        Assert.Equal(new WildGen6Slot(25, 1, 5, 9), WildGen6Editor.ReadSlots(data, 4, 1)[0]);
    }

    [Fact]
    public void SpeciesAndFormSharePackedUShort()
    {
        var data = new byte[4];
        WildGen6Editor.WriteSlots(data, 0, [new WildGen6Slot(Species: 6, Form: 2, MinLevel: 50, MaxLevel: 50)]);

        // 6 | (2 << 11) == 0x1006, little endian, then the two level bytes.
        Assert.Equal([0x06, 0x10, 50, 50], data);
    }

    [Fact]
    public void OffsetIsRejectedWhenTheFileIsTooShortForItsSlots()
    {
        var truncated = new byte[0x20];
        BitConverter.GetBytes(0x18).CopyTo(truncated, 0x10);

        Assert.False(WildGen6Editor.TryGetEncounterOffset(truncated, oras: false, out _));
    }

    [Fact]
    public void OffsetIsRejectedForAFileSmallerThanTheHeader()
    {
        Assert.False(WildGen6Editor.TryGetEncounterOffset(new byte[4], oras: false, out _));
    }

    [Fact]
    public void OffsetIsAcceptedWhenEveryySlotFits()
    {
        var oras = true;
        var headerOffset = 0x18;
        var file = new byte[headerOffset + 0xE + (WildGen6Editor.GetSlotCount(oras) * 4)];
        BitConverter.GetBytes(headerOffset).CopyTo(file, 0x10);

        Assert.True(WildGen6Editor.TryGetEncounterOffset(file, oras, out var offset));
        Assert.Equal(headerOffset + 0xE, offset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupsPartitionExactlyTheSlotArray(bool oras)
    {
        var count = WildGen6Editor.GetSlotCount(oras);
        var slots = Enumerable.Range(0, count).Select(i => new WildGen6Slot(i, 0, 1, 1)).ToArray();

        var groups = WildGen6Editor.GetGroups(oras, slots);

        Assert.Equal(count, groups.Sum(group => group.Slots.Length));
        Assert.Equal(slots, groups.SelectMany(group => group.Slots).ToArray());
    }

    [Fact]
    public void FlattenRejectsAWrongSlotCount()
    {
        var groups = new[] { new WildGen6Group("Hierba", [new WildGen6Slot(1, 0, 1, 1)]) };

        Assert.Throws<WorkspaceException>(() => WildGen6Editor.FlattenGroups(groups, expectedCount: 94, speciesCount: 800));
    }

    [Fact]
    public void FlattenRejectsAnInvertedLevelRange()
    {
        var groups = new[] { new WildGen6Group("Hierba", [new WildGen6Slot(1, 0, MinLevel: 40, MaxLevel: 10)]) };

        Assert.Throws<WorkspaceException>(() => WildGen6Editor.FlattenGroups(groups, expectedCount: 1, speciesCount: 800));
    }

    [Fact]
    public void FlattenRejectsASpeciesOutsideTheGame()
    {
        var groups = new[] { new WildGen6Group("Hierba", [new WildGen6Slot(Species: 900, Form: 0, MinLevel: 1, MaxLevel: 1)]) };

        Assert.Throws<WorkspaceException>(() => WildGen6Editor.FlattenGroups(groups, expectedCount: 1, speciesCount: 800));
    }

    [Fact]
    public void FlattenAcceptsAValidSingleSlot()
    {
        var groups = new[] { new WildGen6Group("Hierba", [new WildGen6Slot(25, 0, 5, 10)]) };

        var slots = WildGen6Editor.FlattenGroups(groups, expectedCount: 1, speciesCount: 800);

        Assert.Equal(new WildGen6Slot(25, 0, 5, 10), Assert.Single(slots));
    }
}
