// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The effort levels: each maps to a libjxl speed tier, and the encoder's choices at each tier are read
/// back through the decoder's parse.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderEffortTests
{
    // SHA-256 of the Fastest output that E1 (feat/jxl-e2 at a00deefef) wrote for each case. Fastest must
    // not change while the higher efforts are added.
    [Theory]
    [InlineData(nameof(L8), "gradient", 17, 23, "DD7C8882D0F8F4A0D8791A495C8C360DD353A04C0C585711F52933C32C419944")]
    [InlineData(nameof(Rgb24), "noise", 256, 256, "166916BFB855961AC5530FF8CE9BC07C948F587E5B0F2EEEF2665017893660D1")]
    [InlineData(nameof(Rgb24), "icon", 64, 64, "5D115BCD280CB9C23101D025F6E4897259817A00EA2AF7A8EE7742EB4D827CB1")]
    [InlineData(nameof(Rgb24), "gradient", 300, 513, "D5A027E86DD98D5E20CD472EA8109EA2D28B1430224200A3896677FF07B2C4E5")]
    [InlineData(nameof(L16), "noise", 17, 23, "73F19946E7D91980952459FCE73EB44C9B8C507427CC25DE4EBF02BB2B08179E")]
    [InlineData(nameof(Rgba64), "translucent", 256, 256, "A43FF2217BE77CEFD00ED786CB89FEFA8E901D7BC438ACE952EB290EE05F3C2F")]
    [InlineData(nameof(RgbaVector), "translucent", 17, 23, "0F95453F6378A0851578D73E97D94F1434ED86CFB74484BEEDFBF36C86FE7044")]
    [InlineData(nameof(La16), "translucent-icon", 64, 64, "8E427C5E09A89E9F26AE6782AAB86A5BA23B1AA59C408FB04FB691F51F47753C")]
    public void Encode_Fastest_IsByteIdenticalToE1(string pixelType, string pattern, int width, int height, string expectedSha256)
    {
        byte[] encoded = JxlPixelTypes.Call(pixelType, new EncodePattern(pattern, width, height, JxlEncoderRoundTrip.LosslessFastest));

        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(encoded)));
    }

    [Fact]
    public void Encode_FastestPhoto_IsByteIdenticalToE1()
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.LosslessFastest);

        Assert.Equal("CAC3BA22594C71A7E1E139DCD945BEC08190014E6B113D23D6F39DD288187A26", Convert.ToHexString(SHA256.HashData(encoded)));
    }

    private sealed record EncodePattern(string Pattern, int Width, int Height, JxlEncoder Encoder) : IJxlPixelTypeCall<byte[]>
    {
        public byte[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = JxlEncoderRoundTrip.CreateImage<TPixel>(this.Pattern, this.Width, this.Height);
            return JxlEncoderRoundTrip.Encode(image, this.Encoder);
        }
    }
}
