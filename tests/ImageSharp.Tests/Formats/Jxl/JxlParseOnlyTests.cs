// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Features that no djxl or conformance pixel reference covers (#101, section 5): timecodes and the
/// extra channels the decoder does not render. The decoder parses them and leaves them out of the image,
/// so each test compares a stream that has the feature with the same stream without it. The oracle tests
/// check with djxl that the streams are valid. The ignored boxes are in <see cref="JxlContainerTests"/>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlParseOnlyTests
{
    private const int Width = 24;
    private const int Height = 16;

    // The extra channel types the decoder describes and leaves out of the pixels, after the first alpha,
    // with sample formats, dim_shifts and UTF-8 names that differ from the alpha's.
    private static readonly JxlTestExtraChannel[] DroppedChannels =
    {
        new(JxlExtraChannelType.Depth, "Tiefe ü", Bits: 16, DimShift: 1),
        new(JxlExtraChannelType.Thermal, "thermal", Bits: 32, Float: true, ExponentBits: 8),
        new(JxlExtraChannelType.Cfa, string.Empty, Bits: 12, DimShift: 3),
        new(JxlExtraChannelType.SelectionMask, "mask"),
        new(JxlExtraChannelType.Optional, "Ω optional", Bits: 16, Float: true, ExponentBits: 5),
        new(JxlExtraChannelType.Alpha, "second alpha", Bits: 10),
    };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<int> DroppedChannelIndices() => new(Enumerable.Range(0, DroppedChannels.Length));

    [Fact]
    public void Load_AnimationWithTimecodes_DecodesLikeTheSameAnimationWithout()
    {
        byte[] withTimecodes = TrafficAnimation(timecodes: true);
        byte[] without = TrafficAnimation(timecodes: false);
        Assert.True(withTimecodes.Length > without.Length);

        using Image<Rgba32> image = Image.Load<Rgba32>(Configuration.Default, withTimecodes);
        using Image<Rgba32> expected = Image.Load<Rgba32>(Configuration.Default, without);

        Assert.Equal(3, expected.Frames.Count);
        Assert.Equal(expected.Frames.Count, image.Frames.Count);
        Assert.Equal(new uint[] { 7, 300, 1 }, Durations(image));
        Assert.Equal(Durations(expected), Durations(image));
        AssertSameAnimationHeader(expected.Metadata.GetFormatMetadata(JxlFormat.Instance), image.Metadata.GetFormatMetadata(JxlFormat.Instance));
        for (int i = 0; i < image.Frames.Count; i++)
        {
            using Image<Rgba32> expectedFrame = expected.Frames.CloneFrame(i);
            using Image<Rgba32> frame = image.Frames.CloneFrame(i);
            ImageComparer.Exact.VerifySimilarity(expectedFrame, frame);
        }
    }

    [Fact]
    public void Identify_AnimationWithTimecodes_IdentifiesLikeTheSameAnimationWithout()
    {
        IImageInfo info = Identify(TrafficAnimation(timecodes: true));
        IImageInfo expected = Identify(TrafficAnimation(timecodes: false));

        Assert.Equal(expected.Width, info.Width);
        Assert.Equal(expected.Height, info.Height);
        Assert.Equal(expected.PixelType.BitsPerPixel, info.PixelType.BitsPerPixel);
        AssertSameAnimationHeader(expected.Metadata.GetFormatMetadata(JxlFormat.Instance), info.Metadata.GetFormatMetadata(JxlFormat.Instance));
    }

    [Fact]
    public Task DecodeToPlanes_AnimationWithTimecodes_MatchesDjxl()
        => AssertPlanesMatchDjxlAsync(
            "animation_timecodes.jxl",
            TrafficAnimation(timecodes: true),
            decoded => Assert.Equal(3, decoded.Frames.Count));

    [Theory]
    [MemberData(nameof(DroppedChannelIndices))]
    public void Load_ExtraChannelAfterTheAlpha_IsDescribedAndDroppedFromThePixels(int index)
    {
        JxlTestExtraChannel channel = DroppedChannels[index];

        using Image<Rgba32> image = Image.Load<Rgba32>(Configuration.Default, Ramp(channel));
        using Image<Rgba32> expected = Image.Load<Rgba32>(Configuration.Default, Ramp());

        ImageComparer.Exact.VerifySimilarity(expected, image);
        JxlMetadata metadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(2, metadata.ExtraChannels.Count);
        AssertDescribes(new JxlTestExtraChannel(JxlExtraChannelType.Alpha), metadata.ExtraChannels[0]);
        AssertDescribes(channel, metadata.ExtraChannels[1]);
    }

    [Theory]
    [MemberData(nameof(DroppedChannelIndices))]
    public void Load_ExtraChannelAfterTheAlpha_KeepsThePixelTypeOfTheFileWithout(int index)
    {
        using Image image = Image.Load(Configuration.Default, Ramp(DroppedChannels[index]));
        using Image expected = Image.Load(Configuration.Default, Ramp());

        Assert.IsType(expected.GetType(), image);
    }

    [Fact]
    public void Identify_AllTheDroppedTypes_DescribesThemInStreamOrder()
    {
        JxlMetadata metadata = Identify(Ramp(DroppedChannels)).Metadata.GetFormatMetadata(JxlFormat.Instance);

        Assert.Equal(DroppedChannels.Length + 1, metadata.ExtraChannels.Count);
        AssertDescribes(new JxlTestExtraChannel(JxlExtraChannelType.Alpha), metadata.ExtraChannels[0]);
        for (int i = 0; i < DroppedChannels.Length; i++)
        {
            AssertDescribes(DroppedChannels[i], metadata.ExtraChannels[i + 1]);
        }
    }

    [Fact]
    public void Load_AllTheDroppedTypes_DecodesLikeTheFileWithout()
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(Configuration.Default, Ramp(DroppedChannels));
        using Image<Rgba32> expected = Image.Load<Rgba32>(Configuration.Default, Ramp());

        ImageComparer.Exact.VerifySimilarity(expected, image);
    }

    // The planes of every extra channel, dim_shifts upsampled, match djxl's: the decoder reads them
    // before it drops them from the pixels.
    [Fact]
    public Task DecodeToPlanes_AllTheDroppedTypes_MatchesDjxl()
        => AssertPlanesMatchDjxlAsync(
            "all_dropped_extra_channels.jxl",
            Ramp(DroppedChannels),
            decoded => Assert.Equal(4 + DroppedChannels.Length, decoded.Frames[0].Planes.Count));

    // libjxl's ExtraChannelInfo::VisitFields rejects kUnknown (15) and the reserved values (7 to 14), so
    // the decoder cannot describe them: an unknown type is invalid, as in djxl.
    [Theory]
    [InlineData(7u)]
    [InlineData(14u)]
    [InlineData(15u)]
    public void Identify_UnknownOrReservedExtraChannelType_ThrowsInvalidImageContent(uint type)
    {
        byte[] data = Ramp(new JxlTestExtraChannel((JxlExtraChannelType)type, "?"));

        Assert.Throws<InvalidImageContentException>(() => Identify(data));
        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(Configuration.Default, data));
    }

    [Theory]
    [InlineData(7u)]
    [InlineData(15u)]
    public async Task Djxl_UnknownOrReservedExtraChannelType_FailsToo(uint type)
    {
        string input = await WriteOutputAsync($"extra_channel_type_{type}.jxl", Ramp(new JxlTestExtraChannel((JxlExtraChannelType)type, "?")));

        (int exitCode, string output) = await JxlTools.RunForExitCodeAsync("djxl", CancellationToken, input, Path.ChangeExtension(input, ".npy"));

        Assert.True(exitCode != 0, output);
    }

    // Three frames: the middle one is cropped and blended over the first, which it reads from slot 1, so
    // the canvas carries over. With timecodes, each frame header gains a 32-bit timecode after its
    // duration (0xHHMMSSFF).
    private static byte[] TrafficAnimation(bool timecodes)
        => new JxlTestLayers(Width, Height, animation: true) { TpsNumerator = 30000, TpsDenominator = 1001, NumLoops = 5, Timecodes = timecodes }
            .Add(new JxlTestFrame(JxlTestLayers.Ramp) { Duration = 7, Timecode = 0x01020304, SaveAsReference = 1 })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB)
            {
                Duration = 300,
                Timecode = 0xFFFFFFFF,
                X0 = 4,
                Y0 = 2,
                XSize = 8,
                YSize = 8,
                Color = new(JxlBlendMode.Blend, Source: 1),
                ExtraChannel = new(JxlBlendMode.Replace, Source: 1),
            })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Duration = 1, Timecode = 0 })
            .ToArray();

    // RGBA, then the given extra channels after the alpha. Ramp's tree predicts every channel after the
    // third from the one above it, so the same section decodes for any number of extra channels.
    private static byte[] Ramp(params JxlTestExtraChannel[] extraChannels)
        => new JxlTestLayers(Width, Height) { ExtraChannels = extraChannels }
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp))
            .ToArray();

    private static void AssertSameAnimationHeader(JxlMetadata expected, JxlMetadata actual)
    {
        Assert.Equal(5, actual.LoopCount);
        Assert.Equal(expected.LoopCount, actual.LoopCount);
        Assert.Equal(expected.TicksPerSecondNumerator, actual.TicksPerSecondNumerator);
        Assert.Equal(expected.TicksPerSecondDenominator, actual.TicksPerSecondDenominator);
    }

    private static void AssertDescribes(JxlTestExtraChannel expected, JxlExtraChannel actual)
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal((int)expected.Bits, actual.BitsPerSample);
        Assert.Equal((int)expected.ExponentBits, actual.ExponentBitsPerSample);
        Assert.Equal(expected.Float, actual.IsFloatingPoint);
    }

    private static uint[] Durations(Image image)
    {
        uint[] durations = new uint[image.Frames.Count];
        for (int i = 0; i < durations.Length; i++)
        {
            durations[i] = image.Frames[i].Metadata.GetFormatMetadata(JxlFormat.Instance).Duration;
        }

        return durations;
    }

    // Decodes the codestream with djxl and with DecodeToPlanes, checks the planes' shape, and compares them.
    private static async Task AssertPlanesMatchDjxlAsync(string name, byte[] codestream, Action<JxlDecodedImage> assertShape)
    {
        string input = await WriteOutputAsync(name, codestream);
        string output = Path.ChangeExtension(input, ".npy");

        await JxlTools.RunAsync("djxl", CancellationToken, input, output);

        using MemoryStream stream = new(codestream);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        assertShape(decoded);
        JxlConformanceComparer.AssertMatchesDjxl(NpyArray.Load(output), JxlConformanceImages.ToNpy(decoded));
    }

    private static async Task<string> WriteOutputAsync(string name, byte[] data)
    {
        string path = Path.Combine(TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlParseOnlyTests)), name);
        await File.WriteAllBytesAsync(path, data, CancellationToken);
        return path;
    }

    private static IImageInfo Identify(byte[] data)
    {
        using MemoryStream stream = new(data);
        return Image.Identify(Configuration.Default, stream);
    }
}
