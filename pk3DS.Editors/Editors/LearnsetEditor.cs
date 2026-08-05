using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>Level-up movesets: one list of (level, move) pairs per species/form entry.</summary>
public static class LearnsetEditor
{
    public static LearnsetCatalogResponse GetCatalog(LearnsetCatalogRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var names = config.GetText(TextName.SpeciesNames);
        var species = config.Learnsets.Select((set, index) => new LearnsetSpeciesSummary(index,
            index < names.Length && !string.IsNullOrWhiteSpace(names[index]) ? names[index] : $"Forma {index:000}", set.Count)).ToArray();
        // Blank move names are dropped rather than given a placeholder: this list is the picker
        // for a single learnset slot, and unnamed ids are not selectable moves.
        var moves = config.GetText(TextName.MoveNames)
            .Select((name, index) => new NamedEntry(index, name))
            .Where(entry => entry.Id > 0 && !string.IsNullOrWhiteSpace(entry.Name))
            .ToArray();
        return new LearnsetCatalogResponse(workspace.Version.ToString(), species, moves);
    }

    public static LearnsetTableResponse GetTable(LearnsetTableRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var set = config.Learnsets[RequireSpecies(config, request.SpeciesIndex)];
        return new LearnsetTableResponse(request.SpeciesIndex,
            set.Moves.Select((move, index) => new LearnsetEntry(Math.Clamp(set.Levels[index], 1, 100), move)).ToArray());
    }

    public static ExportResult Export(LearnsetExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "levelup", [], config =>
            {
                var index = RequireSpecies(config, request.SpeciesIndex);
                var entries = request.Entries ?? [];
                if (entries.Any(entry => entry.MoveId < 1 || entry.MoveId >= config.Moves.Length))
                    throw new WorkspaceException("Una de las entradas tiene un movimiento inválido.");

                var set = config.Learnsets[index];
                set.Levels = entries.Select(entry => Math.Clamp(entry.Level, 1, 100)).ToArray();
                set.Moves = entries.Select(entry => entry.MoveId).ToArray();
                config.GARCLearnsets.Files[index] = set.Write();
                config.GARCLearnsets.Save();
                return [config.GetGARCFileName("levelup")];
            });

    private static int RequireSpecies(GameConfig config, int speciesIndex) =>
        speciesIndex >= 0 && speciesIndex < config.Learnsets.Length
            ? speciesIndex
            : throw new WorkspaceException("La especie indicada no existe.");
}
