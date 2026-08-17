using System;
using System.Buffers.Binary;
using System.IO;

namespace pk3DS.Core.CTR;

/// <summary>
/// Small, allocation-conscious ETC1/ETC1A4 decoder for BCLIM payloads.
/// </summary>
internal static class ETC1Portable
{
    private static readonly int[,] ModifierTable =
    {
        { 2, 8, -2, -8 },
        { 5, 17, -5, -17 },
        { 9, 29, -9, -29 },
        { 13, 42, -13, -42 },
        { 18, 60, -18, -60 },
        { 24, 80, -24, -80 },
        { 33, 106, -33, -106 },
        { 47, 183, -47, -183 },
    };

    private static readonly int[] DifferentialLookup = [0, 1, 2, 3, -4, -3, -2, -1];

    public static byte[] Decode(byte[] data, int width, int height, XLIMEncoding format)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (format is not (XLIMEncoding.ETC1 or XLIMEncoding.ETC1A4))
            throw new ArgumentException("The ETC1 decoder only accepts ETC1 or ETC1A4 data.", nameof(format));

        var orienter = new XLIMOrienter(width, height, XLIMOrientation.None);
        var decodedWidth = checked((int)orienter.Width);
        var decodedHeight = checked((int)orienter.Height);
        var blocksWide = decodedWidth / 4;
        var blocksHigh = decodedHeight / 4;
        var blockCount = checked(blocksWide * blocksHigh);
        var hasAlpha = format == XLIMEncoding.ETC1A4;
        var encodedBlockSize = hasAlpha ? 16 : 8;
        var expectedLength = checked(blockCount * encodedBlockSize);
        if (data.Length < expectedLength)
            throw new InvalidDataException($"El payload ETC1 está incompleto: se esperaban {expectedLength} bytes y hay {data.Length}.");

        var output = new byte[checked(decodedWidth * decodedHeight * 4)];
        var tileScramble = BuildTileScramble(blocksWide, blocksHigh);
        Span<byte> block = stackalloc byte[4 * 4 * 4];

        // The compressed stream is a raster of 4x4 blocks, but 3DS stores those
        // blocks in its ETC1 tile order and presents the texture upside down.
        for (var outputBlock = 0; outputBlock < blockCount; outputBlock++)
        {
            var sourceBlock = tileScramble[outputBlock];
            var sourceOffset = checked(sourceBlock * encodedBlockSize);
            var colorOffset = hasAlpha ? sourceOffset + 8 : sourceOffset;
            DecodeBlock(data.AsSpan(colorOffset, 8), block);

            var destinationBlockX = (outputBlock % blocksWide) * 4;
            var destinationBlockY = (outputBlock / blocksWide) * 4;
            for (var y = 0; y < 4; y++)
            {
                var destinationY = decodedHeight - 1 - destinationBlockY - y;
                for (var x = 0; x < 4; x++)
                {
                    var sourcePixelOffset = (x + (y * 4)) * 4;
                    var destinationPixelOffset = ((destinationBlockX + x) + (destinationY * decodedWidth)) * 4;
                    output[destinationPixelOffset] = block[sourcePixelOffset];
                    output[destinationPixelOffset + 1] = block[sourcePixelOffset + 1];
                    output[destinationPixelOffset + 2] = block[sourcePixelOffset + 2];
                    output[destinationPixelOffset + 3] = hasAlpha
                        ? DecodeAlpha(data, sourceOffset, x, y)
                        : byte.MaxValue;
                }
            }
        }

