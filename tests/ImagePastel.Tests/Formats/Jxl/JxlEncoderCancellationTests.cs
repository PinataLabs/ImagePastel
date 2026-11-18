// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Formats.Jxl.Modular;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Formats.Jxl.VarDct;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// A cancelled token stops an encode with <see cref="OperationCanceledException"/> at each long-running
/// site (issue #337), as <see cref="JxlCancellationTests"/> does for the decoder. The sites past the
/// encode's first check are driven directly, with a token cancelled once the work before them is done.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderCancellationTests
{
    // 512x512: four groups of 256 pixels, lossy or lossless.
    private const int Size = 512;

    private static MemoryAllocator Allocator => Configuration.Default.MemoryAllocator;

    // The token is cancelled from the lossy frame's probe, after the EPF search, so the encode is past its
    // start when it is cancelled: the AC groups' tokenization is the next site to check it.
    [Fact]
    public async Task SaveAsJxlAsync_CancelledDuringTheEncode_ThrowsAndWritesNothing()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Size, Size);
        using CancellationTokenSource source = new();
        bool probed = false;
        JxlEncoder encoder = new()
        {
            Distance = 1,
            Effort = JxlEffort.Thorough,
            TestHooks = new()
            {
                ArHeuristicsProbe = (_, _) =>
                {
                    probed = true;
                    source.Cancel();
                },
            },
        };
        using MemoryStream stream = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsJxlAsync(stream, encoder, source.Token));
        Assert.True(probed);
        Assert.Equal(0, stream.Length);
    }

    // The token is cancelled after the last token pass, so the encode is in its last stage when it is
    // cancelled: the histograms of every stream, then the sections. Fastest and Default build the histograms
    // differently (Fastest lossy has no LZ77 and fewer clusters), so both are covered.
    [Theory]
    [InlineData(0f, JxlEffort.Fastest)]
    [InlineData(0f, JxlEffort.Default)]
    [InlineData(1f, JxlEffort.Fastest)]
    [InlineData(1f, JxlEffort.Default)]
    public async Task SaveAsJxlAsync_CancelledBeforeTheSections_ThrowsAndWritesNothing(float distance, JxlEffort effort)
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Size, Size);
        using CancellationTokenSource source = new();
        bool probed = false;
        JxlEncoder encoder = new()
        {
            Distance = distance,
            Effort = effort,
            TestHooks = new()
            {
                EncodeGroupsProbe = () =>
                {
                    probed = true;
                    source.Cancel();
                },
            },
        };
        using MemoryStream stream = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsJxlAsync(stream, encoder, source.Token));
        Assert.True(probed);
        Assert.Equal(0, stream.Length);
    }

    // The histograms of the tree and of every stream: LZ77, the counts and the hybrid uint search.
    [Fact]
    public void ModularHistograms_CancelledToken_Throws()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Size, Size);
        JxlFrameHeader frameHeader = ReadLosslessFrameHeader(image);
        using CancellationTokenSource source = new();
        using JxlModularFrameEncoder encoder = new(frameHeader, JxlSpeedTier.Squirrel, Allocator, source.Token);
        encoder.ComputeEncodingData(frameHeader, frameHeader.NonserializedMetadata.M, image, doColor: true);
        encoder.ComputeTree();
        encoder.ComputeTokens();

        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => encoder.EncodeGlobalInfo(new JxlBitWriter()));
    }

    // Each section writes its streams one at a time, after the histograms of all of them.
    [Fact]
    public void ModularSections_CancelledToken_Throws()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Size, Size);
        JxlFrameHeader frameHeader = ReadLosslessFrameHeader(image);
        using CancellationTokenSource source = new();
        using JxlModularFrameEncoder encoder = new(frameHeader, JxlSpeedTier.Squirrel, Allocator, source.Token);
        encoder.ComputeEncodingData(frameHeader, frameHeader.NonserializedMetadata.M, image, doColor: true);
        encoder.ComputeTree();
        encoder.ComputeTokens();
        JxlBitWriter writer = new();
        encoder.EncodeGlobalInfo(writer);

        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => encoder.EncodeStream(writer, 0));
    }

    // The colour copy of a lossless frame, before its transforms.
    [Fact]
    public void LosslessColorCopy_CancelledToken_Throws()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Size, Size);
        using JxlModularImage gi = JxlModularImage.Create(Allocator, Size, Size, 8, 3);

        Assert.ThrowsAny<OperationCanceledException>(() => JxlEncoderInput<Rgb24>.CopyColorChannels(image, new JxlBitDepth(), gi, Cancelled()));
    }

    // The colour copy and the XYB conversion of a lossy frame, before its first tile.
    [Fact]
    public void LossyColorCopy_CancelledToken_Throws()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Size, Size);
        Buffer2D<float>[] color = AllocatePlanes(Size, Size);
        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => JxlEncoderInput<Rgb24>.CopyColorPlanes(image, color, Cancelled()));
            Assert.ThrowsAny<OperationCanceledException>(() => JxlFrameEncoder.ToXyb(new JxlColorEncodingHeader(), 255f, color, Size, Size, Cancelled()));
        }
        finally
        {
            JxlPlanes.DisposeAll(color);
        }
    }

    // The per-group transforms of a lossless frame: Default tries the palettes and RCTs of each group.
    [Fact]
    public void ModularGroupTransforms_CancelledToken_Throws()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Size, Size);
        JxlFrameHeader frameHeader = ReadLosslessFrameHeader(image);
        using JxlModularFrameEncoder encoder = new(frameHeader, JxlSpeedTier.Hare, Allocator, Cancelled());

        Assert.ThrowsAny<OperationCanceledException>(() => encoder.ComputeEncodingData(frameHeader, frameHeader.NonserializedMetadata.M, image, doColor: true));
    }

    [Fact]
    public void TreeLearning_CancelledToken_Throws()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Size, Size);
        JxlFrameHeader frameHeader = ReadLosslessFrameHeader(image);
        using CancellationTokenSource source = new();
        using JxlModularFrameEncoder encoder = new(frameHeader, JxlSpeedTier.Squirrel, Allocator, source.Token);
        encoder.ComputeEncodingData(frameHeader, frameHeader.NonserializedMetadata.M, image, doColor: true);

        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(encoder.ComputeTree);
    }

    [Fact]
    public void ModularTokens_CancelledToken_Throws()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Size, Size);
        JxlFrameHeader frameHeader = ReadLosslessFrameHeader(image);
        using CancellationTokenSource source = new();
        using JxlModularFrameEncoder encoder = new(frameHeader, JxlSpeedTier.Squirrel, Allocator, source.Token);
        encoder.ComputeEncodingData(frameHeader, frameHeader.NonserializedMetadata.M, image, doColor: true);
        encoder.ComputeTree();

        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(encoder.ComputeTokens);
    }

    [Fact]
    public void AdaptiveQuantizationField_CancelledToken_Throws()
    {
        Buffer2D<float>[] opsin = AllocatePlanes(Size, Size);
        try
        {
            using (Buffer2D<float> field = JxlEncAdaptiveQuantization.InitialQuantField(1f, opsin, 1f, Allocator, CancellationToken.None, out Buffer2D<float> mask1x1))
            {
                mask1x1.Dispose();
            }

            Assert.ThrowsAny<OperationCanceledException>(() => JxlEncAdaptiveQuantization.InitialQuantField(1f, opsin, 1f, Allocator, Cancelled(), out _));
        }
        finally
        {
            JxlPlanes.DisposeAll(opsin);
        }
    }

    // The tile search: the strategy of each block, the quant field and the chroma-from-luma map. Default
    // and Thorough first compute the adaptive quantization field, which checks the token too.
    [Theory]
    [InlineData(JxlEffort.Fastest)]
    [InlineData(JxlEffort.Default)]
    [InlineData(JxlEffort.Thorough)]
    public void LossyFrameHeuristics_CancelledToken_Throws(JxlEffort effort)
    {
        JxlFrameDimensions frameDim = new(Size, Size, 1, 0, 0, false, 1);
        using JxlPassesEncoderState encState = new(frameDim, JxlEncoderCore.ToSpeedTier(effort), 1f, Allocator, Cancelled());
        Buffer2D<float>[] opsin = AllocatePlanes(Size, Size);
        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => JxlEncHeuristics.LossyFrameHeuristics(encState, gaborish: false, opsin, Allocator));
        }
        finally
        {
            JxlPlanes.DisposeAll(opsin);
        }
    }

    // The coefficients of each group and the DC of each LF group.
    [Fact]
    public void VarDctGroups_CancelledToken_Throws()
        => AssertProbeSiteThrows((frameHeader, encState) =>
        {
            using JxlModularFrameEncoder encModular = new(frameHeader, encState.SpeedTier, Allocator, encState.CancellationToken);
            Buffer2D<float>[] opsin = AllocatePlanes(Size, Size);
            try
            {
                JxlFrameEncoder.InitializePassesEncoder(frameHeader, opsin, encState, encModular, Allocator);
            }
            finally
            {
                JxlPlanes.DisposeAll(opsin);
            }
        });

    [Fact]
    public void EpfSharpnessSearch_CancelledToken_Throws()
        => AssertProbeSiteThrows((frameHeader, encState) =>
        {
            Buffer2D<float>[] origOpsin = AllocatePlanes(Size, Size);
            try
            {
                JxlEncHeuristics.ComputeArHeuristics(frameHeader, encState, origOpsin, Allocator);
            }
            finally
            {
                JxlPlanes.DisposeAll(origOpsin);
            }
        });

    [Fact]
    public void Reconstruction_CancelledToken_Throws()
        => AssertProbeSiteThrows((frameHeader, encState) => JxlPlanes.DisposeAll(JxlEncReconstruct.ReconstructImage(frameHeader, encState, Allocator)));

    // A Thorough lossy encode whose token is cancelled in the probe, where the site runs on the encoder's
    // own state. The encode then stops at its next check.
    private static void AssertProbeSiteThrows(Action<JxlFrameHeader, JxlPassesEncoderState> site)
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Size, Size);
        using CancellationTokenSource source = new();
        Exception siteException = null;
        JxlEncoder encoder = new()
        {
            Distance = 1,
            Effort = JxlEffort.Thorough,
            TestHooks = new()
            {
                ArHeuristicsProbe = (frameHeader, encState) =>
                {
                    source.Cancel();
                    siteException = Record.Exception(() => site(frameHeader, encState));
                },
            },
        };
        using MemoryStream stream = new();

        Assert.ThrowsAny<OperationCanceledException>(() => new JxlEncoderCore(encoder, Allocator).Encode(image, stream, source.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(siteException);
    }

    private static JxlFrameHeader ReadLosslessFrameHeader(Image<Rgb24> image)
        => JxlEncodedFrame.ReadFrameHeader(JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.LosslessFastest)).FrameHeader;

    private static Buffer2D<float>[] AllocatePlanes(int width, int height)
    {
        Buffer2D<float>[] planes = new Buffer2D<float>[3];
        for (int c = 0; c < 3; c++)
        {
            planes[c] = Allocator.Allocate2D<float>(width, height, AllocationOptions.Clean);
        }

        return planes;
    }

    private static CancellationToken Cancelled() => new(canceled: true);
}
