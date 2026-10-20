using pk3DS.Core;
using pk3DS.Core.Structures.PersonalInfo;

namespace pk3DS.Editors;

/// <summary>Per-species personal stats: base stats, types, abilities, held items, egg groups.</summary>
public static class PersonalEditor
{
    public static PersonalEntryResponse GetEntry(PersonalEntryRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var entry = config.Personal[RequireSpecies(config, request.SpeciesIndex)];
        var gen7 = config.Generation == 7 ? entry as PersonalInfoSM : null;
        return new PersonalEntryResponse(request.SpeciesIndex,
            [entry.HP, entry.ATK, entry.DEF, entry.SPE, entry.SPA, entry.SPD],
            entry.Types, entry.CatchRate, entry.Abilities, entry.Items, entry.EggGroups,
            EVYields: [entry.EV_HP, entry.EV_ATK, entry.EV_DEF, entry.EV_SPE, entry.EV_SPA, entry.EV_SPD],
            Gender: entry.Gender,
            HatchCycles: entry.HatchCycles,
            BaseFriendship: entry.BaseFriendship,
            EXPGrowth: entry.EXPGrowth,
            Color: entry.Color & 0x0F,
            BaseEXP: entry.BaseEXP,
            Height: entry.Height,
            Weight: entry.Weight,
            TMHM: entry.TMHM.ToArray(),
            TypeTutors: entry.TypeTutors.ToArray(),
            SpecialTutors: entry.SpecialTutors.Select(bits => bits.ToArray()).ToArray(),
            EscapeRate: gen7?.EscapeRate,
            SpecialZItem: gen7?.SpecialZ_Item,
            SpecialZBaseMove: gen7?.SpecialZ_BaseMove,
            SpecialZMove: gen7?.SpecialZ_ZMove,
            LocalVariant: gen7?.LocalVariant,
            BST: entry.BST,
            ColorRaw: entry.Color);
    }

    public static ExportResult Export(PersonalExportRequest request)
    {
        if (request.Stats?.Length != 6 || request.Types?.Length != 2 || request.Abilities?.Length != 3
            || request.Items?.Length != 3 || request.EggGroups?.Length != 2)
            throw new WorkspaceException("La entrada personal está incompleta.");
        if (request.EVYields is not null && request.EVYields.Length != 6)
            throw new WorkspaceException("La entrada personal debe tener seis valores de EV.");
        if (request.SpecialTutors is not null && request.SpecialTutors.Any(bits => bits is null))
            throw new WorkspaceException("La compatibilidad de tutores contiene un grupo inválido.");

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

                ApplyExtendedFields(entry, request, config);
                GarcWriter.SavePersonal(config);
                return [config.GetGARCFileName("personal")];
            });
    }

    private static void ApplyExtendedFields(PersonalInfo entry, PersonalExportRequest request, GameConfig config)
    {
        if (request.EVYields is { } ev)
        {
            // EVYield is a packed ushort. The Core setters update only each two-bit yield and
            // therefore retain the reserved Telekenesis bit in the same word.
            entry.EV_HP = TwoBits(ev[0]);
            entry.EV_ATK = TwoBits(ev[1]);
            entry.EV_DEF = TwoBits(ev[2]);
            entry.EV_SPE = TwoBits(ev[3]);
            entry.EV_SPA = TwoBits(ev[4]);
            entry.EV_SPD = TwoBits(ev[5]);
        }

        if (request.Gender is int gender) entry.Gender = Byte(gender);
        if (request.HatchCycles is int hatchCycles) entry.HatchCycles = Byte(hatchCycles);
        if (request.BaseFriendship is int friendship) entry.BaseFriendship = Byte(friendship);
        if (request.EXPGrowth is int growth) entry.EXPGrowth = Byte(growth);
        if (request.Color is int color) entry.Color = (entry.Color & 0x30) | Math.Clamp(color, 0, 0x0F);
        if (request.BaseEXP is int baseExp) entry.BaseEXP = Word(baseExp);
        if (request.Height is int height) entry.Height = Word(height);
        if (request.Weight is int weight) entry.Weight = Word(weight);

        // Windows only shows the leading compatibility entries. Merge those entries into the
        // complete arrays read from the record so packed bits with no visible control survive.
        if (request.TMHM is { } tmhm)
            MergeBits(entry.TMHM, tmhm, VisibleTmHmCount(config));
        if (request.TypeTutors is { } typeTutors)
            MergeBits(entry.TypeTutors, typeTutors, VisibleTypeTutorCount(config));
        if (request.SpecialTutors is { } specialTutors)
            MergeSpecialTutors(entry.SpecialTutors, specialTutors, config);

        // Escape rate, Z fields, and the regional variant are represented only by the Gen. VII
        // PersonalInfoSM record and by the Gen. VII Windows editor. Gen. VI requests leave them
        // untouched even if a newer caller sends the optional fields.
        if (config.Generation != 7 || entry is not PersonalInfoSM sm)
            return;
        if (request.EscapeRate is int escapeRate) sm.EscapeRate = Byte(escapeRate);
        if (request.SpecialZItem is int zItem) sm.SpecialZ_Item = Word(zItem);
        if (request.SpecialZBaseMove is int zBaseMove) sm.SpecialZ_BaseMove = Word(zBaseMove);
        if (request.SpecialZMove is int zMove) sm.SpecialZ_ZMove = Word(zMove);
        if (request.LocalVariant is bool localVariant) sm.LocalVariant = localVariant;
    }

    private static void MergeSpecialTutors(bool[][] current, bool[][] incoming, GameConfig config)
    {
        var visibleGroups = config.ORAS
            ? new[] { 15, 17, 16, 15 }
            : config.USUM
                ? new[] { 67 }
                : Array.Empty<int>();
        for (var group = 0; group < Math.Min(Math.Min(current.Length, incoming.Length), visibleGroups.Length); group++)
        {
            if (incoming[group] is null)
                continue;
            MergeBits(current[group], incoming[group], visibleGroups[group]);
        }
    }

    private static void MergeBits(bool[] current, bool[] incoming, int visibleCount)
    {
        var count = Math.Min(Math.Min(current.Length, incoming.Length), visibleCount);
        for (var i = 0; i < count; i++)
            current[i] = incoming[i];
    }

    private static int VisibleTmHmCount(GameConfig config) => config.Generation == 6
        ? config.ORAS ? 107 : 105
        : 100;

    private static int VisibleTypeTutorCount(GameConfig config) => config.Generation == 6
        ? config.ORAS ? 8 : 7
        : 8;

    private static int TwoBits(int value) => Math.Clamp(value, 0, 3);
    private static int Byte(int value) => Math.Clamp(value, byte.MinValue, byte.MaxValue);
    private static int Word(int value) => Math.Clamp(value, ushort.MinValue, ushort.MaxValue);

    private static int[] Clamp(int[] values, int max) => values.Select(value => Math.Clamp(value, 0, max)).ToArray();

    private static int RequireSpecies(GameConfig config, int speciesIndex) =>
        speciesIndex >= 0 && speciesIndex < config.Personal.Table.Length
            ? speciesIndex
            : throw new WorkspaceException("La especie indicada no existe.");
}
