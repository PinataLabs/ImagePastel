// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests.Formats.Webp;

/// <summary>
/// Decodes WebP files with container-level oddities: non-zero reserved fields, bytes after the RIFF payload,
/// truncated chunks and metadata chunks with impossible sizes.
/// </summary>
[Trait("Format", "Webp")]
[ValidateDisposedMemoryAllocations]
public class WebpDecoderRobustnessTests
{
    public enum SampleKind
    {
        SimpleLossy,
        SimpleLossless,
        LossyWithAlpha,
        LosslessWithExif,
        LossyWithExifAndXmp,
        LosslessWithIcc,
    }

    [Theory]
    [InlineData(21, 0x01)] // First reserved byte after the flags byte.
    [InlineData(22, 0xFF)]
    [InlineData(23, 0x80)]
    public void Decode_VP8XWithNonZeroReservedByte_MatchesOriginal(int offset, byte value)
    {
        byte[] original = Encode(SampleKind.LosslessWithExif);
        Assert.Equal("VP8X", Encoding.ASCII.GetString(original, 12, 4));

        byte[] patched = (byte[])original.Clone();
        patched[offset] = value;

        AssertSameImage(original, patched);
    }

    [Theory]
    [InlineData(0x40)]
    [InlineData(0x80)]
    [InlineData(0xC0)]
    [InlineData(0x01)]
    public void Decode_VP8XWithReservedFlagBits_MatchesOriginal(byte reservedBits)
    {
        byte[] original = Encode(SampleKind.LosslessWithExif);
        Assert.Equal("VP8X", Encoding.ASCII.GetString(original, 12, 4));

        byte[] patched = (byte[])original.Clone();
        patched[20] |= reservedBits;

        AssertSameImage(original, patched);
    }

    [Theory]
    [InlineData(SampleKind.SimpleLossy)]
    [InlineData(SampleKind.SimpleLossless)]
    [InlineData(SampleKind.LossyWithAlpha)]
    [InlineData(SampleKind.LosslessWithExif)]
    [InlineData(SampleKind.LossyWithExifAndXmp)]
    [InlineData(SampleKind.LosslessWithIcc)]
    public void Decode_TrailingBytesAfterRiffPayload_AreIgnored(SampleKind kind)
    {
        byte[] original = Encode(kind);
        Assert.Equal(original.Length, 8 + BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(4)));

