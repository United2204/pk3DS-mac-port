using System.Runtime.InteropServices;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;

namespace pk3DS.Editors.Tests;

/// <summary>
/// Builds a throwaway workspace on disk: real GARCs holding synthetic records, laid out where
/// <see cref="GameConfig"/> expects them. This is what lets the editors be tested end to end
/// without a multi-gigabyte dump.
/// </summary>
public abstract class SyntheticWorkspace : IDisposable
{
    /// <summary>English. Language-variant GARCs resolve to their file number plus this.</summary>
    protected const int Language = 1;

    /// <summary>A syntactically valid Title ID, since there is no exheader.bin to read one from.</summary>
    public const string TitleId = "000400000005D000";

    public string Root { get; }
    public string RomFs => Path.Combine(Root, "RomFS");
    public string OutputDirectory { get; }

    public int SpeciesCount { get; }
    public int MoveCount { get; }
    public int ItemCount { get; }

    protected static int ItemSize => Marshal.SizeOf<Item>();

    protected SyntheticWorkspace(int archiveFileCount, int speciesCount, int moveCount, int itemCount)
    {
        SpeciesCount = speciesCount;
        MoveCount = moveCount;
        ItemCount = itemCount;

        Root = Path.Combine(Path.GetTempPath(), $"pk3ds-fixture-{Guid.NewGuid():N}");
        OutputDirectory = Path.Combine(Root, "output");
        Directory.CreateDirectory(OutputDirectory);

        // Version detection counts single-character file names under a/, so the tree has to hold
        // exactly the count that maps to the target game before any GARC content matters.
        for (var fileNumber = 0; fileNumber < archiveFileCount; fileNumber++)
            File.WriteAllBytes(PathFor(fileNumber), []);
    }

