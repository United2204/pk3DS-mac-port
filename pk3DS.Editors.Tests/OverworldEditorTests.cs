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
        Assert.Equal(0, response.Zone.FurnitureCount);
        Assert.Equal(0, response.Zone.NpcCount);
        Assert.Equal(0, response.Zone.WarpCount);
        Assert.Equal(0, response.Zone.TriggerCount);
        Assert.Equal(0, response.Zone.UnknownEntityCount);
    }
}
