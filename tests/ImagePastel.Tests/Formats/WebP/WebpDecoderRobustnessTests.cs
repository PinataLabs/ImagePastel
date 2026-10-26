// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using PinataLabs.ImagePastel.Formats.Webp;
using PinataLabs.ImagePastel.Metadata.Profiles.Exif;
using PinataLabs.ImagePastel.Metadata.Profiles.Xmp;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Memory;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Webp;

/// <summary>
/// Decodes WebP files with container-level oddities: non-zero reserved fields, bytes after the RIFF payload,
/// truncated chunks and metadata chunks with impossible sizes.
/// </summary>
[Trait("Format", "Webp")]
[ValidateDisposedMemoryAllocations]
public class WebpDecoderRobustnessTests
{
    private const byte AlphaFlag = 0x10;

    private const byte ExifFlag = 0x08;

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

    [Fact]
    public void Decode_ChunkHeaderSplitAcrossRiffEnd_IsIgnored()
    {
        // The RIFF payload ends with "VP", and "8L" plus a lossless chunk for another image follow it.
        // Reading the chunk header across the RIFF end would replace the image with the trailing one.
        byte[] original = Encode(SampleKind.LossyWithAlpha);

        using var other = new Image<Rgba32>(5, 3);
        using var otherStream = new MemoryStream();
        other.Save(otherStream, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        byte[] otherChunk = otherStream.ToArray().AsSpan(FindChunk(otherStream.ToArray(), "VP8L")).ToArray();

        byte[] patched = [.. original, .. otherChunk];
        uint riffSize = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4));
        BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(4), riffSize + 2);

        AssertSameImage(original, patched);
    }

    [Theory]
    [InlineData(SampleKind.LossyWithAlpha, 2)]
    [InlineData(SampleKind.LosslessWithExif, 2)]
    [InlineData(SampleKind.LossyWithExifAndXmp, 1)]
    [InlineData(SampleKind.LossyWithExifAndXmp, 6)]
    public void Decode_VP8XChunkLongerThanTenBytes_SkipsExtraBytes(SampleKind kind, int extraBytes)
    {
        byte[] original = Encode(kind);
        int vp8x = FindChunk(original, "VP8X");
        Assert.Equal(10u, BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(vp8x + 4)));

        // An odd chunk size is followed by a padding byte.
        int payloadEnd = vp8x + 8 + 10;
        int insertedBytes = extraBytes + (extraBytes & 1);
        byte[] patched = [.. original.AsSpan(0, payloadEnd), .. new byte[insertedBytes], .. original.AsSpan(payloadEnd)];
        BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(vp8x + 4), (uint)(10 + extraBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(4), BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4)) + (uint)insertedBytes);

        AssertSameImage(original, patched);
    }

    [Fact]
    public void Decode_OddSizedVp8ChunkInVP8X_SkipsPaddingByte()
    {
        // Append a zero byte to the VP8 payload, so its size is odd and a padding byte follows it.
        // A zero byte after the last partition decodes the same as the end of the data.
        // The EXIF and XMP chunks after the VP8 chunk are only found when the padding byte is skipped.
        byte[] original = Encode(SampleKind.LossyWithExifAndXmp);
        int vp8 = FindChunk(original, "VP8 ");
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(vp8 + 4));
        Assert.Equal(0u, size & 1);
        Assert.True(FindChunk(original, "EXIF") > vp8);

        int payloadEnd = vp8 + 8 + (int)size;
        byte[] patched = [.. original.AsSpan(0, payloadEnd), 0, 0, .. original.AsSpan(payloadEnd)];
        BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(vp8 + 4), size + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(4), BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4)) + 2);

        AssertSameImage(original, patched);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(8u)]
    [InlineData(9u)]
    public void Decode_VP8XChunkShorterThanTenBytes_ThrowsInvalidImageContent(uint size)
    {
        byte[] patched = PatchChunkSize(Encode(SampleKind.LosslessWithExif), "VP8X", size);

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(patched);
        });
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(patched));
    }

    [Theory]
    [InlineData(SampleKind.SimpleLossless)]
    [InlineData(SampleKind.LosslessWithIcc)]
    public void Decode_InvalidVp8LSignature_ThrowsAndReleasesBuffers(SampleKind kind)
    {
        byte[] data = Encode(kind);
        data[FindChunk(data, "VP8L") + 8] = 0;

        Assert.ThrowsAny<ImageFormatException>(() => Image.Load<Rgba32>(data));
        Assert.ThrowsAny<ImageFormatException>(() => Image.Identify(data));
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
    [InlineData(SampleKind.SimpleLossless, "VP8L", 0x7FFFFFFFu)]
    [InlineData(SampleKind.SimpleLossless, "VP8L", 0xFFFFFFF0u)]
    [InlineData(SampleKind.SimpleLossy, "VP8 ", 0xFFFFFFF0u)]
    public void Decode_ImageChunkSizeAboveIntMaxValue_ThrowsInvalidImageContent(SampleKind kind, string chunkId, uint size)
    {
        byte[] patched = PatchChunkSize(Encode(kind), chunkId, size);

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(patched);
        });
    }

    [Theory]
    [InlineData(SampleKind.SimpleLossless, "VP8L", 0x10000000u)]
    [InlineData(SampleKind.SimpleLossless, "VP8L", 0x7FFFFFF0u)]
    [InlineData(SampleKind.LosslessWithExif, "VP8L", 0x10000000u)]
    [InlineData(SampleKind.SimpleLossy, "VP8 ", 0x10000000u)]
    [InlineData(SampleKind.SimpleLossy, "VP8 ", 0x7FFFFFF0u)]
    [InlineData(SampleKind.LossyWithAlpha, "VP8 ", 0x10000000u)]
    public void Decode_ImageChunkSizeLargerThanStream_DoesNotAllocateDeclaredSize(SampleKind kind, string chunkId, uint declaredSize)
    {
        // The missing end of the image data is read as zeros, so the file decodes, or fails as invalid content.
        byte[] patched = PatchChunkSize(Encode(kind), chunkId, declaredSize);
        var allocator = new TestMemoryAllocator();
        allocator.EnableNonThreadSafeLogging();
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.MemoryAllocator = allocator;

        Exception loadException = Record.Exception(() =>
        {
            using var stream = new MemoryStream(patched);
            using Image<Rgba32> image = Image.Load<Rgba32>(configuration, stream);
        });
        Exception identifyException = Record.Exception(() =>
        {
            using var stream = new MemoryStream(patched);
            Image.Identify(configuration, stream);
        });

        AssertInvalidImageContent(loadException, false, "Load");
        AssertInvalidImageContent(identifyException, false, "Identify");
        int largest = allocator.AllocationLog.Max(a => a.LengthInBytes);
        Assert.True(largest < 1024 * 1024, $"Decoding allocated a buffer of {largest} bytes.");
    }

    [Theory]
    [InlineData(SampleKind.SimpleLossless, "VP8L")]
    [InlineData(SampleKind.SimpleLossy, "VP8 ")]
    public void Decode_ImageChunkLongerThanStream_DecodesAsIfPaddedWithZeros(SampleKind kind, string chunkId)
    {
        const int missingBytes = 1000;
        byte[] original = Encode(kind);
        int chunk = FindChunk(original, chunkId);
        Assert.Equal(original.Length, chunk + 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(chunk + 4)));
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(chunk + 4)) + missingBytes;

        byte[] truncated = PatchChunkSize(original, chunkId, size);
        byte[] padded = [.. truncated, .. new byte[missingBytes]];
        BinaryPrimitives.WriteUInt32LittleEndian(padded.AsSpan(4), (uint)(padded.Length - 8));

        using Image<Rgba32> expected = Image.Load<Rgba32>(padded);
        using Image<Rgba32> actual = Image.Load<Rgba32>(truncated);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Theory]
    [InlineData(4, 4)] // 1 + 16 bytes: odd, a padding byte follows.
    [InlineData(5, 3)] // 1 + 15 bytes: even, no padding byte.
    public void Decode_UncompressedAlphaChunk_DecodesAlphaAndFollowingChunks(int width, int height)
    {
        // The VP8 and EXIF chunks after the alpha chunk are only found when its padding byte is skipped.
        byte[] alpha = AlphaValues(width * height);
        byte[] data = BuildVp8XFile(
            AlphaFlag | ExifFlag,
            width,
            height,
            BuildChunk("ALPH", [0, .. alpha]),
            Vp8Chunk(width, height),
            BuildChunk("EXIF", ExifBytes()));

        using Image<Rgba32> image = Image.Load<Rgba32>(data);
        AssertAlpha(alpha, image);
        Assert.NotNull(image.Metadata.ExifProfile);
    }

    [Fact]
    public void Decode_OddSizedAlphaChunk_DoesNotReadPaddingByteAsAlpha()
    {
        // The chunk holds one alpha byte fewer than the 15 pixels need. The padding byte after it is not alpha data.
        const int width = 5;
        const int height = 3;
        byte[] alpha = AlphaValues((width * height) - 1);
        byte[] data = BuildVp8XFile(
            AlphaFlag,
            width,
            height,
            BuildChunk("ALPH", [0, .. alpha], 0xFF),
            Vp8Chunk(width, height));

        ImageFormatException ex = Assert.Throws<ImageFormatException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(data);
        });
        Assert.Equal("not enough data in the ALPH chunk", ex.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decode_OddSizedAlphaChunkAtEndOfFileWithoutPadding_Decodes(bool riffSizeIncludesPadding)
    {
        // The alpha chunk is the last chunk and the file ends without its padding byte.
        const int width = 4;
        const int height = 4;
        byte[] alpha = AlphaValues(width * height);
        byte[] alphaChunk = BuildChunk("ALPH", [0, .. alpha]);
        byte[] data = BuildVp8XFile(AlphaFlag, width, height, Vp8Chunk(width, height), alphaChunk.AsSpan(0, alphaChunk.Length - 1).ToArray());
        if (riffSizeIncludesPadding)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)(data.Length + 1 - 8));
        }

        using Image<Rgba32> image = Image.Load<Rgba32>(data);
        AssertAlpha(alpha, image);

        IImageInfo info = Image.Identify(data);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
    }

    [Fact]
    public void Decode_TwoAlphaChunks_UsesTheLastAndReleasesTheFirst()
    {
        // The class checks that every buffer is returned, so the alpha data of the first chunk must be released.
        const int width = 4;
        const int height = 4;
        byte[] alpha = AlphaValues(width * height);
        byte[] data = BuildVp8XFile(
            AlphaFlag,
            width,
            height,
            BuildChunk("ALPH", [0, .. new byte[width * height]]),
            BuildChunk("ALPH", [0, .. alpha]),
            Vp8Chunk(width, height));

        using Image<Rgba32> image = Image.Load<Rgba32>(data);
        AssertAlpha(alpha, image);
    }

    [Fact]
    public void Decode_OddSizedVp8LChunkInVP8X_SkipsPaddingByte()
    {
        // Append a zero byte to the VP8L payload, so its size is odd and a padding byte follows it.
        // The lossless decoder ignores bytes after the end of the bitstream.
        // The EXIF chunk after the VP8L chunk is only found when the padding byte is skipped.
        byte[] original = Encode(SampleKind.LosslessWithExif);
        int vp8l = FindChunk(original, "VP8L");
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(vp8l + 4));
        Assert.Equal(0u, size & 1);
        Assert.True(FindChunk(original, "EXIF") > vp8l);

        int payloadEnd = vp8l + 8 + (int)size;
        byte[] patched = [.. original.AsSpan(0, payloadEnd), 0, 0, .. original.AsSpan(payloadEnd)];
        BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(vp8l + 4), size + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(4), BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4)) + 2);

        AssertSameImage(original, patched);
    }

    [Fact]
    public void Decode_OddSizedVp8LChunkAtEndOfFileWithoutPadding_IsNotReportedAsTruncated()
    {
        // A 1x1 VP8L bitstream with an invalid color cache size, in a 7 byte chunk with no padding byte after it.
        // All of the chunk's data is in the file, so the error is the bitstream error, not missing data.
        byte[] vp8l = [0x2F, 0, 0, 0, 0, 0x02, 0];
        byte[] chunk = BuildChunk("VP8L", vp8l);
        byte[] data = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. chunk.AsSpan(0, chunk.Length - 1)];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)(data.Length - 8));

        ImageFormatException ex = Assert.Throws<ImageFormatException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(data);
        });
        Assert.Equal("Invalid color cache bits found", ex.Message);
    }

    private static byte[] AlphaValues(int count)
    {
        byte[] alpha = new byte[count];
        for (int i = 0; i < alpha.Length; i++)
        {
            alpha[i] = (byte)(255 - (i * 13));
        }

        return alpha;
    }

    private static void AssertAlpha(byte[] expected, Image<Rgba32> image)
    {
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Assert.Equal(expected[(y * image.Width) + x], image[x, y].A);
            }
        }
    }

    private static byte[] ExifBytes()
    {
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Software, "ImageSharp");
        return exif.ToByteArray();
    }

    private static byte[] Vp8Chunk(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(40, 120, 200, 255));
        using var stream = new MemoryStream();
        image.Save(stream, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy });
        byte[] data = stream.ToArray();
        int offset = FindChunk(data, "VP8 ");
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
        return data.AsSpan(offset, 8 + (int)size + (int)(size & 1)).ToArray();
    }

    /// <summary>
    /// Builds a chunk with its header, adding a padding byte after an odd-sized payload.
    /// </summary>
    private static byte[] BuildChunk(string chunkId, byte[] payload, byte paddingByte = 0)
    {
        byte[] chunk = new byte[8 + payload.Length + (payload.Length & 1)];
        Encoding.ASCII.GetBytes(chunkId).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(chunk, 8);
        if ((payload.Length & 1) == 1)
        {
            chunk[^1] = paddingByte;
        }

        return chunk;
    }

    /// <summary>
    /// Builds an extended WebP file with a VP8X chunk holding the given feature flags, followed by the given chunks.
    /// </summary>
    private static byte[] BuildVp8XFile(byte flags, int width, int height, params byte[][] chunks)
    {
        byte[] vp8x = new byte[10];
        vp8x[0] = flags;
        WriteUInt24LittleEndian(vp8x.AsSpan(4), width - 1);
        WriteUInt24LittleEndian(vp8x.AsSpan(7), height - 1);

        using var stream = new MemoryStream();
        stream.Write("RIFF"u8);
        stream.Write(new byte[4]);
        stream.Write("WEBP"u8);
        stream.Write(BuildChunk("VP8X", vp8x));
        foreach (byte[] chunk in chunks)
        {
            stream.Write(chunk);
        }

        byte[] data = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)(data.Length - 8));
        return data;
    }

    private static void WriteUInt24LittleEndian(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
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
