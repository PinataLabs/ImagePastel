// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using Moq;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests;

public partial class ImageTests
{
    public class Save
    {
        [Fact]
        public void DetectedEncoding()
        {
            string dir = TestEnvironment.CreateOutputDirectory(nameof(ImageTests));
            string file = Path.Combine(dir, "DetectedEncoding.png");

            using (var image = new Image<Rgba32>(10, 10))
            {
                image.Save(file);
            }

            using (Image.Load(file, out IImageFormat mime))
            {
                Assert.Equal("image/png", mime.DefaultMimeType);
            }
        }

        [Fact]
        public void WhenExtensionIsUnknown_Throws()
        {
            string dir = TestEnvironment.CreateOutputDirectory(nameof(ImageTests));
            string file = Path.Combine(dir, "UnknownExtensionsEncoding_Throws.tmp");

            Assert.Throws<NotSupportedException>(
                () =>
                    {
                        using (var image = new Image<Rgba32>(10, 10))
                        {
                            image.Save(file);
                        }
                    });
        }

        [Fact]
        public void SetEncoding()
        {
            string dir = TestEnvironment.CreateOutputDirectory(nameof(ImageTests));
            string file = Path.Combine(dir, "SetEncoding.dat");

            using (var image = new Image<Rgba32>(10, 10))
            {
                image.Save(file, new PngEncoder());
            }

            using (Image.Load(file, out IImageFormat mime))
            {
                Assert.Equal("image/png", mime.DefaultMimeType);
            }
        }

        [Theory]
        [InlineData("test.bmp")]
        [InlineData("test.gif")]
        [InlineData("test.jpg")]
        [InlineData("test.pbm")]
        [InlineData("test.png")]
        [InlineData("test.tga")]
        [InlineData("test.tiff")]
        [InlineData("test.webp")]
        public async Task ToNonSeekableStream_SyncAndAsync_MatchSeekableStream(string filename)
        {
            using var image = new Image<Rgba32>(32, 24);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba32((byte)(x * 8), (byte)(y * 10), (byte)((x + y) * 4));
                    }
                }
            });
            IImageEncoder encoder = image.DetectEncoder(filename);

            using var seekable = new MemoryStream();
            image.Save(seekable, encoder);

            using var syncTarget = new MemoryStream();
            image.Save(new NonSeekableStream(syncTarget), encoder);

            using var asyncTarget = new MemoryStream();
            await image.SaveAsync(new NonSeekableStream(asyncTarget), encoder, TestContext.Current.CancellationToken);

            Assert.Equal(seekable.ToArray(), syncTarget.ToArray());
            Assert.Equal(seekable.ToArray(), asyncTarget.ToArray());
        }

        [Fact]
        public void ThrowsWhenDisposed()
        {
            using var image = new Image<Rgba32>(5, 5);
            image.Dispose();
            IImageEncoder encoder = Mock.Of<IImageEncoder>();
            using (var stream = new MemoryStream())
            {
                Assert.Throws<ObjectDisposedException>(() => image.Save(stream, encoder));
            }
        }
    }
}
