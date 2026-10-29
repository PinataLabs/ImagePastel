// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The edge-preserving filter (EPF) on VarDCT frames: the files made with <c>cjxl -e 2 --gaborish=0</c>
/// and an <c>--epf</c> (tests/Images/Input/Jxl/Generated/README.md), whose blocks are all DCT8. The
/// planes are compared with djxl's float output at the lossy thresholds (docs/jxl/spec.md, "Tolerance
/// for lossy output"), and each case records the error measured when it was written.
/// </summary>
/// <remarks>
/// At <c>-d 1</c> the 64 x 64 image is barely filtered: <c>vardct_xyb_epf</c> (one iteration) changes by
/// 2.9e-6 when EPF is turned off, inside the tolerance, and pass 0 changes it by 6.4e-4. The <c>_d3</c>
/// files are filtered well above the tolerance by every pass, so they fail if a pass is skipped or wrong.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlEpfTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured on x64 against djxl v0.12.0 (AVX2): the worst channel RMSE and the peak error. In
    // brackets: how far the reference is from the same file decoded with EPF off (peak, RMSE).
    [Theory]
    [InlineData("vardct_xyb_epf")] // RMSE 1.1e-6, peak 1.2e-5 [2.9e-6, 6.5e-8]
    [InlineData("vardct_xyb_epf2")] // RMSE 1.1e-6, peak 1.2e-5 [2.1e-2, 4.4e-4]
    [InlineData("vardct_xyb_epf3")] // RMSE 1.1e-6, peak 1.2e-5 [2.1e-2, 4.4e-4]
    [InlineData("vardct_xyb_epf1_d3")] // RMSE 1.0e-6, peak 1.6e-5 [3.6e-2, 2.6e-3]
    [InlineData("vardct_xyb_epf2_d3")] // RMSE 9.9e-7, peak 1.6e-5 [6.3e-2, 4.5e-3]
    [InlineData("vardct_xyb_epf3_d3")] // RMSE 1.1e-6, peak 2.5e-5 [1.1e-1, 8.5e-3]
    [InlineData("vardct_xyb_epf3_300x200_d3")] // RMSE 1.2e-6, peak 4.1e-5 [2.5e-1, 1.0e-2]
    [InlineData("vardct_xyb_epf3_13x37_d3")] // RMSE 1.6e-6, peak 7.8e-6 [8.9e-2, 1.1e-2]
    [InlineData("vardct_xyb_epf3_600x400_d3")] // RMSE 1.3e-6, peak 7.8e-5 [4.4e-1, 9.6e-3]
    [InlineData("vardct_xyb_epf3_custom")] // RMSE 1.1e-6, peak 1.2e-5 [6.3e-2, 1.9e-3]
    [InlineData("vardct_xyb_epf_sharp0")] // RMSE 1.1e-6, peak 1.2e-5; every block passes through
    [InlineData("vardct_xyb_epf_quant_mul0")] // RMSE 1.1e-6, peak 1.2e-5; every block passes through
    public void DecodeToPlanes_MatchesDjxl(string name)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);

        Assert.Equal(3, decoded.Frames[0].Planes.Count);
        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
    }

    // Measured: the largest difference is 0.51 of one 8-bit step: the rounding, and the error above.
    [Theory]
    [InlineData("vardct_xyb_epf")]
    [InlineData("vardct_xyb_epf2")]
    [InlineData("vardct_xyb_epf3")]
    [InlineData("vardct_xyb_epf3_d3")]
    [InlineData("vardct_xyb_epf3_300x200_d3")]
    [InlineData("vardct_xyb_epf3_13x37_d3")]
    [InlineData("vardct_xyb_epf3_600x400_d3")]
    public void LoadRgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name), out _);

        JxlConformanceImages.AssertRgbWithinOneStepOfTheClampedReference(expected, JxlConformanceImages.ToNpy(image, 3));
    }

    // loop_filter.cc: epf_sigma_for_modular below 1e-8 fails, as djxl does for both files.
    [Theory]
    [InlineData("xyb_modular_epf_sigma0")]
    [InlineData("xyb_modular_epf_sigma_neg")]
    public void Decode_SigmaForModularBelowTheBound_IsInvalid(string name)
    {
        using FileStream stream = File.OpenRead(InputPath(name));

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains("EPF: sigma for modular is too small", ex.Message, StringComparison.Ordinal);
    }

    // libjxl's JXL_ENSURE(epf_iters > 0) in ComputeSigma. No file reaches it, as only a frame with EPF
    // gets the EPF stages that compute the sigma, so it is called on the DC groups of one such frame.
    [Fact]
    public void ComputeSigma_EpfOff_IsInvalid()
    {
        using JxlDecodedDcGroups dc = JxlDecodedDcGroups.Decode(File.ReadAllBytes(InputPath("vardct_xyb_epf")));
        JxlLoopFilter lf = dc.FrameHeader.LoopFilter;
        Assert.Equal(1u, lf.EpfIters);
        Assert.NotNull(JxlEpfSigma.Compute(lf, dc.State));

        lf.EpfIters = 0;

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => JxlEpfSigma.Compute(lf, dc.State));
        Assert.Contains("EPF sigma computed with EPF off", ex.Message, StringComparison.Ordinal);
    }

    // #315: each pass filters a vector of pixels at a time where the hardware has vectors, and the rest
    // of a row with the scalar code. Both must give the same bits, on any planes and sigmas: the vector
    // code does each pixel's sums in the scalar code's order, without FMA (ADR 0004). The widths take in
    // a row shorter than a vector, whole vectors, and a tail; a third of the blocks pass through. Low
    // contrast, as in a lossy image, puts most weights between 0 and 1. High contrast spans magnitudes
    // from denormals to 1e30, as a hostile stream can give, and the special planes add NaN and infinities,
    // so the weight's select and the weight sum's reciprocal see them too.
    [Theory]
    [InlineData(0, 3, 5, PlaneValues.LowContrast)]
    [InlineData(0, 64, 24, PlaneValues.LowContrast)]
    [InlineData(0, 77, 19, PlaneValues.LowContrast)]
    [InlineData(0, 77, 19, PlaneValues.HighContrast)]
    [InlineData(0, 77, 19, PlaneValues.Special)]
    [InlineData(1, 3, 5, PlaneValues.LowContrast)]
    [InlineData(1, 64, 24, PlaneValues.LowContrast)]
    [InlineData(1, 77, 19, PlaneValues.LowContrast)]
    [InlineData(1, 77, 19, PlaneValues.HighContrast)]
    [InlineData(1, 77, 19, PlaneValues.Special)]
    [InlineData(2, 3, 5, PlaneValues.LowContrast)]
    [InlineData(2, 64, 24, PlaneValues.LowContrast)]
    [InlineData(2, 77, 19, PlaneValues.LowContrast)]
    [InlineData(2, 77, 19, PlaneValues.HighContrast)]
    [InlineData(2, 77, 19, PlaneValues.Special)]
    public void Pass_VectorAndScalar_AreBitIdentical(int pass, int xsize, int ysize, PlaneValues values)
    {
        Random random = new(315 + (pass * 1000) + xsize + ((int)values * 100));
        JxlLoopFilter lf = new();
        for (int c = 0; c < 3; c++)
        {
            lf.EpfChannelScale[c] = NextFloat(random, 1f, 40f);
        }

        lf.EpfPass0SigmaScale = NextFloat(random, 0.5f, 1.5f);
        lf.EpfPass2SigmaScale = NextFloat(random, 0.5f, 1.5f);
        lf.EpfBorderSadMul = NextFloat(random, 0.5f, 3f);

        int xsizeBlocks = (xsize + 7) / 8;
        int ysizeBlocks = (ysize + 7) / 8;
        float[] invSigma = new float[xsizeBlocks * ysizeBlocks];
        for (int i = 0; i < invSigma.Length; i++)
        {
            // Below kMinSigma (about -3.9) a block passes through.
            invSigma[i] = NextFloat(random, -6f, -0.01f);
        }

        JxlEpfSigma sigma = JxlEpfSigma.FromBlocks(invSigma, xsizeBlocks);
        Buffer2D<float>[] planes = new Buffer2D<float>[3];
        for (int c = 0; c < 3; c++)
        {
            planes[c] = Configuration.Default.MemoryAllocator.Allocate2D<float>(xsize, ysize);
            for (int y = 0; y < ysize; y++)
            {
                Span<float> row = planes[c].DangerousGetRowSpan(y);
                for (int x = 0; x < xsize; x++)
                {
                    row[x] = NextSample(random, values);
                }
            }
        }

        try
        {
            float[] scalar = RunPass(pass, lf, sigma, planes, xsize, ysize, scalarOnly: true);
            float[] vector = RunPass(pass, lf, sigma, planes, xsize, ysize, scalarOnly: false);

            // Two NaNs count as equal: .NET keeps no NaN's sign or payload stable, and on net11.0 they
            // change from run to run, in either path. Nothing downstream reads them, only whether a
            // sample is NaN.
            for (int i = 0; i < scalar.Length; i++)
            {
                int scalarBits = BitConverter.SingleToInt32Bits(scalar[i]);
                int vectorBits = BitConverter.SingleToInt32Bits(vector[i]);
                Assert.True(
                    scalarBits == vectorBits || (float.IsNaN(scalar[i]) && float.IsNaN(vector[i])),
                    $"Sample {i}: scalar {scalar[i]:R} (0x{scalarBits:X8}), vector {vector[i]:R} (0x{vectorBits:X8})");
            }
        }
        finally
        {
            JxlPlanes.DisposeAll(planes);
        }
    }

    // Runs one pass over the whole frame, as the pipeline does, and returns its three output planes.
    private static float[] RunPass(int pass, JxlLoopFilter lf, JxlEpfSigma sigma, Buffer2D<float>[] planes, int xsize, int ysize, bool scalarOnly)
    {
        JxlEpfStage stage = pass switch
        {
            0 => new JxlEpf0Stage(lf, sigma),
            1 => new JxlEpf1Stage(lf, sigma),
            _ => new JxlEpf2Stage(lf, sigma),
        };
        stage.ScalarOnly = scalarOnly;

        Buffer2D<float>[] output = new Buffer2D<float>[3];
        try
        {
            for (int c = 0; c < 3; c++)
            {
                output[c] = Configuration.Default.MemoryAllocator.Allocate2D<float>(xsize, ysize, AllocationOptions.Clean);
            }

            using (JxlNeighbourRows input = new(Configuration.Default.MemoryAllocator, planes, stage, xsize, ysize))
            {
                for (int y = 0; y < ysize; y++)
                {
                    input.MoveTo(y);
                    stage.ProcessRow(input, output, xsize, y);
                }
            }

            float[] result = new float[3 * xsize * ysize];
            for (int c = 0; c < 3; c++)
            {
                for (int y = 0; y < ysize; y++)
                {
                    output[c].DangerousGetRowSpan(y).Slice(0, xsize).CopyTo(result.AsSpan(((c * ysize) + y) * xsize));
                }
            }

            return result;
        }
        finally
        {
            JxlPlanes.DisposeAll(output);
        }
    }

    private static float NextFloat(Random random, float min, float max) => min + ((float)random.NextDouble() * (max - min));

    private static float NextSample(Random random, PlaneValues values)
    {
        if (values == PlaneValues.LowContrast)
        {
            return 0.5f + NextFloat(random, -0.01f, 0.01f);
        }

        int pick = random.Next(16);
        if (values == PlaneValues.Special)
        {
            switch (pick)
            {
                case 0:
                    return float.NaN;
                case 1:
                    return float.PositiveInfinity;
                case 2:
                    return float.NegativeInfinity;
            }
        }

        return pick switch
        {
            3 => NextFloat(random, -1f, 1f) * 1e-40f, // denormal
            4 => NextFloat(random, -1f, 1f) * 1e30f,
            5 => 0f,
            _ => NextFloat(random, -4f, 4f),
        };
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

    /// <summary>The samples of the planes of <see cref="Pass_VectorAndScalar_AreBitIdentical"/>.</summary>
    public enum PlaneValues
    {
        /// <summary>0.5 plus or minus 0.01.</summary>
        LowContrast,

        /// <summary>Mostly -4 to 4, with zeros, denormals and magnitudes up to 1e30.</summary>
        HighContrast,

        /// <summary>As <see cref="HighContrast"/>, with NaN and both infinities.</summary>
        Special,
    }
}
