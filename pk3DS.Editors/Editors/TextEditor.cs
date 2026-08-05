using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.Editors;

/// <summary>Game Text and Story Text: browse the string tables and write one back.</summary>
public static class TextEditor
{
    public static TextCatalogResponse GetCatalog(TextCatalogRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var garc = GetGarc(config, request.Kind);
        var knownNames = config.GameText.ToDictionary(reference => reference.Index, reference => reference.Name.ToString());
        var tables = garc.Files.Select((_, index) => new TextTableSummary(
            index,
            knownNames.GetValueOrDefault(index, $"Tabla {index:000}"),
            new TextFile(config, garc.Files[index], remapChars: true).Lines.Length)).ToArray();
        return new TextCatalogResponse(workspace.Version.ToString(), request.Kind, tables);
    }

    public static TextTableResponse GetTable(TextTableRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var garc = GetGarc(config, request.Kind);
        var lines = new TextFile(config, garc.Files[RequireTable(garc, request.TableIndex)], remapChars: true).Lines;
        return new TextTableResponse(request.Kind, request.TableIndex, lines);
    }

    public static ExportResult Export(TextExportRequest request)
    {
        if (request.Lines is null)
            throw new WorkspaceException("No hay líneas de texto para exportar.");

        var archive = request.Kind == TextArchiveKind.Story ? "storytext" : "gametext";
        return EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "text", [archive], config =>
            {
                var garc = GetGarc(config, request.Kind);
                var index = RequireTable(garc, request.TableIndex);
                var text = new TextFile(config, garc.Files[index], remapChars: true) { Lines = request.Lines };
                garc.Files[index] = text.Data;
                garc.Save();
                return [config.GetGARCFileName(archive)];
            });
    }

    internal static GARCFile GetGarc(GameConfig config, TextArchiveKind kind) => kind == TextArchiveKind.Story
        ? config.GetGARCData("storytext")
        : config.GARCGameText;

    private static int RequireTable(GARCFile garc, int tableIndex) =>
        tableIndex >= 0 && tableIndex < garc.Files.Length
            ? tableIndex
            : throw new WorkspaceException("La tabla de texto indicada no existe.");
}
