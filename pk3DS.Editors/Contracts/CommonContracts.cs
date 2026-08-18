using pk3DS.Core.CTR;

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

/// <summary>Rebuilds the embedded CRO hashes and the RomFS static CRR table in a LayeredFS patch.</summary>
public sealed record RebuildCrrRequest(
    string WorkspacePath,
    string? OutputDirectory = null,
    string? TitleId = null);

public sealed record RebuildCrrResponse(
    string GameVersion,
    string OutputDirectory,
    string ZipPath,
    string[] ChangedFiles,
    int CroCount,
    int RehashedCros,
    bool CrrChanged,
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

/// <summary>Runs the Windows ToolsUI-style archive detection before unpacking a supported container.</summary>
public sealed record AutoUnpackRequest(
    string InputFile,
    string? OutputDirectory = null,
    bool SkipDecompression = false,
    bool Recursive = true);

public sealed record AutoUnpackResponse(
    string InputFile,
    string Format,
    string? Identifier,
    string OutputDirectory,
    int Files,
    long Bytes,
    string Note)
{
    public int NestedArchives { get; init; }
}

/// <summary>Runs the Windows ToolsUI-style folder-name detection before packing a supported container.</summary>
public sealed record AutoPackRequest(
    string InputDirectory,
    string? OutputFile = null,
    int GarcVersion = 6,
    int GarcBytesPadding = 4);

public sealed record AutoPackResponse(
    string InputDirectory,
    string Format,
    string? Identifier,
    string OutputFile,
    int Files,
    long Bytes,
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

public sealed record ShuffleGarcRequest(
    string InputFile,
    string? OutputFile = null,
    int? Seed = null);

public sealed record ShuffleGarcResponse(
    string InputFile,
    string OutputFile,
    int Seed,
    int EntryCount,
    int ShuffledEntries,
    int ChangedEntries,
    long Bytes,
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
    string? OutputFile = null,
    string? TemplateFile = null);

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

public sealed record UnpackAlytRequest(
    string InputFile,
    string? OutputDirectory = null);

public sealed record UnpackAlytResponse(
    string InputFile,
    string OutputDirectory,
    int Files,
    int Labels,
    int Symbols,
    long Bytes,
    string Note);

public sealed record PackAlytRequest(
    string InputDirectory,
    string? OutputFile = null,
    string[]? Labels = null,
    string[]? Symbols = null);

public sealed record PackAlytResponse(
    string OutputFile,
    int Files,
    int Labels,
    int Symbols,
    long Bytes,
    string Note);

public sealed record UnpackShuffleArcRequest(
    string InputFile,
    string? OutputDirectory = null);

public sealed record UnpackShuffleArcResponse(
    string InputFile,
    string OutputDirectory,
    int Files,
    int HeaderOffset,
    long Bytes,
    string Note);

public sealed record UnpackGarRequest(
    string InputFile,
    string? OutputDirectory = null);

public sealed record UnpackGarResponse(
    string InputFile,
    string OutputDirectory,
    int Files,
    long Bytes,
    string Note);

public sealed record PackFarcRequest(
    string InputDirectory,
    string? OutputFile = null,
    int DataAlignment = 0x80,
    FARCIndexKind IndexKind = FARCIndexKind.NamedUtf16);

public sealed record PackFarcResponse(
    string OutputFile,
    int Files,
    long Bytes,
    int DataAlignment,
    string Note);

public sealed record UnpackMiniRequest(
    string InputFile,
    string Identifier,
    string? OutputDirectory = null);

public sealed record UnpackMiniResponse(
    string InputFile,
    string Identifier,
    string OutputDirectory,
    int Files,
    long Bytes,
    string Note);

public sealed record PackMiniRequest(
    string InputDirectory,
    string Identifier,
    string? OutputFile = null,
    string? TemplateFile = null);

public sealed record PackMiniResponse(
    string OutputFile,
    string Identifier,
    int Files,
    long Bytes,
    string Note);

public sealed record ConvertImageRequest(
    string InputFile,
    string? OutputFile = null,
    string BclimFormat = "RGBA8");

public sealed record ConvertImageResponse(
    string InputFile,
    string OutputFile,
    string InputFormat,
    string OutputFormat,
    int Width,
    int Height,
    long Bytes,
    string Note);

public sealed record SmdhSettingsResponse(
    byte[] GameRatings,
    uint RegionLockout,
    uint MatchMakerId,
    string MatchMakerBitId,
    uint Flags,
    ushort EulaVersion,
    ushort Reserved,
    float AnimationDefaultFrame,
    uint StreetPassId);

public sealed record SmdhSettingsRequest(
    byte[] GameRatings,
    uint RegionLockout,
    uint MatchMakerId,
    string MatchMakerBitId,
    uint Flags,
    ushort EulaVersion,
    ushort Reserved,
    float AnimationDefaultFrame,
    uint StreetPassId);

public sealed record SmdhInspectRequest(string WorkspacePath);

public sealed record SmdhApplicationInfoResponse(
    int Slot,
    string ShortDescription,
    string LongDescription,
    string Publisher);

public sealed record SmdhInspectResponse(
    string GameVersion,
    string IconFile,
    SmdhApplicationInfoResponse[] AppInfo,
    string SmallIconPngBase64,
    string LargeIconPngBase64,
    string Note,
    SmdhSettingsResponse? Settings = null);

public sealed record SmdhExportRequest(
    string WorkspacePath,
    string? OutputDirectory = null);

public sealed record SmdhExportResponse(
    string GameVersion,
    string OutputDirectory,
    string SmdhFile,
    string SmallIconFile,
    string LargeIconFile,
    string Note);

public sealed record SmdhApplicationInfoRequest(
    int Slot,
    string ShortDescription,
    string LongDescription,
    string Publisher);

public sealed record SmdhUpdateRequest(
    string WorkspacePath,
    SmdhApplicationInfoRequest[] AppInfo,
    string? SmallIconFile = null,
    string? LargeIconFile = null,
    SmdhSettingsRequest? Settings = null);

public sealed record SmdhUpdateResponse(
    string GameVersion,
    string IconFile,
    string BackupFile,
    long Bytes,
    string Note);

public sealed record SmdhImportRequest(string WorkspacePath, string SourceFile);

public sealed record SmdhImportResponse(
    string GameVersion,
    string IconFile,
    string BackupFile,
    long Bytes,
    string Note);

public sealed record SmdhBackupsRequest(string WorkspacePath);

public sealed record SmdhBackupSummary(
    string File,
    long Bytes,
    DateTime CreatedUtc);

public sealed record SmdhBackupsResponse(
    string GameVersion,
    string IconFile,
    SmdhBackupSummary[] Backups,
    string Note);

public sealed record SmdhRestoreRequest(
    string WorkspacePath,
    string BackupFile);

public sealed record SmdhRestoreResponse(
    string GameVersion,
    string IconFile,
    string BackupFile,
    string SafetyBackupFile,
    long Bytes,
    string Note);

public sealed record Lz11Request(
    string InputFile,
    string Operation = "decompress",
    string? OutputFile = null);

public sealed record Lz11Response(
    string InputFile,
    string OutputFile,
    string Operation,
    long Bytes,
    string Note);

public sealed record BlzRequest(
    string InputFile,
    string Operation = "decompress",
    string? OutputFile = null,
    bool BestCompression = false,
    bool Arm9 = false);

public sealed record BlzResponse(
    string InputFile,
    string OutputFile,
    string Operation,
    long Bytes,
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
    TitleScreenAssetSummary[] Assets,
    int DarcPrefixBytes = 0,
    int DarcSuffixBytes = 0);

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

public sealed record TitleScreenApplyRequest(
    string WorkspacePath,
    int FileNumber,
    int AssetEntryIndex,
    string ReplacementFile);

public sealed record TitleScreenApplyResponse(
    string GameVersion,
    string Game,
    string Language,
    int FileNumber,
    int AssetEntryIndex,
    string AssetName,
    string ReplacementFormat,
    string BclimFormat,
    bool Compressed,
    string GarcPath,
    string BackupFile,
    long Bytes,
    string Note);

public sealed record TitleScreenBackupsRequest(string WorkspacePath);

public sealed record TitleScreenBackupSummary(
    string File,
    long Bytes,
    DateTime CreatedUtc);

public sealed record TitleScreenBackupsResponse(
    string GameVersion,
    string GarcPath,
    TitleScreenBackupSummary[] Backups,
    string Note);

public sealed record TitleScreenRestoreRequest(
    string WorkspacePath,
    string BackupFile);

public sealed record TitleScreenRestoreResponse(
    string GameVersion,
    string GarcPath,
    string BackupFile,
    string SafetyBackupFile,
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
    string Note)
{
    public string? CodeBinPath { get; init; }
    public long? CodeBinBytes { get; init; }
    public bool CodeBinReady { get; init; }
    public bool CodeBinCompressed { get; init; }
    public bool SmdhReady { get; init; }
    public InspectDiagnostic[] Diagnostics { get; init; } = [];
}

public sealed record InspectDiagnostic(
    string Severity,
    string Code,
    string Message);

public sealed record PickFolderResponse(string Path);

public sealed record PickFileResponse(string Path);
