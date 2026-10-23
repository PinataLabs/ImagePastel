// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lossy encoding at Default (libjxl effort 5, <c>kHare</c>), slice E4. E4a is the tracer: Gaborish,
/// near-lossless DC, the <c>kACMeta</c> tree for the AC metadata and a learned tree for alpha are libjxl's
/// Hare stages. The later tickets add the adaptive quant field (E4b), the per-block AC quant (E4c), the
/// chroma-from-luma map (E4d), the block context map (E4e), the 8x8 level of the strategy search (E4g) and
/// its merges into blocks of up to 64x64 (E4h).
/// </summary>
/// <remarks>
/// The colour is checked against E3a's interim bound (<see cref="JxlLossyEncoderTests"/>): each channel's
/// RMSE against the source is at most 20 8-bit steps.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlLossyDefaultEncoderTests
{
    // The strategies Hare's search chooses from: DCT8, IDENTITY, DCT2X2 and DCT4X4 at the 8x8 level, and the
    // aligned merges.
    private static readonly JxlAcStrategyType[] HareStrategies =
    {
        JxlAcStrategyType.Dct, JxlAcStrategyType.Identity, JxlAcStrategyType.Dct2X2, JxlAcStrategyType.Dct4X4,
        JxlAcStrategyType.Dct16X8, JxlAcStrategyType.Dct8X16, JxlAcStrategyType.Dct16X16,
        JxlAcStrategyType.Dct32X16, JxlAcStrategyType.Dct16X32, JxlAcStrategyType.Dct32X32,
        JxlAcStrategyType.Dct64X32, JxlAcStrategyType.Dct32X64, JxlAcStrategyType.Dct64X64,
    };

    public static TheoryData<string, float> PhotoReaderCases { get; } = CreatePhotoReaderCases();

    public static TheoryData<string, string, float> AlphaReaderCases { get; } = CreateAlphaReaderCases();

    public static TheoryData<string, string> SizeReaderCases { get; } = CreateSizeReaderCases();

    [Theory]
    [MemberData(nameof(PhotoReaderCases))]
    public async Task Encode_Photo_DecodesThroughEachReaderWithinTheInterimBound(string reader, float distance)
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        using Image<Rgb24> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyDefaultEncoder", $"photo_d{distance}");
        JxlLossyEncoderTests.AssertWithinInterimBound(image, decoded, $"d{distance}, {encoded.Length} bytes");
    }

    // An RGBA photo: the colour is lossy, and the alpha is coded losslessly in Modular with a tree learned
    // at Hare (kLearn, Gradient predictor, a local palette per group). Bike with a hard mask spreads the
    // alpha over its six groups, as a two-colour channel palette; the soft mask has every level.
    [Theory]
    [MemberData(nameof(AlphaReaderCases))]
    public async Task Encode_RgbaPhoto_AlphaIsExactAndColourWithinTheInterimBound(string reader, string mask, float distance)
    {
        using Image<Rgba32> image = JxlLossyAlphaEncoderTests.CreateMaskedImage("bike-582x453", mask);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        using Image<Rgba32> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgba32>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyDefaultEncoder", $"{mask}-bike_d{distance}");
        string label = $"{mask} d{distance}, {encoded.Length} bytes";
        JxlLossyAlphaEncoderTests.AssertAlphaExact(image, decoded, label);
        JxlLossyAlphaEncoderTests.AssertVisibleColourWithinInterimBound(image, decoded, label);
    }

    // Odd sizes, one block, and frames of several groups and LF groups (Bike: six groups; the gradient:
    // nine groups over two LF groups), through each reader.
    [Theory]
    [MemberData(nameof(SizeReaderCases))]
    public async Task Encode_Size_RoundTripsWithinTheInterimBound(string reader, string sizeCase)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(1.0f));

        using Image<Rgb24> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyDefaultEncoder", sizeCase);
        JxlLossyEncoderTests.AssertWithinInterimBound(image, decoded, $"{sizeCase}, {encoded.Length} bytes");
    }

    // libjxl's choices at Hare, read back through the decoder's parse: XYB VarDCT; Gaborish above d0.5
    // (LoopFilterFromParams); the EPF iterations and X quantisation scale as at Fastest; near-lossless DC,
    // so one bit of extra DC precision in every LF group (the gradient has two). The quant field adapts to
    // the image (E4b), and every block is covered by one strategy of the search (E4g, E4h).
    [Theory]
    [InlineData("flower-384x244", 0.5f, false, 0u, 3u)]
    [InlineData("flower-384x244", 1.0f, true, 1u, 3u)]
    [InlineData("flower-384x244", 2.0f, true, 2u, 3u)]
    [InlineData("flower-384x244", 4.0f, true, 3u, 4u)]
    [InlineData("gradient-2049x9", 1.0f, true, 1u, 3u)]
    public void Encode_Photo_IsTheHareFrameOfTheTracer(string sizeCase, float distance, bool expectedGab, uint expectedEpfIters, uint expectedXQmScale)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(distance)));

        JxlFrameHeader header = frame.FrameHeader;
        Assert.Equal(JxlFrameEncoding.VarDct, header.Encoding);
        Assert.True(header.NonserializedMetadata.M.XybEncoded);
        Assert.Equal(JxlColorTransform.Xyb, header.ColorTransform);
        Assert.Equal(expectedGab, header.LoopFilter.Gab);
        Assert.False(header.LoopFilter.GabCustom);
        Assert.Equal(expectedEpfIters, header.LoopFilter.EpfIters);
        Assert.Equal(expectedXQmScale, header.XQmScale);
        Assert.Equal(2u, header.BQmScale);
        Assert.Equal(header.ToFrameDimensions().NumDcGroups, frame.ExtraDcPrecision.Length);
        Assert.All(frame.ExtraDcPrecision, p => Assert.Equal(1, p));

        // E4g, E4h: each block is covered once, by one of the strategies of Hare's search: the four 8x8
        // strategies and the aligned merges up to 64x64 (no DCT4X8, DCT8X4, AFV, DCT32X8, DCT8X32 or
        // DCT128X128 and up).
        foreach (byte b in JxlEncAcStrategyTests.StrategiesOf(frame))
        {
            JxlAcStrategyType strategy = (JxlAcStrategyType)(b >> 1);
            Assert.True(Array.IndexOf(HareStrategies, strategy) >= 0, $"{strategy} is not in Hare's search");
        }

        // E4b: the adaptive quant field varies from block to block.
        (int min, int max) = frame.QuantFieldRange();
        Assert.True(min < max, $"The quant field is constant at {min}");

        // The AC chroma-from-luma map is fitted per tile (Encode_SaturatedPhoto_FitsAChromaFromLumaMap); the DC
        // factors keep their default, as libjxl never fits them.
        JxlColorCorrelation defaultCmap = new(header.ToFrameDimensions().XSize, header.ToFrameDimensions().YSize);
        Assert.Equal(defaultCmap.DcFactors.ToArray(), frame.State.Cmap.DcFactors.ToArray());

        // The block context map (E4e) is chosen for frames of at least 1024·d blocks: the flower's 48x31
        // blocks at d0.5 and d1; the gradient's 257x2 and the flower at d2 and d4 keep the default.
        bool chosen = sizeCase == "flower-384x244" && distance <= 1.0f;
        Assert.Equal(!chosen, frame.State.BlockCtxMap.IsDefault);
    }

    // libjxl's FindBestBlockEntropyModel through the decoder's parse: Bike's 73x57 blocks at d1 get a map of
    // one quant field segment (fewer than 8192·d blocks) with two luma clusters and one chroma cluster
    // (4161 / 1024 / 2 and / 3, clamped), so three contexts. Below 1024·d blocks the map keeps its default
    // bit: Bike at d8, text (40x25 blocks) and bike_alpha at d1.
    [Theory]
    [InlineData("bike", 1.0f, false, 3)]
    [InlineData("bike", 8.0f, true, 15)]
    [InlineData("text", 1.0f, true, 15)]
    [InlineData("bike_alpha", 1.0f, true, 15)]
    public void Encode_CalibrationImage_ChoosesTheBlockContextMapOfLargeFrames(string name, float distance, bool expectedDefault, int expectedNumCtxs)
    {
        string path = Quality.JxlCalibrationTable.Instance.Images.Single(i => i.Name == name).FullPath;
        byte[] encoded;
        if (name == "bike_alpha")
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(path);
            encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));
        }
        else
        {
            using Image<Rgb24> image = Image.Load<Rgb24>(path);
            encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));
        }

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(encoded);

        JxlBlockCtxMap map = frame.State.BlockCtxMap;
        TestContext.Current.TestOutputHelper?.WriteLine($"{name} d{distance}: {frame.State.XSizeBlocks}x{frame.State.YSizeBlocks} blocks, QfThresholds [{string.Join(",", map.QfThresholds)}], NumCtxs {map.NumCtxs}, CtxMap [{string.Join(",", map.CtxMap)}]");
        Assert.Equal(expectedDefault, map.IsDefault);
        Assert.Empty(map.QfThresholds);
        Assert.Equal(expectedNumCtxs, map.NumCtxs);
        Assert.Equal(1, map.NumDcCtxs);
    }

    // At Hare libjxl fits the AC chroma-from-luma map of each 64x64 tile by least squares (the fast
    // FindBestMultiplier), read back through the decoder's parse. The flower crop is saturated, so its tiles
    // predict B from Y with non-zero multipliers; its X multipliers all shrink to 0, as cjxl -e 5 -d 1 gives
    // too. Bike has tiles with non-zero multipliers in both maps (at d1 with E4d's DCT8 blocks and constant
    // field, 73 of its 80 X and 60 of its 80 B multipliers equal cjxl's).
    [Theory]
    [InlineData("flower-384x244", 1.0f, 24, false)]
    [InlineData("flower-384x244", 2.0f, 24, false)]
    [InlineData("flower-384x244", 4.0f, 24, false)]
    [InlineData("bike-582x453", 1.0f, 80, true)]
    public void Encode_SaturatedPhoto_FitsAChromaFromLumaMap(string sizeCase, float distance, int tiles, bool expectYtoX)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(distance)));

        JxlColorCorrelation cmap = frame.State.Cmap;
        TestContext.Current.TestOutputHelper?.WriteLine($"{sizeCase} d{distance}: YtoX [{string.Join(", ", cmap.YtoXMap)}], YtoB [{string.Join(", ", cmap.YtoBMap)}]");
        Assert.Equal(tiles, cmap.YtoXMap.Length);
        Assert.Contains(cmap.YtoBMap, f => f != 0);
        Assert.Equal(expectYtoX, Array.Exists(cmap.YtoXMap, f => f != 0));
    }

    // A grey image has no X (red-green) signal in XYB, so no tile predicts X from Y.
    [Fact]
    public void Encode_GreyImage_KeepsTheYtoXMapZero()
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("flower-384x244");
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                foreach (ref Rgb24 p in accessor.GetRowSpan(y))
                {
                    byte grey = (byte)(((p.R * 77) + (p.G * 150) + (p.B * 29)) >> 8);
                    p = new Rgb24(grey, grey, grey);
                }
            }
        });

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(1.0f)));

        TestContext.Current.TestOutputHelper?.WriteLine($"YtoX [{string.Join(", ", frame.State.Cmap.YtoXMap)}], YtoB [{string.Join(", ", frame.State.Cmap.YtoBMap)}]");
        Assert.All(frame.State.Cmap.YtoXMap, f => Assert.Equal(0, f));
    }

    // The chroma-from-luma map takes the part of B that Y predicts out before B is quantised, so the blue
    // channel of the flower crop at d1 is no worse than with the default map (our decoder). E4a's tracer
    // gave 31,333 bytes and a blue RMSE of 7.92 8-bit steps. E4b's adaptive field at the global scale of
    // 0.39 / d quantises harder, so the bound is now the same encoder with the fit switched off (measured
    // once, not committed): 25,439 bytes and a blue RMSE of 10.48; with the fit, 24,995 bytes and 10.05.
    [Fact]
    public async Task Encode_SaturatedPhoto_BlueErrorDoesNotGrowAgainstTheDefaultMap()
    {
        const double defaultMapBlueRmse = 10.48142761652555;
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("flower-384x244");

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(1.0f));

        using Image<Rgb24> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(JxlEncoderRoundTrip.Reader.Ours, encoded, "LossyDefaultEncoder", "flower_cfl_d1");
        JxlLossyEncoderTests.ChannelErrors errors = JxlLossyEncoderTests.ChannelErrors.Measure(image, decoded);
        TestContext.Current.TestOutputHelper?.WriteLine($"{encoded.Length} bytes: {errors}; blue RMSE {errors.Rmse[2]:R}");
        Assert.True(errors.Rmse[2] <= defaultMapBlueRmse, $"blue RMSE {errors.Rmse[2]:R} > the default map's {defaultMapBlueRmse:R}");
    }

    // AdjustQuantBlockAC (E4c), read back through the decoder's parse of the raw quant field. Each block
    // takes the largest of the quants its Y, X and B coefficients ask for: flat blocks and lone high
    // frequencies raise it, and busy blocks lower it when all three channels are busy. Each block starts
    // from its raw value of E4b's adaptive field (SetQuantFieldRect over InitialQuantField, after
    // AdjustQuantField over the frame's strategies), which this recomputes from the image. Shares of the
    // first blocks on the flower crop (E4h): d0.5 48.9% raised and 1.9% lowered; d1 68.9% and 0%; d2 69.0%
    // and 0% (E4b on E4c, all 8x8: d0.5 64.4% and 1.9%, d1 96.4% and 0%, d2 100% and 0%). Against E4a's
    // constant field the lowered share was larger (d1 4.8%): the adaptive field already gives busy blocks a
    // low quant, near the floor max(4, quant / 2).
    [Theory]
    [InlineData(0.5f, true)]
    [InlineData(1.0f, false)]
    [InlineData(2.0f, false)]
    public void Encode_Photo_AdjustsTheQuantOfEachBlockBothWays(float distance, bool lowersSome)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("flower-384x244");

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(distance)));

        int[] adaptiveQuant = JxlEncAdaptiveQuantizationTests.RawQuantFieldOf(image, distance, frame.State.AcStrategy);
        (int raised, int lowered, int blocks) = frame.QuantFieldAround(adaptiveQuant);
        TestContext.Current.TestOutputHelper?.WriteLine($"d{distance}: raised {raised} ({100.0 * raised / blocks:F1}%), lowered {lowered} ({100.0 * lowered / blocks:F1}%) of {blocks} blocks; range {frame.QuantFieldRange()}");
        Assert.True(raised > 0, "AdjustQuantBlockAC raises no block's quant");
        Assert.True(lowered > 0 == lowersSome, $"AdjustQuantBlockAC lowers {lowered} blocks' quant");
    }

    // The 8x8 strategies other than DCT8 (IDENTITY, DCT2X2, DCT4X4) share coefficient order 1, which libjxl
    // customises at Hare as it does DCT8's (order 0): each channel's coefficients sorted by how often they
    // are zero. The decoder fills an order that is not coded with the natural one, so an order that differs
    // from it was coded.
    [Fact]
    public void Encode_Bike_CodesACustomOrderForTheOther8x8Strategies()
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("bike-582x453");

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(1.0f)));
        frame.DecodeAcGlobal();

        uint[] natural = new uint[64];
        JxlAcStrategy.FromRawStrategy((int)JxlAcStrategyType.Dct2X2).ComputeNaturalCoeffOrder(natural);
        foreach (int ord in new[] { 0, 1 })
        {
            for (int c = 0; c < 3; c++)
            {
                uint[] order = frame.State.CoeffOrders.AsSpan(JxlCoeffOrder.Offset(ord, c), 64).ToArray();
                Assert.Equal(0u, order[0]);
                Assert.Equal(natural.OrderBy(k => k), order.OrderBy(k => k));
                Assert.NotEqual(natural, order);
            }
        }
    }

    // With the merges (E4h) Bike uses every order bucket but 5 (DCT32X8 and DCT8X32, which Hare never
    // chooses). libjxl's ComputeUsedOrders customises the buckets up to 6 (DCT32X16 and DCT16X32) and keeps
    // the natural order for 7 (DCT64X64) and 8 (DCT64X32 and DCT32X64), as it does for blocks bigger than
    // 32x32. Each customised order is a permutation of the natural one with the LLF first.
    [Theory]
    [InlineData(1.0f)]
    [InlineData(4.0f)]
    public void Encode_Bike_CustomisesTheUsedOrdersUpTo32x32(float distance)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("bike-582x453");

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(distance)));
        frame.DecodeAcGlobal();

        int[] usedOrders = UsedOrders(frame);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 6, 7, 8 }, usedOrders);
        foreach (int ord in usedOrders)
        {
            JxlAcStrategy acs = StrategyOfOrder(ord);
            int llf = acs.CoveredBlocksX * acs.CoveredBlocksY;
            uint[] natural = new uint[64 * llf];
            acs.ComputeNaturalCoeffOrder(natural);
            int permuted = 0;
            for (int c = 0; c < 3; c++)
            {
                uint[] order = frame.State.CoeffOrders.AsSpan(JxlCoeffOrder.Offset(ord, c), natural.Length).ToArray();
                Assert.Equal(natural.OrderBy(k => k), order.OrderBy(k => k));
                Assert.Equal(natural.AsSpan(0, llf).ToArray(), order.AsSpan(0, llf).ToArray());
                permuted += order.AsSpan().SequenceEqual(natural) ? 0 : 1;
            }

            // libjxl codes a bucket's three channels when any of them differs from the natural order. It sorts
            // by each position's zero count quantised as (long)(count / sqrt(64 n) + 0.1): with fewer than
            // 0.9 sqrt(64 n) blocks every count is 0 and the order stays natural, so it is not coded (Bike's
            // 19 DCT32X32 blocks at d1, against 28.8).
            int blocks = FirstBlocksOfOrder(frame, ord);
            bool sortable = blocks >= 0.9 * Math.Sqrt(natural.Length);
            TestContext.Current.TestOutputHelper?.WriteLine($"d{distance} order {ord}: {blocks} blocks, {permuted} of 3 channels permuted");
            Assert.True(ord > 6 || !sortable ? permuted == 0 : permuted > 0, $"Order {ord}: {blocks} blocks, {permuted} of 3 channels permuted");
        }
    }

    // Informative: with JXL_TOOLS_DIR, which of the used orders cjxl -e 5 codes on Bike, and whether its
    // coded orders are ours. Its strategy map is ours (JxlEncAcStrategyTests), but its quantised
    // coefficients can differ a little (its FMA builds), so an order may differ in a few places.
    [Fact]
    public async Task Encode_Bike_CodesTheOrdersCjxlCodes()
    {
        JxlTools.GetToolPath("cjxl");
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("bike-582x453");
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", "LossyDefaultEncoder");
        string pngPath = System.IO.Path.Combine(directory, "bike.png");
        string cjxlPath = System.IO.Path.Combine(directory, "bike_e5_d1.jxl");
        image.SaveAsPng(pngPath);
        await JxlTools.RunAsync("cjxl", TestContext.Current.CancellationToken, pngPath, cjxlPath, "-e", "5", "-d", "1", "--container=0");

        using JxlDecodedDcGroups ours = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(1.0f)));
        using JxlDecodedDcGroups theirs = JxlDecodedDcGroups.Decode(await System.IO.File.ReadAllBytesAsync(cjxlPath, TestContext.Current.CancellationToken));
        ours.DecodeAcGlobal();
        theirs.DecodeAcGlobal();

        int[] usedOrders = UsedOrders(ours);
        Assert.Equal(usedOrders, UsedOrders(theirs));
        foreach (int ord in usedOrders)
        {
            JxlAcStrategy acs = StrategyOfOrder(ord);
            int size = 64 * acs.CoveredBlocksX * acs.CoveredBlocksY;
            uint[] natural = new uint[size];
            acs.ComputeNaturalCoeffOrder(natural);
            for (int c = 0; c < 3; c++)
            {
                ReadOnlySpan<uint> ourOrder = ours.State.CoeffOrders.AsSpan(JxlCoeffOrder.Offset(ord, c), size);
                ReadOnlySpan<uint> theirOrder = theirs.State.CoeffOrders.AsSpan(JxlCoeffOrder.Offset(ord, c), size);
                int same = 0;
                for (int k = 0; k < size; k++)
                {
                    same += ourOrder[k] == theirOrder[k] ? 1 : 0;
                }

                TestContext.Current.TestOutputHelper?.WriteLine($"order {ord} channel {c}: ours {(ourOrder.SequenceEqual(natural) ? "natural" : "custom")}, cjxl's {(theirOrder.SequenceEqual(natural) ? "natural" : "custom")}; {same} of {size} positions agree");
                Assert.Equal(theirOrder.SequenceEqual(natural), ourOrder.SequenceEqual(natural));
            }
        }
    }

    // libjxl keeps the natural orders for images under 5x5 blocks in both directions, whatever strategies
    // they use: a 32x32 photo crop (4x4 blocks) that merges into larger blocks.
    [Fact]
    public void Encode_SmallImage_KeepsEveryOrderNatural()
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("flower-32x32");

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(4.0f)));
        frame.DecodeAcGlobal();

        int[] usedOrders = UsedOrders(frame);
        Assert.Contains(usedOrders, ord => ord >= 2);
        foreach (int ord in usedOrders)
        {
            JxlAcStrategy acs = StrategyOfOrder(ord);
            uint[] natural = new uint[64 * acs.CoveredBlocksX * acs.CoveredBlocksY];
            acs.ComputeNaturalCoeffOrder(natural);
            for (int c = 0; c < 3; c++)
            {
                Assert.Equal(natural, frame.State.CoeffOrders.AsSpan(JxlCoeffOrder.Offset(ord, c), natural.Length).ToArray());
            }
        }
    }

    // Customising the orders changes only the orders and the AC tokens: the quantised coefficients, and so
    // the decoded pixels, are those of the natural-order encode of the same image and distance.
    [Theory]
    [InlineData(nameof(JxlEncoderRoundTrip.Reader.Ours))]
    [InlineData(nameof(JxlEncoderRoundTrip.Reader.Djxl))]
    public async Task Encode_CustomOrders_DecodeToThePixelsOfTheNaturalOrderEncode(string reader)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("bike-582x453");

        byte[] custom = JxlEncoderRoundTrip.Encode(image, Lossy(1.0f));
        byte[] natural = JxlEncoderRoundTrip.Encode(image, new JxlEncoder { Distance = 1.0f, Effort = JxlEffort.Default, NaturalCoeffOrders = true });

        Assert.NotEqual(natural, custom);
        JxlEncoderRoundTrip.Reader readerKind = Enum.Parse<JxlEncoderRoundTrip.Reader>(reader);
        using Image<Rgb24> decodedNatural = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(readerKind, natural, "LossyDefaultEncoder", "bike_d1.natural");
        using Image<Rgb24> decodedCustom = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(readerKind, custom, "LossyDefaultEncoder", "bike_d1.custom");
        JxlLossyEncoderTests.ChannelErrors errors = JxlLossyEncoderTests.ChannelErrors.Measure(decodedNatural, decodedCustom);
        Assert.Equal(new[] { 0, 0, 0 }, errors.Peak);
    }

    [Fact]
    public void Encode_LargerDistance_GivesASmallerFile()
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();
        float[] distances = { 0.5f, 1, 2, 4, 8, 16, 25 };

        int[] sizes = Array.ConvertAll(distances, d => JxlEncoderRoundTrip.Encode(image, Lossy(d)).Length);

        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(", ", Array.ConvertAll(distances, d => $"d{d}: {sizes[Array.IndexOf(distances, d)]} B")));
        for (int i = 1; i < sizes.Length; i++)
        {
            Assert.True(sizes[i] < sizes[i - 1], $"d{distances[i]} gives {sizes[i]} bytes, d{distances[i - 1]} gives {sizes[i - 1]}");
        }
    }

    // SHA-256 of this encoder's lossy Default output: the one table of Default pins. It uses no FMA and no
    // C runtime maths on a path that reaches the bytes, so every leg gives the same file. Every E4 ticket
    // changes these on purpose; tickets that run in parallel regenerate the table when they merge.
    [Theory]
    [InlineData("flower-384x244", "", 0.5f, "88E6145ED0606797E70C0BEC85B59FF73562B4A68A4171AC5BA6F731082CDEB5")]
    [InlineData("flower-384x244", "", 1.0f, "3D7638836E201CFA238E5B2D07DCD3F42013A1BB0B4A3B500CAF6973F8F71582")]
    [InlineData("flower-384x244", "", 2.0f, "727DABB0E010B53556C3982039F43A1B48DE68C88DCE5A866ABA599C5D59B096")]
    [InlineData("flower-384x244", "", 4.0f, "75EDE6EBEAB4F214CDAAB2B297E9313AB82893E02DDE285162D35FBA773ECDAA")]
    [InlineData("flower-17x23", "", 1.0f, "02082DB03AA3368FC88DE37F300D21A0A18EB78671F873D84F1DCF36C9841568")]
    [InlineData("dot-1x1", "", 1.0f, "0E0D22FC449A4D7D33375B82BD608513BED6874DF06D79A8F7DA55DE2F61CC42")]
    [InlineData("bike-582x453", "", 1.0f, "E3F5FCE8AECAB4FDC32618FC17ADFE3E0645E58460E6C5BE2B060877EEDD420B")]
    [InlineData("gradient-2049x9", "", 2.0f, "BB61F5256C35D906407A2F732AD242BDA4A47FF75CD40C16D6A83CA0CCC1E834")]
    [InlineData("flower-256x244", "soft", 1.0f, "EBE1EF051831070E6260DB52620153114FE555A2B3C295CBC04D14FC1F2B7148")]
    [InlineData("bike-582x453", "hard", 1.0f, "75A011918D518420BD3960B19DC25EBA58D6A1479AACDEFA4054AB93424F8B01")]
    public void Encode_LossyDefault_IsByteIdenticalToItsPin(string sizeCase, string mask, float distance, string expectedSha256)
    {
        byte[] encoded;
        if (mask.Length == 0)
        {
            using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);
            encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));
        }
        else
        {
            using Image<Rgba32> image = JxlLossyAlphaEncoderTests.CreateMaskedImage(sizeCase, mask);
            encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));
        }

        // Logged in full, so that a ticket that changes the output on purpose can regenerate the table.
        string actual = Convert.ToHexString(SHA256.HashData(encoded));
        TestContext.Current.TestOutputHelper?.WriteLine($"[InlineData(\"{sizeCase}\", \"{mask}\", {distance:0.0#}f, \"{actual}\")]");
        Assert.Equal(expectedSha256, actual);
    }

    internal static JxlEncoder Lossy(float distance) => new() { Distance = distance, Effort = JxlEffort.Default };

    // The order buckets of the strategies the frame uses, in increasing order.
    private static int[] UsedOrders(JxlDecodedDcGroups frame)
    {
        SortedSet<int> used = new();
        for (int by = 0; by < frame.State.YSizeBlocks; by++)
        {
            for (int bx = 0; bx < frame.State.XSizeBlocks; bx++)
            {
                used.Add(JxlCoeffOrder.StrategyOrder[frame.State.AcStrategy.Get(bx, by).RawStrategy]);
            }
        }

        return used.ToArray();
    }

    // How many first blocks of the frame are in the order bucket.
    private static int FirstBlocksOfOrder(JxlDecodedDcGroups frame, int ord)
    {
        int blocks = 0;
        for (int by = 0; by < frame.State.YSizeBlocks; by++)
        {
            for (int bx = 0; bx < frame.State.XSizeBlocks; bx++)
            {
                JxlAcStrategy acs = frame.State.AcStrategy.Get(bx, by);
                blocks += acs.IsFirstBlock && JxlCoeffOrder.StrategyOrder[acs.RawStrategy] == ord ? 1 : 0;
            }
        }

        return blocks;
    }

    // The first strategy, in raw order, of an order bucket: the one whose size the bucket's order has.
    private static JxlAcStrategy StrategyOfOrder(int ord)
    {
        int raw = 0;
        while (JxlCoeffOrder.StrategyOrder[raw] != ord)
        {
            raw++;
        }

        return JxlAcStrategy.FromRawStrategy(raw);
    }

    private static TheoryData<string, float> CreatePhotoReaderCases()
    {
        TheoryData<string, float> cases = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach (float distance in new[] { 1.0f, 2.0f, 4.0f })
            {
                cases.Add(reader.ToString(), distance);
            }
        }

        return cases;
    }

    private static TheoryData<string, string, float> CreateAlphaReaderCases()
    {
        TheoryData<string, string, float> cases = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach (float distance in new[] { 1.0f, 2.0f, 4.0f })
            {
                cases.Add(reader.ToString(), "hard", distance);
            }

            cases.Add(reader.ToString(), "soft", 1.0f);
        }

        return cases;
    }

    private static TheoryData<string, string> CreateSizeReaderCases()
    {
        TheoryData<string, string> cases = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach (string sizeCase in new[] { "flower-17x23", "dot-1x1", "bike-582x453", "gradient-2049x9" })
            {
                cases.Add(reader.ToString(), sizeCase);
            }
        }

        return cases;
    }
}
