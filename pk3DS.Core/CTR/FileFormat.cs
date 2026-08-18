using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace pk3DS.Core.CTR;

public static partial class FileFormat
{
    internal const string defaultExtension = "bin";
    internal static readonly string[] validEXT = ["BCH"];

    public static string Guess(string path)
    {
        using var br = new BinaryReader(File.OpenRead(path));
        return Guess(br);
    }

    public static string Guess(byte[] data)
    {
        using var br = new BinaryReader(new MemoryStream(data));
        return Guess(br);
    }

    public static string Guess(MemoryStream ms, bool start = true)
    {
        using var br = new BinaryReader(ms);
        return Guess(br, start);
    }

    public static string Guess(BinaryReader br, bool start = true)
    {
        long position = br.BaseStream.Position; // Store current position to reset after.

        if (start) // Seek to top of stream if requested
            br.BaseStream.Position = 0;

        // Guess Extension
        if (GuessMini(br, out var ext))
            Console.WriteLine("Mini Packed File detected, extension type " + ext);
        else if (GuessHeaderedDARC(br, out ext))
            Console.WriteLine("Headered DARC File detected, extension type " + ext);
        else if (GuessBCLIM(br, out ext))
            Console.WriteLine("BCLIM File detected, extension type " + ext);
        else if (GuessBFLIM(br, out ext))
            Console.WriteLine("BFLIM File detected, extension type " + ext);
        else if (GuessALYT(br, out ext))
            Console.WriteLine("ALYT File detected, extension type " + ext);
        else if (GuessShuffle(br, out ext))
            Console.WriteLine("Shuffle ARC detected, extension type " + ext);
        else if (GuessSARC(br, out ext))
            Console.WriteLine("SARC File detected, extension type " + ext);
        else if (GuessFARC(br, out ext))
            Console.WriteLine("FARC File detected, extension type " + ext);
        else if (GuessGar(br, out ext))
            Console.WriteLine("GAR detected, extension type " + ext);
        else if (GuessLZ11(br, out ext))
            Console.WriteLine("LZ11 Compressed File detected, extension type " + ext);
        else if (Guess4CHAR(br, out ext))
            Console.WriteLine("4CHAR File detected, extension type " + ext);
        else if (Guess3CHAR(br, out ext))
            Console.WriteLine("3CHAR File detected, extension type " + ext);
        else ext = defaultExtension; // default

        // Return BaseStream position to the start.
        br.BaseStream.Position = position;
        return "." + ext;
    }

    public static bool GuessMini(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position; // Store current position to reset after.
        ext = ""; // Reset extension
        try
        {
            // check for 2char container extensions
            ushort magic = br.ReadUInt16();
            ushort count = br.ReadUInt16();
            br.BaseStream.Position = 4 + (4 * count);
            if (br.ReadUInt32() == br.BaseStream.Length)
            {
                ext += (char)magic & 0xFF;
                ext += (char)magic << 8;
            }
        }
        catch { }
        // Return BaseStream position to the start.
        br.BaseStream.Position = position;

        return ext.Length > 0;
    }

    public static bool GuessHeaderedDARC(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position; // Store current position to reset after.
        ext = ""; // Reset extension
        try
        {
            byte[] magic = Encoding.ASCII.GetBytes(br.ReadChars(4));
            int count = BitConverter.ToUInt16(magic, 0);
            br.BaseStream.Position = position + 4 + (0x40 * count);
            uint tableval = br.ReadUInt32();
            br.BaseStream.Position += 0x20 * tableval;
            while (br.PeekChar() == 0) // seek forward
                br.ReadByte();
            if (br.ReadUInt32() == 0x63726164)
                ext = "darc";
        }
        catch { }
        // Return BaseStream position to the start.
        br.BaseStream.Position = position;

        return ext.Length > 0;
    }

    public static bool GuessBCLIM(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position; // Store current position to reset after.
        ext = ""; // Reset extension
        try
        {
            br.BaseStream.Position = br.BaseStream.Length - 0x28;
            if (br.ReadUInt32() == 0x4D494C43)
            {
                br.BaseStream.Position = br.BaseStream.Length - 0x4;
                if (br.ReadUInt32() == br.BaseStream.Length - 0x28)
                    ext = "bclim";
            }
        }
        catch { }
        // Return BaseStream position to the start.
        br.BaseStream.Position = position;

        return ext.Length > 0;
    }

    public static bool GuessBFLIM(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position;
        ext = "";
        try
        {
            if (br.BaseStream.Length >= FLIMHeader.SIZE)
            {
                br.BaseStream.Position = br.BaseStream.Length - FLIMHeader.SIZE;
                if (br.ReadUInt32() == 0x4D494C46)
                {
                    br.BaseStream.Position = br.BaseStream.Length - 4;
                    if (br.ReadUInt32() == br.BaseStream.Length - FLIMHeader.SIZE)
                        ext = "bflim";
                }
            }
        }
        catch { }
        br.BaseStream.Position = position;
        return ext.Length > 0;
    }

