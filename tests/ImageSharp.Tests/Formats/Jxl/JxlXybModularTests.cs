// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lossy Modular files, which are XYB-encoded: the XYB stage, the output encoding and the sinks. The files
/// are made by <c>cjxl -m 1 -d 1</c> (tests/Images/Input/Jxl/Generated/README.md), and the planes are
/// compared with djxl's float output at the lossy thresholds. The measured errors are noted on each test.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlXybModularTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured: worst channel RMSE 3.6e-7, peak 7.2e-6.
    [Fact]
    public void DecodeToPlanes_Rgb_MatchesDjxl()
        => AssertPlanesMatchDjxl("xyb_modular_rgb").Dispose();

    // Measured: worst channel RMSE 9.6e-8, peak 3.6e-7.
    [Fact]
    public void DecodeToPlanes_Grey_GivesOnePlaneMatchingDjxl()
    {
        // djxl's reference has one channel: an XYB grey image drops to one plane after the XYB stage.
        using JxlDecodedImage decoded = AssertPlanesMatchDjxl("xyb_modular_grey");

        Assert.Single(decoded.Frames[0].Planes);
    }

    [Fact]
    public void Load_Grey_GivesAGreyscalePixelType()
    {
        using Image image = Image.Load(CreateConfiguration(), InputPath("xyb_modular_grey"));

        Assert.IsType<Image<L8>>(image);
    }

    private static JxlDecodedImage AssertPlanesMatchDjxl(string name)
    {
        NpyArray expected = NpyArray.Load(ReferencePath(name));

        using FileStream stream = File.OpenRead(InputPath(name));
        JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        try
        {
            JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded));
            return decoded;
        }
        catch
        {
            decoded.Dispose();
            throw;
        }
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }

    private static string InputPath(string name) => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl");

    private static string ReferencePath(string name) => Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy");
}
