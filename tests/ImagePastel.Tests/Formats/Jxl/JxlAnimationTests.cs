// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Animations: one coalesced, full-canvas frame per displayed frame, as djxl gives by default.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlAnimationTests
{
    private const int Width = 24;
    private const int Height = 16;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static string BlendingDirectory => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "libjxl", "jxl", "blending");

    private static string AnimationFromApngPath => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "animation_from_apng.jxl");

    [Fact]
    public void Load_CroppedTrafficLight_GivesTheFourFramesExactly()
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), Path.Combine(BlendingDirectory, "cropped_traffic_light.jxl"));

        Assert.Equal(4, image.Frames.Count);
        for (int i = 0; i < 4; i++)
        {
            using Image<Rgba32> expected = Image.Load<Rgba32>(Path.Combine(BlendingDirectory, $"cropped_traffic_light_frame-{i}.png"));
            using Image<Rgba32> frame = image.Frames.CloneFrame(i);
            ImageComparer.Exact.VerifySimilarity(expected, frame);
        }
    }

    [Fact]
    public void Load_CroppedTrafficLight_GivesEachFrameItsDuration()
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), Path.Combine(BlendingDirectory, "cropped_traffic_light.jxl"));

        Assert.Equal(new uint[] { 300, 100, 300, 100 }, Durations(image));
        Assert.All(Names(image), name => Assert.Equal(string.Empty, name));
    }

    [Fact]
    public async Task Load_NewtonsCradle_GivesTheDurationsOfTestJson()
    {
        JxlConformanceVector vector = JxlConformanceVector.Load("animation_newtons_cradle");
        string input = await JxlConformanceData.GetInputPathAsync(vector, CancellationToken);

        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), input);

        JxlMetadata metadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(100u, metadata.TicksPerSecondNumerator);
        Assert.Equal(1u, metadata.TicksPerSecondDenominator);
        Assert.Equal(36, vector.Frames.Count);
        uint[] expected = vector.Frames.Select(f => (uint)Math.Round(f.Duration.Value * 100)).ToArray();
        Assert.Equal(expected, Durations(image));
        Assert.Equal(vector.Frames.Select(f => f.Name), Names(image));
    }

    [Fact]
    public void DecodeToPlanes_AnimationFromApng_DropsTheZeroDelayFrameAndMatchesDjxl()
    {
        // make_animation.py writes a four-frame APNG whose second frame has a delay of 0.
        const int apngFrames = 4;
        NpyArray expected = NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", "animation_from_apng.npy"));

        using FileStream stream = File.OpenRead(AnimationFromApngPath);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        Assert.Equal(apngFrames - 1, decoded.Frames.Count);
        JxlConformanceComparer.AssertMatchesDjxl(expected, actual);
    }

    [Fact]
    public void Load_AnimationFromApng_GivesTheApngDelays()
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), AnimationFromApngPath);

        // cjxl counts APNG delays in milliseconds; the zero-delay frame is blended into the 250 ms one.
        JxlMetadata metadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(1000u, metadata.TicksPerSecondNumerator);
        Assert.Equal(1u, metadata.TicksPerSecondDenominator);
        Assert.Equal(new uint[] { 120, 250, 80 }, Durations(image));
    }

    [Fact]
    public void Load_Animation_ReadsTheAnimationHeaderAndFrameNames()
    {
        byte[] codestream = new JxlTestLayers(Width, Height, animation: true) { TpsNumerator = 30000, TpsDenominator = 1001, NumLoops = 3 }
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Duration = 7, Name = "first" })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Duration = 300, Name = "zweites Bild é" })
            .ToArray();

        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), codestream);

        JxlMetadata metadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(3, metadata.LoopCount);
        Assert.Equal(30000u, metadata.TicksPerSecondNumerator);
        Assert.Equal(1001u, metadata.TicksPerSecondDenominator);
        Assert.Equal(new uint[] { 7, 300 }, Durations(image));
        Assert.Equal(new[] { "first", "zweites Bild é" }, Names(image));
    }

    [Fact]
    public void Identify_Animation_ReadsTheAnimationHeader()
    {
        byte[] codestream = new JxlTestLayers(Width, Height, animation: true) { TpsNumerator = 1000, TpsDenominator = 1, NumLoops = 70000 }
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Duration = 7 })
            .ToArray();

        using MemoryStream stream = new(codestream);
        IImageInfo info = Image.Identify(CreateConfiguration(), stream);

        JxlMetadata metadata = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(70000, metadata.LoopCount);
        Assert.Equal(1000u, metadata.TicksPerSecondNumerator);
        Assert.Equal(1u, metadata.TicksPerSecondDenominator);
    }

    [Fact]
    public void Load_StillImage_HasNoAnimationTimingOrFrameDuration()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add) })
            .ToArray();

        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), codestream);

        JxlMetadata metadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(0, metadata.LoopCount);
        Assert.Equal(0u, metadata.TicksPerSecondNumerator);
        Assert.Equal(0u, metadata.TicksPerSecondDenominator);
        Assert.Equal(new uint[] { 0 }, Durations(image));
    }

    [Fact]
    public void Decoder_MaxFrames_DefaultsToAllFrames() => Assert.Equal(uint.MaxValue, new JxlDecoder().MaxFrames);

    // As in the GIF decoder, 0 never matches the count of displayed frames, so every frame is decoded.
    [Theory]
    [InlineData(0u, new uint[] { 300, 100, 300, 100 })]
    [InlineData(1u, new uint[] { 300 })]
    [InlineData(2u, new uint[] { 300, 100 })]
    [InlineData(4u, new uint[] { 300, 100, 300, 100 })]
    [InlineData(5u, new uint[] { 300, 100, 300, 100 })]
    [InlineData(uint.MaxValue, new uint[] { 300, 100, 300, 100 })]
    public void Load_MaxFrames_StopsAfterThatManyDisplayedFrames(uint maxFrames, uint[] durations)
    {
        string path = Path.Combine(BlendingDirectory, "cropped_traffic_light.jxl");
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), path, new JxlDecoder { MaxFrames = maxFrames });

        Assert.Equal(durations, Durations(image));
        for (int i = 0; i < image.Frames.Count; i++)
        {
            using Image<Rgba32> expected = Image.Load<Rgba32>(Path.Combine(BlendingDirectory, $"cropped_traffic_light_frame-{i}.png"));
            using Image<Rgba32> frame = image.Frames.CloneFrame(i);
            ImageComparer.Exact.VerifySimilarity(expected, frame);
        }
    }

    [Fact]
    public async Task DecodeToPlanes_MaxFrames_StopsAfterThatManyDisplayedFrames()
    {
        JxlConformanceVector vector = JxlConformanceVector.Load("animation_newtons_cradle");
        string input = await JxlConformanceData.GetInputPathAsync(vector, CancellationToken);

        await using FileStream stream = File.OpenRead(input);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default, new JxlDecoder { MaxFrames = 2 }).DecodeToPlanes(stream, CancellationToken);

        Assert.Equal(2, decoded.Frames.Count);
    }

    [Fact]
    public void DeepClone_CopiesTheAnimationFields()
    {
        JxlMetadata metadata = new() { LoopCount = 3, TicksPerSecondNumerator = 30000, TicksPerSecondDenominator = 1001 };
        JxlFrameMetadata frameMetadata = new() { Duration = 42, Name = "frame" };

        JxlMetadata clone = (JxlMetadata)metadata.DeepClone();
        JxlFrameMetadata frameClone = (JxlFrameMetadata)frameMetadata.DeepClone();

        Assert.Equal(3, clone.LoopCount);
        Assert.Equal(30000u, clone.TicksPerSecondNumerator);
        Assert.Equal(1001u, clone.TicksPerSecondDenominator);
        Assert.Equal(42u, frameClone.Duration);
        Assert.Equal("frame", frameClone.Name);
    }

    [Fact]
    public void Clone_KeepsTheFrameDurationsAndNames()
    {
        byte[] codestream = new JxlTestLayers(Width, Height, animation: true) { NumLoops = 2 }
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Duration = 7, Name = "a" })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Duration = 9, Name = "b" })
            .ToArray();
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), codestream);

        using Image<Rgba32> clone = image.Clone();

        Assert.Equal(new uint[] { 7, 9 }, Durations(clone));
        Assert.Equal(new[] { "a", "b" }, Names(clone));
        Assert.Equal(2, clone.Metadata.GetFormatMetadata(JxlFormat.Instance).LoopCount);
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

    private static string[] Names(Image image)
    {
        string[] names = new string[image.Frames.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = image.Frames[i].Metadata.GetFormatMetadata(JxlFormat.Instance).Name;
        }

        return names;
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
