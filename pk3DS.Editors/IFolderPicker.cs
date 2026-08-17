namespace pk3DS.Editors;

/// <summary>
/// Picking a folder is the only part of the editing flow that needs the host platform.
/// macOS shells out to osascript; a mobile host would use its own document picker.
/// </summary>
public interface IFolderPicker
{
    /// <summary>Prompts for a folder, or throws <see cref="WorkspaceException"/> if none was chosen.</summary>
    string PickFolder(string prompt);
}

public interface IFilePicker
{
    /// <summary>Prompts for a file, or throws <see cref="WorkspaceException"/> if none was chosen.</summary>
    string PickFile(string prompt);
}
