// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using ImageMagick;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.ColorManagement;
using Xunit;
using static SixLabors.ImageSharp.Tests.TestImages.Bmp;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests.Formats.Bmp;

[Trait("Format", "Bmp")]
public class BmpMetadataTests
{
    // bV5CSType values from the Microsoft BITMAPV5HEADER documentation (wingdi.h).
    private const int ProfileEmbedded = 0x4D424544; // 'MBED'
    private const int ProfileLinked = 0x4C494E4B; // 'LINK'
    private const int LcsSrgb = 0x73524742; // 'sRGB'
    private const int LcsWindowsColorSpace = 0x57696E20; // 'Win '
    private const int LcsCalibratedRgb = 0;

    // Sizes of the BITMAPFILEHEADER, BITMAPV4HEADER and BITMAPV5HEADER structures, from the Microsoft documentation.
    private const int FileHeaderSize = 14;
    private const int V4HeaderSize = 108;
    private const int V5HeaderSize = 124;
    private const int PixelRowSize = 4;

    [Fact]
    public void CloneIsDeep()
    {
        var meta = new BmpMetadata { BitsPerPixel = BmpBitsPerPixel.Pixel24, InfoHeaderType = BmpInfoHeaderType.Os2Version2 };
        var clone = (BmpMetadata)meta.DeepClone();

        clone.BitsPerPixel = BmpBitsPerPixel.Pixel32;
        clone.InfoHeaderType = BmpInfoHeaderType.WinVersion2;

        Assert.False(meta.BitsPerPixel.Equals(clone.BitsPerPixel));
        Assert.False(meta.InfoHeaderType.Equals(clone.InfoHeaderType));
    }

