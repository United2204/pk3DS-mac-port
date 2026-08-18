using pk3DS.Core;

namespace pk3DS.Editors.Tests;

public sealed class OverworldEditorTests : IDisposable
{
    private readonly SyntheticSunMoonWorkspace _workspace = new();
    private readonly SyntheticXyWorkspace _xyWorkspace = new();

    public void Dispose()
    {
        _workspace.Dispose();
        _xyWorkspace.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CatalogListsZoneScriptAndZoneInfoGroups()
    {
        var response = OverworldEditor.GetCatalog(new OverworldCatalogRequest(_workspace.RomFs));

        Assert.Equal("SM", response.GameVersion);
        Assert.Equal(4, response.Groups.Length);
        Assert.Equal(["zone-script", "zone-info", "zone-script", "zone-info"],
            response.Groups.Select(group => group.Id).ToArray());
        Assert.All(response.Groups, group =>
        {
            Assert.Equal(1, group.ScriptCount);
            Assert.Equal(0x20, group.RawBytes);
            Assert.Contains("Zona0", group.LocationName);
        });
    }

    [Fact]
    public void Gen7ZoneParentMapCanBeEditedWithoutTouchingTheSource()
    {
        var original = OverworldEditor.GetGen7Zone(new OverworldGen7ZoneRequest(_workspace.RomFs, 0));

        var result = OverworldEditor.ExportGen7Zone(new OverworldGen7ZoneExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            ZoneIndex: 0, ParentMap: 1));

        Assert.Equal("SM", original.GameVersion);
        Assert.Contains("Zona0", original.LocationName);
        Assert.Equal(0, original.ParentMap);
        Assert.Single(result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);
        Assert.Equal(0, OverworldEditor.GetGen7Zone(new OverworldGen7ZoneRequest(_workspace.RomFs, 0)).ParentMap);
    }

