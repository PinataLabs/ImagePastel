// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Raw quantization tables (D10d), which only JPEG-transcoded files use: <c>jpeg_rgb</c> is
/// <c>cjxl</c>'s lossless transcode of the RGB-coded baseline JPEG <c>Jpg/baseline/jpeg-rgb.jpg</c>. An RGB
/// JPEG transcodes to a VarDCT frame with no colour transform, so the file needs neither YCbCr nor chroma
/// upsampling; its DCT8 table is raw, and the other tables come from the library.
/// </summary>
/// <remarks>
/// The JPEG conformance vectors allow an RMSE of 1e-5, ten times less than the general lossy rule, so this
/// file is held to that too.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlRawQuantTableTests
{
    private const string Name = "jpeg_rgb";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Header_IsVarDctWithNoColourTransform()
    {
        // cjxl puts a transcode in a container, beside the JPEG reconstruction box.
        byte[] codestream = JxlContainer.Read(File.ReadAllBytes(InputPath(Name)), readMetadata: false, CancellationToken.None).Codestream;
        JxlFrameHeader frameHeader = JxlTestLayers.ReadFirstFrameHeader(codestream);

        Assert.Equal(JxlFrameEncoding.VarDct, frameHeader.Encoding);
        Assert.Equal(JxlColorTransform.None, frameHeader.ColorTransform);
    }

    // Measured on x64 against djxl v0.12.0 (AVX2): worst channel RMSE 8.4e-8, peak 2.0e-6.
    [Fact]
    public void DecodeToPlanes_MatchesDjxl()
    {
        NpyArray expected = LoadReference(Name);

        using FileStream stream = File.OpenRead(InputPath(Name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, actual, JxlLossyComparer.JpegRmsError));
    }

    [Fact]
    public void Load_Rgb24_IsWithinOneStepOfTheClampedReference()
    {
        NpyArray expected = LoadReference(Name);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(Name));
        NpyArray actual = JxlConformanceImages.ToNpy(image, 3);

        JxlConformanceImages.AssertWithinOneStepOfTheClampedReference(expected, actual);
    }

    // A raw table for a transform larger than one block, which no transcode has: a JPEG has only DCT8
    // blocks, so its raw table is table 0. A hand-built 16 x 16 VarDCT frame whose varblocks all use one
    // transform has a raw table for it with a different entry for each coefficient and channel, as
    // JxlVarDctBoundsTests' raw_table_nonzeros has for DCT8, and its first varblock has its first 63 AC
    // coefficients of Y at 1, so each weight scales its own coefficient. Table 4 (DCT16X16) is 16 x 16
    // entries. Table 6 is 8 wide and 16 high: DCT16X8 (strategy 6, two blocks high) reads it as it is,
    // and DCT8X16 (strategy 7, two blocks wide) transposed. Measured on x64 against djxl v0.12.0 (AVX2):
    // worst channel RMSE 1.2e-6, peak 1.4e-5.
    [Theory]
    [InlineData(4, new[] { 4 })]
    [InlineData(6, new[] { 6, 6 })]
    [InlineData(6, new[] { 7, 7 })]
    public async Task DecodeToPlanes_RawTableOfALargerTransform_MatchesDjxl(int table, int[] strategies)
    {
        int blocks = JxlDequantMatrices.RequiredSizeX[table] * JxlDequantMatrices.RequiredSizeY[table];
        Assert.True(blocks > 1);
        List<uint> tokens = new() { 63 };
        tokens.AddRange(Enumerable.Repeat(2u, 63));
        byte[] codestream = new JxlTestVarDct(16, 16)
        {
            Strategies = strategies,
            DequantMatrices = w => JxlTestVarDct.WriteDequantMatrices(w, table, t => JxlTestVarDct.WriteRawTable(t, 0x1400, i => 1 + (i % 17), blocks)),
            FirstBlockTokens = tokens.ToArray(),
        }.ToArray();
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlRawQuantTableTests));
        string input = Path.Combine(directory, $"raw_table_{table}_strategy_{strategies[0]}.jxl");
        string output = Path.Combine(directory, $"raw_table_{table}_strategy_{strategies[0]}.npy");
        await File.WriteAllBytesAsync(input, codestream, CancellationToken);

        await JxlTools.RunAsync("djxl", CancellationToken, input, output);

        using MemoryStream stream = new(codestream);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);
        Assert.Single(JxlLossyComparer.AssertWithinThresholds(NpyArray.Load(output), actual));

        // The coefficients reach the output, so the table is used.
        Assert.True(actual.Data.Max() - actual.Data.Min() > 0.01f);
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
