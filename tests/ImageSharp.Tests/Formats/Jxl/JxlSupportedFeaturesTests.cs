// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The frame features of later slices: each is rejected on its own with
/// <see cref="NotSupportedException"/>, so that a slice can lift one rejection at a time. The features
/// that D10 lifted decode.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlSupportedFeaturesTests
{
    private const int Width = 16;
    private const int Height = 16;

    [Theory]
    [InlineData(1, "noise")]
    [InlineData(16, "splines")]
    [InlineData(32, "DC frame")]
    [InlineData(4, "frame flags")]
    [InlineData(8, "frame flags")]
    [InlineData(64, "frame flags")]
    [InlineData(256, "frame flags")]
    public void Decode_FrameFlag_IsNotSupported(ulong flags, string feature)
        => AssertNotSupported(new JxlTestLayers(Width, Height), JxlTestFrame.Last(JxlTestLayers.ColourA) with { Flags = flags }, feature);

    // Patches on a VarDCT frame come with D12; on a Modular frame they decode (JxlLayersTests).
    [Fact]
    public void Decode_VarDctFrameWithPatches_IsNotSupported()
        => AssertNotSupported(new JxlTestLayers(Width, Height), JxlTestFrame.Last(JxlTestLayers.ColourA) with { Encoding = JxlFrameEncoding.VarDct, Flags = 2 }, "Patches on VarDCT frames");

    // The D10 features, which threw until D10, decode: YCbCr, chroma subsampling (the modes are Cb, Y,
    // Cr), colour upsampling and extra-channel upsampling. ColourA is one colour, and upsampling and
    // chroma upsampling keep a flat plane flat, so each frame gives the planes of the same frame without
    // them. JxlYCbCrTests, JxlChromaSubsamplingTests and JxlUpsamplingTests check real pixels against djxl.
    [Theory]
    [InlineData(true, 0, 0, 0, 1u, 0u)]
    [InlineData(true, 1, 0, 1, 1u, 0u)]
    [InlineData(true, 2, 0, 2, 1u, 0u)]
    [InlineData(true, 3, 3, 0, 1u, 0u)]
    [InlineData(false, 0, 0, 0, 2u, 0u)]
    [InlineData(false, 0, 0, 0, 8u, 0u)]
    [InlineData(false, 0, 0, 0, 1u, 4u)]
    [InlineData(false, 0, 0, 0, 2u, 8u)]
    [InlineData(true, 1, 0, 1, 2u, 2u)]
    public void Decode_D10Feature_Decodes(bool yCbCr, int cb, int y, int cr, uint upsampling, uint extraChannelUpsampling)
    {
        JxlTestFrame plain = JxlTestFrame.Last(JxlTestLayers.ColourA) with { YCbCr = yCbCr };
        JxlTestFrame frame = plain with { ChromaSubsampling = yCbCr ? new[] { cb, y, cr } : null, Upsampling = upsampling, ExtraChannelUpsampling = extraChannelUpsampling };

        NpyArray expected = DecodeToNpy(plain);
        NpyArray actual = DecodeToNpy(frame);

        Assert.Equal(new[] { 1, Height, Width, 4 }, actual.Shape);
        Assert.Equal(expected.Shape, actual.Shape);
        for (int i = 0; i < expected.Data.Length; i++)
        {
            Assert.Equal(expected.Data[i], actual.Data[i], 1e-6F);
        }
    }

    [Fact]
    public void Decode_SkipAdaptiveDcSmoothingOnModularFrame_Decodes()
    {
        // libjxl reads the flag only for VarDCT frames, so a Modular frame decodes as it would without it.
        byte[] codestream = new JxlTestLayers(Width, Height).Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Flags = 128 }).ToArray();
        using MemoryStream stream = new(codestream);

        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
        Assert.Single(decoded.Frames);
    }

    private static NpyArray DecodeToNpy(JxlTestFrame frame)
    {
        using MemoryStream stream = new(new JxlTestLayers(Width, Height).Add(frame).ToArray());
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
        return JxlConformanceImages.ToNpy(decoded);
    }

    private static void AssertNotSupported(JxlTestLayers layers, JxlTestFrame frame, string feature)
    {
        byte[] codestream = layers.Add(frame).ToArray();
        using MemoryStream stream = new(codestream);

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None));
        Assert.Contains(feature, ex.Message, StringComparison.Ordinal);
    }
}
