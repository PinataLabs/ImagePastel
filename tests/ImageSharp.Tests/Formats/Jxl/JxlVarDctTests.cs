// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// VarDCT frames whose blocks are all DCT8, with no XYB: the generated files made with
/// <c>benchmark_xl --codec=jxl:thunder:d1:gab0:epf0:noperc</c>, against djxl's float output.
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
    public void Load_Rgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray reference = LoadReference(name);
        NpyArray expected = DropAlpha(reference);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name));
        NpyArray actual = JxlConformanceImages.ToNpy(image, 3);

        JxlConformanceFrameResult result = JxlConformanceComparer.Measure(expected, actual, JxlConformanceComparison.ClampToUnitRange).Single();
        Assert.True(result.PeakError <= (1.0 / 255) + 1e-6, $"Peak error {result.PeakError * 255:G4} steps");
    }

    [Fact]
    public void Decode_StrategyOtherThanDct8_IsNotSupported()
        => AssertNotSupported(File.ReadAllBytes(InputPath("vardct_strategies")), "AC strategy");

    [Fact]
    public void Decode_XybVarDct_IsNotSupported()
        => AssertNotSupported(File.ReadAllBytes(InputPath("vardct_xyb")), "VarDCT");

    [Fact]
    public void Decode_RawQuantTable_IsNotSupported()
    {
        // vardct_groups has its own sections, so AC global starts a section. Its first bit is
        // all_default (0), then the 3-bit mode of the DCT8 table, low bit first: kQuantModeDCT (6).
        // Setting its low bit makes the mode kQuantModeRAW (7).
        byte[] codestream = File.ReadAllBytes(InputPath("vardct_groups"));
        int acGlobal = SectionOffset(codestream, 2);
        Assert.Equal(0b1100, codestream[acGlobal] & 0b1111);
        codestream[acGlobal] |= 0b0010;

        AssertNotSupported(codestream, "RAW");
    }

    // Returns where section sectionIndex of the first frame starts, for a bare codestream with no ICC.
    private static int SectionOffset(byte[] codestream, int sectionIndex)
    {
        JxlCodecMetadata metadata = new();
        JxlBitReader reader = new(codestream, 2, codestream.Length - 2);
        JxlBundle.Read(reader, metadata.Size);
        JxlBundle.Read(reader, metadata.M);
        metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;
        JxlBundle.Read(reader, metadata.TransformData);
        reader.JumpToByteBoundary();
        JxlFrameHeader frameHeader = new(metadata);
        JxlBundle.Read(reader, frameHeader);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        uint[] sizes = JxlToc.ReadToc(JxlToc.NumTocEntries(frameDim.NumGroups, frameDim.NumDcGroups, frameHeader.Passes.NumPasses), reader);
        int offset = 2 + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();
        for (int i = 0; i < sectionIndex; i++)
        {
            offset += (int)sizes[i];
        }

        return offset;
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

    private static void AssertNotSupported(byte[] codestream, string feature)
    {
        using MemoryStream stream = new(codestream);
        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None));
        Assert.Contains(feature, ex.Message, StringComparison.Ordinal);
    }
}
