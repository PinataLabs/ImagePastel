// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The encoder's choice between prefix codes and ANS, as libjxl makes it at e3: prefix codes for a
/// stream of fewer than 100 tokens, or when every context has a single symbol. Each test reads the
/// choice back through the decoder's parse.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderPrefixCodeTests
{
    [Theory]
    [InlineData(nameof(L8), "gradient", 1, 1)]
    [InlineData(nameof(Rgb24), "noise", 1, 1)]
    [InlineData(nameof(L8), "noise", 4, 4)]
    [InlineData(nameof(Rgb24), "gradient", 4, 4)]
    public void Encode_TinyImage_WritesTreeAndImageWithPrefixCodes(string pixelType, string pattern, int width, int height)
    {
        JxlEntropyCodeProbe.ReadGlobalCodes(Encode(pixelType, pattern, width, height), out JxlAnsCode treeCode, out JxlAnsCode imageCode);

        Assert.True(treeCode.UsePrefixCode);
        Assert.True(imageCode.UsePrefixCode);
    }

    // cjxl 0.12 -e 3 also codes both streams of this image with ANS: kWPFixedDC has its full depth
    // here, so the tree has more than 100 tokens too.
    [Fact]
    public void Encode_LargeNoisyImage_KeepsTreeAndImageAns()
    {
        JxlEntropyCodeProbe.ReadGlobalCodes(Encode(nameof(Rgb24), "noise", 256, 256), out JxlAnsCode treeCode, out JxlAnsCode imageCode);

        Assert.False(treeCode.UsePrefixCode);
        Assert.False(imageCode.UsePrefixCode);
    }

    // A grey image has one token per pixel: 10 x 9 is 90 tokens and 10 x 10 is 100.
    [Theory]
    [InlineData(10, 9, true)]
    [InlineData(10, 10, false)]
    public void Encode_NoisyGrey_UsesPrefixCodesBelowOneHundredTokens(int width, int height, bool expectPrefix)
    {
        JxlEntropyCodeProbe.ReadGlobalCodes(Encode(nameof(L8), "noise", width, height), out _, out JxlAnsCode imageCode);

        Assert.Equal(expectPrefix, imageCode.UsePrefixCode);
    }

    // Not every context of a single-colour image holds one symbol (the first pixels are predicted
    // wrongly), so the all-singleton rule does not fire and it stays ANS, as cjxl 0.12 -e 3 does.
    [Theory]
    [InlineData(nameof(L8))]
    [InlineData(nameof(Rgb24))]
    public void Encode_SingleColourLargeImage_StaysAns(string pixelType)
    {
        JxlEntropyCodeProbe.ReadGlobalCodes(Encode(pixelType, "solid", 256, 256), out _, out JxlAnsCode imageCode);

        Assert.False(imageCode.UsePrefixCode);
    }

    private static byte[] Encode(string pixelType, string pattern, int width, int height)
        => JxlPixelTypes.Call(pixelType, new EncodePattern(pattern, width, height));

    private sealed record EncodePattern(string Pattern, int Width, int Height) : IJxlPixelTypeCall<byte[]>
    {
        public byte[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = JxlEncoderRoundTrip.CreateImage<TPixel>(this.Pattern, this.Width, this.Height);
            return JxlEncoderRoundTrip.Encode(image);
        }
    }
}
