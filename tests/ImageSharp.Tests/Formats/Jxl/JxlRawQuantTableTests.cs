// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Raw quantization tables (D10d), which only JPEG-transcoded files use: <c>jpeg_rgb</c> is
/// <c>cjxl</c>'s lossless transcode of the RGB-coded baseline JPEG <c>Jpg/baseline/jpeg-rgb.jpg</c>. An RGB
/// JPEG transcodes to a VarDCT frame with no colour transform, so the file needs neither YCbCr nor chroma
/// upsampling; its DCT8 table is raw, and the other tables come from the library.
/// </summary>
/// <remarks>
/// The JPEG conformance vectors allow an RMSE of 1e-5, ten times less than the general lossy rule, so this
/// file is held to that too.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlRawQuantTableTests
{
    private const string Name = "jpeg_rgb";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Header_IsVarDctWithNoColourTransform()
    {
        // cjxl puts a transcode in a container, beside the JPEG reconstruction box.
        byte[] codestream = JxlContainer.Read(File.ReadAllBytes(InputPath(Name)), readMetadata: false).Codestream;
        JxlFrameHeader frameHeader = JxlTestLayers.ReadFirstFrameHeader(codestream);

        Assert.Equal(JxlFrameEncoding.VarDct, frameHeader.Encoding);
        Assert.Equal(JxlColorTransform.None, frameHeader.ColorTransform);
    }

    // Measured on x64 against djxl v0.12.0 (AVX2): worst channel RMSE 8.4e-8, peak 2.0e-6.
    [Fact]
    public void DecodeToPlanes_MatchesDjxl()
    {
        NpyArray expected = LoadReference(Name);

        using FileStream stream = File.OpenRead(InputPath(Name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, actual, JxlLossyComparer.JpegRmsError));
    }

    [Fact]
    public void Load_Rgb24_IsWithinOneStepOfTheClampedReference()
    {
        NpyArray expected = LoadReference(Name);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(Name));
        NpyArray actual = JxlConformanceImages.ToNpy(image, 3);

        JxlConformanceFrameResult result = JxlConformanceComparer.Measure(expected, actual, JxlConformanceComparison.ClampToUnitRange).Single();
        Assert.True(result.PeakError <= (1.0 / 255) + 1e-6, $"Peak error {result.PeakError * 255:G4} steps");
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
