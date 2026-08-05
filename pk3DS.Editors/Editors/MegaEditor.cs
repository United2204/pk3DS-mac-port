using pk3DS.Core;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>Mega evolution triggers, one variable-length table per species.</summary>
public static class MegaEditor
{
    public static MegaTableResponse GetTable(MegaTableRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var garc = config.GetGARCData("megaevo");
        var mega = new MegaEvolutions(garc.Files[RequireSpecies(garc, request.SpeciesIndex)]);
        return new MegaTableResponse(request.SpeciesIndex,
            mega.Form.Select((_, i) => new MegaEntry(mega.Form[i], mega.Method[i], mega.Argument[i], mega.u6[i])).ToArray());
    }

    public static ExportResult Export(MegaExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "megaevo", ["megaevo"], config =>
            {
                var garc = config.GetGARCData("megaevo");
                var index = RequireSpecies(garc, request.SpeciesIndex);
                var mega = new MegaEvolutions(garc.Files[index]);
                var entries = request.Entries ?? [];
                // The table length is fixed per species by the file itself; it cannot grow or shrink.
                if (entries.Length != mega.Form.Length || entries.Any(IsOutOfRange))
                    throw new WorkspaceException("Las entradas mega no son válidas.");

                mega.Form = entries.Select(entry => (ushort)entry.Form).ToArray();
                mega.Method = entries.Select(entry => (ushort)entry.Method).ToArray();
                mega.Argument = entries.Select(entry => (ushort)entry.Argument).ToArray();
                mega.u6 = entries.Select(entry => (ushort)entry.Auxiliary).ToArray();
                garc.SetFile(index, mega.Write());
                garc.Save();
                return [config.GetGARCFileName("megaevo")];
            });

    private static bool IsOutOfRange(MegaEntry entry) =>
        entry.Form is < 0 or > ushort.MaxValue
        || entry.Method is < 0 or > ushort.MaxValue
        || entry.Argument is < 0 or > ushort.MaxValue
        || entry.Auxiliary is < 0 or > ushort.MaxValue;

    private static int RequireSpecies(GARCFile garc, int speciesIndex) =>
        speciesIndex >= 0 && speciesIndex < garc.Files.Length
            ? speciesIndex
            : throw new WorkspaceException("La especie indicada no existe.");
}