    [Fact]
    public void Gen7ZoneParentMapRejectsUnknownLocationIndexes()
    {
        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportGen7Zone(new OverworldGen7ZoneExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            ZoneIndex: 0, ParentMap: 999)));
    }

    [Fact]
    public void EntryExposesRawHeaderAndDecompressedInstructions()
    {
        var response = OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            _workspace.RomFs, "zone-script", WorldIndex: 0, ScriptIndex: 0));

        Assert.Equal("zone-script", response.Group);
        Assert.Equal(0x0A0AF1E0u, response.Magic);
        Assert.False(response.Debug);
        Assert.Equal(0x1C, response.ScriptInstructionStart);
        Assert.Equal(2, response.CompressedBytes);
        Assert.Equal(8, response.DecompressedBytes);
        Assert.Equal([0x30u, 0x30u], response.Instructions);
        Assert.NotEmpty(response.RawHex);
        Assert.NotNull(response.Zone);
        Assert.Equal(0, response.Zone!.ZoneIndex);
        Assert.Equal(0x54, response.Zone.ZoneDataBytes);
        Assert.Equal(11, response.Zone.ZoneFileCount);
        Assert.Equal(0, response.Zone.ParentMap);
        var entityBlocks = response.Zone.EntityBlocks;
        Assert.NotNull(entityBlocks);
        Assert.Equal(["EP", "EM", "EB", "ES", "EA", "ET"], entityBlocks.Select(block => block.Identifier).ToArray());
        Assert.Equal([2, 1, 1, 1, 1, 1], entityBlocks.Select(block => block.EntryCount).ToArray());
        Assert.All(entityBlocks, block => Assert.True(block.IsMiniArchive));
        Assert.Null(entityBlocks[0].Entries![0].RecordKind);
        Assert.Equal(1, entityBlocks[1].Entries![0].RecordCount);
        Assert.Equal(1, entityBlocks[1].Entries![0].RecordKind);
        Assert.Equal(2, entityBlocks[2].Entries![0].RecordKind);
        Assert.Equal(4, entityBlocks[3].Entries![0].RecordKind);
        Assert.Equal(5, entityBlocks[4].Entries![0].RecordKind);
        Assert.Equal(7, entityBlocks[5].Entries![0].RecordKind);
    }

    [Fact]
    public void Gen7EntityPositionsCanBeEditedWithoutTouchingTheSource()
    {
        var original = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Equal("SM", original.GameVersion);
        Assert.Equal(3, original.Positions.Length);
        Assert.Equal(0, original.Positions[0].ContainerEntry);
        Assert.Equal(0, original.Positions[0].RecordIndex);
        Assert.Equal(10f, original.Positions[0].X);
        Assert.Equal(100f, original.Positions[0].Y);
        Assert.Equal(200f, original.Positions[0].Z);
        Assert.Single(original.EmPositions);
        Assert.Equal(30f, original.EmPositions[0].X);
        Assert.Equal(300f, original.EmPositions[0].Y);
        Assert.Equal(400f, original.EmPositions[0].Z);
        Assert.Single(original.EbPositions);
        Assert.Equal(2, original.EbPositions[0].BlockEntry);
        Assert.Equal(50f, original.EbPositions[0].X);
        Assert.Equal(500f, original.EbPositions[0].Y);
        Assert.Equal(600f, original.EbPositions[0].Z);
        Assert.Single(original.EsPositions);
        Assert.Equal(3, original.EsPositions[0].BlockEntry);
        Assert.Equal(70f, original.EsPositions[0].X);
        Assert.Equal(700f, original.EsPositions[0].Y);
        Assert.Equal(800f, original.EsPositions[0].Z);
        Assert.Single(original.EaPositions);
        Assert.Equal(4, original.EaPositions[0].BlockEntry);
        Assert.Equal(90f, original.EaPositions[0].X);
        Assert.Equal(900f, original.EaPositions[0].Y);
        Assert.Equal(1000f, original.EaPositions[0].Z);
        Assert.Single(original.EtPositions);
        Assert.Equal(5, original.EtPositions[0].BlockEntry);
        Assert.Equal(110f, original.EtPositions[0].X);
        Assert.Equal(1100f, original.EtPositions[0].Y);
        Assert.Equal(1200f, original.EtPositions[0].Z);

        var positions = original.Positions.Select(position => position with { X = position.X + 1 }).ToArray();
        var emPositions = original.EmPositions.Select(position => position with { Z = position.Z + 1 }).ToArray();
        var ebPositions = original.EbPositions.Select(position => position with { Y = position.Y + 1 }).ToArray();
        var esPositions = original.EsPositions.Select(position => position with { Z = position.Z + 1 }).ToArray();
        var eaPositions = original.EaPositions.Select(position => position with { X = position.X + 1 }).ToArray();
        var etPositions = original.EtPositions.Select(position => position with { Y = position.Y + 1 }).ToArray();
        var result = OverworldEditor.ExportGen7Entities(new OverworldGen7EntityExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            positions, emPositions, ebPositions, esPositions, eaPositions, etPositions));

        Assert.Single(result.ChangedFiles);
        Assert.Equal("a/0/8/2", result.ChangedFiles[0]);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);

        var reread = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));
        Assert.Equal(10f, reread.Positions[0].X);
        Assert.Equal(400f, reread.EmPositions[0].Z);
        Assert.Equal(500f, reread.EbPositions[0].Y);
        Assert.Equal(800f, reread.EsPositions[0].Z);
        Assert.Equal(90f, reread.EaPositions[0].X);
        Assert.Equal(1100f, reread.EtPositions[0].Y);
    }

    [Fact]
    public void Gen7EntityExportRejectsChangingThePositionCount()
    {
        var original = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportGen7Entities(new OverworldGen7EntityExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 0, original.Positions[..^1])));
    }

    [Fact]
    public void Gen7EntityExportRejectsChangingTheEmPositionCount()
    {
        var original = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportGen7Entities(new OverworldGen7EntityExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            original.Positions, [])));
    }

    [Fact]
    public void Gen7EntityExportRejectsChangingTheEbPositionCount()
    {
        var original = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportGen7Entities(new OverworldGen7EntityExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            original.Positions, original.EmPositions, [])));
    }

    [Fact]
    public void Gen7EntityExportRejectsChangingTheEsPositionCount()
    {
        var original = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportGen7Entities(new OverworldGen7EntityExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            original.Positions, original.EmPositions, original.EbPositions, [])));
    }

    [Fact]
    public void Gen7EntityExportRejectsChangingTheEaPositionCount()
    {
        var original = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportGen7Entities(new OverworldGen7EntityExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            original.Positions, original.EmPositions, original.EbPositions, original.EsPositions, [])));
    }

    [Fact]
    public void Gen7EntityExportRejectsChangingTheEtPositionCount()
    {
        var original = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportGen7Entities(new OverworldGen7EntityExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            original.Positions, original.EmPositions, original.EbPositions, original.EsPositions,
            original.EaPositions, [])));
    }

    [Fact]
    public void Gen7ScriptInstructionsCanBeEditedWithoutChangingTheirCount()
    {
        var original = OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            _workspace.RomFs, "zone-script", WorldIndex: 0, ScriptIndex: 0));

        var result = OverworldEditor.ExportScript(new OverworldScriptExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            "zone-script", WorldIndex: 0, ScriptIndex: 0, [0xFFFFFFFFu, 0x80000000u]));

        Assert.Equal(2, original.Instructions.Length);
        Assert.Single(result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);
    }

    [Fact]
    public void Gen7ScriptExportRejectsChangingTheInstructionCount()
    {
        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportScript(new OverworldScriptExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            "zone-info", WorldIndex: 0, ScriptIndex: 0, [0x31u])));
    }

    [Fact]
    public void UnknownGroupAndScriptIndexAreRejected()
    {
        Assert.Throws<WorkspaceException>(() => OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            _workspace.RomFs, "map-edit", WorldIndex: 0, ScriptIndex: 0)));
        Assert.Throws<WorkspaceException>(() => OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            _workspace.RomFs, "zone-script", WorldIndex: 0, ScriptIndex: 99)));
    }

    [Fact]
    public void Gen6CatalogListsOverworldAndMapScripts()
    {
        var response = OverworldEditor.GetCatalog(new OverworldCatalogRequest(_xyWorkspace.RomFs));

        Assert.Equal("XY", response.GameVersion);
        Assert.Equal(4, response.Groups.Length);
        Assert.Equal(["gen6-overworld", "gen6-map-script", "gen6-overworld", "gen6-map-script"],
            response.Groups.Select(group => group.Id).ToArray());
        Assert.Contains("Zona0", response.Groups[0].LocationName);
        Assert.Contains("Zona1", response.Groups[2].LocationName);
        Assert.All(response.Groups, group => Assert.Equal(1, group.ScriptCount));
    }

    [Fact]
    public void Gen6EntryReadsTheScriptAfterTheEntityHeader()
    {
        var response = OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            _xyWorkspace.RomFs, "gen6-overworld", WorldIndex: 1, ScriptIndex: 0));

        Assert.Equal("gen6-overworld", response.Group);
        Assert.Contains("Zona1", response.LocationName);
        Assert.Equal(0x0A0AF1E0u, response.Magic);
        Assert.Equal([0x30u, 0x30u], response.Instructions);
        Assert.NotNull(response.Zone);
        Assert.Equal(1, response.Zone!.ZoneIndex);
        Assert.Equal(0x38, response.Zone.ZoneDataBytes);
        Assert.Equal(4, response.Zone.ZoneFileCount);
        Assert.Equal(1, response.Zone.ParentMap);
        Assert.Equal(11, response.Zone.MapArea);
        Assert.Equal(21, response.Zone.MapMatrix);
        Assert.Equal(31, response.Zone.TextFile);
        Assert.Equal(41, response.Zone.ScriptFile);
        Assert.Equal(3, response.Zone.Weather);
        Assert.Equal(1, response.Zone.FurnitureCount);
        Assert.Equal(1, response.Zone.NpcCount);
        Assert.Equal(1, response.Zone.WarpCount);
        Assert.Equal(1, response.Zone.TriggerCount);
        Assert.Equal(1, response.Zone.UnknownEntityCount);
    }

    [Fact]
    public void Gen6OrasStartsZonesAtTheSecondEncdataFile()
    {
        using var oras = new SyntheticOrasWorkspace();
        oras.WriteOverworldFixture();

        var catalog = OverworldEditor.GetCatalog(new OverworldCatalogRequest(oras.RomFs, Language: 1));

        Assert.Equal("ORAS", catalog.GameVersion);
        Assert.Equal(2, catalog.Groups.Length);
        Assert.All(catalog.Groups, group =>
        {
            Assert.Equal(0, group.WorldIndex);
            Assert.Contains("Zona0", group.LocationName);
        });

        var entry = OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            oras.RomFs, "gen6-map-script", WorldIndex: 0, ScriptIndex: 0, Language: 1));
        Assert.Equal(0x0A0AF1E0u, entry.Magic);
        Assert.Equal(0, entry.Zone!.ZoneIndex);
    }

    [Fact]
    public void Gen6ZoneExposesEditableEntitiesAndExportsLayeredFsPatch()
    {
        var original = OverworldEditor.GetGen6Zone(new OverworldGen6ZoneRequest(_xyWorkspace.RomFs, 0));

        Assert.Single(original.Furniture);
        Assert.Single(original.Npcs);
        Assert.Single(original.Warps);
        Assert.Single(original.Triggers);
        Assert.Single(original.UnknownTriggers);
        Assert.NotNull(original.Metadata);
        Assert.Equal(10, original.Metadata!.MapArea);
        Assert.Equal(20, original.Metadata.MapMatrix);
        Assert.Equal(30, original.Metadata.TextFile);
        Assert.Equal(40, original.Metadata.ScriptFile);
        Assert.Equal(0, original.Metadata.ParentMap);
        Assert.Equal(3, original.Metadata.Weather);

        var result = OverworldEditor.Export(new OverworldGen6ExportRequest(
            _xyWorkspace.RomFs, _xyWorkspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            original.Furniture.Select(entry => entry with { X = 12, Width = -2 }).ToArray(),
            original.Npcs.Select(entry => entry with { Id = 7, Model = 3, X = 18, Y = 24 }).ToArray(),
            original.Warps.Select(entry => entry with { DestinationMap = 1, DestinationTileIndex = 4 }).ToArray(),
            original.Triggers.Select(entry => entry with { X = 5, Y = 6 }).ToArray(),
            original.UnknownTriggers,
            original.Metadata with { MapArea = 99, MapMatrix = 88, Weather = 7 }));

        Assert.Single(result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _xyWorkspace);

        // Export is non-destructive: the source workspace still reports the original values.
        var reread = OverworldEditor.GetGen6Zone(new OverworldGen6ZoneRequest(_xyWorkspace.RomFs, 0));
        Assert.Equal(0, reread.Furniture[0].X);
        Assert.Equal(0, reread.Npcs[0].Id);
        Assert.Equal(0, reread.Warps[0].DestinationMap);
    }

    [Fact]
    public void Gen6EntityExportRejectsChangingTheEntityCount()
    {
        var original = OverworldEditor.GetGen6Zone(new OverworldGen6ZoneRequest(_xyWorkspace.RomFs, 0));
        Assert.Throws<WorkspaceException>(() => OverworldEditor.Export(new OverworldGen6ExportRequest(
            _xyWorkspace.RomFs, _xyWorkspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            [], original.Npcs, original.Warps, original.Triggers, original.UnknownTriggers)));
    }

    [Fact]
    public void Gen6MapReadsMovementGridAndExportsOnlyChangedProperties()
    {
        var original = OverworldEditor.GetGen6Map(new OverworldGen6MapRequest(_xyWorkspace.RomFs, 0));

        Assert.Equal(10, original.MapArea);
        Assert.Equal(20, original.MapMatrix);
        Assert.Equal(4, original.Width);
        Assert.Equal(3, original.Height);
        Assert.Equal(12, original.Properties.Length);
        Assert.Equal(2, original.MatrixWidth);
        Assert.Equal(1, original.MatrixHeight);
        Assert.Equal((ushort)120, original.MatrixValues[0]);

        var properties = (uint[])original.Properties.Clone();
        properties[0] = 0xDEADBEEFu;
        var result = OverworldEditor.ExportMap(new OverworldGen6MapExportRequest(
            _xyWorkspace.RomFs, _xyWorkspace.OutputDirectory, SyntheticWorkspace.TitleId, 0, properties));

        Assert.Single(result.ChangedFiles);
        Assert.Equal("a/0/4/1", result.ChangedFiles[0]);
        ExportAssertions.AssertContentDiffersFromSource(result, _xyWorkspace);
    }

    [Fact]
    public void Gen6MapExportRejectsAChangedGridSize()
    {
        var original = OverworldEditor.GetGen6Map(new OverworldGen6MapRequest(_xyWorkspace.RomFs, 0));
        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportMap(new OverworldGen6MapExportRequest(
            _xyWorkspace.RomFs, _xyWorkspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            original.Properties[..^1])));
    }

    [Fact]
    public void Gen6ScriptInstructionsCanBeEditedWithoutChangingTheirCount()
    {
        var original = OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            _xyWorkspace.RomFs, "gen6-overworld", WorldIndex: 0, ScriptIndex: 0));

        var result = OverworldEditor.ExportScript(new OverworldGen6ScriptExportRequest(
            _xyWorkspace.RomFs, _xyWorkspace.OutputDirectory, SyntheticWorkspace.TitleId,
            "gen6-overworld", ZoneIndex: 0, [0x30u, 0x31u]));

        Assert.Equal(2, original.Instructions.Length);
        Assert.Single(result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _xyWorkspace);
    }

    [Fact]
    public void Gen6ScriptExportRejectsAChangedInstructionCount() =>
        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportScript(
            new OverworldGen6ScriptExportRequest(
                _xyWorkspace.RomFs, _xyWorkspace.OutputDirectory, SyntheticWorkspace.TitleId,
                "gen6-map-script", ZoneIndex: 0, [0x30u])));
}
