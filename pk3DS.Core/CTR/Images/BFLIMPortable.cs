using System;
using System.IO;
using pk3DS.Core.CTR.Images;

namespace pk3DS.Core.CTR;

/// <summary>
/// Platform-neutral BFLIM reader. FLIM stores the footer after the tiled pixel payload, like
/// BCLIM, but has a different 40-byte header layout. The shared XLIM decoder keeps this path
/// free of System.Drawing. The writer emits the common 2.2 FLIM footer and reuses the tested
/// XLIM payload encoder, so PNG assets can be converted without a platform image library.
/// </summary>
public sealed class BFLIMPortable
{
    private const int MaxDecodedPixels = 64 * 1024 * 1024;

    public FLIMHeader Header { get; }
    public byte[] PixelData { get; }
    public int Width => Header.Width;
    public int Height => Header.Height;
    public XLIMEncoding Format => Header.Format;

    private BFLIMPortable(FLIMHeader header, byte[] pixelData)
    {
        Header = header;
        PixelData = pixelData;
    }

    public static BFLIMPortable Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < FLIMHeader.SIZE)
            throw new InvalidDataException("BFLIM es demasiado pequeño para contener su cabecera FLIM.");

        var footer = data[^FLIMHeader.SIZE..];
        var header = footer.ToStructure<FLIMHeader>();
        if (!header.Valid)
            throw new InvalidDataException("La cabecera FLIM del BFLIM no es válida.");
        if (header.Width == 0 || header.Height == 0)
            throw new InvalidDataException("BFLIM tiene dimensiones vacías.");

        var orienter = new XLIMOrienter(header.Width, header.Height, header.Orientation);
        var decodedPixels = (long)orienter.Width * orienter.Height;
        if (decodedPixels > MaxDecodedPixels)
            throw new InvalidDataException("BFLIM supera el límite de píxeles admitido.");

        var pixelLength = data.Length - FLIMHeader.SIZE;
        if (header.DataSize > pixelLength)
            throw new InvalidDataException("El tamaño de datos del BFLIM sale de sus límites.");
        if (header.DataSize > 0)
            pixelLength = checked((int)header.DataSize);

        return new BFLIMPortable(header, data[..pixelLength]);
    }

    public uint[] GetPixels() => BCLIMPortable.DecodePixels(
        PixelData, Width, Height, Format, Header.Orientation, allowPalette: false);

    /// <summary>Returns cropped RGBA bytes in normal row-major image order.</summary>
    public byte[] GetRgbaData(bool crop = true) => BCLIMPortable.DecodeRgba(
        PixelData, Width, Height, Format, Header.Orientation, crop, allowPalette: false);

    /// <summary>Encodes standard RGBA pixels into a tiled BFLIM payload.</summary>
    public static byte[] EncodeRgba(byte[] rgba, int width, int height, XLIMEncoding format = XLIMEncoding.RGBA8)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        var bclim = BCLIMPortable.EncodeRgba(rgba, width, height, format);
        var pixelLength = bclim.Length - CLIMHeader.SIZE;

        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(bclim, 0, pixelLength);
            writer.Write(0x4D494C46u); // FLIM
            writer.Write((ushort)0xFEFF);
            writer.Write((ushort)0x14);
            writer.Write(0x00020002u); // FLIM version 2.2
            writer.Write((uint)(pixelLength + FLIMHeader.SIZE));
            writer.Write(1u); // one image section (count + the format's padding)
            writer.Write(0x67616D69u); // imag
            writer.Write(0x10u);
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((short)0x80); // standalone BFLIM alignment marker
            writer.Write((byte)format);
            writer.Write((byte)XLIMOrientation.None);
            writer.Write((uint)pixelLength);
        }

        return output.ToArray();
    }
}
