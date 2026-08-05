using pk3DS.Core;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>Evolution methods, stored as a fixed block of eight entries per species.</summary>
public static class EvolutionEditor
{
    private const int EntriesPerSpecies = 8;

    public static EvolutionTableResponse GetTable(EvolutionTableRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var index = RequireSpecies(config, request.SpeciesIndex);
        return new EvolutionTableResponse(request.SpeciesIndex, config.Evolutions[index].PossibleEvolutions
            .Select(e => new EvolutionEntry(e.Method, e.Argument, e.Species, e.Form, e.Level)).ToArray());
    }

    public static ExportResult Export(EvolutionExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "evolution", [], config =>
            {
                var index = RequireSpecies(config, request.SpeciesIndex);
                var entries = request.Entries ?? [];
                if (entries.Length != EntriesPerSpecies || entries.Any(IsOutOfRange))
                    throw new WorkspaceException("Cada especie debe tener ocho entradas de evolución válidas.");

                config.Evolutions[index].PossibleEvolutions = entries.Select(e => new EvolutionMethod
                {
                    Method = e.Method, Argument = e.Argument, Species = e.Species, Form = e.Form, Level = e.Level,
                }).ToArray();
                GarcWriter.SaveEvolutions(config);
                return [config.GetGARCFileName("evolution")];
            });

    private static bool IsOutOfRange(EvolutionEntry e) =>
        e.Method is < 0 or > ushort.MaxValue
        || e.Argument is < 0 or > ushort.MaxValue
        || e.Species is < 0 or > ushort.MaxValue
        || e.Level is < 0 or > byte.MaxValue
        || e.Form is < sbyte.MinValue or > sbyte.MaxValue;

    private static int RequireSpecies(GameConfig config, int speciesIndex) =>
        speciesIndex >= 0 && speciesIndex < config.Evolutions.Length
            ? speciesIndex
            : throw new WorkspaceException("La especie indicada no existe.");
}
