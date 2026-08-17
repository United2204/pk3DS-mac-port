using System.Security.Cryptography;
using System.Text;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Editors;

namespace pk3DS.Editors.Tests;

public sealed class ProjectToolsTests : IDisposable
{
    private readonly SyntheticXyWorkspace _workspace = new(speciesCount: 8);

    public void Dispose()
    {
        _workspace.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void BuildsStandaloneRomFsAndExeFsWithoutChangingTheWorkspace()
    {
        var sourceRomFsHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(_workspace.RomFs, "a", "0", "0", "0")));
        var sourceCodeHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(_workspace.ExeFs, "code.bin")));
        var output = Path.Combine(_workspace.OutputDirectory, "built");

        var response = ProjectTools.BuildFileSystems(new BuildFileSystemsRequest(
            _workspace.Root, output, IncludeRomFs: true, IncludeExeFs: true));

        Assert.Equal("XY", response.GameVersion);
        Assert.NotNull(response.RomFsFile);
        Assert.NotNull(response.ExeFsFile);
        Assert.True(File.Exists(response.RomFsFile));
        Assert.True(File.Exists(response.ExeFsFile));
        Assert.Equal(new FileInfo(response.RomFsFile!).Length, response.RomFsBytes);
        Assert.Equal(new FileInfo(response.ExeFsFile!).Length, response.ExeFsBytes);
        Assert.True(new FileInfo(response.RomFsFile!).Length > 0x200);
        Assert.True(new FileInfo(response.ExeFsFile!).Length > 0x200);
        Assert.Equal(sourceRomFsHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(_workspace.RomFs, "a", "0", "0", "0"))));
        Assert.Equal(sourceCodeHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(_workspace.ExeFs, "code.bin"))));
    }

    [Fact]
    public void CanBuildOnlyTheRequestedFileSystem()
    {
        var output = Path.Combine(_workspace.OutputDirectory, "romfs-only");

        var response = ProjectTools.BuildFileSystems(new BuildFileSystemsRequest(
            _workspace.RomFs, output, IncludeRomFs: true, IncludeExeFs: false));

        Assert.NotNull(response.RomFsFile);
        Assert.Null(response.ExeFsFile);
        Assert.True(File.Exists(response.RomFsFile));
        Assert.False(File.Exists(Path.Combine(output, "exefs.bin")));
    }

    [Fact]
    public void OutputInsideTheSourceRomFsIsRejected()
    {
        var output = Path.Combine(_workspace.RomFs, "generated");

        Assert.Throws<WorkspaceException>(() => ProjectTools.BuildFileSystems(
            new BuildFileSystemsRequest(_workspace.Root, output)));
    }

    [Fact]
    public void AtLeastOneFileSystemMustBeSelected() =>
        Assert.Throws<WorkspaceException>(() => ProjectTools.BuildFileSystems(
            new BuildFileSystemsRequest(_workspace.Root, _workspace.OutputDirectory, false, false)));

    [Fact]
    public void ExtractsAStandaloneCxiIntoAWorkspace()
    {
        var cxi = CreateCxi();
        var output = Path.Combine(_workspace.OutputDirectory, "cxi-extracted");

        var response = ProjectTools.ExtractProject(new ExtractProjectRequest(cxi, output));

        Assert.Equal("CXI", response.Format);
        Assert.Contains("exheader.bin", response.Files);
        Assert.Contains("exefs/code.bin", response.Files);
        Assert.Contains("romfs/a/0/0/0", response.Files);
        Assert.Equal(SyntheticWorkspace.TitleId,
            new Exheader(Path.Combine(output, "exheader.bin")).TitleID.ToString("X16"));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(_workspace.ExeFs, "code.bin")),
            File.ReadAllBytes(Path.Combine(output, "exefs", "code.bin")));
    }

    [Fact]
    public void ExtractsTheFirstCxiFromAThreeDsWithoutOverreadingIt()
    {
        var cxi = CreateCxi();
        var ncsd = Path.Combine(_workspace.OutputDirectory, "fixture.3ds");
        CreateNcsd(cxi, ncsd);
        var output = Path.Combine(_workspace.OutputDirectory, "3ds-extracted");

        var response = ProjectTools.ExtractProject(new ExtractProjectRequest(ncsd, output));

        Assert.Equal("3DS", response.Format);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(_workspace.ExeFs, "code.bin")),
            File.ReadAllBytes(Path.Combine(output, "exefs", "code.bin")));
        Assert.True(File.Exists(Path.Combine(output, "romfs", "a", "0", "0", "0")));
    }

    [Fact]
    public void InvalidProjectFilesAreRejectedBeforeCreatingAnOutput()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "not-a-project.bin");
        File.WriteAllBytes(input, new byte[0x104]);
        var output = Path.Combine(_workspace.OutputDirectory, "should-not-exist");

        Assert.Throws<WorkspaceException>(() => ProjectTools.ExtractProject(
            new ExtractProjectRequest(input, output)));
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void RebuildsATrimmedThreeDsFromACompleteWorkspace()
    {
        var exheader = new byte[0x800];
        BitConverter.GetBytes(0x0004000000055D00UL).CopyTo(exheader, 0x200);
        File.WriteAllBytes(Path.Combine(_workspace.Root, "exheader.bin"), exheader);
        var sourceCodeHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(_workspace.ExeFs, "code.bin")));
        var output = Path.Combine(_workspace.OutputDirectory, "rebuilt.3ds");

        var response = ProjectTools.RebuildRom(new RebuildRomRequest(
            _workspace.Root, output, Trimmed: true));

        Assert.Equal("XY", response.GameVersion);
        Assert.True(response.Trimmed);
        Assert.Equal(new FileInfo(output).Length, response.Bytes);
        Assert.True(new FileInfo(output).Length > 0x4000);
        using var stream = File.OpenRead(output);
        stream.Position = 0x100;
        Assert.Equal(0x4453434Eu, ReadUInt32(stream));
        stream.Position = 0x4000 + 0x100;
        Assert.Equal(0x4843434Eu, ReadUInt32(stream));
        Assert.Equal(sourceCodeHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(_workspace.ExeFs, "code.bin"))));
    }

    [Fact]
    public void CiaRebuildRequiresMakeromBeforeCreatingAnOutput()
    {
        var output = Path.Combine(_workspace.OutputDirectory, "missing-makerom.cia");

        Assert.Throws<WorkspaceException>(() => ProjectTools.RebuildCia(new RebuildCiaRequest(
            _workspace.Root,
            output,
            MakeromPath: Path.Combine(_workspace.OutputDirectory, "makerom-does-not-exist"))));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void CreatesARedirectPatchWithoutChangingTheWorkspace()
    {
        var sourceCode = Path.Combine(_workspace.ExeFs, "code.bin");
        File.WriteAllText(sourceCode, "rom2:/base\0rom:/a/0/0/5\0", Encoding.Unicode);
        var sourceCodeHash = SHA256.HashData(File.ReadAllBytes(sourceCode));
        var sourceGarc = Path.Combine(_workspace.RomFs, "a", "0", "0", "5");
        var output = Path.Combine(_workspace.OutputDirectory, "redirect-patch");

        var response = ProjectTools.CreateRedirectPatch(new RedirectPatchRequest(
            _workspace.Root,
            ["movesprite"],
            output));

        Assert.Equal("XY", response.GameVersion);
        Assert.Equal(1, response.RedirectedPaths);
        Assert.True(File.Exists(Path.Combine(output, ".code.bin")));
        Assert.True(File.Exists(Path.Combine(output, "a0", "0", "5")));
        Assert.Contains("rom2:/a0/0/5", File.ReadAllText(Path.Combine(output, ".code.bin"), Encoding.Unicode));
        Assert.DoesNotContain("rom:/a/0/0/5", File.ReadAllText(Path.Combine(output, ".code.bin"), Encoding.Unicode));
        Assert.Equal(File.ReadAllBytes(sourceGarc), File.ReadAllBytes(Path.Combine(output, "a0", "0", "5")));
        Assert.Equal(sourceCodeHash, SHA256.HashData(File.ReadAllBytes(sourceCode)));
    }

    [Fact]
    public void PacksAndUnpacksAGarcWithoutChangingTheInputFolder()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "garc-input");
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "0.bin"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(input, "1.bin"), [4, 5, 6, 7]);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "0.bin")));
        var packed = Path.Combine(_workspace.OutputDirectory, "fixture.garc");
        var unpacked = Path.Combine(_workspace.OutputDirectory, "garc-unpacked");

        var packResponse = ProjectTools.PackGarc(new PackGarcRequest(input, packed, Version: 6));
        var unpackResponse = ProjectTools.UnpackGarc(new UnpackGarcRequest(packed, unpacked, SkipDecompression: true));

        Assert.Equal(2, packResponse.Files);
        Assert.Equal(2, unpackResponse.Files);
        Assert.True(File.Exists(packed));
        Assert.Equal(File.ReadAllBytes(Path.Combine(input, "0.bin")), File.ReadAllBytes(Path.Combine(unpacked, "0.bin")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(input, "1.bin")), File.ReadAllBytes(Path.Combine(unpacked, "1.bin")));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "0.bin"))));
    }

    [Fact]
    public void PacksAndUnpacksASingleLayerDarcWithoutChangingTheInputFolder()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "darc-input");
        var group = Path.Combine(input, "group");
        var other = Path.Combine(input, "other");
        Directory.CreateDirectory(group);
        Directory.CreateDirectory(other);
        File.WriteAllBytes(Path.Combine(group, "one.bin"), [8, 9, 10]);
        File.WriteAllBytes(Path.Combine(group, "two.bin"), [11, 12]);
        File.WriteAllBytes(Path.Combine(other, "three.bin"), [13, 14, 15, 16]);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(group, "one.bin")));
        var packed = Path.Combine(_workspace.OutputDirectory, "fixture.darc");
        var unpacked = Path.Combine(_workspace.OutputDirectory, "darc-unpacked");

        var packResponse = ProjectTools.PackDarc(new PackDarcRequest(input, packed));
        var unpackResponse = ProjectTools.UnpackDarc(new UnpackDarcRequest(packed, unpacked));

        Assert.Equal(3, packResponse.Files);
        Assert.Equal(3, unpackResponse.Files);
        Assert.True(File.Exists(packed));
        Assert.Equal(File.ReadAllBytes(Path.Combine(group, "one.bin")), File.ReadAllBytes(Path.Combine(unpacked, "group", "one.bin")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(group, "two.bin")), File.ReadAllBytes(Path.Combine(unpacked, "group", "two.bin")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(other, "three.bin")), File.ReadAllBytes(Path.Combine(unpacked, "other", "three.bin")));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(group, "one.bin"))));
    }

    [Fact]
    public void PacksAndUnpacksASarcWithRootAndNestedFilesWithoutChangingTheInputFolder()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "sarc-input");
        var nested = Path.Combine(input, "folder", "subfolder");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(input, "root.bin"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(nested, "á.bin"), [4, 5, 6, 7]);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(nested, "á.bin")));
        var packed = Path.Combine(_workspace.OutputDirectory, "fixture.sarc");
        var unpacked = Path.Combine(_workspace.OutputDirectory, "sarc-unpacked");

        var packResponse = ProjectTools.PackSarc(new PackSarcRequest(input, packed, DataAlignment: 0x10));
        var unpackResponse = ProjectTools.UnpackSarc(new UnpackSarcRequest(packed, unpacked));

        Assert.Equal(2, packResponse.Files);
        Assert.Equal(2, unpackResponse.Files);
        Assert.Equal(0x10, packResponse.DataAlignment);
        Assert.True(File.Exists(packed));
        Assert.Equal(File.ReadAllBytes(Path.Combine(input, "root.bin")), File.ReadAllBytes(Path.Combine(unpacked, "root.bin")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(nested, "á.bin")), File.ReadAllBytes(Path.Combine(unpacked, "folder", "subfolder", "á.bin")));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(nested, "á.bin"))));

        using var sarc = new SARC(packed);
        Assert.True(sarc.SigMatches);
        Assert.Equal(2, sarc.SFAT.EntryCount);
        Assert.Contains(sarc.SFAT.Entries, entry => sarc.GetFileName(entry) == "root.bin");
        Assert.Contains(sarc.SFAT.Entries, entry => sarc.GetFileName(entry) == Path.Combine("folder", "subfolder", "á.bin"));
    }

    [Fact]
    public void UnpacksAFarcWithUtf16NamesAndKeepsTheOriginalIntact()
    {
        var packed = Path.Combine(_workspace.OutputDirectory, "fixture.farc");
        var unpacked = Path.Combine(_workspace.OutputDirectory, "farc-unpacked");
        CreateFarc(packed,
            ("root.bin", [21, 22, 23]),
            (Path.Combine("folder", "inner.bin"), [24, 25, 26, 27]));
        var sourceHash = SHA256.HashData(File.ReadAllBytes(packed));

        var response = ProjectTools.UnpackFarc(new UnpackFarcRequest(packed, unpacked));

        Assert.Equal(2, response.Files);
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(packed)));
        Assert.Equal([21, 22, 23], File.ReadAllBytes(Path.Combine(unpacked, "root.bin")));
        Assert.Equal([24, 25, 26, 27], File.ReadAllBytes(Path.Combine(unpacked, "folder", "inner.bin")));
        using var farc = new FARC(packed);
        Assert.True(farc.Valid);
        Assert.Equal(2u, farc.FileCount);
        Assert.Contains(farc.Files, file => farc.GetFileName(file) == "root.bin");
    }

    [Fact]
    public void PacksNamedFarcWithUtf16PathsAndRoundTripsWithoutChangingTheSource()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "farc-input");
        var nested = Path.Combine(input, "folder", "subfolder");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(input, "raíz.bin"), [31, 32, 33]);
        File.WriteAllBytes(Path.Combine(nested, "á.bin"), [34, 35, 36, 37]);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(nested, "á.bin")));
        var packed = Path.Combine(_workspace.OutputDirectory, "packed.farc");
        var unpacked = Path.Combine(_workspace.OutputDirectory, "packed-farc-unpacked");

        var packResponse = ProjectTools.PackFarc(new PackFarcRequest(input, packed, DataAlignment: 0x80));
        var unpackResponse = ProjectTools.UnpackFarc(new UnpackFarcRequest(packed, unpacked));

        Assert.Equal(2, packResponse.Files);
        Assert.Equal(2, unpackResponse.Files);
        Assert.Equal(0x80, packResponse.DataAlignment);
        Assert.True(File.Exists(packed));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(nested, "á.bin"))));
        Assert.Equal([31, 32, 33], File.ReadAllBytes(Path.Combine(unpacked, "raíz.bin")));
        Assert.Equal([34, 35, 36, 37], File.ReadAllBytes(Path.Combine(unpacked, "folder", "subfolder", "á.bin")));

        using var farc = new FARC(packed);
        Assert.True(farc.SigMatches);
        Assert.Equal(2u, farc.FileCount);
        Assert.Equal(0u, farc.DataOffset % 0x80u);
        Assert.Contains(farc.Files, file => farc.GetFileName(file) == "raíz.bin");
        Assert.Contains(farc.Files, file => farc.GetFileName(file) == Path.Combine("folder", "subfolder", "á.bin"));
    }

    [Fact]
    public void DecodesPortableBclimAndEncodesRgbaPng()
    {
        var pixelData = Enumerable.Repeat(new byte[] { 255, 0, 0, 255 }, 64).SelectMany(value => value).ToArray();
        var bclim = BCLIMPortable.Read(CreateBclim(8, 8, XLIMEncoding.RGBA8, pixelData));

        var rgba = bclim.GetRgbaData();
        var png = PortablePng.EncodeRgba(rgba, bclim.Width, bclim.Height);

        Assert.Equal(8, bclim.Width);
        Assert.Equal(8, bclim.Height);
        Assert.Equal([255, 0, 0, 255], rgba[..4]);
        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], png[..8]);
        Assert.Equal(8u, ReadBigEndian(png, 16));
        Assert.Equal(8u, ReadBigEndian(png, 20));

        var decodedPng = PortablePng.DecodeRgba(png);
        Assert.Equal(8, decodedPng.Width);
        Assert.Equal(8, decodedPng.Height);
        Assert.Equal(rgba, decodedPng.Rgba);

        var encodedBclim = BCLIMPortable.EncodeRgba(decodedPng.Rgba, decodedPng.Width, decodedPng.Height);
        Assert.Equal(rgba, BCLIMPortable.Read(encodedBclim).GetRgbaData());

        var smallRgba = Enumerable.Range(0, 6)
            .SelectMany(index => new byte[] { (byte)(index * 20), 100, 200, 255 })
            .ToArray();
        var smallRoundTrip = BCLIMPortable.Read(BCLIMPortable.EncodeRgba(smallRgba, 3, 2));
        Assert.Equal(3, smallRoundTrip.Width);
        Assert.Equal(2, smallRoundTrip.Height);
        Assert.Equal(smallRgba, smallRoundTrip.GetRgbaData());
    }

    [Fact]
    public void DecodesPortableEtc1AndEtc1A4Bclim()
    {
        var colorBlock = CreateEtc1Block(red: 8, green: 4, blue: 2);
        var etc1Payload = Enumerable.Repeat(colorBlock, 4).SelectMany(value => value).ToArray();
        var etc1Rgba = BCLIMPortable.Read(CreateBclim(8, 8, XLIMEncoding.ETC1, etc1Payload)).GetRgbaData();

        for (var offset = 0; offset < etc1Rgba.Length; offset += 4)
            Assert.Equal([138, 70, 36, 255], etc1Rgba.AsSpan(offset, 4).ToArray());

        var alphaBlock = CreateEtc1AlphaBlock();
        var etc1A4Payload = Enumerable.Range(0, 4)
            .SelectMany(_ => alphaBlock.Concat(colorBlock))
            .ToArray();
        var etc1A4Rgba = BCLIMPortable.Read(CreateBclim(8, 8, XLIMEncoding.ETC1A4, etc1A4Payload)).GetRgbaData();

        Assert.Equal([138, 70, 36, 51], etc1A4Rgba[..4]);
        Assert.Equal([138, 70, 36, 34], etc1A4Rgba[(8 * 4)..(9 * 4)]);
        Assert.Equal([138, 70, 36, 17], etc1A4Rgba[(16 * 4)..(17 * 4)]);
        Assert.Equal([138, 70, 36, 0], etc1A4Rgba[(24 * 4)..(25 * 4)]);
    }

    [Fact]
    public void DecodesPortableEtc1TileOrderForRectangularImages()
    {
        var compressedBlocks = Enumerable.Range(0, 8)
            .SelectMany(index => CreateEtc1Block((byte)(index + 1), green: 4, blue: 8))
            .ToArray();
        var rgba = BCLIMPortable.Read(CreateBclim(16, 8, XLIMEncoding.ETC1, compressedBlocks)).GetRgbaData();
        // The ETC1 tile permutation is followed by the format's vertical orientation.
        int[] tileScramble = [2, 3, 6, 7, 0, 1, 4, 5];

        for (var outputTile = 0; outputTile < tileScramble.Length; outputTile++)
        {
            var x = (outputTile % 4) * 4;
            var y = (outputTile / 4) * 4;
            var sourceBlock = tileScramble[outputTile];
            var offset = ((x + (y * 16)) * 4);
            Assert.Equal((byte)(17 * (sourceBlock + 1) + 2), rgba[offset]);
            Assert.Equal(70, rgba[offset + 1]);
            Assert.Equal(138, rgba[offset + 2]);
            Assert.Equal(255, rgba[offset + 3]);
        }
    }

    [Fact]
    public void PreviewsAndExportsAnEtc1TitleScreenAsset()
    {
        _workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 467);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        var colorBlock = CreateEtc1Block(red: 8, green: 4, blue: 2);
        var payload = Enumerable.Repeat(colorBlock, 4).SelectMany(value => value).ToArray();
        ReplaceGarcDarcEntry(catalog.GarcPath, archive.FileNumber, asset.EntryIndex,
            CreateBclim(8, 8, XLIMEncoding.ETC1, payload));

        var preview = TitleScreenEditor.Preview(new TitleScreenPreviewRequest(
            _workspace.Root, archive.FileNumber, asset.EntryIndex));
        var previewImage = PortablePng.DecodeRgba(Convert.FromBase64String(preview.PngBase64));
        Assert.Equal("ETC1", preview.BclimFormat);
        Assert.Equal([138, 70, 36, 255], previewImage.Rgba[..4]);

        var output = Path.Combine(_workspace.OutputDirectory, "etc1-title-screen");
        var export = TitleScreenEditor.Export(new TitleScreenExportRequest(
            _workspace.Root, output, archive.FileNumber, IncludePng: true));
        Assert.Equal(1, export.Pngs);
        var png = export.Files.Single(file => file.EndsWith("background.png", StringComparison.Ordinal));
        var exportedImage = PortablePng.DecodeRgba(File.ReadAllBytes(Path.Combine(output, png.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal([138, 70, 36, 255], exportedImage.Rgba[..4]);
    }

    [Fact]
    public void GeneratesAnInMemoryPreviewForAPortableTitleScreenBclim()
    {
        _workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 467);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");

        var response = TitleScreenEditor.Preview(new TitleScreenPreviewRequest(
            _workspace.Root,
            archive.FileNumber,
            asset.EntryIndex));

        Assert.Equal("XY", response.GameVersion);
        Assert.Equal("RGBA8", response.BclimFormat);
        var image = PortablePng.DecodeRgba(Convert.FromBase64String(response.PngBase64));
        Assert.Equal(8, image.Width);
        Assert.Equal(8, image.Height);
        Assert.Equal([255, 0, 0, 255], image.Rgba[..4]);
    }

    [Fact]
    public void ReplacingOneDarcFileKeepsFollowingFileDataAligned()
    {
        var source = Path.Combine(_workspace.OutputDirectory, "darc-source");
        var folder = Path.Combine(source, "group");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "first.bin"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(folder, "second.bin"), [4, 5, 6, 7]);

        var darc = DARC.GetDARC(source);
        var firstIndex = Array.FindIndex(darc.FileNameTable, entry => entry.FileName == "first.bin");
        var secondIndex = Array.FindIndex(darc.FileNameTable, entry => entry.FileName == "second.bin");
        Assert.True(firstIndex >= 0);
        Assert.True(secondIndex >= 0);
        Assert.True(DARC.InsertFile(ref darc, firstIndex, [9, 8, 7, 6, 5]));

        var rebuilt = new DARC(DARC.SetDARC(darc));
        Assert.Equal([9, 8, 7, 6, 5], ReadDarcEntry(rebuilt, firstIndex));
        Assert.Equal([4, 5, 6, 7], ReadDarcEntry(rebuilt, secondIndex));
    }

    [Fact]
    public void ReplacesPortableTitleScreenPngInANewDarcWithoutChangingTheWorkspace()
    {
        _workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 467);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        var sourceHash = SHA256.HashData(File.ReadAllBytes(catalog.GarcPath));
        var replacementRgba = Enumerable.Repeat(new byte[] { 0, 255, 0, 255 }, 64).SelectMany(value => value).ToArray();
        var replacement = Path.Combine(_workspace.OutputDirectory, "replacement.png");
        File.WriteAllBytes(replacement, PortablePng.EncodeRgba(replacementRgba, 8, 8));
        var output = Path.Combine(_workspace.OutputDirectory, "replaced.darc");

        var response = TitleScreenEditor.Replace(new TitleScreenReplaceRequest(
            _workspace.Root,
            archive.FileNumber,
            asset.EntryIndex,
            replacement,
            output));

        Assert.Equal("PNG", response.ReplacementFormat);
        Assert.Equal(XLIMEncoding.RGBA8.ToString(), response.BclimFormat);
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(catalog.GarcPath)));
        Assert.True(File.Exists(response.OutputFile));
        var darc = new DARC(File.ReadAllBytes(response.OutputFile));
        var entryIndex = Array.FindIndex(darc.FileNameTable, entry => entry.FileName == "background.bclim");
        Assert.True(entryIndex >= 0);
        var replacedImage = BCLIMPortable.Read(ReadDarcEntry(darc, entryIndex));
        Assert.Equal(replacementRgba, replacedImage.GetRgbaData());
    }

    [Fact]
    public void ReplacesPortableTitleScreenPngInANewGarcWithoutChangingTheWorkspace()
    {
        _workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 467);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        var sourceHash = SHA256.HashData(File.ReadAllBytes(catalog.GarcPath));
        var replacementRgba = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 64).SelectMany(value => value).ToArray();
        var replacement = Path.Combine(_workspace.OutputDirectory, "replacement-garc.png");
        File.WriteAllBytes(replacement, PortablePng.EncodeRgba(replacementRgba, 8, 8));
        var output = Path.Combine(_workspace.OutputDirectory, "replaced.garc");

        var response = TitleScreenEditor.ReplaceGarc(new TitleScreenReplaceRequest(
            _workspace.Root,
            archive.FileNumber,
            asset.EntryIndex,
            replacement,
            output));

        Assert.False(response.Compressed);
        Assert.Equal("PNG", response.ReplacementFormat);
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(catalog.GarcPath)));
        var replacedGarc = new GARC.MemGARC(File.ReadAllBytes(response.OutputFile));
        var replacedDarc = new DARC(replacedGarc.GetFile(archive.FileNumber));
        var entryIndex = Array.FindIndex(replacedDarc.FileNameTable, entry => entry.FileName == "background.bclim");
        Assert.True(entryIndex >= 0);
        Assert.Equal(replacementRgba, BCLIMPortable.Read(ReadDarcEntry(replacedDarc, entryIndex)).GetRgbaData());
    }

    [Fact]
    public void AppliesPortableTitleScreenPngToWorkspaceAndKeepsBackup()
    {
        _workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 467);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        var originalGarc = File.ReadAllBytes(catalog.GarcPath);
        var replacementRgba = Enumerable.Repeat(new byte[] { 0, 255, 0, 255 }, 64).SelectMany(value => value).ToArray();
        var replacement = Path.Combine(_workspace.OutputDirectory, "workspace-replacement.png");
        File.WriteAllBytes(replacement, PortablePng.EncodeRgba(replacementRgba, 8, 8));

        var response = TitleScreenEditor.Apply(new TitleScreenApplyRequest(
            _workspace.Root, archive.FileNumber, asset.EntryIndex, replacement));

        Assert.Equal(catalog.GarcPath, response.GarcPath);
        Assert.False(response.Compressed);
        Assert.True(File.Exists(response.BackupFile));
        Assert.Equal(originalGarc, File.ReadAllBytes(response.BackupFile));
        var updatedGarc = new GARC.MemGARC(File.ReadAllBytes(catalog.GarcPath));
        var updatedDarc = new DARC(updatedGarc.GetFile(archive.FileNumber));
        var entryIndex = Array.FindIndex(updatedDarc.FileNameTable, entry => entry.FileName == "background.bclim");
        Assert.True(entryIndex >= 0);
        Assert.Equal(replacementRgba, BCLIMPortable.Read(ReadDarcEntry(updatedDarc, entryIndex)).GetRgbaData());
    }

    [Fact]
    public void AppliesPortableTitleScreenPngToCompressedOrasWorkspace()
    {
        using var workspace = new SyntheticOrasWorkspace();
        workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 1120);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        var originalGarc = File.ReadAllBytes(catalog.GarcPath);
        var replacementRgba = Enumerable.Repeat(new byte[] { 255, 255, 0, 255 }, 64).SelectMany(value => value).ToArray();
        var replacement = Path.Combine(workspace.OutputDirectory, "workspace-replacement-oras.png");
        File.WriteAllBytes(replacement, PortablePng.EncodeRgba(replacementRgba, 8, 8));

        var response = TitleScreenEditor.Apply(new TitleScreenApplyRequest(
            workspace.Root, archive.FileNumber, asset.EntryIndex, replacement));

        Assert.True(response.Compressed);
        Assert.Equal(originalGarc, File.ReadAllBytes(response.BackupFile));
        var updatedGarc = new GARC.MemGARC(File.ReadAllBytes(catalog.GarcPath));
        var compressedArchive = updatedGarc.GetFile(archive.FileNumber);
        Assert.Equal(0x11, compressedArchive[0]);
        using var compressed = new MemoryStream(compressedArchive);
        using var decompressed = new MemoryStream();
        LZSS.Decompress(compressed, compressed.Length, decompressed);
        var updatedDarc = new DARC(decompressed.ToArray());
        var entryIndex = Array.FindIndex(updatedDarc.FileNameTable, entry => entry.FileName == "background.bclim");
        Assert.True(entryIndex >= 0);
        Assert.Equal(replacementRgba, BCLIMPortable.Read(ReadDarcEntry(updatedDarc, entryIndex)).GetRgbaData());
    }

    [Fact]
    public void ReplacesPortableTitleScreenPngInANewCompressedOrasGarc()
    {
        using var workspace = new SyntheticOrasWorkspace();
        workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 1120);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        var replacementRgba = Enumerable.Repeat(new byte[] { 255, 255, 0, 255 }, 64).SelectMany(value => value).ToArray();
        var replacement = Path.Combine(workspace.OutputDirectory, "replacement-oras.png");
        File.WriteAllBytes(replacement, PortablePng.EncodeRgba(replacementRgba, 8, 8));
        var output = Path.Combine(workspace.OutputDirectory, "replaced-oras.garc");

        var response = TitleScreenEditor.ReplaceGarc(new TitleScreenReplaceRequest(
            workspace.Root,
            archive.FileNumber,
            asset.EntryIndex,
            replacement,
            output));

        Assert.True(response.Compressed);
        var replacedGarc = new GARC.MemGARC(File.ReadAllBytes(response.OutputFile));
        using var compressed = new MemoryStream(replacedGarc.GetFile(archive.FileNumber));
        using var decompressed = new MemoryStream();
        LZSS.Decompress(compressed, compressed.Length, decompressed);
        var replacedDarc = new DARC(decompressed.ToArray());
        var entryIndex = Array.FindIndex(replacedDarc.FileNameTable, entry => entry.FileName == "background.bclim");
        Assert.True(entryIndex >= 0);
        Assert.Equal(replacementRgba, BCLIMPortable.Read(ReadDarcEntry(replacedDarc, entryIndex)).GetRgbaData());
    }

    [Fact]
    public void InventoriesTitleScreenDarcAndBclimAssetsWithoutDecodingImages()
    {
        _workspace.WriteTitleScreenFixture();

        var response = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));

        Assert.Equal("XY", response.GameVersion);
        Assert.EndsWith(Path.Combine("a", "1", "6", "5"), response.GarcPath);
        Assert.Equal(14, response.Archives.Length);
        var archive = Assert.Single(response.Archives, entry => entry.FileNumber == 467);
        Assert.True(archive.Valid);
        Assert.False(archive.Compressed);
        Assert.Equal(2, archive.Assets.Length);
        Assert.Contains(archive.Assets, asset => asset.Name == "background.bclim" && asset.Bytes == 4);
        Assert.Contains(archive.Assets, asset => asset.Name == "logo.bclim" && asset.Bytes == 3);
        Assert.Contains(response.Archives, entry => entry.FileNumber == 468 && !entry.Valid);
    }

    [Fact]
    public void ExportsSelectedTitleScreenArchiveAndWritesManifest()
    {
        _workspace.WriteTitleScreenFixture();
        var output = Path.Combine(_workspace.OutputDirectory, "title-screen");

        var response = TitleScreenEditor.Export(new TitleScreenExportRequest(
            _workspace.Root, output, FileNumber: 467));

        Assert.Equal(1, response.Archives);
        Assert.Equal(2, response.Assets);
        Assert.Contains("manifest.json", response.Files);
        Assert.True(File.Exists(Path.Combine(output, "X-DE", "X-DE.darc")));
        var background = response.Files.Single(file => file.EndsWith("background.bclim", StringComparison.Ordinal));
        var logo = response.Files.Single(file => file.EndsWith("logo.bclim", StringComparison.Ordinal));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(Path.Combine(output, background.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal([5, 6, 7], File.ReadAllBytes(Path.Combine(output, logo.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Contains("background.bclim", File.ReadAllText(Path.Combine(output, "manifest.json")));
    }

    [Fact]
    public void ExportsPortableTitleScreenPngPreviewAlongsideTheRawAsset()
    {
        _workspace.WritePortableTitleScreenFixture();
        var output = Path.Combine(_workspace.OutputDirectory, "title-screen-png");

        var response = TitleScreenEditor.Export(new TitleScreenExportRequest(
            _workspace.Root,
            output,
            FileNumber: 467,
            IncludePng: true));

        Assert.Equal(1, response.Pngs);
        var png = response.Files.Single(file => file.EndsWith("background.png", StringComparison.Ordinal));
        var image = PortablePng.DecodeRgba(File.ReadAllBytes(Path.Combine(output, png.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(8, image.Width);
        Assert.Equal(8, image.Height);
        Assert.Equal([255, 0, 0, 255], image.Rgba[..4]);
    }

    [Fact]
    public void ReadsCompressedOrasTitleScreenArchives()
    {
        using var workspace = new SyntheticOrasWorkspace();
        workspace.WriteTitleScreenFixture();

        var response = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(workspace.Root));

        Assert.Equal("ORAS", response.GameVersion);
        var archive = Assert.Single(response.Archives, entry => entry.FileNumber == 1120);
        Assert.Equal("OR", archive.Game);
        Assert.Equal("JP1", archive.Language);
        Assert.True(archive.Compressed);
        Assert.True(archive.Valid);
        Assert.Equal(2, archive.Assets.Length);
    }

    private string CreateCxi()
    {
        var built = ProjectTools.BuildFileSystems(new BuildFileSystemsRequest(
            _workspace.Root,
            Path.Combine(_workspace.OutputDirectory, "cxi-input"),
            IncludeRomFs: true,
            IncludeExeFs: true));
        var romFs = File.ReadAllBytes(built.RomFsFile!);
        var exeFs = File.ReadAllBytes(built.ExeFsFile!);
        var exheader = new byte[0x800];
        BitConverter.GetBytes(Convert.ToUInt64(SyntheticWorkspace.TitleId, 16)).CopyTo(exheader, 0x200);

        const int exheaderOffset = 0x200;
        const int exeFsOffset = 0xA00;
        var romFsOffset = Align(exeFsOffset + exeFs.Length, 0x200);
        var totalLength = checked(romFsOffset + romFs.Length);
        var cxi = new byte[totalLength];
        var header = new byte[0x200];
        BitConverter.GetBytes(0x4843434Eu).CopyTo(header, 0x100);
        BitConverter.GetBytes((uint)(totalLength / 0x200)).CopyTo(header, 0x104);
        BitConverter.GetBytes(Convert.ToUInt64(SyntheticWorkspace.TitleId, 16)).CopyTo(header, 0x108);
        BitConverter.GetBytes(0x400u).CopyTo(header, 0x180);
        BitConverter.GetBytes((uint)(exeFsOffset / 0x200)).CopyTo(header, 0x1A0);
        BitConverter.GetBytes((uint)(exeFs.Length / 0x200)).CopyTo(header, 0x1A4);
        BitConverter.GetBytes((uint)(romFsOffset / 0x200)).CopyTo(header, 0x1B0);
        BitConverter.GetBytes((uint)(romFs.Length / 0x200)).CopyTo(header, 0x1B4);
        header.CopyTo(cxi, 0);
        exheader.CopyTo(cxi, exheaderOffset);
        exeFs.CopyTo(cxi, exeFsOffset);
        romFs.CopyTo(cxi, romFsOffset);

        var path = Path.Combine(_workspace.OutputDirectory, "fixture.cxi");
        File.WriteAllBytes(path, cxi);
        return path;
    }

    private static void CreateNcsd(string cxiPath, string outputPath)
    {
        var cxi = File.ReadAllBytes(cxiPath);
        var ncsd = new byte[0x4000 + cxi.Length];
        BitConverter.GetBytes(0x4453434Eu).CopyTo(ncsd, 0x100);
        BitConverter.GetBytes(0x20u).CopyTo(ncsd, 0x120);
        BitConverter.GetBytes((uint)(cxi.Length / 0x200)).CopyTo(ncsd, 0x124);
        cxi.CopyTo(ncsd, 0x4000);
        File.WriteAllBytes(outputPath, ncsd);
    }

    private static void CreateFarc(string outputPath, params (string Name, byte[] Data)[] files)
    {
        const int sirOffset = 0x30;
        const int metaPointer = 0x40;
        const int tableOffset = 0x50;
        const int namesOffset = 0x80;
        const int dataOffset = 0x100;
        var namesLength = files.Sum(file => Encoding.Unicode.GetByteCount(file.Name) + 2);
        var dataLength = files.Sum(file => file.Data.Length);
        var totalLength = Math.Max(namesOffset + namesLength, dataOffset + dataLength);
        var bytes = new byte[totalLength];
        using var stream = new MemoryStream(bytes, writable: true);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        void WriteUInt32At(int offset, uint value)
        {
            stream.Position = offset;
            writer.Write(value);
        }

        WriteUInt32At(0, 0x43524146);
        WriteUInt32At(0x24, sirOffset);
        WriteUInt32At(0x2C, dataOffset);
        WriteUInt32At(sirOffset, 0x30524953);
        WriteUInt32At(sirOffset + 4, metaPointer - sirOffset);
        WriteUInt32At(metaPointer, tableOffset - sirOffset);
        WriteUInt32At(metaPointer + 4, (uint)files.Length);

        var namePosition = namesOffset;
        var dataPosition = dataOffset;
        for (var i = 0; i < files.Length; i++)
        {
            var tableEntry = tableOffset + (i * 0x10);
            WriteUInt32At(tableEntry, (uint)(namePosition - sirOffset));
            WriteUInt32At(tableEntry + 4, (uint)(dataPosition - dataOffset));
            WriteUInt32At(tableEntry + 8, (uint)files[i].Data.Length);
            stream.Position = namePosition;
            writer.Write(Encoding.Unicode.GetBytes(files[i].Name));
            writer.Write((ushort)0);
            namePosition += Encoding.Unicode.GetByteCount(files[i].Name) + 2;
            stream.Position = dataPosition;
            writer.Write(files[i].Data);
            dataPosition += files[i].Data.Length;
        }

        File.WriteAllBytes(outputPath, bytes);
    }

    private static byte[] CreateBclim(int width, int height, XLIMEncoding format, byte[] pixelData)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(pixelData);
        writer.Write(0x4D494C43u); // CLIM
        writer.Write((ushort)0xFEFF);
        writer.Write(0x14u);
        writer.Write((ushort)0x0202);
        writer.Write((uint)(pixelData.Length + CLIMHeader.SIZE));
        writer.Write(1u);
        writer.Write(0x67616D69u); // imag
        writer.Write(0x10u);
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((uint)format);
        writer.Write((uint)pixelData.Length);
        return stream.ToArray();
    }

    private static byte[] CreateEtc1Block(byte red, byte green, byte blue, bool flipped = false)
    {
        var high = ((uint)(red & 0x0F) << 28) |
            ((uint)(red & 0x0F) << 24) |
            ((uint)(green & 0x0F) << 20) |
            ((uint)(green & 0x0F) << 16) |
            ((uint)(blue & 0x0F) << 12) |
            ((uint)(blue & 0x0F) << 8) |
            (flipped ? 1u : 0u);
        var standard = new byte[8];
        standard[0] = (byte)(high >> 24);
        standard[1] = (byte)(high >> 16);
        standard[2] = (byte)(high >> 8);
        standard[3] = (byte)high;
        // A zero modulation word selects the first modifier for every pixel.
        return [standard[7], standard[6], standard[5], standard[4], standard[3], standard[2], standard[1], standard[0]];
    }

    private static byte[] CreateEtc1AlphaBlock()
    {
        var alpha = new byte[8];
        for (var x = 0; x < 4; x++)
        {
            alpha[(2 * x) + 0] = 0x10; // y=0 -> 0, y=1 -> 1
            alpha[(2 * x) + 1] = 0x32; // y=2 -> 2, y=3 -> 3
        }
        return alpha;
    }

    private static byte[] ReadDarcEntry(DARC darc, int index)
    {
        var entry = darc.Entries[index];
        var offset = checked((int)(entry.DataOffset - darc.Header.FileDataOffset));
        return darc.Data.AsSpan(offset, checked((int)entry.DataLength)).ToArray();
    }

    private static void ReplaceGarcDarcEntry(string garcPath, int fileNumber, int entryIndex, byte[] replacement)
    {
        var garc = new GARC.MemGARC(File.ReadAllBytes(garcPath));
        var darc = new DARC(garc.GetFile(fileNumber));
        Assert.True(DARC.InsertFile(ref darc, entryIndex, replacement));
        var files = garc.Files;
        files[fileNumber] = DARC.SetDARC(darc);
        garc.Files = files;
        File.WriteAllBytes(garcPath, garc.Save());
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private static uint ReadBigEndian(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static uint ReadUInt32(Stream stream)
    {
        var bytes = new byte[4];
        Assert.Equal(4, stream.Read(bytes, 0, bytes.Length));
        return BitConverter.ToUInt32(bytes);
    }
}
