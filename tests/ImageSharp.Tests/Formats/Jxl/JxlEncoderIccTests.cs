// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Slice E1f: the pixel profile is embedded verbatim when its colour space matches the pixels, and
/// otherwise the sRGB enum is written (#115).
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderIccTests
{
    /// <summary>
    /// Gets profiles whose colour space matches the pixels: pixel type and profile path. Between them
    /// they take each branch of the ICC predictor: XYZ and TRC triples, <c>curv</c> and <c>para</c>
    /// tags, <c>mluc</c> text, <c>mAB </c> curves with a CLUT, and a profile whose encoded form is past
    /// the 16384 bytes where libjxl changes its LZ77 method.
    /// </summary>
    public static TheoryData<string, string> MatchingProfiles { get; } = new()
    {
        { nameof(Rgb24), TestImages.Icc.CompactDisplayP3V4 },
        { nameof(Rgb24), TestImages.Icc.CompactAdobeRgbV2 },
        { nameof(Rgb24), TestImages.Icc.LittleCmsDisplayP3V4 },
        { nameof(Rgb24), TestImages.Icc.LittleCmsAdobeRgbV4 },
        { nameof(Rgb24), TestImages.Icc.LittleCmsSrgbV2 },
        { nameof(Rgb24), TestImages.Icc.LittleCmsRgbLutAToBV4 },
        { nameof(Rgb24), TestImages.Icc.LittleCmsRgbLut16V2 },
        { nameof(L8), TestImages.Icc.CompactGrayV4 },
        { nameof(L8), TestImages.Icc.LittleCmsGray22V4 },
        { nameof(L8), TestImages.Icc.LittleCmsGrayLutV4 },
    };

    [Theory]
    [MemberData(nameof(MatchingProfiles))]
    public void Encode_MatchingProfile_OurDecoderGivesTheSameProfileBytesAndPixels(string pixelType, string profile)
    {
        byte[] icc = ReadProfile(profile);
        if (pixelType == nameof(L8))
        {
            AssertOurDecoderKeepsProfile<L8>(icc);
        }
        else
        {
            AssertOurDecoderKeepsProfile<Rgb24>(icc);
        }
    }

    private static byte[] ReadProfile(string profile) => File.ReadAllBytes(TestFile.GetInputFileFullPath(profile));

    private static Image<TPixel> CreateImage<TPixel>(byte[] icc)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel> image = JxlEncoderRoundTrip.CreateImage<TPixel>("gradient", 17, 23);
        image.Metadata.IccProfile = icc is null ? null : new IccProfile(icc);
        return image;
    }

    private static void AssertOurDecoderKeepsProfile<TPixel>(byte[] icc)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = CreateImage<TPixel>(icc);

        using Image<TPixel> decoded = Image.Load<TPixel>(JxlEncoderRoundTrip.CreateConfiguration(), JxlEncoderRoundTrip.Encode(image));

        JxlMetadata jxlMetadata = decoded.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.NotNull(jxlMetadata.OriginalIccProfile);
        Assert.Equal(icc, jxlMetadata.OriginalIccProfile.ToByteArray());
        Assert.Equal(icc, decoded.Metadata.IccProfile?.ToByteArray());
        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }
}
