using pk3DS.Core;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>Per-move stats using the complete Gen. VI/VII record exposed by the Windows editor.</summary>
public static class MoveEditor
{
    public static MoveEntryResponse GetEntry(MoveEntryRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var move = config.Moves[RequireMove(config, request.MoveIndex)];
        var gen7 = move as Move7;

        return new MoveEntryResponse(
            request.MoveIndex,
            Type: move.Type,
            Category: move.Category,
            Power: move.Power,
            Accuracy: move.Accuracy,
            PP: move.PP,
            Priority: SignedByte(move.Priority),
            Quality: move.Quality,
            HitMin: move.HitMin,
            HitMax: move.HitMax,
            Inflict: SignedShort(move.Inflict),
            InflictPercent: move.InflictPercent,
            InflictCount: (int)move.InflictCount,
            TurnMin: move.TurnMin,
            TurnMax: move.TurnMax,
            CritStage: move.CritStage,
            Flinch: move.Flinch,
            EffectSequence: move.EffectSequence,
            Recoil: SignedByte(move.Recoil),
            Healing: (int)move.Healing,
            Target: (int)move.Target,
            Stat1: move.Stat1,
            Stat2: move.Stat2,
            Stat3: move.Stat3,
            Stat1Stage: SignedByte(move.Stat1Stage),
            Stat2Stage: SignedByte(move.Stat2Stage),
            Stat3Stage: SignedByte(move.Stat3Stage),
            Stat1Percent: move.Stat1Percent,
            Stat2Percent: move.Stat2Percent,
            Stat3Percent: move.Stat3Percent,
            Flags: GetFlags(move),
            Generation: config.Generation,
            ZMove: gen7?.ZMove,
            ZPower: gen7?.ZPower,
            ZEffect: gen7?.ZEffect,
            RefreshAfflictType: gen7 is null ? null : (int)gen7.RefreshAfflictType,
            RefreshAfflictPercent: gen7?.RefreshAfflictPercent);
    }

    public static ExportResult Export(MoveExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "move", [], config =>
            {
                var move = config.Moves[RequireMove(config, request.MoveIndex)];

                // Apply storage-width conversions only. This keeps unknown-but-valid values intact
                // when a caller reads and writes a record without changing that field.
                move.Type = Byte(request.Type);
                move.Category = Byte(request.Category);
                move.Power = Byte(request.Power);
                move.Accuracy = Byte(request.Accuracy);
                move.PP = Byte(request.PP);
                move.Priority = SignedByteValue(request.Priority);

                if (request.Quality is int quality) move.Quality = Byte(quality);
                if (request.HitMin is int hitMin) move.HitMin = Nibble(hitMin);
                if (request.HitMax is int hitMax) move.HitMax = Nibble(hitMax);
                if (request.Inflict is int inflict) move.Inflict = RawShort(inflict);
                if (request.InflictPercent is int inflictPercent) move.InflictPercent = Byte(inflictPercent);
                if (request.InflictCount is int inflictCount) move.InflictCount = (MoveInflictDuration)Byte(inflictCount);
                if (request.TurnMin is int turnMin) move.TurnMin = Byte(turnMin);
                if (request.TurnMax is int turnMax) move.TurnMax = Byte(turnMax);
                if (request.CritStage is int critStage) move.CritStage = Byte(critStage);
                if (request.Flinch is int flinch) move.Flinch = Byte(flinch);
                if (request.EffectSequence is int effect) move.EffectSequence = Word(effect);
                if (request.Recoil is int recoil) move.Recoil = SignedByteValue(recoil);
                if (request.Healing is int healing) move.Healing = (Heal)Byte(healing);
                if (request.Target is int target) move.Target = (MoveTarget)Byte(target);
                if (request.Stat1 is int stat1) move.Stat1 = Byte(stat1);
                if (request.Stat2 is int stat2) move.Stat2 = Byte(stat2);
                if (request.Stat3 is int stat3) move.Stat3 = Byte(stat3);
                if (request.Stat1Stage is int stat1Stage) move.Stat1Stage = SignedByteValue(stat1Stage);
                if (request.Stat2Stage is int stat2Stage) move.Stat2Stage = SignedByteValue(stat2Stage);
                if (request.Stat3Stage is int stat3Stage) move.Stat3Stage = SignedByteValue(stat3Stage);
                if (request.Stat1Percent is int stat1Percent) move.Stat1Percent = Byte(stat1Percent);
                if (request.Stat2Percent is int stat2Percent) move.Stat2Percent = Byte(stat2Percent);
                if (request.Stat3Percent is int stat3Percent) move.Stat3Percent = Byte(stat3Percent);

                if (request.Flags is uint flags)
                {
                    if (move is Move7 gen7)
                        gen7.Flags = (MoveFlag7)flags;
                    else if (move is Move6 gen6)
                        gen6.Flags = (MoveFlag6)flags;
                }

                // Gen. VII has five additional bytes/words. Leave them untouched when an older
                // caller omits the nullable values, which also preserves any unknown raw bytes.
                if (move is Move7 move7)
                {
                    if (request.ZMove is int zMove) move7.ZMove = Word(zMove);
                    if (request.ZPower is int zPower) move7.ZPower = Byte(zPower);
                    if (request.ZEffect is int zEffect) move7.ZEffect = Byte(zEffect);
                    if (request.RefreshAfflictType is int refreshType) move7.RefreshAfflictType = (RefreshType)Byte(refreshType);
                    if (request.RefreshAfflictPercent is int refreshPercent) move7.RefreshAfflictPercent = Byte(refreshPercent);
                }

                GarcWriter.SaveMoves(config);
                return [config.GetGARCFileName("move")];
            });

    private static uint GetFlags(Move move) => move switch
    {
        Move7 gen7 => (uint)gen7.Flags,
        Move6 gen6 => (uint)gen6.Flags,
        _ => 0,
    };

    private static int Byte(int value) => Math.Clamp(value, byte.MinValue, byte.MaxValue);
    private static int Nibble(int value) => Math.Clamp(value, 0, 0xF);
    private static int RawShort(int value) => Math.Clamp(value, short.MinValue, ushort.MaxValue) & ushort.MaxValue;
    private static int Word(int value) => Math.Clamp(value, ushort.MinValue, ushort.MaxValue);
    private static int SignedByte(int value) => unchecked((sbyte)(byte)value);
    private static int SignedByteValue(int value) => unchecked((byte)Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue));
    private static int SignedShort(int value) => unchecked((short)(ushort)value);

    // Index 0 is the empty move slot, not a usable move.
    private static int RequireMove(GameConfig config, int moveIndex) =>
        moveIndex >= 1 && moveIndex < config.Moves.Length
            ? moveIndex
            : throw new WorkspaceException("El movimiento indicado no existe.");
}
