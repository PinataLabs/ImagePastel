// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PinataLabs.ImagePastel.Compression.Brotli;
using Xunit;
using static PinataLabs.ImagePastel.Tests.Compression.Brotli.BrotliTestStreams;

namespace PinataLabs.ImagePastel.Tests.Compression.Brotli;

public class ManagedBrotliDecoderTests
{
    private const int MinWindow = 10;
    private const int MaxWindow = 24;

    public static TheoryData<int> Qualities() => new(Enumerable.Range(0, 12));

    [Theory]
    [MemberData(nameof(Qualities))]
    public void Decompress_StreamsOfEveryWindowSize_GiveTheOutputOfBrotliDecoder(int quality)
    {
        (string Kind, int Length)[] inputs = { ("random", 0), ("random", 5000), ("text", 20000), ("repetitive", 60000), ("mixed", 300000) };
        for (int window = MinWindow; window <= MaxWindow; window++)
        {
            foreach ((string kind, int length) in inputs)
            {
                byte[] input = Input(kind, length, seed: (quality * 100) + window);
                byte[] stream = Compress(input, quality, window);
                string because = $"{length} {kind} bytes at quality {quality}, window {window}";

                Assert.Equal(Result.Done, Managed(stream, 64 << 10, out byte[] output, out int consumed));
                Assert.True(input.AsSpan().SequenceEqual(output), because);
                Assert.Equal(stream.Length, consumed);
                AssertSameAsOracle(stream, 64 << 10, because);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(13)]
    [InlineData(1000)]
    [InlineData(20000)]
    [InlineData(20001)]
    public void Decompress_IntoBuffersOfAnySize_GivesTheSameOutput(int chunkSize)
    {
        foreach (int quality in new[] { 0, 3, 6, 9, 11 })
        {
            foreach (int window in new[] { 10, 13, 22 })
            {
                foreach (string kind in new[] { "random", "text", "repetitive", "mixed" })
                {
                    byte[] input = Input(kind, 20000, seed: quality + window);
                    byte[] stream = Compress(input, quality, window);

                    Assert.Equal(Result.Done, Managed(stream, chunkSize, out byte[] output, out int consumed));
                    Assert.True(input.AsSpan().SequenceEqual(output), $"{kind} at quality {quality}, window {window}");
                    Assert.Equal(stream.Length, consumed);
                }
            }
        }
    }

    [Theory]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    public void Decompress_StreamsLongerThanALargeWindow_GiveTheOutputOfBrotliDecoder(int window)
    {
        byte[] input = Input("mixed", (1 << window) + 100000, seed: window);
        foreach (int quality in new[] { 2, 6 })
        {
            byte[] stream = Compress(input, quality, window);

            Assert.Equal(Result.Done, Managed(stream, 64 << 10, out byte[] output, out int consumed));
            Assert.True(input.AsSpan().SequenceEqual(output), $"quality {quality}");
            Assert.Equal(stream.Length, consumed);
            AssertSameAsOracle(stream, 1 << 20, $"quality {quality}, window {window}");
        }
    }

    // A flush ends the meta-block and adds an empty metadata meta-block, to end on a byte.
    [Theory]
    [InlineData(1, 10, 700)]
    [InlineData(5, 16, 1)]
    [InlineData(5, 22, 4096)]
    [InlineData(11, 24, 30000)]
    public void Decompress_FlushedStreams_GiveTheOutputOfBrotliDecoder(int quality, int window, int flushEvery)
    {
        foreach (string kind in new[] { "random", "text", "mixed" })
        {
            byte[] input = Input(kind, flushEvery == 1 ? 3000 : 100000, seed: flushEvery);
            byte[] stream = Compress(input, quality, window, flushEvery);

            Assert.Equal(Result.Done, AssertSameAsOracle(stream, 64 << 10, kind));
            Assert.Equal(Result.Done, Managed(stream, 64 << 10, out byte[] output, out _));
            Assert.True(input.AsSpan().SequenceEqual(output), kind);
        }
    }

    [Fact]
    public void Decompress_TheEmptyStream_GivesNoOutput()
    {
        // WBITS 16, then a last meta-block that is empty: what BrotliEncoder writes for no input.
        byte[] stream = { 0x06 };

        Assert.Equal(Result.Done, AssertSameAsOracle(stream, 16, "the empty stream"));
        Assert.Equal(Result.Done, Managed(stream, 16, out byte[] output, out int consumed));
        Assert.Empty(output);
        Assert.Equal(1, consumed);
        for (int quality = 0; quality <= 11; quality++)
        {
            Assert.Equal(Result.Done, AssertSameAsOracle(Compress(ReadOnlySpan<byte>.Empty, quality, 22), 16, $"quality {quality}"));
        }
    }

    [Fact]
    public void Decompress_NoInput_ThrowsInvalidImageContent()
    {
        ManagedBrotliDecoder decoder = new();

        Assert.Throws<InvalidImageContentException>(() => decoder.Decompress(ReadOnlySpan<byte>.Empty, new byte[16], out _, out _));
        Assert.Equal(Result.Unreadable, AssertSameAsOracle(ReadOnlySpan<byte>.Empty, 16, "no input"));
    }

    [Theory]
    [InlineData("random", 300, 5)]
    [InlineData("text", 2000, 9)]
    [InlineData("repetitive", 5000, 11)]
    [InlineData("mixed", 100000, 1)]
    public void Decompress_TruncatedStream_ThrowsInvalidImageContent(string kind, int length, int quality)
    {
        byte[] stream = Compress(Input(kind, length, seed: 3), quality, 18, flushEvery: length / 3);

        int step = Math.Max(1, stream.Length / 300);
        for (int cut = 0; cut < stream.Length; cut += step)
        {
            byte[] truncated = stream.AsSpan(0, cut).ToArray();
            ManagedBrotliDecoder decoder = new();
            byte[] buffer = new byte[64 << 10];

            Assert.Throws<InvalidImageContentException>(() =>
            {
                int consumed = 0;
                while (decoder.Decompress(truncated.AsSpan(consumed), buffer, out int used, out _) != OperationStatus.Done)
                {
                    consumed += used;
                }
            });
            Assert.Equal(Result.Unreadable, Oracle(truncated, out _, out _));
        }
    }

    [Fact]
    public void Decompress_BytesAfterTheStream_AreNotConsumed()
    {
        byte[] input = Input("text", 5000, seed: 1);
        byte[] stream = Compress(input, 5, 22);
        byte[] withTail = stream.Concat(new byte[] { 1, 2, 3, 4, 5 }).ToArray();

        Assert.Equal(Result.Done, AssertSameAsOracle(withTail, 64 << 10, "a stream with bytes after it"));
        Assert.Equal(Result.Done, Managed(withTail, 64 << 10, out byte[] output, out int consumed));
        Assert.Equal(input, output);
        Assert.Equal(stream.Length, consumed);
    }

    [Fact]
    public void Decompress_AfterTheEnd_WritesNothingMore()
    {
        byte[] stream = Compress(Input("text", 100, seed: 1), 5, 22);
        ManagedBrotliDecoder decoder = new();
        byte[] buffer = new byte[200];

        Assert.Equal(OperationStatus.Done, decoder.Decompress(stream, buffer, out int consumed, out int written));
        Assert.Equal(100, written);
        Assert.Equal(OperationStatus.Done, decoder.Decompress(stream.AsSpan(consumed), buffer, out consumed, out written));
        Assert.Equal(0, consumed);
        Assert.Equal(0, written);
    }

    [Fact]
    public void Decompress_AfterAFailure_ThrowsAgain()
    {
        byte[] stream = Compress(Input("text", 5000, seed: 1), 5, 22);
        ManagedBrotliDecoder decoder = new();
        byte[] buffer = new byte[64 << 10];

        Assert.Throws<InvalidImageContentException>(() => decoder.Decompress(stream.AsSpan(0, 100), buffer, out _, out _));
        Assert.Throws<InvalidImageContentException>(() => decoder.Decompress(stream, buffer, out _, out _));
    }

    // 0x11 starts a large window stream, which needs a decoder parameter that BrotliDecoder does not set.
    [Theory]
    [InlineData(new byte[] { 0x11, 0x06, 0x03 })]
    [InlineData(new byte[] { 0x91, 0x06, 0x03 })]
    public void Decompress_LargeWindowStream_ThrowsInvalidImageContent(byte[] stream)
        => Assert.Equal(Result.Unreadable, AssertSameAsOracle(stream, 16, "a large window stream"));

    [Fact]
    public void Decompress_MetadataMetaBlock_IsSkipped()
    {
        BitWriter writer = new();
        writer.Write(0, 1); // WBITS: 16
        WriteMetadataHeader(writer, skipBytes: 2, length: 300);
        writer.WriteBytes(Input("random", 300, seed: 1));
        writer.Write(3, 2); // ISLAST, ISLASTEMPTY
        byte[] stream = writer.ToArray();

        Assert.Equal(Result.Done, AssertSameAsOracle(stream, 16, "a metadata meta-block"));
        Assert.Equal(Result.Done, Managed(stream, 16, out byte[] output, out int consumed));
        Assert.Empty(output);
        Assert.Equal(stream.Length, consumed);
        Assert.Equal(Result.Unreadable, AssertSameAsOracle(stream.AsSpan(0, 200), 16, "a metadata meta-block cut short"));
    }

    [Fact]
    public void Decompress_MetadataLengthWithAZeroTopByte_ThrowsInvalidImageContent()
    {
        BitWriter writer = new();
        writer.Write(0, 1); // WBITS: 16
        writer.Write(0, 1); // ISLAST
        writer.Write(3, 2); // MNIBBLES: a metadata meta-block
        writer.Write(0, 1); // reserved
        writer.Write(2, 2); // MSKIPBYTES
        writer.Write(5, 8);
        writer.Write(0, 8);
        writer.WriteBytes(new byte[16]);

        Assert.Equal(Result.Unreadable, AssertSameAsOracle(writer.ToArray(), 16, "a metadata length with a zero top byte"));
    }

    // Two literal block types and blocks of one literal. Each block switch is "the type before, plus
    // one", which wraps from the last type to the first.
    [Fact]
    public void Decompress_BlockTypeAfterTheLast_IsTheFirst()
    {
        BitWriter writer = new();
        writer.Write(0, 1); // WBITS: 16
        writer.Write(1, 1); // ISLAST
        writer.Write(0, 1); // ISLASTEMPTY
        writer.Write(0, 2); // MNIBBLES: 4
        writer.Write(6 - 1, 16); // MLEN - 1
        writer.Write(1, 4); // NBLTYPESL: 2
        WriteSimpleCode(writer, symbolBits: 2, 1); // block types: always "the type before, plus one"
        WriteSimpleCode(writer, symbolBits: 5, 0); // block lengths: always 1 to 4
        writer.Write(0, 2); // the first block has 1 literal
        writer.Write(0, 1); // NBLTYPESI: 1
        writer.Write(0, 1); // NBLTYPESD: 1
        writer.Write(0, 6); // NPOSTFIX, NDIRECT
        writer.Write(0, 4); // the context modes of the two block types
        writer.Write(0, 1); // NTREESL: 1
        writer.Write(0, 1); // NTREESD: 1
        WriteSimpleCode(writer, symbolBits: 8, 'a', 'b');
        WriteSimpleCode(writer, symbolBits: 10, 32); // insert 4 literals, copy 2 bytes at the last distance
        WriteSimpleCode(writer, symbolBits: 6, 0);
        writer.Write(0, 1); // a
        foreach (int literal in new[] { 1, 1, 0 })
        {
            writer.Write(0, 2); // the next block has 1 literal
            writer.Write(literal, 1);
        }

        byte[] stream = writer.ToArray();

        // The last distance of a new stream is 4.
        Assert.Equal(Result.Done, AssertSameAsOracle(stream, 16, "two literal block types"));
        Assert.Equal(Result.Done, Managed(stream, 16, out byte[] output, out _));
        Assert.Equal("abbaab", Encoding.ASCII.GetString(output));
    }

    // 2^24 commands with no output: each copies a 4-byte dictionary word that transform 42 (omit the
    // last 4 bytes) cuts to nothing. The command block, of the one block type, then ends before the
    // meta-block does. BrotliDecoder is not asked: the C source has no error for this, so what the
    // native library does may depend on its build. On Windows x64 it returns InvalidData.
    [Fact]
    public void Decompress_MoreCommandsThanTheBlockLengthOfOneBlockType_ThrowsInvalidImageContent()
    {
        const int commands = 1 << 24;
        const int distance = (42 << 10) + 1;
        BitWriter writer = new();
        writer.Write(0, 1); // WBITS: 16
        writer.Write(1, 1); // ISLAST
        writer.Write(0, 1); // ISLASTEMPTY
        writer.Write(0, 2); // MNIBBLES: 4
        writer.Write(0xFFFF, 16); // MLEN - 1
        writer.Write(0, 3); // NBLTYPESL, NBLTYPESI, NBLTYPESD: 1
        writer.Write(0, 6); // NPOSTFIX, NDIRECT
        writer.Write(0, 2); // the context mode
        writer.Write(0, 2); // NTREESL, NTREESD: 1
        WriteSimpleCode(writer, symbolBits: 8, 0);
        WriteSimpleCode(writer, symbolBits: 10, 130); // insert nothing, copy 4 bytes at a distance that is read
        WriteSimpleCode(writer, symbolBits: 6, 42); // distances from 32765, with 14 extra bits
        byte[] header = writer.ToArray(out int headerBits);
        byte[] stream = new byte[(int)((headerBits + (14L * (commands + 1))) / 8) + 3];
        header.CopyTo(stream, 0);
        long bit = headerBits;
        for (int i = 0; i <= commands; i++, bit += 14)
        {
            uint bits = (uint)(distance - 32765) << (int)(bit & 7);
            int index = (int)(bit >> 3);
            stream[index] |= (byte)bits;
            stream[index + 1] |= (byte)(bits >> 8);
            stream[index + 2] |= (byte)(bits >> 16);
        }

        ManagedBrotliDecoder decoder = new();

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => decoder.Decompress(stream, new byte[16], out _, out _));
        Assert.Contains("more commands", ex.Message);
    }

    public static TheoryData<int> MutationGroups() => new(Enumerable.Range(0, 8));

    [Theory]
    [MemberData(nameof(MutationGroups))]
    public void Decompress_MutatedStreams_ThrowInvalidImageContentOrGiveTheOutputOfBrotliDecoder(int group)
    {
        Random random = new(1000 + group);
        int[] qualities = { 0, 2, 5, 7, 9, 11 };
        string[] kinds = { "random", "text", "repetitive", "mixed" };
        int unreadable = 0;
        int total = 0;
        foreach (int quality in qualities)
        {
            int window = MinWindow + random.Next(MaxWindow - MinWindow + 1);
            byte[] input = Input(kinds[(group + quality) % kinds.Length], 200 + random.Next(6000), seed: random.Next());
            byte[] stream = Compress(input, quality, window, flushEvery: random.Next(3) == 0 ? 1 + random.Next(2000) : 0);
            for (int i = 0; i < 150; i++)
            {
                byte[] mutated = Mutate(stream, random);
                string because = $"group {group}, quality {quality}, mutation {i}";

                Result result = AssertSameAsOracle(mutated, 64 << 10, because);

                AssertBoundedAllocation(mutated, because);
                unreadable += result == Result.Unreadable ? 1 : 0;
                total++;
            }
        }

        for (int i = 0; i < 100; i++)
        {
            byte[] garbage = new byte[1 + random.Next(64)];
            random.NextBytes(garbage);

            Result result = AssertSameAsOracle(garbage, 64 << 10, $"group {group}, random bytes {i}");

            unreadable += result == Result.Unreadable ? 1 : 0;
            total++;
        }

        Assert.InRange(unreadable, total / 4, total - 1);
    }

    [Fact]
    public void Decompress_BrobBoxesOfTheTestFiles_GiveTheOutputOfBrotliDecoder()
    {
        int readable = 0;
        string directory = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl");
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            foreach (byte[] box in BrobBoxes(File.ReadAllBytes(path)))
            {
                // The contents start with the type of the box that is compressed.
                Result result = AssertSameAsOracle(box.AsSpan(4), 64 << 10, Path.GetFileName(path));

                readable += result == Result.Done ? 1 : 0;
            }
        }

        Assert.True(readable >= 2, $"{readable} brob boxes");
    }

