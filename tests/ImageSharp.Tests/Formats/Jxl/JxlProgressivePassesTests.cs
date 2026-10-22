// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Progressive passes (D13b): frames whose AC (VarDCT) or Modular groups are split into several passes,
/// each refining the last. The decoder outputs only the final image, so each file is checked against
/// djxl v0.12.0's output. The generated files are described in <c>Generated/README.md</c>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlProgressivePassesTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Each file is one frame of Bike.png (582 x 453, 3 x 2 groups) with more than one pass, and no DC frame:
    // cjxl's --progressive_dc=0 keeps the DC in the frame itself.
    [Theory]
    [InlineData("vardct_progressive_ac", false)]
    [InlineData("vardct_qprogressive_ac", false)]
    [InlineData("modular_progressive_ac", true)]
    public void GeneratedFile_IsOneFrameOfSeveralPassesAndGroups(string name, bool modular)
    {
        List<JxlFrameHeader> frames = JxlCodestreamFrames.Read(File.ReadAllBytes(InputPath(name)));

        JxlFrameHeader frame = Assert.Single(frames);
        JxlPasses passes = frame.Passes;
        TestContext.Current.TestOutputHelper?.WriteLine($"{passes.NumPasses} passes, shifts {string.Join(", ", passes.Shift[..(int)passes.NumPasses])}, downsampling {string.Join(", ", passes.Downsample[..(int)passes.NumDownsample])} after passes {string.Join(", ", passes.LastPass[..(int)passes.NumDownsample])}");
        Assert.Equal(JxlFrameType.Regular, frame.FrameType);
        Assert.Equal(modular ? JxlFrameEncoding.Modular : JxlFrameEncoding.VarDct, frame.Encoding);
        Assert.Equal((JxlFrameFlags)0, frame.Flags & JxlFrameFlags.UseDcFrame);
        Assert.True(passes.NumPasses > 1, $"{passes.NumPasses} pass");
        Assert.True(frame.ToFrameDimensions().NumGroups > 1);
    }

    // Against djxl v0.12.0's float32 output, within 1e-4 RMSE and 4e-3 peak per channel. Measured on x64
    // against djxl with AVX2: the worst channel's RMSE and the peak. The two files split the same quantized
    // coefficients differently, so djxl's outputs, and the errors, are the same. A decoder that ignores
    // the shift of the first pass misses vardct_qprogressive_ac by an RMSE of 0.054; one that takes the
    // first pass's coefficient orders for every pass misses both, by 0.091 and 0.026.
    [Theory]
    [InlineData("vardct_progressive_ac")] // 3 passes, shifts 0: 1.0e-6, 1.2e-5
    [InlineData("vardct_qprogressive_ac")] // 2 passes, shifts 1, 0: 1.0e-6, 1.2e-5
    public void DecodeToPlanes_VarDct_MatchesDjxl(string name)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);

        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
    }

    [Theory]
    [InlineData("vardct_progressive_ac")]
    [InlineData("vardct_qprogressive_ac")]
    public void Load_VarDct_Rgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name));
        NpyArray actual = JxlConformanceImages.ToNpy(image, expected.Shape[3]);

        JxlConformanceFrameResult result = Assert.Single(JxlConformanceComparer.Measure(expected, actual, JxlConformanceComparison.ClampToUnitRange));
        TestContext.Current.TestOutputHelper?.WriteLine($"Peak error {result.PeakError * 255:G3} of one 8-bit step");
        Assert.True(result.PeakError <= (1.0 / 255) + 1e-6, $"Peak error {result.PeakError * 255:G4} steps");
    }

    // cjxl -d 0 -p: the passes of modular_progressive_ac, with the groups stored centre first, so the TOC
    // is permuted (D13a).
    [Fact]
    public void LosslessProgressive_IsOneModularFrameOfSeveralPassesWithAPermutedToc()
    {
        List<(JxlFrameHeader Header, JxlGroupOffsets Toc)> frames = JxlCodestreamFrames.ReadWithToc(File.ReadAllBytes(InputPath("modular_progressive")));

        (JxlFrameHeader frame, JxlGroupOffsets toc) = Assert.Single(frames);
        Assert.Equal(JxlFrameType.Regular, frame.FrameType);
        Assert.Equal(JxlFrameEncoding.Modular, frame.Encoding);
        Assert.Equal((JxlFrameFlags)0, frame.Flags & JxlFrameFlags.UseDcFrame);
        Assert.True(frame.Passes.NumPasses > 1, $"{frame.Passes.NumPasses} pass");
        Assert.NotNull(toc.Permutation);
    }

    // A lossless Modular frame of two passes, each decoding its downsampling bracket of the squeezed
    // channels, matches djxl exactly, and is the source image: modular_progressive_ac (cjxl -d 0
    // --progressive_ac, whose TOC is not permuted) and modular_progressive (cjxl -d 0 -p, permuted). A
    // decoder that gives every pass the first pass's bracket fails the ANS final-state check.
    [Theory]
    [InlineData("modular_progressive_ac")]
    [InlineData("modular_progressive")]
    public void DecodeToPlanes_ModularPasses_MatchesDjxlExactly(string name)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.Data, actual.Data);
    }

    [Theory]
    [InlineData("modular_progressive_ac")]
    [InlineData("modular_progressive")]
    public void Load_ModularPasses_IsTheSourceImage(string name)
    {
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name));
        using Image<Rgb24> source = Image.Load<Rgb24>(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Png", "Bike.png"));

        ImageComparer.Exact.VerifySimilarity(source, image);
    }

    // The bounds of libjxl's Passes::VisitFields (#112). A frame of one 256 x 256 group whose image fits
    // in the global Modular stream leaves every group section empty, so a valid header of N passes decodes
    // as the single-pass frame, with the TOC's N + 3 sections. num_passes is at most 11 and num_downsample
    // at most 4 by their U32 encodings, so libjxl's JXL_ENSUREs of those cannot fail when reading.
    [Theory]
    [InlineData(2, -1, new[] { 2 }, new[] { 0 }, null)]
    [InlineData(3, -1, new[] { 4, 2 }, new[] { 0, 1 }, null)]
    [InlineData(11, -1, new[] { 8, 4, 2, 1 }, new[] { 0, 1, 2, 7 }, null)]
    [InlineData(2, 3, new[] { 8, 4, 2 }, new[] { 0, 1, 1 }, "num_downsample 3 > num_passes 2")]
    [InlineData(3, -1, new[] { 2, 4 }, new[] { 0, 1 }, "downsample sequence should be decreasing")]
    [InlineData(3, -1, new[] { 2, 2 }, new[] { 0, 1 }, "downsample sequence should be decreasing")]
    [InlineData(3, -1, new[] { 4, 2 }, new[] { 1, 1 }, "last_pass sequence should be increasing")]
    [InlineData(3, -1, new[] { 4, 2 }, new[] { 1, 0 }, "last_pass sequence should be increasing")]
    [InlineData(2, -1, new[] { 2 }, new[] { 2 }, "last_pass 2 >= num_passes 2")]
    public void DecodeToPlanes_PassesHeader_IsBounded(int numPasses, int numDownsample, int[] downsample, int[] lastPass, string error)
    {
        const int size = 16;
        JxlTestFrame plain = JxlTestFrame.Last(JxlTestLayers.Ramp);
        byte[][] sections = new byte[numPasses + 3][];
        sections[0] = JxlTestLayers.Ramp;
        for (int i = 1; i < sections.Length; i++)
        {
            sections[i] = [];
        }

        JxlTestFrame frame = plain with
        {
            Sections = sections,
            Passes = w => WritePasses(w, numPasses, numDownsample < 0 ? downsample.Length : numDownsample, downsample, lastPass),
        };
        using MemoryStream stream = new(new JxlTestLayers(size, size).Add(frame).ToArray());

        if (error != null)
        {
            InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
            Assert.Contains(error, ex.Message, System.StringComparison.Ordinal);
            return;
        }

        using MemoryStream plainStream = new(new JxlTestLayers(size, size).Add(plain).ToArray());
        using JxlDecodedImage expected = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(plainStream, CancellationToken);
        using JxlDecodedImage actual = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        Assert.Equal(JxlConformanceImages.ToNpy(expected).Data, JxlConformanceImages.ToNpy(actual).Data);
    }

    // libjxl's Passes, field by field, each U32 with its smallest selector: num_passes as U32(Val(1),
    // Val(2), Val(3), BitsOffset(3, 4)), num_downsample as U32(Val(0), Val(1), Val(2), BitsOffset(1, 3)),
    // a 2-bit shift of 0 for each pass but the last, each downsample as U32(Val(1), Val(2), Val(4),
    // Val(8)), and each last_pass as U32(Val(0), Val(1), Val(2), Bits(3)).
    private static void WritePasses(JxlTestBitWriter w, int numPasses, int numDownsample, int[] downsample, int[] lastPass)
    {
        if (numPasses <= 3)
        {
            w.Write(2, (ulong)(numPasses - 1));
        }
        else
        {
            w.Write(2, 3).Write(3, (ulong)(numPasses - 4));
        }

        if (numPasses == 1)
        {
            return;
        }

        if (numDownsample <= 2)
        {
            w.Write(2, (ulong)numDownsample);
        }
        else
        {
            w.Write(2, 3).Write(1, (ulong)(numDownsample - 3));
        }

        for (int i = 0; i < numPasses - 1; i++)
        {
            w.Write(2, 0);
        }

        for (int i = 0; i < numDownsample; i++)
        {
            w.Write(2, (ulong)System.Numerics.BitOperations.Log2((uint)downsample[i]));
        }

        for (int i = 0; i < numDownsample; i++)
        {
            if (lastPass[i] <= 2)
            {
                w.Write(2, (ulong)lastPass[i]);
            }
            else
            {
                w.Write(2, 3).Write(3, (ulong)lastPass[i]);
            }
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

    private static NpyArray LoadReference(string name) => NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy"));
}
