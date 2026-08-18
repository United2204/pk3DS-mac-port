using System;
using System.Buffers.Binary;
using System.IO;

namespace pk3DS.Core.CTR;

/// <summary>
/// Small, allocation-conscious ETC1/ETC1A4 codec for BCLIM payloads.
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

    /// <summary>
    /// Encodes RGBA pixels into the 3DS ETC1 tile order. The encoder intentionally
    /// favors a small, deterministic implementation over a rate-distortion
    /// optimizer: it emits valid individual-mode ETC1 blocks and preserves the
    /// source dimensions through the normal BCLIM padding rules.
    /// </summary>
    public static byte[] Encode(byte[] rgba, int width, int height, XLIMEncoding format)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (format is not (XLIMEncoding.ETC1 or XLIMEncoding.ETC1A4))
            throw new ArgumentException("El codificador ETC1 solo acepta ETC1 o ETC1A4.", nameof(format));
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Las dimensiones ETC1 deben ser positivas.");
        var expected = checked(width * height * 4);
        if (rgba.Length != expected)
            throw new ArgumentException("La longitud RGBA no coincide con las dimensiones ETC1.", nameof(rgba));

        var orienter = new XLIMOrienter(width, height, XLIMOrientation.None);
        var decodedWidth = checked((int)orienter.Width);
        var decodedHeight = checked((int)orienter.Height);
        var blocksWide = decodedWidth / 4;
        var blocksHigh = decodedHeight / 4;
        var blockCount = checked(blocksWide * blocksHigh);
        var hasAlpha = format == XLIMEncoding.ETC1A4;
        var encodedBlockSize = hasAlpha ? 16 : 8;
        var output = new byte[checked(blockCount * encodedBlockSize)];
        var tileScramble = BuildTileScramble(blocksWide, blocksHigh);
        Span<byte> block = stackalloc byte[4 * 4 * 4];

        // Decode() writes the texture upside down. Build the blocks in that
        // decoder coordinate system, then invert the tile permutation when
        // placing them into the BCLIM stream.
        for (var outputBlock = 0; outputBlock < blockCount; outputBlock++)
        {
            var sourceBlock = tileScramble[outputBlock];
            var outputBlockX = (outputBlock % blocksWide) * 4;
            var outputBlockY = (outputBlock / blocksWide) * 4;
            FillBlock(rgba, width, height, decodedWidth, decodedHeight,
                outputBlockX, outputBlockY, block);

            var encoded = EncodeColorBlock(block);
            var destinationOffset = checked(sourceBlock * encodedBlockSize);
            if (hasAlpha)
            {
                EncodeAlphaBlock(block, output.AsSpan(destinationOffset, 8));
                destinationOffset += 8;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(destinationOffset, 4), encoded.Low);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(destinationOffset + 4, 4), encoded.High);
        }

        return output;
    }

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

    private static void FillBlock(byte[] rgba, int width, int height, int decodedWidth, int decodedHeight,
        int blockX, int blockY, Span<byte> block)
    {
        for (var y = 0; y < 4; y++)
        {
            var sourceY = decodedHeight - 1 - blockY - y;
            for (var x = 0; x < 4; x++)
            {
                var sourceX = blockX + x;
                var destination = (x + (y * 4)) * 4;
                if (sourceX < width && sourceY >= 0 && sourceY < height)
                {
                    var source = (sourceX + (sourceY * width)) * 4;
                    block[destination] = rgba[source];
                    block[destination + 1] = rgba[source + 1];
                    block[destination + 2] = rgba[source + 2];
                    block[destination + 3] = rgba[source + 3];
                }
                else
                {
                    block[destination] = 0;
                    block[destination + 1] = 0;
                    block[destination + 2] = 0;
                    block[destination + 3] = 0;
                }
            }
        }
    }

    private static void EncodeAlphaBlock(ReadOnlySpan<byte> block, Span<byte> output)
    {
        output.Clear();
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                var alpha = block[(x + (y * 4)) * 4 + 3];
                var nibble = (byte)Math.Clamp((alpha + 8) / 17, 0, 15);
                var offset = (2 * x) + (y / 2);
                if ((y & 1) == 0)
                    output[offset] = (byte)((output[offset] & 0xF0) | nibble);
                else
                    output[offset] = (byte)((output[offset] & 0x0F) | (nibble << 4));
            }
        }
    }

    private static EncodedColorBlock EncodeColorBlock(ReadOnlySpan<byte> block)
    {
        var horizontal = EncodeColorBlock(block, flipped: false);
        var vertical = EncodeColorBlock(block, flipped: true);
        return vertical.Error < horizontal.Error ? vertical : horizontal;
    }

    private static EncodedColorBlock EncodeColorBlock(ReadOnlySpan<byte> block, bool flipped)
    {
        var first = EncodeSubBlock(block, flipped, second: false);
        var second = EncodeSubBlock(block, flipped, second: true);
        var high = ((uint)first.Red << 28) |
            ((uint)second.Red << 24) |
            ((uint)first.Green << 20) |
            ((uint)second.Green << 16) |
            ((uint)first.Blue << 12) |
            ((uint)second.Blue << 8) |
            ((uint)first.Table << 5) |
            ((uint)second.Table << 2) |
            (flipped ? 1u : 0u);
        var low = first.Selectors | second.Selectors;
        return new EncodedColorBlock(low, high, first.Error + second.Error);
    }

    private static EncodedSubBlock EncodeSubBlock(ReadOnlySpan<byte> block, bool flipped, bool second)
    {
        var pixelCount = 0;
        var averageRed = 0;
        var averageGreen = 0;
        var averageBlue = 0;
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                if (!IsInSubBlock(x, y, flipped, second))
                    continue;
                var offset = (x + (y * 4)) * 4;
                averageRed += block[offset];
                averageGreen += block[offset + 1];
                averageBlue += block[offset + 2];
                pixelCount++;
            }
        }

        var centerRed = Math.Clamp((int)Math.Round(averageRed / (double)pixelCount / 17), 0, 15);
        var centerGreen = Math.Clamp((int)Math.Round(averageGreen / (double)pixelCount / 17), 0, 15);
        var centerBlue = Math.Clamp((int)Math.Round(averageBlue / (double)pixelCount / 17), 0, 15);
        var best = new EncodedSubBlock();
        var bestError = long.MaxValue;

        // A small neighborhood around the mean covers the useful choices for
        // individual mode while keeping real title images inexpensive to edit.
        for (var red = Math.Max(0, centerRed - 2); red <= Math.Min(15, centerRed + 2); red++)
        {
            for (var green = Math.Max(0, centerGreen - 2); green <= Math.Min(15, centerGreen + 2); green++)
            {
                for (var blue = Math.Max(0, centerBlue - 2); blue <= Math.Min(15, centerBlue + 2); blue++)
                {
                    for (var table = 0; table < ModifierTable.GetLength(0); table++)
                    {
                        uint selectors = 0;
                        long error = 0;
                        for (var y = 0; y < 4; y++)
                        {
                            for (var x = 0; x < 4; x++)
                            {
                                if (!IsInSubBlock(x, y, flipped, second))
                                    continue;
                                var offset = (x + (y * 4)) * 4;
                                var selector = FindBestSelector(block, offset, red * 17, green * 17,
                                    blue * 17, table, out var pixelError);
                                error += pixelError;
                                var selectorIndex = y + (x * 4);
                                if ((selector & 1) != 0)
                                    selectors |= 1u << selectorIndex;
                                if ((selector & 2) != 0)
                                    selectors |= 1u << (selectorIndex + 16);
                            }
                        }

                        if (error < bestError)
                        {
                            bestError = error;
                            best = new EncodedSubBlock((byte)red, (byte)green, (byte)blue,
                                (byte)table, selectors, error);
                        }
                    }
                }
            }
        }

        return best;
    }

    private static bool IsInSubBlock(int x, int y, bool flipped, bool second) =>
        flipped ? (y >= 2) == second : (x >= 2) == second;

    private static int FindBestSelector(ReadOnlySpan<byte> block, int offset, int red, int green, int blue,
        int table, out int error)
    {
        var bestSelector = 0;
        var bestError = int.MaxValue;
        for (var selector = 0; selector < 4; selector++)
        {
            var modifier = ModifierTable[table, selector];
            var deltaRed = block[offset] - Math.Clamp(red + modifier, 0, 255);
            var deltaGreen = block[offset + 1] - Math.Clamp(green + modifier, 0, 255);
            var deltaBlue = block[offset + 2] - Math.Clamp(blue + modifier, 0, 255);
            var candidateError = (deltaRed * deltaRed) + (deltaGreen * deltaGreen) + (deltaBlue * deltaBlue);
            if (candidateError < bestError)
            {
                bestError = candidateError;
                bestSelector = selector;
            }
        }

        error = bestError;
        return bestSelector;
    }

    private readonly record struct EncodedColorBlock(uint Low, uint High, long Error);

    private readonly record struct EncodedSubBlock(byte Red, byte Green, byte Blue, byte Table,
        uint Selectors, long Error);

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