    public static bool GuessALYT(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position;
        ext = "";
        try
        {
            if (br.BaseStream.Length >= 4 && new string(br.ReadChars(4)) == "ALYT")
                ext = "alyt";
        }
        catch { }
        br.BaseStream.Position = position;
        return ext.Length > 0;
    }

    public static bool GuessShuffle(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position;
        ext = "";
        try
        {
            if (br.BaseStream.Length - position >= 4)
            {
                var header = br.ReadBytes(0x104);
                if (ShuffleArcPortable.HasHeader(header))
                    ext = "sharc";
            }
        }
        catch { }
        br.BaseStream.Position = position;
        return ext.Length > 0;
    }

    /// <summary>
    /// Recognizes a complete SARC instead of classifying it as an arbitrary four-character
    /// file. The shared detector is also used by the Windows-style batch extension renamer, so
    /// this intentionally validates the SFAT/SFNT tables before returning an extension.
    /// </summary>
    public static bool GuessSARC(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position;
        ext = "";
        try
        {
            if (br.BaseStream.Length - position < 0x14)
                return false;

            // Do not dispose this view: SARC owns the stream instance passed by the caller and
            // disposing it here would invalidate the BinaryReader before the position is reset.
            var sarc = new SARC(br.BaseStream);
            sarc.ValidateStructure();
            ext = "sarc";
        }
        catch { }
        finally
        {
            br.BaseStream.Position = position;
        }

        return ext.Length > 0;
    }

    /// <summary>
    /// Recognizes the SIR0-backed FARC variants supported by the portable reader. FARC can have
    /// a small prefix before its header, so the reader's bounded scan is preferable to checking
    /// only the first four bytes.
    /// </summary>
    public static bool GuessFARC(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position;
        ext = "";
        try
        {
            // FARC's reader intentionally scans from the absolute beginning because retail
            // variants may contain a prefix before the header. There is no meaningful relative
            // form for a non-zero stream position.
            if (position != 0 || br.BaseStream.Length < 0x30)
                return false;

            // FARC likewise wraps the caller's stream; the BinaryReader owns its lifetime.
            var farc = new FARC(br.BaseStream);
            if (farc.SigMatches)
                ext = "farc";
        }
        catch { }
        finally
        {
            br.BaseStream.Position = position;
        }

        return ext.Length > 0;
    }

    public static bool GuessLZ11(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position; // Store current position to reset after.
        ext = ""; // Reset extension
        try
        {
            int type = br.PeekChar();
            if (type != 0x11)
                return false;
            byte[] sizeBytes = new byte[3];
            br.Read(sizeBytes, 0, 3);

            int decompressedSize = sizeBytes[0] | sizeBytes[1] << 8 | sizeBytes[2];
            if (decompressedSize > br.BaseStream.Length && decompressedSize < br.BaseStream.Length * 10) // assuming 10x compression isn't feasible
                ext = "lz"; // really weak LZ detection, at most 16MB
        }
        catch { }
        br.BaseStream.Position = position;
        return ext.Length > 0;
    }

    public static bool GuessGar(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position;
        ext = "";
        try
        {
            if (br.BaseStream.Length - position >= 8)
            {
                var header = br.ReadBytes(8);
                if (GarPortable.HasHeader(header) &&
                    BitConverter.ToUInt32(header, 4) == br.BaseStream.Length - position)
                    ext = "gar";
            }
        }
        catch { }
        br.BaseStream.Position = position;
        return ext.Length > 0;
    }

    public static bool Guess4CHAR(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position; // Store current position to reset after.
        ext = ""; // Reset extension
        try
        {
            byte[] magic = Encoding.ASCII.GetBytes(br.ReadChars(4));

            Regex r = PatternAZ09();
            ext = Encoding.ASCII.GetString(magic);
            // Return BaseStream position to the start.
            br.BaseStream.Position = position;

            return r.IsMatch(ext) && ext.Length == 4;
        }
        catch { }
        br.BaseStream.Position = position;
        return false;
    }

    public static bool Guess3CHAR(BinaryReader br, out string ext)
    {
        long position = br.BaseStream.Position; // Store current position to reset after.
        ext = ""; // Reset extension
        try
        {
            byte[] magic = Encoding.ASCII.GetBytes(br.ReadChars(3));

            ext = Encoding.ASCII.GetString(magic);
            // Return BaseStream position to the start.
            br.BaseStream.Position = position;

            return validEXT.Contains(ext);
        }
        catch { }
        br.BaseStream.Position = position;
        return false;
    }

    [GeneratedRegex("^[a-zA-Z0-9]*$")]
    private static partial Regex PatternAZ09();
}
