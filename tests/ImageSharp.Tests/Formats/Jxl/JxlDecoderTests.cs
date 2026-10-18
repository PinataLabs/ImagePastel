// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using ImageMagick;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jxl;
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

    // The container is not detected yet.
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20 }, false)]
    public void Detector_RecognisesTheBareCodestream(byte[] header, bool expected)
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

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