    private static void WriteMetadataHeader(BitWriter writer, int skipBytes, int length)
    {
        writer.Write(0, 1); // ISLAST
        writer.Write(3, 2); // MNIBBLES: a metadata meta-block
        writer.Write(0, 1); // reserved
        writer.Write(skipBytes, 2); // MSKIPBYTES
        writer.Write(length - 1, skipBytes * 8); // MSKIPLEN - 1
        writer.AlignToByte();
    }

    // A simple prefix code (RFC 7932, section 3.4) of one or two symbols.
    private static void WriteSimpleCode(BitWriter writer, int symbolBits, params int[] symbols)
    {
        writer.Write(1, 2); // HSKIP: a simple code
        writer.Write(symbols.Length - 1, 2); // NSYM - 1
        foreach (int symbol in symbols)
        {
            writer.Write(symbol, symbolBits);
        }
    }

    private static byte[] Mutate(byte[] stream, Random random)
    {
        byte[] mutated = (byte[])stream.Clone();
        switch (random.Next(6))
        {
            case 0:
            case 1:
                for (int flips = 1 + random.Next(3); flips > 0; flips--)
                {
                    mutated[random.Next(mutated.Length)] ^= (byte)(1 << random.Next(8));
                }

                return mutated;
            case 2:
                return mutated.AsSpan(0, random.Next(mutated.Length)).ToArray();
            case 3:
                mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
                return mutated;
            case 4:
                int start = random.Next(mutated.Length);
                int count = 1 + random.Next(Math.Min(8, mutated.Length - start));
                return mutated.AsSpan(0, start).ToArray().Concat(mutated.Skip(start + count)).ToArray();
            default:
                int from = random.Next(mutated.Length);
                random.NextBytes(mutated.AsSpan(from, Math.Min(1 + random.Next(16), mutated.Length - from)));
                return mutated;
        }
    }

