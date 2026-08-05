using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.Editors;

/// <summary>
/// Writes the in-memory tables of a <see cref="GameConfig"/> back into their GARCs.
/// Each table has its own packing quirk, so these live together rather than inline in the editors.
/// </summary>
internal static class GarcWriter
{
    /// <summary>
    /// Replaces one file inside a GARC.
    /// <para>
    /// Always go through this instead of <c>garc.Files[i] = data</c>. <see cref="GARCFile.Files"/>
    /// forwards to <c>MemGARC.Files</c>, whose getter <em>rebuilds the whole array and copies every
    /// entry out</em> on each read. Indexing that result assigns into a throwaway array, so the
    /// edit is silently discarded and the export ships an unmodified file. The array has to be
    /// taken once, mutated, and assigned back through the setter, which is what repacks the GARC.
    /// </para>
    /// </summary>
    public static void SetFile(this GARCFile garc, int index, byte[] data)
    {
        var files = garc.Files;
        files[index] = data;
        garc.Files = files;
    }

    /// <summary>
    /// Overwrites a slice of one file in place, for records packed several to a file.
    /// Same aliasing hazard as <see cref="SetFile"/>.
    /// </summary>
    public static void PatchFile(this GARCFile garc, int index, byte[] data, int offset)
    {
        var files = garc.Files;
        Array.Copy(data, 0, files[index], offset, data.Length);
        garc.Files = files;
    }

    public static void SavePersonal(GameConfig config)
    {
        var files = config.GARCPersonal.Files;
        // The personal GARC ends with a packed copy of every entry; that trailing file is what the game reads.
        for (var i = 0; i < files.Length - 1; i++)
            config.Personal.Table[i].Write().CopyTo(files[^1], i * files[i].Length);
        config.GARCPersonal.Files = files;
        config.GARCPersonal.Save();
    }

    public static void SaveLearnsets(GameConfig config)
    {
        var files = config.GARCLearnsets.Files;
        for (var i = 0; i < files.Length; i++)
            files[i] = config.Learnsets[i].Write();
        config.GARCLearnsets.Files = files;
        config.GARCLearnsets.Save();
    }

    public static void SaveEvolutions(GameConfig config)
    {
        var garc = config.GetGARCData("evolution");
        garc.Files = config.Evolutions.Select(evolution => evolution.Write()).ToArray();
        garc.Save();
    }

    public static void SaveMoves(GameConfig config)
    {
        var files = config.Moves.Select(move => move.Write()).ToArray();
        // Only XY stores moves as loose files; everything else packs them into a single mini archive.
        config.GARCMoves.Files = config.XY ? files : [Mini.PackMini(files, "WD")];
        config.GARCMoves.Save();
    }
}
