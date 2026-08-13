using System.Security.Cryptography;
using pk3DS.Core.CTR;

namespace pk3DS.Editors.Tests;

public sealed class CROHashTests
{
    [Fact]
    public void RehashReturnsFixedCopyWithoutMutatingInput()
    {
        var original = ValidCro(0x11);
        var snapshot = original.ToArray();

        var fixedCro = CRO.Rehash(original);

        Assert.Equal(snapshot, original);
        Assert.NotEqual(snapshot.AsSpan(0, 0x80).ToArray(), fixedCro.AsSpan(0, 0x80).ToArray());
        Assert.Equal(snapshot.AsSpan(0x180).ToArray(), fixedCro.AsSpan(0x180).ToArray());
        Assert.Equal(CRO.ComputeHash(fixedCro), SHA256.HashData(fixedCro.AsSpan(0, 0x80)));
    }

    [Fact]
    public void RebuildCrrSortsHashesAndIsIdempotent()
    {
        var crr = EmptyCrr(2);
        var first = ValidCro(0x22);
        var second = ValidCro(0x77);

        var rebuilt = CRO.RebuildCRR(crr, [first, second]);

        Assert.True(rebuilt.Changed);
        var expected = rebuilt.Cros.Select(CRO.ComputeHash)
            .OrderBy(hash => Convert.ToHexString(hash), StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], rebuilt.Crr.AsSpan(0x400 + (index * 0x20), 0x20).ToArray());

        var rerun = CRO.RebuildCRR(rebuilt.Crr, rebuilt.Cros);
        Assert.False(rerun.Changed);
    }

    [Fact]
    public void RebuildCrrRejectsWrongCroCount()
    {
        var exception = Assert.Throws<ArgumentException>(() => CRO.RebuildCRR(EmptyCrr(2), [ValidCro(1)]));

        Assert.Contains("espera 2 CROs", exception.Message);
    }

    private static byte[] EmptyCrr(int count)
    {
        var crr = new byte[0x500];
        BitConverter.GetBytes(0x400).CopyTo(crr, 0x350);
        BitConverter.GetBytes(count).CopyTo(crr, 0x354);
        return crr;
    }

    private static byte[] ValidCro(byte seed)
    {
        var cro = new byte[0x200];
        BitConverter.GetBytes(0x180).CopyTo(cro, 0xB0);
        BitConverter.GetBytes(0x20).CopyTo(cro, 0xB4);
        BitConverter.GetBytes(0x1C0).CopyTo(cro, 0xB8);
        BitConverter.GetBytes(0x1A0).CopyTo(cro, 0xC0);
        for (var index = 0x180; index < cro.Length; index++)
            cro[index] = (byte)(seed + index);
        return cro;
    }
}
