// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

[Trait("Format", "Jxl")]
public class JxlDecoderTests
{
    private static string DeltaPalettePath => JxlConformanceVector.Load("delta_palette").CommittedInputPath;

    private static string GeneratedDirectory => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated");

    private static string PremultipliedAlphaPath => Path.Combine(GeneratedDirectory, "premultiplied_alpha.jxl");

    [Fact]
    public void Identify_ReportsSizeAndBitDepth()
    {
        using FileStream stream = File.OpenRead(DeltaPalettePath);

        IImageInfo info = new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None);

        Assert.Equal(555, info.Width);
        Assert.Equal(751, info.Height);
        Assert.Equal(24, info.PixelType.BitsPerPixel);
    }

    [Fact]
    public void Identify_ThroughImage_UsesTheDetector()
    {
        IImageInfo info = Image.Identify(CreateConfiguration(), DeltaPalettePath, out IImageFormat format);

        Assert.Equal(JxlFormat.Instance, format);

        Assert.Equal(555, info.Width);
        Assert.Equal(751, info.Height);
    }

    // The Magick cross-checks of the conformance inputs are in JxlMagickCrossCheckTests.
    [Fact]
    public void Load_PremultipliedAlpha_GivesStraightAlpha()
    {
        // The straight PNG holds the pixels before they were premultiplied and rounded to 16 bits.
        // Dividing by alpha again is off by at most 2.5 16-bit steps per channel where alpha >= 1/4.
        using Image<Rgba64> image = Image.Load<Rgba64>(CreateConfiguration(), PremultipliedAlphaPath);
        using Image<Rgba64> expected = Image.Load<Rgba64>(Path.Combine(GeneratedDirectory, "premultiplied_alpha_straight.png"));

        ImageComparer.Tolerant(0F, perPixelManhattanThreshold: 9).VerifySimilarity(expected, image);
    }

    [Fact]
    public void Load_PremultipliedAlpha_WhereAlphaIsZero_GivesBlack()
    {
        // Alpha is 0 in the first 8 columns.
        using Image<RgbaVector> image = Image.Load<RgbaVector>(CreateConfiguration(), PremultipliedAlphaPath);

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Assert.Equal(new RgbaVector(0, 0, 0, 0), image[x, y]);
            }
        }
    }

    [Fact]
    public void DecodeToPlanes_PremultipliedAlpha_KeepsTheColoursAsCoded()
    {
        using FileStream stream = File.OpenRead(PremultipliedAlphaPath);
        using JxlDecodedImage planes = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
        using Image<Rgba64> reference = Image.Load<Rgba64>(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", "premultiplied_alpha.png"));

        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = 0; x < reference.Width; x++)
            {
                Rgba64 expected = reference[x, y];
                Assert.Equal(expected.R, Quantize16(planes.Frames[0].Planes[0].DangerousGetRowSpan(y)[x]));
                Assert.Equal(expected.G, Quantize16(planes.Frames[0].Planes[1].DangerousGetRowSpan(y)[x]));
                Assert.Equal(expected.B, Quantize16(planes.Frames[0].Planes[2].DangerousGetRowSpan(y)[x]));
                Assert.Equal(expected.A, Quantize16(planes.Frames[0].Planes[3].DangerousGetRowSpan(y)[x]));
            }
        }
    }

    [Theory]
    [InlineData("greyscale", 1)] // 8-bit grey
    [InlineData("greyscale_alpha", 2)] // 16-bit grey and alpha
    public void DecodeToPlanes_Greyscale_GivesOneGreyPlane(string name, int planeCount)
    {
        using FileStream stream = File.OpenRead(Path.Combine(GeneratedDirectory, name + ".jxl"));
        using JxlDecodedImage planes = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
        using Image<La32> reference = Image.Load<La32>(GeneratedReferencePath(name));

        Assert.Equal(planeCount, planes.Frames[0].Planes.Count);
        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = 0; x < reference.Width; x++)
            {
                La32 expected = reference[x, y];
                Assert.Equal(expected.L, Quantize16(planes.Frames[0].Planes[0].DangerousGetRowSpan(y)[x]));
                if (planeCount == 2)
                {
                    Assert.Equal(expected.A, Quantize16(planes.Frames[0].Planes[1].DangerousGetRowSpan(y)[x]));
                }
            }
        }
    }

    [Fact]
    public void Load_Greyscale_GivesL8MatchingDjxl()
    {
        using Image image = Image.Load(CreateConfiguration(), Path.Combine(GeneratedDirectory, "greyscale.jxl"));
        using Image<L8> reference = Image.Load<L8>(GeneratedReferencePath("greyscale"));

        Image<L8> grey = Assert.IsType<Image<L8>>(image);
        ImageComparer.Exact.VerifySimilarity(reference, grey);
    }

    [Fact]
    public void Load_GreyscaleAlpha_GivesLa32MatchingDjxl()
    {
        using Image image = Image.Load(CreateConfiguration(), Path.Combine(GeneratedDirectory, "greyscale_alpha.jxl"));
        using Image<La32> reference = Image.Load<La32>(GeneratedReferencePath("greyscale_alpha"));

        Image<La32> grey = Assert.IsType<Image<La32>>(image);
        ImageComparer.Exact.VerifySimilarity(reference, grey);
    }

    [Fact]
    public void Load_Greyscale_AsRgb24_GivesTheGreyInEveryComponent()
    {
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), Path.Combine(GeneratedDirectory, "greyscale.jxl"));
        using Image<L8> reference = Image.Load<L8>(GeneratedReferencePath("greyscale"));

        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = 0; x < reference.Width; x++)
            {
                byte expected = reference[x, y].PackedValue;
                Assert.Equal(new Rgb24(expected, expected, expected), image[x, y]);
            }
        }
    }

    [Theory]
    [InlineData("greyscale_squeeze")]
    [InlineData("greyscale_squeeze_wide")] // squeezed channels in the DC groups
    public void Load_GreyscaleSqueeze_MatchesDjxl(string name)
    {
        using Image image = Image.Load(CreateConfiguration(), Path.Combine(GeneratedDirectory, name + ".jxl"));
        using Image<L8> reference = Image.Load<L8>(GeneratedReferencePath(name));

        Image<L8> grey = Assert.IsType<Image<L8>>(image);
        ImageComparer.Exact.VerifySimilarity(reference, grey);
    }

    [Fact]
    public void Load_RgbaSqueeze_MatchesDjxl()
    {
        using Image image = Image.Load(CreateConfiguration(), Path.Combine(GeneratedDirectory, "rgba_squeeze.jxl"));
        using Image<Rgba32> reference = Image.Load<Rgba32>(GeneratedReferencePath("rgba_squeeze"));

        Image<Rgba32> rgba = Assert.IsType<Image<Rgba32>>(image);
        ImageComparer.Exact.VerifySimilarity(reference, rgba);
    }

    [Fact]
    public void DecodeToPlanes_GreyscaleSqueeze_MatchesDjxl()
    {
        using FileStream stream = File.OpenRead(Path.Combine(GeneratedDirectory, "greyscale_squeeze.jxl"));
        using JxlDecodedImage planes = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
        using Image<L16> reference = Image.Load<L16>(GeneratedReferencePath("greyscale_squeeze"));

        Assert.Equal(1, planes.Frames[0].Planes.Count);
        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = 0; x < reference.Width; x++)
            {
                Assert.Equal(reference[x, y].PackedValue, Quantize16(planes.Frames[0].Planes[0].DangerousGetRowSpan(y)[x]));
            }
        }
    }

    [Theory]
    [InlineData("alpha_nonpremultiplied", 48)] // 12-bit RGB and alpha
    [InlineData("alpha_triangles", 36)] // 9-bit RGB and alpha
    public void Identify_CountsTheAlphaBits(string id, int bitsPerPixel)
    {
        using FileStream stream = File.OpenRead(JxlConformanceVector.Load(id).CommittedInputPath);

        IImageInfo info = new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None);

        Assert.Equal(1024, info.Width);
        Assert.Equal(1024, info.Height);
        Assert.Equal(bitsPerPixel, info.PixelType.BitsPerPixel);
    }

    // The extra channel types of libjxl's ExtraChannel enum that the decoder drops from the pixels. Black
    // is dropped too, as alpha_triangles has no CMYK profile.
    [Theory]
    [InlineData(1)] // depth
    [InlineData(3)] // selection mask
    [InlineData(4)] // black
    [InlineData(5)] // CFA
    [InlineData(6)] // thermal
    [InlineData(16)] // optional
    public void Decode_ExtraChannelThatIsDropped_GivesTheColourAndOpaqueAlpha(uint type)
    {
        using MemoryStream stream = new(AlphaTrianglesWithExtraChannel(type));
        using Image<Rgba64> image = new JxlDecoder().Decode<Rgba64>(Configuration.Default, stream, CancellationToken.None);

        AssertAlphaTrianglesColourWithOpaqueAlpha(image);
    }

    // The generated files are one colour each: see make_extra_channels.py.
    [Theory]
    [InlineData("depth", 255)] // (200, 100, 50) with depth 7: the depth is dropped
    [InlineData("two_alpha_a", 128)] // (200, 100, 50) with alphas 128 and 255: the second alpha is dropped
    public void Load_ExtraChannelsAfterTheFirstAlpha_AreDropped(string name, byte alpha)
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), Path.Combine(GeneratedDirectory, name + ".jxl"));

        Assert.Equal(16, image.Width);
        Assert.Equal(16, image.Height);
        Assert.All(Pixels(image), p => Assert.Equal(new Rgba32(200, 100, 50, alpha), p));
    }

    [Fact]
    public void Decode_SpotColourWithSolidityZero_LeavesTheColourUnchanged()
    {
        using MemoryStream stream = new(AlphaTrianglesWithExtraChannel(2, spotSolidity: 0));
        using Image<Rgba64> image = new JxlDecoder().Decode<Rgba64>(Configuration.Default, stream, CancellationToken.None);

        AssertAlphaTrianglesColourWithOpaqueAlpha(image);
    }

    [Fact]
    public void Load_Spot_ChoosesA16BitPixelType()
    {
        using Image image = Image.Load(CreateConfiguration(), JxlConformanceVector.Load("spot").CommittedInputPath);

        Assert.IsType<Image<Rgba64>>(image);
    }

    [Fact]
    public void Load_Spot_RendersBothInksAsDjxlDoes()
    {
        // The two spot channels overlap, so the comparison also checks they are rendered in stream order.
        string path = JxlConformanceVector.Load("spot").CommittedInputPath;
        using (FileStream stream = File.OpenRead(path))
        {
            using JxlDecodedImage planes = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
            Assert.True(CountOverlap(planes.Frames[0].Planes[4], planes.Frames[0].Planes[5]) > 0);
        }

        using Image<Rgba64> image = Image.Load<Rgba64>(CreateConfiguration(), path);
        using Image<Rgba64> reference = Image.Load<Rgba64>(GeneratedReferencePath("spot"));

        ImageComparer.Exact.VerifySimilarity(reference, image);
    }

    [Fact]
    public async Task Load_CmykLayers_ConvertsTheReferencePlanesNaively()
    {
        // The conformance reference holds the planes as coded: C, M, Y, K and alpha, where 0 is full ink.
        // The expected pixel is R = C K, G = M K, B = Y K with the alpha kept (issue #111), quantised.
        JxlConformanceVector vector = JxlConformanceVector.Load("cmyk_layers");
        NpyArray planes = await JxlConformanceData.LoadReferenceImageAsync(vector, TestContext.Current.CancellationToken);
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), vector.CommittedInputPath);

        Assert.Equal(new[] { 1, image.Height, image.Width, 5 }, planes.Shape);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int i = ((y * image.Width) + x) * 5;
                float k = planes.Data[i + 3];
                Rgba32 expected = new(Quantize8(planes.Data[i] * k), Quantize8(planes.Data[i + 1] * k), Quantize8(planes.Data[i + 2] * k), Quantize8(planes.Data[i + 4]));
                Rgba32 actual = image[x, y];

                // Within one quantisation step.
                Assert.InRange(actual.R - expected.R, -1, 1);
                Assert.InRange(actual.G - expected.G, -1, 1);
                Assert.InRange(actual.B - expected.B, -1, 1);
                Assert.InRange(actual.A - expected.A, -1, 1);
            }
        }
    }

    [Fact]
    public async Task Load_PatchesLossless_MatchesTheReference()
    {
        JxlConformanceVector vector = JxlConformanceVector.Load("patches_lossless");
        NpyArray planes = await JxlConformanceData.LoadReferenceImageAsync(vector, TestContext.Current.CancellationToken);
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), vector.CommittedInputPath);

        Assert.Equal(new[] { 1, image.Height, image.Width, 4 }, planes.Shape);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int i = ((y * image.Width) + x) * 4;
                Rgba32 expected = new(Quantize8(planes.Data[i]), Quantize8(planes.Data[i + 1]), Quantize8(planes.Data[i + 2]), Quantize8(planes.Data[i + 3]));
                Rgba32 actual = image[x, y];

                // Within one quantisation step.
                Assert.InRange(actual.R - expected.R, -1, 1);
                Assert.InRange(actual.G - expected.G, -1, 1);
                Assert.InRange(actual.B - expected.B, -1, 1);
                Assert.InRange(actual.A - expected.A, -1, 1);
            }
        }
    }

    [Fact]
    public void Load_CmykLayers_ChoosesRgba32AndHasNoPixelProfile()
    {
        using Image image = Image.Load(CreateConfiguration(), JxlConformanceVector.Load("cmyk_layers").CommittedInputPath);

        Assert.IsType<Image<Rgba32>>(image);
        Assert.Null(image.Metadata.IccProfile);
        Assert.Equal(IccColorSpaceType.Cmyk, image.Metadata.GetFormatMetadata(JxlFormat.Instance).EmbeddedIccProfile.Header.DataColorSpace);
    }

    // A dim_shift of 3 makes the extra channel 8x smaller, upsampled 8x on decode, which decodes since
    // D10b. alpha_triangles' frame then reads its alpha as a 128 x 128 channel, as djxl does.
    [Fact]
    public async Task DecodeToPlanes_ExtraChannelWithDimShift_MatchesDjxl()
    {
        byte[] codestream = AlphaTrianglesWithExtraChannel(1, dimShift: 3);
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlDecoderTests));
        string input = Path.Combine(directory, "alpha_dim_shift_3.jxl");
        string output = Path.Combine(directory, "alpha_dim_shift_3.npy");
        await File.WriteAllBytesAsync(input, codestream, TestContext.Current.CancellationToken);

        await JxlTools.RunAsync("djxl", TestContext.Current.CancellationToken, input, output);

        NpyArray expected = NpyArray.Load(output);
        using MemoryStream stream = new(codestream);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, TestContext.Current.CancellationToken);
        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
    }

    /// <summary>
    /// Returns alpha_triangles' frame behind a hand-written header that gives its one extra channel the
    /// type, name and dim_shift. The header is 1024 x 1024 with 9-bit samples and the default colour
    /// encoding, as alpha_triangles' own.
    /// </summary>
    /// <param name="type">The extra channel type, as libjxl's ExtraChannel enum.</param>
    /// <param name="name">The extra channel name, up to 15 bytes of UTF-8.</param>
    /// <param name="dimShift">The dim_shift, 0 or 3.</param>
    /// <param name="spotSolidity">The solidity of a red spot colour, as an F16.</param>
    /// <returns>The codestream.</returns>
    internal static byte[] AlphaTrianglesWithExtraChannel(uint type, string name = "", uint dimShift = 0, ushort spotSolidity = 0x3C00)
    {
        byte[] original = File.ReadAllBytes(JxlConformanceVector.Load("alpha_triangles").CommittedInputPath);
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        JxlTestBitWriter writer = new JxlTestBitWriter().Write(8, 0xFF).Write(8, 0x0A);
        writer.Bit(false).Write(2, 1).Write(13, 1023).Write(3, 1); // 1024 x 1024
        writer.Bit(false).Bit(false); // image metadata: not all default, no extra fields
        writer.Bit(false).Write(2, 3).Write(6, 8).Bit(true); // 9-bit integer samples, 16-bit buffers suffice
        writer.Write(2, 1); // one extra channel
        writer.Bit(false); // not all default
        if (type < 2)
        {
            writer.Write(2, type);
        }
        else
        {
            writer.Write(2, 2).Write(4, type - 2); // BitsOffset(4, 2)
        }

        writer.Bit(false).Write(2, 3).Write(6, 8); // 9-bit integer samples
        if (dimShift is not (0 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(dimShift), dimShift, "Expected 0 or 3.");
        }

        writer.Write(2, dimShift == 0 ? 0UL : 1UL); // dim_shift: Val(0) or Val(3)
        if (nameBytes.Length == 0)
        {
            writer.Write(2, 0);
        }
        else
        {
            writer.Write(2, 1).Write(4, (ulong)nameBytes.Length); // name length as Bits(4)
            foreach (byte b in nameBytes)
            {
                writer.Write(8, b);
            }
        }

        if (type == 0)
        {
            writer.Bit(false); // straight alpha
        }
        else if (type == 2)
        {
            writer.Write(16, 0x3C00).Write(16, 0).Write(16, 0).Write(16, spotSolidity); // red, as F16
        }
        else if (type == 5)
        {
            writer.Write(2, 0); // CFA channel 1
        }

        writer.Bit(false).Bit(true).Write(2, 0); // not XYB, default colour encoding, no extensions
        writer.Bit(true); // all-default transform data
        writer.ZeroPadToByte();
        return writer.ToArray().Concat(original.AsSpan(FrameOffset(original)).ToArray()).ToArray();
    }

    [Fact]
    public void Decode_ProducesTheSameSamplesAsDecodeToPlanes()
    {
        using FileStream stream = File.OpenRead(DeltaPalettePath);
        using JxlDecodedImage planes = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
        using FileStream stream2 = File.OpenRead(DeltaPalettePath);
        using Image<Rgb48> image = new JxlDecoder().Decode<Rgb48>(Configuration.Default, stream2, CancellationToken.None);

        Assert.Equal(planes.Width, image.Width);
        Assert.Equal(planes.Height, image.Height);
        Rgb48 pixel = image[100, 200];
        Assert.Equal(Quantize16(planes.Frames[0].Planes[0].DangerousGetRowSpan(200)[100]), pixel.R);
        Assert.Equal(Quantize16(planes.Frames[0].Planes[1].DangerousGetRowSpan(200)[100]), pixel.G);
        Assert.Equal(Quantize16(planes.Frames[0].Planes[2].DangerousGetRowSpan(200)[100]), pixel.B);
    }

    [Fact]
    public void Decode_Truncated_ThrowsInvalidImageContent()
    {
        byte[] data = File.ReadAllBytes(DeltaPalettePath).AsSpan(0, 1000).ToArray();
        using MemoryStream stream = new(data);

        Assert.Throws<InvalidImageContentException>(() => new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None));
    }

    [Fact]
    public void Decode_BadSignature_ThrowsInvalidImageContent()
    {
        using MemoryStream stream = new(new byte[] { 0xFF, 0x0B, 0, 0, 0, 0 });

        Assert.Throws<InvalidImageContentException>(() => new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None));
    }

    [Fact]
    public void Decode_Cancelled_ThrowsOperationCanceled()
    {
        using FileStream stream = File.OpenRead(DeltaPalettePath);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, cts.Token));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0x0A }, true)]
    [InlineData(new byte[] { 0xFF, 0x0A, 0x72, 0x17 }, true)]
    [InlineData(new byte[] { 0xFF, 0x0B }, false)]
    [InlineData(new byte[] { 0x0A, 0xFF }, false)]
    [InlineData(new byte[] { 0xFF }, false)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A }, true)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A, 0x00, 0x00 }, true)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0B }, false)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20 }, false)]
    public void Detector_RecognisesTheCodestreamAndTheContainer(byte[] header, bool expected)
    {
        IImageFormat format = new JxlImageFormatDetector().DetectFormat(header);

        Assert.Equal(expected, format == JxlFormat.Instance);
    }

    [Fact]
    public void OnlyTheDecoderSurfaceIsPublic()
    {
        // D15a (#308) makes the colour description and the enums public, and D15b (#311) the decoder surface.
        // The encoder, its options and every codec type stay internal until E6.
        string[] publicTypes = typeof(Image).Assembly.GetExportedTypes()
            .Where(t => t.Namespace?.StartsWith("SixLabors.ImageSharp.Formats.Jxl", StringComparison.Ordinal) == true)
            .Select(t => t.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "SixLabors.ImageSharp.Formats.Jxl.JxlColorEncoding",
                "SixLabors.ImageSharp.Formats.Jxl.JxlColorSpace",
                "SixLabors.ImageSharp.Formats.Jxl.JxlConfigurationModule",
                "SixLabors.ImageSharp.Formats.Jxl.JxlDecoder",
                "SixLabors.ImageSharp.Formats.Jxl.JxlExtraChannel",
                "SixLabors.ImageSharp.Formats.Jxl.JxlExtraChannelType",
                "SixLabors.ImageSharp.Formats.Jxl.JxlFormat",
                "SixLabors.ImageSharp.Formats.Jxl.JxlFrameMetadata",
                "SixLabors.ImageSharp.Formats.Jxl.JxlImageFormatDetector",
                "SixLabors.ImageSharp.Formats.Jxl.JxlMetadata",
                "SixLabors.ImageSharp.Formats.Jxl.JxlPrimaries",
                "SixLabors.ImageSharp.Formats.Jxl.JxlRenderingIntent",
                "SixLabors.ImageSharp.Formats.Jxl.JxlTransferFunction",
                "SixLabors.ImageSharp.Formats.Jxl.JxlWhitePoint",
            },
            publicTypes);
    }

    private static byte Quantize8(float value) => (byte)MathF.Round(Math.Clamp(value, 0F, 1F) * 255F);

    private static ushort Quantize16(float value) => (ushort)Math.Round(Math.Clamp(value, 0F, 1F) * 65535F);

    // The djxl output for a generated file: see the README next to the generated files.
    private static string GeneratedReferencePath(string name) => Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".png");

    private static Rgba32[] Pixels(Image<Rgba32> image)
    {
        Rgba32[] pixels = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }

    // Checks the image has alpha_triangles' colour, and is opaque.
    private static void AssertAlphaTrianglesColourWithOpaqueAlpha(Image<Rgba64> image)
    {
        using FileStream original = File.OpenRead(JxlConformanceVector.Load("alpha_triangles").CommittedInputPath);
        using Image<Rgb48> colour = new JxlDecoder().Decode<Rgb48>(Configuration.Default, original, CancellationToken.None);

        Assert.Equal(colour.Size(), image.Size());
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgb48 expected = colour[x, y];
                Assert.Equal(new Rgba64(expected.R, expected.G, expected.B, ushort.MaxValue), image[x, y]);
            }
        }
    }

    // The number of pixels where both planes are above 0.
    private static int CountOverlap(Buffer2D<float> a, Buffer2D<float> b)
    {
        int count = 0;
        for (int y = 0; y < a.Height; y++)
        {
            Span<float> rowA = a.DangerousGetRowSpan(y);
            Span<float> rowB = b.DangerousGetRowSpan(y);
            for (int x = 0; x < a.Width; x++)
            {
                count += rowA[x] > 0 && rowB[x] > 0 ? 1 : 0;
            }
        }

        return count;
    }

    // The first byte of the frame in a bare codestream with no ICC profile and no preview.
    private static int FrameOffset(byte[] codestream)
    {
        JxlCodecMetadata metadata = new();
        JxlBitReader reader = new(codestream, 2, codestream.Length - 2);
        JxlBundle.Read(reader, metadata.Size);
        JxlBundle.Read(reader, metadata.M);
        metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;
        JxlBundle.Read(reader, metadata.TransformData);
        reader.JumpToByteBoundary();
        int offset = 2 + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();
        return offset;
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
