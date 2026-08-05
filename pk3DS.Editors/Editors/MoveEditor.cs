using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>Per-move stats: type, category, power, accuracy, PP and priority.</summary>
public static class MoveEditor
{
    public static MoveEntryResponse GetEntry(MoveEntryRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var move = config.Moves[RequireMove(config, request.MoveIndex)];
        return new MoveEntryResponse(request.MoveIndex, move.Type, move.Category, move.Power, move.Accuracy, move.PP, move.Priority);
    }

    public static ExportResult Export(MoveExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "move", [], config =>
            {
                var move = config.Moves[RequireMove(config, request.MoveIndex)];
                move.Type = Math.Clamp(request.Type, 0, 17);
                move.Category = Math.Clamp(request.Category, 0, 2);
                move.Power = Math.Clamp(request.Power, 0, byte.MaxValue);
                move.Accuracy = Math.Clamp(request.Accuracy, 0, byte.MaxValue);
                move.PP = Math.Clamp(request.PP, 0, byte.MaxValue);
                move.Priority = Math.Clamp(request.Priority, sbyte.MinValue, sbyte.MaxValue);
                GarcWriter.SaveMoves(config);
                return [config.GetGARCFileName("move")];
            });

    // Index 0 is the empty move slot, not a usable move.
    private static int RequireMove(GameConfig config, int moveIndex) =>
        moveIndex >= 1 && moveIndex < config.Moves.Length
            ? moveIndex
            : throw new WorkspaceException("El movimiento indicado no existe.");
}
