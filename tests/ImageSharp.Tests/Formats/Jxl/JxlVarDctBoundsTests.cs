// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// One test per structural bound of libjxl's VarDCT decoder (issue #112), on streams from
/// <see cref="JxlTestVarDct"/>: a stream past the bound throws <see cref="InvalidImageContentException"/>,
/// and a stream at the bound decodes. No other exception escapes.
/// </summary>
/// <remarks>
/// <para>Bounds that no stream can reach:</para>
/// <list type="bullet">
/// <item>Quantiser: libjxl checks neither global_scale nor quant_dc. Their U32 encodings start at 1, so
/// neither can be 0; <see cref="Quantizer_ExtremeValues_Decode"/> decodes both ends.</item>
/// <item>Coefficient orders: a permutation cannot repeat an entry, because it is coded as a Lehmer code,
/// whose every valid code is a permutation. <c>DecodeLehmerCode</c>'s own checks (n != 0,
/// code[i] + i &lt; n) are behind ReadPermutation's checks, which these tests cover; the function is
/// tested on its own in <see cref="JxlVarDctComponentsTests"/>. libjxl's DecodePermutation, with its ANS check
/// (coeff_order.cc:77), is only reached by a permuted TOC; <see cref="JxlPermutedTocTests"/> covers its bounds.</item>
/// <item>Dequantization matrices: the mode has 3 bits and all 8 modes exist, so "Invalid quantization
/// table encoding" cannot happen; the predefined table has 0 bits, so "Invalid predefined table" cannot
/// either; and the number of distance bands has 4 bits, at most 16 of the 17 libjxl allows. Interpolate's
/// position check is a JXL_ENSURE that the band geometry keeps.</item>
/// <item>AC groups: "Frame dimensions are too big for the image." (dec_group.cc) checks that each colour
/// channel's blocks of the group, shifted by its chroma subsampling, lie inside the DC planes, which are
/// <c>xsize_blocks</c> x <c>ysize_blocks</c>. The group's blocks lie inside them, since the groups are
/// counted from the frame's pixel size, which is at most 8 <c>xsize_blocks</c>. A shift only moves the
/// rectangle's corners towards the origin: <c>(x0 &gt;&gt; s) + (xsize &gt;&gt; s) &lt;= (x0 + xsize) &gt;&gt; s</c>.
/// So no stream reaches it; <see cref="AcGroup_SubsampledGroupsAtOddSizes_Decode"/> decodes subsampled
/// frames of several groups at the bound. In libjxl, a frame that uses a DC frame checks against the DC
/// frame's image instead, which can be smaller; <c>JxlFrameDecoder</c> makes that check before the
/// sections, and <see cref="JxlDcFrameTests"/> covers it. The number of histograms is 1 + a bit field, so
/// it is never 0.</item>
/// </list>
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlVarDctBoundsTests
{
    public enum Outcome
    {
        /// <summary>The stream decodes.</summary>
        Decodes,

        /// <summary>The stream is over a bound: <see cref="InvalidImageContentException"/>.</summary>
        Invalid,
    }

    // F16 values.
    private const ushort Zero = 0x0000;
    private const ushort NegativeZero = 0x8000;
    private const ushort SmallestSubnormal = 0x0001; // 2^-24
    private const ushort One = 0x3C00;
    private const ushort MinusOne = 0xBC00;
    private const ushort Four = 0x4400;
    private const ushort AboveFour = 0x4401; // 4.00390625
    private const ushort MinusFour = 0xC400;
    private const ushort BelowMinusFour = 0xC401;
    private const ushort Largest = 0x7BFF; // 65504
    private const ushort Infinity = 0x7C00;
    private const ushort NaN = 0x7E00;

    [Theory]
    [InlineData(8, 8)]
    [InlineData(16, 8)]
    [InlineData(520, 8)]
    public void Builder_DefaultFrame_Decodes(int width, int height)
        => AssertOutcome(new JxlTestVarDct(width, height).ToArray(), Outcome.Decodes);

    // chroma_from_luma.cc: |base correlation| <= 4, for X and for B.
    [Theory]
    [InlineData(Four, Zero, Outcome.Decodes)]
    [InlineData(MinusFour, Zero, Outcome.Decodes)]
    [InlineData(Zero, Four, Outcome.Decodes)]
    [InlineData(Zero, MinusFour, Outcome.Decodes)]
    [InlineData(AboveFour, Zero, Outcome.Invalid)]
    [InlineData(BelowMinusFour, Zero, Outcome.Invalid)]
    [InlineData(Zero, AboveFour, Outcome.Invalid)]
    [InlineData(Zero, BelowMinusFour, Outcome.Invalid)]
    public void ColorCorrelation_BaseCorrelationOutsideFour_Throws(ushort baseX, ushort baseB, Outcome outcome)
        => AssertOutcome(ColorCorrelation(baseX, baseB), outcome, "correlation is out of range");

    // F16Coder::Read: an F16 is finite.
    [Theory]
    [InlineData(Infinity, Zero)]
    [InlineData(Zero, NaN)]
    public void ColorCorrelation_BaseNotFinite_Throws(ushort baseX, ushort baseB)
        => AssertOutcome(ColorCorrelation(baseX, baseB), Outcome.Invalid, "F16 infinity or NaN");

    // entropy_coder.cc: at most 64 block contexts, the product of (thresholds + 1) over the DC thresholds
    // of the three channels and the quant field thresholds.
    [Theory]
    [InlineData(3, 15, 0, 0, Outcome.Decodes)] // 4 x 16
    [InlineData(0, 0, 3, 15, Outcome.Decodes)] // 4 x 16
    [InlineData(4, 12, 0, 0, Outcome.Invalid)] // 5 x 13
    [InlineData(0, 0, 4, 12, Outcome.Invalid)] // 5 x 13
    [InlineData(15, 15, 15, 15, Outcome.Invalid)]
    public void BlockCtxMap_MoreThan64BlockContexts_Throws(int xThresholds, int yThresholds, int bThresholds, int qfThresholds, Outcome outcome)
        => AssertOutcome(BlockCtxMapSize(xThresholds, yThresholds, bThresholds, qfThresholds), outcome, "too big");

    // entropy_coder.cc: at most 16 distinct contexts.
    [Theory]
    [InlineData(16, Outcome.Decodes)]
    [InlineData(17, Outcome.Invalid)]
    public void BlockCtxMap_MoreThan16Contexts_Throws(int numContexts, Outcome outcome)
        => AssertOutcome(BlockCtxMapContexts(numContexts), outcome, "too many distinct contexts");

    // coeff_order.cc ReadPermutation: the DCT8 order keeps its first coefficient, so the coded end is at
    // most 64 - 1.
    [Theory]
    [InlineData(63u, Outcome.Decodes)]
    [InlineData(64u, Outcome.Invalid)]
    [InlineData(1000u, Outcome.Invalid)]
    public void CoeffOrder_PermutationPastItsSize_Throws(uint end, Outcome outcome)
        => AssertOutcome(CoeffOrder(end), outcome, "Invalid permutation size");

    // coeff_order.cc ReadPermutation: entry i of the Lehmer code is below 64 - i.
    [Theory]
    [InlineData(1, 62u, Outcome.Decodes)]
    [InlineData(1, 63u, Outcome.Invalid)]
    [InlineData(63, 0u, Outcome.Decodes)]
    [InlineData(63, 1u, Outcome.Invalid)]
    public void CoeffOrder_LehmerCodePastItsEnd_Throws(int index, uint value, Outcome outcome)
        => AssertOutcome(CoeffOrder((uint)index, (uint)index, value), outcome, "Invalid lehmer code");

    // coeff_order.cc DecodeCoeffOrders: the ANS state of the orders ends where it started.
    [Theory]
    [InlineData(0x130000u, Outcome.Decodes)]
    [InlineData(0x130001u, Outcome.Invalid)]
    public void CoeffOrder_AnsFinalState_Throws(uint state, Outcome outcome)
        => AssertOutcome(CoeffOrderAns(state), outcome, "Invalid ANS stream");

    // quant_weights.cc Decode: the modes with fixed 8x8 weights fit only tables of one block. Table 4
    // (DCT16) is 2 x 2 blocks and table 1 (IDENTITY) one block; the DCT mode fits any table.
    [Theory]
    [InlineData(1, 1, Outcome.Decodes)]
    [InlineData(1, 2, Outcome.Decodes)]
    [InlineData(1, 3, Outcome.Decodes)]
    [InlineData(1, 4, Outcome.Decodes)]
    [InlineData(1, 5, Outcome.Decodes)]
    [InlineData(4, 6, Outcome.Decodes)]
    [InlineData(4, 1, Outcome.Invalid)]
    [InlineData(4, 2, Outcome.Invalid)]
    [InlineData(4, 3, Outcome.Invalid)]
    [InlineData(4, 4, Outcome.Invalid)]
    [InlineData(4, 5, Outcome.Invalid)]
    public void DequantMatrices_ModeForAnotherBlockSize_Throws(int table, int mode, Outcome outcome)
        => AssertOutcome(DequantEncoding(table, mode, One), outcome, "Invalid mode");

    // quant_weights.cc DecodeDctParams: the first distance band of each channel is at least 1e-8.
    [Theory]
    [InlineData(SmallestSubnormal, Outcome.Decodes)]
    [InlineData(Zero, Outcome.Invalid)]
    [InlineData(MinusOne, Outcome.Invalid)]
    public void DequantMatrices_DistanceBandSeedBelow1e8_Throws(ushort seed, Outcome outcome)
        => AssertOutcome(Dequant(0, w => JxlTestVarDct.WriteOneBandDct(w, seed)), outcome, "Distance band seed is too small");

    // quant_weights.cc Decode: the weights of the ID and DCT2 modes and the multipliers of the DCT4 and
    // DCT4X8 modes are at least 1e-8 in magnitude. The first one is the value given, in table 1.
    [Theory]
    [InlineData(1, SmallestSubnormal, Outcome.Decodes)]
    [InlineData(2, SmallestSubnormal, Outcome.Decodes)]
    [InlineData(3, SmallestSubnormal, Outcome.Decodes)]
    [InlineData(4, SmallestSubnormal, Outcome.Decodes)]
    [InlineData(1, MinusOne, Outcome.Decodes)] // the table is not computed
    [InlineData(1, Zero, Outcome.Invalid)]
    [InlineData(1, NegativeZero, Outcome.Invalid)]
    [InlineData(2, Zero, Outcome.Invalid)]
    [InlineData(3, Zero, Outcome.Invalid)]
    [InlineData(4, Zero, Outcome.Invalid)]
    public void DequantMatrices_ParameterOfZero_Throws(int mode, ushort first, Outcome outcome)
        => AssertOutcome(DequantEncoding(1, mode, first), outcome, "is too small");

    // F16Coder::Read: an F16 is finite, in every mode.
    [Theory]
    [InlineData(1, Largest, Outcome.Decodes)]
    [InlineData(5, Largest, Outcome.Decodes)]
    [InlineData(6, Largest, Outcome.Decodes)]
    [InlineData(1, Infinity, Outcome.Invalid)]
    [InlineData(1, NaN, Outcome.Invalid)]
    [InlineData(2, Infinity, Outcome.Invalid)]
    [InlineData(3, Infinity, Outcome.Invalid)]
    [InlineData(4, Infinity, Outcome.Invalid)]
    [InlineData(5, Infinity, Outcome.Invalid)]
    [InlineData(6, Infinity, Outcome.Invalid)]
    public void DequantMatrices_ValueNotFinite_Throws(int mode, ushort first, Outcome outcome)
        => AssertOutcome(DequantEncoding(1, mode, first), outcome, "F16 infinity or NaN");

    // quant_weights.cc ComputeQuantTable: every weight of a table that is used is in [1e-8, 1e8). Table 0
    // (DCT8) is computed for the DCT8 blocks: in the ID mode its weights are the value given times 64, and
    // in the DCT4 mode with one band of 1, weight (0, 1) is 64 over the multiplier, 1.07e8 for 10 times
    // 2^-24 and 9.8e7 for 11 times.
    [Theory]
    [InlineData(1, One, Outcome.Decodes)]
    [InlineData(1, MinusOne, Outcome.Invalid)]
    [InlineData(2, MinusOne, Outcome.Invalid)]
    [InlineData(3, (ushort)0x000B, Outcome.Decodes)]
    [InlineData(3, (ushort)0x000A, Outcome.Invalid)]
    [InlineData(3, MinusOne, Outcome.Invalid)]
    public void DequantMatrices_WeightOutOfRange_Throws(int mode, ushort first, Outcome outcome)
        => AssertOutcome(DequantEncoding(0, mode, first), outcome, "Invalid quantization table");

    // quant_weights.cc GetQuantWeights: each distance band, the previous one times Mult(value), stays at
    // least 1e-8. From a seed of 2^-24 (3.8e-6 after the factor of 64), a second band of -256 gives
    // 1.5e-8 and one of -512 gives 7.4e-9.
    [Theory]
    [InlineData((ushort)0xDC00, Outcome.Decodes)]
    [InlineData((ushort)0xE000, Outcome.Invalid)]
    public void DequantMatrices_DistanceBandBelow1e8_Throws(ushort secondBand, Outcome outcome)
        => AssertOutcome(Dequant(0, w => JxlTestVarDct.WriteDctParams(w.Write(3, 6), SmallestSubnormal, secondBand)), outcome, "Invalid distance bands");

    // quant_weights.cc ComputeQuantTable: the same range for a table of two distance bands, whose second
    // band is the first times Mult(value): 65504 x 64 x 2 is 8.4e6, and 65504 x 64 x 65505 is 2.7e11.
    [Theory]
    [InlineData(One, Outcome.Decodes)]
    [InlineData(Largest, Outcome.Invalid)]
    public void DequantMatrices_DistanceBandOver1e8_Throws(ushort secondBand, Outcome outcome)
        => AssertOutcome(Dequant(0, w => JxlTestVarDct.WriteDctParams(w.Write(3, 6), Largest, secondBand)), outcome, "Invalid quantization table");

    // quant_weights.cc ComputeQuantTable (AFV): the AFV bands, from the weight at index 5, stay at least
    // 1e-8.
    [Theory]
    [InlineData(One, Outcome.Decodes)]
    [InlineData(Zero, Outcome.Invalid)]
    [InlineData(MinusOne, Outcome.Invalid)]
    public void DequantMatrices_AfvBandBelow1e8_Throws(ushort band, Outcome outcome)
        => AssertOutcome(Dequant(0, w => WriteAfv(w, band)), outcome, "Invalid AFV bands");

    // dec_modular.cc DecodeQuantTable: the denominator of a raw table is at least 1e-8. The smallest
    // positive F16, 2^-24, is above it; with entries of 1 its weights are 1.7e7, inside ComputeQuantTable's
    // range.
    [Theory]
    [InlineData(SmallestSubnormal, Outcome.Decodes)]
    [InlineData(One, Outcome.Decodes)]
    [InlineData(Zero, Outcome.Invalid)]
    [InlineData(NegativeZero, Outcome.Invalid)]
    [InlineData(MinusOne, Outcome.Invalid)]
    public void DequantMatrices_RawDenominatorBelow1e8_Throws(ushort denominator, Outcome outcome)
        => AssertOutcome(RawTable(denominator, 1), outcome, "Invalid qtable_den: value too small");

    // dec_modular.cc DecodeQuantTable: every entry of a raw table is positive. The last entry is the
    // value given and the others are 1.
    [Theory]
    [InlineData(1, Outcome.Decodes)]
    [InlineData(0, Outcome.Invalid)]
    [InlineData(-1, Outcome.Invalid)]
    public void DequantMatrices_RawEntryNotPositive_Throws(int last, Outcome outcome)
        => AssertOutcome(RawTable(One, last), outcome, "Invalid raw quantization table");

    // dec_modular.cc DecodeAcMetadata: the count covers every varblock. Two DCT8 blocks need two entries.
    [Theory]
    [InlineData(2, Outcome.Decodes)]
    [InlineData(1, Outcome.Invalid)]
    public void AcMetadata_CountBelowTheVarblocks_Throws(int count, Outcome outcome)
        => AssertOutcome(new JxlTestVarDct(16, 8) { Strategies = new[] { 0, 0 }, AcMetadataCount = count }.ToArray(), outcome, "Corrupted stream");

    // dec_modular.cc DecodeAcMetadata: a chroma-subsampled frame has only varblocks of one block, as a
    // JPEG has. The modes are Cb, Y, Cr. A 16 x 16 frame is 2 x 2 blocks: four DCT4X4 (raw 3) or Identity
    // (raw 1) blocks decode in 4:2:0, and a DCT16X16 (raw 4) does not, nor do two DCT8X16 (raw 7, 2 x 1)
    // in 4:2:2 or two DCT16X8 (raw 6, 1 x 2) with Y subsampled (the "mixed" mode). A 4:4:4 YCbCr frame
    // takes the DCT16X16.
    [Theory]
    [InlineData(new[] { 0, 0, 0 }, new[] { 4 }, Outcome.Decodes)]
    [InlineData(new[] { 0, 1, 0 }, new[] { 3, 3, 3, 3 }, Outcome.Decodes)]
    [InlineData(new[] { 0, 1, 0 }, new[] { 1, 1, 1, 1 }, Outcome.Decodes)]
    [InlineData(new[] { 0, 1, 0 }, new[] { 4 }, Outcome.Invalid)]
    [InlineData(new[] { 0, 2, 0 }, new[] { 7, 7 }, Outcome.Invalid)]
    [InlineData(new[] { 1, 0, 1 }, new[] { 6, 6 }, Outcome.Invalid)]
    public void AcMetadata_StrategyNotFittingChromaSubsampling_Throws(int[] modes, int[] strategies, Outcome outcome)
        => AssertOutcome(new JxlTestVarDct(16, 16) { ChromaSubsampling = modes, Strategies = strategies }.ToArray(), outcome, "AC strategy not compatible with chroma subsampling");

    // dec_group.cc: the subsampled channels of every group lie inside the DC planes (see the remarks). A
    // 520 x 21 frame is three groups, the last of 2 or 1 blocks across, and neither size is a multiple
    // of a subsampled block.
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(0, 2, 0)]
    [InlineData(0, 3, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 3, 0)]
    public void AcGroup_SubsampledGroupsAtOddSizes_Decode(int cb, int y, int cr)
        => AssertOutcome(new JxlTestVarDct(520, 21) { ChromaSubsampling = new[] { cb, y, cr } }.ToArray(), Outcome.Decodes);

    // libjxl ef67fde2 runs adaptive DC smoothing on a chroma-subsampled VarDCT frame without the skip flag.
    // v0.12.0 rejected such a frame in InitFrame ("Non-444 chroma subsampling is not allowed when adaptive
    // DC smoothing is enabled"), so djxl v0.12.0 cannot check it, and the other subsampled streams here
    // have the flag, as every JPEG transcode has.
    [Fact]
    public void Decode_SubsampledFrameWithAdaptiveDcSmoothing_Decodes()
        => AssertOutcome(new JxlTestVarDct(40, 21) { ChromaSubsampling = new[] { 0, 1, 0 }, SkipAdaptiveDcSmoothing = false }.ToArray(), Outcome.Decodes);

    // dec_modular.cc DecodeAcMetadata: the EPF sharpness is in [0, 8).
    [Theory]
    [InlineData(0, Outcome.Decodes)]
    [InlineData(7, Outcome.Decodes)]
    [InlineData(8, Outcome.Invalid)]
    [InlineData(-1, Outcome.Invalid)]
    public void AcMetadata_SharpnessOutside0To7_Throws(int sharpness, Outcome outcome)
        => AssertOutcome(new JxlTestVarDct(8, 8) { Sharpness = sharpness }.ToArray(), outcome, "Corrupted sharpness field");

    // dec_modular.cc DecodeAcMetadata: the raw strategy is in [0, 27). Strategy 26 (DCT128X256, 32 x 16
    // blocks) fills a 256 x 128 frame. Identity (1), AFV0 (14) and AFV3 (17) decode too.
    [Theory]
    [InlineData(8, 8, 0, Outcome.Decodes)]
    [InlineData(8, 8, 1, Outcome.Decodes)]
    [InlineData(8, 8, 14, Outcome.Decodes)]
    [InlineData(8, 8, 17, Outcome.Decodes)]
    [InlineData(256, 128, 26, Outcome.Decodes)]
    [InlineData(8, 8, 27, Outcome.Invalid)]
    [InlineData(8, 8, -1, Outcome.Invalid)]
    public void AcMetadata_UnknownStrategy_Throws(int width, int height, int strategy, Outcome outcome)
        => AssertOutcome(new JxlTestVarDct(width, height) { Strategies = new[] { strategy } }.ToArray(), outcome, "Invalid AC strategy");

    // dec_modular.cc DecodeAcMetadata: a varblock ends inside the frame. DCT8X16 (raw 7) is 2 x 1 blocks
    // and DCT16X8 (raw 6) 1 x 2.
    [Theory]
    [InlineData(16, 8, 7, Outcome.Decodes)]
    [InlineData(8, 16, 6, Outcome.Decodes)]
    [InlineData(8, 8, 7, Outcome.Invalid)]
    [InlineData(8, 8, 6, Outcome.Invalid)]
    public void AcMetadata_VarblockPastTheFrame_Throws(int width, int height, int strategy, Outcome outcome)
        => AssertOutcome(new JxlTestVarDct(width, height) { Strategies = new[] { strategy } }.ToArray(), outcome, "overflow");

    // dec_modular.cc DecodeAcMetadata: a varblock ends inside its AC group of 32 x 32 blocks, here in a
    // frame of 33 blocks.
    [Theory]
    [InlineData(true, 30, Outcome.Decodes)]
    [InlineData(false, 30, Outcome.Decodes)]
    [InlineData(true, 31, Outcome.Invalid)]
    [InlineData(false, 31, Outcome.Invalid)]
    public void AcMetadata_VarblockPastItsAcGroup_Throws(bool horizontal, int position, Outcome outcome)
        => AssertOutcome(AcGroupOverflow(horizontal, position), outcome, "overflow");

    // ac_strategy.h SetNoBoundsCheck: varblocks do not overlap. In 3 x 2 blocks, a DCT16X16 at (1, 0)
    // covers (1, 1), and a DCT8X16 at (0, 1) would cover it too.
    [Theory]
    [InlineData(0, Outcome.Decodes)]
    [InlineData(7, Outcome.Invalid)]
    public void AcMetadata_OverlappingVarblocks_Throw(int secondRow, Outcome outcome)
        => AssertOutcome(new JxlTestVarDct(24, 16) { Strategies = new[] { 0, 4, secondRow } }.ToArray(), outcome, "block overlap");

    // dec_group.cc: the histogram selector is below the number of histogram sets. Three AC groups allow
    // up to four sets, coded in 2 bits; with three, the selector 3 has no set.
    [Theory]
    [InlineData(2, Outcome.Decodes)]
    [InlineData(3, Outcome.Invalid)]
    public void AcGroup_HistogramSelectorOutOfRange_Throws(int selector, Outcome outcome)
        => AssertOutcome(new JxlTestVarDct(520, 8) { NumHistograms = 3, HistogramSelectors = new[] { selector, 0, 1 } }.ToArray(), outcome, "Invalid histogram selector");

    // dec_group.cc DecodeACVarBlock: a DCT8 block has at most 63 non-zero AC coefficients.
    [Theory]
    [InlineData(63, Outcome.Decodes)]
    [InlineData(64, Outcome.Invalid)]
    [InlineData(1000, Outcome.Invalid)]
    public void AcGroup_NonZerosOverTheBlockSize_Throws(int nonZeros, Outcome outcome)
        => AssertOutcome(NonZeros(nonZeros, Math.Min(nonZeros, 63)), outcome, "too large");

    // dec_group.cc DecodeACVarBlock: the block has as many non-zero coefficients as it says.
    [Theory]
    [InlineData(1, 1, Outcome.Decodes)]
    [InlineData(63, 63, Outcome.Decodes)]
    [InlineData(1, 0, Outcome.Invalid)]
    [InlineData(63, 62, Outcome.Invalid)]
    public void AcGroup_FewerNonZerosThanCoded_Throws(int nonZeros, int coded, Outcome outcome)
        => AssertOutcome(NonZeros(nonZeros, coded), outcome, "at end of block");

    // dec_group.cc DecodeGroup: the ANS state of each AC group ends where it started.
    [Theory]
    [InlineData(0x130000u, Outcome.Decodes)]
    [InlineData(0x130001u, Outcome.Invalid)]
    public void AcGroup_AnsFinalState_Throws(uint state, Outcome outcome)
        => AssertOutcome(new JxlTestVarDct(8, 8) { AcAnsState = state }.ToArray(), outcome, "ANS checksum failure");

    // quantizer.cc: libjxl checks neither field, and neither can be 0.
    [Theory]
    [InlineData(1u, 1u)]
    [InlineData(73728u, 65536u)]
    public void Quantizer_ExtremeValues_Decode(uint globalScale, uint quantDc)
        => AssertOutcome(Quantizer(globalScale, quantDc), Outcome.Decodes);

    // Issue #112: whatever bit of a VarDCT stream is wrong, only the allowed exceptions escape. A flip in the
    // image size can also reach the pixel cap, whose InvalidMemoryOperationException is ImageSharp's own.
    // Since D14 nothing throws NotSupportedException: a flip that sets a frame flag libjxl does not define
    // is ignored (D14b), and one that sets HavePreview skips the preview frame (D14a).
    [Theory]
    [InlineData("default")]
    [InlineData("nonzeros_63")]
    [InlineData("order_lehmer_62")]
    [InlineData("block_ctx_map_16_contexts")]
    [InlineData("histogram_selector_2")]
    [InlineData("raw_table_nonzeros")]
    public void Decode_AnyBitFlipped_ThrowsOnlyTheAllowedExceptions(string name)
    {
        byte[] codestream = name switch
        {
            "default" => new JxlTestVarDct(16, 16) { Sharpness = 3 }.ToArray(),
            "nonzeros_63" => NonZeros(63, 63),
            "order_lehmer_62" => CoeffOrder(1, 1, 62, nonZeros: 2),
            "block_ctx_map_16_contexts" => BlockCtxMapContexts(16),
            "raw_table_nonzeros" => RawTableNonZeros(),
            _ => new JxlTestVarDct(520, 8) { NumHistograms = 3, HistogramSelectors = new[] { 2, 0, 1 } }.ToArray(),
        };

        for (int bit = 0; bit < codestream.Length * 8; bit++)
        {
            byte[] flipped = (byte[])codestream.Clone();
            flipped[bit / 8] ^= (byte)(1 << (bit % 8));
            try
            {
                Decode(flipped).Dispose();
            }
            catch (Exception ex) when (ex is InvalidImageContentException or InvalidMemoryOperationException or OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Assert.Fail($"Bit {bit}: {ex}");
            }
        }
    }

    // libjxl rejects the same streams, and decodes the others, including those this port does not support
    // yet.
    [Theory]
    [InlineData("cfl_base_x_4", Outcome.Decodes)]
    [InlineData("cfl_base_x_above_4", Outcome.Invalid)]
    [InlineData("cfl_base_b_below_minus_4", Outcome.Invalid)]
    [InlineData("block_ctx_map_64", Outcome.Decodes)]
    [InlineData("block_ctx_map_65", Outcome.Invalid)]
    [InlineData("block_ctx_map_16_contexts", Outcome.Decodes)]
    [InlineData("block_ctx_map_17_contexts", Outcome.Invalid)]
    [InlineData("order_end_64", Outcome.Decodes)]
    [InlineData("order_end_65", Outcome.Invalid)]
    [InlineData("order_lehmer_62", Outcome.Decodes)]
    [InlineData("order_lehmer_63", Outcome.Invalid)]
    [InlineData("order_ans_state", Outcome.Invalid)]
    [InlineData("dequant_id_for_dct16", Outcome.Invalid)]
    [InlineData("dequant_dct_for_dct16", Outcome.Decodes)]
    [InlineData("dequant_seed_subnormal", Outcome.Decodes)]
    [InlineData("dequant_seed_zero", Outcome.Invalid)]
    [InlineData("dequant_id_weight_zero", Outcome.Invalid)]
    [InlineData("dequant_infinity", Outcome.Invalid)]
    [InlineData("dequant_dct4_multiplier_11", Outcome.Decodes)]
    [InlineData("dequant_dct4_multiplier_10", Outcome.Invalid)]
    [InlineData("dequant_id_negative", Outcome.Invalid)]
    [InlineData("dequant_band_minus_256", Outcome.Decodes)]
    [InlineData("dequant_band_minus_512", Outcome.Invalid)]
    [InlineData("dequant_band_over_1e8", Outcome.Invalid)]
    [InlineData("dequant_afv_band_one", Outcome.Decodes)]
    [InlineData("dequant_afv_band_zero", Outcome.Invalid)]
    [InlineData("dequant_raw_den_subnormal", Outcome.Decodes)]
    [InlineData("dequant_raw_den_zero", Outcome.Invalid)]
    [InlineData("dequant_raw_den_negative", Outcome.Invalid)]
    [InlineData("dequant_raw_entry_1", Outcome.Decodes)]
    [InlineData("dequant_raw_entry_0", Outcome.Invalid)]
    [InlineData("dequant_raw_entry_negative", Outcome.Invalid)]
    [InlineData("ac_metadata_count_1", Outcome.Invalid)]
    [InlineData("sharpness_7", Outcome.Decodes)]
    [InlineData("sharpness_8", Outcome.Invalid)]
    [InlineData("strategy_26", Outcome.Decodes)]
    [InlineData("strategy_27", Outcome.Invalid)]
    [InlineData("strategy_past_frame", Outcome.Invalid)]
    [InlineData("strategy_at_ac_group_end", Outcome.Decodes)]
    [InlineData("strategy_past_ac_group", Outcome.Invalid)]
    [InlineData("strategy_past_ac_group_y", Outcome.Invalid)]
    [InlineData("strategy_overlap", Outcome.Invalid)]
    [InlineData("histogram_selector_2", Outcome.Decodes)]
    [InlineData("histogram_selector_3", Outcome.Invalid)]
    [InlineData("nonzeros_63", Outcome.Decodes)]
    [InlineData("nonzeros_64", Outcome.Invalid)]
    [InlineData("nonzeros_left", Outcome.Invalid)]
    [InlineData("ac_ans_state", Outcome.Invalid)]
    [InlineData("quantizer_max", Outcome.Decodes)]
    [InlineData("strategy_dct16_444", Outcome.Decodes)]
    [InlineData("strategy_dct4_420", Outcome.Decodes)]
    [InlineData("strategy_dct16_420", Outcome.Invalid)]
    [InlineData("strategy_dct8x16_422", Outcome.Invalid)]
    [InlineData("strategy_dct16x8_y_subsampled", Outcome.Invalid)]
    [InlineData("subsampled_groups_420", Outcome.Decodes)]
    [InlineData("subsampled_groups_y", Outcome.Decodes)]
    public async Task Djxl_RejectsTheSameStreams(string name, Outcome outcome)
    {
        byte[] codestream = name switch
        {
            "cfl_base_x_4" => ColorCorrelation(Four, Zero),
            "cfl_base_x_above_4" => ColorCorrelation(AboveFour, Zero),
            "cfl_base_b_below_minus_4" => ColorCorrelation(Zero, BelowMinusFour),
            "block_ctx_map_64" => BlockCtxMapSize(3, 15, 0, 0),
            "block_ctx_map_65" => BlockCtxMapSize(4, 12, 0, 0),
            "block_ctx_map_16_contexts" => BlockCtxMapContexts(16),
            "block_ctx_map_17_contexts" => BlockCtxMapContexts(17),
            "order_end_64" => CoeffOrder(63),
            "order_end_65" => CoeffOrder(64),
            "order_lehmer_62" => CoeffOrder(1, 1, 62),
            "order_lehmer_63" => CoeffOrder(1, 1, 63),
            "order_ans_state" => CoeffOrderAns(0x130001),
            "dequant_id_for_dct16" => DequantEncoding(4, 1, One),
            "dequant_dct_for_dct16" => DequantEncoding(4, 6, One),
            "dequant_seed_subnormal" => Dequant(0, w => JxlTestVarDct.WriteOneBandDct(w, SmallestSubnormal)),
            "dequant_seed_zero" => Dequant(0, w => JxlTestVarDct.WriteOneBandDct(w, Zero)),
            "dequant_id_weight_zero" => DequantEncoding(1, 1, Zero),
            "dequant_infinity" => DequantEncoding(1, 1, Infinity),
            "dequant_dct4_multiplier_11" => DequantEncoding(0, 3, 0x000B),
            "dequant_dct4_multiplier_10" => DequantEncoding(0, 3, 0x000A),
            "dequant_id_negative" => DequantEncoding(0, 1, MinusOne),
            "dequant_band_minus_256" => Dequant(0, w => JxlTestVarDct.WriteDctParams(w.Write(3, 6), SmallestSubnormal, 0xDC00)),
            "dequant_band_minus_512" => Dequant(0, w => JxlTestVarDct.WriteDctParams(w.Write(3, 6), SmallestSubnormal, 0xE000)),
            "dequant_band_over_1e8" => Dequant(0, w => JxlTestVarDct.WriteDctParams(w.Write(3, 6), Largest, Largest)),
            "dequant_afv_band_one" => Dequant(0, w => WriteAfv(w, One)),
            "dequant_afv_band_zero" => Dequant(0, w => WriteAfv(w, Zero)),
            "dequant_raw_den_subnormal" => RawTable(SmallestSubnormal, 1),
            "dequant_raw_den_zero" => RawTable(Zero, 1),
            "dequant_raw_den_negative" => RawTable(MinusOne, 1),
            "dequant_raw_entry_1" => RawTable(One, 1),
            "dequant_raw_entry_0" => RawTable(One, 0),
            "dequant_raw_entry_negative" => RawTable(One, -1),
            "ac_metadata_count_1" => new JxlTestVarDct(16, 8) { Strategies = new[] { 0, 0 }, AcMetadataCount = 1 }.ToArray(),
            "sharpness_7" => new JxlTestVarDct(8, 8) { Sharpness = 7 }.ToArray(),
            "sharpness_8" => new JxlTestVarDct(8, 8) { Sharpness = 8 }.ToArray(),
            "strategy_26" => new JxlTestVarDct(256, 128) { Strategies = new[] { 26 } }.ToArray(),
            "strategy_27" => new JxlTestVarDct(8, 8) { Strategies = new[] { 27 } }.ToArray(),
            "strategy_past_frame" => new JxlTestVarDct(8, 8) { Strategies = new[] { 7 } }.ToArray(),
            "strategy_at_ac_group_end" => AcGroupOverflow(true, 30),
            "strategy_past_ac_group" => AcGroupOverflow(true, 31),
            "strategy_past_ac_group_y" => AcGroupOverflow(false, 31),
            "strategy_overlap" => new JxlTestVarDct(24, 16) { Strategies = new[] { 0, 4, 7 } }.ToArray(),
            "histogram_selector_2" => new JxlTestVarDct(520, 8) { NumHistograms = 3, HistogramSelectors = new[] { 2, 0, 1 } }.ToArray(),
            "histogram_selector_3" => new JxlTestVarDct(520, 8) { NumHistograms = 3, HistogramSelectors = new[] { 3, 0, 1 } }.ToArray(),
            "nonzeros_63" => NonZeros(63, 63),
            "nonzeros_64" => NonZeros(64, 63),
            "nonzeros_left" => NonZeros(1, 0),
            "ac_ans_state" => new JxlTestVarDct(8, 8) { AcAnsState = 0x130001 }.ToArray(),
            "strategy_dct16_444" => new JxlTestVarDct(16, 16) { ChromaSubsampling = new[] { 0, 0, 0 }, Strategies = new[] { 4 } }.ToArray(),
            "strategy_dct4_420" => new JxlTestVarDct(16, 16) { ChromaSubsampling = new[] { 0, 1, 0 }, Strategies = new[] { 3, 3, 3, 3 } }.ToArray(),
            "strategy_dct16_420" => new JxlTestVarDct(16, 16) { ChromaSubsampling = new[] { 0, 1, 0 }, Strategies = new[] { 4 } }.ToArray(),
            "strategy_dct8x16_422" => new JxlTestVarDct(16, 16) { ChromaSubsampling = new[] { 0, 2, 0 }, Strategies = new[] { 7, 7 } }.ToArray(),
            "strategy_dct16x8_y_subsampled" => new JxlTestVarDct(16, 16) { ChromaSubsampling = new[] { 1, 0, 1 }, Strategies = new[] { 6, 6 } }.ToArray(),
            "subsampled_groups_420" => new JxlTestVarDct(520, 21) { ChromaSubsampling = new[] { 0, 1, 0 } }.ToArray(),
            "subsampled_groups_y" => new JxlTestVarDct(520, 21) { ChromaSubsampling = new[] { 1, 0, 1 } }.ToArray(),
            _ => Quantizer(73728, 65536),
        };
        string input = Path.Combine(TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlVarDctBoundsTests)), name + ".jxl");
        await File.WriteAllBytesAsync(input, codestream, TestContext.Current.CancellationToken);

        (int exitCode, string output) = await JxlTools.RunForExitCodeAsync("djxl", TestContext.Current.CancellationToken, input, input + ".npy");

        Assert.True((outcome == Outcome.Invalid) == (exitCode != 0), $"djxl exited with code {exitCode}.{Environment.NewLine}{output}");
    }

    // The builder's frames decode as libjxl decodes them, with the coefficients of a block at the bound.
    [Theory]
    [InlineData("nonzeros_63")]
    [InlineData("order_lehmer_62")]
    [InlineData("raw_table_nonzeros")]
    [InlineData("subsampled_nonzeros_y_420")]
    [InlineData("subsampled_nonzeros_y_422")]
    [InlineData("subsampled_nonzeros_420")]
    public async Task DecodeToPlanes_MatchesDjxl(string name)
    {
        // The subsampled frames have AC coefficients in their first block's Y channel, which is
        // subsampled in the first two ("mixed" modes), so the chroma-upsampling stage spreads it.
        byte[] codestream = name switch
        {
            "nonzeros_63" => NonZeros(63, 63),
            "order_lehmer_62" => CoeffOrder(1, 1, 62, nonZeros: 2),
            "subsampled_nonzeros_y_420" => new JxlTestVarDct(40, 21) { ChromaSubsampling = new[] { 1, 0, 1 }, FirstBlockTokens = NonZeroTokens(9, 9) }.ToArray(),
            "subsampled_nonzeros_y_422" => new JxlTestVarDct(40, 21) { ChromaSubsampling = new[] { 2, 0, 2 }, FirstBlockTokens = NonZeroTokens(9, 9) }.ToArray(),
            "subsampled_nonzeros_420" => new JxlTestVarDct(40, 21) { ChromaSubsampling = new[] { 0, 1, 0 }, FirstBlockTokens = NonZeroTokens(9, 9) }.ToArray(),
            _ => RawTableNonZeros(),
        };
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlVarDctBoundsTests));
        string input = Path.Combine(directory, name + "_planes.jxl");
        string output = Path.Combine(directory, name + "_planes.npy");
        await File.WriteAllBytesAsync(input, codestream, TestContext.Current.CancellationToken);

        await JxlTools.RunAsync("djxl", TestContext.Current.CancellationToken, input, output);

        NpyArray expected = NpyArray.Load(output);
        using JxlDecodedImage decoded = Decode(codestream);
        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
    }

    private static byte[] ColorCorrelation(ushort baseX, ushort baseB)
        => new JxlTestVarDct(8, 8)
        {
            // Not default, the default colour factor 84, the bases, and DC factors of 0.
            ColorCorrelationDc = w => w.Bit(false).Write(2, 0).Write(16, baseX).Write(16, baseB).Write(8, 128).Write(8, 128),
        }.ToArray();

    private static byte[] BlockCtxMapSize(int xThresholds, int yThresholds, int bThresholds, int qfThresholds)
        => new JxlTestVarDct(8, 8) { BlockCtxMap = w => JxlTestVarDct.WriteBlockCtxMap(w, xThresholds, yThresholds, bThresholds, qfThresholds) }.ToArray();

    // A map of no thresholds has 3 x 13 entries; the first numContexts are 0, 1, 2, ... and the rest 0.
    private static byte[] BlockCtxMapContexts(int numContexts)
        => new JxlTestVarDct(8, 8)
        {
            BlockCtxMap = w => JxlTestVarDct.WriteBlockCtxMap(w, 0, 0, 0, 0, contextMap: w =>
            {
                w.Bit(false).Bit(false); // not simple, no move-to-front
                JxlTestVarDct.WriteFlatPrefixHistograms(w, 1);
                for (int i = 0; i < 3 * 13; i++)
                {
                    JxlTestVarDct.WriteToken(w, i < numContexts ? (uint)i : 0);
                }
            }),
        }.ToArray();

    // The DCT8 order is coded (used_orders 1): for each channel, the end of its permutation less the 1
    // coefficient kept, then the Lehmer code from entry 1 to the end, which is 0 but for entry index.
    private static byte[] CoeffOrder(uint end, uint index = 0, uint value = 0, int nonZeros = 0)
        => new JxlTestVarDct(8, 8)
        {
            CoeffOrders = w =>
            {
                w.Write(2, 3).Write(13, 1);
                JxlTestVarDct.WriteFlatPrefixHistograms(w, 8);
                for (int c = 0; c < 3; c++)
                {
                    JxlTestVarDct.WriteToken(w, end);
                    for (uint i = 1; i < Math.Min(end + 1, 64); i++)
                    {
                        JxlTestVarDct.WriteToken(w, i == index ? value : 0);
                    }
                }
            },
            FirstBlockTokens = nonZeros == 0 ? null : Enumerable.Repeat(2u, nonZeros).Prepend((uint)nonZeros).ToArray(),
        }.ToArray();

    // The DCT8 order is coded with an ANS histogram of the single symbol 0, so each permutation is the
    // identity and the state never changes.
    private static byte[] CoeffOrderAns(uint state)
        => new JxlTestVarDct(8, 8)
        {
            CoeffOrders = w =>
            {
                w.Write(2, 3).Write(13, 1);
                JxlTestVarDct.WriteAnsZeroHistograms(w, 8);
                w.Write(32, state);
            },
        }.ToArray();

    private static byte[] Dequant(int table, Action<JxlTestBitWriter> encoding)
        => new JxlTestVarDct(8, 8) { DequantMatrices = w => JxlTestVarDct.WriteDequantMatrices(w, table, encoding) }.ToArray();

    // Table table in mode mode, whose first parameter is first and the others 1, with one distance
    // band of 1 where the mode has bands.
    private static byte[] DequantEncoding(int table, int mode, ushort first)
        => Dequant(table, w =>
        {
            w.Write(3, (ulong)mode);
            int count = mode switch
            {
                1 => 9, // ID: 3 weights per channel
                2 => 18, // DCT2: 6 weights per channel
                3 => 6, // DCT4: 2 multipliers per channel
                4 => 3, // DCT4X8: 1 multiplier per channel
                5 => 27, // AFV: 9 weights per channel
                _ => 0,
            };
            for (int i = 0; i < count; i++)
            {
                w.Write(16, i == 0 ? first : One);
            }

            if (mode is 3 or 4 or 5 or 6)
            {
                JxlTestVarDct.WriteDctParams(w, mode == 6 ? first : One);
            }

            if (mode == 5)
            {
                JxlTestVarDct.WriteDctParams(w, One);
            }
        });

    // A raw DCT8 table with the denominator given, whose entries are 1 but for the last.
    private static byte[] RawTable(ushort denominator, int last)
        => Dequant(0, w => JxlTestVarDct.WriteRawTable(w, denominator, i => i == (3 * 64) - 1 ? last : 1));

    // A raw DCT8 table of a different entry for each coefficient and channel, with a denominator of 2^-10
    // (F16 0x1400), so its weights range from 1024/17 to 1024; and a first block whose 63 AC coefficients
    // of Y are all 1, so that each weight scales its own coefficient.
    private static byte[] RawTableNonZeros()
    {
        List<uint> tokens = new() { 63 };
        tokens.AddRange(Enumerable.Repeat(2u, 63));
        return new JxlTestVarDct(8, 8)
        {
            DequantMatrices = w => JxlTestVarDct.WriteDequantMatrices(w, 0, t => JxlTestVarDct.WriteRawTable(t, 0x1400, i => 1 + (i % 17))),
            FirstBlockTokens = tokens.ToArray(),
        }.ToArray();
    }

    // AFV weights of 1, but for the first AFV band (index 5), then the DCT and 4x4 bands of 1.
    private static void WriteAfv(JxlTestBitWriter w, ushort band)
    {
        w.Write(3, 5);
        for (int c = 0; c < 3; c++)
        {
            for (int i = 0; i < 9; i++)
            {
                w.Write(16, i == 5 ? band : One);
            }
        }

        JxlTestVarDct.WriteDctParams(w, One);
        JxlTestVarDct.WriteDctParams(w, One);
    }

    // A column or row of 33 blocks: DCT8 blocks, and a varblock of two blocks along it at position.
    private static byte[] AcGroupOverflow(bool horizontal, int position)
    {
        int[] strategies = Enumerable.Repeat(0, 32).ToArray();
        strategies[position] = horizontal ? 7 : 6;
        return new JxlTestVarDct(horizontal ? 264 : 8, horizontal ? 8 : 264) { Strategies = strategies }.ToArray();
    }

    // The first block's Y channel says nonZeros, then has coded non-zero coefficients (1) and zeros, up
    // to where the decoder stops: the last non-zero, or the end of the block.
    private static byte[] NonZeros(int nonZeros, int coded)
        => new JxlTestVarDct(8, 8) { FirstBlockTokens = NonZeroTokens(nonZeros, coded) }.ToArray();

    private static uint[] NonZeroTokens(int nonZeros, int coded)
    {
        List<uint> tokens = new() { (uint)nonZeros };
        int left = nonZeros;
        for (int k = 1; k < 64 && left != 0 && nonZeros <= 63; k++)
        {
            bool nonZero = k <= coded;
            tokens.Add(nonZero ? 2u : 0u);
            left -= nonZero ? 1 : 0;
        }

        return tokens.ToArray();
    }

    private static byte[] Quantizer(uint globalScale, uint quantDc)
        => new JxlTestVarDct(8, 8)
        {
            Quantizer = w =>
            {
                if (globalScale == 1)
                {
                    w.Write(2, 0).Write(11, 0); // BitsOffset(11, 1)
                }
                else
                {
                    w.Write(2, 3).Write(16, globalScale - 8193); // BitsOffset(16, 8193)
                }

                if (quantDc == 1)
                {
                    w.Write(2, 1).Write(5, 0); // BitsOffset(5, 1)
                }
                else
                {
                    w.Write(2, 3).Write(16, quantDc - 1); // BitsOffset(16, 1)
                }
            },
        }.ToArray();

    private static JxlDecodedImage Decode(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        return new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
    }

    private static void AssertOutcome(byte[] codestream, Outcome outcome, string message = null)
    {
        if (outcome == Outcome.Decodes)
        {
            using JxlDecodedImage decoded = Decode(codestream);
            Assert.Single(decoded.Frames);
            return;
        }

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => Decode(codestream));
        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }
}