        var random = new Random(27);
        for (int count = 1; count <= 64; count++)
        {
            byte[] patched = new byte[original.Length + count];
            original.CopyTo(patched, 0);
            random.NextBytes(patched.AsSpan(original.Length));

            AssertSameImage(original, patched);
        }
    }

    [Theory]
    [InlineData(SampleKind.LossyWithAlpha)]
    [InlineData(SampleKind.LossyWithExifAndXmp)]
    public void Decode_StreamNotAtStart_TrailingBytesAreIgnored(SampleKind kind)
    {
        // The RIFF end is relative to where the image starts in the stream, not to the start of the stream.
        byte[] original = Encode(kind);
        byte[] padded = new byte[5 + original.Length + 5];
        padded.AsSpan().Fill(0x5A);
        original.CopyTo(padded, 5);
        var configuration = new Configuration(new WebpConfigurationModule()) { ReadOrigin = ReadOrigin.Current };

        using Image<Rgba32> expected = Image.Load<Rgba32>(original);
        using var stream = new MemoryStream(padded) { Position = 5 };
        using Image<Rgba32> actual = Image.Load<Rgba32>(configuration, stream);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
        Assert.Equal(expected.Metadata.ExifProfile?.Values.Count, actual.Metadata.ExifProfile?.Values.Count);

        stream.Position = 5;
        IImageInfo info = Image.Identify(configuration, stream);
        Assert.Equal(expected.Width, info.Width);
        Assert.Equal(expected.Height, info.Height);
    }

    [Theory]
    [InlineData(TestImages.Webp.Lossy.NoFilter06)]
    [InlineData(TestImages.Webp.Lossy.Alpha1)]
    [InlineData(TestImages.Webp.Lossy.WithXmp)]
    [InlineData(TestImages.Webp.Lossy.WithIccp)]
    [InlineData(TestImages.Webp.Lossless.GreenTransform1)]
    [InlineData(TestImages.Webp.Lossless.WithExif)]
    public void Decode_TestFileWithTrailingBytes_MatchesOriginal(string imagePath)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, imagePath));
        foreach (int count in new[] { 1, 3, 7, 64 })
        {
            byte[] patched = new byte[original.Length + count];
            original.CopyTo(patched, 0);
            patched.AsSpan(original.Length).Fill(0xA5);

            AssertSameImage(original, patched);
        }
    }

    [Theory]
    [InlineData(SampleKind.LosslessWithExif)]
    [InlineData(SampleKind.LossyWithExifAndXmp)]
    public void Decode_TrailingBytesLookingLikeAChunk_AreIgnored(SampleKind kind)
    {
        byte[] original = Encode(kind);
        byte[] junk = { (byte)'E', (byte)'X', (byte)'I', (byte)'F', 0xF0, 0xFF, 0xFF, 0x7F, 1, 2, 3 };
        byte[] patched = new byte[original.Length + junk.Length];
        original.CopyTo(patched, 0);
        junk.CopyTo(patched, original.Length);

        AssertSameImage(original, patched);
    }

    [Theory]
    [InlineData("EXIF", 0x7FFFFFF0u, false)]
    [InlineData("EXIF", 0xFFFFFFF0u, false)]
    [InlineData("EXIF", 0xFFFFFFFFu, false)]
    [InlineData("EXIF", 0x7FFFFFF0u, true)]
    [InlineData("EXIF", 0xFFFFFFF0u, true)]
    [InlineData("XMP ", 0x7FFFFFF0u, false)]
    [InlineData("XMP ", 0xFFFFFFF0u, false)]
    [InlineData("XMP ", 0xFFFFFFFFu, false)]
    [InlineData("XMP ", 0x7FFFFFF0u, true)]
    [InlineData("XMP ", 0xFFFFFFF0u, true)]
    public void Decode_MetadataChunkSizeLargerThanStream_DropsMetadata(string chunkId, uint size, bool ignoreMetadata)
    {
        byte[] original = Encode(SampleKind.LossyWithExifAndXmp);
        byte[] patched = PatchChunkSize(original, chunkId, size);
        var decoder = new WebpDecoder { IgnoreMetadata = ignoreMetadata };

        using Image<Rgba32> expected = Image.Load<Rgba32>(original, decoder);
        using Image<Rgba32> actual = Image.Load<Rgba32>(patched, decoder);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
        if (chunkId == "EXIF")
        {
            Assert.Null(actual.Metadata.ExifProfile);
        }
        else
        {
            Assert.Null(actual.Metadata.XmpProfile);
        }

        IImageInfo info = Image.Identify(patched);
        Assert.Equal(expected.Width, info.Width);
        Assert.Equal(expected.Height, info.Height);
    }

    [Theory]
    [InlineData(0x7FFFFFF0u, false)]
    [InlineData(0xFFFFFFF0u, false)]
    [InlineData(0xFFFFFFFFu, false)]
    [InlineData(0x7FFFFFF0u, true)]
    [InlineData(0xFFFFFFF0u, true)]
    public void Decode_IccpChunkSizeLargerThanStream_DecodesOrThrowsInvalidImageContent(uint size, bool ignoreMetadata)
    {
        byte[] patched = PatchChunkSize(Encode(SampleKind.LosslessWithIcc), "ICCP", size);
        var decoder = new WebpDecoder { IgnoreMetadata = ignoreMetadata };

        Exception loadException = Record.Exception(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(patched, decoder);
        });
        Exception identifyException = Record.Exception(() => Image.Identify(patched));

        if (loadException is not null)
        {
            Assert.IsType<InvalidImageContentException>(loadException);
        }

        if (identifyException is not null)
        {
            Assert.IsType<InvalidImageContentException>(identifyException);
        }
    }

    [Theory]
    [InlineData(SampleKind.LossyWithExifAndXmp, "EXIF")]
    [InlineData(SampleKind.LossyWithExifAndXmp, "XMP ")]
    [InlineData(SampleKind.LosslessWithIcc, "ICCP")]
    public void Decode_MetadataChunkSizeLargerThanStream_DoesNotAllocateDeclaredSize(SampleKind kind, string chunkId)
    {
        const uint declaredSize = 256 * 1024 * 1024;
        byte[] patched = PatchChunkSize(Encode(kind), chunkId, declaredSize);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Record.Exception(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(patched);
        });
        Record.Exception(() => Image.Identify(patched));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < declaredSize / 4, $"Decoding allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(SampleKind.SimpleLossy)]
    [InlineData(SampleKind.SimpleLossless)]
    [InlineData(SampleKind.LossyWithAlpha)]
    [InlineData(SampleKind.LosslessWithExif)]
    [InlineData(SampleKind.LossyWithExifAndXmp)]
    [InlineData(SampleKind.LosslessWithIcc)]
    public void Decode_TruncatedFile_ThrowsInvalidImageContent(SampleKind kind)
    {
        byte[] original = Encode(kind);
        bool lossless = TryFindChunk(original, "VP8L", out int imageChunk);
        if (!lossless)
        {
            imageChunk = FindChunk(original, "VP8 ");
        }

        // Up to the end of the image header every byte is needed: the file can not be decoded or identified.
        int imageHeaderEnd = imageChunk + 8 + (lossless ? 5 : 10);

        // The format detector needs 12 bytes, so shorter files are not recognized as WebP at all.
        for (int length = 12; length < original.Length; length++)
        {
            byte[] truncated = original.AsSpan(0, length).ToArray();
            Exception loadException = Record.Exception(() =>
            {
                using Image<Rgba32> image = Image.Load<Rgba32>(truncated);
            });
            Exception identifyException = Record.Exception(() => Image.Identify(truncated));

            // Inside the image data a truncated file may still decode, as the missing tail is read as zeros.
            // If it can not be decoded the error is reported as invalid content.
            bool mustThrow = length < imageHeaderEnd;
            AssertInvalidImageContent(loadException, mustThrow, $"Load of {length} bytes");
            AssertInvalidImageContent(identifyException, mustThrow, $"Identify of {length} bytes");
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x7FFFFFF0u)]
    [InlineData(0xFFFFFFF0u)]
    [InlineData(0xFFFFFFFFu)]
    public void Decode_AlphaChunkSizeInvalid_ThrowsInvalidImageContent(uint size)
    {
        byte[] patched = PatchChunkSize(Encode(SampleKind.LossyWithAlpha), "ALPH", size);

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(patched);
        });
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(patched));
    }

    [Theory]
    [InlineData(SampleKind.SimpleLossless, "VP8L", 0x7FFFFFF0u)]
    [InlineData(SampleKind.SimpleLossless, "VP8L", 0xFFFFFFF0u)]
    [InlineData(SampleKind.SimpleLossy, "VP8 ", 0x7FFFFFF0u)]
    [InlineData(SampleKind.SimpleLossy, "VP8 ", 0xFFFFFFF0u)]
    public void Decode_ImageChunkSizeLargerThanStream_ThrowsInvalidImageContent(SampleKind kind, string chunkId, uint size)
    {
        byte[] patched = PatchChunkSize(Encode(kind), chunkId, size);

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(patched);
        });
    }

    private static void AssertInvalidImageContent(Exception exception, bool mustThrow, string operation)
    {
        if (exception is null)
        {
            Assert.False(mustThrow, $"{operation} did not throw.");
            return;
        }

        Assert.True(exception is InvalidImageContentException, $"{operation} threw {exception}");
    }

    private static byte[] PatchChunkSize(byte[] data, string chunkId, uint size)
    {
        byte[] patched = (byte[])data.Clone();
        int offset = FindChunk(patched, chunkId);
        BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(offset + 4), size);
        return patched;
    }

    private static int FindChunk(byte[] data, string chunkId)
        => TryFindChunk(data, chunkId, out int offset) ? offset : throw new InvalidOperationException($"Chunk '{chunkId}' not found.");

    private static bool TryFindChunk(byte[] data, string chunkId, out int offset)
    {
        offset = 12;
        while (offset + 8 <= data.Length)
        {
            if (Encoding.ASCII.GetString(data, offset, 4) == chunkId)
            {
                return true;
            }

            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            offset += 8 + (int)size + (int)(size & 1);
        }

        return false;
    }

    private static void AssertSameImage(byte[] expectedData, byte[] actualData)
    {
        using Image<Rgba32> expected = Image.Load<Rgba32>(expectedData);
        using Image<Rgba32> actual = Image.Load<Rgba32>(actualData);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
        Assert.Equal(expected.Metadata.ExifProfile?.Values.Count, actual.Metadata.ExifProfile?.Values.Count);
        Assert.Equal(expected.Metadata.XmpProfile?.ToByteArray(), actual.Metadata.XmpProfile?.ToByteArray());
        Assert.Equal(expected.Metadata.IccProfile?.ToByteArray(), actual.Metadata.IccProfile?.ToByteArray());

        IImageInfo expectedInfo = Image.Identify(expectedData);
        IImageInfo actualInfo = Image.Identify(actualData);
        Assert.Equal(expectedInfo.Width, actualInfo.Width);
        Assert.Equal(expectedInfo.Height, actualInfo.Height);
        Assert.Equal(expectedInfo.PixelType.BitsPerPixel, actualInfo.PixelType.BitsPerPixel);
    }

    private static byte[] Encode(SampleKind kind)
    {
        bool lossless = kind is SampleKind.SimpleLossless or SampleKind.LosslessWithExif or SampleKind.LosslessWithIcc;
        bool alpha = kind is SampleKind.LossyWithAlpha or SampleKind.LosslessWithExif;

        using var image = new Image<Rgba32>(23, 17);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte a = alpha ? (byte)(255 - (x * 9)) : (byte)255;
                    row[x] = new Rgba32((byte)(x * 11), (byte)(y * 15), (byte)((x + y) * 6), a);
                }
            }
        });

        if (kind is SampleKind.LosslessWithExif or SampleKind.LossyWithExifAndXmp)
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Software, "ImageSharp");
            exif.SetValue(ExifTag.Artist, "WebP robustness");
            image.Metadata.ExifProfile = exif;
        }

        if (kind is SampleKind.LossyWithExifAndXmp)
        {
            image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'></x:xmpmeta>"));
        }

        if (kind is SampleKind.LosslessWithIcc)
        {
            byte[] iccBytes = File.ReadAllBytes(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, TestImages.Webp.Lossless.WithIccp));
            using Image iccSource = Image.Load(iccBytes);
            image.Metadata.IccProfile = iccSource.Metadata.IccProfile;
        }

        using var stream = new MemoryStream();
        image.Save(stream, new WebpEncoder { FileFormat = lossless ? WebpFileFormatType.Lossless : WebpFileFormatType.Lossy });
        return stream.ToArray();
    }
}
