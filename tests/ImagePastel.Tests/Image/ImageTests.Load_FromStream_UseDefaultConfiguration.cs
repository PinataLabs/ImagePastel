// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Bmp;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.TestUtilities;
using Xunit;

namespace PinataLabs.ImagePastel.Tests;

public partial class ImageTests
{
    public class Load_FromStream_UseDefaultConfiguration : IDisposable
    {
        private static readonly byte[] Data = TestFile.Create(TestImages.Bmp.Bit8).Bytes;

        private MemoryStream BaseStream { get; }

        private AsyncStreamWrapper Stream { get; }

        private bool AllowSynchronousIO { get; set; } = true;

        public Load_FromStream_UseDefaultConfiguration()
        {
            this.BaseStream = new MemoryStream(Data);
            this.Stream = new AsyncStreamWrapper(this.BaseStream, () => this.AllowSynchronousIO);
        }

        private static void VerifyDecodedImage(Image img)
        {
            Assert.Equal(new Size(127, 64), img.Size());
        }

        [Fact]
        public void Stream_Specific()
        {
            using (var img = Image.Load<Rgba32>(this.Stream))
            {
                VerifyDecodedImage(img);
            }
        }

        [Fact]
        public void Stream_Agnostic()
        {
            using (var img = Image.Load(this.Stream))
            {
                VerifyDecodedImage(img);
            }
        }

        [Fact]
        public void Stream_OutFormat_Specific()
        {
            using (var img = Image.Load<Rgba32>(this.Stream, out IImageFormat format))
            {
                VerifyDecodedImage(img);
                Assert.IsType<BmpFormat>(format);
            }
        }

        [Fact]
        public void Stream_Decoder_Specific()
        {
            using (var img = Image.Load<Rgba32>(this.Stream, new BmpDecoder()))
            {
                VerifyDecodedImage(img);
            }
        }

        [Fact]
        public void Stream_Decoder_Agnostic()
        {
            using (var img = Image.Load(this.Stream, new BmpDecoder()))
            {
                VerifyDecodedImage(img);
            }
        }

        [Fact]
        public void Stream_OutFormat_Agnostic()
        {
            using (var img = Image.Load(this.Stream, out IImageFormat format))
            {
                VerifyDecodedImage(img);
                Assert.IsType<BmpFormat>(format);
            }
        }

        [Fact]
        public async Task Async_Stream_OutFormat_Agnostic()
        {
            this.AllowSynchronousIO = false;
            var formattedImage = await Image.LoadWithFormatAsync(this.Stream, TestContext.Current.CancellationToken);
            using (formattedImage.Image)
            {
                VerifyDecodedImage(formattedImage.Image);
                Assert.IsType<BmpFormat>(formattedImage.Format);
            }
        }

        [Fact]
        public async Task Async_Stream_Specific()
        {
            this.AllowSynchronousIO = false;
            using (var img = await Image.LoadAsync<Rgba32>(this.Stream, TestContext.Current.CancellationToken))
            {
                VerifyDecodedImage(img);
            }
        }

        [Fact]
        public async Task Async_Stream_Agnostic()
        {
            this.AllowSynchronousIO = false;
            using (var img = await Image.LoadAsync(this.Stream, TestContext.Current.CancellationToken))
            {
                VerifyDecodedImage(img);
            }
        }

        [Fact]
        public async Task Async_Stream_OutFormat_Specific()
        {
            this.AllowSynchronousIO = false;
            var formattedImage = await Image.LoadWithFormatAsync<Rgba32>(this.Stream, TestContext.Current.CancellationToken);
            using (formattedImage.Image)
            {
                VerifyDecodedImage(formattedImage.Image);
                Assert.IsType<BmpFormat>(formattedImage.Format);
            }
        }

        [Fact]
        public async Task Async_Stream_Decoder_Specific()
        {
            this.AllowSynchronousIO = false;
            using (var img = await Image.LoadAsync<Rgba32>(this.Stream, new BmpDecoder(), TestContext.Current.CancellationToken))
            {
                VerifyDecodedImage(img);
            }
        }

        [Fact]
        public async Task Async_Stream_Decoder_Agnostic()
        {
            this.AllowSynchronousIO = false;
            using (var img = await Image.LoadAsync(this.Stream, new BmpDecoder(), TestContext.Current.CancellationToken))
            {
                VerifyDecodedImage(img);
            }
        }

        public void Dispose()
        {
            this.BaseStream?.Dispose();
        }
    }
}
