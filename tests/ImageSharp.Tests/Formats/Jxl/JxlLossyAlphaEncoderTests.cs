// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lossy encoding of an image with alpha at Fastest (libjxl effort 3, slice E3c): the colour is an XYB
/// VarDCT frame, and the alpha a straight extra channel coded losslessly in Modular, as cjxl does by
/// default. Invisible pixels are smoothed before the DCT, so a transparent area costs no colour detail.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlLossyAlphaEncoderTests
{
    // E3a's interim bound on each colour channel's RMSE, in 8-bit steps (JxlLossyEncoderTests). These masks
    // are not in the calibration table, so they keep it; the table's photo with alpha (bike-small.png) is
    // checked against cjxl's SSIMULACRA2, with alpha exact, in JxlLossyCalibrationTests (E3f).
    private const double InterimRmseBound = 20.0;

    public static TheoryData<string, string, string> AlphaReaderCases { get; } = CreateAlphaReaderCases();

    // Alpha survives exactly through each reader, and the colour stays within the interim bound where it
    // is visible. The cases: a soft-edged disk (every alpha level, opaque inside, fully transparent
    // corners) and a hard-edged one (two levels, which libjxl's channel palette takes), in one group
    // (alpha in the global stream) and in several groups (alpha in the group streams), and over two LF
    // groups.
    [Theory]
    [MemberData(nameof(AlphaReaderCases))]
    public async Task Encode_Rgba_AlphaIsExactAndColourWithinTheInterimBound(string reader, string mask, string sizeCase)
    {
        using Image<Rgba32> image = CreateMaskedImage(sizeCase, mask);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, JxlLossyEncoderTests.Lossy(1.0f));

        using Image<Rgba32> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgba32>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyAlphaEncoder", $"{mask}-{sizeCase}");
        string label = $"{mask} {sizeCase}, {encoded.Length} bytes";
        AssertAlphaExact(image, decoded, label);
        AssertVisibleColourWithinInterimBound(image, decoded, label);
    }

    // A transparent area of noise: libjxl's SimplifyInvisible replaces its colour before the DCT, so the
    // file is smaller than the same image made opaque, where the noise is coded.
    [Fact]
    public void Encode_TransparentNoise_IsSmallerThanTheSameImageOpaque()
    {
        using Image<Rgba32> transparent = CreateImageWithTransparentNoise(alpha: 0);
        using Image<Rgba32> opaque = CreateImageWithTransparentNoise(alpha: 255);

        byte[] transparentFile = JxlEncoderRoundTrip.Encode(transparent, JxlLossyEncoderTests.Lossy(1.0f));
        byte[] opaqueFile = JxlEncoderRoundTrip.Encode(opaque, JxlLossyEncoderTests.Lossy(1.0f));

        TestContext.Current.TestOutputHelper?.WriteLine($"transparent noise {transparentFile.Length} bytes, opaque noise {opaqueFile.Length} bytes");
        Assert.True(transparentFile.Length < opaqueFile.Length, $"transparent {transparentFile.Length} bytes >= opaque {opaqueFile.Length} bytes");
    }

    // libjxl tries the global channel palette on the extra channels at effort 3, as for lossless: alpha
    // of two levels becomes a palette of two colours over the one channel, which is the only Modular
    // transform (the colour is VarDCT, so there is no RCT).
    [Fact]
    public void Encode_TwoLevelAlpha_IsCodedWithAChannelPaletteOfTwoColours()
    {
        using Image<Rgba32> image = CreateMaskedImage("flower-256x244", "hard");

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, JxlLossyEncoderTests.Lossy(1.0f)));

        JxlTransform palette = Assert.Single(frame.GlobalTransforms);
        Assert.Equal(JxlTransformId.Palette, palette.Id);
        Assert.Equal(0u, palette.BeginC);
        Assert.Equal(1u, palette.NumC);
        Assert.Equal(2u, palette.NbColors);
    }

    // The other 8-bit RGB pixel types with alpha encode the same samples as Rgba32.
    [Theory]
    [InlineData(nameof(Bgra32))]
    [InlineData(nameof(Argb32))]
    [InlineData(nameof(Abgr32))]
    public void Encode_Other8BitRgbaTypes_GiveTheBytesOfRgba32(string pixelType)
    {
        using Image<Rgba32> image = CreateMaskedImage("flower-17x23", "soft");
        byte[] expected = JxlEncoderRoundTrip.Encode(image, JxlLossyEncoderTests.Lossy(1.0f));

        byte[] actual = JxlPixelTypes.Call(pixelType, new EncodeAs(image));

        Assert.Equal(expected, actual);
    }

    // SHA-256 of this encoder's output with alpha (feat/jxl-e3f, after E3b's custom order), as
    // JxlLossyEncoderTests pins the opaque cases: any change to lossy Fastest output with alpha must update
    // these on purpose. One group (alpha in the global stream) and six (alpha in the group streams, as a
    // two-colour channel palette).
    [Theory]
    [InlineData("soft", "flower-256x244", "1CFAEA0D9B92A8BBB5076BABCF1BE6250B290B76FB749BB4A43537C2F314AAAA")]
    [InlineData("hard", "bike-582x453", "987B83B7321B7362B924C4E0C49394F9611A09A6E50D02A6997769A13758DFF6")]
    public void Encode_LossyAlpha_IsByteIdenticalToItsPin(string mask, string sizeCase, string expectedSha256)
    {
        using Image<Rgba32> image = CreateMaskedImage(sizeCase, mask);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, JxlLossyEncoderTests.Lossy(1.0f));

        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(encoded)));
    }

    // A size case of JxlLossyEncoderTests with an alpha mask: "soft" ramps from opaque inside a disk to
    // fully transparent outside a larger one; "hard" is opaque inside the disk and transparent outside.
    internal static Image<Rgba32> CreateMaskedImage(string sizeCase, string mask)
    {
        using Image<Rgb24> rgb = JxlLossyEncoderTests.LoadSizeCase(sizeCase);
        Image<Rgba32> image = new(rgb.Width, rgb.Height);
        double cx = (rgb.Width - 1) / 2.0;
        double cy = (rgb.Height - 1) / 2.0;
        double radius = Math.Max(1, Math.Min(rgb.Width, rgb.Height) / 2.0);
        for (int y = 0; y < rgb.Height; y++)
        {
            for (int x = 0; x < rgb.Width; x++)
            {
                double r = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy))) / radius;
                byte alpha = mask switch
                {
                    "hard" => r < 0.8 ? (byte)255 : (byte)0,
                    _ => (byte)Math.Clamp(Math.Round(255 * (1.1 - r) / 0.6), 0, 255),
                };

                Rgb24 p = rgb[x, y];
                image[x, y] = new Rgba32(p.R, p.G, p.B, alpha);
            }
        }

        return image;
    }

    // The flower crop with its right half replaced by noise of the given alpha.
    private static Image<Rgba32> CreateImageWithTransparentNoise(byte alpha)
    {
        using Image<Rgb24> rgb = JxlLossyEncoderTests.LoadSizeCase("flower-256x244");
        Image<Rgba32> image = new(rgb.Width, rgb.Height);
        Random random = new(42);
        for (int y = 0; y < rgb.Height; y++)
        {
            for (int x = 0; x < rgb.Width; x++)
            {
                Rgb24 p = rgb[x, y];
                image[x, y] = x < rgb.Width / 2
                    ? new Rgba32(p.R, p.G, p.B, 255)
                    : new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha);
            }
        }

        return image;
    }

    internal static void AssertAlphaExact(Image<Rgba32> expected, Image<Rgba32> actual, string label)
    {
        Assert.Equal(expected.Size(), actual.Size());
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                if (expected[x, y].A != actual[x, y].A)
                {
                    Assert.Fail($"{label}: alpha at ({x}, {y}) is {actual[x, y].A}, expected {expected[x, y].A}");
                }
            }
        }
    }

    internal static void AssertVisibleColourWithinInterimBound(Image<Rgba32> expected, Image<Rgba32> actual, string label)
    {
        double[] sums = new double[3];
        long n = 0;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Rgba32 e = expected[x, y];
                if (e.A == 0)
                {
                    continue;
                }

                Rgba32 a = actual[x, y];
                int[] d = { a.R - e.R, a.G - e.G, a.B - e.B };
                for (int c = 0; c < 3; c++)
                {
                    sums[c] += d[c] * d[c];
                }

                n++;
            }
        }

        double[] rmse = Array.ConvertAll(sums, s => Math.Sqrt(s / Math.Max(n, 1)));
        TestContext.Current.TestOutputHelper?.WriteLine($"{label}: visible RMSE R {rmse[0]:F2} G {rmse[1]:F2} B {rmse[2]:F2} over {n} pixels");
        for (int c = 0; c < 3; c++)
        {
            Assert.True(rmse[c] <= InterimRmseBound, $"{label}: channel {c} visible RMSE {rmse[c]:F2} > {InterimRmseBound}");
        }
    }

    private static TheoryData<string, string, string> CreateAlphaReaderCases()
    {
        (string Mask, string SizeCase)[] cases =
        {
            ("soft", "flower-256x244"),
            ("hard", "flower-256x244"),
            ("soft", "bike-582x453"),
            ("hard", "bike-582x453"),
            ("soft", "gradient-2049x9"),
        };

        TheoryData<string, string, string> data = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach ((string mask, string sizeCase) in cases)
            {
                data.Add(reader.ToString(), mask, sizeCase);
            }
        }

        return data;
    }

    // Encodes the image converted to another pixel type.
    private sealed record EncodeAs(Image<Rgba32> Image) : IJxlPixelTypeCall<byte[]>
    {
        public byte[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> converted = this.Image.CloneAs<TPixel>();
            return JxlEncoderRoundTrip.Encode(converted, JxlLossyEncoderTests.Lossy(1.0f));
        }
    }
}
