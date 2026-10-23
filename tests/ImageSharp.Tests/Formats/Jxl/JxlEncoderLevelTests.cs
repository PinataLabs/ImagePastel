// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;
using Reader = SixLabors.ImageSharp.Tests.Formats.Jxl.JxlEncoderRoundTrip.Reader;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The level-10 container the encoder writes when the codestream needs level 10 (E6c, #333), checked
/// through <c>SaveAsJxl</c> with <see cref="Configuration.Default"/>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderLevelTests
{
    [Fact]
    public void SaveAsJxl_LosslessGrey16_WritesAJxllContainer()
    {
        using Image<L16> image = JxlEncoderRoundTrip.CreateImage<L16>("gradient", 17, 23);

        AssertLevel10Container(Save(image, new JxlEncoder { Distance = 0 }));
    }

    [Fact]
    public void SaveAsJxl_LosslessRgba64_WritesAJxllContainer()
    {
        using Image<Rgba64> image = JxlEncoderRoundTrip.CreateImage<Rgba64>("gradient", 17, 23);

        AssertLevel10Container(Save(image, new JxlEncoder { Distance = 0 }));
    }

    // A level-5 codestream stays bare: 8-bit lossless, and lossy without alpha, whose XYB samples always
    // fit libjxl's 16-bit buffers.
    [Theory]
    [InlineData(nameof(Rgb24), 0f)]
    [InlineData(nameof(Rgba32), 0f)]
    [InlineData(nameof(L8), 0f)]
    [InlineData(nameof(L16), 1f)]
    [InlineData(nameof(Rgb48), 1f)]
    public void SaveAsJxl_Level5_StaysABareCodestream(string pixelType, float distance)
    {
        byte[] encoded = JxlPixelTypes.Call(pixelType, new SavePattern("gradient", 17, 23, distance));

        Assert.Equal(new byte[] { 0xFF, 0x0A }, encoded[..2]);
    }

    // Lossy alpha keeps the pixel type's depth, so 16-bit alpha needs level 10, as in libjxl.
    [Fact]
    public void SaveAsJxl_LossyRgba64_WritesAJxllContainer()
    {
        using Image<Rgba64> image = JxlEncoderRoundTrip.CreateImage<Rgba64>("translucent", 17, 23);

        AssertLevel10Container(Save(image, new JxlEncoder { Distance = 1 }));
    }

    // With metadata, jxll comes right after ftyp and the jxlp split of E6b is kept.
    [Theory]
    [InlineData(false, new[] { "JXL ", "ftyp", "jxll", "jxlp", "Exif", "jxlp" })]
    [InlineData(true, new[] { "JXL ", "ftyp", "jxll", "jxlp", "Exif", "xml ", "jxlp" })]
    public void SaveAsJxl_Level10WithMetadata_PutsJxllBeforeTheJxlpSplit(bool withXmp, string[] expectedBoxes)
    {
        using Image<L16> bare = JxlEncoderRoundTrip.CreateImage<L16>("gradient", 17, 23);
        using Image<L16> image = bare.Clone();
        ExifProfile exif = new();
        exif.SetValue(ExifTag.Software, "E6c");
        image.Metadata.ExifProfile = exif;
        if (withXmp)
        {
            image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"));
        }

        JxlEncoder encoder = new() { Distance = 0 };
        byte[] encoded = Save(image, encoder);
        IReadOnlyList<JxlBox> boxes = JxlBoxList.Read(encoded);

        Assert.Equal(expectedBoxes, JxlBoxList.Types(boxes));
        Assert.Equal(new byte[] { 10 }, boxes[2].Contents);
        Assert.Equal(0u, JxlBoxList.JxlpIndex(boxes[3]));
        Assert.Equal(0x80000001u, JxlBoxList.JxlpIndex(boxes[^1]));
        Assert.Equal(exif.ToByteArray(), boxes[4].Contents[4..]);

        // The same codestream as without metadata, where it is the jxlc box.
        Assert.Equal(JxlBoxList.Read(Save(bare, encoder))[3].Contents, JxlBoxList.Codestream(encoded));
    }

    // A side over 2^18 is beyond level 5. The image is all zeros; only its size matters.
    [Theory]
    [InlineData((1 << 18) + 1, 1)]
    [InlineData(1, (1 << 18) + 1)]
    public void SaveAsJxl_SideOver2To18_WritesAJxllContainer(int width, int height)
    {
        using Image<L8> image = new(width, height);

        byte[] encoded = Save(image, new JxlEncoder { Distance = 0, Effort = JxlEffort.Fastest });

        AssertLevel10Container(encoded);
        IImageInfo info = Image.Identify(encoded);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
    }

    // libjxl's "Too large image dimensions": no level allows a side over 2^30. The image is hand-built:
    // every buffer of its pixel memory is the same 1 MiB array, so it costs 1 MiB, not 1 GiB. Its tall side
    // is the height, because a row has to fit in one buffer.
    [Fact]
    public void SaveAsJxl_SideOver2To30_ThrowsAndWritesNothing()
    {
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = new AliasingAllocator();
        using Image<L8> image = new(configuration, 1, (1 << 30) + 1);
        using MemoryStream stream = new();

        ImageFormatException ex = Assert.Throws<ImageFormatException>(() => image.SaveAsJxl(stream, new JxlEncoder { Distance = 0 }));

        Assert.Contains("Too large image dimensions", ex.Message);
        Assert.Equal(0, stream.Length);
    }

    // Every level-10 output decodes. Our decoder gives the pixels of the codestream alone, which is the
    // output before E6c (no codestream pin moved), and a lossless save gives the source back through every
    // reader. A lossy save is checked for size through Magick.NET and djxl; the lossy suites check its pixels.
    [Theory]
    [InlineData(nameof(Reader.Ours), nameof(L16), 0f, Level10Case.Plain)]
    [InlineData(nameof(Reader.Ours), nameof(Rgba64), 0f, Level10Case.Plain)]
    [InlineData(nameof(Reader.Ours), nameof(Rgba64), 1f, Level10Case.Plain)]
    [InlineData(nameof(Reader.Ours), nameof(L16), 0f, Level10Case.WithExif)]
    [InlineData(nameof(Reader.Ours), nameof(L8), 0f, Level10Case.Wide)]
    [InlineData(nameof(Reader.Magick), nameof(L16), 0f, Level10Case.Plain)]
    [InlineData(nameof(Reader.Magick), nameof(Rgba64), 0f, Level10Case.Plain)]
    [InlineData(nameof(Reader.Magick), nameof(Rgba64), 1f, Level10Case.Plain)]
    [InlineData(nameof(Reader.Magick), nameof(L16), 0f, Level10Case.WithExif)]
    [InlineData(nameof(Reader.Magick), nameof(L8), 0f, Level10Case.Wide)]
    [InlineData(nameof(Reader.Djxl), nameof(L16), 0f, Level10Case.Plain)]
    [InlineData(nameof(Reader.Djxl), nameof(Rgba64), 0f, Level10Case.Plain)]
    [InlineData(nameof(Reader.Djxl), nameof(Rgba64), 1f, Level10Case.Plain)]
    [InlineData(nameof(Reader.Djxl), nameof(L16), 0f, Level10Case.WithExif)]
    [InlineData(nameof(Reader.Djxl), nameof(L8), 0f, Level10Case.Wide)]
    public Task SaveAsJxl_Level10_Decodes(string reader, string pixelType, float distance, Level10Case level10Case)
    {
        Reader parsed = Enum.Parse<Reader>(reader);
        JxlEncoderRoundTrip.SkipUnlessReaderRuns(parsed);
        return JxlPixelTypes.Verify(pixelType, new DecodeCase(parsed, distance, level10Case));
    }

    // libjxl's level-10 layout without boxes: the signature, ftyp, jxll holding the level, then the whole
    // codestream in one jxlc box.
    private static void AssertLevel10Container(byte[] encoded)
    {
        IReadOnlyList<JxlBox> boxes = JxlBoxList.Read(encoded);

        Assert.Equal(new[] { "JXL ", "ftyp", "jxll", "jxlc" }, JxlBoxList.Types(boxes));
        Assert.Equal("jxl \0\0\0\0jxl "u8.ToArray(), boxes[1].Contents);
        Assert.Equal(new byte[] { 10 }, boxes[2].Contents);
        Assert.Equal(new byte[] { 0xFF, 0x0A }, boxes[3].Contents[..2]);
    }

    private static byte[] Save(Image image, JxlEncoder encoder)
    {
        using MemoryStream stream = new();
        image.SaveAsJxl(stream, encoder);
        return stream.ToArray();
    }

    // Hands out the same array for every buffer, so an image of any height costs one buffer. Its pixels
    // alias each other, which is fine for an image that is never filled.
    private sealed class AliasingAllocator : MemoryAllocator
    {
        private const int BufferCapacityInBytes = 1 << 20;

        public override IMemoryOwner<T> Allocate<T>(int length, AllocationOptions options = AllocationOptions.None)
            => new SharedOwner<T>(length);

        protected internal override int GetBufferCapacityInBytes() => BufferCapacityInBytes;

        private sealed class SharedOwner<T> : IMemoryOwner<T>
            where T : struct
        {
            private static readonly T[] Shared = new T[BufferCapacityInBytes / Unsafe.SizeOf<T>()];

            public SharedOwner(int length) => this.Memory = Shared.AsMemory(0, length);

            public Memory<T> Memory { get; }

            public void Dispose()
            {
            }
        }
    }

    /// <summary>What makes a level-10 output in <see cref="SaveAsJxl_Level10_Decodes"/>.</summary>
    public enum Level10Case
    {
        /// <summary>The depth, at 17 × 23, with no metadata.</summary>
        Plain,

        /// <summary>The depth, at 17 × 23, with an Exif profile.</summary>
        WithExif,

        /// <summary>A side of 2^18 + 1.</summary>
        Wide,
    }

    private sealed record DecodeCase(Reader Reader, float Distance, Level10Case Case) : IJxlPixelTypeCall<Task>
    {
        public async Task Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            (int width, int height) = this.Case == Level10Case.Wide ? ((1 << 18) + 1, 1) : (17, 23);
            using Image<TPixel> image = JxlEncoderRoundTrip.CreateImage<TPixel>("gradient", width, height);
            if (this.Case == Level10Case.WithExif)
            {
                ExifProfile exif = new();
                exif.SetValue(ExifTag.Software, "E6c");
                image.Metadata.ExifProfile = exif;
            }

            byte[] encoded = Save(image, new JxlEncoder { Distance = this.Distance });
            Assert.Equal(new byte[] { 10 }, JxlBoxList.Read(encoded)[2].Contents);
            bool lossless = this.Distance == 0;
            string name = $"{typeof(TPixel).Name}_d{this.Distance}_{this.Case}";

            switch (this.Reader)
            {
                case Reader.Ours:
                {
                    using Image<TPixel> decoded = Image.Load<TPixel>(encoded);
                    using Image<TPixel> codestreamOnly = Image.Load<TPixel>(JxlBoxList.Codestream(encoded));
                    ImageComparer.Exact.VerifySimilarity(codestreamOnly, decoded);
                    if (lossless)
                    {
                        ImageComparer.Exact.VerifySimilarity(image, decoded);
                    }

                    break;
                }

                case Reader.Magick when lossless:
                    JxlEncoderRoundTrip.AssertMagickExact(image, encoded);
                    break;
                case Reader.Djxl when lossless:
                    await JxlEncoderRoundTrip.AssertDjxlExactAsync(image, encoded, name, TestContext.Current.CancellationToken);
                    break;
                default:
                {
                    using Image<TPixel> decoded = await JxlEncoderRoundTrip.DecodeAsync<TPixel>(this.Reader, encoded, nameof(JxlEncoderLevelTests), name, highBitDepth: true);
                    Assert.Equal(image.Size(), decoded.Size());
                    break;
                }
            }
        }
    }

    // Saves a pattern image of the pixel type through SaveAsJxl.
    private sealed record SavePattern(string Pattern, int Width, int Height, float Distance) : IJxlPixelTypeCall<byte[]>
    {
        public byte[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = JxlEncoderRoundTrip.CreateImage<TPixel>(this.Pattern, this.Width, this.Height);
            return Save(image, new JxlEncoder { Distance = this.Distance });
        }
    }
}
