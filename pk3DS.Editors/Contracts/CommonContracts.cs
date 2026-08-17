namespace pk3DS.Editors;

/// <summary>An id/name pair for a dropdown: species, item, move, trainer class.</summary>
public sealed record NamedEntry(int Id, string Name);

/// <summary>Where an export landed and which RomFS files it changed.</summary>
public sealed record ExportResult(string OutputFolder, string ZipPath, string[] ChangedFiles);

public sealed record ModuleAvailability(string Id, string Name, string Area, bool SourceAvailable, string Requirement);

public sealed record WorkspaceRequest(string WorkspacePath);

/// <summary>Builds standalone RomFS/ExeFS binaries from an extracted workspace.</summary>
public sealed record BuildFileSystemsRequest(
    string WorkspacePath,
    string? OutputDirectory = null,
    bool IncludeRomFs = true,
    bool IncludeExeFs = true);

public sealed record BuildFileSystemsResponse(
    string GameVersion,
    string OutputDirectory,
    string? RomFsFile,
    long? RomFsBytes,
    string? ExeFsFile,
    long? ExeFsBytes,
    string Note);

public sealed record ExtractProjectRequest(string InputPath, string? OutputDirectory = null);

public sealed record ExtractProjectResponse(
    string Format,
    string OutputDirectory,
    string[] Files,
    string Note);

public sealed record RebuildRomRequest(
    string WorkspacePath,
    string? OutputFile = null,
    bool Trimmed = true,
    string? SerialText = null);

public sealed record RebuildRomResponse(
    string GameVersion,
    string OutputFile,
    long Bytes,
    bool Trimmed,
    string Note);

public sealed record RebuildCiaRequest(
    string WorkspacePath,
    string? OutputFile = null,
    bool Trimmed = true,
    string? SerialText = null,
    string? MakeromPath = null);

public sealed record RebuildCiaResponse(
    string GameVersion,
    string OutputFile,
    long Bytes,
    bool Trimmed,
    string MakeromPath,
    string Note);

/// <summary>Creates the redirect patch used by the original pk3DS CIA workflow.</summary>
public sealed record RedirectPatchRequest(
    string WorkspacePath,
    string[] GarcNames,
    string? OutputDirectory = null,
    bool IncludeAllLanguageVariants = false,
    string[]? AdditionalPaths = null,
    int? Language = null);

public sealed record RedirectPatchResponse(
    string GameVersion,
    string OutputDirectory,
    string CodeFile,
    string[] CopiedFiles,
    int RedirectedPaths,
    string Note);

public sealed record UnpackGarcRequest(
    string InputFile,
    string? OutputDirectory = null,
    bool SkipDecompression = false);

public sealed record UnpackGarcResponse(
    string OutputDirectory,
    int Files,
    string Note);

public sealed record PackGarcRequest(
    string InputDirectory,
    string? OutputFile = null,
    int Version = 6,
    int BytesPadding = 4);

public sealed record PackGarcResponse(
    string OutputFile,
    int Files,
    long Bytes,
    int Version,
    string Note);

public sealed record UnpackDarcRequest(
    string InputFile,
    string? OutputDirectory = null);

public sealed record UnpackDarcResponse(
    string OutputDirectory,
    int Files,
    string Note);

public sealed record PackDarcRequest(
    string InputDirectory,
    string? OutputFile = null);

public sealed record PackDarcResponse(
    string OutputFile,
    int Files,
    long Bytes,
    string Note);

public sealed record UnpackSarcRequest(
    string InputFile,
    string? OutputDirectory = null);

public sealed record UnpackSarcResponse(
    string OutputDirectory,
    int Files,
    string Note);

public sealed record PackSarcRequest(
    string InputDirectory,
    string? OutputFile = null,
    int DataAlignment = 0x10);

public sealed record PackSarcResponse(
    string OutputFile,
    int Files,
    long Bytes,
    int DataAlignment,
    string Note);

public sealed record UnpackFarcRequest(
    string InputFile,
    string? OutputDirectory = null);

public sealed record UnpackFarcResponse(
    string OutputDirectory,
    int Files,
    string Note);

public sealed record TitleScreenCatalogRequest(string WorkspacePath);

public sealed record TitleScreenAssetSummary(
    int EntryIndex,
    string Name,
    int Bytes);

public sealed record TitleScreenArchiveSummary(
    string Game,
    string Language,
    int FileNumber,
    string RomFsPath,
    bool Compressed,
    int SourceBytes,
    int? DarcBytes,
    bool Valid,
    string? Error,
    TitleScreenAssetSummary[] Assets);

public sealed record TitleScreenCatalogResponse(
    string GameVersion,
    bool CompressedArchives,
    string GarcPath,
    TitleScreenArchiveSummary[] Archives,
    string Note);

public sealed record TitleScreenExportRequest(
    string WorkspacePath,
    string? OutputDirectory = null,
    int? FileNumber = null,
    bool IncludeRawDarc = true,
    bool IncludePng = false);

public sealed record TitleScreenExportResponse(
    string GameVersion,
    string OutputDirectory,
    int Archives,
    int Assets,
    int Pngs,
    string[] Files,
    string Note);

public sealed record TitleScreenReplaceRequest(
    string WorkspacePath,
    int FileNumber,
    int AssetEntryIndex,
    string ReplacementFile,
    string? OutputFile = null);

public sealed record TitleScreenReplaceResponse(
    string GameVersion,
    string Game,
    string Language,
    int FileNumber,
    int AssetEntryIndex,
    string AssetName,
    string ReplacementFormat,
    string BclimFormat,
    string OutputFile,
    long Bytes,
    string Note);

public sealed record TitleScreenGarcReplaceResponse(
    string GameVersion,
    string Game,
    string Language,
    int FileNumber,
    int AssetEntryIndex,
    string AssetName,
    string ReplacementFormat,
    string BclimFormat,
    bool Compressed,
    string OutputFile,
    long Bytes,
    string Note);

public sealed record TitleScreenPreviewRequest(
    string WorkspacePath,
    int FileNumber,
    int AssetEntryIndex);

public sealed record TitleScreenPreviewResponse(
    string GameVersion,
    string Game,
    string Language,
    int FileNumber,
    int AssetEntryIndex,
    string AssetName,
    int Width,
    int Height,
    string BclimFormat,
    string PngBase64,
    string Note);

public sealed record InspectResponse(
    string RomFsPath,
    string GameVersion,
    bool IsComplete,
    string? TitleId,
    string? ExeFsPath,
    string? ExheaderPath,
    ModuleAvailability[] Modules,
    string Note);

public sealed record PickFolderResponse(string Path);

public sealed record PickFileResponse(string Path);
