using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Editors;

namespace pk3DS.Editors.Tests;

public sealed class OrasWildRandomizerTests
{
    [Fact]
    public void CatalogReadsCompressedOrasAreasAtTheirActualGarcIndices()
    {
        using var workspace = new SyntheticOrasWorkspace();
        workspace.WriteWildFixture();

        var catalog = WildGen6Editor.GetCatalog(new WildGen6CatalogRequest(workspace.RomFs, Language: 1));

        Assert.Equal("ORAS", catalog.Game);
        Assert.Equal(536, catalog.Areas.Length);
        Assert.Equal(0, catalog.Areas[0].FileIndex);
        Assert.True(catalog.Areas[0].HasEncounters);
        Assert.Equal(1, catalog.Areas[1].FileIndex);
        Assert.True(catalog.Areas[1].HasEncounters);
    }

    [Fact]
    public void RandomizerUpdatesCompressedOrasAreasAndTheirPackedEncounterCopies()
    {
        using var workspace = new SyntheticOrasWorkspace();
        workspace.WriteWildFixture();
        var sourcePath = Path.Combine(workspace.RomFs, "a", "0", "1", "3");
        var sourceGarc = File.ReadAllBytes(sourcePath);

        var result = RandomizerService.Randomize(new RandomizeRequest(
            workspace.RomFs, workspace.OutputDirectory, SyntheticWorkspace.TitleId, Language: 1,
            RandomizeAbilities: false, RandomizeHeldItems: false, RandomizeLearnsets: false,
            Wild: new WildRandomizerOptions(
                Enabled: true, RandomizeSpecies: false, RandomizeLevels: true,
                LevelMultiplier: 1.25m)));

        Assert.Equal(sourceGarc, File.ReadAllBytes(sourcePath));
        Assert.Equal(["a/0/1/3"], result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, workspace);

        var exported = ExportAssertions.ReadGarcArchive(result, "a/0/1/3");
        var rawGarc = new GARC.MemGARC(exported);
        Assert.Equal(0x11, rawGarc.GetFile(0)[0]);
        var decodedGarc = new GARC.LazyGARC(exported);
        var packed = decodedGarc[537];
        const int slotBytes = 61 * 4;

        for (var areaIndex = 0; areaIndex < 2; areaIndex++)
        {
            var area = decodedGarc[areaIndex];
            Assert.True(WildGen6Editor.TryGetEncounterOffset(area, oras: true, out var areaOffset));
            var areaSlots = WildGen6Editor.ReadSlots(area, areaOffset, WildGen6Editor.GetSlotCount(oras: true));
            var packedOffset = BitConverter.ToInt32(packed, (areaIndex + 1) * sizeof(int)) + 0xE;
            Assert.Equal(area.AsSpan(areaOffset, slotBytes).ToArray(), packed.AsSpan(packedOffset, slotBytes).ToArray());
            Assert.All(areaSlots, slot => Assert.Equal(slot.MaxLevel, slot.MinLevel));
        }
    }
}
