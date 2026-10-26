// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The <c>SaveAsJxl</c> and <c>SaveAsJxlAsync</c> extensions with <see cref="Configuration.Default"/>, as a
/// user reaches the encoder (E6a, #331; the done-check, #335). Each file is read back by the default
/// configuration's decoder.
/// </summary>
[Trait("Format", "Jxl")]
public class ImageExtensionsTests
{
    [Fact]
    public void SaveAsJxl_Path()
    {
        string file = OutputPath("SaveAsJxl_Path.jxl");

        using (Image<Rgba32> image = CreateImage())
        {
            image.SaveAsJxl(file);
        }

        AssertJxl(file);
    }

    [Fact]
    public async Task SaveAsJxlAsync_Path()
    {
        string file = OutputPath("SaveAsJxlAsync_Path.jxl");

        using (Image<Rgba32> image = CreateImage())
        {
#pragma warning disable xUnit1051 // The overload without a token is the one under test.
            await image.SaveAsJxlAsync(file);
#pragma warning restore xUnit1051
        }

        AssertJxl(file);
    }

    [Fact]
    public async Task SaveAsJxlAsync_Path_CancellationToken()
    {
        string file = OutputPath("SaveAsJxlAsync_Path_CancellationToken.jxl");

        using (Image<Rgba32> image = CreateImage())
        {
            await image.SaveAsJxlAsync(file, cancellationToken: TestContext.Current.CancellationToken);
        }

        AssertJxl(file);
    }

    [Fact]
    public void SaveAsJxl_Path_Encoder()
    {
        string file = OutputPath("SaveAsJxl_Path_Encoder.jxl");

        using (Image<Rgba32> image = CreateImage())
        {
            image.SaveAsJxl(file, new JxlEncoder());
        }

        AssertJxl(file);
    }

    [Fact]
    public async Task SaveAsJxlAsync_Path_Encoder()
    {
        string file = OutputPath("SaveAsJxlAsync_Path_Encoder.jxl");

        using (Image<Rgba32> image = CreateImage())
        {
            await image.SaveAsJxlAsync(file, new JxlEncoder(), cancellationToken: TestContext.Current.CancellationToken);
        }

        AssertJxl(file);
    }

    [Fact]
    public void SaveAsJxl_Stream()
    {
        using MemoryStream memoryStream = new();

        using (Image<Rgba32> image = CreateImage())
        {
            image.SaveAsJxl(memoryStream);
        }

        AssertJxl(memoryStream);
    }

    [Fact]
    public async Task SaveAsJxlAsync_Stream()
    {
        using MemoryStream memoryStream = new();

        using (Image<Rgba32> image = CreateImage())
        {
            await image.SaveAsJxlAsync(memoryStream, cancellationToken: TestContext.Current.CancellationToken);
        }

        AssertJxl(memoryStream);
    }

    [Fact]
    public void SaveAsJxl_Stream_Encoder()
    {
        using MemoryStream memoryStream = new();

        using (Image<Rgba32> image = CreateImage())
        {
            image.SaveAsJxl(memoryStream, new JxlEncoder());
        }

        AssertJxl(memoryStream);
    }

    [Fact]
    public async Task SaveAsJxlAsync_Stream_Encoder()
    {
        using MemoryStream memoryStream = new();

        using (Image<Rgba32> image = CreateImage())
        {
            await image.SaveAsJxlAsync(memoryStream, new JxlEncoder(), cancellationToken: TestContext.Current.CancellationToken);
        }

        AssertJxl(memoryStream);
    }

    [Fact]
    public void Save_JxlExtension_UsesTheJxlEncoder()
    {
        string file = OutputPath("Save_JxlExtension.jxl");

        using (Image<Rgba32> image = CreateImage())
        {
            image.Save(file);
        }

        AssertJxl(file);
    }

    // Image.Save picks the encoder by extension: the default configuration's is a new JxlEncoder, so the
    // file is the lossy default (Distance 1, Effort Default).
    [Fact]
    public void Save_JxlExtension_WritesWhatTheDefaultJxlEncoderWrites()
    {
        string file = OutputPath("Save_JxlExtension_Default.jxl");
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("gradient", 37, 23);
        using MemoryStream expected = new();
        image.SaveAsJxl(expected, new JxlEncoder { Distance = 1, Effort = JxlEffort.Default });

        image.Save(file);

        Assert.Equal(expected.ToArray(), File.ReadAllBytes(file));
    }

    // Each effort, lossless and at the default distance, through SaveAsJxl and our decoder: lossless is
    // exact, lossy is within the interim bound.
    [Theory]
    [InlineData(JxlEffort.Fastest, 0f)]
    [InlineData(JxlEffort.Fastest, 1f)]
    [InlineData(JxlEffort.Default, 0f)]
    [InlineData(JxlEffort.Default, 1f)]
    [InlineData(JxlEffort.Thorough, 0f)]
    [InlineData(JxlEffort.Thorough, 1f)]
    public void SaveAsJxl_Effort_DecodesBack(JxlEffort effort, float distance)
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("gradient", 37, 23);
        using MemoryStream memoryStream = new();

        image.SaveAsJxl(memoryStream, new JxlEncoder { Distance = distance, Effort = effort });

