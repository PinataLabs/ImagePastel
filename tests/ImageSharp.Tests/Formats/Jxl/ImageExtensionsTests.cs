// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The <c>SaveAsJxl</c> and <c>SaveAsJxlAsync</c> extensions with <see cref="Configuration.Default"/>, as a
/// user reaches the encoder (E6a, #331). Each file is read back by the default configuration's decoder.
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