        return output;
    }

    private static byte DecodeAlpha(byte[] data, int blockOffset, int x, int y)
    {
        var packed = data[blockOffset + (2 * x) + (y / 2)];
        var nibble = (packed >> ((y & 1) * 4)) & 0x0F;
        return (byte)(nibble * 0x11);
    }

    private static void DecodeBlock(ReadOnlySpan<byte> encoded, Span<byte> rgba)
    {
        // BCLIM stores each ETC1 block in the reverse byte order emitted by the
        // standard big-endian ETC1 representation. Reverse the 8-byte block
        // before applying the format's bit fields.
        var high = BinaryPrimitives.ReadUInt32LittleEndian(encoded[4..]);
        var low = BinaryPrimitives.ReadUInt32LittleEndian(encoded);
        var differential = (high & 2) != 0;

        int red1;
        int red2;
        int green1;
        int green2;
        int blue1;
        int blue2;
        if (differential)
        {
            var redBase = (int)(high >> 27);
            var greenBase = (int)(high >> 19);
            var blueBase = (int)(high >> 11);
            red1 = Convert5To8(redBase);
            red2 = ConvertDifferential(redBase, (int)(high >> 24));
            green1 = Convert5To8(greenBase);
            green2 = ConvertDifferential(greenBase, (int)(high >> 16));
            blue1 = Convert5To8(blueBase);
            blue2 = ConvertDifferential(blueBase, (int)(high >> 8));
        }
        else
        {
            red1 = Convert4To8((int)(high >> 28));
            red2 = Convert4To8((int)(high >> 24));
            green1 = Convert4To8((int)(high >> 20));
            green2 = Convert4To8((int)(high >> 16));
            blue1 = Convert4To8((int)(high >> 12));
            blue2 = Convert4To8((int)(high >> 8));
        }

        var tableA = (int)((high >> 5) & 7);
        var tableB = (int)((high >> 2) & 7);
        var flipped = (high & 1) != 0;
        DecodeSubBlock(rgba, red1, green1, blue1, tableA, low, second: false, flipped);
        DecodeSubBlock(rgba, red2, green2, blue2, tableB, low, second: true, flipped);
    }

    private static void DecodeSubBlock(Span<byte> rgba, int red, int green, int blue, int tableIndex,
        uint low, bool second, bool flipped)
    {
        var baseX = 0;
        var baseY = 0;
        if (second)
        {
            if (flipped)
                baseY = 2;
            else
                baseX = 2;
        }

        for (var i = 0; i < 8; i++)
        {
            int x;
            int y;
            if (flipped)
            {
                x = baseX + (i >> 1);
                y = baseY + (i & 1);
            }
            else
            {
                x = baseX + (i >> 2);
                y = baseY + (i & 3);
            }

            var selectorBit = (int)((low >> (y + (x * 4))) & 1);
            var selectorHighBit = (int)((low >> (y + (x * 4) + 15)) & 2);
            var modifier = ModifierTable[tableIndex, selectorBit | selectorHighBit];
            var offset = (x + (y * 4)) * 4;
            rgba[offset] = Clamp(red + modifier);
            rgba[offset + 1] = Clamp(green + modifier);
            rgba[offset + 2] = Clamp(blue + modifier);
            rgba[offset + 3] = byte.MaxValue;
        }
    }

    private static int ConvertDifferential(int baseColor, int difference) =>
        Convert5To8((baseColor & 0x1F) + DifferentialLookup[difference & 7]);

    private static int Convert4To8(int value)
    {
        var color = value & 0x0F;
        return (color << 4) | color;
    }

    private static int Convert5To8(int value)
    {
        var color = value & 0x1F;
        return (color << 3) | (color >> 2);
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    private static int[] BuildTileScramble(int blocksWide, int blocksHigh)
    {
        var result = new int[checked(blocksWide * blocksHigh)];
        var baseAccumulator = 0;
        var lineAccumulator = 0;
        var baseNumber = 0;
        var lineNumber = 0;

        for (var tile = 0; tile < result.Length; tile++)
        {
            if (tile % blocksWide == 0 && tile > 0)
            {
                if (lineAccumulator < 1)
                {
                    lineAccumulator++;
                    lineNumber += 2;
                    baseNumber = lineNumber;
                }
                else
                {
                    lineAccumulator = 0;
                    baseNumber -= 2;
                    lineNumber = baseNumber;
                }
            }

            result[tile] = baseNumber;
            if (baseAccumulator < 1)
            {
                baseAccumulator++;
                baseNumber++;
            }
            else
            {
                baseAccumulator = 0;
                baseNumber += 3;
            }
        }

        for (var i = 0; i < result.Length; i++)
        {
            if ((uint)result[i] >= (uint)result.Length)
                throw new InvalidDataException("El orden de tiles ETC1 está fuera de rango.");
        }

        return result;
    }
}