        memoryStream.Position = 0;
        using Image<Rgb24> decoded = Image.Load<Rgb24>(memoryStream);
        if (distance == 0)
        {
            ImageComparer.Exact.VerifySimilarity(image, decoded);
        }
        else
        {
            JxlLossyEncoderTests.AssertWithinInterimBound(image, decoded, $"SaveAsJxl at {effort}, d{distance}, {memoryStream.Length} bytes");
        }
    }

    [Fact]
    public void SaveAsJxl_LosslessThorough_KeepsEveryPixel()
    {
        using Image<Rgba32> image = JxlEncoderRoundTrip.CreateImage<Rgba32>("translucent", 37, 23);
        using MemoryStream memoryStream = new();

        image.SaveAsJxl(memoryStream, new JxlEncoder { Distance = 0, Effort = JxlEffort.Thorough });

        memoryStream.Position = 0;
        using Image<Rgba32> decoded = Image.Load<Rgba32>(memoryStream);
        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    // The defaults (Distance 1, Effort Default) through our decoder and Magick.
    [Theory]
    [InlineData(nameof(JxlEncoderRoundTrip.Reader.Ours))]
    [InlineData(nameof(JxlEncoderRoundTrip.Reader.Magick))]
    public async Task SaveAsJxl_LossyDefaults_DecodesThroughEachReaderWithinTheInterimBound(string reader)
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();
        using MemoryStream memoryStream = new();

        image.SaveAsJxl(memoryStream);

        byte[] encoded = memoryStream.ToArray();
        using Image<Rgb24> decoded = await JxlLossyEncoderTests.DecodeAsync(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "save_as_jxl_defaults");
        JxlLossyEncoderTests.AssertWithinInterimBound(image, decoded, $"SaveAsJxl defaults through {reader}, {encoded.Length} bytes");
    }

    // Distance in [0, 25], as libjxl's JxlEncoderSetFrameDistance, and an Effort that JxlEffort defines.
    // The options are checked before anything is written.
    [Theory]
    [InlineData(-0.01f, JxlEffort.Default)]
    [InlineData(25.01f, JxlEffort.Default)]
    [InlineData(float.NaN, JxlEffort.Default)]
    [InlineData(1.0f, (JxlEffort)3)]
    [InlineData(0.0f, (JxlEffort)(-1))]
    public void SaveAsJxl_OptionOutOfRange_ThrowsAndWritesNothing(float distance, JxlEffort effort)
    {
        using MemoryStream memoryStream = new();
        using Image<Rgba32> image = CreateImage();

        Assert.Throws<ImageFormatException>(() => image.SaveAsJxl(memoryStream, new JxlEncoder { Distance = distance, Effort = effort }));
        Assert.Equal(0, memoryStream.Length);
    }

    [Theory]
    [InlineData(-0.01f, JxlEffort.Default)]
    [InlineData(25.01f, JxlEffort.Default)]
    [InlineData(float.NaN, JxlEffort.Default)]
    [InlineData(1.0f, (JxlEffort)3)]
    [InlineData(0.0f, (JxlEffort)(-1))]
    public async Task SaveAsJxlAsync_OptionOutOfRange_ThrowsAndWritesNothing(float distance, JxlEffort effort)
    {
        using MemoryStream memoryStream = new();
        using Image<Rgba32> image = CreateImage();

        await Assert.ThrowsAsync<ImageFormatException>(() => image.SaveAsJxlAsync(memoryStream, new JxlEncoder { Distance = distance, Effort = effort }, TestContext.Current.CancellationToken));
        Assert.Equal(0, memoryStream.Length);
    }

    // The boundaries are accepted: Distance 0 and 25, and the smallest lossy distance, which libjxl clamps
    // to 0.05 (kMinButteraugliDistance), so it writes what 0.05 writes.
    [Fact]
    public void SaveAsJxl_DistanceAtTheBoundaries_IsAccepted()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("gradient", 37, 23);

        byte[] lossless = Save(image, 0f);
        byte[] smallest = Save(image, 0.01f);
        byte[] clamped = Save(image, 0.05f);
        byte[] largest = Save(image, 25f);

        using Image<Rgb24> decoded = Image.Load<Rgb24>(lossless);
        ImageComparer.Exact.VerifySimilarity(image, decoded);
        Assert.Equal(clamped, smallest);
        Assert.NotEqual(lossless, smallest);
        Assert.Equal(new Size(37, 23), Image.Identify(largest).Size());

        static byte[] Save(Image image, float distance) => JxlEncoderRoundTrip.SaveAsJxl(image, new JxlEncoder { Distance = distance });
    }

    private static string OutputPath(string fileName)
        => Path.Combine(TestEnvironment.CreateOutputDirectory(nameof(ImageExtensionsTests)), fileName);

    private static Image<Rgba32> CreateImage() => new(10, 10, new Rgba32(30, 90, 200, 255));

    private static void AssertJxl(string file)
    {
        using FileStream stream = File.OpenRead(file);
        AssertJxl(stream);
    }

    private static void AssertJxl(Stream stream)
    {
        stream.Position = 0;
        using Image image = Image.Load(stream, out IImageFormat format);

        Assert.Same(JxlFormat.Instance, format);
        Assert.Equal("image/jxl", format.DefaultMimeType);
        Assert.Equal(10, image.Width);
        Assert.Equal(10, image.Height);
    }
}
