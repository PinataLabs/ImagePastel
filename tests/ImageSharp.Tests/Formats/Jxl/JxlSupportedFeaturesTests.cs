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

    [Fact]
    public void Decode_VarDctFrame_IsNotSupported()
        => AssertNotSupported(new JxlTestLayers(Width, Height), JxlTestFrame.Last(JxlTestLayers.ColourA) with { Encoding = JxlFrameEncoding.VarDct }, "VarDCT");

    [Fact]
    public void Decode_XybVarDctFrame_IsNotSupported()
        => AssertNotSupported(new JxlTestLayers(Width, Height) { Xyb = true }, JxlTestFrame.Last(JxlTestLayers.ColourA) with { Encoding = JxlFrameEncoding.VarDct }, "VarDCT");

    [Fact]
    public void Decode_YCbCrFrame_IsNotSupported()
        => AssertNotSupported(new JxlTestLayers(Width, Height), JxlTestFrame.Last(JxlTestLayers.ColourA) with { YCbCr = true }, "YCbCr");

    [Theory]
    [InlineData(1, "noise")]
    [InlineData(16, "splines")]
    [InlineData(32, "DC frame")]
    [InlineData(128, "adaptive DC smoothing")]
    [InlineData(130, "adaptive DC smoothing")] // with patches, which are supported
    [InlineData(4, "frame flags")]
    [InlineData(8, "frame flags")]
    [InlineData(64, "frame flags")]
    [InlineData(256, "frame flags")]
    public void Decode_FrameFlag_IsNotSupported(ulong flags, string feature)
        => AssertNotSupported(new JxlTestLayers(Width, Height), JxlTestFrame.Last(JxlTestLayers.ColourA) with { Flags = flags }, feature);

    private static void AssertNotSupported(JxlTestLayers layers, JxlTestFrame frame, string feature)
    {
        byte[] codestream = layers.Add(frame).ToArray();
        using MemoryStream stream = new(codestream);

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None));
        Assert.Contains(feature, ex.Message, StringComparison.Ordinal);
    }
}
