// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Checks the noise generator against the words libjxl's <c>Xorshift128Plus</c> gives for the same seeds.
/// The expected values come from a harness that compiles libjxl's <c>xorshift128plus-inl.h</c> at
/// <c>ef67fde2</c> (scalar Highway target) and prints its first fills.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlXorshift128PlusTests
{
    [Fact]
    public void Fill_FirstVisibleFrameAtTheOrigin_GivesLibjxlsWords()
    {
        // The seeds of the first group of the first shown frame: visible 1, non-visible 0, at (0, 0).
        JxlXorshift128Plus rng = new(1, 0, 0, 0);
        ulong[] batch = new ulong[JxlXorshift128Plus.N];

        rng.Fill(batch);
        Assert.Equal(
            new[] { 0xA64D02541E9FCEE7UL, 0x4BA318DCB005EE44UL, 0x0F02F8F4760B8AA9UL, 0xF729890FF47E6053UL, 0x0CDFD3B5006258BAUL, 0xA399FC591E883FB8UL, 0x21FD51C8329CAFB6UL, 0x25185632D2A757CBUL },
            batch);

        rng.Fill(batch);
        Assert.Equal(
            new[] { 0x0E6CAC77649F7AC9UL, 0x5B5AEC7FC53F4BCCUL, 0x9AD9B0878C8E4E9AUL, 0x2CF77F538268B957UL, 0x9AA5F0572204708DUL, 0xCF56548D2BC2A527UL, 0x7E7571630C212BCFUL, 0x16FE575B245CA415UL },
            batch);
    }

    [Fact]
    public void Fill_AllFourSeeds_GivesLibjxlsWords()
    {
        JxlXorshift128Plus rng = new(2, 3, 256, 512);
        ulong[] batch = new ulong[JxlXorshift128Plus.N];

        rng.Fill(batch);
        Assert.Equal(
            new[] { 0x6DB1E8855F4BA7D8UL, 0xC3CFDA0CFAC98346UL, 0xDEC5AFFB3551EBA3UL, 0x3C0F68B26EF78690UL, 0x73F5B6878B2F4058UL, 0x49DA63CBC71C4E14UL, 0x83BDF87804077223UL, 0x8DDB1540F40BF4EFUL },
            batch);

        rng.Fill(batch);
        Assert.Equal(
            new[] { 0x66DB5EA7EBD78C42UL, 0xB951C58F7A542CAFUL, 0x94A540B920EED12AUL, 0xA3A8AC1F7432403BUL, 0x6913ECF1AB2B7C31UL, 0x1206BCA04C70A8CBUL, 0xCE09149C0094D765UL, 0x0D27139410DC3D1BUL },
            batch);
    }

    // libjxl's BitsToFloat of the first four 32-bit words of the third fill: the low word of each 64-bit
    // word comes first, as libjxl copies the words on a little-endian machine.
    [Theory]
    [InlineData(0x7A28665Du, 1.47717893f)]
    [InlineData(0x39D224E6u, 1.22586274f)]
    [InlineData(0xCC80E191u, 1.79884148f)]
    [InlineData(0xFD7A4D11u, 1.99014735f)]
    public void BitsToFloat_GivesLibjxlsFloat(uint bits, float expected)
        => Assert.Equal(expected, JxlNoise.BitsToFloat(bits));

    [Fact]
    public void Fill_ThirdFill_SplitsIntoLibjxlsWords()
    {
        JxlXorshift128Plus rng = new(2, 3, 256, 512);
        ulong[] batch = new ulong[JxlXorshift128Plus.N];
        rng.Fill(batch);
        rng.Fill(batch);
        rng.Fill(batch);

        Assert.Equal(0x7A28665Du, (uint)batch[0]);
        Assert.Equal(0x39D224E6u, (uint)(batch[0] >> 32));
        Assert.Equal(0xCC80E191u, (uint)batch[1]);
        Assert.Equal(0xFD7A4D11u, (uint)(batch[1] >> 32));
    }
}
