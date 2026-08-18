using pk3DS.Core;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// Static encounters for Gen VII: gifts, fixed encounters and in-game trades. The three groups
/// live in different files of the <c>encounterstatic</c> GARC with different record sizes.
/// </summary>
public static class StaticEditor
{
    public static StaticCatalogResponse GetCatalog(StaticCatalogRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen7(config, "encuentros estáticos");
        var garc = config.GetGARCData("encounterstatic");
        if (garc.Files.Length <= 4)
            throw new WorkspaceException("El archivo de encuentros estáticos no tiene el formato esperado.");

        return new StaticCatalogResponse(
            [
                new StaticGroupSummary("gift", "Regalos", garc.Files[0].Length / EncounterGift7.SIZE),
                new StaticGroupSummary("static", "Encuentros fijos", garc.Files[1].Length / EncounterStatic7.SIZE),
                new StaticGroupSummary("trade", "Intercambios", garc.Files[4].Length / EncounterTrade7.SIZE),
            ],
            Catalogs.Species(config), Catalogs.Items(config), Catalogs.Moves(config));
    }

    public static StaticEntryResponse GetEntry(StaticEntryRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen7(config, "encuentros estáticos");
        return ToResponse(config.GetGARCData("encounterstatic"), request.Group, request.EntryIndex);
    }

    public static ExportResult Export(StaticExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "static", ["encounterstatic"], config =>
            {
                Guard.Gen7(config, "encuentros estáticos");
                var garc = config.GetGARCData("encounterstatic");
                var entry = Validate(request.Entry, Catalogs.SpeciesCount(config), Catalogs.ItemCount(config), Catalogs.MoveCount(config));
                Apply(garc, request.Group, request.EntryIndex, entry);
                garc.Save();
                return [config.GetGARCFileName("encounterstatic")];
            });

    private static (int FileIndex, int Size) GetGroupLayout(string group) => group switch
    {
        "gift" => (0, EncounterGift7.SIZE),
        "static" => (1, EncounterStatic7.SIZE),
        "trade" => (4, EncounterTrade7.SIZE),
        _ => throw new WorkspaceException("El grupo de encuentros estáticos no es válido."),
    };

    private static byte[] ReadRecord(GARCFile garc, string group, int entryIndex, out int fileIndex, out int size)
    {
        (fileIndex, size) = GetGroupLayout(group);
        if (fileIndex >= garc.Files.Length || entryIndex < 0 || (entryIndex + 1) * size > garc.Files[fileIndex].Length)
            throw new WorkspaceException("La entrada de encuentro estático no existe.");
        return garc.Files[fileIndex].Skip(entryIndex * size).Take(size).ToArray();
    }

    private static StaticEntryResponse ToResponse(GARCFile garc, string group, int entryIndex)
    {
        var data = ReadRecord(garc, group, entryIndex, out _, out _);
        return group switch
        {
            "gift" => Describe(group, entryIndex, new EncounterGift7(data)),
            "static" => Describe(group, entryIndex, new EncounterStatic7(data)),
            "trade" => Describe(group, entryIndex, new EncounterTrade7(data)),
            _ => throw new WorkspaceException("El grupo de encuentros estáticos no es válido."),
        };
    }

    private static StaticEntryResponse Describe(string group, int index, EncounterGift7 entry) => new(group, index,
        new StaticEntry(entry.Species, entry.Form, entry.Level, entry.HeldItem, Gender: entry.Gender, Ability: entry.Ability, Nature: entry.Nature,
            ShinyLock: entry.ShinyLock, IsEgg: entry.IsEgg, SpecialMove: entry.SpecialMove));

    private static StaticEntryResponse Describe(string group, int index, EncounterStatic7 entry) => new(group, index,
        new StaticEntry(entry.Species, entry.Form, entry.Level, entry.HeldItem, Gender: entry.Gender, Ability: entry.Ability,
            Nature: entry.Nature, Shiny: entry.Shiny, ShinyLock: entry.ShinyLock, Map: entry.Map, RelearnMoves: entry.RelearnMoves,
            IVs: entry.IVs, EVs: entry.EVs, Aura: entry.Aura, Allies: entry.Allies, Ally1: entry.Ally1, Ally2: entry.Ally2));

    private static StaticEntryResponse Describe(string group, int index, EncounterTrade7 entry) => new(group, index,
        new StaticEntry(entry.Species, entry.Form, entry.Level, entry.HeldItem, Gender: entry.Gender, Ability: entry.Ability,
            Nature: entry.Nature, IVs: entry.IVs, TradeRequestSpecies: entry.TradeRequestSpecies, TID: entry.TID,
            SID: entry.SID, OTGender: entry.OT_Gender, OTIntensity: entry.OT_Intensity, OTMemory: entry.OT_Memory,
            OTTextVar: entry.OT_TextVar, OTFeeling: entry.OT_Feeling));

