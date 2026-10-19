// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The entropy choices libjxl makes at e5 and e7 for Modular frames: RLE LZ77 and the fast hybrid uint
/// search. Each test reads the choice back through the decoder's parse; the round trips of these
/// images are cases of <see cref="JxlEncoderRoundTrip"/>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderLz77Tests
{
    // cjxl 0.12 -e 5 -d 0 turns LZ77 on for the ramp stripes too.
    [Theory]
    [InlineData("ramp-stripes")]
    [InlineData("noisy-stripes")]
    public void Encode_DefaultRepeatingRows_TurnsLz77On(string pattern)
    {
        JxlEntropyCodeProbe.ReadGlobalCodes(Encode(pattern, 128, 128, JxlEffort.Default), out _, out JxlAnsCode imageCode);

        Assert.True(imageCode.Lz77.Enabled);
    }

    private static byte[] Encode(string pattern, int width, int height, JxlEffort effort)
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>(pattern, width, height);
        return JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.Lossless(effort));
    }
}