    private static void AssertBoundedAllocation(byte[] stream, string because)
    {
        ManagedBrotliDecoder decoder = new();
        byte[] buffer = new byte[64 << 10];
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            int consumed = 0;
            long total = 0;
            while (total <= OutputLimit && decoder.Decompress(stream.AsSpan(consumed), buffer, out int used, out int written) != OperationStatus.Done)
            {
                consumed += used;
                total += written;
            }
        }
        catch (InvalidImageContentException)
        {
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 40 << 20, $"{because}: allocated {allocated} bytes");
    }

    private static IEnumerable<byte[]> BrobBoxes(byte[] file)
    {
        ReadOnlySpan<byte> signature = new byte[] { 0, 0, 0, 0xC, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0xD, 0xA, 0x87, 0xA };
        List<byte[]> boxes = new();
        if (!file.AsSpan().StartsWith(signature))
        {
            return boxes;
        }

        long pos = signature.Length;
        while (pos + 8 <= file.Length)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan((int)pos));
            string type = Encoding.ASCII.GetString(file, (int)pos + 4, 4);
            int headerSize = 8;
            if (size == 1 && pos + 16 <= file.Length)
            {
                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan((int)pos + 8)), int.MaxValue);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = file.Length - pos;
            }

            if (size < headerSize || size > file.Length - pos)
            {
                break;
            }

            if (type == "brob" && size - headerSize >= 4)
            {
                boxes.Add(file.AsSpan((int)pos + headerSize, (int)size - headerSize).ToArray());
            }

            pos += size;
        }

        return boxes;
    }

    // Writes bits, least significant first.
    private sealed class BitWriter
    {
        private readonly List<byte> bytes = new();
        private int bitCount;

        public void Write(int value, int count)
        {
            for (int i = 0; i < count; i++, this.bitCount++)
            {
                if ((this.bitCount & 7) == 0)
                {
                    this.bytes.Add(0);
                }

                this.bytes[^1] |= (byte)(((value >> i) & 1) << (this.bitCount & 7));
            }
        }

        public void AlignToByte() => this.bitCount = this.bytes.Count * 8;

        public void WriteBytes(byte[] data)
        {
            this.AlignToByte();
            this.bytes.AddRange(data);
            this.bitCount = this.bytes.Count * 8;
        }

        public byte[] ToArray() => this.bytes.ToArray();

        public byte[] ToArray(out int bits)
        {
            bits = this.bitCount;
            return this.bytes.ToArray();
        }
    }
}