    protected string PathFor(int fileNumber)
    {
        var path = Path.Combine(RomFs, "a",
            (fileNumber / 100 % 10).ToString(),
            (fileNumber / 10 % 10).ToString(),
            (fileNumber % 10).ToString());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    /// <summary>
    /// Packs <paramref name="files"/> into a real GARC at the given file number. The folder-based
    /// packer is used because it is the public entry point; it orders entries by parsing each file
    /// name as an integer, so plain numeric names are enough.
    /// </summary>
    protected void WriteGarc(int fileNumber, byte[][] files)
    {
        var staging = Path.Combine(Root, "staging", fileNumber.ToString());
        Directory.CreateDirectory(staging);
        for (var i = 0; i < files.Length; i++)
            File.WriteAllBytes(Path.Combine(staging, i.ToString()), files[i]);

        GARC.PackGARC(staging, PathFor(fileNumber), GARC.VER_6, bytesPadding: 4);
        Directory.Delete(staging, recursive: true);
    }

    protected static byte[][] Repeat(int count, Func<int, byte[]> build) =>
        Enumerable.Range(0, count).Select(build).ToArray();

    /// <summary>
    /// The personal GARC stores one file per species followed by a packed copy of the whole table;
    /// the packed trailer is the file <see cref="PersonalTable"/> actually reads.
    /// </summary>
    protected byte[][] BuildPersonalFiles(int entrySize)
    {
        var entries = Repeat(SpeciesCount, _ => new byte[entrySize]);
        var packed = new byte[SpeciesCount * entrySize];
        for (var i = 0; i < SpeciesCount; i++)
            entries[i].CopyTo(packed, i * entrySize);
        return [.. entries, packed];
    }

    /// <summary>Learnsets are (move, level) ushort pairs closed by a 0xFFFF sentinel in both generations.</summary>
    protected static byte[] Learnset(params (ushort Move, ushort Level)[] entries)
    {
        var data = new byte[(entries.Length * 4) + 4];
        for (var i = 0; i < entries.Length; i++)
        {
            BitConverter.GetBytes(entries[i].Move).CopyTo(data, i * 4);
            BitConverter.GetBytes(entries[i].Level).CopyTo(data, (i * 4) + 2);
        }
        BitConverter.GetBytes(ushort.MaxValue).CopyTo(data, entries.Length * 4);
        BitConverter.GetBytes(ushort.MaxValue).CopyTo(data, (entries.Length * 4) + 2);
        return data;
    }

    /// <summary>
    /// Text tables are built through <see cref="TextFile"/> itself rather than hand-packed, so the
    /// fixture cannot drift from the real encoder. Only the tables the editors read are populated.
    /// </summary>
    protected static byte[][] BuildTextFiles(int tableCount, params (int Index, string Prefix, int Count)[] populated)
    {
        // TextFile only consults the config for variable codes, which plain names never use, so an
        // uninitialised config is enough to encode here.
        var config = new GameConfig(GameVersion.XY);
        var files = new byte[tableCount][];
        for (var i = 0; i < tableCount; i++)
            files[i] = new TextFile(config).Data;

        foreach (var (index, prefix, count) in populated)
        {
            files[index] = new TextFile(config)
            {
                Lines = Enumerable.Range(0, count).Select(i => $"{prefix}{i}").ToArray(),
            }.Data;
        }
        return files;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// An X/Y workspace. X/Y is the cheapest fixture because <c>GameConfig.GetGameData</c> touches no
/// files for it, unlike the Gen VII branches which stat <c>encdata</c> to pick a version variant.
/// </summary>
public sealed class SyntheticXyWorkspace : SyntheticWorkspace
{
    private const int ArchiveFileCount = 271;

    // GARC file numbers for X/Y, from GARCReference.GARCReference_XY.
    private const int MoveGarc = 212, EggMoveGarc = 213, LevelUpGarc = 214,
        EvolutionGarc = 215, MegaEvoGarc = 216, PersonalGarc = 218, ItemGarc = 220, GameTextGarc = 72;

    // Text table indexes inside the gametext GARC, from TextReference.GameText_XY.
    private const int MoveNamesTable = 13, SpeciesNamesTable = 80, ItemNamesTable = 96;

    private const int Move6Size = 0x22;

    public SyntheticXyWorkspace(int speciesCount = 12, int moveCount = 10, int itemCount = 8)
        : base(ArchiveFileCount, speciesCount, moveCount, itemCount)
    {
        WriteGarc(PersonalGarc, BuildPersonalFiles(PersonalInfoXY.SIZE));
        WriteGarc(LevelUpGarc, Repeat(speciesCount, _ => Learnset((1, 1))));
        WriteGarc(EvolutionGarc, Repeat(speciesCount, _ => new byte[EvolutionSet6.SIZE]));
        WriteGarc(EggMoveGarc, Repeat(speciesCount, _ => Gen6EggMoves()));
        WriteGarc(MegaEvoGarc, Repeat(speciesCount, _ => new byte[0x10]));
        // Only X/Y stores moves as loose files; Gen VII packs them into a mini archive.
        WriteGarc(MoveGarc, Repeat(moveCount, _ => new byte[Move6Size]));
        WriteGarc(ItemGarc, Repeat(itemCount, _ => new byte[ItemSize]));
        WriteGarc(GameTextGarc + Language, BuildTextFiles(ItemNamesTable + 1,
            (SpeciesNamesTable, "Especie", speciesCount),
            (MoveNamesTable, "Movimiento", moveCount),
            (ItemNamesTable, "Objeto", itemCount)));
    }

    /// <summary>Gen VI egg moves are a ushort count followed by that many move ids.</summary>
    private static byte[] Gen6EggMoves(params ushort[] moves)
    {
        var data = new byte[2 + (moves.Length * 2)];
        BitConverter.GetBytes((ushort)moves.Length).CopyTo(data, 0);
        for (var i = 0; i < moves.Length; i++)
            BitConverter.GetBytes(moves[i]).CopyTo(data, 2 + (i * 2));
        return data;
    }
}

/// <summary>
/// A Sun/Moon workspace, which unlocks the Gen VII-only editors (trainers, static encounters).
/// <para>
/// <c>GameConfig.GetGameData</c> stats <c>encdata</c> for Gen VII and falls back to the Moon GARC
/// table when that file is empty, so encdata must exist and be non-empty for the Sun layout to be
/// selected. That is why this fixture writes a real encdata GARC even though the wild encounter
/// editor is not exercised.
/// </para>
/// </summary>
public sealed class SyntheticSunMoonWorkspace : SyntheticWorkspace
{
    private const int ArchiveFileCount = 311;

    // GARC file numbers for Sun/Moon, from GARCReference.GARCReference_SM and _SN.
    private const int MoveGarc = 11, EggMoveGarc = 12, LevelUpGarc = 13, EvolutionGarc = 14,
        MegaEvoGarc = 15, PersonalGarc = 17, ItemGarc = 19, GameTextGarc = 30,
        ZoneDataGarc = 77, EncDataGarc = 82, WorldDataGarc = 91,
        TrClassGarc = 104, TrDataGarc = 105, TrPokeGarc = 106, EncounterStaticGarc = 155;

    // Text table indexes inside the gametext GARC, from TextReference.GameText_SM.
    private const int ItemNamesTable = 36, SpeciesNamesTable = 55, MetListTable = 67,
        TrainerNamesTable = 105, TrainerClassesTable = 106, MoveNamesTable = 113;

    private const int Move7Size = 0x28;
    private const int TrainerDataSize = 0x14;
    private const int TrainerPokemonSize = 0x20;

    public int TrainerCount { get; }
    public int TrainerClassCount { get; }
    public int PokemonPerTrainer { get; }

    public int GiftCount { get; }
    public int StaticCount { get; }
    public int TradeCount { get; }

    public SyntheticSunMoonWorkspace(
        int speciesCount = 12, int moveCount = 10, int itemCount = 8,
        int trainerCount = 4, int pokemonPerTrainer = 2)
        : base(ArchiveFileCount, speciesCount, moveCount, itemCount)
    {
        TrainerCount = trainerCount;
        TrainerClassCount = 6;
        PokemonPerTrainer = pokemonPerTrainer;
        GiftCount = 3;
        StaticCount = 3;
        TradeCount = 2;

        WriteGarc(PersonalGarc, BuildPersonalFiles(PersonalInfoSM.SIZE));
        WriteGarc(LevelUpGarc, Repeat(speciesCount, _ => Learnset((1, 1))));
        WriteGarc(EvolutionGarc, Repeat(speciesCount, _ => new byte[EvolutionSet7.SIZE]));
        WriteGarc(EggMoveGarc, Repeat(speciesCount, _ => Gen7EggMoves()));
        WriteGarc(MegaEvoGarc, Repeat(speciesCount, _ => new byte[0x10]));
        // Gen VII packs every move record into a single "WD" mini archive in file 0.
        WriteGarc(MoveGarc, [Mini.PackMini(Repeat(moveCount, _ => new byte[Move7Size]), "WD")]);
        WriteGarc(ItemGarc, Repeat(itemCount, _ => new byte[ItemSize]));

        WriteGarc(GameTextGarc + Language, BuildTextFiles(MoveNamesTable + 1,
            (SpeciesNamesTable, "Especie", speciesCount),
            (MoveNamesTable, "Movimiento", moveCount),
            (ItemNamesTable, "Objeto", itemCount),
            (MetListTable, "Zona", 32),
            (TrainerNamesTable, "Entrenador", trainerCount),
            (TrainerClassesTable, "Clase", TrainerClassCount)));

        WriteGarc(EncDataGarc, BuildEncounterFiles());
        WriteGarc(ZoneDataGarc, BuildZoneFiles());
        WriteGarc(WorldDataGarc, [Mini.PackMini([BuildWorld()], "WD")]);

        WriteGarc(TrClassGarc, Repeat(TrainerClassCount, _ => new byte[4]));
        WriteGarc(TrDataGarc, Repeat(trainerCount, _ => TrainerRecord(pokemonPerTrainer)));
        WriteGarc(TrPokeGarc, Repeat(trainerCount, _ => new byte[TrainerPokemonSize * pokemonPerTrainer]));
        WriteGarc(EncounterStaticGarc, BuildStaticFiles());
    }

    /// <summary>Gen VII egg moves prefix the list with the form table index.</summary>
    private static byte[] Gen7EggMoves(params ushort[] moves)
    {
        var data = new byte[4 + (moves.Length * 2)];
        BitConverter.GetBytes((ushort)0).CopyTo(data, 0);                   // form table index
        BitConverter.GetBytes((ushort)moves.Length).CopyTo(data, 2);
        for (var i = 0; i < moves.Length; i++)
            BitConverter.GetBytes(moves[i]).CopyTo(data, 4 + (i * 2));
        return data;
    }

    /// <summary>Team size lives in byte 3 of the trainer record and drives how trpoke is sliced.</summary>
    private static byte[] TrainerRecord(int pokemonCount)
    {
        var data = new byte[TrainerDataSize];
        data[3] = (byte)pokemonCount;
        return data;
    }

    /// <summary>
    /// The static encounter GARC keeps gifts in file 0, fixed encounters in file 1 and trades in
    /// file 4; the files between them are unused by the editor but must exist.
    /// </summary>
    private byte[][] BuildStaticFiles() =>
    [
        new byte[GiftCount * EncounterGift7.SIZE],
        new byte[StaticCount * EncounterStatic7.SIZE],
        [],
        [],
        new byte[TradeCount * EncounterTrade7.SIZE],
    ];

    // Wild encounters ----------------------------------------------------------
    //
    // Layout, from Area7.GetArray:
    //   encdata holds 11 files per area, and only file 9 + 11*areaIndex carries the tables.
    //   That file is a "EA" mini archive whose entries are 4 bytes of header, a 0x164-byte day
    //   table and a 0x164-byte night table.
    //   A table is: min level, max level, ten rate bytes, then eight groups of ten uint slots at
    //   0xC, then six weather slots at 0x14C. Each slot packs species and form into one uint.

    private const int FilesPerArea = 11;
    private const int TableSize = 0x164;
    private const int SlotGroups = 8;
    private const int SlotsPerGroup = 10;
    private const int WeatherSlotOffset = 0x14C;

    public int AreaCount { get; } = 2;
    public int TablesPerArea { get; } = 2;

    /// <summary>File number inside encdata that carries the tables for the given area.</summary>
    public int AreaFileNumber(int areaIndex) => 9 + (FilesPerArea * areaIndex);

    private byte[][] BuildEncounterFiles()
    {
        var files = Repeat(AreaCount * FilesPerArea, _ => Array.Empty<byte>());
        for (var area = 0; area < AreaCount; area++)
        {
            var tables = Repeat(TablesPerArea, _ => DayNightEntry());
            files[AreaFileNumber(area)] = Mini.PackMini(tables, "EA");
        }
        return files;
    }

    /// <summary>One mini-archive entry: header, day table, night table.</summary>
    private static byte[] DayNightEntry()
    {
        var entry = new byte[4 + (TableSize * 2)];
        Table().CopyTo(entry, 4);
        Table().CopyTo(entry, 4 + TableSize);
        return entry;
    }

    private static byte[] Table()
    {
        var table = new byte[TableSize];
        table[0] = 5;   // min level
        table[1] = 15;  // max level
        for (var i = 0; i < SlotsPerGroup; i++)
            table[2 + i] = 10; // ten slots at 10% each, the only totals the editor accepts

        for (var group = 0; group < SlotGroups; group++)
        for (var slot = 0; slot < SlotsPerGroup; slot++)
            WriteSlot(table, 0xC + (group * 4 * SlotsPerGroup) + (slot * 4), species: 1, form: 0);

        for (var slot = 0; slot < 6; slot++)
            WriteSlot(table, WeatherSlotOffset + (slot * 4), species: 1, form: 0);

        return table;
    }

    private static void WriteSlot(byte[] table, int offset, uint species, uint form) =>
        BitConverter.GetBytes(species | (form << 11)).CopyTo(table, offset);

    /// <summary>File 0 is the zone table; file 1 maps each zone to its world.</summary>
    private byte[][] BuildZoneFiles()
    {
        var zones = new byte[AreaCount * ZoneData7.SIZE];
        // ParentMap at 0x1C indexes the location name list, and 0 is always present.
        var worldIndexes = new byte[AreaCount * 2];
        return [zones, worldIndexes];
    }

    /// <summary>
    /// A world is a mapping table of (zone index, area index) pairs; its start offset lives at 0x8.
    /// Zone i is mapped to area i so every area ends up with a name.
    /// </summary>
    private byte[] BuildWorld()
    {
        const int mappingOffset = 0xC;
        var world = new byte[mappingOffset + (AreaCount * 4)];
        BitConverter.GetBytes(mappingOffset).CopyTo(world, 0x8);
        for (var i = 0; i < AreaCount; i++)
        {
            BitConverter.GetBytes((ushort)i).CopyTo(world, mappingOffset + (i * 4));
            BitConverter.GetBytes((ushort)i).CopyTo(world, mappingOffset + (i * 4) + 2);
        }
        return world;
    }
}
