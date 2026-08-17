using System;
using System.IO;
using System.Linq;
using System.Text;
using pk3DS.Core.CTR.Images;

namespace pk3DS.Core.CTR;

/// <summary>
/// Platform-neutral BCLIM reader. It decodes the tiled pixel payload to cropped RGBA bytes and
/// deliberately leaves image-file encoding to a separate portable codec.
/// </summary>
public sealed class BCLIMPortable
{
    private const int MaxDecodedPixels = 64 * 1024 * 1024;

    public CLIMHeader Header { get; }
    public byte[] PixelData { get; }
    public int Width => Header.Width;
    public int Height => Header.Height;
    public XLIMEncoding Format => Header.Format;

    private BCLIMPortable(CLIMHeader header, byte[] pixelData)
    {
        Header = header;
        PixelData = pixelData;
    }

    public static BCLIMPortable Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < CLIMHeader.SIZE)
            throw new InvalidDataException("BCLIM es demasiado pequeño para contener su cabecera CLIM.");

        var footerBytes = data[^CLIMHeader.SIZE..];
        var header = footerBytes.ToStructure<CLIMHeader>();
        if (!header.Valid)
            throw new InvalidDataException("La cabecera CLIM del BCLIM no es válida.");
        if (header.Width == 0 || header.Height == 0)
            throw new InvalidDataException("BCLIM tiene dimensiones vacías.");

        var orienter = new XLIMOrienter(header.Width, header.Height, header.Orientation);
        var decodedPixels = (long)orienter.Width * orienter.Height;
        if (decodedPixels > MaxDecodedPixels)
            throw new InvalidDataException("BCLIM supera el límite de píxeles admitido.");

        var pixelLength = data.Length - CLIMHeader.SIZE;
        if (header.DataSize > pixelLength)
            throw new InvalidDataException("El tamaño de datos del BCLIM sale de sus límites.");
        if (header.DataSize > 0)
            pixelLength = checked((int)header.DataSize);

        return new BCLIMPortable(header, data[..pixelLength]);
    }

    public uint[] GetPixels()
    {
        var orienter = new XLIMOrienter(Width, Height, Header.Orientation);
        var expected = checked((int)((long)orienter.Width * orienter.Height));
        if (Format == XLIMEncoding.RGB5A1 && PixelData.Length >= 4 && BitConverter.ToUInt16(PixelData, 0) == 2)
            return GetPalettePixels(expected);
        if (Format is XLIMEncoding.ETC1 or XLIMEncoding.ETC1A4)
        {
            var rgba = ETC1Portable.Decode(PixelData, Width, Height, Format);
            var etcPixels = new uint[expected];
            for (uint index = 0; index < etcPixels.Length; index++)
            {
                var coordinate = orienter.Get(index);
                if (coordinate.X >= orienter.Width || coordinate.Y >= orienter.Height)
                    continue;
                var offset = checked((int)((coordinate.X + (coordinate.Y * orienter.Width)) * 4));
                etcPixels[index] = (uint)((rgba[offset + 3] << 24) | (rgba[offset] << 16) |
                    (rgba[offset + 1] << 8) | rgba[offset + 2]);
            }
            return etcPixels;
        }

        var pixels = PixelConverter.GetPixels(PixelData, Format).Take(expected).ToArray();
        if (pixels.Length != expected)
            throw new InvalidDataException("El payload BCLIM no contiene suficientes píxeles.");
        return pixels;
    }

    /// <summary>Returns cropped RGBA bytes in normal row-major image order.</summary>
    public byte[] GetRgbaData(bool crop = true)
    {
        var orienter = new XLIMOrienter(Width, Height, Header.Orientation);
        var outputWidth = crop ? Width : checked((int)orienter.Width);
        var outputHeight = crop ? Height : checked((int)orienter.Height);
        var pixels = GetPixels();
        var rgba = new byte[checked(outputWidth * outputHeight * 4)];

        for (uint index = 0; index < pixels.Length; index++)
        {
            var coordinate = orienter.Get(index);
            if (coordinate.X >= outputWidth || coordinate.Y >= outputHeight)
                continue;

            var offset = checked((int)((coordinate.X + (coordinate.Y * (uint)outputWidth)) * 4));
            var value = pixels[index];
            rgba[offset] = (byte)(value >> 16);
            rgba[offset + 1] = (byte)(value >> 8);
            rgba[offset + 2] = (byte)value;
            rgba[offset + 3] = (byte)(value >> 24);
        }

        return rgba;
    }

    /// <summary>Encodes standard RGBA pixels into a tiled BCLIM payload.</summary>
    public static byte[] EncodeRgba(byte[] rgba, int width, int height, XLIMEncoding format = XLIMEncoding.RGBA8)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0 || width > ushort.MaxValue || height > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "BCLIM dimensions must fit in an unsigned 16-bit value.");
        var expected = checked(width * height * 4);
        if (rgba.Length != expected)
            throw new ArgumentException("RGBA data length does not match the BCLIM dimensions.", nameof(rgba));
        if (format is XLIMEncoding.ETC1 or XLIMEncoding.ETC1A4)
            throw new FormatException($"El formato BCLIM {format} todavía no tiene codificador portable.");

        var orienter = new XLIMOrienter(width, height, XLIMOrientation.None);
        var payloadPixels = checked((long)orienter.Width * orienter.Height);
        if (payloadPixels > MaxDecodedPixels)
            throw new InvalidDataException("BCLIM supera el límite de píxeles admitido.");
        using var pixels = new MemoryStream();
        using (var writer = new BinaryWriter(pixels, Encoding.UTF8, leaveOpen: true))
        {
            byte pending = 0;
            for (long index = 0; index < payloadPixels; index++)
            {
                var coordinate = orienter.Get(checked((uint)index));
                var color = coordinate.X < width && coordinate.Y < height
                    ? ReadRgba(rgba, width, coordinate.X, coordinate.Y)
                    : new RgbaColor(0, 0, 0, 0);
                var packed = EncodePixel(color, format);
                if (format is XLIMEncoding.L4 or XLIMEncoding.A4)
                {
                    if ((index & 1) == 0)
                        pending = packed;
                    else
                        writer.Write((byte)(pending | (packed << 4)));
                }
                else
                {
                    WritePackedPixel(writer, packed, format, color);
                }
            }
            if ((format is XLIMEncoding.L4 or XLIMEncoding.A4) && (payloadPixels & 1) != 0)
                writer.Write(pending);
        }

        var pixelData = pixels.ToArray();
        var perfect = width == height && width != 0 && (width & (width - 1)) == 0;
        if (!perfect)
            Array.Resize(ref pixelData, NextPowerOfTwo(pixelData.Length));

        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(pixelData);
            writer.Write(0x4D494C43u); // CLIM
            writer.Write((ushort)0xFEFF);
            writer.Write(0x14u);
            writer.Write((ushort)0x0202);
            writer.Write((uint)(pixelData.Length + CLIMHeader.SIZE));
            writer.Write(1u);
            writer.Write(0x67616D69u); // imag
            writer.Write(0x10u);
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((uint)format);
            writer.Write((uint)pixelData.Length);
        }
        return output.ToArray();
    }

    private uint[] GetPalettePixels(int expected)
    {
        using var stream = new MemoryStream(PixelData, writable: false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 2)
            throw new InvalidDataException("La paleta BCLIM no tiene el marcador esperado.");

        var colorCount = reader.ReadUInt16();
        if (colorCount == 0 || colorCount > 256)
            throw new InvalidDataException("La paleta BCLIM tiene una cantidad de colores inválida.");
        var colors = new uint[colorCount];
        for (var i = 0; i < colors.Length; i++)
            colors[i] = PixelConverter.GetDecodedPixelValue(reader.ReadUInt16(), XLIMEncoding.RGB565);

        var pixels = new uint[expected];
        var half = colors.Length < 0x10;
        for (var index = 0; index < pixels.Length;)
        {
            var value = reader.ReadByte();
            var low = value & 0x0F;
            var high = value >> 4;
            if (half)
            {
                if (low >= colors.Length || high >= colors.Length)
                    throw new InvalidDataException("La paleta BCLIM contiene un índice fuera de rango.");
                pixels[index++] = colors[low];
                if (index < pixels.Length)
                    pixels[index++] = colors[high];
            }
            else
            {
                if (value >= colors.Length)
                    throw new InvalidDataException("La paleta BCLIM contiene un índice fuera de rango.");
                pixels[index++] = colors[value];
            }
        }

        return pixels;
    }

    private static RgbaColor ReadRgba(byte[] rgba, int width, uint x, uint y)
    {
        var offset = checked((int)((x + (y * (uint)width)) * 4));
        return new RgbaColor(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
    }

    private static byte EncodePixel(RgbaColor color, XLIMEncoding format) => format switch
    {
        XLIMEncoding.L8 => Luminance(color),
        XLIMEncoding.A8 => color.A,
        XLIMEncoding.LA4 => (byte)((Luminance(color) / 0x11 << 4) | (color.A / 0x11)),
        XLIMEncoding.LA8 => 0,
        XLIMEncoding.HILO8 => 0,
        XLIMEncoding.RGB565 => 0,
        XLIMEncoding.RGBX8 => 0,
        XLIMEncoding.RGBA8 => 0,
        XLIMEncoding.RGB5A1 => 0,
        XLIMEncoding.RGBA4 => 0,
        XLIMEncoding.L4 => (byte)(Luminance(color) / 0x11),
        XLIMEncoding.A4 => (byte)(color.A / 0x11),
        _ => throw new FormatException($"El formato BCLIM {format} no está soportado."),
    };

    private static void WritePackedPixel(BinaryWriter writer, byte packed, XLIMEncoding format, RgbaColor color)
    {
        switch (format)
        {
            case XLIMEncoding.LA8:
                writer.Write((ushort)((Luminance(color) << 8) | color.A));
                break;
            case XLIMEncoding.HILO8:
                writer.Write((ushort)((color.R << 8) | color.G));
                break;
            case XLIMEncoding.RGB565:
                writer.Write((ushort)((ToBits(color.R, 5) << 11) | (ToBits(color.G, 6) << 5) | ToBits(color.B, 5)));
                break;
            case XLIMEncoding.RGBX8:
                writer.Write(color.B);
                writer.Write(color.G);
                writer.Write(color.R);
                break;
            case XLIMEncoding.RGBA8:
                writer.Write(color.A);
                writer.Write(color.B);
                writer.Write(color.G);
                writer.Write(color.R);
                break;
            case XLIMEncoding.RGB5A1:
                writer.Write((ushort)((ToBits(color.R, 5) << 11) | (ToBits(color.G, 5) << 6)
                    | (ToBits(color.B, 5) << 1) | (color.A > 0x80 ? 1 : 0)));
                break;
            case XLIMEncoding.RGBA4:
                writer.Write((ushort)((color.R / 0x11 << 12) | (color.G / 0x11 << 8)
                    | (color.B / 0x11 << 4) | (color.A / 0x11)));
                break;
            case XLIMEncoding.L8:
            case XLIMEncoding.A8:
            case XLIMEncoding.LA4:
                writer.Write(packed);
                break;
            default:
                throw new FormatException($"El formato BCLIM {format} no está soportado.");
        }
    }

    private static byte Luminance(RgbaColor color) =>
        (byte)((((0x4CB2 * color.R) + (0x9691 * color.G) + (0x1D3E * color.B)) >> 16) & 0xFF);

    private static int ToBits(byte value, int bits) => (value * ((1 << bits) - 1) + 127) / 255;

    private readonly record struct RgbaColor(byte R, byte G, byte B, byte A);

    private static int NextPowerOfTwo(int value)
    {
        if (value <= 0)
            throw new InvalidDataException("Dimensión BCLIM inválida.");
        var result = 1;
        while (result < value)
            result = checked(result << 1);
        return result;
    }
}
