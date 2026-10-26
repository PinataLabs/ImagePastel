// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Formats.Jxl.VarDct;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The adaptive quantisation field of lossy Default (libjxl's <c>InitialQuantField</c>, slice E4b), against
/// libjxl's own values and through the decoder's parse of the frame.
/// </summary>
/// <remarks>
/// <para>
/// The reference values come from a harness (<c>e4-notes/e4b-scratch/aq.cc</c>) that builds libjxl
/// <c>ef67fde2</c>'s <c>enc_adaptive_quantization.cc</c> and <c>quantizer.cc</c> twice, both with
/// <c>-ffp-contract=off</c> (ADR 0004): on Highway's scalar target, and on its AVX2 target with
/// <c>HWY_DISABLE_BMI2_FMA</c>, which keeps cjxl's 8 lanes but makes each <c>MulAdd</c> a multiply then an
/// add. Its input is the opsin of <see cref="CreateOpsin"/>, written to a file.
/// </para>
/// <para>
/// The masking image takes <c>std::log1p</c> per pixel, which this port computes as
/// <c>(float)Math.Log(1.0 + (double)x)</c> (ADR 0004). Its expected values come from the AVX2 build with
/// <c>log1pf</c> wrapped to the same formula; against glibc's <c>log1pf</c>, 818 of Bike's 266,304 pixels
/// differ by one unit in the last place (at most 1.9e-7 relative).
/// </para>
/// <para>
/// The modulations sum over 8 lanes (<c>SumOfLanes</c>), and the port sums in AVX2's lane order, as
/// cjxl does on x64. So it matches the AVX2 build bit for bit, and the scalar build, which sums in one
/// lane, within <see cref="ScalarLaneSumTolerance"/>.
/// </para>
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlEncAdaptiveQuantizationTests
{
    // The largest relative difference of a block's value from libjxl's scalar build, which sums the
    // modulations in another order: measured 5.8e-7 on the crop and 9.1e-7 on Bike. The raw quant field
    // is the same on every block.
    private const float ScalarLaneSumTolerance = 2e-6f;

    // The crop's field from libjxl's scalar build, as float bits, row by row.
    private static readonly uint[] CropScalarFieldBits =
    {
        0x3F2FF8D0, 0x3F0A6657, 0x3F17C7D1, 0x3F233307, 0x3F30AB23, 0x3F30CA77, 0x3F311C48, 0x3F317458, 0x3F317AED,
        0x3F2D7502, 0x3F01F95A, 0x3EBC15B9, 0x3ED21621, 0x3EE9B6B4, 0x3EE23B3B, 0x3ECE74E6, 0x3EDEF20A, 0x3F01A4BB,
        0x3F0932D8, 0x3F2018ED, 0x3ED12DAA, 0x3F10590E, 0x3ED7E575, 0x3EF53A4A, 0x3ECAE6C8, 0x3F029A74, 0x3EB795AA,
        0x3ECB9CD9, 0x3ECC5023, 0x3EC4730C, 0x3EE659CA, 0x3EDAADB5, 0x3EEE1DC5, 0x3EC9D2B8, 0x3EFCBE89, 0x3ECAE2C1,
        0x3F2A12F1, 0x3F28A4F0, 0x3F1ACBFB, 0x3F288117, 0x3EF413C4, 0x3ED7D590, 0x3EC9942E, 0x3EC24545, 0x3EC7393A,
    };

    // A 72x40 crop of Bike over the red tank and the white "DUCATI" letters: two 64x64 tiles across, so the
    // pre-erosion of the second reads 4 pixels into the first.
    [Fact]
    public void InitialQuantField_OnACrop_IsWithinTheLaneSumToleranceOfLibjxlsScalarBuild()
    {
        using Image<Rgb24> crop = LoadCrop();

        using Buffer2D<float> field = InitialQuantField(crop, 1.0f, gaborish: true, out Buffer2D<float> mask1x1);
        mask1x1.Dispose();

        Assert.Equal(9, field.Width);
        Assert.Equal(5, field.Height);
        for (int i = 0; i < CropScalarFieldBits.Length; i++)
        {
            float expected = BitConverter.Int32BitsToSingle((int)CropScalarFieldBits[i]);
            float actual = field[i % 9, i / 9];
            Assert.True(MathF.Abs(actual - expected) <= ScalarLaneSumTolerance * MathF.Abs(expected), $"Block {i}: {actual:R} against libjxl's {expected:R}");
        }
    }

    [Fact]
    public void InitialQuantField_OnACrop_GivesLibjxlsAvx2ValuesBitForBit()
    {
        using Image<Rgb24> crop = LoadCrop();

        using Buffer2D<float> field = InitialQuantField(crop, 1.0f, gaborish: true, out Buffer2D<float> mask1x1);
        using (mask1x1)
        {
            Assert.Equal("CF01A4DAB95247799AAFAAFA37623FAB75EEE4CE8E7640BA658FE3029E5C382A", Sha256(field));
            Assert.Equal("2DE8A0DCD93FF90AC3E4DE1A1CEF6B34A05100386DFF7F5186DE742748F14AF6", Sha256(mask1x1));
            Assert.Equal("674F68A0BAFE0B96C7577E8090B4F9A4D1A768439169C3FE2489E65E7ABAEAB4", Sha256(RawQuantField(field, 1.0f)));
        }
    }

    // Bike (582x453, 73x57 blocks, 10x8 tiles with a one-block column and row at the far edges) at each
    // Default distance; at d0.5 Gaborish is off, so the field is computed at 0.62 d. The raw quant field is
    // SetQuantFieldRect's integers at the global scale of 0.39 / d. The masking image is the same at every
    // distance.
    [Theory]
    [InlineData(1.0f, true, "6A846CE3AA5CFDAE165591C8E432636CF2E8EE70C00369D44E3A6594AA40E657", "B1B0FA2A31D81A485750663BB521230B7CA37AFD5F396F5CEDB1D1370B45BA0E")]
    [InlineData(2.0f, true, "5FA3E254D945291BE366C4CA72A6F716BBB9062F55CBCD14F1E2FD3B35653637", "B73EE7A8116B6E1DCA806CA00D72E609D24DAF07536343276C8461A5A294ADA9")]
    [InlineData(4.0f, true, "6E8A1A9D6E482C6E01562E8DA23F663F0548E66822497F718663D5B3896D1E52", "7536F0B7B243B2B9C55C125C6AAC788410B294B4B231CA52490A53E6BDC12561")]
    [InlineData(0.5f, false, "E448915B5F7DF0CA51AFFF0439FDB84CA570A7A31295C8B8A317D138FC4C1871", "D58FAF4CE5507A87F9BF664DA59001E6C630E03C357A0838FEAEC4A4E8E503A1")]
    public void InitialQuantField_OnBike_GivesLibjxlsAvx2ValuesBitForBit(float distance, bool gaborish, string expectedField, string expectedRaw)
    {
        using Image<Rgb24> bike = JxlLossyEncoderTests.LoadSizeCase("bike-582x453");

        using Buffer2D<float> field = InitialQuantField(bike, distance, gaborish, out Buffer2D<float> mask1x1);
        using (mask1x1)
        {
            Assert.Equal(expectedField, Sha256(field));
            Assert.Equal("25935DCA7C50A55920488D7B7A6DB5E20903BF333FC909AAFC8C0251415EEF6A", Sha256(mask1x1));
            Assert.Equal(expectedRaw, Sha256(RawQuantField(field, distance)));
        }
    }

    // Through the decoder's parse: the quant field of a photo varies from block to block, and its scale
    // (raw value times the quantiser's scale) follows libjxl's global scale of 0.39 / d, so that d times
    // its mean stays about the same at every distance. Measured on Bike, min / mean / max of the scaled
    // field and d times the mean:
    // - E4b alone: d1 0.2340 / 0.5443 / 0.7799 (0.544), d2 0.1170 / 0.2702 / 0.3899 (0.540), d4 0.0585 /
    //   0.1248 / 0.1754 (0.499; the modulations are dampened above d2).
    // - E4b with E4c's AdjustQuantBlockAC, which raises most blocks: d1 0.3120 / 0.6121 / 0.8579 (0.612),
    //   d2 0.1559 / 0.3073 / 0.4288 (0.615), d4 0.0779 / 0.1443 / 0.1949 (0.577).
    // - With E4h's merges, the mean over every block (each strategy's value once per block it covers):
    //   d1 0.3120 / 0.5780 / 0.8579 (0.578), d2 0.1559 / 0.2881 / 0.4288 (0.576), d4 0.0779 / 0.1287 /
    //   0.1949 (0.515). AdjustQuantField mixes the values of each merged block.
    [Fact]
    public void Encode_Photo_HasAnAdaptiveQuantFieldWhoseMeanFollowsTheDistance()
    {
        using Image<Rgb24> bike = JxlLossyEncoderTests.LoadSizeCase("bike-582x453");
        float[] distances = { 1.0f, 2.0f, 4.0f };
        float[] meanTimesD = new float[distances.Length];

        for (int i = 0; i < distances.Length; i++)
        {
            float distance = distances[i];
            using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(bike, JxlLossyDefaultEncoderTests.Lossy(distance)));
            (float min, float mean, float max) = ScaledFieldRange(frame);
            meanTimesD[i] = mean * distance;
            TestContext.Current.TestOutputHelper?.WriteLine($"d{distance}: min {min:0.0000}, mean {mean:0.0000}, max {max:0.0000}, mean * d {meanTimesD[i]:0.0000}");
            Assert.True(min < max, $"The field is constant at {min} at d{distance}");
        }

        foreach (float m in meanTimesD)
        {
            Assert.InRange(m, 0.39f, 0.39f * 2);
            Assert.InRange(m, meanTimesD[0] * 0.85f, meanTimesD[0] * 1.15f);
        }
    }

    // At d0.5 Gaborish is off, so libjxl computes the field at 0.62 d (LossyFrameHeuristics). The field read
    // back is that one, not the field at d: its mean is nearer the mean of SetQuantFieldRect over
    // InitialQuantField(0.31) than over InitialQuantField(0.5), at the frame's global scale. (Means rather
    // than every block, as AdjustQuantBlockAC moves single blocks.)
    [Fact]
    public void Encode_AtHalfDistance_ComputesTheFieldAt062TimesTheDistance()
    {
        const float distance = 0.5f;
        using Image<Rgb24> bike = JxlLossyEncoderTests.LoadSizeCase("bike-582x453");

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(bike, JxlLossyDefaultEncoderTests.Lossy(distance)));

        Assert.False(frame.FrameHeader.LoopFilter.Gab);
        double parsed = MeanRaw(frame);
        double at062 = MeanOf(RawQuantField(bike, distance, distanceForIqf: 0.62f * distance, frame.State.AcStrategy));
        double atD = MeanOf(RawQuantField(bike, distance, distanceForIqf: distance, frame.State.AcStrategy));
        TestContext.Current.TestOutputHelper?.WriteLine($"parsed {parsed:0.00}, at 0.62 d {at062:0.00}, at d {atD:0.00}");
        Assert.True(Math.Abs(parsed - at062) < Math.Abs(parsed - atD) / 4, $"parsed {parsed}, at 0.62 d {at062}, at d {atD}");
    }

    // A 16x16 block takes the maximum of its four values at d1 (the mixer is 1 up to d1.54138) and their
    // mean from d3.31 (the mixer reaches 0); the 8x8 blocks keep theirs.
    [Theory]
    [InlineData(1.0f, 4.0f)]
    [InlineData(4.0f, 2.5f)]
    public void AdjustQuantField_GivesAMultiBlockItsMaximumOrMean(float distance, float expected)
    {
        JxlAcStrategyImage acs = new(4, 2);
        acs.FillDct8();
        acs.Set(0, 0, JxlAcStrategyType.Dct16X16);
        using Buffer2D<float> field = Configuration.Default.MemoryAllocator.Allocate2D<float>(4, 2);
        float[] values = { 1, 2, 5, 6, 3, 4, 7, 8 };
        for (int i = 0; i < values.Length; i++)
        {
            field[i % 4, i / 4] = values[i];
        }

        JxlEncAdaptiveQuantization.AdjustQuantField(acs, new JxlRect(0, 0, 4, 2), distance, field);

        Assert.Equal(new[] { expected, expected, 5, 6, expected, expected, 7, 8 }, new[] { field[0, 0], field[1, 0], field[2, 0], field[3, 0], field[0, 1], field[1, 1], field[2, 1], field[3, 1] });
    }

    // The opsin of an sRGB image as the encoder makes it: XYB, padded to whole blocks by repeating the last
    // column and row.
    internal static Buffer2D<float>[] CreateOpsin(Image<Rgb24> image)
    {
        int xsize = (image.Width + 7) / 8 * 8;
        int ysize = (image.Height + 7) / 8 * 8;
        Buffer2D<float>[] planes = new Buffer2D<float>[3];
        for (int c = 0; c < 3; c++)
        {
            planes[c] = Configuration.Default.MemoryAllocator.Allocate2D<float>(xsize, ysize, AllocationOptions.Clean);
        }

        JxlEncoderInput<Rgb24>.CopyColorPlanes(image, planes);
        float[] premulAbsorb = JxlXybEncoder.ComputePremulAbsorb(255.0f);
        for (int y = 0; y < image.Height; y++)
        {
            JxlXybEncoder.SrgbRowToXyb(
                planes[0].DangerousGetRowSpan(y).Slice(0, image.Width),
                planes[1].DangerousGetRowSpan(y).Slice(0, image.Width),
                planes[2].DangerousGetRowSpan(y).Slice(0, image.Width),
                premulAbsorb);
        }

        foreach (Buffer2D<float> plane in planes)
        {
            for (int y = 0; y < image.Height; y++)
            {
                Span<float> row = plane.DangerousGetRowSpan(y);
                row.Slice(image.Width, xsize - image.Width).Fill(row[image.Width - 1]);
            }

            for (int y = image.Height; y < ysize; y++)
            {
                plane.DangerousGetRowSpan(image.Height - 1).CopyTo(plane.DangerousGetRowSpan(y));
            }
        }

        return planes;
    }

    private static Image<Rgb24> LoadCrop()
    {
        Image<Rgb24> bike = JxlLossyEncoderTests.LoadSizeCase("bike-582x453");
        bike.Mutate(x => x.Crop(new Rectangle(120, 312, 72, 40)));
        return bike;
    }

    // libjxl's call in LossyFrameHeuristics: the field at d, or at 0.62 d without Gaborish.
    private static Buffer2D<float> InitialQuantField(Image<Rgb24> image, float distance, bool gaborish, out Buffer2D<float> mask1x1)
    {
        Buffer2D<float>[] opsin = CreateOpsin(image);
        try
        {
            float distanceForIqf = gaborish ? distance : distance * 0.62f;
            return JxlEncAdaptiveQuantization.InitialQuantField(distanceForIqf, opsin, 1.0f, Configuration.Default.MemoryAllocator, out mask1x1);
        }
        finally
        {
            JxlPlanes.DisposeAll(opsin);
        }
    }

    // The raw quant field the encoder sets at Default before AdjustQuantBlockAC: the field at d with
    // Gaborish (d > 0.5), at 0.62 d without, then AdjustQuantField over the strategies the frame chose,
    // tile by tile (with every block DCT8 it changes nothing).
    internal static int[] RawQuantFieldOf(Image<Rgb24> image, float distance, JxlAcStrategyImage acStrategy = null)
        => RawQuantField(image, distance, distance > 0.5f ? distance : 0.62f * distance, acStrategy);

    // The raw quant field the encoder sets from the field at distanceForIqf, at the global scale of d.
    private static int[] RawQuantField(Image<Rgb24> image, float distance, float distanceForIqf, JxlAcStrategyImage acStrategy = null)
    {
        Buffer2D<float>[] opsin = CreateOpsin(image);
        try
        {
            using Buffer2D<float> field = JxlEncAdaptiveQuantization.InitialQuantField(distanceForIqf, opsin, 1.0f, Configuration.Default.MemoryAllocator, out Buffer2D<float> mask1x1);
            mask1x1.Dispose();
            if (acStrategy != null)
            {
                for (int ty = 0; ty < field.Height; ty += 8)
                {
                    for (int tx = 0; tx < field.Width; tx += 8)
                    {
                        JxlRect tile = new(tx, ty, Math.Min(8, field.Width - tx), Math.Min(8, field.Height - ty));
                        JxlEncAdaptiveQuantization.AdjustQuantField(acStrategy, tile, distance, field);
                    }
                }
            }

            return RawQuantField(field, distance);
        }
        finally
        {
            JxlPlanes.DisposeAll(opsin);
        }
    }

    private static double MeanOf(int[] values)
    {
        double sum = 0;
        foreach (int v in values)
        {
            sum += v;
        }

        return sum / values.Length;
    }

    // The mean of the raw quant field read back over every block: the decoder reads one value per strategy,
    // at its first block, so each value counts once per block the strategy covers.
    private static double MeanRaw(JxlDecodedDcGroups frame)
    {
        double sum = 0;
        for (int by = 0; by < frame.State.YSizeBlocks; by++)
        {
            Span<int> row = frame.State.RawQuantField.DangerousGetRowSpan(by);
            for (int bx = 0; bx < frame.State.XSizeBlocks; bx++)
            {
                JxlAcStrategy acs = frame.State.AcStrategy.Get(bx, by);
                if (acs.IsFirstBlock)
                {
                    sum += (double)row[bx] * acs.CoveredBlocksX * acs.CoveredBlocksY;
                }
            }
        }

        return sum / (frame.State.XSizeBlocks * frame.State.YSizeBlocks);
    }

    // The quant field read back, scaled to libjxl's float field: each raw value times the quantiser's scale.
    private static (float Min, float Mean, float Max) ScaledFieldRange(JxlDecodedDcGroups frame)
    {
        float scale = frame.State.Quantizer.Scale;
        (int min, int max) = frame.QuantFieldRange();
        return (min * scale, (float)MeanRaw(frame) * scale, max * scale);
    }

    private static int[] RawQuantField(Buffer2D<float> field, float distance)
    {
        JxlQuantizer quantizer = new(new JxlDequantMatrices());
        quantizer.ComputeGlobalScaleAndQuant(JxlEncHeuristics.InitialQuantDc(distance), (float)(0.39 / distance), 0);
        int[] raw = new int[field.Width * field.Height];
        quantizer.SetQuantFieldRect(field, new JxlRect(0, 0, field.Width, field.Height), raw, field.Width);
        return raw;
    }

    private static string Sha256(Buffer2D<float> plane)
    {
        byte[] bytes = new byte[plane.Width * plane.Height * 4];
        for (int y = 0; y < plane.Height; y++)
        {
            Span<float> row = plane.DangerousGetRowSpan(y);
            for (int x = 0; x < plane.Width; x++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(((y * plane.Width) + x) * 4), row[x]);
            }
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string Sha256(int[] values)
    {
        byte[] bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
