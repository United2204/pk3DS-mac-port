namespace pk3DS.Editors;

/// <summary>An id/name pair for a dropdown: species, item, move, trainer class.</summary>
public sealed record NamedEntry(int Id, string Name);

/// <summary>Where an export landed and which RomFS files it changed.</summary>
public sealed record ExportResult(string OutputFolder, string ZipPath, string[] ChangedFiles);

public sealed record ModuleAvailability(string Id, string Name, string Area, bool SourceAvailable, string Requirement);

public sealed record WorkspaceRequest(string WorkspacePath);

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
