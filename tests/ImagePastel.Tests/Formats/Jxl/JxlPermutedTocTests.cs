// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// D13a: a frame whose TOC is permuted, so that its sections are stored in another order than the one they
/// are decoded in (libjxl toc.cc, ReadToc and ReadGroupOffsets, and DecodePermutation in coeff_order.cc).
/// <c>group_order_centre</c> is <c>cjxl -d 1 --group_order=1</c> of <c>Png/Bike.png</c>, which stores the
/// centre groups first (tests/Images/Input/Jxl/Generated/README.md).
/// </summary>
[Trait("Format", "Jxl")]
public class JxlPermutedTocTests
{
    private const string CentreFirst = "group_order_centre";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured on x64 against djxl v0.12.0 (AVX2): worst channel RMSE 1.0e-6, peak 1.2e-5.
    [Fact]
    public void DecodeToPlanes_CentreFirst_MatchesDjxl()
    {
        NpyArray expected = LoadReference(CentreFirst);

        using FileStream stream = File.OpenRead(InputPath(CentreFirst));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);

        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
    }

    // So that the file keeps covering the permutation: one VarDCT frame of 3 x 2 groups whose TOC is
    // permuted, and not with the identity.
    [Fact]
    public void CentreFirst_IsOneVarDctFrameWithAPermutedToc()
    {
        List<(JxlFrameHeader Header, JxlGroupOffsets Toc)> frames = JxlCodestreamFrames.ReadWithToc(File.ReadAllBytes(InputPath(CentreFirst)));

        (JxlFrameHeader header, JxlGroupOffsets toc) = Assert.Single(frames);
        Assert.Equal(JxlFrameEncoding.VarDct, header.Encoding);
        Assert.Equal(6L, header.ToFrameDimensions().NumGroups);
        Assert.Equal(1u, header.Passes.NumPasses);

        // DC global, one DC group, AC global and six AC groups.
        Assert.NotNull(toc.Permutation);
        Assert.Equal(9, toc.Permutation.Length);
        Assert.NotEqual(Enumerable.Range(0, 9).Select(i => (uint)i), toc.Permutation);
    }

    // A hand-built VarDCT frame of three groups, so six sections, stored rotated by one: the Lehmer code
    // 1, 1, 1, 1, 1 (and an implicit 0) is the permutation 1, 2, 3, 4, 5, 0, which stores section i at
    // i + 1 and the last section first. A rotation is not its own inverse, so reading the permutation the
    // wrong way round fails. The first block of AC group 0 has coefficients, so the AC groups differ.
    [Fact]
    public void DecodeToPlanes_RotatedSections_MatchTheUnpermutedFrame()
    {
        float[] unpermuted = DecodePlanes(Frame());

        float[] rotated = DecodePlanes(Frame(Lehmer(5, 1, 1, 1, 1, 1), storedSections: new[] { 5, 0, 1, 2, 3, 4 }));

        Assert.Equal(unpermuted, rotated);
    }

    // A permutation whose Lehmer code ends before the last entry is the identity there: end 0 is the
    // identity.
    [Fact]
    public void DecodeToPlanes_IdentityPermutation_MatchesTheUnpermutedFrame()
        => Assert.Equal(DecodePlanes(Frame()), DecodePlanes(Frame(Lehmer(0))));

    // coeff_order.cc ReadPermutation: the coded end is at most the number of sections, 6.
    [Theory]
    [InlineData(6u, false)]
    [InlineData(7u, true)]
    [InlineData(1000u, true)]
    public void Permutation_EndPastTheSections_Throws(uint end, bool invalid)
        => AssertOutcome(Frame(Lehmer(end, new uint[end])), invalid, "Invalid permutation size");

    // coeff_order.cc ReadPermutation: entry i of the Lehmer code is below 6 - i. Lehmer code 5 at entry 0
    // stores section 0 last and moves the others forward by one.
    [Theory]
    [InlineData(0, 5u, new[] { 1, 2, 3, 4, 5, 0 }, false)]
    [InlineData(0, 6u, null, true)]
    [InlineData(5, 0u, null, false)]
    [InlineData(5, 1u, null, true)]
    public void Permutation_LehmerCodePastItsEnd_Throws(int index, uint value, int[] storedSections, bool invalid)
    {
        uint[] code = new uint[6];
        code[index] = value;
        AssertOutcome(Frame(Lehmer(6, code), storedSections), invalid, "Invalid lehmer code");
    }

    // coeff_order.cc DecodePermutation: the ANS state ends where it started. The histograms are one ANS
    // histogram of the single symbol 0, so the permutation is the identity and the state never changes.
    [Theory]
    [InlineData(0x130000u, false)]
    [InlineData(0x130001u, true)]
    public void Permutation_AnsFinalState_Throws(uint state, bool invalid)
        => AssertOutcome(
            Frame(w =>
            {
                JxlTestVarDct.WriteAnsZeroHistograms(w, 8);
                w.Write(32, state);
            }),
            invalid,
            "Invalid ANS stream");

    // toc.cc ReadToc: the bit budget is checked before the permutation, at 12 bits for each of the 6
    // entries. Here 63 bits are left after the permuted bit.
    [Fact]
    public void Toc_PermutedWithoutTheBitsForItsEntries_Throws()
    {
        JxlTestBitWriter writer = new();
        writer.Bit(true).Bits(63, false);

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => JxlToc.ReadGroupOffsets(6, writer.ToReader()));
        Assert.Contains("Not enough bytes for TOC", ex.Message, StringComparison.Ordinal);
    }

    // The permutation's tokens with the flat prefix code over the 8 permutation contexts: the end, then
    // the Lehmer code up to it.
    private static Action<JxlTestBitWriter> Lehmer(uint end, params uint[] code)
        => w =>
        {
            JxlTestVarDct.WriteFlatPrefixHistograms(w, 8);
            JxlTestVarDct.WriteToken(w, end);
            foreach (uint entry in code)
            {
                JxlTestVarDct.WriteToken(w, entry);
            }
        };

    // 520 x 8: three groups, so DC global, one DC group, AC global and three AC groups.
    private static byte[] Frame(Action<JxlTestBitWriter> permutation = null, int[] storedSections = null)
        => new JxlTestVarDct(520, 8)
        {
            FirstBlockTokens = new uint[] { 3, 2, 2, 2 },
            TocPermutation = permutation,
            StoredSections = storedSections,
        }.ToArray();

    private static float[] DecodePlanes(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        return JxlConformanceImages.ToNpy(decoded).Data;
    }

    private static void AssertOutcome(byte[] codestream, bool invalid, string message)
    {
        if (!invalid)
        {
            DecodePlanes(codestream);
            return;
        }

        using MemoryStream stream = new(codestream);
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    private static string InputPath(string name) => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl");

    private static NpyArray LoadReference(string name) => NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy"));
}
