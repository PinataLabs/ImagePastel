// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The transforms lossless Fastest tries, as libjxl effort 3 does: the global palette, the channel
/// palettes and global YCoCg. Each test reads the encoder's choice back through the decoder's parse.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderTransformTests
{
    // The sizes E1b's encoder (feat/jxl-e1 at b737478c3, no transforms) wrote for the same images.
    private const int IconSizeWithoutTransforms = 2981;
    private const int PhotoSizeWithoutTransforms = 129520;

    // The size this encoder (feat/jxl-e1-review at 1d1002ced) writes for the full-range photo with its
    // YCoCg step turned off, the only transform it takes there.
    private const int FullRangePhotoSizeWithoutYCoCg = 140588;

    [Fact]
    public void Encode_EightColourIcon_WritesAGlobalPaletteOfEightColours()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("icon", 64, 64);

        List<JxlTransform> transforms = JxlEncodedFrame.ReadGlobalTransforms(JxlEncoderRoundTrip.Encode(image));

        JxlTransform palette = Assert.Single(transforms);
        Assert.Equal(JxlTransformId.Palette, palette.Id);
        Assert.Equal(0u, palette.BeginC);
        Assert.Equal(3u, palette.NumC);
        Assert.Equal(8u, palette.NbColors);
        Assert.Equal(0u, palette.NbDeltas);
    }

    [Fact]
    public void Encode_EightColourIcon_IsSmallerThanWithoutTransforms()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("icon", 64, 64);

        int size = JxlEncoderRoundTrip.Encode(image).Length;

        Assert.True(size < IconSizeWithoutTransforms, $"{size} bytes, E1b wrote {IconSizeWithoutTransforms}");
    }

    [Fact]
    public void Encode_Photo_WritesYCoCgOverTheColourChannels()
    {
        using Image<Rgb24> image = LoadPhoto();

        List<JxlTransform> transforms = JxlEncodedFrame.ReadGlobalTransforms(JxlEncoderRoundTrip.Encode(image));

        JxlTransform rct = Assert.Single(transforms, t => t.Id == JxlTransformId.Rct);
        Assert.Equal(6u, rct.RctType);
        Assert.Same(rct, transforms[^1]);

        // Each channel palette before it adds one meta channel in front of the colour channels.
        Assert.Equal((uint)(transforms.Count - 1), rct.BeginC);
    }

    [Fact]
    public void Encode_Photo_IsSmallerThanWithoutTransforms()
    {
        using Image<Rgb24> image = LoadPhoto();

        int size = JxlEncoderRoundTrip.Encode(image).Length;

        Assert.True(size < PhotoSizeWithoutTransforms, $"{size} bytes, E1b wrote {PhotoSizeWithoutTransforms}");
    }

    // The photo test above measures channel palettes and YCoCg together. In this crop neither the
    // global palette nor a channel palette applies (the test checks that YCoCg is the only
    // transform), so the whole gain over the size without YCoCg is YCoCg's.
    [Fact]
    public void Encode_FullRangePhoto_WritesOnlyYCoCgAndIsSmallerThanWithoutIt()
    {
        using Image<Rgb24> image = LoadFullRangePhoto();

        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        JxlTransform rct = Assert.Single(JxlEncodedFrame.ReadGlobalTransforms(encoded));
        Assert.Equal(JxlTransformId.Rct, rct.Id);
        Assert.Equal(6u, rct.RctType);
        Assert.True(encoded.Length < FullRangePhotoSizeWithoutYCoCg, $"{encoded.Length} bytes, {FullRangePhotoSizeWithoutYCoCg} without YCoCg");
        using Image<Rgb24> decoded = Image.Load<Rgb24>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    [Fact]
    public void Encode_Photo_RoundTripsThroughOurDecoder()
    {
        using Image<Rgb24> image = LoadPhoto();

        using Image<Rgb24> decoded = Image.Load<Rgb24>(JxlEncoderRoundTrip.CreateConfiguration(), JxlEncoderRoundTrip.Encode(image));

        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    [Theory]
    [InlineData("gradient")]
    [InlineData("noise")]
    [InlineData("icon")]
    public void Encode_Grey_WritesNoRct(string pattern)
    {
        using Image<L8> image = JxlEncoderRoundTrip.CreateImage<L8>(pattern, 64, 64);

        List<JxlTransform> transforms = JxlEncodedFrame.ReadGlobalTransforms(JxlEncoderRoundTrip.Encode(image));

        Assert.DoesNotContain(transforms, t => t.Id == JxlTransformId.Rct);
    }

    [Fact]
    public void Encode_GreyWithFewLevels_WritesAChannelPalette()
    {
        // Three levels spread over most of the 8-bit range: libjxl compacts the channel when fewer
        // than 95% of the values in its range occur.
        using Image<L8> image = new(32, 32);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                System.Span<L8> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new L8((byte)(((x + y) % 3) switch { 0 => 10, 1 => 120, _ => 230 }));
                }
            }
        });

        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        JxlTransform palette = Assert.Single(JxlEncodedFrame.ReadGlobalTransforms(encoded));
        Assert.Equal(JxlTransformId.Palette, palette.Id);
        Assert.Equal(0u, palette.BeginC);
        Assert.Equal(1u, palette.NumC);
        Assert.Equal(3u, palette.NbColors);
        using Image<L8> decoded = Image.Load<L8>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    [Fact]
    public void Encode_SolidRgb_WritesNoPalette()
    {
        // libjxl does not make a palette of one colour.
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("solid", 32, 32);

        List<JxlTransform> transforms = JxlEncodedFrame.ReadGlobalTransforms(JxlEncoderRoundTrip.Encode(image));

        Assert.DoesNotContain(transforms, t => t.Id == JxlTransformId.Palette);
    }

    // A photo in one group: the middle 384x244 of the conformance flower, which the jxl-oracle job also has.
    internal static Image<Rgb24> LoadPhoto() => LoadFlower(x => (x - 384) / 2, 384);

    // Another photo in one group: the left 400x244 of the same flower.
    private static Image<Rgb24> LoadFullRangePhoto() => LoadFlower(_ => 0, 400);

    private static Image<Rgb24> LoadFlower(System.Func<int, int> left, int width)
    {
        Image<Rgb24> image = Image.Load<Rgb24>(JxlEncoderRoundTrip.CreateConfiguration(), JxlConformanceVector.Load("lz77_flower").CommittedInputPath);
        image.Mutate(x => x.Crop(new Rectangle(left(image.Width), 0, width, image.Height)));
        image.Metadata.IccProfile = null;
        return image;
    }
}
