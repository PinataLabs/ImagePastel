// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// VarDCT frames with no XYB: the generated files made with
/// <c>benchmark_xl --codec=jxl:thunder:d1:gab0:epf0:noperc</c>, whose blocks are all DCT8, and
/// <c>vardct_strategies</c>, made at a slower effort with 4x8, 8x4 and 32x32 blocks, and
/// <c>vardct_identity_afv</c>, at effort 7 with every kind of block, Identity and AFV among them, an adaptive
/// quant field and non-zero chroma-from-luma maps, against djxl's float output.
/// </summary>
/// <remarks>
/// VarDCT output cannot match djxl bit for bit: libjxl fuses multiplies and adds on AVX2 and
/// approximates a reciprocal in the quantisation bias, and this port does neither (ADR 0004). So the
/// planes are compared at the lossy conformance thresholds, and each case records the error measured
/// when it was written.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlVarDctTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured on x64 against djxl v0.12.0 (AVX2): the worst channel RMSE and the peak error.
    [Theory]
    [InlineData("vardct_small")] // RMSE 8.1e-8, peak 3.6e-7
    [InlineData("vardct_groups")] // RMSE 8.3e-8, peak 4.2e-7
    [InlineData("vardct_alpha")] // RMSE 8.1e-8, peak 4.2e-7
    [InlineData("vardct_grey")] // RMSE 6.4e-8, peak 3.0e-7
    [InlineData("vardct_grey_alpha")] // RMSE 6.4e-8, peak 3.0e-7
    [InlineData("vardct_strategies")] // RMSE 2.7e-7, peak 1.7e-6
    [InlineData("vardct_identity_afv")] // RMSE 1.0e-7, peak 1.4e-6
    public void DecodeToPlanes_MatchesDjxl(string name)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, actual));
    }

    [Theory]
    [InlineData("vardct_small")]
    [InlineData("vardct_groups")]
    [InlineData("vardct_alpha")]
    [InlineData("vardct_strategies")]
    [InlineData("vardct_identity_afv")]
    public void Load_Rgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray reference = LoadReference(name);
        NpyArray expected = DropAlpha(reference);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name));
        NpyArray actual = JxlConformanceImages.ToNpy(image, 3);

        JxlConformanceImages.AssertWithinOneStepOfTheClampedReference(expected, actual);
    }

    // A grey image that is not XYB still codes three colour planes in a VarDCT frame, as libjxl's
    // always do; the output has one. Neither side of these is a multiple of 8.
    [Theory]
    [InlineData("vardct_grey", 1)]
    [InlineData("vardct_grey_alpha", 2)]
    public void Load_Grey_IsWithinOneStepOfTheClampedReference(string name, int channels)
    {
        byte[] codestream = File.ReadAllBytes(InputPath(name));
        JxlFrameHeader frameHeader = JxlTestLayers.ReadFirstFrameHeader(codestream);
        Assert.Equal(JxlFrameEncoding.VarDct, frameHeader.Encoding);
        Assert.False(frameHeader.NonserializedMetadata.M.XybEncoded);

        NpyArray expected = LoadReference(name);
        Assert.Equal(channels, expected.Shape[3]);

        using Image<La16> image = Image.Load<La16>(CreateConfiguration(), codestream);
        NpyArray actual = JxlConformanceImages.ToNpy(image, channels);

        JxlConformanceImages.AssertWithinOneStepOfTheClampedReference(expected, actual);
    }

    // The djxl match above covers an adaptive quant field and non-zero chroma-from-luma maps only if the
    // file has them. Read from the decoder's state after the DC groups: the quant field over the first
    // blocks of the varblocks, and how many of the 5 x 3 tiles of each map are not 0.
    [Fact]
    public void DecodeDcGroups_IdentityAfv_HasAnAdaptiveQuantFieldAndNonZeroChromaFromLuma()
    {
        using JxlDecodedDcGroups dc = JxlDecodedDcGroups.Decode(File.ReadAllBytes(InputPath("vardct_identity_afv")));

        Assert.Equal((4, 10), dc.QuantFieldRange());
        Assert.Equal(15, dc.State.Cmap.YtoXMap.Length);
        Assert.Equal(9, dc.State.Cmap.YtoXMap.Count(v => v != 0));
        Assert.Equal(2, dc.State.Cmap.YtoBMap.Count(v => v != 0));
    }

    [Fact]
    public void Decode_XybVarDct_Decodes()
    {
        // JxlXybVarDctTests compares XYB VarDCT output with djxl's; this is the same image as vardct_small.
        using FileStream stream = File.OpenRead(InputPath("vardct_xyb"));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);

        Assert.Equal(3, decoded.Frames[0].Planes.Count);
        Assert.Equal(64, decoded.Frames[0].Planes[0].Width);
        Assert.Equal(48, decoded.Frames[0].Planes[0].Height);
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

    private static NpyArray DropAlpha(NpyArray array)
    {
        int channels = array.Shape[3];
        if (channels == 3)
        {
            return array;
        }

        int pixels = array.Data.Length / channels;
        float[] data = new float[pixels * 3];
        for (int i = 0; i < pixels; i++)
        {
            Array.Copy(array.Data, i * channels, data, i * 3, 3);
        }

        return new NpyArray(new[] { array.Shape[0], array.Shape[1], array.Shape[2], 3 }, data);
    }
}
