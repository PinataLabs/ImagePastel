// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Jpeg;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.Tests.TestUtilities;
using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests;

public partial class ImageTests
{
    /// <summary>
    /// Tests the <see cref="Image"/> class.
    /// </summary>
    public class DetectFormat : ImageLoadTestBase
    {
        private static readonly string ActualImagePath = TestFile.GetInputFileFullPath(TestImages.Bmp.F);

        private byte[] ActualImageBytes => TestFile.Create(TestImages.Bmp.F).Bytes;

        private ReadOnlySpan<byte> ActualImageSpan => this.ActualImageBytes.AsSpan();

        private IImageFormat LocalImageFormat => this.localImageFormatMock.Object;

        private static readonly IImageFormat ExpectedGlobalFormat =
            Configuration.Default.ImageFormatsManager.FindFormatByFileExtension("bmp");

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FromBytes_GlobalConfiguration(bool useSpan)
        {
            IImageFormat type = useSpan
                                    ? Image.DetectFormat(this.ActualImageSpan)
                                    : Image.DetectFormat(this.ActualImageBytes);

            Assert.Equal(ExpectedGlobalFormat, type);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FromBytes_CustomConfiguration(bool useSpan)
        {
            IImageFormat type = useSpan
                                    ? Image.DetectFormat(this.LocalConfiguration, this.ByteArray.AsSpan())
                                    : Image.DetectFormat(this.LocalConfiguration, this.ByteArray);

            Assert.Equal(this.LocalImageFormat, type);
        }

        [Fact]
        public void FromFileSystemPath_GlobalConfiguration()
        {
            IImageFormat type = Image.DetectFormat(ActualImagePath);
            Assert.Equal(ExpectedGlobalFormat, type);
        }

        [Fact]
        public void FromFileSystemPath_CustomConfiguration()
        {
            IImageFormat type = Image.DetectFormat(this.LocalConfiguration, this.MockFilePath);
            Assert.Equal(this.LocalImageFormat, type);
        }

        [Fact]
        public void FromStream_GlobalConfiguration()
        {
            using (var stream = new MemoryStream(this.ActualImageBytes))
            {
                IImageFormat type = Image.DetectFormat(stream);
                Assert.Equal(ExpectedGlobalFormat, type);
            }
        }

        [Fact]
        public void FromStream_CustomConfiguration()
        {
            IImageFormat type = Image.DetectFormat(this.LocalConfiguration, this.DataStream);
            Assert.Equal(this.LocalImageFormat, type);
        }

        [Fact]
        public void WhenNoMatchingFormatFound_ReturnsNull()
        {
            IImageFormat type = Image.DetectFormat(new Configuration(), this.DataStream);
            Assert.Null(type);
        }

        [Fact]
        public async Task FromStreamAsync_GlobalConfiguration()
        {
            using (var stream = new MemoryStream(this.ActualImageBytes))
            {
                IImageFormat type = await Image.DetectFormatAsync(new AsyncStreamWrapper(stream, () => false), TestContext.Current.CancellationToken);
                Assert.Equal(ExpectedGlobalFormat, type);
            }
        }

        [Fact]
        public async Task FromStreamAsync_CustomConfiguration()
        {
            IImageFormat type = await Image.DetectFormatAsync(this.LocalConfiguration, new AsyncStreamWrapper(this.DataStream, () => false), TestContext.Current.CancellationToken);
            Assert.Equal(this.LocalImageFormat, type);
        }

        [Fact]
        public async Task WhenNoMatchingFormatFoundAsync_ReturnsNull()
        {
            IImageFormat type = await Image.DetectFormatAsync(new Configuration(), new AsyncStreamWrapper(this.DataStream, () => false), TestContext.Current.CancellationToken);
            Assert.Null(type);
        }

        [Theory]
        [InlineData(ReadOrigin.Begin, 0)]
        [InlineData(ReadOrigin.Begin, 100)]
        [InlineData(ReadOrigin.Current, 0)]
        public async Task FromSeekableStreamAsync_MatchesSync(ReadOrigin readOrigin, int startPosition)
        {
            byte[] bytes = TestFile.Create(TestImages.Png.Splash).Bytes;
            var configuration = new Configuration(new PngConfigurationModule()) { ReadOrigin = readOrigin };

            using var syncStream = new MemoryStream(bytes) { Position = startPosition };
            IImageFormat syncFormat = Image.DetectFormat(configuration, syncStream);

            using var asyncStream = new MemoryStream(bytes) { Position = startPosition };
            IImageFormat asyncFormat = await Image.DetectFormatAsync(configuration, asyncStream, TestContext.Current.CancellationToken);

            Assert.Equal(PngFormat.Instance, syncFormat);
            Assert.Equal(syncFormat, asyncFormat);
            Assert.Equal(syncStream.Position, asyncStream.Position);
        }

        [Fact]
        public async Task FromSeekableStream_ShortRemainingData_ReturnsNull()
        {
            // Only the 2-byte JPEG SOI marker remains after the start position, fewer than the 11 bytes the JPEG detector needs.
            byte[] bytes = new byte[22];
            bytes[20] = 0xFF;
            bytes[21] = 0xD8;
            var configuration = new Configuration(new JpegConfigurationModule()) { ReadOrigin = ReadOrigin.Current };

            using var syncStream = new MemoryStream(bytes) { Position = 20 };
            IImageFormat syncFormat = Image.DetectFormat(configuration, syncStream);

            using var asyncStream = new MemoryStream(bytes) { Position = 20 };
            IImageFormat asyncFormat = await Image.DetectFormatAsync(configuration, asyncStream, TestContext.Current.CancellationToken);

            Assert.Null(syncFormat);
            Assert.Null(asyncFormat);
            Assert.Equal(20, syncStream.Position);
            Assert.Equal(20, asyncStream.Position);
        }

        [Fact]
        public async Task FromSeekableStreamAsync_ReadsOnlyTheHeader()
        {
            byte[] bytes = TestFile.Create(TestImages.Png.Splash).Bytes;
            using var stream = new ReadCountingStream(new MemoryStream(bytes));

            IImageFormat format = await Image.DetectFormatAsync(stream, TestContext.Current.CancellationToken);

            Assert.Equal(PngFormat.Instance, format);
            Assert.InRange(stream.BytesRead, 1, Configuration.Default.MaxHeaderSize);
        }

        private sealed class ReadCountingStream : Stream
        {
            private readonly Stream inner;

            public ReadCountingStream(Stream inner) => this.inner = inner;

            public long BytesRead { get; private set; }

            public override bool CanRead => this.inner.CanRead;

            public override bool CanSeek => this.inner.CanSeek;

            public override bool CanWrite => false;

            public override long Length => this.inner.Length;

            public override long Position
            {
                get => this.inner.Position;
                set => this.inner.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count)
                => this.Count(this.inner.Read(buffer, offset, count));

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => this.Count(await this.inner.ReadAsync(buffer, offset, count, cancellationToken));

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                => this.Count(await this.inner.ReadAsync(buffer, cancellationToken));

            public override long Seek(long offset, SeekOrigin origin) => this.inner.Seek(offset, origin);

            public override void Flush() => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.inner.Dispose();
                }

                base.Dispose(disposing);
            }

            private int Count(int read)
            {
                this.BytesRead += read;
                return read;
            }
        }
    }
}
