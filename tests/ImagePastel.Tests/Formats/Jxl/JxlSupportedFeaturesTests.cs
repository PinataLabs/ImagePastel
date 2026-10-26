// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The frame features that the slices lifted one at a time. Since D14b the frame decoder throws no
/// <see cref="NotSupportedException"/>: it ignores the frame flags libjxl does not define (here), and
/// decodes the grey YCbCr frames that need three planes (JxlGreyYCbCrTests). The features that D10 and
/// D11 lifted decode here. The later ones are
/// checked against djxl elsewhere: patches on VarDCT frames (D12a) in JxlXybVarDctTests and the
/// conformance tests, splines (D12b) in JxlSplinesTests, noise (D12c) in JxlNoiseTests, the permuted TOC
/// (D13a) in JxlPermutedTocTests, passes (D13b) in JxlProgressivePassesTests, and DC frames (D13c) in
/// JxlDcFrameTests.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlSupportedFeaturesTests
{
    private const int Width = 16;
    private const int Height = 16;

    // libjxl ignores the frame flags it does not define (4, 8, 64 and 256 and up), so the frame decodes as
    // the same frame without them (D14b). The OSS-Fuzz inputs 5382562858532864 and 6569981946494976 set 4
    // and 64. The Ramp layer differs at every pixel. JxlTestLayers writes flags up to 272.
    [Theory]
    [InlineData(4UL)]
    [InlineData(8UL)]
    [InlineData(64UL)]
    [InlineData(256UL)]
    [InlineData(4UL | 8UL | 64UL)]
    [InlineData(4UL | 256UL)]
    public void Decode_UndefinedFrameFlag_IsIgnored(ulong flags)
    {
        JxlTestFrame plain = JxlTestFrame.Last(JxlTestLayers.Ramp);

        NpyArray expected = DecodeToNpy(plain);
        NpyArray actual = DecodeToNpy(plain with { Flags = flags });

        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.Data, actual.Data);
    }

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

    // The D11 feature, which threw before D11: XYB output in the BT.709, gamma, DCI and PQ transfer
    // functions. Each encodes the linear samples one by one, so each plane is the curve of the plane of
    // the same image with a linear transfer function. The curves are checked against libjxl's values in
    // JxlTransferFunctionsTests, and real pixels against djxl in JxlXybModularTests and JxlXybVarDctTests.
    [Theory]
    [InlineData(nameof(JxlTransferFunction.Bt709))]
    [InlineData(nameof(JxlTransferFunction.Dci))]
    [InlineData(nameof(JxlTransferFunction.Pq))]
    [InlineData("Gamma")] // A gamma of 1 / 2.2.
    public void Decode_XybOutputInATransferFunction_IsTheCurveOfTheLinearOutput(string transferFunction)
    {
        const uint gamma = 4545450;
        JxlTestFrame frame = JxlTestFrame.Last(JxlTestLayers.Ramp);
        JxlTestLayers layers = transferFunction == "Gamma"
            ? new JxlTestLayers(Width, Height) { Xyb = true, Gamma = gamma }
            : new JxlTestLayers(Width, Height) { Xyb = true, TransferFunction = Enum.Parse<JxlTransferFunction>(transferFunction) };
        NpyArray linear = DecodeToNpy(new JxlTestLayers(Width, Height) { Xyb = true, TransferFunction = JxlTransferFunction.Linear }, frame);
        NpyArray actual = DecodeToNpy(layers, frame);

        // PQ's 1.0 is 10,000 nits, and the image's intensity target is the default 255 nits.
        Func<float, float> curve = transferFunction switch
        {
            nameof(JxlTransferFunction.Bt709) => JxlTransferFunctions.Bt709EncodedFromDisplay,
            nameof(JxlTransferFunction.Dci) => v => JxlTransferFunctions.GammaEncodedFromDisplay(v, 1f / 2.6f),
            nameof(JxlTransferFunction.Pq) => v => JxlTransferFunctions.PqEncodedFromDisplay(v, JxlTransferFunctions.PqScaleTo10000Nits(255)),
            _ => v => JxlTransferFunctions.GammaEncodedFromDisplay(v, (float)(gamma * (1.0 / 1e7))),
        };

        Assert.Equal(new[] { 1, Height, Width, 4 }, actual.Shape);
        Assert.Equal(linear.Shape, actual.Shape);
        for (int i = 0; i < linear.Data.Length; i++)
        {
            float expected = i % 4 == 3 ? linear.Data[i] : curve(linear.Data[i]);
            Assert.Equal(expected, actual.Data[i]);
        }
    }

    // HLG output applies the OOTF to the three planes together before the curve, so it is not a curve of
    // each sample. It decodes; JxlXybVarDctTests checks it against djxl.
    [Fact]
    public void Decode_XybOutputInHlg_Decodes()
    {
        NpyArray actual = DecodeToNpy(new JxlTestLayers(Width, Height) { Xyb = true, TransferFunction = JxlTransferFunction.Hlg }, JxlTestFrame.Last(JxlTestLayers.Ramp));

        Assert.Equal(new[] { 1, Height, Width, 4 }, actual.Shape);
        Assert.All(actual.Data, v => Assert.True(float.IsFinite(v)));
    }

    // The other D11 feature: a PQ or HLG image gets a pixel profile, which it did not before D11. An RGB
    // encoding that a cicp tag names, as here, gets libjxl's 3D LUT to Lab. JxlColorEncodingTests compares
    // that LUT, and the table curve of the other encodings, with djxl's.
    [Theory]
    [InlineData(nameof(JxlTransferFunction.Pq))]
    [InlineData(nameof(JxlTransferFunction.Hlg))]
    public void Load_PqOrHlg_HasA3DLutProfile(string transferFunction)
    {
        using MemoryStream stream = new(new JxlTestLayers(Width, Height) { TransferFunction = Enum.Parse<JxlTransferFunction>(transferFunction) }.Add(JxlTestFrame.Last(JxlTestLayers.Ramp)).ToArray());

        using Image<Rgba32> image = new JxlDecoder().Decode<Rgba32>(Configuration.Default, stream, TestContext.Current.CancellationToken);

        IccProfile profile = image.Metadata.IccProfile;
        Assert.NotNull(profile);
        Assert.Equal(IccColorSpaceType.CieLab, profile.Header.ProfileConnectionSpace);
        Assert.True(profile.TryGetTag(IccProfileTag.AToB0, out IccTagDataEntry _));
        Assert.True(profile.CheckIsValid());
    }

    [Fact]
    public void Decode_SkipAdaptiveDcSmoothingOnModularFrame_Decodes()
    {
        // libjxl reads the flag only for VarDCT frames, so a Modular frame decodes as it would without it.
        byte[] codestream = new JxlTestLayers(Width, Height).Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Flags = 128 }).ToArray();
        using MemoryStream stream = new(codestream);

        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, TestContext.Current.CancellationToken);
        Assert.Single(decoded.Frames);
    }

    private static NpyArray DecodeToNpy(JxlTestFrame frame) => DecodeToNpy(new JxlTestLayers(Width, Height), frame);

    private static NpyArray DecodeToNpy(JxlTestLayers layers, JxlTestFrame frame)
    {
        using MemoryStream stream = new(layers.Add(frame).ToArray());
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, TestContext.Current.CancellationToken);
        return JxlConformanceImages.ToNpy(decoded);
    }
}
