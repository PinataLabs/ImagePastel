// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;
using System.Text;
using PinataLabs.ImagePastel.Compression.Brotli;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Compression.Brotli;

/// <summary>
/// Checks the tables of the managed Brotli decoder against RFC 7932 and google/brotli v1.2.0.
/// </summary>
public class BrotliTablesTests
{
    [Fact]
    public void Dictionary_IsTheDataOfDictionaryBin()
    {
        // sha256sum c/common/dictionary.bin, at google/brotli v1.2.0.
        const string expected = "20e42eb1b511c21806d4d227d07e5dd06877d8ce7b3a817f378f313653f35c70";

        Assert.Equal(122784, BrotliDictionary.Data.Length);
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(BrotliDictionary.Data)).ToLowerInvariant());
    }

    [Fact]
    public void Dictionary_OffsetsFollowTheWordCounts()
    {
        for (int length = 0; length < 31; length++)
        {
            int bits = BrotliDictionary.SizeBitsByLength[length];
            int size = bits == 0 ? 0 : length << bits;

            Assert.Equal(BrotliDictionary.OffsetsByLength[length] + size, BrotliDictionary.OffsetsByLength[length + 1]);
            Assert.Equal(bits != 0, length is >= BrotliDictionary.MinWordLength and <= BrotliDictionary.MaxWordLength);
        }

        Assert.Equal(BrotliDictionary.Data.Length, BrotliDictionary.OffsetsByLength[31]);
    }

    // Rows of kCmdLut in c/dec/prefix_inc.h.
    [Theory]
    [InlineData(0, 0x00, 0x00, 0, 0x00, 0x0000, 0x0002)]
    [InlineData(1, 0x00, 0x00, 0, 0x01, 0x0000, 0x0003)]
    [InlineData(7, 0x00, 0x00, 0, 0x03, 0x0000, 0x0009)]
    [InlineData(8, 0x00, 0x00, 0, 0x00, 0x0001, 0x0002)]
    [InlineData(27, 0x00, 0x00, 0, 0x03, 0x0003, 0x0005)]
    [InlineData(700, 0x18, 0x08, -1, 0x03, 0x5842, 0x0146)]
    [InlineData(703, 0x18, 0x18, -1, 0x03, 0x5842, 0x0846)]
    public void Commands_AreTheRowsOfTheCommandTable(int symbol, int insertBits, int copyBits, int distanceCode, int context, int insertOffset, int copyOffset)
    {
        BrotliCommand command = BrotliPrefix.Commands[symbol];

        Assert.Equal(insertBits, command.InsertLenExtraBits);
        Assert.Equal(copyBits, command.CopyLenExtraBits);
        Assert.Equal(distanceCode, command.DistanceCode);
        Assert.Equal(context, command.Context);
        Assert.Equal(insertOffset, command.InsertLenOffset);
        Assert.Equal(copyOffset, command.CopyLenOffset);
    }

    [Fact]
    public void Commands_CoverEveryInsertAndCopyLengthRangeOnce()
    {
        Assert.Equal(BrotliPrefix.NumCommandSymbols, BrotliPrefix.Commands.Length);

        // RFC 7932, section 5: 24 insert length codes and 24 copy length codes; 704 = 11 cells of 64 pairs.
        // The first two cells (implicit distance) and the cells after them (explicit) each hold a pair once.
        System.Collections.Generic.HashSet<(int Insert, int Copy, int Distance)> pairs = new();
        foreach (BrotliCommand command in BrotliPrefix.Commands)
        {
            Assert.True(pairs.Add((command.InsertLenOffset, command.CopyLenOffset, command.DistanceCode)));
        }
    }

    [Fact]
    public void BlockLengths_RangesAreContiguous()
    {
        for (int code = 0; code + 1 < BrotliPrefix.NumBlockLengthSymbols; code++)
        {
            Assert.Equal(BrotliPrefix.BlockLengthOffset[code] + (1 << BrotliPrefix.BlockLengthBits[code]), BrotliPrefix.BlockLengthOffset[code + 1]);
        }
    }

    // RFC 7932, section 7.1: the context ID of each mode.
    [Theory]
    [InlineData(0, 0xC7, 0x55, 0x07)] // LSB6: p1 & 0x3f
    [InlineData(1, 0xC7, 0x55, 0x31)] // MSB6: p1 >> 2
    [InlineData(2, (int)'b', (int)' ', 60)] // UTF8: a lowercase consonant after a space
    [InlineData(2, (int)'B', (int)'b', 55)] // UTF8: an uppercase consonant after a lowercase letter
    [InlineData(3, 0xFF, 0x00, 56)] // Signed: (lut2[p1] << 3) | lut2[p2]
    public void Context_GivesTheContextIdOfTheMode(int mode, int p1, int p2, int expected)
    {
        ReadOnlySpan<byte> lookup = BrotliContext.Lookup.Slice(mode << 9, 512);

        Assert.Equal(2048, BrotliContext.Lookup.Length);
        Assert.Equal(expected, lookup[p1] | lookup[256 + p2]);
    }

    // RFC 7932, appendix B.
    [Theory]
    [InlineData(0, "time", "time")]
    [InlineData(1, "time", "time ")]
    [InlineData(2, "time", " time ")]
    [InlineData(3, "time", "ime")]
    [InlineData(4, "time", "Time ")]
    [InlineData(5, "time", "time the ")]
    [InlineData(9, "time", "Time")]
    [InlineData(12, "time", "tim")]
    [InlineData(44, "time", "TIME")]
    [InlineData(49, "time", "timing ")]
    [InlineData(54, "time", "")]
    [InlineData(64, "time", "")]
    [InlineData(73, "time", " the time of the ")]
    [InlineData(120, "time", " Time='")]
    [InlineData(44, "été", "ÉTÉ")]
    public void Transform_GivesTheWordOfTheRfc(int transform, string word, string expected)
    {
        byte[] destination = new byte[64];

        int length = BrotliTransform.TransformDictionaryWord(destination, Encoding.UTF8.GetBytes(word), transform);

        Assert.Equal(expected, Encoding.UTF8.GetString(destination, 0, length));
    }
}
