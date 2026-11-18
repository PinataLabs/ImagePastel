// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;
using PinataLabs.ImagePastel.Compression.Brotli;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Compression.Brotli;

/// <summary>
/// Makes Brotli streams with <see cref="BrotliEncoder"/>, and decodes them with
/// <see cref="BrotliDecoder"/>, the oracle for <see cref="ManagedBrotliDecoder"/>.
/// </summary>
internal static class BrotliTestStreams
{
    /// <summary>The largest output that <see cref="Oracle"/> collects.</summary>
    public const int OutputLimit = 64 << 20;

    private static readonly string[] Vocabulary =
    {
        "the", "of", "and", "information", "which", "available", "<div class=\"", "</div>", "http://www.", ".com/",
        "copyright", "International", "JPEG XL", "metadata", "xmlns:", "rdf:Description", "0123456789", "\r\n", "    ", "=\"",
    };

    /// <summary>What <see cref="Oracle"/> found.</summary>
    public enum Result
    {
        /// <summary>The stream is complete.</summary>
        Done,

        /// <summary>The stream is corrupt or truncated.</summary>
        Unreadable,

        /// <summary>The stream has more output than <see cref="OutputLimit"/>.</summary>
        TooLarge,
    }

    /// <summary>Compresses <paramref name="data"/> as one stream.</summary>
    /// <param name="data">The data.</param>
    /// <param name="quality">The quality, 0 to 11.</param>
    /// <param name="window">The window bits, 10 to 24.</param>
    /// <param name="flushEvery">If not 0, the number of bytes after which the encoder is flushed.</param>
    /// <returns>The stream.</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data, int quality, int window, int flushEvery = 0)
    {
        // Not a using declaration: that would make the struct read-only, and each call would mutate a copy.
        BrotliEncoder encoder = new(quality, window);
        try
        {
            return Compress(ref encoder, data, flushEvery);
        }
        finally
        {
            encoder.Dispose();
        }
    }

    private static byte[] Compress(ref BrotliEncoder encoder, ReadOnlySpan<byte> data, int flushEvery)
    {
        using MemoryStream stream = new();
        byte[] buffer = new byte[64 << 10];
        int step = flushEvery == 0 ? int.MaxValue : flushEvery;
        while (true)
        {
            ReadOnlySpan<byte> part = data.Slice(0, Math.Min(step, data.Length));
            data = data.Slice(part.Length);
            OperationStatus status;
            do
            {
                status = encoder.Compress(part, buffer, out int consumed, out int written, isFinalBlock: data.IsEmpty);
                part = part.Slice(consumed);
                stream.Write(buffer, 0, written);
            }
            while (status == OperationStatus.DestinationTooSmall || !part.IsEmpty);

            Assert.Equal(OperationStatus.Done, status);
            if (data.IsEmpty)
            {
                return stream.ToArray();
            }

            do
            {
                status = encoder.Flush(buffer, out int written);
                stream.Write(buffer, 0, written);
            }
            while (status == OperationStatus.DestinationTooSmall);
        }
    }

    /// <summary>Decodes <paramref name="stream"/> with <see cref="BrotliDecoder"/>.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="output">The output, when the result is <see cref="Result.Done"/>.</param>
    /// <param name="consumed">The number of bytes of the stream that are used.</param>
    /// <returns>The result.</returns>
    public static Result Oracle(ReadOnlySpan<byte> stream, out byte[] output, out int consumed)
    {
        BrotliDecoder decoder = default;
        try
        {
            return Oracle(ref decoder, stream, out output, out consumed);
        }
        finally
        {
            decoder.Dispose();
        }
    }

    private static Result Oracle(ref BrotliDecoder decoder, ReadOnlySpan<byte> stream, out byte[] output, out int consumed)
    {
        using MemoryStream collected = new();
        byte[] buffer = new byte[64 << 10];
        consumed = 0;
        output = null;
        while (true)
        {
            OperationStatus status = decoder.Decompress(stream.Slice(consumed), buffer, out int used, out int written);
            consumed += used;
            collected.Write(buffer, 0, written);
            if (collected.Length > OutputLimit)
            {
                return Result.TooLarge;
            }

            switch (status)
            {
                case OperationStatus.Done:
                    output = collected.ToArray();
                    return Result.Done;
                case OperationStatus.DestinationTooSmall:
                    break;
                default:
                    return Result.Unreadable;
            }
        }
    }

