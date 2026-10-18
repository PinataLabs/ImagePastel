// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using ImageMagick;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Tests.TestUtilities.ReferenceCodecs;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

[Trait("Format", "Jxl")]
public class JxlDecoderTests
{
    private static string DeltaPalettePath => JxlConformanceVector.Load("delta_palette").CommittedInputPath;

    [Fact]
    public void Identify_ReportsSizeAndBitDepth()
    {
        using FileStream stream = File.OpenRead(DeltaPalettePath);

        IImageInfo info = new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None);

        Assert.Equal(555, info.Width);
        Assert.Equal(751, info.Height);
        Assert.Equal(24, info.PixelType.BitsPerPixel);
    }

    [Fact]
    public void Identify_ThroughImage_UsesTheDetector()
    {
        IImageInfo info = Image.Identify(CreateConfiguration(), DeltaPalettePath, out IImageFormat format);

        Assert.Equal(JxlFormat.Instance, format);

        Assert.Equal(555, info.Width);
        Assert.Equal(751, info.Height);
    }

    [Theory]
    [InlineData("delta_palette")]
    [InlineData("lz77_flower")]
    public void Load_Rgb24_MatchesMagickExactly(string id)
    {
        Assert.SkipUnless(MagickFormatInfo.Create(MagickFormat.Jxl)?.SupportsReading == true, "This Magick.NET build has no libjxl.");

        string path = JxlConformanceVector.Load(id).CommittedInputPath;
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), path);
        using FileStream stream = File.OpenRead(path);
        using Image<Rgb24> reference = MagickReferenceDecoder.HighBitDepthInstance.Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None);

        ImageComparer.Exact.VerifySimilarity(reference, image);
    }

    [Theory]
    [InlineData("alpha_nonpremultiplied")]
    [InlineData("alpha_triangles")]
    public void Load_Rgba64_MatchesMagickExactly(string id)
    {
        Assert.SkipUnless(MagickFormatInfo.Create(MagickFormat.Jxl)?.SupportsReading == true, "This Magick.NET build has no libjxl.");

        string path = JxlConformanceVector.Load(id).CommittedInputPath;
        using Image<Rgba64> image = Image.Load<Rgba64>(CreateConfiguration(), path);
        using FileStream stream = File.OpenRead(path);
        using Image<Rgba64> reference = MagickReferenceDecoder.HighBitDepthInstance.Decode<Rgba64>(Configuration.Default, stream, CancellationToken.None);

        ImageComparer.Exact.VerifySimilarity(reference, image);
    }

    [Fact]
    public void Load_Float32_MatchesMagickAt16Bits()
    {
        Assert.SkipUnless(MagickFormatInfo.Create(MagickFormat.Jxl)?.SupportsReading == true, "This Magick.NET build has no libjxl.");

        // Magick returns 16-bit samples, so both sides are clamped to [0, 1] and compared at 16 bits.
        string path = JxlConformanceVector.Load("lossless_pfm").CommittedInputPath;
        using Image<Rgba64> image = Image.Load<Rgba64>(CreateConfiguration(), path);
        using FileStream stream = File.OpenRead(path);
        using Image<Rgba64> reference = MagickReferenceDecoder.HighBitDepthInstance.Decode<Rgba64>(Configuration.Default, stream, CancellationToken.None);

        // Samples on a rounding boundary may land one 16-bit step apart, in each colour channel.
        ImageComparer.Tolerant(0F, perPixelManhattanThreshold: 3).VerifySimilarity(reference, image);
    }

    [Theory]
    [InlineData("alpha_nonpremultiplied", 48)] // 12-bit RGB and alpha
    [InlineData("alpha_triangles", 36)] // 9-bit RGB and alpha
    public void Identify_CountsTheAlphaBits(string id, int bitsPerPixel)
    {
        using FileStream stream = File.OpenRead(JxlConformanceVector.Load(id).CommittedInputPath);

        IImageInfo info = new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None);

        Assert.Equal(1024, info.Width);
        Assert.Equal(1024, info.Height);
        Assert.Equal(bitsPerPixel, info.PixelType.BitsPerPixel);
    }

    [Theory]
    [InlineData(0, false)] // alpha
    [InlineData(1, true)] // depth
    public void Decode_ExtraChannelThatIsNotAlpha_ThrowsNotSupported(int type, bool throws)
    {
        // alpha_triangles' frame, behind a header that gives its extra channel the type.
        byte[] original = File.ReadAllBytes(JxlConformanceVector.Load("alpha_triangles").CommittedInputPath);
        JxlTestBitWriter writer = new JxlTestBitWriter().Write(8, 0xFF).Write(8, 0x0A);
        writer.Bit(false).Write(2, 1).Write(13, 1023).Write(3, 1); // 1024 x 1024
        writer.Bit(false).Bit(false); // image metadata: not all default, no extra fields
        writer.Bit(false).Write(2, 3).Write(6, 8).Bit(true); // 9-bit integer samples, 16-bit buffers suffice
        writer.Write(2, 1); // one extra channel
        writer.Bit(false).Write(2, (ulong)type); // not all default, the type
        writer.Bit(false).Write(2, 3).Write(6, 8); // 9-bit integer samples
        writer.Write(2, 0).Write(2, 0); // no dim shift, no name
        if (type == 0)
        {
            writer.Bit(false); // straight alpha
        }

        writer.Bit(false).Bit(true).Write(2, 0); // not XYB, default colour encoding, no extensions
        writer.Bit(true); // all-default transform data
        writer.ZeroPadToByte();
        byte[] data = writer.ToArray().Concat(original.AsSpan(FrameOffset(original)).ToArray()).ToArray();
        using MemoryStream stream = new(data);

        if (throws)
        {
            Assert.Throws<NotSupportedException>(() => new JxlDecoder().Decode<Rgba64>(Configuration.Default, stream, CancellationToken.None));
        }
        else
        {
            using Image<Rgba64> image = new JxlDecoder().Decode<Rgba64>(Configuration.Default, stream, CancellationToken.None);
            Assert.Equal(1024, image.Width);
        }
    }

    [Fact]
    public void Decode_ProducesTheSameSamplesAsDecodeToPlanes()
    {
        using FileStream stream = File.OpenRead(DeltaPalettePath);
        using JxlDecodedImage planes = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
        using FileStream stream2 = File.OpenRead(DeltaPalettePath);
        using Image<Rgb48> image = new JxlDecoder().Decode<Rgb48>(Configuration.Default, stream2, CancellationToken.None);

        Assert.Equal(planes.Width, image.Width);
        Assert.Equal(planes.Height, image.Height);
        Rgb48 pixel = image[100, 200];
        Assert.Equal(Quantize16(planes.Frames[0].Planes[0].DangerousGetRowSpan(200)[100]), pixel.R);
        Assert.Equal(Quantize16(planes.Frames[0].Planes[1].DangerousGetRowSpan(200)[100]), pixel.G);
        Assert.Equal(Quantize16(planes.Frames[0].Planes[2].DangerousGetRowSpan(200)[100]), pixel.B);
    }

    [Fact]
    public void Decode_Truncated_ThrowsInvalidImageContent()
    {
        byte[] data = File.ReadAllBytes(DeltaPalettePath).AsSpan(0, 1000).ToArray();
        using MemoryStream stream = new(data);

        Assert.Throws<InvalidImageContentException>(() => new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None));
    }

    [Fact]
    public void Decode_BadSignature_ThrowsInvalidImageContent()
    {
        using MemoryStream stream = new(new byte[] { 0xFF, 0x0B, 0, 0, 0, 0 });

        Assert.Throws<InvalidImageContentException>(() => new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None));
    }

    [Fact]
    public void Decode_Cancelled_ThrowsOperationCanceled()
    {
        using FileStream stream = File.OpenRead(DeltaPalettePath);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, cts.Token));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0x0A }, true)]
    [InlineData(new byte[] { 0xFF, 0x0A, 0x72, 0x17 }, true)]
    [InlineData(new byte[] { 0xFF, 0x0B }, false)]
    [InlineData(new byte[] { 0x0A, 0xFF }, false)]
    [InlineData(new byte[] { 0xFF }, false)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A }, true)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A, 0x00, 0x00 }, true)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0B }, false)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20 }, false)]
    public void Detector_RecognisesTheCodestreamAndTheContainer(byte[] header, bool expected)
    {
        IImageFormat format = new JxlImageFormatDetector().DetectFormat(header);

        Assert.Equal(expected, format == JxlFormat.Instance);
    }

    [Fact]
    public void NoJxlTypeIsPublic()
    {
        Type[] publicTypes = typeof(Image).Assembly.GetExportedTypes()
            .Where(t => t.Namespace?.StartsWith("SixLabors.ImageSharp.Formats.Jxl", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Empty(publicTypes);
    }

    [Fact]
    public void DefaultConfiguration_HasNoJxlFormat()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();

        Assert.DoesNotContain(JxlFormat.Instance, configuration.ImageFormats);
        Assert.Null(configuration.ImageFormatsManager.FindFormatByFileExtension("jxl"));
    }

    private static ushort Quantize16(float value) => (ushort)Math.Round(Math.Clamp(value, 0F, 1F) * 65535F);

    // The first byte of the frame in a bare codestream with no ICC profile and no preview.
    private static int FrameOffset(byte[] codestream)
    {
        JxlCodecMetadata metadata = new();
        JxlBitReader reader = new(codestream, 2, codestream.Length - 2);
        JxlBundle.Read(reader, metadata.Size);
        JxlBundle.Read(reader, metadata.M);
        metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;
        JxlBundle.Read(reader, metadata.TransformData);
        reader.JumpToByteBoundary();
        int offset = 2 + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();
        return offset;
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