    [Theory]
    [InlineData(WinBmpv2, BmpInfoHeaderType.WinVersion2)]
    [InlineData(WinBmpv3, BmpInfoHeaderType.WinVersion3)]
    [InlineData(WinBmpv4, BmpInfoHeaderType.WinVersion4)]
    [InlineData(WinBmpv5, BmpInfoHeaderType.WinVersion5)]
    [InlineData(Os2v2Short, BmpInfoHeaderType.Os2Version2Short)]
    [InlineData(Rgb32h52AdobeV3, BmpInfoHeaderType.AdobeVersion3)]
    [InlineData(Rgba32bf56AdobeV3, BmpInfoHeaderType.AdobeVersion3WithAlpha)]
    [InlineData(Os2v2, BmpInfoHeaderType.Os2Version2)]
    public void Identify_DetectsCorrectBitmapInfoHeaderType(string imagePath, BmpInfoHeaderType expectedInfoHeaderType)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            IImageInfo imageInfo = Image.Identify(stream);
            Assert.NotNull(imageInfo);
            BmpMetadata bitmapMetadata = imageInfo.Metadata.GetBmpMetadata();
            Assert.NotNull(bitmapMetadata);
            Assert.Equal(expectedInfoHeaderType, bitmapMetadata.InfoHeaderType);
        }
    }

    [Fact]
    public void Decode_V5EmbeddedProfile_ReadsProfile()
    {
        byte[] profile = SnakeProfileBytes();
        byte[] bmp = CreateV5Bitmap(ProfileEmbedded, profile);

        using (Image<Rgba32> image = Image.Load<Rgba32>(bmp))
        {
            Assert.Equal(profile, image.Metadata.IccProfile?.ToByteArray());
            Assert.Equal(new Rgba32(0x30, 0x20, 0x10), image[0, 0]);
        }

        Assert.Equal(profile, Image.Identify(bmp).Metadata.IccProfile?.ToByteArray());
    }

    [Theory]
    [InlineData(LcsSrgb)]
    [InlineData(LcsWindowsColorSpace)]
    [InlineData(ProfileLinked)]
    public void Decode_V5WithoutEmbeddedProfile_HasNoPixelProfile(int csType)
    {
        // The profile fields still point at valid profile bytes; only the color space type differs.
        byte[] bmp = CreateV5Bitmap(csType, SnakeProfileBytes());

        using (Image<Rgba32> image = Image.Load<Rgba32>(bmp))
        {
            Assert.Null(image.Metadata.IccProfile);
            Assert.Equal(new Rgba32(0x30, 0x20, 0x10), image[0, 0]);
        }

        Assert.Null(Image.Identify(bmp).Metadata.IccProfile);
    }

    [Fact]
    public void Decode_V5EmbeddedProfile_IgnoreMetadata_DoesNotReadProfile()
    {
        byte[] bmp = CreateV5Bitmap(ProfileEmbedded, SnakeProfileBytes());
        var decoder = new BmpDecoder { IgnoreMetadata = true };

        using (Image<Rgba32> image = Image.Load<Rgba32>(bmp, decoder))
        {
            Assert.Null(image.Metadata.IccProfile);
        }

        using var stream = new MemoryStream(bmp, false);
        Assert.Null(decoder.Identify(Configuration.Default, stream, TestContext.Current.CancellationToken).Metadata.IccProfile);
    }

    [Theory]
    [InlineData(int.MaxValue, null)] // Offset past the end of the stream.
    [InlineData(-1, null)] // Negative offset.
    [InlineData(0, null)] // Offset at the start of the info header.
    [InlineData(V5HeaderSize - 4, null)] // Offset inside the info header.
    [InlineData(V5HeaderSize + PixelRowSize + 1, null)] // Profile runs one byte past the end of the stream.
    [InlineData(null, -1)] // Negative size.
    [InlineData(null, 0)] // Empty profile.
    [InlineData(null, 127)] // Smaller than a profile header.
    [InlineData(null, int.MaxValue)] // Larger than the stream.
    public void Decode_V5EmbeddedProfileOutOfRange_DecodesWithoutProfile(int? profileOffset, int? profileSize)
    {
        byte[] bmp = CreateV5Bitmap(ProfileEmbedded, SnakeProfileBytes(), profileOffset, profileSize);

        using (Image<Rgba32> image = Image.Load<Rgba32>(bmp))
        {
            Assert.Null(image.Metadata.IccProfile);
            Assert.Equal(new Rgba32(0x30, 0x20, 0x10), image[0, 0]);
        }

        Assert.Null(Image.Identify(bmp).Metadata.IccProfile);
    }

    [Theory]
    [InlineData(BmpBitsPerPixel.Pixel1, false)]
    [InlineData(BmpBitsPerPixel.Pixel4, false)]
    [InlineData(BmpBitsPerPixel.Pixel8, false)]
    [InlineData(BmpBitsPerPixel.Pixel16, false)]
    [InlineData(BmpBitsPerPixel.Pixel24, false)]
    [InlineData(BmpBitsPerPixel.Pixel32, false)]
    [InlineData(BmpBitsPerPixel.Pixel32, true)]
    public void Encode_ImageWithIccProfile_KeepsProfileByteForByte(BmpBitsPerPixel bitsPerPixel, bool supportTransparency)
    {
        using Image source = Image.Load(TestFile.Create(TestImages.Jpeg.Baseline.Snake).Bytes);
        byte[] expected = source.Metadata.IccProfile.ToByteArray();

        using var stream = new MemoryStream();
        source.SaveAsBmp(stream, new BmpEncoder { BitsPerPixel = bitsPerPixel, SupportTransparency = supportTransparency });
        byte[] bmp = stream.ToArray();

        using (Image image = Image.Load(bmp))
        {
            Assert.Equal(expected, image.Metadata.IccProfile?.ToByteArray());
            Assert.Equal(BmpInfoHeaderType.WinVersion5, image.Metadata.GetBmpMetadata().InfoHeaderType);
        }

        Assert.Equal(expected, Image.Identify(bmp).Metadata.IccProfile?.ToByteArray());

        // ImageMagick reads the profile from the BITMAPV5HEADER fields on its own.
        using var magick = new MagickImage(bmp);
        Assert.Equal(expected, magick.GetColorProfile()?.ToByteArray());
    }

    [Fact]
    public void Decode_MagickV5EmbeddedProfile_ReadsProfile()
    {
        byte[] profile = SnakeProfileBytes();
        using var magick = new MagickImage(MagickColors.Red, 3, 2);
        magick.SetProfile(new ColorProfile(profile));
        byte[] bmp = magick.ToByteArray(MagickFormat.Bmp);
        Assert.Equal(V5HeaderSize, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(FileHeaderSize)));

        using (Image<Rgba32> image = Image.Load<Rgba32>(bmp))
        {
            Assert.Equal(profile, image.Metadata.IccProfile?.ToByteArray());
            Assert.Equal(new Rgba32(255, 0, 0), image[2, 1]);
        }

        Assert.Equal(profile, Image.Identify(bmp).Metadata.IccProfile?.ToByteArray());
    }

    [Theory]
    [InlineData(false, 40)] // BITMAPINFOHEADER
    [InlineData(true, 108)] // BITMAPV4HEADER
    public void Encode_ImageWithoutIccProfile_WritesSameHeaderAsBefore(bool supportTransparency, int expectedHeaderSize)
    {
        using var image = new Image<Rgba32>(3, 2);
        Assert.Null(image.Metadata.IccProfile);

        using var stream = new MemoryStream();
        image.SaveAsBmp(stream, new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel32, SupportTransparency = supportTransparency });
        byte[] bmp = stream.ToArray();

        // The file is the file header, the info header and 3x2 pixels of 4 bytes, with nothing after them.
        int expectedLength = FileHeaderSize + expectedHeaderSize + (3 * 2 * 4);
        Assert.Equal(expectedLength, bmp.Length);
        Assert.Equal(expectedLength, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(2))); // bfSize
        Assert.Equal(FileHeaderSize + expectedHeaderSize, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(10))); // bfOffBits
        Assert.Equal(expectedHeaderSize, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(FileHeaderSize))); // biSize
    }

    [Fact]
    public void Encode_ImageWithGrayIccProfile_WritesNoProfile()
    {
        // BMP samples are always RGB, so a gray profile can't describe them.
        using Image source = Image.Load(TestFile.Create(TestImages.Png.GrayA8BitInterlaced).Bytes);
        Assert.Equal(IccColorSpaceType.Gray, source.Metadata.IccProfile?.Header.DataColorSpace);

        using var stream = new MemoryStream();
        source.SaveAsBmp(stream, new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel24 });
        byte[] bmp = stream.ToArray();

        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(FileHeaderSize))); // biSize
        Assert.Null(Image.Identify(bmp).Metadata.IccProfile);
    }

    [Theory]
    [InlineData(IccRenderingIntent.Perceptual, 4)] // LCS_GM_IMAGES
    [InlineData(IccRenderingIntent.MediaRelativeColorimetric, 2)] // LCS_GM_GRAPHICS
    [InlineData(IccRenderingIntent.Saturation, 1)] // LCS_GM_BUSINESS
    [InlineData(IccRenderingIntent.AbsoluteColorimetric, 8)] // LCS_GM_ABS_COLORIMETRIC
    public void Encode_ImageWithIccProfile_WritesProfileIntent(IccRenderingIntent intent, int expectedBmpIntent)
    {
        byte[] profile = SnakeProfileBytes();
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(64), (uint)intent); // The header's rendering intent field.
        using var image = new Image<Rgba32>(3, 2);
        image.Metadata.IccProfile = new IccProfile(profile);

        using var stream = new MemoryStream();
        image.SaveAsBmp(stream);
        byte[] bmp = stream.ToArray();

        Span<byte> header = bmp.AsSpan(FileHeaderSize, V5HeaderSize);
        Assert.Equal(V5HeaderSize, BinaryPrimitives.ReadInt32LittleEndian(header)); // bV5Size
        Assert.Equal(ProfileEmbedded, BinaryPrimitives.ReadInt32LittleEndian(header.Slice(56))); // bV5CSType
        Assert.Equal(expectedBmpIntent, BinaryPrimitives.ReadInt32LittleEndian(header.Slice(108))); // bV5Intent
        Assert.Equal(profile.Length, BinaryPrimitives.ReadInt32LittleEndian(header.Slice(116))); // bV5ProfileSize
        Assert.Equal(bmp.Length, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(2))); // bfSize
    }

    [Fact]
    public void Decode_V5CalibratedRgb_BuildsProfileFromEndpointsAndGamma()
    {
        // DCI-P3 primaries with a different gamma per channel. The reference is the profile Little CMS
        // builds from the same primaries, the D65 white and the same gammas (Icc/README.md).
        byte[] bmp = CreateCalibratedBitmap(V5HeaderSize, P3Endpoints(), P3Gammas);
        IccProfile expected = IccProfileFactoryTests.Load(TestImages.Icc.LittleCmsP3Gamma182224V4);

        using (Image<Rgba32> image = Image.Load<Rgba32>(bmp))
        {
            Assert.NotNull(image.Metadata.IccProfile);
            IccProfileFactoryTests.AssertSameColour(expected, image.Metadata.IccProfile);
            Assert.Equal(new Rgba32(0x30, 0x20, 0x10), image[0, 0]);
        }

        IccProfileFactoryTests.AssertSameColour(expected, Image.Identify(bmp).Metadata.IccProfile);
    }

    [Fact]
    public void Decode_V4CalibratedRgb_BuildsProfileFromEndpointsAndGamma()
    {
        // A V4 header carries the same color space fields as a V5 one.
        byte[] bmp = CreateCalibratedBitmap(V4HeaderSize, P3Endpoints(), P3Gammas);

        using Image<Rgba32> image = Image.Load<Rgba32>(bmp);
        IccProfileFactoryTests.AssertSameColour(
            IccProfileFactoryTests.Load(TestImages.Icc.LittleCmsP3Gamma182224V4),
            image.Metadata.IccProfile);
    }

    [Fact]
    public void Decode_BmpSuiteCalibratedRgb_BuildsProfile()
    {
        // bmpsuite's pal8v4.bmp stores the BT.709 chromaticities as its endpoints and a gamma of 2.2 for
        // each channel.
        using Image<Rgba32> image = Image.Load<Rgba32>(TestFile.Create(WinBmpv4).Bytes);
        IccProfileFactoryTests.AssertSameColour(
            IccProfileFactoryTests.Load(TestImages.Icc.LittleCmsRec709Gamma22V4),
            image.Metadata.IccProfile);
    }

    [Fact]
    public void Decode_V4CalibratedRgbWithZeroEndpoints_HasNoPixelProfile()
    {
        // rgba32v4.bmp, like the V4 headers this encoder writes, says LCS_CALIBRATED_RGB with all-zero values.
        using Image<Rgba32> image = Image.Load<Rgba32>(TestFile.Create(Rgba32v4).Bytes);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [InlineData("AllZero")]
    [InlineData("ZeroRed")]
    [InlineData("NegativeGreenY")]
    [InlineData("NegativeBlueZ")]
    [InlineData("SamePrimaries")]
    [InlineData("CollinearPrimaries")]
    [InlineData("ZeroGamma")]
    [InlineData("HugeGamma")]
    public void Decode_V5CalibratedRgbDegenerate_DecodesWithoutProfile(string degenerate)
    {
        double[] endpoints = P3Endpoints();
        double[] gammas = { 1.8, 2.2, 2.4 };
        switch (degenerate)
        {
            case "AllZero":
                endpoints = new double[9];
                break;
            case "ZeroRed":
                Array.Clear(endpoints, 0, 3);
                break;
            case "NegativeGreenY":
                endpoints[4] = -endpoints[4];
                break;
            case "NegativeBlueZ":
                endpoints[8] = -endpoints[8];
                break;
            case "SamePrimaries":
                Array.Copy(endpoints, 0, endpoints, 3, 3);
                Array.Copy(endpoints, 0, endpoints, 6, 3);
                break;
            case "CollinearPrimaries":
                // Green is the mean of red and blue, so all three lie on one line in xy.
                for (int i = 0; i < 3; i++)
                {
                    endpoints[3 + i] = (endpoints[i] + endpoints[6 + i]) / 2;
                }

                break;
            case "ZeroGamma":
                gammas[1] = 0;
                break;
            case "HugeGamma":
                gammas[2] = 65535; // Larger than an ICC s15Fixed16Number can hold.
                break;
        }

        byte[] bmp = CreateCalibratedBitmap(V5HeaderSize, endpoints, gammas);

        using (Image<Rgba32> image = Image.Load<Rgba32>(bmp))
        {
            Assert.Null(image.Metadata.IccProfile);
            Assert.Equal(new Rgba32(0x30, 0x20, 0x10), image[0, 0]);
        }

        Assert.Null(Image.Identify(bmp).Metadata.IccProfile);
    }

    [Theory]
    [InlineData(4, IccRenderingIntent.Perceptual)] // LCS_GM_IMAGES
    [InlineData(2, IccRenderingIntent.MediaRelativeColorimetric)] // LCS_GM_GRAPHICS
    [InlineData(1, IccRenderingIntent.Saturation)] // LCS_GM_BUSINESS
    [InlineData(8, IccRenderingIntent.AbsoluteColorimetric)] // LCS_GM_ABS_COLORIMETRIC
    [InlineData(0, IccRenderingIntent.Perceptual)] // Not set.
    public void Decode_V5CalibratedRgb_ProfileHasHeaderIntent(int bmpIntent, IccRenderingIntent expected)
    {
        byte[] bmp = CreateCalibratedBitmap(V5HeaderSize, P3Endpoints(), P3Gammas, bmpIntent);

        using Image<Rgba32> image = Image.Load<Rgba32>(bmp);
        Assert.Equal(expected, image.Metadata.IccProfile?.Header.RenderingIntent);
    }

    [Theory]
    [InlineData(V4HeaderSize)]
    [InlineData(V5HeaderSize)]
    public void Decode_CalibratedRgb_IgnoreMetadata_BuildsNoProfile(int headerSize)
    {
        byte[] bmp = CreateCalibratedBitmap(headerSize, P3Endpoints(), P3Gammas);
        var decoder = new BmpDecoder { IgnoreMetadata = true };

        using Image<Rgba32> image = Image.Load<Rgba32>(bmp, decoder);
        Assert.Null(image.Metadata.IccProfile);
    }

    private static byte[] SnakeProfileBytes()
    {
        using Image jpeg = Image.Load(TestFile.Create(TestImages.Jpeg.Baseline.Snake).Bytes);
        return jpeg.Metadata.IccProfile.ToByteArray();
    }

    /// <summary>
    /// Builds a 1x1, 24-bit BMP with a BITMAPV5HEADER, laid out as the Microsoft documentation describes.
    /// The profile bytes follow the pixel data. The profile offset is relative to the start of the info header.
    /// </summary>
    private static byte[] CreateV5Bitmap(int csType, byte[] profile, int? profileOffset = null, int? profileSize = null)
    {
        int pixelOffset = FileHeaderSize + V5HeaderSize;
        byte[] data = new byte[pixelOffset + PixelRowSize + profile.Length];
        Span<byte> span = data;

        // BITMAPFILEHEADER
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(2), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(10), pixelOffset);

        // BITMAPV5HEADER
        Span<byte> header = span.Slice(FileHeaderSize, V5HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header, V5HeaderSize); // bV5Size
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4), 1); // bV5Width
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(8), 1); // bV5Height
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(12), 1); // bV5Planes
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(14), 24); // bV5BitCount
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(16), 0); // bV5Compression = BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(20), PixelRowSize); // bV5SizeImage
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(56), csType); // bV5CSType
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(108), 4); // bV5Intent = LCS_GM_IMAGES
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(112), profileOffset ?? (V5HeaderSize + PixelRowSize)); // bV5ProfileData
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(116), profileSize ?? profile.Length); // bV5ProfileSize

        // One BGR pixel, padded to 4 bytes.
        span[pixelOffset] = 0x10;
        span[pixelOffset + 1] = 0x20;
        span[pixelOffset + 2] = 0x30;

        profile.CopyTo(span.Slice(pixelOffset + PixelRowSize));
        return data;
    }

    /// <summary>
    /// The CIEXYZ endpoints of the DCI-P3 primaries (0.680, 0.320), (0.265, 0.690), (0.150, 0.060), each
    /// scaled to its share of the D65 white's luminance, as an encoder that writes real XYZ values would.
    /// </summary>
    private static readonly double[] P3Gammas = { 1.8, 2.2, 2.4 };

    private static double[] P3Endpoints()
    {
        (double X, double Y, double Lum)[] primaries = { (0.680, 0.320, 0.2290), (0.265, 0.690, 0.6917), (0.150, 0.060, 0.0793) };
        double[] endpoints = new double[9];
        for (int i = 0; i < 3; i++)
        {
            (double x, double y, double lum) = primaries[i];
            endpoints[i * 3] = x / y * lum;
            endpoints[(i * 3) + 1] = lum;
            endpoints[(i * 3) + 2] = (1 - x - y) / y * lum;
        }

        return endpoints;
    }

    /// <summary>
    /// Builds a 1x1, 24-bit BMP with a BITMAPV4HEADER (108 bytes) or BITMAPV5HEADER (124 bytes) whose
    /// bV5CSType is LCS_CALIBRATED_RGB, laid out as the Microsoft documentation describes.
    /// </summary>
    /// <param name="headerSize">108 or 124.</param>
    /// <param name="endpoints">The red, green and blue CIEXYZ endpoints (X, Y, Z each), written as FXPT2DOT30.</param>
    /// <param name="gammas">The red, green and blue gammas, written as unsigned 16.16.</param>
    /// <param name="intent">The bV5Intent value (V5 only).</param>
    private static byte[] CreateCalibratedBitmap(int headerSize, double[] endpoints, double[] gammas, int intent = 4)
    {
        int pixelOffset = FileHeaderSize + headerSize;
        byte[] data = new byte[pixelOffset + PixelRowSize];
        Span<byte> span = data;

        // BITMAPFILEHEADER
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(2), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(10), pixelOffset);

        // BITMAPV4HEADER / BITMAPV5HEADER
        Span<byte> header = span.Slice(FileHeaderSize, headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(header, headerSize); // bV5Size
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4), 1); // bV5Width
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(8), 1); // bV5Height
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(12), 1); // bV5Planes
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(14), 24); // bV5BitCount
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(16), 0); // bV5Compression = BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(20), PixelRowSize); // bV5SizeImage
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(56), LcsCalibratedRgb); // bV5CSType
        for (int i = 0; i < 9; i++)
        {
            // bV5Endpoints: CIEXYZTRIPLE of FXPT2DOT30 values.
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(60 + (i * 4)), (int)Math.Round(endpoints[i] * (1 << 30)));
        }

        for (int i = 0; i < 3; i++)
        {
            // bV5GammaRed, bV5GammaGreen, bV5GammaBlue: unsigned 16.16.
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(96 + (i * 4)), (uint)Math.Round(gammas[i] * 65536));
        }

        if (headerSize == V5HeaderSize)
        {
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(108), intent); // bV5Intent
        }

        // One BGR pixel, padded to 4 bytes.
        span[pixelOffset] = 0x10;
        span[pixelOffset + 1] = 0x20;
        span[pixelOffset + 2] = 0x30;
        return data;
    }
}