    /// <summary>
    /// Decodes <paramref name="stream"/> with <see cref="ManagedBrotliDecoder"/>, into buffers of
    /// <paramref name="chunkSize"/> bytes. Any exception but <see cref="InvalidImageContentException"/>
    /// escapes.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="chunkSize">The size of the buffer for each call.</param>
    /// <param name="output">The output, when the result is <see cref="Result.Done"/>.</param>
    /// <param name="consumed">The number of bytes of the stream that are used.</param>
    /// <returns>The result.</returns>
    public static Result Managed(ReadOnlySpan<byte> stream, int chunkSize, out byte[] output, out int consumed)
    {
        ManagedBrotliDecoder decoder = new();
        using MemoryStream collected = new();
        byte[] buffer = new byte[chunkSize];
        consumed = 0;
        output = null;
        try
        {
            while (true)
            {
                OperationStatus status = decoder.Decompress(stream.Slice(consumed), buffer, out int used, out int written);
                consumed += used;
                collected.Write(buffer, 0, written);
                if (collected.Length > OutputLimit)
                {
                    return Result.TooLarge;
                }

                if (status == OperationStatus.Done)
                {
                    output = collected.ToArray();
                    return Result.Done;
                }

                Assert.Equal(OperationStatus.DestinationTooSmall, status);
                Assert.Equal(chunkSize, written);
            }
        }
        catch (InvalidImageContentException)
        {
            return Result.Unreadable;
        }
    }

    /// <summary>
    /// Checks that <see cref="ManagedBrotliDecoder"/> and <see cref="BrotliDecoder"/> agree on
    /// <paramref name="stream"/>: on whether it can be read, and then on its output and its length.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="chunkSize">The size of the buffer for each call of the managed decoder.</param>
    /// <param name="because">What the stream is, for the message.</param>
    /// <returns>The result of both.</returns>
    public static Result AssertSameAsOracle(ReadOnlySpan<byte> stream, int chunkSize, string because)
    {
        Result expected = Oracle(stream, out byte[] expectedOutput, out int expectedConsumed);
        Result actual = Managed(stream, chunkSize, out byte[] output, out int consumed);

        Assert.True(expected == actual, $"{because}: BrotliDecoder says {expected}, the managed decoder says {actual}");
        if (expected == Result.Done)
        {
            Assert.True(expectedConsumed == consumed, $"{because}: consumed {consumed} bytes, not {expectedConsumed}");
            Assert.True(expectedOutput.AsSpan().SequenceEqual(output), $"{because}: the output differs");
        }

        return expected;
    }

    /// <summary>Makes <paramref name="length"/> bytes of the named kind.</summary>
    /// <param name="kind">"random", "text", "repetitive" or "mixed".</param>
    /// <param name="length">The number of bytes.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Input(string kind, int length, int seed)
    {
        Random random = new(seed);
        byte[] data = new byte[length];
        switch (kind)
        {
            case "random":
                random.NextBytes(data);
                break;
            case "text":
                FillText(data, random);
                break;
            case "repetitive":
                FillRepetitive(data, random);
                break;
            case "mixed":
                // Parts of each kind, and copies of earlier parts from far back.
                for (int pos = 0; pos < length;)
                {
                    Span<byte> part = data.AsSpan(pos, Math.Min(length - pos, 1 + random.Next(40000)));
                    switch (random.Next(5))
                    {
                        case 0:
                            random.NextBytes(part);
                            break;
                        case 1:
                            FillText(part, random);
                            break;
                        case 2:
                            FillRepetitive(part, random);
                            break;
                        case 3:
                            part.Fill((byte)random.Next(256));
                            break;
                        default:
                            if (pos >= part.Length)
                            {
                                data.AsSpan(random.Next(pos - part.Length + 1), part.Length).CopyTo(part);
                            }

                            break;
                    }

                    pos += part.Length;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return data;
    }

    // Words of the vocabulary and of the static dictionary, some with a capital, as the encoder's
    // dictionary search looks for.
    private static void FillText(Span<byte> data, Random random)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            ReadOnlySpan<byte> word;
            if (random.Next(3) == 0)
            {
                word = Encoding.ASCII.GetBytes(Vocabulary[random.Next(Vocabulary.Length)]);
            }
            else
            {
                int wordLength = random.Next(BrotliDictionary.MinWordLength, BrotliDictionary.MaxWordLength + 1);
                int index = random.Next(1 << BrotliDictionary.SizeBitsByLength[wordLength]);
                word = BrotliDictionary.Data.Slice(BrotliDictionary.OffsetsByLength[wordLength] + (index * wordLength), wordLength);
            }

            word = word.Slice(0, Math.Min(word.Length, data.Length - pos));
            word.CopyTo(data.Slice(pos));
            if (random.Next(4) == 0 && data[pos] is >= (byte)'a' and <= (byte)'z')
            {
                data[pos] ^= 32;
            }

            pos += word.Length;
            if (pos < data.Length)
            {
                data[pos++] = (byte)" ,.\n"[random.Next(4)];
            }
        }
    }

    private static void FillRepetitive(Span<byte> data, Random random)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            int period = 1 + random.Next(300);
            int run = Math.Min(data.Length - pos, period + random.Next(5000));
            for (int i = 0; i < run; i++)
            {
                data[pos + i] = i < period ? (byte)random.Next(256) : data[pos + i - period];
            }

            pos += run;
        }
    }
}
