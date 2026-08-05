using System.Diagnostics;
using pk3DS.Editors;

namespace pk3DS.Mac.Web;

/// <summary>
/// macOS folder picker. The browser cannot hand a real filesystem path to a local server, so the
/// host opens a native dialog through osascript and returns the POSIX path it produced.
/// </summary>
public sealed class MacFolderPicker : IFolderPicker
{
    public string PickFolder(string prompt)
    {
        if (!OperatingSystem.IsMacOS())
            throw new WorkspaceException("El selector de carpetas solo está disponible en macOS.");

        // The prompt is a fixed string from our own code, never user input, so embedding it in the
        // AppleScript literal is safe. Keep it that way if you ever make it configurable.
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "-e", $"POSIX path of (choose folder with prompt \"{prompt}\")" },
        }) ?? throw new WorkspaceException("No pude abrir el selector de carpetas.");

        var path = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(path))
            return path;
        throw new WorkspaceException(error.Contains("User canceled", StringComparison.OrdinalIgnoreCase)
            ? "No se seleccionó ninguna carpeta."
            : "No pude abrir el selector de carpetas.");
    }
}
