// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using PinataLabs.ImagePastel.Advanced;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.TestUtilities;
using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests;

public partial class ImageTests
{
    public class SaveAsync
    {
        [Fact]
        public async Task DetectedEncoding()
        {
            string dir = TestEnvironment.CreateOutputDirectory(nameof(ImageTests));
            string file = Path.Combine(dir, "DetectedEncodingAsync.png");

            using (var image = new Image<Rgba32>(10, 10))
            {
                await image.SaveAsync(file, cancellationToken: TestContext.Current.CancellationToken);
            }

            using (Image.Load(file, out IImageFormat mime))
            {
                Assert.Equal("image/png", mime.DefaultMimeType);
            }
        }

        [Fact]
        public async Task WhenExtensionIsUnknown_Throws()
        {
            string dir = TestEnvironment.CreateOutputDirectory(nameof(ImageTests));
            string file = Path.Combine(dir, "UnknownExtensionsEncoding_Throws.tmp");

            await Assert.ThrowsAsync<NotSupportedException>(
                async () =>
                    {
                        using (var image = new Image<Rgba32>(10, 10))
                        {
                            await image.SaveAsync(file, cancellationToken: TestContext.Current.CancellationToken);
                        }
                    });
        }

        [Fact]
        public async Task SetEncoding()
        {
            string dir = TestEnvironment.CreateOutputDirectory(nameof(ImageTests));
            string file = Path.Combine(dir, "SetEncoding.dat");

            using (var image = new Image<Rgba32>(10, 10))
            {
                await image.SaveAsync(file, new PngEncoder(), cancellationToken: TestContext.Current.CancellationToken);
            }

            using (Image.Load(file, out IImageFormat mime))
            {
                Assert.Equal("image/png", mime.DefaultMimeType);
            }
        }

        [Theory]
        [InlineData("test.pbm", "image/x-portable-pixmap")]
        [InlineData("test.png", "image/png")]
        [InlineData("test.tga", "image/tga")]
        [InlineData("test.bmp", "image/bmp")]
        [InlineData("test.jpg", "image/jpeg")]
        [InlineData("test.gif", "image/gif")]
        public async Task SaveStreamWithMime(string filename, string mimeType)
        {
            using (var image = new Image<Rgba32>(5, 5))
            {
                string ext = Path.GetExtension(filename);
                IImageFormat format = image.GetConfiguration().ImageFormatsManager.FindFormatByFileExtension(ext);
                Assert.Equal(mimeType, format.DefaultMimeType);

                using (var stream = new MemoryStream())
                {
                    var asyncStream = new AsyncStreamWrapper(stream, () => false);
                    await image.SaveAsync(asyncStream, format, cancellationToken: TestContext.Current.CancellationToken);

                    stream.Position = 0;

                    (Image Image, IImageFormat Format) imf = await Image.LoadWithFormatAsync(stream, TestContext.Current.CancellationToken);

                    Assert.Equal(format, imf.Format);
                    Assert.Equal(mimeType, imf.Format.DefaultMimeType);

                    imf.Image.Dispose();
                }
            }
        }

        [Fact]
        public async Task ThrowsWhenDisposed()
        {
            var image = new Image<Rgba32>(5, 5);
            image.Dispose();
            IImageEncoder encoder = Mock.Of<IImageEncoder>();
            using (var stream = new MemoryStream())
            {
                await Assert.ThrowsAsync<ObjectDisposedException>(async () => await image.SaveAsync(stream, encoder, TestContext.Current.CancellationToken));
            }
        }

        [Theory]
        [InlineData("test.pbm")]
        [InlineData("test.png")]
        [InlineData("test.tga")]
        [InlineData("test.bmp")]
        [InlineData("test.jpg")]
        [InlineData("test.gif")]
        public async Task SaveAsync_NeverCallsSyncMethods(string filename)
        {
            using (var image = new Image<Rgba32>(5, 5))
            {
                IImageEncoder encoder = image.DetectEncoder(filename);
                using (var stream = new MemoryStream())
                {
                    var asyncStream = new AsyncStreamWrapper(stream, () => false);
                    await image.SaveAsync(asyncStream, encoder, TestContext.Current.CancellationToken);
                }
            }
        }

        [Fact]
        public async Task SaveAsync_WithNonSeekableStream_IsCancellable()
        {
            using var image = new Image<Rgba32>(4000, 4000);
            var encoder = new PngEncoder() { CompressionLevel = PngCompressionLevel.BestCompression };
            using var stream = new MemoryStream();
            var asyncStream = new AsyncStreamWrapper(stream, () => false);
            var cts = new CancellationTokenSource();

            var pausedStream = new PausedStream(asyncStream);
            pausedStream.OnWaiting(s =>
            {
                cts.Cancel();
                pausedStream.Release();
            });

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await image.SaveAsync(pausedStream, encoder, cts.Token));
        }
    }
}
