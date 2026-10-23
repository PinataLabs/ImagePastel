// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Tests.TestUtilities.ReferenceCodecs;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Slice E1f: the pixel profile is embedded verbatim when its colour space matches the pixels, and
/// otherwise the sRGB enum is written (#115). A profile that is not valid, or that libjxl's ICC reader
/// would refuse, throws <see cref="ImageFormatException"/>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderIccTests
{
    private const int IccHeaderSize = 128;

    // The end of the 11-tag table of LittleCms sRGB-v2.icc: the count, then 12 bytes per tag.
    private const int IccTagTableEnd = IccHeaderSize + 4 + (11 * 12);

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
    public Task Encode_MatchingProfile_OurDecoderGivesTheSameProfileBytesAndPixels(string pixelType, string profile)
        => JxlPixelTypes.Verify(pixelType, new ProfileCheck(Check.OurDecoderKeepsProfile, ReadProfile(profile)));

    [Theory]
    [MemberData(nameof(MatchingProfiles))]
    public Task Encode_MatchingProfile_MagickGivesTheSameProfileBytesAndPixels(string pixelType, string profile)
    {
        MagickReferenceDecoder.SkipUnlessJxlIsSupported();

        return JxlPixelTypes.Verify(pixelType, new ProfileCheck(Check.MagickKeepsProfile, ReadProfile(profile)));
    }

    [Theory]
    [MemberData(nameof(MatchingProfiles))]
    public Task Encode_MatchingProfile_DjxlGivesTheSamePixels(string pixelType, string profile)
        => JxlPixelTypes.Verify(pixelType, new ProfileCheck(Check.DjxlExact, ReadProfile(profile), $"Icc_{pixelType}_{Path.GetFileNameWithoutExtension(profile)}"));

    [Theory]
    [InlineData(TestImages.Icc.CompactDisplayP3V4)]
    [InlineData(TestImages.Icc.LittleCmsRgbLut16V2)]
    public void Encode_MatchingProfile_IccStreamUsesPrefixCodes(string profile)
    {
        // libjxl's WriteICC sets force_huffman, so the ICC stream uses prefix codes at any size.
        using Image<Rgb24> image = CreateImage<Rgb24>(ReadProfile(profile));

        JxlAnsCode code = JxlEntropyCodeProbe.ReadIccCode(JxlEncoderRoundTrip.Encode(image));

        Assert.True(code.UsePrefixCode);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void Encode_ProfileWithArbitraryBytes_RoundTripsByteForByte(int seed, bool randomTagTable)
    {
        // The predictor must encode any bytes, not only a well-formed profile: the real tag table
        // followed by noisy tag contents, or a few noisy tag entries followed by noise.
        byte[] icc = ReadProfile(TestImages.Icc.LittleCmsSrgbV2);
        Random random = new(seed);
        if (randomTagTable)
        {
            random.NextBytes(icc.AsSpan(IccHeaderSize));
            BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(IccHeaderSize), (uint)(seed * 5));
        }
        else
        {
            random.NextBytes(icc.AsSpan(IccTagTableEnd));
        }

        AssertOurDecoderKeepsProfile<Rgb24>(icc);
    }

    [Fact]
    public void Encode_ProfileWhoseTagListIsTooLargeForTheDecoder_ThrowsAndWritesNothing()
    {
        // libjxl's ICC reader checks that the commands of the encoded profile lie in its first 1024
        // bytes. 200 tags whose offsets and sizes cannot be predicted take about 11 bytes each there.
        const int numTags = 200;
        byte[] icc = new byte[4000];
        ReadProfile(TestImages.Icc.CompactDisplayP3V4).AsSpan(0, IccHeaderSize).CopyTo(icc);
        BinaryPrimitives.WriteUInt32BigEndian(icc, (uint)icc.Length);
        BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(IccHeaderSize), numTags);
        for (int i = 0; i < numTags; i++)
        {
            Span<byte> entry = icc.AsSpan(IccHeaderSize + 4 + (i * 12), 12);
            BinaryPrimitives.WriteUInt32BigEndian(entry, 0x7A7A7A7A); // "zzzz"
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], 0xF0000000u + (uint)(i * 977));
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], 0x10000000u + (uint)(i * 131));
        }

        Assert.True(new IccProfile(icc).CheckIsValid());
        using Image<Rgb24> image = CreateImage<Rgb24>(icc);
        using MemoryStream stream = new();

        Assert.Throws<ImageFormatException>(() => image.Save(stream, JxlEncoderRoundTrip.LosslessFastest));
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(nameof(Rgb24), TestImages.Tiff.CmykTestIccProfile, IccColorSpaceType.Cmyk)]
    [InlineData(nameof(Rgb24), TestImages.Icc.CompactGrayV4, IccColorSpaceType.Gray)]
    [InlineData(nameof(L8), TestImages.Icc.CompactDisplayP3V4, IccColorSpaceType.Rgb)]
    public Task Encode_ProfileNotMatchingThePixels_WritesTheSrgbEnumAndNoProfile(string pixelType, string profile, IccColorSpaceType profileColorSpace)
    {
        byte[] icc = ReadProfile(profile);
        Assert.Equal(profileColorSpace, new IccProfile(icc).Header.DataColorSpace);

        return JxlPixelTypes.Verify(pixelType, new ProfileCheck(Check.OurDecoderGetsSrgbEnum, icc));
    }

    [Theory]
    [InlineData(nameof(Rgb24))]
    [InlineData(nameof(L8))]
    public Task Encode_NoProfile_WritesTheSrgbEnum(string pixelType)
        => JxlPixelTypes.Verify(pixelType, new ProfileCheck(Check.OurDecoderGetsSrgbEnum, null));

    [Fact]
    public void Encode_ProfileThatIsNotValid_ThrowsAndWritesNothing()
    {
        // An RGB profile whose header claims more bytes than it has. libjxl's JxlEncoderSetICCProfile
        // rejects a profile its CMS cannot parse, as the encoder rejects one its ICC reader would refuse.
        byte[] icc = ReadProfile(TestImages.Icc.CompactDisplayP3V4)[..200];
        Assert.False(new IccProfile(icc).CheckIsValid());
        using Image<Rgb24> image = CreateImage<Rgb24>(icc);
        using MemoryStream stream = new();

        Assert.Throws<ImageFormatException>(() => image.Save(stream, JxlEncoderRoundTrip.LosslessFastest));
        Assert.Equal(0, stream.Length);
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
        Assert.NotNull(jxlMetadata.EmbeddedIccProfile);
        Assert.Equal(icc, jxlMetadata.EmbeddedIccProfile.ToByteArray());
        Assert.Equal(icc, decoded.Metadata.IccProfile?.ToByteArray());
        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    // MagickReferenceDecoder drops metadata, so the profile is read through MagickImage itself.
    private static void AssertMagickKeepsProfile<TPixel>(byte[] icc)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = CreateImage<TPixel>(icc);
        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        using ImageMagick.MagickImage magick = new(encoded);
        Assert.Equal(icc, magick.GetColorProfile()?.ToByteArray());
        JxlEncoderRoundTrip.AssertMagickExact(image, encoded);
    }

    private static async Task AssertDjxlExactAsync<TPixel>(byte[] icc, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = CreateImage<TPixel>(icc);
        await JxlEncoderRoundTrip.AssertDjxlExactAsync(image, JxlEncoderRoundTrip.Encode(image), name, TestContext.Current.CancellationToken);
    }

    private static void AssertOurDecoderGetsSrgbEnum<TPixel>(byte[] icc)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = CreateImage<TPixel>(icc);

        using Image<TPixel> decoded = Image.Load<TPixel>(JxlEncoderRoundTrip.CreateConfiguration(), JxlEncoderRoundTrip.Encode(image));

        JxlMetadata jxlMetadata = decoded.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Null(jxlMetadata.EmbeddedIccProfile);
        Assert.Null(decoded.Metadata.IccProfile);
        JxlColorEncoding colorEncoding = jxlMetadata.ColorEncoding;
        Assert.NotNull(colorEncoding);
        Assert.Equal(typeof(TPixel) == typeof(L8) ? JxlColorSpace.Gray : JxlColorSpace.Rgb, colorEncoding.ColorSpace);
        Assert.Equal(JxlWhitePoint.D65, colorEncoding.WhitePoint);
        Assert.Equal(JxlPrimaries.Srgb, colorEncoding.Primaries);
        Assert.Null(colorEncoding.Gamma);
        Assert.Equal(JxlTransferFunction.Srgb, colorEncoding.TransferFunction);
        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    private enum Check
    {
        OurDecoderKeepsProfile,
        MagickKeepsProfile,
        DjxlExact,
        OurDecoderGetsSrgbEnum,
    }

    // One check of a profile on an image of the pixel type that the theory data names.
    private sealed record ProfileCheck(Check Check, byte[] Icc, string Name = null) : IJxlPixelTypeCall<Task>
    {
        public Task Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            switch (this.Check)
            {
                case Check.OurDecoderKeepsProfile:
                    AssertOurDecoderKeepsProfile<TPixel>(this.Icc);
                    break;
                case Check.MagickKeepsProfile:
                    AssertMagickKeepsProfile<TPixel>(this.Icc);
                    break;
                case Check.DjxlExact:
                    return AssertDjxlExactAsync<TPixel>(this.Icc, this.Name);
                default:
                    AssertOurDecoderGetsSrgbEnum<TPixel>(this.Icc);
                    break;
            }

            return Task.CompletedTask;
        }
    }
}
