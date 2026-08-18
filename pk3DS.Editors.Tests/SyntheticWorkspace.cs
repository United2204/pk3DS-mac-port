using System.Runtime.InteropServices;
using System.Text;
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
    public string ExeFs => Path.Combine(Root, "ExeFS");
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

    protected byte[] BuildTitleScreenDarc(bool validBclim = false)
    {
        var root = Path.Combine(Root, $"title-screen-darc-{Guid.NewGuid():N}");
        var folder = Path.Combine(root, "group");
        Directory.CreateDirectory(folder);
        var background = validBclim
            ? BCLIMPortable.EncodeRgba(
                Enumerable.Repeat(new byte[] { 255, 0, 0, 255 }, 64).SelectMany(value => value).ToArray(),
                8,
                8)
            : [1, 2, 3, 4];
        File.WriteAllBytes(Path.Combine(folder, "background.bclim"), background);
        File.WriteAllBytes(Path.Combine(folder, "logo.bclim"), [5, 6, 7]);
        try
        {
            return DARC.SetDARC(DARC.GetDARC(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    protected byte[] CompressFixture(byte[] data)
    {
        var source = Path.Combine(Root, $"fixture-{Guid.NewGuid():N}.bin");
        var compressed = Path.Combine(Root, $"fixture-{Guid.NewGuid():N}.lz");
        File.WriteAllBytes(source, data);
        try
        {
            LZSS.Compress(source, compressed);
            return File.ReadAllBytes(compressed);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(compressed)) File.Delete(compressed);
        }
    }

    protected static byte[][] Repeat(int count, Func<int, byte[]> build) =>
        Enumerable.Range(0, count).Select(build).ToArray();

    protected void WriteCodeBin(int tableOffset, byte[] signature, int rawSlotCount)
    {
        var signatureOffset = tableOffset - signature.Length;
        var end = tableOffset + (rawSlotCount * sizeof(ushort));
        var size = (end + 0x1FF) & ~0x1FF;
        var data = new byte[size];
        signature.CopyTo(data, signatureOffset);
        for (var index = 0; index < rawSlotCount; index++)
            BitConverter.GetBytes((ushort)((index + 1) % 10)).CopyTo(data, tableOffset + (index * sizeof(ushort)));
        Directory.CreateDirectory(ExeFs);
        File.WriteAllBytes(Path.Combine(ExeFs, "code.bin"), data);
        var smdh = SMDHPortable.CreateBlank();
        smdh.AppInfo[0] = new SMDHApplicationInfo("Fixture game", "Synthetic pk3DS workspace", "pk3DS tests");
        File.WriteAllBytes(Path.Combine(ExeFs, "icon.bin"), smdh.Write());
    }

    protected void AddCodeTable(int tableOffset, byte[] signature, int rawSlotCount)
        => AddCodeTable(tableOffset, signature, rawSlotCount, -signature.Length);

    protected void AddCodeTable(int tableOffset, byte[] signature, int rawSlotCount, int signatureDelta)
    {
        var path = Path.Combine(ExeFs, "code.bin");
        var data = File.ReadAllBytes(path);
        var signatureOffset = tableOffset + signatureDelta;
        signature.CopyTo(data, signatureOffset);
        for (var index = 0; index < rawSlotCount; index++)
            BitConverter.GetBytes((ushort)((index + 1) % 10)).CopyTo(data, tableOffset + (index * sizeof(ushort)));
        File.WriteAllBytes(path, data);
    }

    protected void AddCodeBytes(int offset, byte[] bytes)
    {
        var path = Path.Combine(ExeFs, "code.bin");
        var data = File.ReadAllBytes(path);
        if (offset < 0 || offset + bytes.Length > data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        bytes.CopyTo(data, offset);
        File.WriteAllBytes(path, data);
    }

    protected void WriteLooseFile(string relativePath, byte[] data)
    {
        var path = Path.Combine(RomFs, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

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

    /// <summary>A tiny valid compressed script shared by the Gen. VI and Gen. VII OWSE fixtures.</summary>
    protected static byte[] BuildScriptFixture()
    {
        const int headerSize = 0x1C;
        var data = new byte[headerSize + 2];
        BitConverter.GetBytes(data.Length).CopyTo(data, 0x00);
        BitConverter.GetBytes(0x0A0AF1E0u).CopyTo(data, 0x04);
        BitConverter.GetBytes(headerSize).CopyTo(data, 0x0C);
        BitConverter.GetBytes(headerSize + 8).CopyTo(data, 0x10);
        BitConverter.GetBytes(headerSize + 8).CopyTo(data, 0x14);
        BitConverter.GetBytes(headerSize + 8).CopyTo(data, 0x18);
        data[headerSize] = 0x30;
        data[headerSize + 1] = 0x30;
        return data;
    }

    /// <summary>A Gen. VI entity block with one record of each known entity class.</summary>
    protected static byte[] BuildGen6EntityFixture()
    {
        var script = BuildScriptFixture();
        const int furnitureCount = 1;
        const int npcCount = 1;
        const int warpCount = 1;
        const int triggerCount = 1;
        const int unknownCount = 1;
        const int entityBytes =
            (furnitureCount * 0x14) + (npcCount * 0x30) + (warpCount * 0x18)
            + (triggerCount * 0x18) + (unknownCount * 0x18);
        var data = new byte[12 + entityBytes + script.Length];
        BitConverter.GetBytes(8 + entityBytes).CopyTo(data, 0x00); // entity header length
        data[4] = furnitureCount;
        data[5] = npcCount;
        data[6] = warpCount;
        data[7] = triggerCount;
        BitConverter.GetBytes(unknownCount).CopyTo(data, 0x08);
        script.CopyTo(data, 12 + entityBytes);
        return data;
    }

    protected static byte[] MaisonTrainer(ushort trainerClass, params ushort[] choices)
    {
        var data = new byte[4 + (choices.Length * sizeof(ushort))];
        BitConverter.GetBytes(trainerClass).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)choices.Length).CopyTo(data, 2);
        for (var i = 0; i < choices.Length; i++)
            BitConverter.GetBytes(choices[i]).CopyTo(data, 4 + (i * sizeof(ushort)));
        return data;
    }

    protected static byte[] MaisonPokemon(ushort species, ushort form, byte nature, ushort item,
        ushort[] moves, bool[] evs)
    {
        var data = new byte[0x10];
        BitConverter.GetBytes(species).CopyTo(data, 0);
        for (var i = 0; i < 4; i++)
            BitConverter.GetBytes(moves[i]).CopyTo(data, 2 + (i * sizeof(ushort)));
        for (var i = 0; i < 6; i++)
            if (evs[i]) data[0xA] |= (byte)(1 << i);
        data[0xB] = nature;
        BitConverter.GetBytes(item).CopyTo(data, 0xC);
        BitConverter.GetBytes(form).CopyTo(data, 0xE);
        return data;
    }

    /// <summary>Builds the 20-byte X/Y trainer record with item and move payloads enabled.</summary>
    protected static byte[] Trainer6(ushort trainerClass, byte battleType, byte pokemonCount,
        byte ai = 1, bool healer = false, byte money = 10, ushort prize = 1)
    {
        var data = new byte[20];
        data[0] = 3; // moves + items
        data[1] = (byte)trainerClass;
        data[2] = battleType;
        data[3] = pokemonCount;
        BitConverter.GetBytes((ushort)1).CopyTo(data, 4);
        BitConverter.GetBytes((ushort)2).CopyTo(data, 6);
        BitConverter.GetBytes((ushort)3).CopyTo(data, 8);
        BitConverter.GetBytes((ushort)4).CopyTo(data, 10);
        data[12] = ai;
        data[16] = healer ? (byte)1 : (byte)0;
        data[17] = money;
        BitConverter.GetBytes(prize).CopyTo(data, 18);
        return data;
    }

    protected static byte[] TrainerPokemon6(ushort species, ushort form, ushort level,
        byte ivs = 20, int ability = 1, int gender = 0, ushort item = 1,
        params ushort[] moves)
    {
        var data = new byte[18];
        data[0] = ivs;
        data[1] = (byte)(((ability & 0xF) << 4) | (gender & 0x7));
        BitConverter.GetBytes(level).CopyTo(data, 2);
        BitConverter.GetBytes(species).CopyTo(data, 4);
        BitConverter.GetBytes(form).CopyTo(data, 6);
        BitConverter.GetBytes(item).CopyTo(data, 8);
        for (var i = 0; i < 4; i++)
            BitConverter.GetBytes(moves.Length > i ? moves[i] : (ushort)0).CopyTo(data, 10 + (i * 2));
        return data;
    }

    protected static byte[] TrainerTeam6(params byte[][] pokemon) => pokemon.SelectMany(data => data).ToArray();

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
    private const int EncDataGarc = 12, TrDataGarc = 38, TrClassGarc = 39, TrPokeGarc = 40,
        MapGrGarc = 41, MapMatrixGarc = 42, MoveGarc = 212, EggMoveGarc = 213, LevelUpGarc = 214,
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
        var gameText = BuildTextFiles(142,
            (SpeciesNamesTable, "Especie", speciesCount),
            (MoveNamesTable, "Movimiento", moveCount),
            (ItemNamesTable, "Objeto", itemCount),
            (17, "Tipo", 18),
            (20, "Clase", 6),
            (21, "Entrenador", 3),
            (47, "Naturaleza", 25),
            (72, "Zona", 32));
        WriteGarc(GameTextGarc + Language, gameText);
        WriteGarc(GameTextGarc + 2, gameText);
        WriteGarc(EncDataGarc, BuildOverworldFiles());
        WriteGarc(MapGrGarc, Enumerable.Range(0, 12).Select(index => BuildGen6MapGrid(index)).ToArray());
        WriteGarc(MapMatrixGarc, Enumerable.Range(0, 22).Select(index => BuildGen6MapMatrix(index)).ToArray());
        WriteGarc(TrClassGarc, Repeat(6, _ => new byte[4]));
        WriteGarc(TrDataGarc,
        [
            Trainer6(0, 0, 2),
            Trainer6(1, 1, 2, healer: true),
            Trainer6(2, 2, 1),
        ]);
        WriteGarc(TrPokeGarc,
        [
            TrainerTeam6(TrainerPokemon6(1, 0, 25, moves: [1, 2, 3, 4]), TrainerPokemon6(2, 1, 30, ivs: 31, gender: 1, moves: [2, 3, 4, 5])),
            TrainerTeam6(TrainerPokemon6(3, 0, 35, ability: 2, item: 2, moves: [3, 4, 5, 6]), TrainerPokemon6(4, 0, 40, ability: 3, gender: 2, item: 3, moves: [4, 5, 6, 7])),
            TrainerTeam6(TrainerPokemon6(5, 0, 45, ability: 4, gender: 3, item: 4, moves: [5, 6, 7, 8])),
        ]);
        WriteGarc(203, [MaisonPokemon(1, 0, 2, 1, [1, 2, 3, 4], [true, false, false, true, false, false]), MaisonPokemon(2, 1, 3, 2, [2, 3, 4, 5], [false, true, false, false, true, false])]);
        WriteGarc(204, [MaisonTrainer(1, 0, 1), MaisonTrainer(2, 1)]);
        WriteGarc(205, [MaisonPokemon(3, 0, 4, 1, [1, 2, 3, 4], [false, false, true, false, false, true]), MaisonPokemon(4, 0, 5, 2, [2, 3, 4, 5], [false, false, false, true, true, false])]);
        WriteGarc(206, [MaisonTrainer(1, 1), MaisonTrainer(2, 0)]);
        WriteCodeBin(0x00464796, [0xD4, 0x00, 0xAE, 0x02, 0xAF, 0x02, 0xB0, 0x02], 105);
        AddCodeTable(0x004455A8, [0x1E, 0x28, 0x32, 0x3C, 0x46, 0x50, 0x5A, 0x5E, 0x62, 0x05, 0x0A, 0x0F, 0x14, 0x19, 0x1E, 0x23, 0x28, 0x2D, 0x32], 29, 0x3A);
        var typeChart = new byte[0x000D12A8 + (18 * 18)];
        for (var index = 0; index < 18 * 18; index++)
            typeChart[0x000D12A8 + index] = new byte[] { 0, 2, 4, 8 }[index % 4];
        WriteLooseFile("DllBattle.cro", typeChart);
        WriteStarterCros();
        AddCodeBytes(0x00001000,
            [0x23, 0x00, 0xD4, 0xE5, 0x01, 0x50, 0x85, 0xE2, 0x05, 0x00, 0x50, 0xE1, 0xDE, 0xFF, 0xFF, 0xCA]);
        AddCodeBytes(0x00002000,
            [0x00, 0x20, 0x22, 0xE0, 0x02, 0x30, 0x21, 0xE2, 0x03, 0x20, 0x92, 0xE1, 0x1C, 0x00, 0x00, 0x0A]);
        var opower = new byte[9 + (65 * 22)];
        Encoding.ASCII.GetBytes("49461845\0").CopyTo(opower, 0);
        for (var index = 0; index < 65; index++)
        {
            var at = 9 + (index * 22);
            opower[at + 1] = 2;
            opower[at + 3] = (byte)(10 + index);
            opower[at + 4] = (byte)(20 + index);
            opower[at + 0xE] = 1;
            opower[at + 0xF] = 2;
            BitConverter.GetBytes((ushort)(100 + index)).CopyTo(opower, at + 0x12);
            opower[at + 0x14] = 30;
        }
        AddCodeBytes(0x00400000, opower);
        WriteGen6CodeBinTables();
    }

    /// <summary>
    /// Minimal X/Y encdata for the read-only OWSE fixture: file 0 is the master zone table and
    /// each following file is a ZO mini-archive with an overworld and map script.
    /// </summary>
    private static byte[][] BuildOverworldFiles()
    {
        const int zoneCount = 2;
        const int zoneDataSize = 0x38;
        var master = new byte[zoneCount * zoneDataSize];
        var files = new byte[zoneCount + 1][];
        files[0] = master;

        for (var zone = 0; zone < zoneCount; zone++)
        {
            BitConverter.GetBytes((ushort)zone).CopyTo(master, zone * zoneDataSize + 0x1C);
            var zoneData = new byte[zoneDataSize];
            zoneData[0] = 2; // map type
            zoneData[1] = 1; // map movement
            BitConverter.GetBytes((ushort)(10 + zone)).CopyTo(zoneData, 0x02);
            BitConverter.GetBytes((ushort)(20 + zone)).CopyTo(zoneData, 0x04);
            BitConverter.GetBytes((ushort)(30 + zone)).CopyTo(zoneData, 0x06);
            BitConverter.GetBytes((ushort)(40 + zone)).CopyTo(zoneData, 0x18);
            BitConverter.GetBytes((ushort)zone).CopyTo(zoneData, 0x1C);
            BitConverter.GetBytes((ushort)3).CopyTo(zoneData, 0x1E); // weather
            var encounters = new byte[0x10];
            files[zone + 1] = Mini.PackMini(
                [zoneData, BuildGen6EntityFixture(), BuildScriptFixture(), encounters], "ZO");
        }

        return files;
    }

    /// <summary>Replaces the OWSE-only encdata with two real X/Y wild encounter tables.</summary>
    public void WriteWildFixture()
    {
        var master = new byte[0x70];
        var files = new[] { master, BuildGen6EncounterFile(1), BuildGen6EncounterFile(2) };
        WriteGarc(EncDataGarc, files);
    }

    private static byte[] BuildGen6EncounterFile(int seed)
    {
        const int dataOffset = 0x20;
        var data = new byte[dataOffset + (WildGen6Editor.GetSlotCount(oras: false) * 4)];
        BitConverter.GetBytes(0x10).CopyTo(data, 0x10);
        for (var index = 0; index < WildGen6Editor.GetSlotCount(oras: false); index++)
        {
            var at = dataOffset + (index * 4);
            BitConverter.GetBytes((ushort)(((index + seed) % 8) + 1)).CopyTo(data, at);
            data[at + 2] = (byte)(5 + (index % 3));
            data[at + 3] = (byte)(10 + (index % 5));
        }
        return data;
    }

    private static byte[] BuildGen6MapGrid(int seed)
    {
        const int width = 4;
        const int height = 3;
        var data = new byte[0x88 + (width * height * sizeof(uint))];
        Encoding.ASCII.GetBytes("GR").CopyTo(data, 0);
        BitConverter.GetBytes((ushort)6).CopyTo(data, 2);
        data[4] = 0x80;
        BitConverter.GetBytes((ushort)width).CopyTo(data, 0x80);
        BitConverter.GetBytes((ushort)height).CopyTo(data, 0x82);
        for (var index = 0; index < width * height; index++)
            BitConverter.GetBytes((uint)(0x1000021 + seed + index)).CopyTo(data, 0x88 + (index * sizeof(uint)));
        return data;
    }

    private static byte[] BuildGen6MapMatrix(int seed)
    {
        const int width = 2;
        const int height = 1;
        var data = new byte[0x18 + (width * height * sizeof(ushort))];
        Encoding.ASCII.GetBytes("MM").CopyTo(data, 0);
        BitConverter.GetBytes((ushort)2).CopyTo(data, 2);
        BitConverter.GetBytes(0x10).CopyTo(data, 4);
        BitConverter.GetBytes(data.Length).CopyTo(data, 0x0C);
        BitConverter.GetBytes((ushort)width).CopyTo(data, 0x14);
        BitConverter.GetBytes((ushort)height).CopyTo(data, 0x16);
        BitConverter.GetBytes((ushort)(100 + seed)).CopyTo(data, 0x18);
        BitConverter.GetBytes((ushort)(200 + seed)).CopyTo(data, 0x1A);
        return data;
    }

    /// <summary>
    /// Adds the Gen. VI title-screen GARC used by the headless title-screen inventory tests.
    /// The fixture only populates X-DE (file 467); the other expected archives stay empty so the
    /// catalog can also prove that missing/invalid archives are reported without aborting the scan.
    /// </summary>
    public void WriteTitleScreenFixture()
    {
        var files = Enumerable.Repeat(Array.Empty<byte>(), 481).ToArray();
        files[467] = BuildTitleScreenDarc();
        WriteGarc(165, files);
    }

    /// <summary>Writes the same fixture with a decodable 8×8 BCLIM for replacement tests.</summary>
    public void WritePortableTitleScreenFixture()
    {
        var files = Enumerable.Repeat(Array.Empty<byte>(), 481).ToArray();
        files[467] = BuildTitleScreenDarc(validBclim: true);
        WriteGarc(165, files);
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

    private void WriteStarterCros()
    {
        const int pokeHeaderOffset = 0xB8;
        const int pokeDataOffset = 0x110;
        const int pokeRecordSize = 0x54;
        var poke = new byte[pokeDataOffset + (6 * pokeRecordSize)];
        BitConverter.GetBytes(0x100).CopyTo(poke, pokeHeaderOffset);
        for (var index = 0; index < 6; index++)
            BitConverter.GetBytes((ushort)(index + 1)).CopyTo(poke, pokeDataOffset + (index * pokeRecordSize));
        WriteLooseFile("DllPoke3Select.cro", poke);

        const int fieldOffset = 0xF805C;
        const int fieldSize = 0x18;
        const int giftCount = 0x13;
        var field = new byte[fieldOffset + (giftCount * fieldSize)];
        for (var index = 0; index < giftCount; index++)
            BitConverter.GetBytes((ushort)(index + 1)).CopyTo(field, fieldOffset + (index * fieldSize));
        WriteLooseFile("DllField.cro", field);
    }

    private void WriteGen6CodeBinTables()
    {
        byte[] tutorSignature =
        [
            0x00, 0x46, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x54, 0x79, 0x70, 0x65, 0x00, 0x00, 0x45, 0x64, 0x67,
            0x65, 0x49, 0x44, 0x00, 0xFF,
        ];
        const int tutorSignatureOffset = 0x00460000;
        AddCodeBytes(tutorSignatureOffset, tutorSignature);
        var code = File.ReadAllBytes(Path.Combine(ExeFs, "code.bin"));
        var tutorOffset = tutorSignatureOffset + tutorSignature.Length;
        foreach (var count in new[] { 0xF, 0x11, 0x10, 0xF })
        {
            for (var index = 0; index < count; index++)
            {
                BitConverter.GetBytes((ushort)((index + 1) % MoveCount)).CopyTo(code, tutorOffset);
                tutorOffset += sizeof(ushort);
            }
            BitConverter.GetBytes(ushort.MaxValue).CopyTo(code, tutorOffset);
            tutorOffset += sizeof(ushort);
        }
        File.WriteAllBytes(Path.Combine(ExeFs, "code.bin"), code);

        byte[] martSignature =
        [
            0x00, 0x72, 0x6F, 0x6D, 0x3A, 0x2F, 0x44, 0x6C, 0x6C, 0x53, 0x74, 0x61, 0x72, 0x74, 0x4D, 0x65,
            0x6E, 0x75, 0x2E, 0x63, 0x72, 0x6F, 0x00,
        ];
        const int martSignatureOffset = 0x00461000;
        AddCodeBytes(martSignatureOffset, martSignature);
        code = File.ReadAllBytes(Path.Combine(ExeFs, "code.bin"));
        var martOffset = martSignatureOffset + martSignature.Length;
        foreach (var count in new[] { 2, 11, 14, 17, 18, 19, 19, 19, 19, 1, 4, 10, 3, 9, 1, 1, 3, 3, 5, 5, 6, 7, 5, 5, 8, 3 })
        for (var index = 0; index < count; index++)
        {
            BitConverter.GetBytes((ushort)((index + 1) % ItemCount)).CopyTo(code, martOffset);
            martOffset += sizeof(ushort);
        }
        File.WriteAllBytes(Path.Combine(ExeFs, "code.bin"), code);
    }
}

/// <summary>A minimal OR/AS workspace used to verify the compressed title-screen archives.</summary>
public sealed class SyntheticOrasWorkspace : SyntheticWorkspace
{
    private const int ArchiveFileCount = 299;

    public SyntheticOrasWorkspace()
        : base(ArchiveFileCount, speciesCount: 1, moveCount: 1, itemCount: 1)
    {
        // EditorSession initializes the shared Gen. VI tables before opening OWSE.
        WriteGarc(195, BuildPersonalFiles(PersonalInfoORAS.SIZE));
        WriteGarc(191, [Learnset((1, 1))]);
        WriteGarc(192, [new byte[EvolutionSet6.SIZE]]);
        WriteGarc(189, [Mini.PackMini([new byte[0x22]], "WD")]);
        var gameText = BuildTextFiles(142, (90, "Zona", 4));
        WriteGarc(72, gameText);
    }

    public void WriteOverworldFixture()
    {
        const int zoneDataSize = 0x38;
        var master = new byte[zoneDataSize];
        BitConverter.GetBytes((ushort)0).CopyTo(master, 0x1C);
        var zoneData = new byte[zoneDataSize];
        zoneData[0] = 2;
        zoneData[1] = 1;
        BitConverter.GetBytes((ushort)12).CopyTo(zoneData, 0x02);
        BitConverter.GetBytes((ushort)22).CopyTo(zoneData, 0x04);
        BitConverter.GetBytes((ushort)32).CopyTo(zoneData, 0x06);
        BitConverter.GetBytes((ushort)42).CopyTo(zoneData, 0x18);
        BitConverter.GetBytes((ushort)3).CopyTo(zoneData, 0x1E);
        var zone = Mini.PackMini(
            [master, BuildGen6EntityFixture(), BuildScriptFixture(), new byte[0x10]], "ZO");

        // OR/AS reserves encdata file 1 and starts zones at file 2.
        WriteGarc(13, [master, [], zone]);
    }

    public void WriteTitleScreenFixture()
    {
        var files = Enumerable.Repeat(Array.Empty<byte>(), 1136).ToArray();
        files[1120] = CompressFixture(BuildTitleScreenDarc());
        WriteGarc(152, files);
    }

    public void WritePortableTitleScreenFixture()
    {
        var files = Enumerable.Repeat(Array.Empty<byte>(), 1136).ToArray();
        files[1120] = CompressFixture(BuildTitleScreenDarc(validBclim: true));
        WriteGarc(152, files);
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
        TrClassGarc = 104, TrDataGarc = 105, TrPokeGarc = 106, EncounterStaticGarc = 155, PickupGarc = 267,
        MaisonPkNormalGarc = 277, MaisonTrNormalGarc = 278, MaisonPkSpecialGarc = 279, MaisonTrSpecialGarc = 280;

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

        var gameText = BuildTextFiles(MoveNamesTable + 1,
            (SpeciesNamesTable, "Especie", speciesCount),
            (MoveNamesTable, "Movimiento", moveCount),
            (ItemNamesTable, "Objeto", itemCount),
            (107, "Tipo", 18),
            (MetListTable, "Zona", 32),
            (TrainerNamesTable, "Entrenador", trainerCount),
            (TrainerClassesTable, "Clase", TrainerClassCount));
        WriteGarc(GameTextGarc + Language, gameText);
        WriteGarc(GameTextGarc + 2, gameText);

        WriteGarc(EncDataGarc, BuildEncounterFiles());
        WriteGarc(ZoneDataGarc, BuildZoneFiles());
        WriteGarc(WorldDataGarc, [Mini.PackMini([BuildWorld()], "WD")]);

        WriteGarc(TrClassGarc, Repeat(TrainerClassCount, _ => new byte[4]));
        WriteGarc(TrDataGarc, Repeat(trainerCount, _ => TrainerRecord(pokemonPerTrainer)));
        WriteGarc(TrPokeGarc, Enumerable.Range(0, trainerCount).Select(index => TrainerTeam7(index, pokemonPerTrainer)).ToArray());
        WriteGarc(EncounterStaticGarc, BuildStaticFiles());
        WriteMaison(MaisonPkNormalGarc, MaisonTrNormalGarc, [0, 1]);
        WriteMaison(MaisonPkSpecialGarc, MaisonTrSpecialGarc, [1, 0]);
        // Real Gen VII dumps commonly LZSS-compress this entry. Keeping the fixture compressed
        // verifies that the editor transparently decompresses it and recompresses it on export.
        WriteGarc(PickupGarc, [Compress(PickupTable())]);
        WriteCodeBin(0x0059795A, [0x03, 0x40, 0x03, 0x41, 0x03, 0x42, 0x03, 0x43, 0x03], 100);
        byte[] typeSignature =
        [
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
            0xC3, 0x00, 0x00, 0x00, 0xCB, 0x00, 0x00, 0x00, 0xD3, 0x00, 0x00, 0x00, 0xDB, 0x00, 0x00, 0x00,
            0xF3, 0x00, 0x00, 0x00, 0xFB, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
        ];
        var typeData = new byte[typeSignature.Length + (18 * 18)];
        typeSignature.CopyTo(typeData, 0);
        for (var index = 0; index < 18 * 18; index++)
            typeData[typeSignature.Length + index] = new byte[] { 0, 2, 4, 8 }[(index + 1) % 4];
        AddCodeBytes(0x00410000, typeData);
        WriteShopCro();
    }

    /// <summary>Gen VII Shop.cro with regular marts, BP inventories and tutor lists.</summary>
    private void WriteShopCro()
    {
        const int lengthOffset = 0x52D2;
        const int dataOffset = 0x54DE;
        var lengths = new byte[] { 4, 5, 6, 7 };
        var regularLengths = new[] { 9, 11, 13, 15, 17, 19, 20, 21, 9, 4, 8, 12, 5, 4, 11, 3, 10, 6, 10, 6, 4, 5, 7 };
        var battlePointLengths = new[] { 8, 7, 18, 12, 21, 16 };
        var data = new byte[0x5800];
        lengths.CopyTo(data, lengthOffset);

        byte[] regularSignature =
        [
            0x2D, 0x00, 0x00, 0x00, 0x3B, 0x00, 0x00, 0x00, 0x2F, 0x00, 0x00, 0x00, 0x3D, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00,
            0x10, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
        ];
        byte[] battlePointSignature =
        [
            0x09, 0x0B, 0x0D, 0x0F, 0x11, 0x13, 0x14, 0x15, 0x09, 0x04, 0x08, 0x0C, 0x05, 0x04, 0x0B, 0x03,
            0x0A, 0x06, 0x0A, 0x06, 0x04, 0x05, 0x07, 0x01,
        ];
        regularSignature.CopyTo(data, 0x5000);
        battlePointSignature.CopyTo(data, 0x5600);

        var itemRange = Math.Max(1, ItemCount - 1);
        var regularOffset = 0x5000 + regularSignature.Length;
        var regularIndex = 0;
        foreach (var length in regularLengths)
        for (var index = 0; index < length; index++)
        {
            BitConverter.GetBytes((ushort)((regularIndex++ % itemRange) + 1)).CopyTo(data, regularOffset);
            regularOffset += 2;
        }

        var battlePointOffset = 0x5600 + battlePointSignature.Length;
        var battlePointIndex = 0;
        foreach (var length in battlePointLengths)
        for (var index = 0; index < length; index++)
        {
            BitConverter.GetBytes((ushort)((battlePointIndex++ % itemRange) + 1)).CopyTo(data, battlePointOffset);
            BitConverter.GetBytes((ushort)(1000 + battlePointIndex)).CopyTo(data, battlePointOffset + 2);
            battlePointOffset += 4;
        }

        var offset = dataOffset;
        var move = 1;
        foreach (var length in lengths)
        for (var index = 0; index < length; index++)
        {
            BitConverter.GetBytes((ushort)move).CopyTo(data, offset);
            BitConverter.GetBytes((ushort)(100 + move)).CopyTo(data, offset + 2);
            offset += 4;
            move = (move % MoveCount) + 1;
        }

        Directory.CreateDirectory(RomFs);
        File.WriteAllBytes(Path.Combine(RomFs, "Shop.cro"), data);
    }

    /// <summary>Two items whose ten level-band rates each sum to 100.</summary>
    private static byte[] PickupTable() =>
    [
        3, 0, // two rows plus the format's one-based header
        0, 0, // reserved/padding
        1, 0, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50,
        2, 0, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50,
    ];

    private void WriteMaison(int pokemonGarc, int trainerGarc, ushort[] choices)
    {
        WriteGarc(pokemonGarc, [MaisonPokemon(1, 0, 2, 1, [1, 2, 3, 4], [true, false, false, true, false, false]), MaisonPokemon(2, 1, 3, 2, [2, 3, 4, 5], [false, true, false, false, true, false])]);
        WriteGarc(trainerGarc, [MaisonTrainer(1, choices), MaisonTrainer(2, choices.Reverse().ToArray())]);
    }

    private byte[] Compress(byte[] data)
    {
        var source = Path.Combine(Root, "pickup.bin");
        var compressed = Path.Combine(Root, "pickup.lz");
        File.WriteAllBytes(source, data);
        LZSS.Compress(source, compressed);
        var result = File.ReadAllBytes(compressed);
        File.Delete(source);
        File.Delete(compressed);
        return result;
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

    private byte[] TrainerTeam7(int trainerIndex, int pokemonCount)
    {
        var data = new byte[TrainerPokemonSize * pokemonCount];
        for (var index = 0; index < pokemonCount; index++)
        {
            var offset = index * TrainerPokemonSize;
            data[offset + 0xE] = (byte)(20 + index);
            BitConverter.GetBytes((ushort)(((trainerIndex + index) % (SpeciesCount - 1)) + 1)).CopyTo(data, offset + 0x10);
            BitConverter.GetBytes((ushort)(((index + 1) % Math.Max(1, MoveCount)))).CopyTo(data, offset + 0x18);
        }
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
            files[area * FilesPerArea] = Mini.PackMini(
                [
                    Mini.PackMini([Gen7PositionEntry(1, 10), Gen7PositionEntry(2, 20)], "EP"),
                    Mini.PackMini([Gen7ModelEntry(1, 30)], "EM"),
                    Mini.PackMini([Gen7EbEntry(1, 50)], "EB"),
                    Mini.PackMini([Gen7EsEntry(1, 70)], "ES"),
                    Mini.PackMini([Gen7EaEntry(1, 90)], "EA"),
                    Mini.PackMini([Gen7EaKind6Entry(1, 95)], "EA"),
                    Mini.PackMini([Gen7EtEntry(1, 110)], "ET"),
                ], "ED");
            files[(area * FilesPerArea) + 7] = Mini.PackMini([BuildScriptFixture()], "ZS");
            files[(area * FilesPerArea) + 8] = Mini.PackMini([BuildScriptFixture()], "ZI");
        }
        return files;
    }

    private static byte[] Gen7PositionEntry(int count, float baseX)
    {
        var entry = new byte[8 + (count * 0x3C)];
        BitConverter.GetBytes(count).CopyTo(entry, 0);
        for (var index = 0; index < count; index++)
        {
            var offset = 8 + (index * 0x3C);
            BitConverter.GetBytes(baseX + index).CopyTo(entry, offset);
            BitConverter.GetBytes(100f + index).CopyTo(entry, offset + 4);
            BitConverter.GetBytes(200f + index).CopyTo(entry, offset + 8);
        }
        return entry;
    }

    private static byte[] Gen7ModelEntry(int count, float baseX)
    {
        const int recordSize = 0x78;
        var entry = new byte[8 + (count * recordSize)];
        BitConverter.GetBytes(count).CopyTo(entry, 0);
        BitConverter.GetBytes(1).CopyTo(entry, 4); // stable primary EM record kind
        for (var index = 0; index < count; index++)
        {
            var offset = 8 + (index * recordSize);
            BitConverter.GetBytes(baseX + index).CopyTo(entry, offset);
            BitConverter.GetBytes(300f + index).CopyTo(entry, offset + 4);
            BitConverter.GetBytes(400f + index).CopyTo(entry, offset + 8);
        }
        return entry;
    }

    private static byte[] Gen7EbEntry(int count, float baseX)
    {
        const int recordSize = 0x3C;
        var entry = new byte[8 + (count * recordSize) + 0x1C];
        BitConverter.GetBytes(count).CopyTo(entry, 0);
        BitConverter.GetBytes(2).CopyTo(entry, 4); // stable primary EB record kind
        for (var index = 0; index < count; index++)
        {
            var offset = 8 + (index * recordSize);
            BitConverter.GetBytes(baseX + index).CopyTo(entry, offset);
            BitConverter.GetBytes(500f + index).CopyTo(entry, offset + 4);
            BitConverter.GetBytes(600f + index).CopyTo(entry, offset + 8);
        }
        return entry;
    }

    private static byte[] Gen7EsEntry(int count, float baseX)
    {
        const int recordSize = 0x38;
        var entry = new byte[8 + (count * recordSize) + 0x14];
        BitConverter.GetBytes(count).CopyTo(entry, 0);
        BitConverter.GetBytes(4).CopyTo(entry, 4); // stable ES record kind
        for (var index = 0; index < count; index++)
        {
            var offset = 8 + (index * recordSize);
            BitConverter.GetBytes(baseX + index).CopyTo(entry, offset);
            BitConverter.GetBytes(700f + index).CopyTo(entry, offset + 4);
            BitConverter.GetBytes(800f + index).CopyTo(entry, offset + 8);
        }
        return entry;
    }

    private static byte[] Gen7EaEntry(int count, float baseX)
    {
        const int recordSize = 0x3C;
        var entry = new byte[8 + (count * recordSize) + 0x20];
        BitConverter.GetBytes(count).CopyTo(entry, 0);
        BitConverter.GetBytes(5).CopyTo(entry, 4); // stable EA record kind
        for (var index = 0; index < count; index++)
        {
            var offset = 8 + (index * recordSize);
            BitConverter.GetBytes(baseX + index).CopyTo(entry, offset);
            BitConverter.GetBytes(900f + index).CopyTo(entry, offset + 4);
            BitConverter.GetBytes(1000f + index).CopyTo(entry, offset + 8);
        }
        return entry;
    }

    private static byte[] Gen7EaKind6Entry(int count, float baseX)
    {
        const int descriptorSize = 0x1C;
        const int payloadSize = 0x30;
        var payloadStart = 0x20 + ((count - 1) * descriptorSize);
        var entry = new byte[payloadStart + (count * payloadSize) + 0x08];
        BitConverter.GetBytes(count).CopyTo(entry, 0);
        BitConverter.GetBytes(6).CopyTo(entry, 4); // confirmed EA type-6 position record
        for (var index = 0; index < count; index++)
        {
            var descriptorOffset = index == 0
                ? 0x08
                : 0x20 + ((index - 1) * descriptorSize);
            var pointerOffset = descriptorOffset + (index == 0 ? 0x14 : 0x18);
            var payloadOffset = payloadStart + (index * payloadSize);
            BitConverter.GetBytes(payloadOffset).CopyTo(entry, pointerOffset);
            BitConverter.GetBytes(1u).CopyTo(entry, payloadOffset);
            BitConverter.GetBytes(1u).CopyTo(entry, payloadOffset + 4);
            BitConverter.GetBytes(baseX + index).CopyTo(entry, payloadOffset + 8);
            BitConverter.GetBytes(950f + index).CopyTo(entry, payloadOffset + 12);
            BitConverter.GetBytes(1050f + index).CopyTo(entry, payloadOffset + 16);
        }
        return entry;
    }

    private static byte[] Gen7EtEntry(int count, float baseX)
    {
        const int recordSize = 0x54;
        var entry = new byte[8 + (count * recordSize) + 0x18];
        BitConverter.GetBytes(count).CopyTo(entry, 0);
        BitConverter.GetBytes(7).CopyTo(entry, 4); // stable ET record kind
        for (var index = 0; index < count; index++)
        {
            var offset = 8 + (index * recordSize);
            BitConverter.GetBytes(baseX + index).CopyTo(entry, offset);
            BitConverter.GetBytes(1100f + index).CopyTo(entry, offset + 4);
            BitConverter.GetBytes(1200f + index).CopyTo(entry, offset + 8);
        }
        return entry;
    }

    private static byte[] Gen7EtKind9Entry()
    {
        const int descriptorCount = 2;
        const int firstDescriptorSize = 0x14;
        const int descriptorSize = 0x18;
        const int pointHeaderSize = 0x08;
        const int pointSize = 0x0C;
        var tableEnd = 8 + firstDescriptorSize + ((descriptorCount - 1) * descriptorSize);
        var firstDataOffset = tableEnd;
        var firstPointCount = 2;
        var secondDataOffset = firstDataOffset + pointHeaderSize + (firstPointCount * pointSize);
        var secondPointCount = 3;
        var secondTailOffset = secondDataOffset + pointHeaderSize + (secondPointCount * pointSize);
        var entry = new byte[secondTailOffset + 0x20];
        BitConverter.GetBytes(descriptorCount).CopyTo(entry, 0);
        BitConverter.GetBytes(9).CopyTo(entry, 4);

        BitConverter.GetBytes(firstDataOffset).CopyTo(entry, 8);
        BitConverter.GetBytes(1f).CopyTo(entry, 12);
        BitConverter.GetBytes(secondDataOffset).CopyTo(entry, 16);

        BitConverter.GetBytes(9).CopyTo(entry, 0x1C);
        BitConverter.GetBytes(secondDataOffset).CopyTo(entry, 0x20);
        BitConverter.GetBytes(2f).CopyTo(entry, 0x24);
        BitConverter.GetBytes(secondTailOffset).CopyTo(entry, 0x28);

        BitConverter.GetBytes(firstPointCount | 0x10000).CopyTo(entry, firstDataOffset);
        WriteEtKind9Points(entry, firstDataOffset + pointHeaderSize, [70f, 71f]);
        BitConverter.GetBytes(secondPointCount).CopyTo(entry, secondDataOffset);
        WriteEtKind9Points(entry, secondDataOffset + pointHeaderSize, [80f, 81f, 82f]);

        for (var index = secondTailOffset; index < entry.Length; index++)
            entry[index] = (byte)(0xD0 + (index - secondTailOffset));
        return entry;
    }

    private static void WriteEtKind9Points(byte[] entry, int offset, float[] baseXs)
    {
        for (var index = 0; index < baseXs.Length; index++)
        {
            BitConverter.GetBytes(baseXs[index]).CopyTo(entry, offset + (index * 0x0C));
            BitConverter.GetBytes(700f + index).CopyTo(entry, offset + (index * 0x0C) + 4);
            BitConverter.GetBytes(800f + index).CopyTo(entry, offset + (index * 0x0C) + 8);
        }
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
