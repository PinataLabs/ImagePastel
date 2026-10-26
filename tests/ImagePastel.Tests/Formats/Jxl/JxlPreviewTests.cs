// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Preview frames (D14a): an image whose header has a preview starts with a preview frame, which the
/// decoder skips as libjxl does when the preview is not wanted. The frame's header is read at the
/// preview size and its sections are skipped, so it is never shown, blended or saved to a reference
/// slot. It still counts as a frame that is not shown, which seeds the noise of the frames after it.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlPreviewTests
{
    private const int Width = 24;
    private const int Height = 16;

    // The largest noise strength, 1023/1024, at every intensity.
    private static readonly uint[] StrongLut = Enumerable.Repeat(1023u, 8).ToArray();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The main image is 24 x 16 and the preview 40 x 32, so a size taken from the preview is wrong.
    [Fact]
    public void Identify_WithPreview_GivesTheMainImageSize()
    {
        byte[] codestream = WithPreview(Layers().ToArray(), 40, 32);

        using MemoryStream stream = new(codestream);
        IImageInfo info = new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken);

        Assert.Equal(Width, info.Width);
        Assert.Equal(Height, info.Height);
    }

    // The main frame adds slot 1, which nothing fills, so it decodes to its own samples. The preview asks
    // to be saved to slot 1; were it decoded and saved, the main frame would add its samples.
    [Fact]
    public void Load_WithPreview_DecodesAsWithoutIt()
    {
        byte[] withoutPreview = Layers().ToArray();
        byte[] withPreview = WithPreview(withoutPreview, 40, 32, header => header.SaveAsReference = 1);

        using Image<Rgba32> expected = Image.Load<Rgba32>(CreateConfiguration(), withoutPreview);
        using Image<Rgba32> actual = Image.Load<Rgba32>(CreateConfiguration(), withPreview);

        Assert.Equal(1, actual.Frames.Count);
        AssertSamePixels(expected, actual);
    }

    // preview_noise.jxl is the stream PreviewNoise builds (Generated/README.md). The test writes the
    // stream to its output directory, from which the file was copied, so the file can be made again.
    [Fact]
    public void PreviewNoise_IsTheStreamPreviewNoiseBuilds()
    {
        byte[] built = PreviewNoise();
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlPreviewTests));
        File.WriteAllBytes(Path.Combine(directory, "preview_noise.jxl"), built);

        Assert.Equal(File.ReadAllBytes(InputPath("preview_noise")), built);
    }

    // After the preview comes a hidden frame with noise: a regular frame that is not the last and has no
    // duration. The last frame replaces only a 4 x 4 corner of it, so the hidden frame's noise shows.
    // libjxl counts the preview as a frame that is not shown, so the hidden frame's noise is seeded with a
    // visible frame index of 0 and a non-visible frame index of 2: not 1, as without the preview, nor
    // (1, 1), as if the preview were shown. djxl v0.12.0 gives the expected planes. Measured on x64
    // against djxl with AVX2: worst channel RMSE 6.8e-8, peak 2.4e-7; without the preview, 0.68.
    [Fact]
    public void DecodeToPlanes_PreviewCountsAsNotShownForTheNoise()
    {
        NpyArray expected = LoadReference("preview_noise");
        NpyArray actual = DecodeToNpy(PreviewNoise());
        NpyArray withoutPreview = DecodeToNpy(PreviewNoiseLayers().ToArray());

        JxlConformanceFrameResult result = Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, actual));
        TestContext.Current.TestOutputHelper?.WriteLine($"Worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");

        JxlConformanceFrameResult difference = Assert.Single(JxlConformanceComparer.Measure(withoutPreview, actual));
        TestContext.Current.TestOutputHelper?.WriteLine($"Against the image without the preview: worst channel RMSE {difference.WorstChannelRmse:G3}, peak {difference.PeakError:G3}");
        Assert.True(difference.WorstChannelRmse > 100 * result.WorstChannelRmse, "The preview did not change the noise.");
    }

    // The preview frame's sections are skipped, not decoded: a section of bytes that are not a Modular
    // stream does not stop the decode.
    [Fact]
    public void Load_PreviewSectionThatIsNotModularData_IsSkipped()
    {
        byte[] withoutPreview = Layers().ToArray();
        byte[] withPreview = WithPreview(withoutPreview, 40, 32, section: Enumerable.Repeat((byte)0xFF, 64).ToArray());

        using Image<Rgba32> expected = Image.Load<Rgba32>(CreateConfiguration(), withoutPreview);
        using Image<Rgba32> actual = Image.Load<Rgba32>(CreateConfiguration(), withPreview);

        AssertSamePixels(expected, actual);
    }

    public static TheoryData<string> InvalidPreviewHeaders => new(PreviewHeaderChanges.Keys);

    // libjxl's FrameHeader checks for a preview (frame_header.cc): it must be a regular frame, with
    // kReplace blending on every channel and no custom size or origin.
    [Theory]
    [MemberData(nameof(InvalidPreviewHeaders))]
    public void Decode_InvalidPreviewFrameHeader_Throws(string change)
    {
        byte[] codestream = WithPreview(Layers().ToArray(), 40, 32, PreviewHeaderChanges[change]);

        Assert.Throws<InvalidImageContentException>(() => Decode(codestream));
    }

    // The same frame headers are valid for a frame that is not the preview.
    [Theory]
    [MemberData(nameof(InvalidPreviewHeaders))]
    public void FrameHeader_InvalidOnlyAsThePreview(string change)
    {
        JxlCodecMetadata metadata = JxlCodestreamFrames.ReadImageHeader(Layers().ToArray(), out _);
        JxlFrameHeader header = new(metadata) { Encoding = JxlFrameEncoding.Modular, IsLast = false };
        PreviewHeaderChanges[change](header);
        JxlBitWriter writer = new();
        JxlBundle.Write(header, writer);

        JxlBundle.Read(new JxlBitReader(writer.ToArray()), new JxlFrameHeader(metadata));
        Assert.Throws<InvalidImageContentException>(() => JxlBundle.Read(new JxlBitReader(writer.ToArray()), new JxlFrameHeader(metadata) { NonserializedIsPreview = true }));
    }

    // Every cut inside the preview frame, in its header, its TOC or its section, throws. Identify only
    // reads the image header, so it still gives the size, as libjxl's basic info does.
    [Fact]
    public void Decode_TruncatedPreviewFrame_Throws()
    {
        byte[] withoutPreview = Layers().ToArray();
        byte[] codestream = WithPreview(withoutPreview, 40, 32);
        JxlCodestreamFrames.ReadImageHeader(codestream, out int previewStart);
        JxlCodestreamFrames.ReadImageHeader(withoutPreview, out int firstFrame);
        int previewEnd = codestream.Length - (withoutPreview.Length - firstFrame);

        for (int length = previewStart; length < previewEnd; length++)
        {
            byte[] truncated = codestream[..length];
            Assert.Throws<InvalidImageContentException>(() => Decode(truncated));

            using MemoryStream stream = new(truncated);
            Assert.Equal(Width, new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken).Width);
        }
    }

    // The preview frame's section ends one byte past the end of the codestream.
    [Fact]
    public void Decode_PreviewSectionPastTheEnd_Throws()
    {
        byte[] withoutPreview = Layers().ToArray();
        JxlCodestreamFrames.ReadImageHeader(withoutPreview, out int firstFrame);
        byte[] codestream = WithPreview(withoutPreview, 40, 32, section: new byte[100]);
        byte[] cut = codestream[..^(withoutPreview.Length - firstFrame + 1)];

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => Decode(cut));
        Assert.Contains("preview", ex.Message);
    }

    private static Dictionary<string, Action<JxlFrameHeader>> PreviewHeaderChanges => new()
    {
        ["reference_only"] = header => header.FrameType = JxlFrameType.ReferenceOnly,
        ["skip_progressive"] = header => header.FrameType = JxlFrameType.SkipProgressive,
        ["dc"] = header =>
        {
            header.FrameType = JxlFrameType.Dc;
            header.DcLevel = 1;
        },
        ["colour_blending"] = header => header.BlendingInfo.Mode = JxlBlendMode.Add,
        ["extra_channel_blending"] = header => header.ExtraChannelBlendingInfo[0].Mode = JxlBlendMode.Blend,
        ["custom_size"] = header =>
        {
            header.CustomSizeOrOrigin = true;
            header.FrameSizeX = 40;
            header.FrameSizeY = 32;
        },
    };

    private static void Decode(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        using Image image = new JxlDecoder().Decode(Configuration.Default, stream, CancellationToken);
    }

    /// <summary>
    /// Inserts a preview frame into a bare codestream with no ICC profile: the image header gets a preview
    /// of the given size, and a single-group Modular frame of that size comes before the first frame.
    /// </summary>
    /// <param name="codestream">The codestream.</param>
    /// <param name="previewWidth">The preview width, up to 256.</param>
    /// <param name="previewHeight">The preview height, up to 256.</param>
    /// <param name="configure">Changes the preview frame's header before it is written.</param>
    /// <param name="section">The preview frame's only section; the Modular section of a 16 x 16 RGBA image by default.</param>
    /// <returns>The codestream with a preview.</returns>
    private static byte[] WithPreview(byte[] codestream, int previewWidth, int previewHeight, Action<JxlFrameHeader> configure = null, byte[] section = null)
    {
        JxlCodecMetadata metadata = JxlCodestreamFrames.ReadImageHeader(codestream, out int frameOffset);
        Assert.False(metadata.M.ColorEncoding.WantIcc);
        Assert.False(metadata.M.HavePreview);
        metadata.M.HavePreview = true;
        metadata.M.PreviewSize.Set((ulong)previewWidth, (ulong)previewHeight);

        JxlBitWriter writer = new();
        JxlBundle.WriteCodestreamHeaders(metadata, writer);
        writer.ZeroPadToByte();

        JxlFrameHeader header = new(metadata)
        {
            NonserializedIsPreview = true,
            Encoding = JxlFrameEncoding.Modular,
            IsLast = false,
        };
        configure?.Invoke(header);
        JxlBundle.Write(header, writer);

        section ??= JxlTestLayers.ColourA;
        JxlTocWriter.WriteTocPermutation(writer);
        JxlTocWriter.WriteTocSizes(new[] { (uint)section.Length }, writer);
        writer.AppendByteAligned(section);
        writer.AppendByteAligned(codestream.AsSpan(frameOffset));
        return writer.ToArray();
    }

    private static byte[] PreviewNoise() => WithPreview(PreviewNoiseLayers().ToArray(), 12, 8);

    private static JxlTestLayers PreviewNoiseLayers()
        => new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.Ramp) { NoiseLut = StrongLut })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { XSize = 4, YSize = 4 });

    private static NpyArray DecodeToNpy(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        return JxlConformanceImages.ToNpy(decoded);
    }

    private static string InputPath(string name) => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl");

    private static NpyArray LoadReference(string name) => NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy"));

    private static JxlTestLayers Layers()
        => new JxlTestLayers(Width, Height).Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { Color = new(JxlBlendMode.Add, Source: 1), ExtraChannel = new(JxlBlendMode.Add, Source: 1) });

    private static void AssertSamePixels<TPixel>(Image<TPixel> expected, Image<TPixel> actual)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected[x, y], actual[x, y]);
            }
        }
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
