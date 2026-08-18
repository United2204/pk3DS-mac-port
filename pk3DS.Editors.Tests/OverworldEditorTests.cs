using System.IO.Compression;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.CTR;

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
    public void Gen7ZoneWorldAndAreaRoutingCanBeEditedWithoutTouchingTheSource()
    {
        var original = OverworldEditor.GetGen7Zone(new OverworldGen7ZoneRequest(_workspace.RomFs, 0));

        Assert.Equal(0, original.WorldIndex);
        Assert.Equal(0, original.AreaIndex);

        var result = OverworldEditor.ExportGen7Zone(new OverworldGen7ZoneExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            ZoneIndex: 0, ParentMap: 1, AreaIndex: 1));

        Assert.Equal(2, result.ChangedFiles.Length);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);

        var reread = OverworldEditor.GetGen7Zone(new OverworldGen7ZoneRequest(_workspace.RomFs, 0));
        Assert.Equal(0, reread.WorldIndex);
        Assert.Equal(0, reread.AreaIndex);
    }

    [Fact]
    public void Gen7ZoneAreaRoutingRejectsAnUnknownArea()
    {
        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportGen7Zone(new OverworldGen7ZoneExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId,
            ZoneIndex: 0, ParentMap: 0, AreaIndex: 999)));
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
        Assert.Equal(["EP", "EM", "EB", "ES", "EA", "EA", "ET"], entityBlocks.Select(block => block.Identifier).ToArray());
        Assert.Equal([2, 1, 1, 1, 1, 1, 1], entityBlocks.Select(block => block.EntryCount).ToArray());
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], entityBlocks.Select(block => block.BlockIndex).ToArray());
        Assert.All(entityBlocks, block => Assert.True(block.IsMiniArchive));
        Assert.Null(entityBlocks[0].Entries![0].RecordKind);
        Assert.Equal(1, entityBlocks[1].Entries![0].RecordCount);
        Assert.Equal(1, entityBlocks[1].Entries![0].RecordKind);
        Assert.Equal("EM principal", entityBlocks[1].Entries![0].Schema);
        Assert.Equal(0x78, entityBlocks[1].Entries![0].RecordStride);
        Assert.Equal(0x08, entityBlocks[1].Entries![0].PositionOffset);
        Assert.Equal(2, entityBlocks[2].Entries![0].RecordKind);
        Assert.Equal("EB tipo 2", entityBlocks[2].Entries![0].Schema);
        Assert.Equal(4, entityBlocks[3].Entries![0].RecordKind);
        Assert.Equal("ES tipo 4", entityBlocks[3].Entries![0].Schema);
        Assert.Equal(0x38, entityBlocks[3].Entries![0].RecordStride);
        Assert.Equal(5, entityBlocks[4].Entries![0].RecordKind);
        Assert.Equal("EA tipo 5", entityBlocks[4].Entries![0].Schema);
        Assert.Equal(6, entityBlocks[5].Entries![0].RecordKind);
        Assert.Equal("EA tipo 6", entityBlocks[5].Entries![0].Schema);
        Assert.Equal(0x30, entityBlocks[5].Entries![0].RecordStride);
        Assert.Equal(0x08, entityBlocks[5].Entries![0].PositionOffset);
        Assert.Equal(7, entityBlocks[6].Entries![0].RecordKind);
        Assert.Equal("ET tipo 7", entityBlocks[6].Entries![0].Schema);
        Assert.StartsWith("01 00 00 00", entityBlocks[1].Entries![0].PreviewHex);
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
        Assert.Equal(2, original.EaPositions.Length);
        Assert.Equal(4, original.EaPositions[0].BlockEntry);
        Assert.Equal(90f, original.EaPositions[0].X);
        Assert.Equal(900f, original.EaPositions[0].Y);
        Assert.Equal(1000f, original.EaPositions[0].Z);
        Assert.Equal(95f, original.EaPositions[1].X);
        Assert.Equal(950f, original.EaPositions[1].Y);
        Assert.Equal(1050f, original.EaPositions[1].Z);
        Assert.Single(original.EtPositions);
        Assert.Equal(6, original.EtPositions[0].BlockEntry);
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
        Assert.Equal(95f, reread.EaPositions[1].X);
        Assert.Equal(1100f, reread.EtPositions[0].Y);
    }

    [Fact]
    public void Gen7EiKind10ReadsAndExportsItsFixedPositionRecords()
    {
        var config = EditorSession.OpenReadOnly(_workspace.RomFs, language: null);
        var garc = config.Config.GetlzGARCData("encdata");
        var blocks = Mini.UnpackMini(garc[0], "ED").ToList();
        blocks.Add(Mini.PackMini([BuildGen7EiKind10EntryForTest()], "EI"));
        garc[0] = Mini.PackMini(blocks.ToArray(), "ED");
        garc.Save();

        var response = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Equal(2, response.EiPositions!.Length);
        Assert.Equal(7, response.EiPositions[0].BlockEntry);
        Assert.Equal(130f, response.EiPositions[0].X);
        Assert.Equal(2131f, response.EiPositions[1].Z);
        var entry = OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            _workspace.RomFs, "zone-script", WorldIndex: 0, ScriptIndex: 0));
        var ei = entry.Zone!.EntityBlocks!.Last();
        Assert.Equal("EI tipo 10", ei.Entries![0].Schema);
        Assert.Equal(0x5C, ei.Entries[0].RecordStride);
        Assert.Equal(0x08, ei.Entries[0].PositionOffset);

        var edited = response.EiPositions
            .Select((position, index) => index == 0 ? position with { X = 4321f } : position)
            .ToArray();
        var result = OverworldEditor.ExportGen7Entities(new OverworldGen7EntityExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            response.Positions, response.EmPositions, response.EbPositions, response.EsPositions,
            response.EaPositions, response.EtPositions, EiPositions: edited));

        Assert.Single(result.ChangedFiles);
        using var archive = ZipFile.OpenRead(result.ZipPath);
        using var stream = archive.GetEntry($"luma/titles/{SyntheticWorkspace.TitleId}/romfs/a/0/8/2")!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var outputGarc = new GARC.MemGARC(buffer.ToArray());
        var outputBlocks = Mini.UnpackMini(outputGarc.Files[0], "ED");
        var outputEiEntry = Mini.UnpackMini(outputBlocks[7], "EI")[0];
        Assert.Equal(4321f, BitConverter.ToSingle(outputEiEntry, 0x08));
        Assert.Equal(2131f, BitConverter.ToSingle(outputEiEntry, 0x64 + 8));
        Assert.Equal(0xB0, outputEiEntry[0xBC]);
        Assert.Equal(0xCF, outputEiEntry[0xDB]);
    }

    [Fact]
    public void Gen7PrKinds203And204EditTheirFixedPositionPrefix()
    {
        var config = EditorSession.OpenReadOnly(_workspace.RomFs, language: null);
        var garc = config.Config.GetlzGARCData("encdata");
        var blocks = Mini.UnpackMini(garc[0], "ED").ToList();
        blocks.Add(Mini.PackMini([
            BuildGen7PrEntry(203, 320f, 20f, -120f),
            BuildGen7PrEntry(204, 420f, 30f, -220f),
        ], "PR"));
        garc[0] = Mini.PackMini(blocks.ToArray(), "ED");
        garc.Save();

        var response = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Equal(2, response.PrPositions!.Length);
        Assert.Equal(7, response.PrPositions[0].BlockEntry);
        Assert.Equal(320f, response.PrPositions[0].X);
        Assert.Equal(1, response.PrPositions[1].ContainerEntry);
        Assert.Equal(420f, response.PrPositions[1].X);
        var entry = OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            _workspace.RomFs, "zone-script", WorldIndex: 0, ScriptIndex: 0));
        var pr = entry.Zone!.EntityBlocks!.Last();
        Assert.Equal("PR tipo 203", pr.Entries![0].Schema);
        Assert.Null(pr.Entries[0].RecordStride);
        Assert.Equal(0x08, pr.Entries[0].PositionOffset);
        Assert.Equal("PR tipo 204", pr.Entries[1].Schema);

        var edited = response.PrPositions
            .Select((position, index) => index == 0 ? position with { Z = 987f } : position)
            .ToArray();
        var result = OverworldEditor.ExportGen7Entities(new OverworldGen7EntityExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            response.Positions, response.EmPositions, response.EbPositions, response.EsPositions,
            response.EaPositions, response.EtPositions, PrPositions: edited));

        Assert.Single(result.ChangedFiles);
        using var archive = ZipFile.OpenRead(result.ZipPath);
        using var stream = archive.GetEntry($"luma/titles/{SyntheticWorkspace.TitleId}/romfs/a/0/8/2")!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var outputGarc = new GARC.MemGARC(buffer.ToArray());
        var outputBlocks = Mini.UnpackMini(outputGarc.Files[0], "ED");
        var outputPrEntry = Mini.UnpackMini(outputBlocks[7], "PR")[0];
        Assert.Equal(987f, BitConverter.ToSingle(outputPrEntry, 0x10));
        Assert.Equal(0xD0, outputPrEntry[0x14]);
        Assert.Equal(0xEB, outputPrEntry[0x2F]);
    }

    [Fact]
    public void Gen7EsUsesTheRetailShortRecordStride()
    {
        var config = EditorSession.OpenReadOnly(_workspace.RomFs, language: null);
        var garc = config.Config.GetlzGARCData("encdata");
        var blocks = Mini.UnpackMini(garc[0], "ED").ToList();
        var esEntries = Mini.UnpackMini(blocks[3], "ES");
        esEntries[0] = BuildRetailEsEntry();
        blocks[3] = Mini.PackMini(esEntries, "ES");
        garc[0] = Mini.PackMini(blocks.ToArray(), "ED");
        garc.Save();

        var response = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Equal(2, response.EsPositions.Length);
        Assert.Equal(70f, response.EsPositions[0].X);
        Assert.Equal(700f, response.EsPositions[0].Y);
        Assert.Equal(800f, response.EsPositions[0].Z);
        Assert.Equal(71f, response.EsPositions[1].X);
        Assert.Equal(701f, response.EsPositions[1].Y);
        Assert.Equal(801f, response.EsPositions[1].Z);
    }

    [Fact]
    public void Gen7RawEntityExportPreservesTheDecompressedEdContainer()
    {
        var config = EditorSession.OpenReadOnly(_workspace.RomFs, language: null);
        var sourceEd = config.Config.GetlzGARCData("encdata")[0];

        var result = OverworldEditor.ExportGen7EntityRaw(new OverworldGen7EntityRawExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, WorldIndex: 0));

        Assert.Equal("SM", result.GameVersion);
        Assert.Equal(0, result.SourceFileIndex);
        Assert.Equal("a/0/8/2", result.SourceGarc);
        Assert.Equal(sourceEd, File.ReadAllBytes(Path.Combine(result.OutputDirectory, "ed.bin")));
        Assert.Equal(16, result.Files.Length);
        Assert.Equal(7, result.Files.Count(file => file.BlockIndex is not null && file.EntryIndex is null));
        Assert.Equal(8, result.Files.Count(file => file.EntryIndex is not null));
        Assert.True(File.Exists(result.ManifestFile));
        using var manifest = JsonDocument.Parse(File.ReadAllText(result.ManifestFile));
        Assert.Equal("pk3DS OWSE Gen VII raw ED", manifest.RootElement.GetProperty("format").GetString());
        Assert.Equal("ed-000-entry-000.bin",
            manifest.RootElement.GetProperty("blocks")[0].GetProperty("entries")[0]
                .GetProperty("relativePath").GetString());
    }

    [Fact]
    public void Gen7EtKind9ReadsPointTablesAndPreservesTheirTail()
    {
        var config = EditorSession.OpenReadOnly(_workspace.RomFs, language: null);
        var garc = config.Config.GetlzGARCData("encdata");
        var blocks = Mini.UnpackMini(garc[0], "ED").ToList();
        blocks.Add(Mini.PackMini([Gen7EtKind9EntryForTest()], "ET"));
        garc[0] = Mini.PackMini(blocks.ToArray(), "ED");
        garc.Save();

        var response = OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0));

        Assert.Equal(6, response.EtPositions.Length);
        Assert.Equal([70f, 71f, 80f, 81f, 82f],
            response.EtPositions.Skip(1).Select(position => position.X).ToArray());

        var entry = OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            _workspace.RomFs, "zone-script", WorldIndex: 0, ScriptIndex: 0));
        var et9 = entry.Zone!.EntityBlocks!.Last();
        Assert.Equal("ET tipo 9 (tabla de puntos)", et9.Entries![0].Schema);
        Assert.Equal(0x0C, et9.Entries[0].RecordStride);
        Assert.Equal(0x08, et9.Entries[0].PositionOffset);

        var editedPositions = response.EtPositions
            .Select((position, index) => index == 5 ? position with { X = 1234f } : position)
            .ToArray();
        var result = OverworldEditor.ExportGen7Entities(new OverworldGen7EntityExportRequest(
            _workspace.RomFs, _workspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            response.Positions, response.EmPositions, response.EbPositions, response.EsPositions,
            response.EaPositions, editedPositions));
        Assert.Single(result.ChangedFiles);
        ExportAssertions.AssertContentDiffersFromSource(result, _workspace);

        using var archive = ZipFile.OpenRead(result.ZipPath);
        using var stream = archive.GetEntry($"luma/titles/{SyntheticWorkspace.TitleId}/romfs/a/0/8/2")!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var outputGarc = new GARC.MemGARC(buffer.ToArray());
        var outputBlocks = Mini.UnpackMini(outputGarc.Files[0], "ED");
        var outputEtEntry = Mini.UnpackMini(outputBlocks[7], "ET")[0];
        Assert.Equal(1234f, BitConverter.ToSingle(outputEtEntry, 0x74));
        Assert.Equal(0xD0, outputEtEntry[0x80]);
        Assert.Equal(0xEF, outputEtEntry[0x9F]);
    }

    private static byte[] Gen7EtKind9EntryForTest()
    {
        const int firstDescriptorSize = 0x14;
        const int descriptorSize = 0x18;
        const int pointHeaderSize = 0x08;
        const int pointSize = 0x0C;
        const int descriptorCount = 2;
        var tableEnd = 8 + firstDescriptorSize + descriptorSize;
        var firstDataOffset = tableEnd;
        var firstPointCount = 2;
        var secondDataOffset = firstDataOffset + pointHeaderSize + (firstPointCount * pointSize);
        var secondPointCount = 3;
        var secondTailOffset = secondDataOffset + pointHeaderSize + (secondPointCount * pointSize);
        var entry = new byte[secondTailOffset + 0x20];
        BitConverter.GetBytes(descriptorCount).CopyTo(entry, 0);
        BitConverter.GetBytes(9).CopyTo(entry, 4);
        BitConverter.GetBytes(firstDataOffset).CopyTo(entry, 8);
        BitConverter.GetBytes(1f).CopyTo(entry, 12);
        BitConverter.GetBytes(secondDataOffset).CopyTo(entry, 16);
        BitConverter.GetBytes(9).CopyTo(entry, 0x1C);
        BitConverter.GetBytes(secondDataOffset).CopyTo(entry, 0x20);
        BitConverter.GetBytes(2f).CopyTo(entry, 0x24);
        BitConverter.GetBytes(secondTailOffset).CopyTo(entry, 0x28);
        BitConverter.GetBytes(firstPointCount | 0x10000).CopyTo(entry, firstDataOffset);
        BitConverter.GetBytes(firstPointCount).CopyTo(entry, firstDataOffset + 4);
        BitConverter.GetBytes(secondPointCount).CopyTo(entry, secondDataOffset);
        BitConverter.GetBytes(0).CopyTo(entry, secondDataOffset + 4);
        var allPoints = new[] { 70f, 71f, 80f, 81f, 82f };
        for (var index = 0; index < allPoints.Length; index++)
        {
            var offset = index < firstPointCount
                ? firstDataOffset + pointHeaderSize + (index * pointSize)
                : secondDataOffset + pointHeaderSize + ((index - firstPointCount) * pointSize);
            BitConverter.GetBytes(allPoints[index]).CopyTo(entry, offset);
            BitConverter.GetBytes(700f + index).CopyTo(entry, offset + 4);
            BitConverter.GetBytes(800f + index).CopyTo(entry, offset + 8);
        }
        for (var index = secondTailOffset; index < entry.Length; index++)
            entry[index] = (byte)(0xD0 + index - secondTailOffset);
        return entry;
    }

    [Fact]
    public void Gen7EntityInventoryLabelsUnconfirmedVariantsWithoutEnablingThem()
    {
        var config = EditorSession.OpenReadOnly(_workspace.RomFs, language: null);
        var garc = config.Config.GetlzGARCData("encdata");
        var blocks = Mini.UnpackMini(garc[0], "ED").ToList();
        blocks.Add(Mini.PackMini([BuildEntityVariantEntry(1, 3, 0x2D0)], "EM"));
        blocks.Add(Mini.PackMini([BuildEntityVariantEntry(1, 4, 0x20)], "ES"));
        blocks.Add(Mini.PackMini([BuildEntityVariantEntry(1, 9, 0x60)], "ET"));
        blocks.Add(Mini.PackMini([BuildEntityVariantEntry(1, 10, 0x40)], "EI"));
        blocks.Add(Mini.PackMini([BuildEntityVariantEntry(1, 12, 0x40)], "FS"));
        blocks.Add(Mini.PackMini([BuildEntityVariantEntry(1, 13, 0x40)], "FS"));
        garc[0] = Mini.PackMini(blocks.ToArray(), "ED");
        garc.Save();

        var response = OverworldEditor.GetEntry(new OverworldScriptEntryRequest(
            _workspace.RomFs, "zone-script", WorldIndex: 0, ScriptIndex: 0));
        var summaries = response.Zone!.EntityBlocks!;

        var emVariant = summaries[7].Entries![0];
        Assert.Equal("EM tipo 3: tabla anidada variable no confirmada", emVariant.Schema);
        Assert.Null(emVariant.RecordStride);
        Assert.Equal("ES tipo 4: rango no confirmado", summaries[8].Entries![0].Schema);
        Assert.Equal("ET tipo 9: esquema variable no confirmado", summaries[9].Entries![0].Schema);
        Assert.Equal("EI tipo 10: rango no confirmado", summaries[10].Entries![0].Schema);
        Assert.Equal("FS tipo 12: estructura interna variable no confirmada", summaries[11].Entries![0].Schema);
        Assert.Equal("FS tipo 13: estructura variable no confirmada", summaries[12].Entries![0].Schema);
        Assert.Single(OverworldEditor.GetGen7Entities(new OverworldGen7EntityRequest(_workspace.RomFs, 0)).EmPositions);
    }

    private static byte[] BuildEntityVariantEntry(int count, int kind, int bytes)
    {
        var entry = new byte[bytes];
        BitConverter.GetBytes(count).CopyTo(entry, 0);
        BitConverter.GetBytes(kind).CopyTo(entry, 4);
        return entry;
    }

    private static byte[] BuildRetailEsEntry()
    {
        const int count = 2;
        const int recordSize = 0x38;
        var entry = new byte[4 + (count * recordSize) + 0x20];
        BitConverter.GetBytes(count).CopyTo(entry, 0);
        for (var index = 0; index < count; index++)
        {
            var recordOffset = 4 + (index * recordSize);
            BitConverter.GetBytes(4).CopyTo(entry, recordOffset);
            var positionOffset = recordOffset + 4;
            BitConverter.GetBytes(70f + index).CopyTo(entry, positionOffset);
            BitConverter.GetBytes(700f + index).CopyTo(entry, positionOffset + 4);
            BitConverter.GetBytes(800f + index).CopyTo(entry, positionOffset + 8);
        }

        for (var index = 0; index < 0x20; index++)
            entry[4 + (count * recordSize) + index] = (byte)(0xA0 + index);
        return entry;
    }

    private static byte[] BuildGen7EiKind10EntryForTest()
    {
        const int count = 2;
        const int recordSize = 0x5C;
        const int recordsEnd = 4 + (count * recordSize);
        var entry = new byte[recordsEnd + 0x20];
        BitConverter.GetBytes(count).CopyTo(entry, 0);
        for (var index = 0; index < count; index++)
        {
            var recordOffset = 4 + (index * recordSize);
            if (index == 0)
                BitConverter.GetBytes(10).CopyTo(entry, recordOffset);
            var positionOffset = recordOffset + 4;
            BitConverter.GetBytes(130f + index).CopyTo(entry, positionOffset);
            BitConverter.GetBytes(1130f + index).CopyTo(entry, positionOffset + 4);
            BitConverter.GetBytes(2130f + index).CopyTo(entry, positionOffset + 8);
        }

        for (var index = recordsEnd; index < entry.Length; index++)
            entry[index] = (byte)(0xB0 + index - recordsEnd);
        return entry;
    }

    private static byte[] BuildGen7PrEntry(int kind, float x, float y, float z)
    {
        var entry = new byte[0x30];
        BitConverter.GetBytes(1).CopyTo(entry, 0);
        BitConverter.GetBytes(kind).CopyTo(entry, 4);
        BitConverter.GetBytes(x).CopyTo(entry, 0x08);
        BitConverter.GetBytes(y).CopyTo(entry, 0x0C);
        BitConverter.GetBytes(z).CopyTo(entry, 0x10);
        for (var index = 0x14; index < entry.Length; index++)
            entry[index] = (byte)(0xD0 + index - 0x14);
        return entry;
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
    public void Gen6ZoneMetadataRoundTripsFlagsAudioCameraAndCoordinates()
    {
        var original = OverworldEditor.GetGen6Zone(new OverworldGen6ZoneRequest(_xyWorkspace.RomFs, 0));
        var metadata = original.Metadata! with
        {
            MapType = 7,
            MapMove = 9,
            BgmSpring = 0x01020304,
            BgmSummer = 0x11121314,
            BgmAutumn = 0x21222324,
            BgmWinter = 0x31323334,
            TownMapGroup = 42,
            OlValue = 17,
            SkyBoxEnabled = true,
            RollerSkateEnabled = true,
            BattleBackground = 63,
            MapChange = 19,
            BicycleEnabled = true,
            RunEnabled = true,
            EscapeRopeEnabled = true,
            FlyEnabled = false,
            BgmEnabled = true,
            UnknownFlag = true,
            Camera1 = 123,
            Camera2 = 456,
            CameraFlags = 0xDEADBEEFu,
            StartX = 12.5f,
            StartY = -3.25f,
            StartZ = 5,
            EndX = 1.5f,
            EndY = 2.25f,
            EndZ = -6,
        };

        var result = OverworldEditor.Export(new OverworldGen6ExportRequest(
            _xyWorkspace.RomFs, _xyWorkspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            original.Furniture, original.Npcs, original.Warps, original.Triggers,
            original.UnknownTriggers, metadata));

        using var archive = ZipFile.OpenRead(result.ZipPath);
        var archiveEntry = archive.GetEntry(
            $"luma/titles/{SyntheticWorkspace.TitleId}/romfs/{result.ChangedFiles[0]}")!;
        using var stream = archiveEntry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var garc = new GARC.MemGARC(buffer.ToArray());
        var zoneData = Mini.UnpackMini(garc.GetFile(1), "ZO")[0];

        Assert.Equal((byte)7, zoneData[0]);
        Assert.Equal((byte)9, zoneData[1]);
        Assert.Equal(0x01020304u, BitConverter.ToUInt32(zoneData, 0x08));
        Assert.Equal(0x31323334u, BitConverter.ToUInt32(zoneData, 0x14));
        Assert.Equal(42, BitConverter.ToUInt16(zoneData, 0x1A));
        Assert.Equal(17, BitConverter.ToUInt16(zoneData, 0x1C) >> 10);
        Assert.Equal(63, (BitConverter.ToUInt16(zoneData, 0x1E) >> 7) & 0x7F);
        Assert.Equal(19u, BitConverter.ToUInt32(zoneData, 0x20) & 0x1F);
        Assert.Equal(0xDEADBEEFu, BitConverter.ToUInt32(zoneData, 0x26));
        Assert.Equal(225, BitConverter.ToInt16(zoneData, 0x2C));
        Assert.Equal(-58, BitConverter.ToInt16(zoneData, 0x30));
        Assert.Equal(27, BitConverter.ToInt16(zoneData, 0x32));
        Assert.Equal(40, BitConverter.ToInt16(zoneData, 0x36));
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
    public void Gen6MapMatrixCanBeEditedWithoutTouchingTheSource()
    {
        var original = OverworldEditor.GetGen6Map(new OverworldGen6MapRequest(_xyWorkspace.RomFs, 0));
        var matrix = (ushort[])original.MatrixValues.Clone();
        matrix[0] = 0xBEEF;

        var result = OverworldEditor.ExportMap(new OverworldGen6MapExportRequest(
            _xyWorkspace.RomFs, _xyWorkspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            Properties: null, MatrixValues: matrix));

        Assert.Single(result.ChangedFiles);
        Assert.Equal("a/0/4/2", result.ChangedFiles[0]);
        ExportAssertions.AssertContentDiffersFromSource(result, _xyWorkspace);

        var reread = OverworldEditor.GetGen6Map(new OverworldGen6MapRequest(_xyWorkspace.RomFs, 0));
        Assert.Equal((ushort)120, reread.MatrixValues[0]);
    }

    [Fact]
    public void Gen6MapMatrixExportRejectsAChangedCellCount()
    {
        var original = OverworldEditor.GetGen6Map(new OverworldGen6MapRequest(_xyWorkspace.RomFs, 0));
        Assert.Throws<WorkspaceException>(() => OverworldEditor.ExportMap(new OverworldGen6MapExportRequest(
            _xyWorkspace.RomFs, _xyWorkspace.OutputDirectory, SyntheticWorkspace.TitleId, 0,
            Properties: null, MatrixValues: original.MatrixValues[..^1])));
    }

    [Fact]
    public void Gen6MapBuildsPreviewFromMiniMapGrEntries()
    {
        var mapGrPath = Path.Combine(_xyWorkspace.RomFs, "a", "0", "4", "1");
        var mapGr = new GARC.MemGARC(File.ReadAllBytes(mapGrPath));
        var mapGrFiles = mapGr.Files;
        mapGrFiles[0] = Mini.PackMini([BuildMapTile(1), [1], [2]], "GR");
        mapGrFiles[1] = Mini.PackMini([BuildMapTile(11), [3], [4]], "GR");
        mapGrFiles[10] = mapGrFiles[0];
        mapGr.Files = mapGrFiles;
        File.WriteAllBytes(mapGrPath, mapGr.Save());

        var matrixPath = Path.Combine(_xyWorkspace.RomFs, "a", "0", "4", "2");
        var matrixGarc = new GARC.MemGARC(File.ReadAllBytes(matrixPath));
        var matrixFiles = matrixGarc.Files;
        matrixFiles[20] = Mini.PackMini([BuildMapMatrix(0, 1)], "MM");
        matrixGarc.Files = matrixFiles;
        File.WriteAllBytes(matrixPath, matrixGarc.Save());

        var response = OverworldEditor.GetGen6Map(new OverworldGen6MapRequest(_xyWorkspace.RomFs, 0));

        Assert.NotNull(response.Preview);
        Assert.NotNull(response.Preview!.PngBase64);
        Assert.Equal(4, response.Preview.Width);
        Assert.Equal(2, response.Preview.Height);
        Assert.Empty(response.Properties);
        Assert.NotNull(response.Diagnostics);
        Assert.Contains("contenedor Mini", response.Diagnostics!);
        var image = PortablePng.DecodeRgba(Convert.FromBase64String(response.Preview.PngBase64!));
        Assert.Equal([0, 0, 1, 255], image.Rgba[..4]);
        Assert.Null(response.Preview.Diagnostics);
    }

    private static byte[] BuildMapTile(uint seed)
    {
        var tile = new byte[4 + (4 * sizeof(uint))];
        BitConverter.GetBytes((ushort)2).CopyTo(tile, 0);
        BitConverter.GetBytes((ushort)2).CopyTo(tile, 2);
        for (var index = 0; index < 4; index++)
            BitConverter.GetBytes(seed + (uint)index).CopyTo(tile, 4 + (index * sizeof(uint)));
        return tile;
    }

    private static byte[] BuildMapMatrix(ushort first, ushort second)
    {
        var matrix = new byte[12];
        BitConverter.GetBytes((ushort)0).CopyTo(matrix, 0);
        BitConverter.GetBytes((ushort)0).CopyTo(matrix, 2);
        BitConverter.GetBytes((ushort)2).CopyTo(matrix, 4);
        BitConverter.GetBytes((ushort)1).CopyTo(matrix, 6);
        BitConverter.GetBytes(first).CopyTo(matrix, 8);
        BitConverter.GetBytes(second).CopyTo(matrix, 10);
        return matrix;
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
