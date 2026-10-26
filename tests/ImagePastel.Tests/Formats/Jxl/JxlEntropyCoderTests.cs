// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;
using PinataLabs.ImagePastel.Formats.Jxl.Modular;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The entropy coder on its own: token streams written by <see cref="JxlEntropyEncoder"/> and read back
/// by <see cref="JxlEntropyDecoder"/> and <see cref="JxlAnsSymbolReader"/>, with the histogram
/// parameters of each speed tier.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEntropyCoderTests
{
    // Rows of 64 equal values, a different value in each row: the token shape that RLE LZ77 is for. With
    // an image width, a copy uses the special distance 1; without one, the plain distance 1.
    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    public void Rle_RowsOfRepeats_AreSmallerWithLz77AndReadBack(int imageWidth)
    {
        List<JxlToken> tokens = RepeatingRows(rows: 40, width: 64);

        byte[] withLz77 = Encode(Params(JxlHistogramParams.Lz77Method.Rle, imageWidth), tokens, out JxlAnsCode code);
        byte[] withoutLz77 = Encode(Params(JxlHistogramParams.Lz77Method.None, imageWidth), tokens, out JxlAnsCode plainCode);

        Assert.True(code.Lz77.Enabled);
        Assert.False(plainCode.Lz77.Enabled);
        Assert.True(withLz77.Length < withoutLz77.Length, $"{withLz77.Length} B with LZ77, {withoutLz77.Length} B without");
        Assert.Equal(tokens.Select(t => t.Value), Decode(withLz77, tokens, imageWidth));
    }

    // RLE pays only when it saves at least 0.2 bits a token: noise has no runs to copy.
    [Fact]
    public void Rle_Noise_LeavesLz77Off()
    {
        System.Random random = new(7);
        List<JxlToken> tokens = Enumerable.Range(0, 4096).Select(_ => new JxlToken(0, (uint)random.Next(256))).ToList();

        byte[] encoded = Encode(Params(JxlHistogramParams.Lz77Method.Rle, 64), tokens, out JxlAnsCode code);

        Assert.False(code.Lz77.Enabled);
        Assert.Equal(tokens.Select(t => t.Value), Decode(encoded, tokens, 64));
    }

    // libjxl picks its LZ77 match finders from Kitten (effort 8) down. They are not ported, so these
    // tiers throw rather than code the streams with RLE.
    [Theory]
    [InlineData(nameof(JxlSpeedTier.Kitten))]
    [InlineData(nameof(JxlSpeedTier.Tortoise))]
    [InlineData(nameof(JxlSpeedTier.Glacier))]
    public void ForModular_KittenAndSlower_ThrowsNotSupportedNamingEffort8(string speedTier)
    {
        System.NotSupportedException ex = Assert.Throws<System.NotSupportedException>(() => JxlHistogramParams.ForModular(System.Enum.Parse<JxlSpeedTier>(speedTier), modularMode: true, JxlPredictor.Best));

        Assert.Contains("libjxl effort 8 and up", ex.Message);
    }

    private static List<JxlToken> RepeatingRows(int rows, int width)
    {
        List<JxlToken> tokens = new();
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < width; x++)
            {
                tokens.Add(new JxlToken(0, (uint)((y * 37) % 50)));
            }
        }

        return tokens;
    }

    // Default's parameters, with the LZ77 method and image width under test.
    private static JxlHistogramParams Params(JxlHistogramParams.Lz77Method lz77, int imageWidth)
    {
        JxlHistogramParams parameters = JxlHistogramParams.ForModular(JxlSpeedTier.Hare, modularMode: true, JxlPredictor.Best);
        parameters.Lz77 = lz77;
        parameters.ImageWidths = new[] { imageWidth };
        return parameters;
    }

    // Writes one stream of tokens in one context, then reads its codes back.
    private static byte[] Encode(JxlHistogramParams parameters, List<JxlToken> tokens, out JxlAnsCode code)
    {
        List<JxlToken>[] streams = { new(tokens) };
        JxlEntropyEncodingData codes = new();
        JxlBitWriter writer = new();
        JxlEntropyEncoder.BuildAndEncodeHistograms(parameters, 1, streams, codes, writer);
        JxlEntropyEncoder.WriteTokens(streams[0], codes, 0, writer);
        writer.ZeroPadToByte();
        byte[] encoded = writer.ToArray();

        code = new JxlAnsCode();
        JxlBitReader reader = new(encoded, 0, encoded.Length);
        JxlEntropyDecoder.DecodeHistograms(reader, 1, code);
        reader.Close();
        return encoded;
    }

    private static List<uint> Decode(byte[] encoded, List<JxlToken> tokens, int imageWidth)
    {
        JxlAnsCode code = new();
        JxlBitReader reader = new(encoded, 0, encoded.Length);
        byte[] contextMap = JxlEntropyDecoder.DecodeHistograms(reader, 1, code);
        JxlAnsSymbolReader symbols = new(code, reader, imageWidth);
        List<uint> values = tokens.Select(t => symbols.ReadHybridUint((int)t.Context, reader, contextMap)).ToList();
        Assert.True(symbols.CheckAnsFinalState);
        reader.Close();
        return values;
    }
}
