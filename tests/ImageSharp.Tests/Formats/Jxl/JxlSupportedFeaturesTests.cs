// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The frame features of later slices: each is rejected on its own with
/// <see cref="NotSupportedException"/>, so that a slice can lift one rejection at a time.
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

    [Fact]
    public void Decode_SkipAdaptiveDcSmoothingOnModularFrame_Decodes()
    {
        // libjxl reads the flag only for VarDCT frames, so a Modular frame decodes as it would without it.
        byte[] codestream = new JxlTestLayers(Width, Height).Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Flags = 128 }).ToArray();
        using MemoryStream stream = new(codestream);

        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
        Assert.Single(decoded.Frames);
    }

    private static void AssertNotSupported(JxlTestLayers layers, JxlTestFrame frame, string feature)
    {
        byte[] codestream = layers.Add(frame).ToArray();
        using MemoryStream stream = new(codestream);

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None));
        Assert.Contains(feature, ex.Message, StringComparison.Ordinal);
    }
}
