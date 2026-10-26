// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests;

public partial class ImageTests
{
    public class Load_EmptyInput
    {
        private static readonly PngDecoder Decoder = new();

        public static TheoryData<string, Func<Image>> SpanLoads => new()
        {
            { "Load(span)", () => Image.Load(ReadOnlySpan<byte>.Empty) },
            { "Load(span, out format)", () => Image.Load(ReadOnlySpan<byte>.Empty, out _) },
            { "Load(config, span)", () => Image.Load(Configuration.Default, ReadOnlySpan<byte>.Empty) },
            { "Load(config, span, out format)", () => Image.Load(Configuration.Default, ReadOnlySpan<byte>.Empty, out _) },
            { "Load<T>(span)", () => Image.Load<Rgba32>(ReadOnlySpan<byte>.Empty) },
            { "Load<T>(span, out format)", () => Image.Load<Rgba32>(ReadOnlySpan<byte>.Empty, out _) },
            { "Load<T>(config, span)", () => Image.Load<Rgba32>(Configuration.Default, ReadOnlySpan<byte>.Empty) },
            { "Load<T>(config, span, out format)", () => Image.Load<Rgba32>(Configuration.Default, ReadOnlySpan<byte>.Empty, out _) },
        };

        public static TheoryData<string, Func<Image>> SpanLoadsWithDecoder => new()
        {
            { "Load(span, decoder)", () => Image.Load(ReadOnlySpan<byte>.Empty, Decoder) },
            { "Load(config, span, decoder)", () => Image.Load(Configuration.Default, ReadOnlySpan<byte>.Empty, Decoder) },
            { "Load<T>(span, decoder)", () => Image.Load<Rgba32>(ReadOnlySpan<byte>.Empty, Decoder) },
            { "Load<T>(config, span, decoder)", () => Image.Load<Rgba32>(Configuration.Default, ReadOnlySpan<byte>.Empty, Decoder) },
        };

        [Theory]
        [MemberData(nameof(SpanLoads))]
        public void Load_EmptySpan_ThrowsUnknownImageFormatException(string overload, Func<Image> load)
        {
            Assert.NotNull(overload);
            Assert.Throws<UnknownImageFormatException>(() => load().Dispose());
        }

        [Theory]
        [MemberData(nameof(SpanLoadsWithDecoder))]
        public void Load_EmptySpanWithDecoder_ThrowsSameAsEmptyStream(string overload, Func<Image> load)
        {
            Assert.NotNull(overload);
            Exception expected = Record.Exception(() => Image.Load(new MemoryStream(), Decoder).Dispose());
            Assert.NotNull(expected);

            Exception actual = Record.Exception(() => load().Dispose());

            Assert.IsType(expected.GetType(), actual);
        }

        [Fact]
        public void Load_EmptyArrayAndStream_ThrowUnknownImageFormatException()
        {
            Assert.Throws<UnknownImageFormatException>(() => Image.Load(Array.Empty<byte>()).Dispose());
            Assert.Throws<UnknownImageFormatException>(() => Image.Load(new MemoryStream()).Dispose());
            Assert.Throws<UnknownImageFormatException>(() => Image.Load<Rgba32>(Array.Empty<byte>()).Dispose());
            Assert.Throws<UnknownImageFormatException>(() => Image.Load<Rgba32>(new MemoryStream()).Dispose());
        }

        [Fact]
        public async Task LoadAsync_EmptyStream_ThrowsUnknownImageFormatException()
        {
            await Assert.ThrowsAsync<UnknownImageFormatException>(async () => (await Image.LoadAsync(new MemoryStream(), TestContext.Current.CancellationToken)).Dispose());
            await Assert.ThrowsAsync<UnknownImageFormatException>(async () => (await Image.LoadAsync<Rgba32>(new MemoryStream(), TestContext.Current.CancellationToken)).Dispose());
        }

        [Fact]
        public async Task IdentifyAndDetectFormat_EmptyInput_ReturnNull()
        {
            Assert.Null(Image.DetectFormat(ReadOnlySpan<byte>.Empty));
            Assert.Null(Image.DetectFormat(Array.Empty<byte>()));
            Assert.Null(Image.DetectFormat(new MemoryStream()));
            Assert.Null(await Image.DetectFormatAsync(new MemoryStream(), TestContext.Current.CancellationToken));

            Assert.Null(Image.Identify(Array.Empty<byte>(), out IImageFormat arrayFormat));
            Assert.Null(arrayFormat);
            Assert.Null(Image.Identify(new MemoryStream(), out IImageFormat streamFormat));
            Assert.Null(streamFormat);
            Assert.Null(await Image.IdentifyAsync(new MemoryStream(), TestContext.Current.CancellationToken));
        }
    }
}
