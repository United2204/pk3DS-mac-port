using pk3DS.Core.CTR;

namespace pk3DS.Editors.Tests;

public sealed class SmdhPortableTests
{
    [Fact]
    public void BlankSmdhHasTheRetailSizeAndRoundTripsMetadata()
    {
        var smdh = SMDHPortable.CreateBlank();
        smdh.AppInfo[0] = new SMDHApplicationInfo("pk3DS Mac", "Editor portátil", "United2204");

        var roundTrip = SMDHPortable.Read(smdh.Write());

        Assert.Equal(SMDHPortable.FileSize, roundTrip.Write().Length);
        Assert.Equal("pk3DS Mac", roundTrip.AppInfo[0].ShortDescription);
        Assert.Equal("Editor portátil", roundTrip.AppInfo[0].LongDescription);
        Assert.Equal("United2204", roundTrip.AppInfo[0].Publisher);
    }

    [Fact]
    public void SmallAndLargeRgb565IconsRoundTripThroughThePortableCodec()
    {
        var smdh = SMDHPortable.CreateBlank();
        var small = BuildRgba(SMDHPortable.SmallIconWidth, SMDHPortable.SmallIconHeight);
        var large = BuildRgba(SMDHPortable.LargeIconWidth, SMDHPortable.LargeIconHeight);
        smdh.SetSmallIconRgba(small);
        smdh.SetLargeIconRgba(large);

        var roundTrip = SMDHPortable.Read(smdh.Write());
        var decodedSmall = roundTrip.GetSmallIconRgba();
        var decodedLarge = roundTrip.GetLargeIconRgba();

        Assert.Equal(small.Length, decodedSmall.Length);
        Assert.Equal(large.Length, decodedLarge.Length);
        Assert.Equal(small[0], decodedSmall[0]);
        Assert.Equal(small[1], decodedSmall[1]);
        Assert.Equal(small[2], decodedSmall[2]);
        var sourceBlue = large[(47 * 48 + 31) * 4 + 2];
        var decodedBlue = decodedLarge[(47 * 48 + 31) * 4 + 2];
        Assert.InRange(Math.Abs(sourceBlue - decodedBlue), 0, 8);
        Assert.All(decodedSmall.Where((_, index) => index % 4 == 3), alpha => Assert.Equal(byte.MaxValue, alpha));
    }

    [Fact]
    public void ApplicationSettingsRoundTripWithoutDroppingBinaryValues()
    {
        var smdh = SMDHPortable.CreateBlank();
        var ratings = Enumerable.Range(0, SMDHApplicationSettings.GameRatingsCount)
            .Select(index => (byte)(index * 7))
            .ToArray();
        ratings.CopyTo(smdh.Settings.GameRatings, 0);
        smdh.Settings.RegionLockout = 0x45;
        smdh.Settings.MatchMakerId = 0x12345678;
        smdh.Settings.MatchMakerBitId = 0x1122334455667788;
        smdh.Settings.Flags = 0x3FF;
        smdh.Settings.EulaVersion = 0x1234;
        smdh.Settings.Reserved = 0xBEEF;
        smdh.Settings.AnimationDefaultFrame = 12.5f;
        smdh.Settings.StreetPassId = 0xAABBCCDD;

        var roundTrip = SMDHPortable.Read(smdh.Write());

        Assert.Equal(ratings, roundTrip.Settings.GameRatings);
        Assert.Equal(0x45u, roundTrip.Settings.RegionLockout);
        Assert.Equal(0x12345678u, roundTrip.Settings.MatchMakerId);
        Assert.Equal(0x1122334455667788ul, roundTrip.Settings.MatchMakerBitId);
        Assert.Equal(0x3FFu, roundTrip.Settings.Flags);
        Assert.Equal((ushort)0x1234, roundTrip.Settings.EulaVersion);
        Assert.Equal((ushort)0xBEEF, roundTrip.Settings.Reserved);
        Assert.Equal(12.5f, roundTrip.Settings.AnimationDefaultFrame);
        Assert.Equal(0xAABBCCDDu, roundTrip.Settings.StreetPassId);
    }

    [Fact]
    public void InvalidSmdhIsRejectedBeforeReadingFields()
    {
        Assert.Throws<InvalidDataException>(() => SMDHPortable.Read(new byte[SMDHPortable.FileSize - 1]));

        var bytes = new byte[SMDHPortable.FileSize];
        Assert.Throws<InvalidDataException>(() => SMDHPortable.Read(bytes));
    }

    private static byte[] BuildRgba(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            rgba[offset] = (byte)(x * 255 / Math.Max(1, width - 1));
            rgba[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
            rgba[offset + 2] = (byte)((x + y) * 255 / Math.Max(1, width + height - 2));
            rgba[offset + 3] = byte.MaxValue;
        }
        return rgba;
    }
}
