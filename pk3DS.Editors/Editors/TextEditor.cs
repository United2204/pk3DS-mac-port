using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.Editors;

/// <summary>Game Text and Story Text: browse, edit, import, export, and randomize string tables.</summary>
public static class TextEditor
{
    public static TextCatalogResponse GetCatalog(TextCatalogRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var garc = GetGarc(config, request.Kind);
        var files = garc.Files;
        var knownNames = config.GameText.ToDictionary(reference => reference.Index, reference => reference.Name.ToString());
        var tables = files.Select((data, index) => new TextTableSummary(
            index,
            knownNames.GetValueOrDefault(index, $"Tabla {index:000}"),
            new TextFile(config, data, remapChars: true).Lines.Length)).ToArray();
        return new TextCatalogResponse(workspace.Version.ToString(), request.Kind, tables);
    }

    public static TextTableResponse GetTable(TextTableRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var garc = GetGarc(config, request.Kind);
        var lines = new TextFile(config, garc.Files[RequireTable(garc, request.TableIndex)], remapChars: true).Lines;
        return new TextTableResponse(request.Kind, request.TableIndex, lines);
    }

    public static TextAllTablesResponse GetAllTables(TextAllTablesRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var garc = GetGarc(config, request.Kind);
        var files = garc.Files;
        var tables = files
            .Select(data => new TextFile(config, data, remapChars: true).Lines)
            .ToArray();
        return new TextAllTablesResponse(request.Kind, tables);
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
                garc.SetFile(index, text.Data);
                garc.Save();
                return [config.GetGARCFileName(archive)];
            });
    }

    public static TextArchiveExportResponse ExportAll(TextArchiveExportRequest request)
    {
        if (request.Tables is null)
            throw new WorkspaceException("No hay tablas de texto para exportar.");

        var archive = request.Kind == TextArchiveKind.Story ? "storytext" : "gametext";
        var failedTableIndices = new List<int>();
        var errata = new List<string>();
        var result = EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "text", [archive], config =>
            {
                var garc = GetGarc(config, request.Kind);
                var files = garc.Files;
                if (request.Tables.Length != files.Length)
                    throw new WorkspaceException($"La cantidad de tablas importadas no coincide: se recibieron {request.Tables.Length} y se esperaban {files.Length}.");

                for (var index = 0; index < request.Tables.Length; index++)
                {
                    var lines = request.Tables[index];
                    if (lines is null || lines.Any(line => line is null))
                        throw new WorkspaceException($"La tabla {index} contiene líneas nulas no válidas.");
                    byte[] encoded;
                    try
                    {
                        encoded = new TextFile(config, files[index], remapChars: true) { Lines = lines }.Data;
                    }
                    catch (Exception ex)
                    {
                        // Match the Windows recovery path: keep other tables' edits, and leave
                        // this table's original bytes in the copied GARC.
                        failedTableIndices.Add(index);
                        errata.Add($"File {index:000} | {ex.Message}");
                        continue;
                    }
                    garc.SetFile(index, encoded);
                }

                garc.Save();
                return [config.GetGARCFileName(archive)];
            });
        return new TextArchiveExportResponse(result.OutputFolder, result.ZipPath, result.ChangedFiles,
            failedTableIndices.ToArray(), errata.ToArray());
    }

    internal static GARCFile GetGarc(GameConfig config, TextArchiveKind kind) => kind == TextArchiveKind.Story
        ? config.GetGARCData("storytext")
        : config.GARCGameText;

    private static int RequireTable(GARCFile garc, int tableIndex) =>
        tableIndex >= 0 && tableIndex < garc.Files.Length
            ? tableIndex
            : throw new WorkspaceException("La tabla de texto indicada no existe.");
}
