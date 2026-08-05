using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>Per-species personal stats: base stats, types, abilities, held items, egg groups.</summary>
public static class PersonalEditor
{
    public static PersonalEntryResponse GetEntry(PersonalEntryRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var entry = config.Personal[RequireSpecies(config, request.SpeciesIndex)];
        return new PersonalEntryResponse(request.SpeciesIndex,
            [entry.HP, entry.ATK, entry.DEF, entry.SPE, entry.SPA, entry.SPD],
            entry.Types, entry.CatchRate, entry.Abilities, entry.Items, entry.EggGroups);
    }

    public static ExportResult Export(PersonalExportRequest request)
    {
        if (request.Stats?.Length != 6 || request.Types?.Length != 2 || request.Abilities?.Length != 3
            || request.Items?.Length != 3 || request.EggGroups?.Length != 2)
            throw new WorkspaceException("La entrada personal está incompleta.");

        return EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "personal", [], config =>
            {
                var entry = config.Personal[RequireSpecies(config, request.SpeciesIndex)];
                entry.Stats = Clamp(request.Stats, byte.MaxValue);
                entry.Types = Clamp(request.Types, byte.MaxValue);
                entry.CatchRate = Math.Clamp(request.CatchRate, 0, byte.MaxValue);
                entry.Abilities = Clamp(request.Abilities, byte.MaxValue);
                entry.Items = Clamp(request.Items, ushort.MaxValue);
                entry.EggGroups = Clamp(request.EggGroups, byte.MaxValue);
                GarcWriter.SavePersonal(config);
                return [config.GetGARCFileName("personal")];
            });
    }

    private static int[] Clamp(int[] values, int max) => values.Select(value => Math.Clamp(value, 0, max)).ToArray();

    private static int RequireSpecies(GameConfig config, int speciesIndex) =>
        speciesIndex >= 0 && speciesIndex < config.Personal.Table.Length
            ? speciesIndex
            : throw new WorkspaceException("La especie indicada no existe.");
}
