using System.IO.Compression;
using pk3DS.Editors;

namespace pk3DS.Editors.Tests;

/// <summary>
/// Assertions about a produced LayeredFS archive.
/// <para>
/// Checking that the archive merely <em>contains</em> the file an editor claims to have changed is
/// not enough: an editor that read the GARC and then failed to mutate it would still emit an
/// identical copy and pass. <see cref="AssertContentDiffersFromSource"/> closes that gap.
/// </para>
/// </summary>
internal static class ExportAssertions
{
    public static string[] EntriesOf(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }

    private static string ArchivePathFor(string changedFile) =>
        $"luma/titles/{SyntheticWorkspace.TitleId}/romfs/{changedFile}";

    public static void AssertContainsChangedFiles(ExportResult result)
    {
        Assert.True(File.Exists(result.ZipPath), "no se generó el ZIP");
        Assert.NotEmpty(result.ChangedFiles);

        var entries = EntriesOf(result.ZipPath);
        foreach (var changed in result.ChangedFiles)
            Assert.Contains(ArchivePathFor(changed), entries);
    }

    /// <summary>
    /// Asserts the archived copy of every changed file actually differs from the source dump, so
    /// the export is a real edit rather than a passthrough.
    /// </summary>
    public static void AssertContentDiffersFromSource(ExportResult result, SyntheticWorkspace workspace)
    {
        AssertContainsChangedFiles(result);

        using var archive = ZipFile.OpenRead(result.ZipPath);
        foreach (var changed in result.ChangedFiles)
        {
            var entry = archive.GetEntry(ArchivePathFor(changed))!;
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            // ChangedFiles uses forward slashes; the source path needs the platform separator.
            var source = Path.Combine(workspace.RomFs, changed.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(source), $"{changed}: no existe en el dump de origen");
            Assert.NotEqual(File.ReadAllBytes(source), buffer.ToArray());
        }
    }

    public static void AssertExeFsContentDiffersFromSource(ExportResult result, SyntheticWorkspace workspace)
    {
        Assert.True(File.Exists(result.ZipPath), "no se generó el ZIP ExeFS");
        Assert.NotEmpty(result.ChangedFiles);
        using var archive = ZipFile.OpenRead(result.ZipPath);
        foreach (var changed in result.ChangedFiles)
        {
            var entry = archive.GetEntry($"luma/titles/{SyntheticWorkspace.TitleId}/exefs/{changed}")!;
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var source = Path.Combine(workspace.ExeFs, changed.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(source), $"{changed}: no existe en el ExeFS de origen");
            Assert.NotEqual(File.ReadAllBytes(source), buffer.ToArray());
        }
    }
}
