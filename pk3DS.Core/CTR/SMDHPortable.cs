using System.Buffers.Binary;
using System;
using System.IO;
using System.Text;

namespace pk3DS.Core.CTR;

/// <summary>
/// Platform-neutral reader/writer for the 3DS System Menu Data Header (SMDH).
/// The Windows project used <c>System.Drawing</c>; this variant keeps the binary format and
/// converts the 16-bit RGB565 icon tiles to ordinary RGBA bytes for the web host.
/// </summary>
public sealed class SMDHPortable
{
    public const int FileSize = 0x36C0;
    public const uint Magic = 0x48444D53; // "SMDH" in little endian
    public const int AppInfoCount = 16;
    public const int SmallIconWidth = 24;
    public const int SmallIconHeight = 24;
    public const int LargeIconWidth = 48;
    public const int LargeIconHeight = 48;

    private const int AppInfoBytes = 0x200;
    private const int SettingsBytes = 0x30;
    private const int ReservedBytes = 8;
    private const int SmallIconBytes = 0x480;
    private const int LargeIconBytes = 0x1200;

    private readonly byte[] _reserved;

    public ushort Version { get; }
    public ushort Reserved2 { get; }
    public SMDHApplicationSettings Settings { get; }
    public SMDHApplicationInfo[] AppInfo { get; }
    public byte[] SmallIcon { get; private set; }
    public byte[] LargeIcon { get; private set; }

    private SMDHPortable(
        ushort version,
        ushort reserved2,
        SMDHApplicationInfo[] appInfo,
        SMDHApplicationSettings settings,
        byte[] reserved,
        byte[] smallIcon,
        byte[] largeIcon)
    {
        Version = version;
        Reserved2 = reserved2;
        Settings = settings;
        AppInfo = appInfo;
        _reserved = reserved;
        SmallIcon = smallIcon;
        LargeIcon = largeIcon;
    }

    public static SMDHPortable Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != FileSize)
            throw new InvalidDataException($"SMDH debe tener exactamente 0x{FileSize:X} bytes.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            throw new InvalidDataException("La cabecera SMDH no tiene la firma esperada.");

        var appInfo = new SMDHApplicationInfo[AppInfoCount];
        var offset = 8;
        for (var index = 0; index < appInfo.Length; index++)
        {
            appInfo[index] = new SMDHApplicationInfo(
                ReadString(data, offset, 0x80),
                ReadString(data, offset + 0x80, 0x100),
                ReadString(data, offset + 0x180, 0x80));
            offset += AppInfoBytes;
        }

        var settings = SMDHApplicationSettings.Read(data.AsSpan(offset, SettingsBytes));
        offset += SettingsBytes;
        var reserved = data[offset..(offset + ReservedBytes)];
        offset += ReservedBytes;
        var smallIcon = data[offset..(offset + SmallIconBytes)];
        offset += SmallIconBytes;
        var largeIcon = data[offset..(offset + LargeIconBytes)];

        return new SMDHPortable(
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6)),
            appInfo,
            settings,
            reserved,
            smallIcon,
            largeIcon);
    }

    public static SMDHPortable CreateBlank()
    {
        var data = new byte[FileSize];
        BinaryPrimitives.WriteUInt32LittleEndian(data, Magic);
        return Read(data);
    }

    public byte[] Write()
    {
        var data = new byte[FileSize];
        BinaryPrimitives.WriteUInt32LittleEndian(data, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), Reserved2);

        var offset = 8;
        foreach (var info in AppInfo)
        {
            if (info is null)
                throw new InvalidDataException("Un slot AppInfo de SMDH está vacío.");
            WriteString(data, offset, 0x80, info.ShortDescription, "descripción corta");
            WriteString(data, offset + 0x80, 0x100, info.LongDescription, "descripción larga");
            WriteString(data, offset + 0x180, 0x80, info.Publisher, "editor");
            offset += AppInfoBytes;
        }

        Settings.Write().CopyTo(data, offset);
        offset += SettingsBytes;
        _reserved.CopyTo(data, offset);
        offset += ReservedBytes;
        SmallIcon.CopyTo(data, offset);
        offset += SmallIconBytes;
        LargeIcon.CopyTo(data, offset);
        return data;
    }

    public byte[] GetSmallIconRgba() => DecodeIcon(SmallIcon, SmallIconWidth, SmallIconHeight);

    public byte[] GetLargeIconRgba() => DecodeIcon(LargeIcon, LargeIconWidth, LargeIconHeight);

    public void SetSmallIconRgba(byte[] rgba) => SmallIcon = EncodeIcon(rgba, SmallIconWidth, SmallIconHeight);

    public void SetLargeIconRgba(byte[] rgba) => LargeIcon = EncodeIcon(rgba, LargeIconWidth, LargeIconHeight);

    private static string ReadString(byte[] data, int offset, int byteLength)
    {
        var value = Encoding.Unicode.GetString(data, offset, byteLength);
        var terminator = value.IndexOf('\0');
        return terminator < 0 ? value : value[..terminator];
    }

    private static void WriteString(byte[] data, int offset, int byteLength, string value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        var maxCharacters = byteLength / 2;
        if (value.Length > maxCharacters)
            throw new ArgumentException($"La {field} supera los {maxCharacters} caracteres UTF-16.", nameof(value));
        var encoded = Encoding.Unicode.GetBytes(value);
        encoded.CopyTo(data, offset);
    }

    private static byte[] DecodeIcon(byte[] source, int width, int height)
    {
        var expected = checked(width * height * 2);
        if (source.Length != expected)
            throw new InvalidDataException("El icono SMDH no tiene el tamaño esperado.");

        var rgba = new byte[checked(width * height * 4)];
        var tilesWide = (width + 7) / 8;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixel = TileOffset(x, y, tilesWide);
            var packed = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(pixel * 2));
            var target = (y * width + x) * 4;
            rgba[target] = Expand((packed >> 11) & 0x1F, 5);
            rgba[target + 1] = Expand((packed >> 5) & 0x3F, 6);
            rgba[target + 2] = Expand(packed & 0x1F, 5);
            rgba[target + 3] = byte.MaxValue;
        }
        return rgba;
    }

    private static byte[] EncodeIcon(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (rgba.Length != checked(width * height * 4))
            throw new ArgumentException("El tamaño RGBA no coincide con el icono SMDH.", nameof(rgba));

        var source = new byte[checked(width * height * 2)];
        var tilesWide = (width + 7) / 8;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var target = (y * width + x) * 4;
            var packed = (ushort)((Reduce(rgba[target], 5) << 11)
                | (Reduce(rgba[target + 1], 6) << 5)
                | Reduce(rgba[target + 2], 5));
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(TileOffset(x, y, tilesWide) * 2), packed);
        }
        return source;
    }

    private static int TileOffset(int x, int y, int tilesWide) =>
        (((y / 8) * tilesWide + (x / 8)) * 64) + ((y % 8) * 8) + (x % 8);

    private static byte Expand(int value, int bits) =>
        (byte)((value << (8 - bits)) | (value >> (bits - (8 - bits))));

    private static int Reduce(byte value, int bits) =>
        (value * ((1 << bits) - 1) + 127) / 255;
}

