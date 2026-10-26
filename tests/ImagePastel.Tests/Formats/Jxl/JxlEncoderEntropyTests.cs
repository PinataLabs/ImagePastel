// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The entropy choices libjxl makes at e5 and e7 for Modular frames: RLE LZ77 and the fast hybrid uint
/// search. Each test reads the choice back through the decoder's parse; the round trips of these
/// images are cases of <see cref="JxlEncoderRoundTrip"/>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderEntropyTests
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

    // RLE pays only when the copies save 0.2 bits a token. A single colour costs almost nothing a token
    // without LZ77, and noise has no runs, so both keep LZ77 off, as cjxl 0.12 -e 5 -d 0 does.
    [Theory]
    [InlineData("solid")]
    [InlineData("noise")]
    public void Encode_DefaultSolidOrNoise_LeavesLz77Off(string pattern)
    {
        JxlEntropyCodeProbe.ReadGlobalCodes(Encode(pattern, 256, 256, JxlEffort.Default), out _, out JxlAnsCode imageCode);

        Assert.False(imageCode.Lz77.Enabled);
    }

    // libjxl's fast search tries (4, 2, 0), (4, 1, 2), (0, 0, 0) and (2, 0, 1) for each histogram; on
    // noise, cjxl 0.12 -e 5 picks the last three too.
    [Fact]
    public void Encode_DefaultNoise_SearchesTheHybridUintConfigs()
    {
        JxlEntropyCodeProbe.ReadGlobalCodes(Encode("noise", 256, 256, JxlEffort.Default), out _, out JxlAnsCode imageCode);

        Assert.Contains(imageCode.UintConfig, c => (c.SplitExponent, c.MsbInToken, c.LsbInToken) != (4u, 2u, 0u));
    }

    [Fact]
    public void Encode_FastestNoise_KeepsTheDefaultHybridUintConfig()
    {
        JxlEntropyCodeProbe.ReadGlobalCodes(Encode("noise", 256, 256, JxlEffort.Fastest), out _, out JxlAnsCode imageCode);

        Assert.All(imageCode.UintConfig, c => Assert.Equal((4u, 2u, 0u), (c.SplitExponent, c.MsbInToken, c.LsbInToken)));
        Assert.False(imageCode.Lz77.Enabled);
    }

    private static byte[] Encode(string pattern, int width, int height, JxlEffort effort)
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>(pattern, width, height);
        return JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.Lossless(effort));
    }
}
