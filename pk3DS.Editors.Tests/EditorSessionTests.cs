using pk3DS.Core;
using pk3DS.Editors;

namespace pk3DS.Editors.Tests;

public class PathSafetyTests
{
    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("a/../../outside.bin")]
    [InlineData("/etc/passwd")]
    public void PathsThatEscapeTheRootAreRejected(string relativePath)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pk3ds-test-{Guid.NewGuid():N}");

        Assert.Throws<WorkspaceException>(() => EditorSession.GetChildPath(root, relativePath));
    }

    [Fact]
    public void PathsInsideTheRootAreResolved()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pk3ds-test-{Guid.NewGuid():N}");

        var resolved = EditorSession.GetChildPath(root, Path.Combine("a", "1", "9"));

        Assert.StartsWith(Path.GetFullPath(root), resolved, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("a", "1", "9"), resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void ARootPrefixIsNotEnoughToBeInsideTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pk3ds-root");

        // "pk3ds-root-evil" starts with "pk3ds-root" as a string but is a different directory.
        Assert.Throws<WorkspaceException>(() => EditorSession.GetChildPath(root, "../pk3ds-root-evil/file.bin"));
    }
}

public class LanguageTests
{
    [Fact]
    public void MissingLanguageFallsBackToEnglish() => Assert.Equal(1, EditorSession.NormalizeLanguage(null));

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void LanguagesInRangeArePreserved(int language) =>
        Assert.Equal(language, EditorSession.NormalizeLanguage(language));

    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    public void LanguagesOutOfRangeAreRejected(int language) =>
        Assert.Throws<WorkspaceException>(() => EditorSession.NormalizeLanguage(language));
}

public class TitleIdTests
{
    private static GameWorkspace WorkspaceWithoutExheader() =>
        new("/tmp/root", "/tmp/root/romfs", null, null, GameVersion.SN);

    [Fact]
    public void AnExplicitTitleIdWins() =>
        Assert.Equal("0004000000164800", EditorSession.ResolveTitleId(WorkspaceWithoutExheader(), "0004000000164800"));

    [Fact]
    public void AMissingTitleIdIsRejected() =>
        Assert.Throws<WorkspaceException>(() => EditorSession.ResolveTitleId(WorkspaceWithoutExheader(), null));

    [Theory]
    [InlineData("0004")]                  // too short
    [InlineData("00040000001648000")]     // too long
    [InlineData("0004000000164ZZZ")]      // not hexadecimal
    public void AMalformedTitleIdIsRejected(string titleId) =>
        Assert.Throws<WorkspaceException>(() => EditorSession.ResolveTitleId(WorkspaceWithoutExheader(), titleId));
}

/// <summary>
/// The user may point at either the extracted root or the RomFS folder itself; both have to work,
/// and anything without the archive folder <c>a</c> has to be refused with a clear message.
/// </summary>
public class WorkspaceResolutionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pk3ds-test-{Guid.NewGuid():N}");

    private string CreateTree(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void TheRomFsFolderItselfIsAccepted()
    {
        CreateTree("romfs", "a");

        Assert.Equal(Path.Combine(_root, "romfs"), GameWorkspace.ResolveRomFs(Path.Combine(_root, "romfs")));
    }

    [Theory]
    [InlineData("RomFS")]
    [InlineData("romfs")]
    public void AParentFolderResolvesToItsRomFsChild(string folderName)
    {
        CreateTree(folderName, "a");

        // Compared case-insensitively on purpose: the default macOS volume is case-insensitive, so
        // the spelling that comes back is whichever candidate was probed first, not what is on disk.
        Assert.Equal(Path.Combine(_root, folderName), GameWorkspace.ResolveRomFs(_root), ignoreCase: true);
    }

    [Fact]
    public void AFolderWithoutTheArchiveDirectoryIsRejected()
    {
        CreateTree("romfs");

        Assert.Throws<WorkspaceException>(() => GameWorkspace.ResolveRomFs(_root));
    }

    [Fact]
    public void OpeningAFolderThatDoesNotExistIsRejected() =>
        Assert.Throws<WorkspaceException>(() => GameWorkspace.Open(Path.Combine(_root, "missing")));

    [Fact]
    public void OpeningAnEmptyPathIsRejected() =>
        Assert.Throws<WorkspaceException>(() => GameWorkspace.Open("   "));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
