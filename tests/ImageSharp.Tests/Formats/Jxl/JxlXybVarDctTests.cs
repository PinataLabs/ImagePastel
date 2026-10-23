// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// XYB VarDCT frames, D8's done-check: the files made with <c>cjxl -e 2 --gaborish=0 --epf=0</c>
/// (tests/Images/Input/Jxl/Generated/README.md), whose blocks are all DCT8 with the default quant tables;
/// and D9e's: the files made with <c>cjxl -e 7 --gaborish=0 --epf=0</c>, which mix the DCT sizes from 2x2
/// to 64x64, with their library quant tables and custom coefficient orders; and D9f's
/// <c>vardct_xyb_identity_afv</c>, at the same settings, which adds Identity and AFV blocks, an adaptive quant
/// field and a non-zero chroma-from-luma map; and D11a's <c>vardct_xyb_bt709</c> and <c>vardct_xyb_dci</c>,
/// which render to the BT.709 and DCI curves; and D11b's <c>vardct_xyb_pq</c> and <c>vardct_xyb_pq_1000</c>,
/// which render to PQ from intensity targets of 10,000 and 1,000 nits; and D11c's <c>vardct_xyb_hlg</c> and
/// <c>vardct_xyb_grey_hlg</c>, which render to HLG through the OOTF to scene light; and D12a's
/// <c>vardct_xyb_grey_patches</c>, a grey VarDCT frame with patches from an XYB reference frame. The planes are compared with
/// djxl's float output at the
/// lossy thresholds (docs/jxl/spec.md, "Tolerance for lossy output"), and each case records the error
/// measured when it was written.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlXybVarDctTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured on x64 against djxl v0.12.0 (AVX2): the worst channel RMSE and the peak error.
    [Theory]
    [InlineData("vardct_xyb_64", 3)] // RMSE 1.1e-6, peak 1.2e-5
    [InlineData("vardct_xyb_300x200_d05", 3)] // RMSE 1.1e-6, peak 2.6e-5
    [InlineData("vardct_xyb_600x400_d3", 3)] // RMSE 1.3e-6, peak 7.8e-5
    [InlineData("vardct_xyb_grey", 1)] // RMSE 2.6e-7, peak 1.6e-6
    [InlineData("vardct_xyb_alpha", 4)] // RMSE 3.0e-6, peak 4.1e-5
    [InlineData("vardct_xyb_icc", 3)] // RMSE 1.4e-6, peak 7.7e-6
    [InlineData("vardct_xyb_grey_d50", 1)] // RMSE 1.1e-7, peak 7.8e-7
    [InlineData("vardct_xyb_strategies", 3)] // RMSE 9.8e-7, peak 1.3e-5
    [InlineData("vardct_xyb_sizes_noisy", 3)] // RMSE 6.7e-7, peak 2.5e-5
    [InlineData("vardct_xyb_sizes_fine", 3)] // RMSE 7.2e-7, peak 3.0e-5
    [InlineData("vardct_xyb_sizes_smooth", 3)] // RMSE 6.1e-7, peak 1.0e-5
    [InlineData("vardct_xyb_identity_afv", 4)] // RMSE 4.1e-7, peak 2.8e-5
    [InlineData("vardct_xyb_e7", 3)] // Gaborish, EPF (1 iteration) and other block sizes: RMSE 1.1e-6, peak 1.7e-5
    [InlineData("vardct_xyb_bt709", 3)] // The BT.709 curve (Op709): RMSE 6.9e-7, peak 6.0e-6
    [InlineData("vardct_xyb_dci", 3)] // The DCI curve (OpGamma, 1 / 2.6), steep near 0: RMSE 2.7e-6, peak 1.1e-4
    [InlineData("vardct_xyb_pq", 3)] // PQ (OpPq), intensity target 10000 nits, steep near 0: RMSE 2.1e-5, peak 7.1e-4
    [InlineData("vardct_xyb_pq_1000", 3)] // PQ, intensity target 1000 nits, so linear 1.0 is PQ's 0.1: RMSE 3.1e-5, peak 1.3e-3
    [InlineData("vardct_xyb_hlg", 3)] // HLG (OpHlg), the OOTF with the BT.2100 luminances, steep in the shadows: RMSE 3.5e-6, peak 1.7e-4
    [InlineData("vardct_xyb_grey_hlg", 1)] // Grey HLG (OpHlg), the OOTF with the sRGB luminances: RMSE 1.8e-7, peak 2.5e-6
    [InlineData("vardct_xyb_grey_patches", 1)] // Grey, with patches from a three-plane XYB reference frame: RMSE 1.4e-7, peak 7.2e-7
    [InlineData("vardct_xyb_passes", 3)] // Three passes (D13b): RMSE 1.1e-6, peak 1.2e-5
    public void DecodeToPlanes_MatchesDjxl(string name, int planes)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);

        Assert.Equal(planes, decoded.Frames[0].Planes.Count);
        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
    }

    // Measured: the largest difference is 0.50 of one 8-bit step, which is the rounding, except for PQ,
    // which is steep near 0 and so magnifies the peaks above: 0.58 (vardct_xyb_pq) and 0.59
    // (vardct_xyb_pq_1000).
    [Theory]
    [InlineData("vardct_xyb_64")]
    [InlineData("vardct_xyb_300x200_d05")]
    [InlineData("vardct_xyb_600x400_d3")]
    [InlineData("vardct_xyb_grey")]
    [InlineData("vardct_xyb_strategies")]
    [InlineData("vardct_xyb_sizes_noisy")]
    [InlineData("vardct_xyb_sizes_fine")]
    [InlineData("vardct_xyb_sizes_smooth")]
    [InlineData("vardct_xyb_identity_afv")]
    [InlineData("vardct_xyb_bt709")]
    [InlineData("vardct_xyb_dci")]
    [InlineData("vardct_xyb_pq")] // 0.58 of one step
    [InlineData("vardct_xyb_pq_1000")] // 0.59 of one step
    [InlineData("vardct_xyb_hlg")]
    [InlineData("vardct_xyb_grey_hlg")]
    [InlineData("vardct_xyb_grey_patches")]
    public void LoadRgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name), out _);

        // A grey image is R = G = B, so its one reference channel is compared with each.
        int channels = expected.Shape[3];
        AssertWithinOneStep(expected, JxlConformanceImages.ToNpy(image, 3), 3, (i, c) => expected.Data[(i * channels) + (channels == 1 ? 0 : c)]);
    }

    // The samples are HLG-encoded, as djxl's are. HLG's square-root segment is steep in the shadows (its
    // slope from scene light is 3 / 2E, about 360 at E = 0.004), so it magnifies the differences of a few
    // 1e-7 in linear light that come from libjxl's FMA (ADR 0004) to several 16-bit steps. A sample whose
    // clamped reference is at least 0.1, where the slope is at most 15, must be within one 16-bit step; a
    // darker one within the lossy peak error.
    // Measured: vardct_xyb_hlg is within 0.93 of one step at or above 0.1; 14 of its 12288 samples, all
    // below 0.1, are more than one step away, the largest 8.1 steps at 0.0041. vardct_xyb_grey_hlg is within
    // 0.58 of one step everywhere.
    [Theory]
    [InlineData("vardct_xyb_hlg")]
    [InlineData("vardct_xyb_grey_hlg")]
    public void LoadRgb48_Hlg_IsWithinOneStepOfTheClampedReferenceOutsideTheShadows(string name)
    {
        const double step = 1.0 / 65535;
        const double shadows = 0.1;
        NpyArray expected = LoadReference(name);
        using Image<Rgb48> image = Image.Load<Rgb48>(CreateConfiguration(), InputPath(name));
        NpyArray actual = JxlConformanceImages.ToNpy(image, 3);

        // A grey image is R = G = B, so its one reference channel is compared with each.
        int channels = expected.Shape[3];
        Assert.Equal(expected.Shape[1], actual.Shape[1]);
        Assert.Equal(expected.Shape[2], actual.Shape[2]);
        int pixels = actual.Shape[1] * actual.Shape[2];
        double worst = 0;
        double worstInShadows = 0;
        int overOneStep = 0;
        for (int i = 0; i < pixels; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                double e = Math.Clamp(expected.Data[(i * channels) + (channels == 1 ? 0 : c)], 0F, 1F);
                double difference = Math.Abs(actual.Data[(i * 3) + c] - e) / step;
                overOneStep += difference > 1 + 1e-4 ? 1 : 0;
                if (e >= shadows)
                {
                    worst = Math.Max(worst, difference);
                }
                else
                {
                    worstInShadows = Math.Max(worstInShadows, difference);
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest difference {worst:G3} of one 16-bit step at or above {shadows}, {worstInShadows:G3} below; {overOneStep} of {pixels * 3} samples over one step");
        Assert.True(worst <= 1 + 1e-4, $"Largest difference {worst:G4} 16-bit steps at or above {shadows}");
        Assert.True(worstInShadows * step <= JxlLossyComparer.PeakError, $"Largest difference {worstInShadows:G4} 16-bit steps below {shadows}");
    }

    // The samples are HLG, which JxlMetadata reports.
    [Fact]
    public void Load_RgbHlg_ReportsHlg()
    {
        using Image<Rgb48> image = Image.Load<Rgb48>(CreateConfiguration(), InputPath("vardct_xyb_hlg"));

        JxlMetadata metadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.True(metadata.IsXybEncoded);
        Assert.Equal(JxlTransferFunction.Hlg, metadata.ColorEncoding.TransferFunction);
    }

    // An XYB image renders to its own PQ or HLG encoding, so its pixels get that encoding's profile: the one
    // the lossless file with the same encoding gets, a 3D-LUT profile for RGB and a table curve for grey.
    // The PQ profile does not depend on the intensity target, so both PQ files get colour_pq's. Identify
    // gives the same profile.
    [Theory]
    [InlineData("vardct_xyb_pq", "colour_pq")]
    [InlineData("vardct_xyb_pq_1000", "colour_pq")]
    [InlineData("vardct_xyb_hlg", "colour_hlg")]
    [InlineData("vardct_xyb_grey_hlg", "colour_grey_hlg")]
    public void Load_XybPqOrHlg_GivesTheProfileOfItsLosslessTwin(string name, string twin)
    {
        using Image<Rgb48> image = Image.Load<Rgb48>(CreateConfiguration(), InputPath(name));
        using Image<Rgb48> lossless = Image.Load<Rgb48>(CreateConfiguration(), InputPath(twin));
        IImageInfo info = Image.Identify(CreateConfiguration(), InputPath(name), out _);

        Assert.NotNull(lossless.Metadata.IccProfile);
        Assert.NotNull(image.Metadata.IccProfile);
        Assert.Equal(lossless.Metadata.IccProfile.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
        Assert.Equal(image.Metadata.IccProfile.ToByteArray(), info.Metadata.IccProfile.ToByteArray());
    }

    // An XYB image renders to its own BT.709 or DCI encoding, so its pixels get that encoding's parametric
    // profile, and Identify, which reads only the headers, gives the same profile.
    [Theory]
    [InlineData("vardct_xyb_bt709")]
    [InlineData("vardct_xyb_dci")]
    public void LoadAndIdentify_XybBt709OrDci_GiveTheSamePixelProfile(string name)
    {
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name));
        IImageInfo info = Image.Identify(CreateConfiguration(), InputPath(name), out _);

        Assert.NotNull(image.Metadata.IccProfile);
        Assert.NotNull(info.Metadata.IccProfile);
        Assert.Equal(image.Metadata.IccProfile.ToByteArray(), info.Metadata.IccProfile.ToByteArray());
    }

    // The PQ files at 16 bits. PQ is steep near 0, so the planes differ from djxl's by more than a 16-bit
    // step there (the peaks above), which libjxl's FMA causes (ADR 0004): this
    // port's TF_PQ applied to djxl's own linear output gives djxl's PQ output within 3.6e-7. So the image
    // is compared with this decoder's planes, clamped, which checks the 16-bit conversion, and with djxl's
    // at the lossy peak threshold.
    // Measured: 0.50 of one 16-bit step from the planes, which is the rounding; from djxl's clamped
    // reference, 5.2e-4 (34 steps, vardct_xyb_pq) and 1.3e-3 (83 steps, vardct_xyb_pq_1000).
    [Theory]
    [InlineData("vardct_xyb_pq")]
    [InlineData("vardct_xyb_pq_1000")]
    public void LoadRgb48_Pq_IsWithinOneStepOfTheClampedPlanes(string name)
    {
        NpyArray djxl = LoadReference(name);
        NpyArray planes;
        using (FileStream stream = File.OpenRead(InputPath(name)))
        {
            using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
            planes = JxlConformanceImages.ToNpy(decoded);
        }

        using Image<Rgb48> image = Image.Load<Rgb48>(CreateConfiguration(), InputPath(name));
        NpyArray actual = JxlConformanceImages.ToNpy(image, 3);

        AssertWithinOneStep(planes, actual, 3, (i, c) => planes.Data[(i * 3) + c], 16);
        double peak = 0;
        for (int i = 0; i < actual.Data.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(actual.Data[i] - Math.Clamp(djxl.Data[i], 0F, 1F)));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest difference from djxl {peak:G3}, {peak * 65535:G3} 16-bit steps");
        Assert.True(peak <= JxlLossyComparer.PeakError, $"Largest difference from djxl {peak:G4}");
    }

    // Measured: the largest difference is 0.50 of one 8-bit step, which is the rounding.
    [Theory]
    [InlineData("vardct_xyb_alpha")]
    [InlineData("vardct_xyb_identity_afv")]
    public void LoadRgba32_Alpha_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), InputPath(name));

        AssertWithinOneStep(expected, JxlConformanceImages.ToNpy(image, 4), 4, (i, c) => expected.Data[(i * 4) + c]);
    }

    // Measured: the largest difference is 0.50 of one 8-bit step, which is the rounding.
    [Fact]
    public void LoadRgb24_IccProfile_IsTheSrgbEncodingOfDjxlsLinearOutput()
    {
        // djxl's planes are linear sRGB; Image<TPixel> holds sRGB with no pixel profile (ADR 0002).
        NpyArray expected = LoadReference("vardct_xyb_icc");
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath("vardct_xyb_icc"));

        AssertWithinOneStep(expected, JxlConformanceImages.ToNpy(image, 3), 3, (i, c) => (float)LinearToSrgb(Math.Clamp(expected.Data[(i * 3) + c], 0F, 1F)));
        Assert.Null(image.Metadata.IccProfile);
        Assert.NotNull(image.Metadata.GetFormatMetadata(JxlFormat.Instance).EmbeddedIccProfile);
    }

    // A grey image with a D50 white point is one the XYB stage cannot output, so it renders to linear
    // sRGB like an ICC profile, and the pixels are sRGB: no pixel profile describes the D50 encoding
    // (ADR 0002), which JxlMetadata still reports.
    // Measured: the largest difference is 0.50 of one 8-bit step, which is the rounding.
    [Fact]
    public void Load_GreyWithAWhitePointTheXybStageCannotOutput_IsSrgbWithNoPixelProfile()
    {
        NpyArray expected = LoadReference("vardct_xyb_grey_d50");
        using Image<L8> image = Image.Load<L8>(CreateConfiguration(), InputPath("vardct_xyb_grey_d50"));

        AssertWithinOneStep(expected, JxlConformanceImages.ToNpy(image, 1), 1, (i, _) => (float)LinearToSrgb(Math.Clamp(expected.Data[i], 0F, 1F)));
        Assert.Null(image.Metadata.IccProfile);
        JxlMetadata metadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(JxlWhitePoint.Custom, metadata.ColorEncoding.WhitePoint);
        Assert.Null(metadata.EmbeddedIccProfile);
    }

    [Fact]
    public void Identify_GreyWithAWhitePointTheXybStageCannotOutput_HasNoPixelProfile()
    {
        IImageInfo info = Image.Identify(CreateConfiguration(), InputPath("vardct_xyb_grey_d50"), out _);

        Assert.Null(info.Metadata.IccProfile);
    }

    [Fact]
    public void Load_Grey_GivesAGreyscalePixelType()
    {
        using Image image = Image.Load(CreateConfiguration(), InputPath("vardct_xyb_grey"));

        Assert.IsType<Image<L8>>(image);
    }

    // The D11 files keep the 8-bit samples of their sources, so Image.Load picks an 8-bit pixel type.
    [Theory]
    [InlineData("vardct_xyb_bt709", typeof(Rgb24))]
    [InlineData("vardct_xyb_dci", typeof(Rgb24))]
    [InlineData("vardct_xyb_pq", typeof(Rgb24))]
    [InlineData("vardct_xyb_pq_1000", typeof(Rgb24))]
    [InlineData("vardct_xyb_hlg", typeof(Rgb24))]
    [InlineData("vardct_xyb_grey_hlg", typeof(L8))]
    public void Load_TransferFunction_PicksThePixelType(string name, Type pixelType)
    {
        using Image image = Image.Load(CreateConfiguration(), InputPath(name));

        Assert.Equal(typeof(Image<>).MakeGenericType(pixelType), image.GetType());
    }

    [Theory]
    [InlineData("vardct_xyb_64", 64, 64, 24, false, "Srgb")]
    [InlineData("vardct_xyb_300x200_d05", 300, 200, 24, false, "Srgb")]
    [InlineData("vardct_xyb_600x400_d3", 600, 400, 24, false, "Srgb")]
    [InlineData("vardct_xyb_grey", 300, 200, 8, true, "Srgb")]
    [InlineData("vardct_xyb_alpha", 72, 56, 32, false, "Srgb")]
    [InlineData("vardct_xyb_bt709", 64, 64, 24, false, "Bt709")]
    [InlineData("vardct_xyb_dci", 64, 64, 24, false, "Dci")]
    [InlineData("vardct_xyb_pq", 64, 64, 24, false, "Pq")]
    [InlineData("vardct_xyb_pq_1000", 64, 64, 24, false, "Pq")]
    [InlineData("vardct_xyb_hlg", 64, 64, 24, false, "Hlg")]
    [InlineData("vardct_xyb_grey_hlg", 300, 200, 8, true, "Hlg")]
    public void Identify_ReportsSizeChannelsAndColourEncoding(string name, int width, int height, int bitsPerPixel, bool grey, string transferFunction)
    {
        IImageInfo info = Image.Identify(CreateConfiguration(), InputPath(name), out _);

        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        Assert.Equal(bitsPerPixel, info.PixelType.BitsPerPixel);
        JxlMetadata metadata = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.True(metadata.IsXybEncoded);
        Assert.Equal(grey ? JxlColorSpace.Gray : JxlColorSpace.Rgb, metadata.ColorEncoding.ColorSpace);
        Assert.Equal(Enum.Parse<JxlTransferFunction>(transferFunction), metadata.ColorEncoding.TransferFunction);
        Assert.Null(metadata.EmbeddedIccProfile);
    }

    [Fact]
    public void Identify_IccProfile_ReportsTheProfileAndNoColourEncoding()
    {
        IImageInfo info = Image.Identify(CreateConfiguration(), InputPath("vardct_xyb_icc"), out _);

        Assert.Equal(32, info.Width);
        Assert.Equal(32, info.Height);
        Assert.Equal(24, info.PixelType.BitsPerPixel);
        JxlMetadata metadata = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.True(metadata.IsXybEncoded);
        Assert.Null(metadata.ColorEncoding);
        Assert.NotNull(metadata.EmbeddedIccProfile);
        Assert.Null(info.Metadata.IccProfile);
    }

    // The djxl match above covers an adaptive quant field and a non-zero chroma-from-luma map only if the
    // file has them. Read from the decoder's state after the DC groups: the quant field over the first
    // blocks of the varblocks, and how many tiles of each map are not 0.
    [Theory]
    [InlineData("vardct_xyb_identity_afv", 5, 18, 1, 0)] // 5 x 3 tiles
    [InlineData("vardct_xyb_strategies", 6, 8, 0, 0)] // one tile
    public void DecodeDcGroups_QuantFieldAndChromaFromLuma(string name, int minQuant, int maxQuant, int nonZeroYtoX, int nonZeroYtoB)
    {
        using JxlDecodedDcGroups dc = JxlDecodedDcGroups.Decode(File.ReadAllBytes(InputPath(name)));

        Assert.Equal((minQuant, maxQuant), dc.QuantFieldRange());
        Assert.Equal(nonZeroYtoX, dc.State.Cmap.YtoXMap.Count(v => v != 0));
        Assert.Equal(nonZeroYtoB, dc.State.Cmap.YtoBMap.Count(v => v != 0));
    }

    // The grey file cjxl made with --patches=1 has the frames the patches need: a Modular reference-only
    // frame saved before its colour transform, so in XYB, then the displayed VarDCT frame with patches.
    [Fact]
    public void GreyPatches_IsAnXybReferenceFrameThenAVarDctFrameWithPatches()
    {
        List<JxlFrameHeader> frames = JxlCodestreamFrames.Read(File.ReadAllBytes(InputPath("vardct_xyb_grey_patches")));

        Assert.Equal(2, frames.Count);
        Assert.Equal(JxlFrameType.ReferenceOnly, frames[0].FrameType);
        Assert.Equal(JxlFrameEncoding.Modular, frames[0].Encoding);
        Assert.True(frames[0].SaveBeforeColorTransform);
        Assert.Equal(3, frames[0].NumDecodedColorPlanes);
        Assert.Equal(JxlFrameEncoding.VarDct, frames[1].Encoding);
        Assert.Equal(JxlFrameFlags.Patches, frames[1].Flags & JxlFrameFlags.Patches);
        Assert.True(frames[1].IsLast);
    }

    // Compares each sample with the clamped reference, which reference(pixel, channel) gives, in steps of
    // the given bit depth.
    private static void AssertWithinOneStep(NpyArray expected, NpyArray actual, int channels, Func<int, int, float> reference, int bits = 8)
    {
        double steps = (1 << bits) - 1;
        Assert.Equal(expected.Shape[1], actual.Shape[1]);
        Assert.Equal(expected.Shape[2], actual.Shape[2]);
        int pixels = actual.Shape[1] * actual.Shape[2];
        double worst = 0;
        for (int i = 0; i < pixels; i++)
        {
            for (int c = 0; c < channels; c++)
            {
                double e = Math.Clamp(reference(i, c), 0F, 1F);
                worst = Math.Max(worst, Math.Abs(actual.Data[(i * channels) + c] - e) * steps);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest difference {worst:G3} of one {bits}-bit step");
        Assert.True(worst <= 1 + 1e-4, $"Largest difference {worst:G4} {bits}-bit steps");
    }

    // The sRGB encoding of IEC 61966-2-1, in double precision.
    private static double LinearToSrgb(double v) => v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;

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