    /// <summary>
    /// Applies only the fields the request actually set. A null field means "leave as-is", so the
    /// UI can send a partial entry without clearing values it does not expose.
    /// </summary>
    private static void Apply(GARCFile garc, string group, int entryIndex, StaticEntry entry)
    {
        var data = ReadRecord(garc, group, entryIndex, out var fileIndex, out var size);
        switch (group)
        {
            case "gift":
                var gift = new EncounterGift7(data) { Species = entry.Species, Form = entry.Form, Level = entry.Level, HeldItem = entry.HeldItem };
                if (entry.Gender is not null) gift.Gender = entry.Gender.Value;
                if (entry.Ability is not null) gift.Ability = (sbyte)entry.Ability.Value;
                if (entry.Nature is not null) gift.Nature = (sbyte)entry.Nature.Value;
                if (entry.ShinyLock is not null) gift.ShinyLock = entry.ShinyLock.Value;
                if (entry.IsEgg is not null) gift.IsEgg = entry.IsEgg.Value;
                if (entry.SpecialMove is not null) gift.SpecialMove = entry.SpecialMove.Value;
                data = gift.Data;
                break;
            case "static":
                var encounter = new EncounterStatic7(data) { Species = entry.Species, Form = entry.Form, Level = entry.Level, HeldItem = entry.HeldItem };
                if (entry.Gender is not null) encounter.Gender = entry.Gender.Value;
                if (entry.Ability is not null) encounter.Ability = entry.Ability.Value;
                if (entry.Nature is not null) encounter.Nature = entry.Nature.Value;
                if (entry.Shiny is not null) encounter.Shiny = entry.Shiny.Value;
                if (entry.ShinyLock is not null) encounter.ShinyLock = entry.ShinyLock.Value;
                if (entry.Map is not null) encounter.Map = entry.Map.Value;
                if (entry.RelearnMoves is { Length: 4 }) encounter.RelearnMoves = entry.RelearnMoves;
                if (entry.IVs is { Length: 6 }) encounter.IVs = entry.IVs;
                if (entry.EVs is { Length: 6 }) encounter.EVs = entry.EVs;
                if (entry.Aura is not null) encounter.Aura = entry.Aura.Value;
                if (entry.Allies is not null) encounter.Allies = entry.Allies.Value;
                if (entry.Ally1 is not null) encounter.Ally1 = entry.Ally1.Value;
                if (entry.Ally2 is not null) encounter.Ally2 = entry.Ally2.Value;
                data = encounter.Data;
                break;
            case "trade":
                var trade = new EncounterTrade7(data) { Species = entry.Species, Form = entry.Form, Level = entry.Level, HeldItem = entry.HeldItem };
                if (entry.Gender is not null) trade.Gender = entry.Gender.Value;
                if (entry.Ability is not null) trade.Ability = entry.Ability.Value;
                if (entry.Nature is not null) trade.Nature = entry.Nature.Value;
                if (entry.IVs is { Length: 6 }) trade.IVs = entry.IVs;
                if (entry.TradeRequestSpecies is not null) trade.TradeRequestSpecies = entry.TradeRequestSpecies.Value;
                if (entry.TID is not null) trade.TID = entry.TID.Value;
                if (entry.SID is not null) trade.SID = entry.SID.Value;
                if (entry.OTGender is not null) trade.OT_Gender = entry.OTGender.Value;
                if (entry.OTIntensity is not null) trade.OT_Intensity = (ushort)entry.OTIntensity.Value;
                if (entry.OTMemory is not null) trade.OT_Memory = (ushort)entry.OTMemory.Value;
                if (entry.OTTextVar is not null) trade.OT_TextVar = (ushort)entry.OTTextVar.Value;
                if (entry.OTFeeling is not null) trade.OT_Feeling = (ushort)entry.OTFeeling.Value;
                data = trade.Data;
                break;
            default:
                throw new WorkspaceException("El grupo de encuentros estáticos no es válido.");
        }
        // Several records share one file, so only this entry's slice is overwritten.
        garc.PatchFile(fileIndex, data, entryIndex * size);
    }

    /// <summary>Returns the validated entry so callers get a non-null value to apply.</summary>
    internal static StaticEntry Validate(StaticEntry? entry, int speciesCount, int itemCount, int moveCount)
    {
        if (entry is null || IsOutOfRange(entry, speciesCount, itemCount, moveCount))
            throw new WorkspaceException("La especie, forma, nivel u objeto no son válidos.");
        return entry;
    }

    private static bool IsOutOfRange(StaticEntry entry, int speciesCount, int itemCount, int moveCount) =>
        entry.Species < 0 || entry.Species >= speciesCount
        || entry.Form is < 0 or > byte.MaxValue
        || entry.Level is < 1 or > 100
        || entry.HeldItem < 0 || entry.HeldItem >= itemCount
        || entry.Gender is < -1 or > 3
        || entry.Ability is < -1 or > 7
        || entry.Nature is < -1 or > 25
        || entry.Aura is < 0 or > 18
        || entry.Ally1 is < 0 or > byte.MaxValue
        || entry.Ally2 is < 0 or > byte.MaxValue
        || entry.Map is < 0 or > short.MaxValue - 1
        || entry.Allies is < 0 or > byte.MaxValue
        || entry.SpecialMove < 0 || entry.SpecialMove >= moveCount
        || entry.TradeRequestSpecies < 0 || entry.TradeRequestSpecies >= speciesCount
        || entry.TID is < 0 or > ushort.MaxValue
        || entry.SID is < 0 or > ushort.MaxValue
        || entry.OTGender is < 0 or > byte.MaxValue
        || entry.OTIntensity is < 0 or > ushort.MaxValue
        || entry.OTMemory is < 0 or > ushort.MaxValue
        || entry.OTTextVar is < 0 or > ushort.MaxValue
        || entry.OTFeeling is < 0 or > ushort.MaxValue
        || entry.RelearnMoves is { Length: not 4 }
        || entry.RelearnMoves?.Any(move => move < 0 || move >= moveCount) == true
        || entry.IVs is { Length: not 6 }
        || entry.IVs?.Any(iv => iv is < -3 or > 31) == true
        || entry.EVs is { Length: not 6 }
        || entry.EVs?.Any(ev => ev is < 0 or > byte.MaxValue) == true;
}
