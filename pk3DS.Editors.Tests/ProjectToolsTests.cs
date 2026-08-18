using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public void RebuildsCrrAsLayeredFsPatchWithoutChangingTheSourceCros()
    {
        static byte[] ValidCro(byte seed)
        {
            var cro = new byte[0x200];
            BitConverter.GetBytes(0x180).CopyTo(cro, 0xB0);
            BitConverter.GetBytes(0x20).CopyTo(cro, 0xB4);
            BitConverter.GetBytes(0x1C0).CopyTo(cro, 0xB8);
            BitConverter.GetBytes(0x1A0).CopyTo(cro, 0xC0);
            for (var index = 0x180; index < cro.Length; index++)
                cro[index] = (byte)(seed + index);
            return cro;
        }

        var firstPath = Path.Combine(_workspace.RomFs, "DllFirst.cro");
        var secondPath = Path.Combine(_workspace.RomFs, "DllSecond.cro");
        var first = ValidCro(0x11);
        var second = ValidCro(0x77);
        File.WriteAllBytes(firstPath, first);
        File.WriteAllBytes(secondPath, second);
        var croCount = Directory.EnumerateFiles(_workspace.RomFs, "*.cro", SearchOption.TopDirectoryOnly).Count();
        var crr = new byte[0x500];
        BitConverter.GetBytes(0x400).CopyTo(crr, 0x350);
        BitConverter.GetBytes(croCount).CopyTo(crr, 0x354);
        var crrPath = Path.Combine(_workspace.RomFs, ".crr", "static.crr");
        Directory.CreateDirectory(Path.GetDirectoryName(crrPath)!);
        File.WriteAllBytes(crrPath, crr);

        var output = Path.Combine(_workspace.OutputDirectory, "crr");
        var response = ProjectTools.RebuildCrr(new RebuildCrrRequest(
            _workspace.Root,
            output,
            SyntheticWorkspace.TitleId));

        Assert.Equal("XY", response.GameVersion);
        Assert.Equal(croCount, response.CroCount);
        Assert.Equal(croCount, response.RehashedCros);
        Assert.True(response.CrrChanged);
        Assert.Contains("DllFirst.cro", response.ChangedFiles);
        Assert.Contains("DllSecond.cro", response.ChangedFiles);
        Assert.Contains(".crr/static.crr", response.ChangedFiles);
        Assert.True(File.Exists(response.ZipPath));
        Assert.Equal(first, File.ReadAllBytes(firstPath));
        Assert.Equal(second, File.ReadAllBytes(secondPath));
        Assert.Equal(crr, File.ReadAllBytes(crrPath));

        using var archive = ZipFile.OpenRead(response.ZipPath);
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("/romfs/DllFirst.cro", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("/romfs/DllSecond.cro", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("/romfs/.crr/static.crr", StringComparison.Ordinal));
    }

    [Fact]
    public void InspectsPortableSmdhMetadataAndIconPreviews()
    {
        var response = ProjectTools.InspectSmdh(new SmdhInspectRequest(_workspace.Root));

        Assert.Equal("XY", response.GameVersion);
        Assert.Equal("icon.bin", response.IconFile);
        Assert.Equal("Fixture game", response.AppInfo[0].ShortDescription);
        Assert.NotNull(response.Settings);
        Assert.Equal(16, response.Settings!.GameRatings.Length);
        Assert.Equal(24, PortablePng.DecodeRgba(Convert.FromBase64String(response.SmallIconPngBase64)).Width);
        Assert.Equal(48, PortablePng.DecodeRgba(Convert.FromBase64String(response.LargeIconPngBase64)).Width);
    }

    [Fact]
    public void ExportsSmdhAndBothIconSizesWithoutChangingTheWorkspace()
    {
        var source = File.ReadAllBytes(Path.Combine(_workspace.ExeFs, "icon.bin"));
        var output = Path.Combine(_workspace.OutputDirectory, "smdh");

        var response = ProjectTools.ExportSmdh(new SmdhExportRequest(_workspace.Root, output));

        Assert.True(File.Exists(response.SmdhFile));
        Assert.True(File.Exists(response.SmallIconFile));
        Assert.True(File.Exists(response.LargeIconFile));
        Assert.Equal(source, File.ReadAllBytes(Path.Combine(_workspace.ExeFs, "icon.bin")));
        Assert.Equal(24, PortablePng.DecodeRgba(File.ReadAllBytes(response.SmallIconFile)).Width);
        Assert.Equal(48, PortablePng.DecodeRgba(File.ReadAllBytes(response.LargeIconFile)).Width);
    }

    [Fact]
    public void UpdatesSmdhMetadataAndIconWithABackup()
    {
        var iconPath = Path.Combine(_workspace.ExeFs, "icon.bin");
        var original = File.ReadAllBytes(iconPath);
        var smallPng = Path.Combine(_workspace.OutputDirectory, "small.png");
        var rgba = new byte[SMDHPortable.SmallIconWidth * SMDHPortable.SmallIconHeight * 4];
        for (var index = 0; index < rgba.Length; index += 4)
        {
            rgba[index] = 220;
            rgba[index + 1] = 80;
            rgba[index + 2] = 40;
            rgba[index + 3] = byte.MaxValue;
        }
        File.WriteAllBytes(smallPng, PortablePng.EncodeRgba(rgba, SMDHPortable.SmallIconWidth, SMDHPortable.SmallIconHeight));

        var response = ProjectTools.UpdateSmdh(new SmdhUpdateRequest(
            _workspace.Root,
            [new SmdhApplicationInfoRequest(0, "Edited fixture", "Edited description", "Edited publisher")],
            smallPng,
            Settings: new SmdhSettingsRequest(
                Enumerable.Range(0, 16).Select(index => (byte)(index + 1)).ToArray(),
                0x45,
                0x12345678,
                "0x1122334455667788",
                0x3FF,
                2,
                0xBEEF,
                12.5f,
                0xAABBCCDD)));

        Assert.True(File.Exists(response.BackupFile));
        Assert.Equal(original, File.ReadAllBytes(response.BackupFile));
        var updated = SMDHPortable.Read(File.ReadAllBytes(iconPath));
        Assert.Equal("Edited fixture", updated.AppInfo[0].ShortDescription);
        Assert.Equal((byte)222, updated.GetSmallIconRgba()[0]);
        Assert.Equal(Enumerable.Range(0, 16).Select(index => (byte)(index + 1)), updated.Settings.GameRatings);
        Assert.Equal(0x45u, updated.Settings.RegionLockout);
        Assert.Equal(0x12345678u, updated.Settings.MatchMakerId);
        Assert.Equal(0x1122334455667788ul, updated.Settings.MatchMakerBitId);
        Assert.Equal(0x3FFu, updated.Settings.Flags);
        Assert.Equal((ushort)2, updated.Settings.EulaVersion);
        Assert.Equal((ushort)0xBEEF, updated.Settings.Reserved);
        Assert.Equal(12.5f, updated.Settings.AnimationDefaultFrame);
        Assert.Equal(0xAABBCCDDu, updated.Settings.StreetPassId);
    }

    [Fact]
    public void ListsAndRestoresSmdhBackupsWithASafetyCopy()
    {
        var iconPath = Path.Combine(_workspace.ExeFs, "icon.bin");
        var original = File.ReadAllBytes(iconPath);
        ProjectTools.UpdateSmdh(new SmdhUpdateRequest(
            _workspace.Root,
            [new SmdhApplicationInfoRequest(0, "Changed before restore", "Changed", "Fixture")]));
        var changed = File.ReadAllBytes(iconPath);

        var catalog = ProjectTools.GetSmdhBackups(new SmdhBackupsRequest(_workspace.Root));
        Assert.Single(catalog.Backups);
        Assert.Equal(original, File.ReadAllBytes(catalog.Backups[0].File));

        var response = ProjectTools.RestoreSmdhBackup(new SmdhRestoreRequest(
            _workspace.Root,
            catalog.Backups[0].File));

        Assert.Equal(original, File.ReadAllBytes(iconPath));
        Assert.Equal(changed, File.ReadAllBytes(response.SafetyBackupFile));
        Assert.Equal(original.LongLength, response.Bytes);
    }

    [Fact]
    public void ImportsACompleteSmdhAndBacksUpTheCurrentIcon()
    {
        var iconPath = Path.Combine(_workspace.ExeFs, "icon.bin");
        var original = File.ReadAllBytes(iconPath);
        var imported = SMDHPortable.CreateBlank();
        imported.AppInfo[0] = new SMDHApplicationInfo("Imported fixture", "Imported complete SMDH", "Importer");
        var source = Path.Combine(_workspace.OutputDirectory, "imported-icon.bin");
        File.WriteAllBytes(source, imported.Write());

        var response = ProjectTools.ImportSmdh(new SmdhImportRequest(_workspace.Root, source));

        Assert.True(File.Exists(response.BackupFile));
        Assert.Equal(original, File.ReadAllBytes(response.BackupFile));
        Assert.Equal("Imported fixture", SMDHPortable.Read(File.ReadAllBytes(iconPath)).AppInfo[0].ShortDescription);
    }

    [Fact]
    public void RejectsMalformedSmdhSettingsBeforeCreatingABackup()
    {
        var iconPath = Path.Combine(_workspace.ExeFs, "icon.bin");
        var original = File.ReadAllBytes(iconPath);

        Assert.Throws<WorkspaceException>(() => ProjectTools.UpdateSmdh(new SmdhUpdateRequest(
            _workspace.Root,
            [],
            Settings: new SmdhSettingsRequest(
                [1, 2, 3],
                0,
                0,
                "0x1234",
                0,
                0,
                0,
                0,
                0))));

        Assert.Equal(original, File.ReadAllBytes(iconPath));
    }

    [Fact]
    public void PacksAndUnpacksMiniBlocksWithTheirIdentifier()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "mini-input");
        var packed = Path.Combine(_workspace.OutputDirectory, "mini-wd.bin");
        var unpacked = Path.Combine(_workspace.OutputDirectory, "mini-output");
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "01.bin"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(input, "00.bin"), [4, 5, 6, 7, 8]);

        var pack = ProjectTools.PackMini(new PackMiniRequest(input, "wd", packed));
        var expected = Mini.UnpackMini(File.ReadAllBytes(packed), "WD");
        var unpack = ProjectTools.UnpackMini(new UnpackMiniRequest(packed, "WD", unpacked));

        Assert.Equal("WD", pack.Identifier);
        Assert.Equal(2, pack.Files);
        Assert.Equal(2, unpack.Files);
        Assert.Equal(expected[0], File.ReadAllBytes(Path.Combine(unpacked, "0.bin")));
        Assert.Equal(expected[1], File.ReadAllBytes(Path.Combine(unpacked, "1.bin")));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(input, "01.bin")));
    }

    [Fact]
    public void RebuildsMiniFromTemplateAndPreservesExtendedHeader()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "padded-mini-input");
        var original = Path.Combine(_workspace.OutputDirectory, "padded-mini.bin");
        var output = Path.Combine(_workspace.OutputDirectory, "padded-mini-rebuilt.bin");
        var unpacked = Path.Combine(_workspace.OutputDirectory, "padded-mini-unpacked");
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "0.bin"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(input, "1.bin"), [4, 5, 6, 7]);

        var canonical = Mini.PackMini([
            File.ReadAllBytes(Path.Combine(input, "0.bin")),
            File.ReadAllBytes(Path.Combine(input, "1.bin")),
        ], "WD");
        var padded = Mini.AdjustMiniHeader(canonical, 0x20);
        File.WriteAllBytes(original, padded);
        File.WriteAllBytes(Path.Combine(input, "1.bin"), [8, 9, 10, 11, 12]);

        var response = ProjectTools.PackMini(new PackMiniRequest(input, "WD", output, original));

        Assert.Contains("padding", response.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0x20, BitConverter.ToInt32(File.ReadAllBytes(output), 4));
        Assert.Equal(padded, File.ReadAllBytes(original));

        var unpackResponse = ProjectTools.UnpackMini(new UnpackMiniRequest(output, "WD", unpacked));
        Assert.Equal(2, unpackResponse.Files);
        Assert.Equal([1, 2, 3, 0], File.ReadAllBytes(Path.Combine(unpacked, "0.bin")));
        Assert.Equal([8, 9, 10, 11, 12, 0, 0, 0], File.ReadAllBytes(Path.Combine(unpacked, "1.bin")));
    }

    [Fact]
    public void AutoPackMiniFindsAdjacentTemplateAndPreservesExtendedHeader()
    {
        var root = Path.Combine(_workspace.OutputDirectory, "adjacent-mini_wd");
        var original = Path.Combine(_workspace.OutputDirectory, "adjacent-mini.bin");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "0.bin"), [21, 22]);

        var canonical = Mini.PackMini([new byte[] { 21, 22 }], "WD");
        File.WriteAllBytes(original, Mini.AdjustMiniHeader(canonical, 0x18));
        File.WriteAllBytes(Path.Combine(root, "0.bin"), [31, 32, 33]);

        var response = ProjectTools.PackAuto(new AutoPackRequest(root));

        Assert.Equal("Mini", response.Format);
        Assert.Equal("WD", response.Identifier);
        Assert.Equal(0x18, BitConverter.ToInt32(File.ReadAllBytes(response.OutputFile), 4));
        Assert.Contains("padding", response.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Mini.AdjustMiniHeader(canonical, 0x18), File.ReadAllBytes(original));
    }

    [Fact]
    public void AutoUnpackRecursesIntoMiniArchivesAndCanDisableRecursion()
    {
        var innerFolder = Path.Combine(_workspace.OutputDirectory, "nested-mini-inner");
        Directory.CreateDirectory(innerFolder);
        File.WriteAllBytes(Path.Combine(innerFolder, "0.bin"), [31, 32, 33]);
        var inner = Path.Combine(_workspace.OutputDirectory, "nested-inner.bin");
        ProjectTools.PackMini(new PackMiniRequest(innerFolder, "WD", inner));

        var outerFolder = Path.Combine(_workspace.OutputDirectory, "nested-mini-outer");
        Directory.CreateDirectory(outerFolder);
        File.WriteAllBytes(Path.Combine(outerFolder, "0.bin"), File.ReadAllBytes(inner));
        File.WriteAllBytes(Path.Combine(outerFolder, "1.bin"), [34, 35]);
        var outer = Path.Combine(_workspace.OutputDirectory, "nested-outer.bin");
        ProjectTools.PackMini(new PackMiniRequest(outerFolder, "ZO", outer));
        var sourceHash = SHA256.HashData(File.ReadAllBytes(outer));

        var recursiveOutput = Path.Combine(_workspace.OutputDirectory, "nested-recursive");
        var recursive = ProjectTools.UnpackAuto(new AutoUnpackRequest(outer, recursiveOutput));

        Assert.Equal("Mini", recursive.Format);
        Assert.Equal(2, recursive.Files);
        Assert.Equal(1, recursive.NestedArchives);
        Assert.Contains("interno", recursive.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([31, 32, 33, 0], File.ReadAllBytes(Path.Combine(
            recursiveOutput, "0_wd", "0.bin")));
        Assert.Equal([34, 35, 0, 0], File.ReadAllBytes(Path.Combine(recursiveOutput, "1.bin")));

        var flatOutput = Path.Combine(_workspace.OutputDirectory, "nested-flat");
        var flat = ProjectTools.UnpackAuto(new AutoUnpackRequest(
            outer, flatOutput, Recursive: false));

        Assert.Equal(0, flat.NestedArchives);
        Assert.False(Directory.Exists(Path.Combine(flatOutput, "0_wd")));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(outer)));
    }

    [Fact]
    public void DetectsAndUnpacksEverySupportedArchiveFamilyWithoutChangingSources()
    {
        var cases = new List<(string Input, string Format, string? Identifier, int Files, bool SkipDecompression)>();

        var miniInput = Path.Combine(_workspace.OutputDirectory, "auto-mini-input");
        Directory.CreateDirectory(miniInput);
        File.WriteAllBytes(Path.Combine(miniInput, "00.bin"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(miniInput, "01.bin"), [4, 5]);
        var mini = Path.Combine(_workspace.OutputDirectory, "auto-mini.bin");
        ProjectTools.PackMini(new PackMiniRequest(miniInput, "WD", mini));
        cases.Add((mini, "Mini", "WD", 2, false));

        var garcInput = Path.Combine(_workspace.OutputDirectory, "auto-garc-input");
        Directory.CreateDirectory(garcInput);
        File.WriteAllBytes(Path.Combine(garcInput, "0.bin"), [6, 7]);
        File.WriteAllBytes(Path.Combine(garcInput, "1.bin"), [8, 9, 10]);
        var garc = Path.Combine(_workspace.OutputDirectory, "auto-garc.bin");
        ProjectTools.PackGarc(new PackGarcRequest(garcInput, garc, Version: 6));
        cases.Add((garc, "GARC", null, 2, true));

        var darcInput = Path.Combine(_workspace.OutputDirectory, "auto-darc-input", "group");
        Directory.CreateDirectory(darcInput);
        File.WriteAllBytes(Path.Combine(darcInput, "entry.bin"), [11, 12, 13]);
        var darc = Path.Combine(_workspace.OutputDirectory, "auto-darc.bin");
        ProjectTools.PackDarc(new PackDarcRequest(Path.GetDirectoryName(darcInput)!, darc));
        cases.Add((darc, "DARC", null, 1, false));

        var sarcInput = Path.Combine(_workspace.OutputDirectory, "auto-sarc-input");
        Directory.CreateDirectory(sarcInput);
        File.WriteAllBytes(Path.Combine(sarcInput, "entry.bin"), [14, 15, 16]);
        var sarc = Path.Combine(_workspace.OutputDirectory, "auto-sarc.bin");
        ProjectTools.PackSarc(new PackSarcRequest(sarcInput, sarc));
        cases.Add((sarc, "SARC", null, 1, false));

        var alytSarc = Path.Combine(_workspace.OutputDirectory, "auto-alyt-source.sarc");
        SARC.Pack(sarcInput, alytSarc);
        var alyt = Path.Combine(_workspace.OutputDirectory, "auto-alyt.bin");
        File.WriteAllBytes(alyt, CreateAlyt(File.ReadAllBytes(alytSarc)));
        cases.Add((alyt, "ALYT", null, 1, false));

        var shuffle = Path.Combine(_workspace.OutputDirectory, "auto-shuffle.bin");
        File.WriteAllBytes(shuffle, CreateShuffleArc(false));
        cases.Add((shuffle, "Shuffle ARC", null, 2, false));

        var gar = Path.Combine(_workspace.OutputDirectory, "auto-gar.bin");
        File.WriteAllBytes(gar, CreateGar());
        cases.Add((gar, "GAR", null, 2, false));

        var farc = Path.Combine(_workspace.OutputDirectory, "auto-farc.bin");
        CreateFarc(farc, ("entry.bin", [17, 18, 19]));
        cases.Add((farc, "FARC", null, 1, false));

        foreach (var (input, format, identifier, files, skipDecompression) in cases)
        {
            var source = File.ReadAllBytes(input);
            var output = Path.Combine(_workspace.OutputDirectory, $"{Path.GetFileNameWithoutExtension(input)}-unpacked");
            var response = ProjectTools.UnpackAuto(new AutoUnpackRequest(input, output, skipDecompression));

            Assert.Equal(input, response.InputFile);
            Assert.Equal(format, response.Format);
            Assert.Equal(identifier, response.Identifier);
            Assert.Equal(files, response.Files);
            Assert.True(response.Bytes >= 0);
            Assert.True(Directory.Exists(output));
            Assert.Contains("detectado automáticamente", response.Note, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(source, File.ReadAllBytes(input));
        }
    }

    [Fact]
    public void DetectsWindowsFolderConventionsWhenPackingGarcsDarcsAndMinis()
    {
        var garcInput = Path.Combine(_workspace.OutputDirectory, "auto-pack_g");
        Directory.CreateDirectory(garcInput);
        File.WriteAllBytes(Path.Combine(garcInput, "0.bin"), [21, 22]);
        var garcSource = SHA256.HashData(File.ReadAllBytes(Path.Combine(garcInput, "0.bin")));
        var garc = ProjectTools.PackAuto(new AutoPackRequest(garcInput, GarcVersion: 6));

        var darcInput = Path.Combine(_workspace.OutputDirectory, "auto-pack_d", "group");
        Directory.CreateDirectory(darcInput);
        File.WriteAllBytes(Path.Combine(darcInput, "entry.bin"), [23, 24, 25]);
        var darcSource = SHA256.HashData(File.ReadAllBytes(Path.Combine(darcInput, "entry.bin")));
        var darc = ProjectTools.PackAuto(new AutoPackRequest(Path.GetDirectoryName(darcInput)!));

        var miniInput = Path.Combine(_workspace.OutputDirectory, "auto-pack_zo");
        Directory.CreateDirectory(miniInput);
        File.WriteAllBytes(Path.Combine(miniInput, "00.bin"), [26, 27, 28]);
        var miniSource = SHA256.HashData(File.ReadAllBytes(Path.Combine(miniInput, "00.bin")));
        var mini = ProjectTools.PackAuto(new AutoPackRequest(miniInput));

        Assert.Equal("GARC", garc.Format);
        Assert.Equal(1, garc.Files);
        Assert.Equal("DARC", darc.Format);
        Assert.Equal(1, darc.Files);
        Assert.Equal("Mini", mini.Format);
        Assert.Equal("ZO", mini.Identifier);
        Assert.Equal(1, mini.Files);
        Assert.True(File.Exists(garc.OutputFile));
        Assert.True(File.Exists(darc.OutputFile));
        Assert.True(File.Exists(mini.OutputFile));
        Assert.Equal(garcSource, SHA256.HashData(File.ReadAllBytes(Path.Combine(garcInput, "0.bin"))));
        Assert.Equal(darcSource, SHA256.HashData(File.ReadAllBytes(Path.Combine(darcInput, "entry.bin"))));
        Assert.Equal(miniSource, SHA256.HashData(File.ReadAllBytes(Path.Combine(miniInput, "00.bin"))));
    }

    [Fact]
    public void AutoUnpackUsesWindowsFolderConventionsWhenOutputIsOmitted()
    {
        var miniInput = Path.Combine(_workspace.OutputDirectory, "auto-default.bin");
        File.WriteAllBytes(miniInput, Mini.PackMini([[31, 32, 33]], "ZO"));
        var mini = ProjectTools.UnpackAuto(new AutoUnpackRequest(miniInput));

        Assert.Equal(Path.Combine(_workspace.OutputDirectory, "auto-default_zo"), mini.OutputDirectory);
        Assert.Equal("ZO", mini.Identifier);
        Assert.True(Directory.Exists(mini.OutputDirectory));
        Assert.Equal([31, 32, 33, 0], File.ReadAllBytes(Path.Combine(mini.OutputDirectory, "0.bin")));

        var garcInput = Path.Combine(_workspace.OutputDirectory, "auto-default-garc.bin");
        var garcSource = Path.Combine(_workspace.OutputDirectory, "auto-default-garc-source");
        Directory.CreateDirectory(garcSource);
        File.WriteAllBytes(Path.Combine(garcSource, "0.bin"), [34, 35]);
        ProjectTools.PackGarc(new PackGarcRequest(garcSource, garcInput, Version: 6));
        var garc = ProjectTools.UnpackAuto(new AutoUnpackRequest(garcInput));
        Assert.Equal(Path.Combine(_workspace.OutputDirectory, "auto-default-garc_g"), garc.OutputDirectory);

        var darcInput = Path.Combine(_workspace.OutputDirectory, "auto-default-darc.bin");
        var darcSource = Path.Combine(_workspace.OutputDirectory, "auto-default-darc-source", "folder");
        Directory.CreateDirectory(darcSource);
        File.WriteAllBytes(Path.Combine(darcSource, "entry.bin"), [36, 37]);
        ProjectTools.PackDarc(new PackDarcRequest(Path.GetDirectoryName(darcSource)!, darcInput));
        var darc = ProjectTools.UnpackAuto(new AutoUnpackRequest(darcInput));
        Assert.Equal(Path.Combine(_workspace.OutputDirectory, "auto-default-darc_d"), darc.OutputDirectory);

        var sarcSource = Path.Combine(_workspace.OutputDirectory, "auto-default-sarc-source");
        Directory.CreateDirectory(sarcSource);
        File.WriteAllBytes(Path.Combine(sarcSource, "entry.bin"), [38, 39]);
        var sarcInput = Path.Combine(_workspace.OutputDirectory, "auto-default-sarc.bin");
        ProjectTools.PackSarc(new PackSarcRequest(sarcSource, sarcInput));
        var sarc = ProjectTools.UnpackAuto(new AutoUnpackRequest(sarcInput));
        Assert.Equal(Path.Combine(_workspace.OutputDirectory, "auto-default-sarc-unpacked"), sarc.OutputDirectory);
    }

    [Fact]
    public void RejectsMiniOffsetsThatDoNotCoverTheWholeFile()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "invalid-mini.bin");
        var output = Path.Combine(_workspace.OutputDirectory, "invalid-mini-output");
        var data = new byte[16];
        data[0] = (byte)'W';
        data[1] = (byte)'D';
        BitConverter.GetBytes((ushort)1).CopyTo(data, 2);
        BitConverter.GetBytes((uint)12).CopyTo(data, 4);
        BitConverter.GetBytes((uint)15).CopyTo(data, 8);
        File.WriteAllBytes(input, data);

        Assert.Throws<WorkspaceException>(() => ProjectTools.UnpackMini(
            new UnpackMiniRequest(input, "WD", output)));
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void UnpacksAlytAndItsEmbeddedSarcWithoutChangingTheSource()
    {
        var sarcInput = Path.Combine(_workspace.OutputDirectory, "alyt-sarc-input");
        Directory.CreateDirectory(Path.Combine(sarcInput, "nested"));
        File.WriteAllBytes(Path.Combine(sarcInput, "nested", "payload.bin"), [9, 8, 7, 6]);
        var sarcPath = Path.Combine(_workspace.OutputDirectory, "embedded.sarc");
        SARC.Pack(sarcInput, sarcPath);

        var alytPath = Path.Combine(_workspace.OutputDirectory, "wrapped.alyt");
        File.WriteAllBytes(alytPath, CreateAlyt(File.ReadAllBytes(sarcPath)));
        var sourceHash = SHA256.HashData(File.ReadAllBytes(alytPath));
        var output = Path.Combine(_workspace.OutputDirectory, "wrapped-output");

        Assert.Equal(".alyt", FileFormat.Guess(File.ReadAllBytes(alytPath)));

        var response = ProjectTools.UnpackAlyt(new UnpackAlytRequest(alytPath, output));

        Assert.Equal(1, response.Files);
        Assert.Equal(1, response.Labels);
        Assert.Equal(1, response.Symbols);
        Assert.Equal([9, 8, 7, 6], File.ReadAllBytes(Path.Combine(output, "nested", "payload.bin")));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(alytPath)));
    }

    [Fact]
    public void PacksAlytWithOptionalNamesAndCanUnpackItAgain()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "alyt-pack-input");
        Directory.CreateDirectory(Path.Combine(input, "nested"));
        var payload = Path.Combine(input, "nested", "payload.bin");
        File.WriteAllBytes(payload, [1, 3, 5, 7, 9]);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(payload));
        var output = Path.Combine(_workspace.OutputDirectory, "packed.alyt");

        var response = ProjectTools.PackAlyt(new PackAlytRequest(
            input,
            output,
            ["zona_principal"],
            ["worlddata"]));

        Assert.Equal(1, response.Files);
        Assert.Equal(1, response.Labels);
        Assert.Equal(1, response.Symbols);
        var packed = ALYTPortable.Read(File.ReadAllBytes(output));
        Assert.Equal(1, packed.LabelCount);
        Assert.Equal(1, packed.SymbolCount);
        Assert.Equal("zona_principal", packed.Labels[0]);
        Assert.Equal("worlddata", packed.Symbols[0]);
        Assert.Equal(".alyt", FileFormat.Guess(File.ReadAllBytes(output)));

        var unpacked = Path.Combine(_workspace.OutputDirectory, "packed-alyt-unpacked");
        ProjectTools.UnpackAlyt(new UnpackAlytRequest(output, unpacked));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(unpacked, "nested", "payload.bin"))));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(payload)));

        var repacked = Path.Combine(_workspace.OutputDirectory, "repacked.alyt");
        var repackedResponse = ProjectTools.PackAlyt(new PackAlytRequest(unpacked, repacked));
        Assert.Equal(1, repackedResponse.Labels);
        Assert.Equal(1, repackedResponse.Symbols);
        var repackedData = ALYTPortable.Read(File.ReadAllBytes(repacked));
        Assert.Equal("zona_principal", repackedData.Labels[0]);
        Assert.Equal("worlddata", repackedData.Symbols[0]);
    }

    [Fact]
    public void RejectsMalformedAlytBeforeCreatingAnOutput()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "invalid.alyt");
        var output = Path.Combine(_workspace.OutputDirectory, "invalid-alyt-output");
        var data = new byte[0x40];
        Encoding.ASCII.GetBytes("ALYT").CopyTo(data, 0);
        BitConverter.GetBytes(0x28).CopyTo(data, 8);
        BitConverter.GetBytes(8).CopyTo(data, 12);
        BitConverter.GetBytes(0x30).CopyTo(data, 16);
        BitConverter.GetBytes(8).CopyTo(data, 20);
        BitConverter.GetBytes(0x38).CopyTo(data, 24);
        BitConverter.GetBytes(8).CopyTo(data, 28);
        BitConverter.GetBytes(0x40).CopyTo(data, 32);
        BitConverter.GetBytes(8).CopyTo(data, 36);
        File.WriteAllBytes(input, data);

        Assert.Throws<WorkspaceException>(() => ProjectTools.UnpackAlyt(new UnpackAlytRequest(input, output)));
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnpacksShuffleArcRawChunksWithoutChangingTheSource(bool hasPrefix)
    {
        var input = Path.Combine(_workspace.OutputDirectory, hasPrefix ? "shuffle-prefixed.arc" : "shuffle.arc");
        var output = Path.Combine(_workspace.OutputDirectory, hasPrefix ? "shuffle-prefixed-output" : "shuffle-output");
        var source = CreateShuffleArc(hasPrefix);
        File.WriteAllBytes(input, source);
        var sourceHash = SHA256.HashData(source);

        Assert.Equal(".sharc", FileFormat.Guess(source));
        var response = ProjectTools.UnpackShuffleArc(new UnpackShuffleArcRequest(input, output));

        Assert.Equal(2, response.Files);
        Assert.Equal(hasPrefix ? 0x100 : 0, response.HeaderOffset);
        Assert.Equal(7, response.Bytes);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(output, "0.zip")));
        Assert.Equal([9, 8, 7, 6], File.ReadAllBytes(Path.Combine(output, "1.zip")));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(input)));
    }

    [Fact]
    public void RejectsOverlappingShuffleArcChunksBeforeCreatingAnOutput()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "invalid-shuffle.arc");
        var output = Path.Combine(_workspace.OutputDirectory, "invalid-shuffle-output");
        var source = CreateShuffleArc(false);
        BitConverter.GetBytes(0x82u).CopyTo(source, 0x54); // Second entry overlaps the first chunk.
        File.WriteAllBytes(input, source);

        Assert.Throws<WorkspaceException>(() => ProjectTools.UnpackShuffleArc(
            new UnpackShuffleArcRequest(input, output)));
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void UnpacksGarNamedEntriesWithoutChangingTheSource()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "legacy.gar");
        var output = Path.Combine(_workspace.OutputDirectory, "legacy-gar-output");
        var source = CreateGar();
        File.WriteAllBytes(input, source);
        var sourceHash = SHA256.HashData(source);

        Assert.Equal(".gar", FileFormat.Guess(source));
        var response = ProjectTools.UnpackGar(new UnpackGarRequest(input, output));

        Assert.Equal(2, response.Files);
        Assert.Equal(5, response.Bytes);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(output, "first.bin")));
        Assert.Equal([4, 5], File.ReadAllBytes(Path.Combine(output, "second.bin")));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(input)));
    }

    [Fact]
    public void RejectsMalformedGarNameBeforeCreatingAnOutput()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "invalid.gar");
        var output = Path.Combine(_workspace.OutputDirectory, "invalid-gar-output");
        var source = CreateGar();
        BitConverter.GetBytes(0xFFFF_FFFFu).CopyTo(source, 0x50); // Second name-with-extension offset.
        File.WriteAllBytes(input, source);

        Assert.Throws<WorkspaceException>(() => ProjectTools.UnpackGar(
            new UnpackGarRequest(input, output)));
        Assert.False(Directory.Exists(output));
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
    public void ExtractsIntoAnExistingEmptyDirectory()
    {
        var cxi = CreateCxi();
        var output = Path.Combine(_workspace.OutputDirectory, "cxi-extracted-empty");
        Directory.CreateDirectory(output);

        var response = ProjectTools.ExtractProject(new ExtractProjectRequest(cxi, output));

        Assert.Equal("CXI", response.Format);
        Assert.Contains("exheader.bin", response.Files);
        Assert.Contains("exefs/code.bin", response.Files);
        Assert.Contains("romfs/a/0/0/0", response.Files);
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
    public void ExtractsTheFirstUnencryptedNcchFromACia()
    {
        var cxi = CreateCxi();
        var cia = Path.Combine(_workspace.OutputDirectory, "fixture.cia");
        CreateCia(cxi, cia);
        var output = Path.Combine(_workspace.OutputDirectory, "cia-extracted");

        var response = ProjectTools.ExtractProject(new ExtractProjectRequest(cia, output));

        Assert.Equal("CIA", response.Format);
        Assert.Contains("exheader.bin", response.Files);
        Assert.Contains("exefs/code.bin", response.Files);
        Assert.Contains("romfs/a/0/0/0", response.Files);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(_workspace.ExeFs, "code.bin")),
            File.ReadAllBytes(Path.Combine(output, "exefs", "code.bin")));
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
        stream.Position = 0x4000 + 0x188 + 7;
        Assert.Equal(5, stream.ReadByte()); // FixedCrypto + NoCrypto.
        stream.Position = 0x4000 + 0x200;
        var embeddedExheader = new byte[0x400];
        stream.ReadExactly(embeddedExheader);
        Assert.Equal(exheader.AsSpan(0, 0x400).ToArray(), embeddedExheader);
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
    public void CreatesARedirectPatchFromCompressedCodeBin()
    {
        var sourceCode = Path.Combine(_workspace.ExeFs, "code.bin");
        var sourceText = "rom2:/base\0rom:/a/0/0/5\0";
        var rawCode = new byte[0x400];
        Encoding.Unicode.GetBytes(sourceText).CopyTo(rawCode, 0);
        var compressed = BLZCoder.Encode(rawCode);
        File.WriteAllBytes(sourceCode, compressed);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(sourceCode));
        var output = Path.Combine(_workspace.OutputDirectory, "redirect-patch-blz");

        var response = ProjectTools.CreateRedirectPatch(new RedirectPatchRequest(
            _workspace.Root,
            ["movesprite"],
            output));

        Assert.Equal(1, response.RedirectedPaths);
        Assert.Contains("rom2:/a0/0/5", File.ReadAllText(Path.Combine(output, ".code.bin"), Encoding.Unicode));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(sourceCode)));
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
    public void ShufflesGarcReferencesWithoutChangingTheSourceOrFimbData()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "shuffle-garc-input");
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "0.bin"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(input, "1.bin"), [4, 5, 6, 7]);
        File.WriteAllBytes(Path.Combine(input, "2.bin"), [8, 9, 10, 11, 12]);
        var packed = Path.Combine(_workspace.OutputDirectory, "shuffle-source.garc");
        ProjectTools.PackGarc(new PackGarcRequest(input, packed, Version: 6));
        var source = File.ReadAllBytes(packed);
        var original = GARC.UnpackGARC(packed);
        var output = Path.Combine(_workspace.OutputDirectory, "shuffle-result.garc");

        var response = ProjectTools.ShuffleGarc(new ShuffleGarcRequest(packed, output, Seed: 1));

        Assert.Equal(1, response.Seed);
        Assert.Equal(3, response.EntryCount);
        Assert.Equal(3, response.ShuffledEntries);
        Assert.True(response.ChangedEntries > 0);
        Assert.Equal(source, File.ReadAllBytes(packed));
        var shuffled = GARC.UnpackGARC(output);
        Assert.Equal(original.DataOffset, shuffled.DataOffset);
        Assert.Equal(original.fimg.DataSize, shuffled.fimg.DataSize);
        Assert.Equal(
            source.AsSpan((int)original.DataOffset, original.fimg.DataSize).ToArray(),
            File.ReadAllBytes(output).AsSpan((int)shuffled.DataOffset, shuffled.fimg.DataSize).ToArray());
        Assert.NotEqual(
            original.fatb.Entries[0].SubEntries[0].Start,
            shuffled.fatb.Entries[0].SubEntries[0].Start);
    }

    [Fact]
    public void RejectsGarcDataRangesBeforeCreatingAnOutput()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "invalid-garc-input");
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "0.bin"), [1, 2, 3]);
        var valid = Path.Combine(_workspace.OutputDirectory, "valid.garc");
        ProjectTools.PackGarc(new PackGarcRequest(input, valid, Version: 6));

        var data = File.ReadAllBytes(valid);
        BitConverter.GetBytes(int.MaxValue).CopyTo(data, 0x48); // First BTAF subentry end offset.
        var invalid = Path.Combine(_workspace.OutputDirectory, "invalid-data.garc");
        File.WriteAllBytes(invalid, data);
        var output = Path.Combine(_workspace.OutputDirectory, "invalid-data-garc-unpacked");

        Assert.Throws<WorkspaceException>(() => ProjectTools.UnpackGarc(new UnpackGarcRequest(invalid, output, SkipDecompression: true)));
        Assert.False(Directory.Exists(output));
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
    public void PacksAndUnpacksNestedDarcFoldersWithoutChangingTheInputFolder()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "nested-darc-input");
        var nested = Path.Combine(input, "group", "subfolder");
        var sibling = Path.Combine(input, "group", "sibling");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(sibling);
        File.WriteAllBytes(Path.Combine(input, "group", "root.bin"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(nested, "deep.bin"), [4, 5, 6, 7]);
        File.WriteAllBytes(Path.Combine(sibling, "other.bin"), [8, 9]);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(nested, "deep.bin")));
        var packed = Path.Combine(_workspace.OutputDirectory, "nested.darc");
        var unpacked = Path.Combine(_workspace.OutputDirectory, "nested-darc-unpacked");

        var packResponse = ProjectTools.PackDarc(new PackDarcRequest(input, packed));
        var unpackResponse = ProjectTools.UnpackDarc(new UnpackDarcRequest(packed, unpacked));

        Assert.Equal(3, packResponse.Files);
        Assert.Equal(3, unpackResponse.Files);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(unpacked, "group", "root.bin")));
        Assert.Equal([4, 5, 6, 7], File.ReadAllBytes(Path.Combine(unpacked, "group", "subfolder", "deep.bin")));
        Assert.Equal([8, 9], File.ReadAllBytes(Path.Combine(unpacked, "group", "sibling", "other.bin")));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(nested, "deep.bin"))));
    }

    [Fact]
    public void RebuildsDarcFromTemplateAndPreservesContainerPrefix()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "template-darc-input");
        var group = Path.Combine(input, "group");
        Directory.CreateDirectory(group);
        File.WriteAllBytes(Path.Combine(group, "entry.bin"), [1, 2, 3]);
        var templateDarc = Path.Combine(_workspace.OutputDirectory, "template-source.darc");
        ProjectTools.PackDarc(new PackDarcRequest(input, templateDarc));

        var prefix = new byte[] { 0xD1, 0xA0, 0x3D, 0x5E, 0x10, 0x20, 0x30, 0x40 };
        var suffix = new byte[] { 0x90, 0x91, 0x92, 0x93, 0x94 };
        var template = Path.Combine(_workspace.OutputDirectory, "template-source.bin");
        var originalTemplate = prefix.Concat(File.ReadAllBytes(templateDarc)).Concat(suffix).ToArray();
        File.WriteAllBytes(template, originalTemplate);
        File.WriteAllBytes(Path.Combine(group, "entry.bin"), [9, 8, 7, 6, 5]);
        var output = Path.Combine(_workspace.OutputDirectory, "template-rebuilt.darc");

        var response = ProjectTools.PackDarc(new PackDarcRequest(input, output, template));

        var rebuilt = File.ReadAllBytes(output);
        Assert.Equal(prefix, rebuilt[..prefix.Length]);
        Assert.Equal(suffix, rebuilt[^suffix.Length..]);
        Assert.Equal(prefix.Length, DARC.GetDARCposition(rebuilt));
        Assert.Contains("plantilla original", response.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalTemplate, File.ReadAllBytes(template));

        var unpacked = Path.Combine(_workspace.OutputDirectory, "template-rebuilt-unpacked");
        Assert.True(DARC.Darc2files(rebuilt[prefix.Length..^suffix.Length], unpacked));
        Assert.Equal([9, 8, 7, 6, 5], File.ReadAllBytes(Path.Combine(unpacked, "group", "entry.bin")));
    }

    [Fact]
    public void AutoPackDarcFindsAdjacentTemplateAndChoosesNewOutput()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "adjacent_d", "group");
        Directory.CreateDirectory(input);
        var entry = Path.Combine(input, "entry.bin");
        File.WriteAllBytes(entry, [1, 2, 3]);
        var root = Path.GetDirectoryName(input)!;
        var original = Path.Combine(_workspace.OutputDirectory, "adjacent.darc");
        ProjectTools.PackDarc(new PackDarcRequest(root, original));

        var prefix = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var template = prefix.Concat(File.ReadAllBytes(original)).ToArray();
        File.WriteAllBytes(original, template);
        File.WriteAllBytes(entry, [4, 5, 6, 7]);

        var response = ProjectTools.PackAuto(new AutoPackRequest(root));

        Assert.Equal("DARC", response.Format);
        Assert.Equal(Path.Combine(_workspace.OutputDirectory, "adjacent-repacked.darc"), response.OutputFile);
        Assert.Equal(prefix, File.ReadAllBytes(response.OutputFile)[..prefix.Length]);
        Assert.Equal(template, File.ReadAllBytes(original));
    }

    [Fact]
    public void RejectsDarcEntriesWithUnsafeNames()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "unsafe-darc-input", "group");
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "file.bin"), [1, 2, 3]);
        var packed = Path.Combine(_workspace.OutputDirectory, "unsafe-source.darc");
        ProjectTools.PackDarc(new PackDarcRequest(Path.GetDirectoryName(input)!, packed));

        var archive = new DARC(File.ReadAllBytes(packed));
        var groupIndex = Array.FindIndex(archive.FileNameTable, entry => entry.FileName == "group");
        Assert.True(groupIndex >= 0);
        archive.FileNameTable[groupIndex].FileName = "..";
        var unsafePacked = Path.Combine(_workspace.OutputDirectory, "unsafe.darc");
        File.WriteAllBytes(unsafePacked, DARC.SetDARC(archive));

        var output = Path.Combine(_workspace.OutputDirectory, "unsafe-unpacked");
        Assert.Throws<WorkspaceException>(() => ProjectTools.UnpackDarc(new UnpackDarcRequest(unsafePacked, output)));
        Assert.False(Directory.Exists(output));
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
        Assert.Equal(".sarc", FileFormat.Guess(File.ReadAllBytes(packed)));
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
    public void RejectsSarcDataRangesBeforeCreatingAnOutput()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "invalid-sarc-data-input");
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "entry.bin"), [1, 2, 3]);
        var valid = Path.Combine(_workspace.OutputDirectory, "valid.sarc");
        ProjectTools.PackSarc(new PackSarcRequest(input, valid));

        var data = File.ReadAllBytes(valid);
        BitConverter.GetBytes(int.MaxValue).CopyTo(data, 0x2C); // SFAT entry end offset.
        var invalid = Path.Combine(_workspace.OutputDirectory, "invalid-data.sarc");
        File.WriteAllBytes(invalid, data);
        var output = Path.Combine(_workspace.OutputDirectory, "invalid-data-unpacked");

        Assert.Throws<WorkspaceException>(() => ProjectTools.UnpackSarc(new UnpackSarcRequest(invalid, output)));
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void RejectsSarcNameOffsetsBeforeCreatingAnOutput()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "invalid-sarc-name-input");
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "entry.bin"), [4, 5, 6]);
        var valid = Path.Combine(_workspace.OutputDirectory, "valid-name.sarc");
        ProjectTools.PackSarc(new PackSarcRequest(input, valid));

        var data = File.ReadAllBytes(valid);
        BitConverter.GetBytes(0x00FFFFFF).CopyTo(data, 0x24); // SFAT entry name offset.
        var invalid = Path.Combine(_workspace.OutputDirectory, "invalid-name.sarc");
        File.WriteAllBytes(invalid, data);
        var output = Path.Combine(_workspace.OutputDirectory, "invalid-name-unpacked");

        Assert.Throws<WorkspaceException>(() => ProjectTools.UnpackSarc(new UnpackSarcRequest(invalid, output)));
        Assert.False(Directory.Exists(output));
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
        Assert.Equal(".farc", FileFormat.Guess(File.ReadAllBytes(packed)));
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
    public void RejectsUnsafeFarcNamesWithoutCreatingAnOutputOrEscapingTheDestination()
    {
        var packed = Path.Combine(_workspace.OutputDirectory, "unsafe.farc");
        var output = Path.Combine(_workspace.OutputDirectory, "unsafe-unpacked");
        var escapeName = $"../farc-escape-{Guid.NewGuid():N}.bin";
        var escaped = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(output)!, escapeName));
        CreateFarc(packed, (escapeName, [41, 42, 43]));

        Assert.Throws<WorkspaceException>(() => ProjectTools.UnpackFarc(
            new UnpackFarcRequest(packed, output)));
        Assert.False(Directory.Exists(output));
        Assert.False(File.Exists(escaped));
    }

    [Fact]
    public void UnpacksHashIndexedFarcWithDeterministicSyntheticNames()
    {
        var packed = Path.Combine(_workspace.OutputDirectory, "hash-indexed.farc");
        var output = Path.Combine(_workspace.OutputDirectory, "hash-indexed-unpacked");
        CreateFarc(packed, ("entry.bin", [51, 52, 53]));
        var data = File.ReadAllBytes(packed);
        BitConverter.GetBytes(1u).CopyTo(data, 0x48); // SIR0 FAT5 type 1: CRC32 hash index.
        BitConverter.GetBytes(0xAABBCCDDu).CopyTo(data, 0x50); // The original name is not stored.
        File.WriteAllBytes(packed, data);

        var response = ProjectTools.UnpackFarc(new UnpackFarcRequest(packed, output));

        Assert.Equal(1, response.Files);
        Assert.Contains("hash-XXXXXXXX.bin", response.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([51, 52, 53], File.ReadAllBytes(Path.Combine(output, "hash-AABBCCDD.bin")));
        using var farc = new FARC(packed);
        Assert.Equal(FARCIndexKind.Crc32Hash, farc.IndexKind);
        Assert.Equal(0xAABBCCDDu, farc.Files[0].NameHash);
    }

    [Fact]
    public void PacksHashIndexedFarcAndPreservesSyntheticKeys()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "hash-farc-input");
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "hash-AABBCCDD.bin"), [61, 62, 63]);
        File.WriteAllBytes(Path.Combine(input, "entry.bin"), [64, 65, 66]);
        var packed = Path.Combine(_workspace.OutputDirectory, "hash-packed.farc");
        var unpacked = Path.Combine(_workspace.OutputDirectory, "hash-packed-unpacked");

        var packResponse = ProjectTools.PackFarc(new PackFarcRequest(
            input, packed, DataAlignment: 0x80, IndexKind: FARCIndexKind.Crc32Hash));
        var unpackResponse = ProjectTools.UnpackFarc(new UnpackFarcRequest(packed, unpacked));

        Assert.Equal(2, packResponse.Files);
        Assert.Contains("CRC32", packResponse.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, unpackResponse.Files);
        Assert.Equal([61, 62, 63], File.ReadAllBytes(Path.Combine(unpacked, "hash-AABBCCDD.bin")));
        Assert.Equal([64, 65, 66], File.ReadAllBytes(Path.Combine(unpacked, "hash-5897B024.bin")));
        using var farc = new FARC(packed);
        Assert.Equal(FARCIndexKind.Crc32Hash, farc.IndexKind);
        Assert.Contains(farc.Files, file => file.NameHash == 0xAABBCCDDu);
        Assert.Contains(farc.Files, file => file.NameHash == 0x5897B024u);
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
    public void DecodesPortableBflimAndConvertsItToPng()
    {
        var expectedRgba = Enumerable.Repeat(new byte[] { 24, 160, 72, 255 }, 64).SelectMany(value => value).ToArray();
        var pixelData = Enumerable.Repeat(new byte[] { 255, 72, 160, 24 }, 64).SelectMany(value => value).ToArray();
        var bflim = BFLIMPortable.Read(CreateBflim(8, 8, XLIMEncoding.RGBA8, pixelData));

        Assert.Equal("FLIM", FLIMHeader.Identifier);
        Assert.Equal(".bflim", FileFormat.Guess(CreateBflim(8, 8, XLIMEncoding.RGBA8, pixelData)));
        Assert.Equal(8, bflim.Width);
        Assert.Equal(8, bflim.Height);
        Assert.Equal(XLIMEncoding.RGBA8, bflim.Format);
        Assert.Equal([24, 160, 72, 255], bflim.GetRgbaData()[..4]);

        var encoded = BFLIMPortable.EncodeRgba(expectedRgba, 8, 8);
        var encodedBflim = BFLIMPortable.Read(encoded);
        Assert.Equal(expectedRgba, encodedBflim.GetRgbaData());
        Assert.Equal(".bflim", FileFormat.Guess(encoded));

        var input = Path.Combine(_workspace.OutputDirectory, "fixture.bflim");
        var output = Path.Combine(_workspace.OutputDirectory, "fixture-bflim.png");
        File.WriteAllBytes(input, CreateBflim(8, 8, XLIMEncoding.RGBA8, pixelData));
        var response = ProjectTools.ConvertImage(new ConvertImageRequest(input, output));

        Assert.Equal("BFLIM", response.InputFormat);
        Assert.Equal("PNG", response.OutputFormat);
        Assert.Equal(expectedRgba, PortablePng.DecodeRgba(File.ReadAllBytes(output)).Rgba);
    }

    [Fact]
    public void ConvertsPngToBflimAndBackWithoutChangingTheSource()
    {
        var rgba = Enumerable.Range(0, 8 * 8)
            .SelectMany(index => new byte[] { (byte)(index * 3), (byte)(255 - index), 42, (byte)(index % 2 == 0 ? 255 : 80) })
            .ToArray();
        var png = Path.Combine(_workspace.OutputDirectory, "bflim-input.png");
        var bflim = Path.Combine(_workspace.OutputDirectory, "converted.bflim");
        var roundTrip = Path.Combine(_workspace.OutputDirectory, "bflim-round-trip.png");
        File.WriteAllBytes(png, PortablePng.EncodeRgba(rgba, 8, 8));
        var sourceHash = SHA256.HashData(File.ReadAllBytes(png));

        var encoded = ProjectTools.ConvertImage(new ConvertImageRequest(png, bflim, "ETC1A4"));
        var decoded = ProjectTools.ConvertImage(new ConvertImageRequest(bflim, roundTrip));

        Assert.Equal("BFLIM", encoded.OutputFormat);
        Assert.Equal("BFLIM", decoded.InputFormat);
        Assert.Equal("PNG", decoded.OutputFormat);
        Assert.Equal(rgba.Length, PortablePng.DecodeRgba(File.ReadAllBytes(roundTrip)).Rgba.Length);
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(png)));
    }

    [Fact]
    public void ConvertsPngToBclimAndBackWithoutChangingTheSource()
    {
        var rgba = Enumerable.Range(0, 8 * 8)
            .SelectMany(index => new byte[] { (byte)index, (byte)(255 - index), 42, 255 })
            .ToArray();
        var png = Path.Combine(_workspace.OutputDirectory, "input.png");
        var bclim = Path.Combine(_workspace.OutputDirectory, "converted.bclim");
        var roundTrip = Path.Combine(_workspace.OutputDirectory, "round-trip.png");
        File.WriteAllBytes(png, PortablePng.EncodeRgba(rgba, 8, 8));
        var sourceHash = SHA256.HashData(File.ReadAllBytes(png));

        var encoded = ProjectTools.ConvertImage(new ConvertImageRequest(png, bclim));
        var decoded = ProjectTools.ConvertImage(new ConvertImageRequest(bclim, roundTrip));

        Assert.Equal("PNG", encoded.InputFormat);
        Assert.Equal("BCLIM", encoded.OutputFormat);
        Assert.Equal(8, encoded.Width);
        Assert.Equal(8, encoded.Height);
        Assert.Equal("BCLIM", decoded.InputFormat);
        Assert.Equal("PNG", decoded.OutputFormat);
        Assert.Equal(rgba, PortablePng.DecodeRgba(File.ReadAllBytes(roundTrip)).Rgba);
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(png)));
    }

    [Fact]
    public void ConvertsPngToSelectedPortableEtc1BclimFormat()
    {
        var rgba = Enumerable.Repeat(new byte[] { 20, 210, 70, 255 }, 64).SelectMany(value => value).ToArray();
        var png = Path.Combine(_workspace.OutputDirectory, "etc-input.png");
        var bclim = Path.Combine(_workspace.OutputDirectory, "etc-output.bclim");
        File.WriteAllBytes(png, PortablePng.EncodeRgba(rgba, 8, 8));

        var response = ProjectTools.ConvertImage(new ConvertImageRequest(png, bclim, "ETC1"));
        var image = BCLIMPortable.Read(File.ReadAllBytes(bclim));

        Assert.Equal("BCLIM", response.OutputFormat);
        Assert.Contains("ETC1", response.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(XLIMEncoding.ETC1, image.Format);
        Assert.Equal(8, image.Width);
        Assert.Equal(8, image.Height);
        var decoded = image.GetRgbaData();
        Assert.InRange(decoded[0], 0, 65);
        Assert.InRange(decoded[1], 165, 255);
        Assert.InRange(decoded[2], 20, 120);
        Assert.Equal(255, decoded[3]);
    }

    [Fact]
    public void RejectsUnsupportedImageExtensionsAndExistingImageOutputs()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "input.gif");
        File.WriteAllBytes(input, [1, 2, 3]);

        Assert.Throws<WorkspaceException>(() => ProjectTools.ConvertImage(new ConvertImageRequest(input)));

        var png = Path.Combine(_workspace.OutputDirectory, "input.png");
        File.WriteAllBytes(png, PortablePng.EncodeRgba(new byte[4 * 4 * 4], 4, 4));
        var existing = Path.Combine(_workspace.OutputDirectory, "existing.bclim");
        File.WriteAllBytes(existing, [1]);
        Assert.Throws<WorkspaceException>(() => ProjectTools.ConvertImage(new ConvertImageRequest(png, existing)));
        Assert.Throws<WorkspaceException>(() => ProjectTools.ConvertImage(
            new ConvertImageRequest(png, Path.Combine(_workspace.OutputDirectory, "unsupported.bclim"), "RGB565")));
    }

    [Fact]
    public void CompressesAndDecompressesLz11WithoutChangingTheSource()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "lz-input.bin");
        var compressed = Path.Combine(_workspace.OutputDirectory, "lz-output.bin");
        var decompressed = Path.Combine(_workspace.OutputDirectory, "lz-round-trip.bin");
        var source = Enumerable.Range(0, 4096).Select(index => (byte)(index % 23)).ToArray();
        File.WriteAllBytes(input, source);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(input));

        var compressedResponse = ProjectTools.ProcessLz11(new Lz11Request(input, "compress", compressed));
        var decompressedResponse = ProjectTools.ProcessLz11(new Lz11Request(compressed, "decompress", decompressed));

        Assert.Equal("compress", compressedResponse.Operation);
        Assert.Equal("decompress", decompressedResponse.Operation);
        Assert.Equal(source, File.ReadAllBytes(decompressed));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(input)));
        Assert.Equal(0x11, File.ReadAllBytes(compressed)[0]);
    }

    [Fact]
    public void RejectsInvalidLz11InputAndExistingLz11Output()
    {
        var invalid = Path.Combine(_workspace.OutputDirectory, "not-lz11.bin");
        File.WriteAllBytes(invalid, [0, 1, 2, 3]);
        Assert.Throws<WorkspaceException>(() => ProjectTools.ProcessLz11(new Lz11Request(invalid)));

        var input = Path.Combine(_workspace.OutputDirectory, "plain.bin");
        File.WriteAllBytes(input, [1, 2, 3]);
        var existing = Path.Combine(_workspace.OutputDirectory, "existing.bin");
        File.WriteAllBytes(existing, [4]);
        Assert.Throws<WorkspaceException>(() => ProjectTools.ProcessLz11(new Lz11Request(input, "compress", existing)));
    }

    [Fact]
    public void CompressesAndDecompressesBlzWithoutChangingTheSource()
    {
        var input = Path.Combine(_workspace.OutputDirectory, "blz-input.bin");
        var compressed = Path.Combine(_workspace.OutputDirectory, "blz-output.bin");
        var decompressed = Path.Combine(_workspace.OutputDirectory, "blz-round-trip.bin");
        var source = Enumerable.Range(0, 2048).Select(index => (byte)(index % 17)).ToArray();
        File.WriteAllBytes(input, source);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(input));

        var compressedResponse = ProjectTools.ProcessBlz(new BlzRequest(input, "compress", compressed));
        var decompressedResponse = ProjectTools.ProcessBlz(new BlzRequest(compressed, "decompress", decompressed));

        Assert.Equal("compress", compressedResponse.Operation);
        Assert.Equal("decompress", decompressedResponse.Operation);
        Assert.Equal(source, File.ReadAllBytes(decompressed));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(input)));
    }

    [Fact]
    public void RejectsInvalidBlzInput()
    {
        var invalid = Path.Combine(_workspace.OutputDirectory, "not-blz.bin");
        File.WriteAllBytes(invalid, [1, 2, 3, 4]);

        Assert.Throws<WorkspaceException>(() => ProjectTools.ProcessBlz(new BlzRequest(invalid)));
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
    public void EncodesPortableEtc1AndEtc1A4BclimWithRoundTripOrientation()
    {
        var rgba = Enumerable.Range(0, 16 * 8)
            .SelectMany(index => new byte[]
            {
                (byte)(index % 16 * 16),
                (byte)(index / 16 * 32),
                (byte)(255 - (index % 16 * 16)),
                (byte)(index % 4 * 85),
            })
            .ToArray();

        var encoded = BCLIMPortable.EncodeRgba(rgba, 16, 8, XLIMEncoding.ETC1A4);
        var decoded = BCLIMPortable.Read(encoded);
        var roundTrip = decoded.GetRgbaData();

        Assert.Equal(XLIMEncoding.ETC1A4, decoded.Format);
        Assert.Equal(16, decoded.Width);
        Assert.Equal(8, decoded.Height);
        Assert.Equal(rgba.Length, roundTrip.Length);
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            Assert.InRange(Math.Abs(roundTrip[offset] - rgba[offset]), 0, 45);
            Assert.InRange(Math.Abs(roundTrip[offset + 1] - rgba[offset + 1]), 0, 45);
            Assert.InRange(Math.Abs(roundTrip[offset + 2] - rgba[offset + 2]), 0, 45);
            Assert.InRange(Math.Abs(roundTrip[offset + 3] - rgba[offset + 3]), 0, 17);
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
    public void ReplacesPngInAnEtc1TitleScreenAssetKeepingTheOriginalFormat()
    {
        _workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 467);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        var colorBlock = CreateEtc1Block(red: 8, green: 4, blue: 2);
        var payload = Enumerable.Repeat(colorBlock, 4).SelectMany(value => value).ToArray();
        ReplaceGarcDarcEntry(catalog.GarcPath, archive.FileNumber, asset.EntryIndex,
            CreateBclim(8, 8, XLIMEncoding.ETC1, payload));

        var replacementRgba = Enumerable.Repeat(new byte[] { 0, 255, 0, 255 }, 64).SelectMany(value => value).ToArray();
        var replacement = Path.Combine(_workspace.OutputDirectory, "replacement-etc1.png");
        File.WriteAllBytes(replacement, PortablePng.EncodeRgba(replacementRgba, 8, 8));
        var output = Path.Combine(_workspace.OutputDirectory, "replaced-etc1.darc");

        var response = TitleScreenEditor.Replace(new TitleScreenReplaceRequest(
            _workspace.Root, archive.FileNumber, asset.EntryIndex, replacement, output));

        Assert.Equal("PNG", response.ReplacementFormat);
        Assert.Equal(XLIMEncoding.ETC1.ToString(), response.BclimFormat);
        var darc = new DARC(File.ReadAllBytes(response.OutputFile));
        var entryIndex = Array.FindIndex(darc.FileNameTable, entry => entry.FileName == "background.bclim");
        Assert.True(entryIndex >= 0);
        var replacedImage = BCLIMPortable.Read(ReadDarcEntry(darc, entryIndex));
        Assert.Equal(XLIMEncoding.ETC1, replacedImage.Format);
        var outputRgba = replacedImage.GetRgbaData();
        Assert.InRange(outputRgba[0], 0, 45);
        Assert.InRange(outputRgba[1], 180, 255);
        Assert.InRange(outputRgba[2], 0, 45);
        Assert.Equal(255, outputRgba[3]);
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
    public void PreservesPrefixedTitleScreenDarcWhenGeneratingAGarc()
    {
        _workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 467);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        var prefix = new byte[] { 0xD1, 0xA0, 0x3D, 0x5E, 0x10, 0x20 };
        var suffix = new byte[] { 0x90, 0x91, 0x92, 0x93 };
        PrefixGarcEntry(catalog.GarcPath, archive.FileNumber, prefix, compressed: false, suffix);

        // Re-read after mutating the fixture so the catalog captures the retail prefix.
        catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));
        archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 467);
        asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        Assert.Equal(prefix.Length, archive.DarcPrefixBytes);
        Assert.Equal(suffix.Length, archive.DarcSuffixBytes);

        var replacementRgba = Enumerable.Repeat(new byte[] { 0, 255, 0, 255 }, 64).SelectMany(value => value).ToArray();
        var replacement = Path.Combine(_workspace.OutputDirectory, "prefixed-title.png");
        File.WriteAllBytes(replacement, PortablePng.EncodeRgba(replacementRgba, 8, 8));
        var output = Path.Combine(_workspace.OutputDirectory, "prefixed-title.garc");

        var response = TitleScreenEditor.ReplaceGarc(new TitleScreenReplaceRequest(
            _workspace.Root,
            archive.FileNumber,
            asset.EntryIndex,
            replacement,
            output));

        var replacedGarc = new GARC.MemGARC(File.ReadAllBytes(response.OutputFile));
        var replacedArchive = replacedGarc.GetFile(archive.FileNumber);
        Assert.Equal(prefix, replacedArchive[..prefix.Length]);
        Assert.Equal(suffix, replacedArchive[^suffix.Length..]);
        var replacedDarc = new DARC(replacedArchive[prefix.Length..^suffix.Length]);
        var entryIndex = Array.FindIndex(replacedDarc.FileNameTable, entry => entry.FileName == "background.bclim");
        Assert.True(entryIndex >= 0);
        Assert.Equal(replacementRgba, BCLIMPortable.Read(ReadDarcEntry(replacedDarc, entryIndex)).GetRgbaData());
    }

    [Fact]
    public void PreservesPrefixedTitleScreenDarcWhenGeneratingStandaloneDarc()
    {
        _workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 467);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        var prefix = new byte[] { 0xE1, 0xE2, 0xE3 };
        var suffix = new byte[] { 0xF1, 0xF2, 0xF3, 0xF4 };
        PrefixGarcEntry(catalog.GarcPath, archive.FileNumber, prefix, compressed: false, suffix);

        var replacementRgba = Enumerable.Repeat(new byte[] { 255, 0, 255, 255 }, 64).SelectMany(value => value).ToArray();
        var replacement = Path.Combine(_workspace.OutputDirectory, "prefixed-standalone.png");
        File.WriteAllBytes(replacement, PortablePng.EncodeRgba(replacementRgba, 8, 8));
        var output = Path.Combine(_workspace.OutputDirectory, "prefixed-standalone.darc");

        var response = TitleScreenEditor.Replace(new TitleScreenReplaceRequest(
            _workspace.Root,
            archive.FileNumber,
            asset.EntryIndex,
            replacement,
            output));

        var replacedArchive = File.ReadAllBytes(response.OutputFile);
        Assert.Equal(prefix, replacedArchive[..prefix.Length]);
        Assert.Equal(suffix, replacedArchive[^suffix.Length..]);
        var replacedDarc = new DARC(replacedArchive[prefix.Length..^suffix.Length]);
        var entryIndex = Array.FindIndex(replacedDarc.FileNameTable, entry => entry.FileName == "background.bclim");
        Assert.True(entryIndex >= 0);
        Assert.Equal(replacementRgba, BCLIMPortable.Read(ReadDarcEntry(replacedDarc, entryIndex)).GetRgbaData());
    }

    [Fact]
    public void ReportsTitleScreenDarcEnvelopeInExportManifest()
    {
        _workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(_workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 467);
        var prefix = new byte[] { 0xB1, 0xB2, 0xB3, 0xB4 };
        var suffix = new byte[] { 0xC1, 0xC2 };
        PrefixGarcEntry(catalog.GarcPath, archive.FileNumber, prefix, compressed: false, suffix);
        var output = Path.Combine(_workspace.OutputDirectory, "title-screen-manifest");

        var response = TitleScreenEditor.Export(new TitleScreenExportRequest(
            _workspace.Root,
            output,
            FileNumber: archive.FileNumber,
            IncludeRawDarc: true,
            IncludePng: false));

        Assert.Equal(1, response.Archives);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(response.OutputDirectory, "manifest.json")));
        var entry = Assert.Single(manifest.RootElement.EnumerateArray());
        Assert.Equal(prefix.Length, entry.GetProperty("darcPrefixBytes").GetInt32());
        Assert.Equal(suffix.Length, entry.GetProperty("darcSuffixBytes").GetInt32());
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

        var backups = TitleScreenEditor.GetBackups(new TitleScreenBackupsRequest(_workspace.Root));
        var backup = Assert.Single(backups.Backups);
        Assert.Equal(response.BackupFile, backup.File);

        var restore = TitleScreenEditor.RestoreBackup(new TitleScreenRestoreRequest(_workspace.Root, backup.File));

        Assert.Equal(catalog.GarcPath, restore.GarcPath);
        Assert.True(File.Exists(restore.SafetyBackupFile));
        Assert.Equal(originalGarc, File.ReadAllBytes(catalog.GarcPath));
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
    public void RejectsTitleScreenBackupOutsideWorkspaceBackupDirectory()
    {
        _workspace.WritePortableTitleScreenFixture();
        var outside = Path.Combine(_workspace.OutputDirectory, "X-EN-467-5.bak");
        File.WriteAllBytes(outside, []);

        Assert.Throws<WorkspaceException>(() => TitleScreenEditor.RestoreBackup(
            new TitleScreenRestoreRequest(_workspace.Root, outside)));
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
    public void PreservesPrefixedTitleScreenDarcWhenGeneratingACompressedOrasGarc()
    {
        using var workspace = new SyntheticOrasWorkspace();
        workspace.WritePortableTitleScreenFixture();
        var catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(workspace.Root));
        var archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 1120);
        var asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        var prefix = new byte[] { 0xC3, 0x91, 0x7A, 0x44, 0x28, 0x19, 0x02 };
        var suffix = new byte[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4 };
        PrefixGarcEntry(catalog.GarcPath, archive.FileNumber, prefix, compressed: true, suffix);

        catalog = TitleScreenEditor.GetCatalog(new TitleScreenCatalogRequest(workspace.Root));
        archive = Assert.Single(catalog.Archives, entry => entry.FileNumber == 1120);
        Assert.True(archive.Compressed);
        asset = Assert.Single(archive.Assets, entry => entry.Name == "background.bclim");
        Assert.Equal(prefix.Length, archive.DarcPrefixBytes);
        Assert.Equal(suffix.Length, archive.DarcSuffixBytes);

        var replacementRgba = Enumerable.Repeat(new byte[] { 255, 255, 0, 255 }, 64).SelectMany(value => value).ToArray();
        var replacement = Path.Combine(workspace.OutputDirectory, "prefixed-title-oras.png");
        File.WriteAllBytes(replacement, PortablePng.EncodeRgba(replacementRgba, 8, 8));
        var output = Path.Combine(workspace.OutputDirectory, "prefixed-title-oras.garc");

        var response = TitleScreenEditor.ReplaceGarc(new TitleScreenReplaceRequest(
            workspace.Root,
            archive.FileNumber,
            asset.EntryIndex,
            replacement,
            output));

        Assert.True(response.Compressed);
        var replacedGarc = new GARC.MemGARC(File.ReadAllBytes(response.OutputFile));
        var replacedArchive = DecompressLzss(replacedGarc.GetFile(archive.FileNumber));
        Assert.Equal(prefix, replacedArchive[..prefix.Length]);
        Assert.Equal(suffix, replacedArchive[^suffix.Length..]);
        var replacedDarc = new DARC(replacedArchive[prefix.Length..^suffix.Length]);
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

    private static void CreateCia(string cxiPath, string outputPath)
    {
        var cxi = File.ReadAllBytes(cxiPath);
        const int headerSize = 0x2020;
        const int certificateSize = 0x80;
        const int ticketSize = 0x40;
        const int tmdSize = 0x100;
        const int metaSize = 0x80;
        var contentOffset = Align(headerSize, 0x40);
        contentOffset = Align(contentOffset + certificateSize, 0x40);
        contentOffset = Align(contentOffset + ticketSize, 0x40);
        contentOffset = Align(contentOffset + tmdSize, 0x40);
        var metaOffset = Align(contentOffset + cxi.Length, 0x40);
        var cia = new byte[metaOffset + metaSize];
        BitConverter.GetBytes((uint)headerSize).CopyTo(cia, 0x00);
        BitConverter.GetBytes((uint)certificateSize).CopyTo(cia, 0x08);
        BitConverter.GetBytes((uint)ticketSize).CopyTo(cia, 0x0C);
        BitConverter.GetBytes((uint)tmdSize).CopyTo(cia, 0x10);
        BitConverter.GetBytes((uint)metaSize).CopyTo(cia, 0x14);
        BitConverter.GetBytes((ulong)cxi.Length).CopyTo(cia, 0x18);
        cxi.CopyTo(cia, contentOffset);
        File.WriteAllBytes(outputPath, cia);
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

    private static byte[] CreateAlyt(byte[] sarcData)
    {
        const int dataOffset = 0x40;
        const int dataSize = 4 + 0x40 + 4 + 0x20;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("ALYT"));
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(0x28);
        writer.Write(8);
        writer.Write(0x30);
        writer.Write(8);
        writer.Write(0x38);
        writer.Write(8);
        writer.Write(dataOffset);
        writer.Write(dataSize + sarcData.Length);
        writer.Write(Encoding.ASCII.GetBytes("LTBL"));
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(Encoding.ASCII.GetBytes("LMTL"));
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(Encoding.ASCII.GetBytes("LFNL"));
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(1);
        writer.Write(new byte[0x40]);
        writer.Write(1);
        writer.Write(new byte[0x20]);
        writer.Write(sarcData);
        return stream.ToArray();
    }

    private static byte[] CreateShuffleArc(bool hasPrefix)
    {
        const int headerSize = 0x18;
        const int entrySize = 0x30;
        const int firstDataOffset = 0x80;
        const int secondDataOffset = 0x90;
        var headerOffset = hasPrefix ? 0x100 : 0;
        var data = new byte[headerOffset + secondDataOffset + 4];

        void WriteUInt32At(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(data, offset);

        WriteUInt32At(headerOffset, 0x0B);
        WriteUInt32At(headerOffset + 4, 0x12345678);
        WriteUInt32At(headerOffset + 8, 0xAABBCCDD);
        WriteUInt32At(headerOffset + 12, 0x01020304);
        WriteUInt32At(headerOffset + 16, 2);
        WriteUInt32At(headerOffset + 20, 0);

        var firstEntry = headerOffset + headerSize;
        var secondEntry = firstEntry + entrySize;
        WriteUInt32At(firstEntry + 8, 3);
        WriteUInt32At(firstEntry + 12, firstDataOffset);
        WriteUInt32At(secondEntry + 8, 4);
        WriteUInt32At(secondEntry + 12, secondDataOffset);
        new byte[] { 1, 2, 3 }.CopyTo(data, headerOffset + firstDataOffset);
        new byte[] { 9, 8, 7, 6 }.CopyTo(data, headerOffset + secondDataOffset);
        return data;
    }

    private static byte[] CreateGar()
    {
        const int metadataOffset = 0x40;
        const int offsetsOffset = 0x80;
        const int dataOffset = 0x88;
        const int firstData = 0x88;
        const int secondData = 0x90;
        var data = new byte[secondData + 2];

        void WriteUInt32At(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(data, offset);

        WriteUInt32At(0x00, 0x02524147);
        WriteUInt32At(0x04, (uint)data.Length);
        WriteUInt32At(0x08, 0x12345678);
        WriteUInt32At(0x0C, 0x3C);
        WriteUInt32At(0x10, metadataOffset);
        WriteUInt32At(0x14, offsetsOffset);
        WriteUInt32At(0x34, 0x20);
        WriteUInt32At(0x38, 0x24);

        WriteUInt32At(metadataOffset, 3);
        WriteUInt32At(metadataOffset + 4, 0x58);
        WriteUInt32At(metadataOffset + 8, 0x5E);
        WriteUInt32At(metadataOffset + 0x0C, 2);
        WriteUInt32At(metadataOffset + 0x10, 0x68);
        WriteUInt32At(metadataOffset + 0x14, 0x6F);

        Encoding.ASCII.GetBytes("first\0first.bin\0second\0second.bin\0").CopyTo(data, 0x58);
        WriteUInt32At(offsetsOffset, dataOffset);
        WriteUInt32At(offsetsOffset + 4, secondData);
        new byte[] { 1, 2, 3 }.CopyTo(data, firstData);
        new byte[] { 4, 5 }.CopyTo(data, secondData);
        return data;
    }

    private static byte[] CreateBflim(int width, int height, XLIMEncoding format, byte[] pixelData)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(pixelData);
        writer.Write(0x4D494C46u); // FLIM
        writer.Write((ushort)0xFEFF);
        writer.Write((ushort)0x14);
        writer.Write(0x00010000u); // Version 1.0
        writer.Write((uint)(pixelData.Length + FLIMHeader.SIZE));
        writer.Write(1u);
        writer.Write(0x67616D69u); // imag
        writer.Write(0x10u);
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((short)0);
        writer.Write((byte)format);
        writer.Write((byte)XLIMOrientation.None);
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

    private static void PrefixGarcEntry(string garcPath, int fileNumber, byte[] prefix, bool compressed, byte[]? suffix = null)
    {
        var garc = new GARC.MemGARC(File.ReadAllBytes(garcPath));
        var archive = garc.GetFile(fileNumber);
        var decoded = compressed ? DecompressLzss(archive) : archive;
        var prefixed = prefix.Concat(decoded).Concat(suffix ?? []).ToArray();
        var files = garc.Files;
        files[fileNumber] = compressed ? CompressLzss(Path.GetDirectoryName(garcPath)!, prefixed) : prefixed;
        garc.Files = files;
        File.WriteAllBytes(garcPath, garc.Save());
    }

    private static byte[] DecompressLzss(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        LZSS.Decompress(input, input.Length, output);
        return output.ToArray();
    }

    private static byte[] CompressLzss(string directory, byte[] data)
    {
        var source = Path.Combine(directory, $"title-prefix-{Guid.NewGuid():N}.bin");
        var compressed = Path.Combine(directory, $"title-prefix-{Guid.NewGuid():N}.lz");
        File.WriteAllBytes(source, data);
        try
        {
            LZSS.Compress(source, compressed);
            return File.ReadAllBytes(compressed);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(compressed)) File.Delete(compressed);
        }
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
