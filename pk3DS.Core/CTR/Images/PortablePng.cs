using System;
using System.IO;
using System.IO.Compression;

namespace pk3DS.Core.CTR;

/// <summary>Small dependency-free PNG encoder for RGBA image exports.</summary>
public static class PortablePng
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] EncodeRgba(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "PNG dimensions must be positive.");
        var rowLength = checked(width * 4);
        var expected = checked(rowLength * height);
        if (rgba.Length != expected)
            throw new ArgumentException("RGBA data length does not match the PNG dimensions.", nameof(rgba));

        var scanlines = new byte[checked((rowLength + 1) * height)];
        for (var y = 0; y < height; y++)
        {
            var scanlineOffset = y * (rowLength + 1);
            Buffer.BlockCopy(rgba, y * rowLength, scanlines, scanlineOffset + 1, rowLength);
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(scanlines, 0, scanlines.Length);
            compressed = compressedStream.ToArray();
        }

        using var output = new MemoryStream();
        output.Write(Signature, 0, Signature.Length);

        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // truecolour + alpha
        WriteChunk(output, "IHDR"u8, ihdr);
        WriteChunk(output, "IDAT"u8, compressed);
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    public static PortablePngImage DecodeRgba(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < Signature.Length || !data.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            throw new InvalidDataException("El archivo no tiene una firma PNG válida.");

        var position = Signature.Length;
        var idat = new MemoryStream();
        var sawHeader = false;
        var sawEnd = false;
        int width = 0;
        int height = 0;
        byte colorType = 0;
        while (position <= data.Length - 12)
        {
            var length = ReadBigEndian(data, position);
            position += 4;
            var type = data.AsSpan(position, 4);
            position += 4;
            if (position > data.Length - 4 || length > int.MaxValue
                || (ulong)length > (ulong)(data.Length - position - 4))
                throw new InvalidDataException("Un bloque PNG sale de los límites del archivo.");

            var chunk = data.AsSpan(position, (int)length);
            position += (int)length;
            var expectedCrc = ReadBigEndian(data, position);
            position += 4;
            if (ComputeCrc(type, chunk) != expectedCrc)
                throw new InvalidDataException("Un bloque PNG tiene un CRC inválido.");

            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || length != 13)
                    throw new InvalidDataException("La cabecera IHDR del PNG no es válida.");
                var widthValue = ReadBigEndian(chunk, 0);
                var heightValue = ReadBigEndian(chunk, 4);
                if (widthValue == 0 || heightValue == 0 || widthValue > int.MaxValue || heightValue > int.MaxValue)
                    throw new InvalidDataException("Las dimensiones PNG no son válidas.");
                if (chunk[8] != 8 || chunk[9] is not (2 or 6) || chunk[10] != 0 || chunk[11] != 0 || chunk[12] != 0)
                    throw new FormatException("Solo se admiten PNG de 8 bits RGB/RGBA sin entrelazado.");
                width = (int)widthValue;
                height = (int)heightValue;
                colorType = chunk[9];
                if ((long)width * height > 64 * 1024 * 1024)
                    throw new InvalidDataException("El PNG supera el límite de píxeles admitido.");
                sawHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!sawHeader)
                    throw new InvalidDataException("El PNG contiene datos antes de IHDR.");
                idat.Write(chunk);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0)
                    throw new InvalidDataException("El bloque IEND del PNG no está vacío.");
                sawEnd = true;
                break;
            }
        }

        if (!sawHeader || !sawEnd || idat.Length == 0)
            throw new InvalidDataException("El PNG no contiene una secuencia IHDR/IDAT/IEND completa.");

        var bytesPerPixel = colorType == 6 ? 4 : 3;
        var rowLength = checked(width * bytesPerPixel);
        var filteredLength = checked((rowLength + 1) * height);
        byte[] filtered;
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true))
        using (var inflated = new MemoryStream())
        {
            zlib.CopyTo(inflated);
            filtered = inflated.ToArray();
        }
        if (filtered.Length != filteredLength)
            throw new InvalidDataException("El tamaño descomprimido del PNG no coincide con sus dimensiones.");

        var rows = new byte[checked(rowLength * height)];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * (rowLength + 1);
            var targetRow = y * rowLength;
            var filter = filtered[sourceRow];
            if (filter > 4)
                throw new FormatException($"El filtro PNG {filter} no es válido.");
            for (var x = 0; x < rowLength; x++)
            {
                var raw = filtered[sourceRow + 1 + x];
                var left = x >= bytesPerPixel ? rows[targetRow + x - bytesPerPixel] : 0;
                var up = y > 0 ? rows[targetRow - rowLength + x] : 0;
                var upperLeft = y > 0 && x >= bytesPerPixel ? rows[targetRow - rowLength + x - bytesPerPixel] : 0;
                rows[targetRow + x] = filter switch
                {
                    0 => raw,
                    1 => unchecked((byte)(raw + left)),
                    2 => unchecked((byte)(raw + up)),
                    3 => unchecked((byte)(raw + ((left + up) / 2))),
                    4 => unchecked((byte)(raw + Paeth(left, up, upperLeft))),
                    _ => throw new InvalidDataException("Filtro PNG no soportado."),
                };
            }
        }

        var rgba = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * rowLength;
            var targetRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var source = sourceRow + (x * bytesPerPixel);
                var target = targetRow + (x * 4);
                rgba[target] = rows[source];
                rgba[target + 1] = rows[source + 1];
                rgba[target + 2] = rows[source + 2];
                rgba[target + 3] = colorType == 6 ? rows[source + 3] : byte.MaxValue;
            }
        }

        return new PortablePngImage(width, height, rgba);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, (uint)data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crc = 0xFFFF_FFFFu;
        foreach (var value in type)
            crc = UpdateCrc(crc, value);
        foreach (var value in data)
            crc = UpdateCrc(crc, value);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBigEndian(crcBytes, crc ^ 0xFFFF_FFFFu);
        output.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB8_8320u : crc >> 1;
        return crc;
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var value in type)
            crc = UpdateCrc(crc, value);
        foreach (var value in data)
            crc = UpdateCrc(crc, value);
        return crc ^ 0xFFFF_FFFFu;
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        var estimate = left + up - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= upDistance && leftDistance <= upperLeftDistance
            ? left
            : upDistance <= upperLeftDistance ? up : upperLeft;
    }

    private static uint ReadBigEndian(ReadOnlySpan<byte> source, int offset) =>
        ((uint)source[offset] << 24) | ((uint)source[offset + 1] << 16)
        | ((uint)source[offset + 2] << 8) | source[offset + 3];

    private static void WriteBigEndian(Span<byte> destination, uint value) => WriteBigEndian(destination, 0, value);

    private static void WriteBigEndian(Span<byte> destination, int offset, uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }
}

public sealed record PortablePngImage(int Width, int Height, byte[] Rgba);
