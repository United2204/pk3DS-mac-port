using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>Egg moves, stored one file per species with a different layout in Gen VI and VII.</summary>
public static class EggMoveEditor
{
    public static EggMoveTableResponse GetTable(EggMoveTableRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var garc = config.GetGARCData("eggmove");
        var set = Read(config, garc, RequireSpecies(garc, request.SpeciesIndex));
        return new EggMoveTableResponse(request.SpeciesIndex, set.Moves, set.FormTableIndex);
    }

    public static ExportResult Export(EggMoveExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "eggmove", [], config =>
            {
                var garc = config.GetGARCData("eggmove");
                var index = RequireSpecies(garc, request.SpeciesIndex);
                var moves = request.Moves ?? [];
                if (moves.Any(move => move < 1 || move >= config.Moves.Length))
                    throw new WorkspaceException("Hay un movimiento inválido.");

                var set = Read(config, garc, index);
                set.Moves = moves.Distinct().ToArray();
                if (config.Generation == 7 && request.FormTableIndex is not null)
                    set.FormTableIndex = Math.Clamp(request.FormTableIndex.Value, 0, ushort.MaxValue);
                garc.SetFile(index, set.Write());
                garc.Save();
                return [config.GetGARCFileName("eggmove")];
            });

    private static EggMoves Read(GameConfig config, GARCFile garc, int index) => config.Generation == 6
        ? new EggMoves6(garc.Files[index])
        : new EggMoves7(garc.Files[index]);

    private static int RequireSpecies(GARCFile garc, int speciesIndex) =>
        speciesIndex >= 0 && speciesIndex < garc.Files.Length
            ? speciesIndex
            : throw new WorkspaceException("La especie indicada no existe.");
}
