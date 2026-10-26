// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Chroma subsampling (D10e) and the JPEG transcodes: <c>cjxl</c>'s lossless transcodes of small JPEGs at
/// 4:4:4, 4:2:0, 4:2:2, 4:4:0 and grey. Each is a VarDCT YCbCr frame with a raw DCT8 quantization table.
/// The subsampled ones take the chroma-upsampling stage, and D8's subsampled DC, AC metadata and group
/// decode. Hand-built Modular frames check chroma subsampling on Modular YCbCr, which <c>cjxl</c> never
/// writes, against <c>djxl</c> at test time.
/// </summary>
/// <remarks>
/// <para>
/// The 67 x 45 files have an odd size, which is not a multiple of a block, nor of the 16 pixels of a
/// subsampled block. <c>jpeg_420_groups</c> is 288 x 163, two groups. <c>jpeg_mixed</c> is the repo's
/// <c>Jpg/baseline/jpeg422.jpg</c>, which <c>cjxl</c> writes in the "mixed" mode: Y is subsampled
/// vertically, and Cb and Cr are not. <c>jpeg_444_smoothing</c> is a 4:4:4 transcode whose
/// skip-adaptive-DC-smoothing flag is cleared, so that adaptive DC smoothing runs on YCbCr; the smoothing
/// moves <c>djxl</c>'s output by an RMSE of 7.4e-4, so a decoder that skipped it would fail.
/// </para>
/// <para>
/// The JPEG conformance vectors allow an RMSE of 1e-5, ten times less than the general lossy rule, so these
/// files are held to that too.
/// </para>
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlChromaSubsamplingTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured on x64 against djxl v0.12.0 (AVX2), worst channel RMSE and peak error: jpeg_444 3.1e-7 and
    // 2.1e-6, jpeg_420 2.9e-7 and 2.1e-6, jpeg_422 2.9e-7 and 2.2e-6, jpeg_440 3.0e-7 and 2.1e-6, jpeg_grey
    // 2.6e-7 and 2.0e-6, jpeg_420_groups 2.7e-7 and 2.8e-6, jpeg_mixed 9.2e-8 and 4.8e-7, and
    // jpeg_444_smoothing 4.2e-8 and 1.2e-7. libjxl fuses the multiplies and adds of the chroma upsampling
    // and the YCbCr conversion on AVX2, and this port does not (ADR 0004).
    [Theory]
    [InlineData("jpeg_444")]
    [InlineData("jpeg_420")]
    [InlineData("jpeg_422")]
    [InlineData("jpeg_440")]
    [InlineData("jpeg_grey")]
    [InlineData("jpeg_420_groups")]
    [InlineData("jpeg_mixed")]
    [InlineData("jpeg_444_smoothing")]
    public void DecodeToPlanes_MatchesDjxl(string name)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        JxlConformanceFrameResult result = Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, actual, JxlLossyComparer.JpegRmsError));
        TestContext.Current.TestOutputHelper?.WriteLine($"{name}: worst channel RMSE {result.WorstChannelRmse:G2}, peak {result.PeakError:G2}");
    }

    [Theory]
    [InlineData("jpeg_444")]
    [InlineData("jpeg_420")]
    [InlineData("jpeg_422")]
    [InlineData("jpeg_440")]
    [InlineData("jpeg_420_groups")]
    [InlineData("jpeg_mixed")]
    [InlineData("jpeg_444_smoothing")]
    public void Load_Rgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);

        using Image image = Image.Load(CreateConfiguration(), InputPath(name));
        Image<Rgb24> rgb = Assert.IsType<Image<Rgb24>>(image);

        JxlConformanceImages.AssertWithinOneStepOfTheClampedReference(expected, JxlConformanceImages.ToNpy(rgb, 3));
    }

    // The grey JPEG transcodes to a YCbCr frame of a grey image, which loads as grey.
    [Fact]
    public void Load_Grey_GivesL8WithinOneStepOfTheClampedReference()
    {
        NpyArray expected = LoadReference("jpeg_grey");

        using Image image = Image.Load(CreateConfiguration(), InputPath("jpeg_grey"));
        Image<L8> grey = Assert.IsType<Image<L8>>(image);

        JxlConformanceImages.AssertWithinOneStepOfTheClampedReference(expected, JxlConformanceImages.ToNpy(grey, 1));
    }

    // A Modular YCbCr frame with chroma subsampling, which cjxl never writes: the hand-built RGBA ramp
    // layer, whose samples depend on the position, decoded at each channel's subsampled size. The modes
    // are Cb, Y, Cr; (1, 0, 1) and (2, 3, 0) subsample Y. The 21 x 13 frame is not a multiple of 2 either
    // way. Measured on x64 against djxl v0.12.0 (AVX2): worst channel RMSE 6.2e-8, peak 2.4e-7.
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(0, 2, 0)]
    [InlineData(0, 3, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 3, 0)]
    public async Task DecodeToPlanes_ModularSubsampledFrame_MatchesDjxl(int cb, int y, int cr)
    {
        byte[] codestream = new JxlTestLayers(21, 13).Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { YCbCr = true, ChromaSubsampling = new[] { cb, y, cr } }).ToArray();
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlChromaSubsamplingTests));
        string input = Path.Combine(directory, $"modular_{cb}{y}{cr}.jxl");
        string output = Path.Combine(directory, $"modular_{cb}{y}{cr}.npy");
        await File.WriteAllBytesAsync(input, codestream, CancellationToken);

        await JxlTools.RunAsync("djxl", CancellationToken, input, output);

        using MemoryStream stream = new(codestream);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        Assert.Single(JxlLossyComparer.AssertWithinThresholds(NpyArray.Load(output), JxlConformanceImages.ToNpy(decoded)));
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }

    private static string InputPath(string name) => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl");

    private static NpyArray LoadReference(string name) => NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy"));
}