public sealed record SMDHApplicationInfo(
    string ShortDescription,
    string LongDescription,
    string Publisher);

/// <summary>
/// The 0x30-byte ApplicationSettings area that follows the sixteen AppInfo slots in an SMDH.
/// Values are kept explicit so editing one field does not discard the others.
/// </summary>
public sealed class SMDHApplicationSettings
{
    public const int GameRatingsCount = 0x10;
    public const int SerializedSize = 0x30;

    public byte[] GameRatings { get; }
    public uint RegionLockout { get; set; }
    public uint MatchMakerId { get; set; }
    public ulong MatchMakerBitId { get; set; }
    public uint Flags { get; set; }
    public ushort EulaVersion { get; set; }
    public ushort Reserved { get; set; }
    public float AnimationDefaultFrame { get; set; }
    public uint StreetPassId { get; set; }

    public SMDHApplicationSettings(
        byte[] gameRatings,
        uint regionLockout = 0,
        uint matchMakerId = 0,
        ulong matchMakerBitId = 0,
        uint flags = 0,
        ushort eulaVersion = 0,
        ushort reserved = 0,
        float animationDefaultFrame = 0,
        uint streetPassId = 0)
    {
        if (gameRatings is null || gameRatings.Length != GameRatingsCount)
            throw new ArgumentException($"SMDH debe contener exactamente {GameRatingsCount} ratings.", nameof(gameRatings));
        GameRatings = (byte[])gameRatings.Clone();
        RegionLockout = regionLockout;
        MatchMakerId = matchMakerId;
        MatchMakerBitId = matchMakerBitId;
        Flags = flags;
        EulaVersion = eulaVersion;
        Reserved = reserved;
        AnimationDefaultFrame = animationDefaultFrame;
        StreetPassId = streetPassId;
    }

    internal static SMDHApplicationSettings Read(ReadOnlySpan<byte> data)
    {
        if (data.Length != SerializedSize)
            throw new InvalidDataException("La sección ApplicationSettings SMDH tiene un tamaño inválido.");
        return new SMDHApplicationSettings(
            data[..GameRatingsCount].ToArray(),
            BinaryPrimitives.ReadUInt32LittleEndian(data[0x10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[0x14..]),
            BinaryPrimitives.ReadUInt64LittleEndian(data[0x18..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[0x20..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[0x24..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[0x26..]),
            BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(data[0x28..])),
            BinaryPrimitives.ReadUInt32LittleEndian(data[0x2C..]));
    }

    internal byte[] Write()
    {
        if (GameRatings is null || GameRatings.Length != GameRatingsCount)
            throw new InvalidDataException($"SMDH debe contener exactamente {GameRatingsCount} ratings.");
        var data = new byte[SerializedSize];
        GameRatings.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x10), RegionLockout);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x14), MatchMakerId);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x18), MatchMakerBitId);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x20), Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x24), EulaVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x26), Reserved);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x28), BitConverter.SingleToUInt32Bits(AnimationDefaultFrame));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2C), StreetPassId);
        return data;
    }
}
