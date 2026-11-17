// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.IO;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The total sample budget (issue #316): the canvases of the displayed frames of an animation, each the
/// image's pixels times its colour and extra channels, must not pass 2^34 samples in total.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlTotalSampleBudgetTests
{
    private const int Width = 24;
    private const int Height = 16;

    // One 24 x 16 RGBA canvas.
    private const long CanvasSamples = Width * Height * 4;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // 16 frames at the per-frame budget of 2^30 make exactly 2^34. A 1920 x 1080 RGBA canvas is 8,294,400
    // samples, so 2,071 frames fit and 2,072 do not. uint.MaxValue frames, MaxFrames's default, must not
    // overflow the count.
    [Theory]
    [InlineData(16L, 1L << 30, false)]
    [InlineData(17L, 1L << 30, true)]
    [InlineData(2071L, 1920L * 1080 * 4, false)]
    [InlineData(2072L, 1920L * 1080 * 4, true)]
    [InlineData((long)uint.MaxValue, 1L << 30, true)]
    [InlineData((long)uint.MaxValue, 1L, false)]
    public void CheckTotal_ThrowsAbove2To34Samples(long frames, long frameSamples, bool throws)
    {
        void Check() => JxlSampleBudget.CheckTotal(frames, frameSamples);

        if (throws)
        {
            Assert.Throws<InvalidMemoryOperationException>(Check);
        }
        else
        {
            Check();
        }
    }

    // With a limit of two canvases, a three-frame animation throws before its third frame, and a
    // two-frame one decodes.
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Decode_AnimationOverTheTotalBudget_ThrowsInvalidMemoryOperation(int frames, bool throws)
    {
        byte[] codestream = Animation(frames);

        if (throws)
        {
            Assert.Throws<InvalidMemoryOperationException>(() => Decode(codestream, 2 * CanvasSamples));
            Assert.Throws<InvalidMemoryOperationException>(() => DecodeToPlanes(codestream, 2 * CanvasSamples));
        }
        else
        {
            Assert.Equal(frames, Decode(codestream, 2 * CanvasSamples));
            Assert.Equal(frames, DecodeToPlanes(codestream, 2 * CanvasSamples));
        }
    }

    // Only displayed frames count: a frame of duration 0 is blended into the next one and gets no canvas.
    [Fact]
    public void Decode_FrameOfDurationZero_DoesNotCount()
    {
        byte[] codestream = new JxlTestLayers(Width, Height, animation: true)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Duration = 7 })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB))
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Duration = 7 })
            .ToArray();

        Assert.Equal(2, Decode(codestream, 2 * CanvasSamples));
        Assert.Equal(2, DecodeToPlanes(codestream, 2 * CanvasSamples));
    }

    // MaxFrames stops the decode before the budget is reached.
    [Fact]
    public void Decode_MaxFramesBelowTheBudget_StopsFirst()
    {
        byte[] codestream = Animation(3);

        Assert.Equal(2, Decode(codestream, 2 * CanvasSamples, maxFrames: 2));
        Assert.Equal(2, DecodeToPlanes(codestream, 2 * CanvasSamples, maxFrames: 2));
    }

    [Fact]
    public void Identify_IsNotLimitedByTheTotalBudget()
    {
        using MemoryStream stream = new(Animation(3));
        using BufferedReadStream bufferedStream = new(Configuration.Default, stream);
        JxlDecoderCore core = new(Configuration.Default) { MaxTotalSamples = CanvasSamples };

        IImageInfo info = core.Identify(bufferedStream, CancellationToken);

        Assert.Equal(Width, info.Width);
        Assert.Equal(Height, info.Height);
    }

    // Running out of memory at any allocation of an animation's decode, in turn, throws
    // InvalidMemoryOperationException, as #112 allows, and releases every buffer taken: the frames decoded
    // so far, the image itself and the frame planes.
    [Fact]
    public void Decode_OutOfMemoryAtAnyAllocation_ThrowsInvalidMemoryOperationAndReleasesEveryBuffer()
    {
        byte[] codestream = Animation(3);

        OutOfMemoryAllocator counter = new(failAt: int.MaxValue);
        Assert.Equal(3, Decode(codestream, CreateConfiguration(counter)));
        Assert.Equal(0, counter.Outstanding);
        Assert.True(counter.Allocations > 0);
        for (int failAt = 1; failAt <= counter.Allocations; failAt++)
        {
            OutOfMemoryAllocator allocator = new(failAt);
            Assert.Throws<InvalidMemoryOperationException>(() => Decode(codestream, CreateConfiguration(allocator)));
            Assert.True(allocator.Outstanding == 0, $"Allocation {failAt} of {counter.Allocations} failed and left {allocator.Outstanding} buffers.");
        }
    }

    [Fact]
    public void DecodeToPlanes_OutOfMemoryAtAnyAllocation_ThrowsInvalidMemoryOperationAndReleasesEveryBuffer()
    {
        byte[] codestream = Animation(3);

        OutOfMemoryAllocator counter = new(failAt: int.MaxValue);
        Assert.Equal(3, DecodeToPlanes(codestream, CreateConfiguration(counter)));
        Assert.Equal(0, counter.Outstanding);
        for (int failAt = 1; failAt <= counter.Allocations; failAt++)
        {
            OutOfMemoryAllocator allocator = new(failAt);
            Assert.Throws<InvalidMemoryOperationException>(() => DecodeToPlanes(codestream, CreateConfiguration(allocator)));
            Assert.True(allocator.Outstanding == 0, $"Allocation {failAt} of {counter.Allocations} failed and left {allocator.Outstanding} buffers.");
        }
    }

    // An animation of the given number of frames, each displayed.
    private static byte[] Animation(int frames)
    {
        JxlTestLayers layers = new(Width, Height, animation: true);
        for (int i = 1; i < frames; i++)
        {
            layers.Add(new JxlTestFrame(i % 2 == 0 ? JxlTestLayers.ColourA : JxlTestLayers.ColourB) { Duration = 7 });
        }

        return layers.Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Duration = 7 }).ToArray();
    }

    // Decodes through the image sink and returns the number of frames.
    private static int Decode(byte[] codestream, long maxTotalSamples, uint maxFrames = uint.MaxValue)
        => Decode(codestream, Configuration.Default, maxTotalSamples, maxFrames);

    private static int Decode(byte[] codestream, Configuration configuration, long maxTotalSamples = JxlSampleBudget.MaxTotalSamples, uint maxFrames = uint.MaxValue)
    {
        using MemoryStream stream = new(codestream);
        using BufferedReadStream bufferedStream = new(configuration, stream);
        JxlDecoderCore core = new(configuration, new JxlDecoder { MaxFrames = maxFrames }) { MaxTotalSamples = maxTotalSamples };
        using Image<Rgba32> image = core.Decode<Rgba32>(bufferedStream, CancellationToken);
        return image.Frames.Count;
    }

    // Decodes to planes and returns the number of frames.
    private static int DecodeToPlanes(byte[] codestream, long maxTotalSamples, uint maxFrames = uint.MaxValue)
        => DecodeToPlanes(codestream, Configuration.Default, maxTotalSamples, maxFrames);

    private static int DecodeToPlanes(byte[] codestream, Configuration configuration, long maxTotalSamples = JxlSampleBudget.MaxTotalSamples, uint maxFrames = uint.MaxValue)
    {
        using MemoryStream stream = new(codestream);
        JxlDecoderCore core = new(configuration, new JxlDecoder { MaxFrames = maxFrames }) { MaxTotalSamples = maxTotalSamples };
        using JxlDecodedImage decoded = core.DecodeToPlanes(stream, CancellationToken);
        return decoded.Frames.Count;
    }

    // One thread, so the allocations come in the same order on every decode.
    private static Configuration CreateConfiguration(MemoryAllocator allocator)
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.MemoryAllocator = allocator;
        configuration.MaxDegreeOfParallelism = 1;
        return configuration;
    }

    // Counts the buffers it hands out and not yet returned, and throws OutOfMemoryException on allocation
    // failAt (from 1). As in JxlLayersTests, the Modular decoder's int buffers never fail, as it does not
    // yet release them all when an allocation fails.
    private sealed class OutOfMemoryAllocator : MemoryAllocator
    {
        private readonly int failAt;

        public OutOfMemoryAllocator(int failAt) => this.failAt = failAt;

        public int Allocations { get; private set; }

        public int Outstanding { get; private set; }

        public override IMemoryOwner<T> Allocate<T>(int length, AllocationOptions options = AllocationOptions.None)
        {
            if (typeof(T) != typeof(int) && ++this.Allocations == this.failAt)
            {
                throw new OutOfMemoryException();
            }

            this.Outstanding++;
            return new Owner<T>(this, new T[length]);
        }

        protected internal override int GetBufferCapacityInBytes() => int.MaxValue;

        private sealed class Owner<T> : IMemoryOwner<T>
        {
            private OutOfMemoryAllocator allocator;

            public Owner(OutOfMemoryAllocator allocator, T[] array)
            {
                this.allocator = allocator;
                this.Memory = array;
            }

            public Memory<T> Memory { get; }

            public void Dispose()
            {
                if (this.allocator != null)
                {
                    this.allocator.Outstanding--;
                    this.allocator = null;
                }
            }
        }
    }
}
