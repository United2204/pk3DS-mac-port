using System.Diagnostics;
using pk3DS.Editors;

namespace pk3DS.Mac.Web;

/// <summary>macOS file picker used by the project extraction tools.</summary>
public sealed class MacFilePicker : IFilePicker
{
    public string PickFile(string prompt, params string[] allowedExtensions)
    {
        if (!OperatingSystem.IsMacOS())
            throw new WorkspaceException("El selector de archivos solo está disponible en macOS.");

        var escapedPrompt = prompt.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var typeClause = allowedExtensions.Length == 0
            ? string.Empty
            : $" of type {{{string.Join(", ", allowedExtensions.Select(extension =>
                $"\"{extension.TrimStart('.').Replace("\\", "\\\\").Replace("\"", "\\\"")}\""))}}}";

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-e",
                $"POSIX path of (choose file with prompt \"{escapedPrompt}\"{typeClause})",
            },
        }) ?? throw new WorkspaceException("No pude abrir el selector de archivos.");

        var path = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(path))
            return path;
        throw new WorkspaceException(error.Contains("User canceled", StringComparison.OrdinalIgnoreCase)
            ? "No se seleccionó ningún archivo."
            : "No pude abrir el selector de archivos.");
    }
}
