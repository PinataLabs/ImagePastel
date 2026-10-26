// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl.Base;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Checks the render pipeline's HLG OOTF to scene light, libjxl's <c>HlgOOTF::ToSceneLight</c> and
/// <c>Apply</c>, against the values that libjxl's own code gives when it is built without FMA (Highway's
/// scalar target, <c>-ffp-contract=off</c>; ADR 0004).
/// </summary>
[Trait("Format", "Jxl")]
public class JxlHlgOotfTests
{
    private static readonly float[] SrgbLuminances = { 0.2126F, 0.7152F, 0.0722F };

    private static readonly float[] Bt2100Luminances = { 0.2627F, 0.678F, 0.0593F };

    // libjxl's ToSceneLight for display peaks below, at and above HLG's reference 1000 nits, with the
    // sRGB and the BT.2100 luminances, on a colour, on white, on samples near 0, and on samples with a
    // negative channel. Measured: exact.
    [Theory]
    [InlineData(false, 1000F, 0.5F, 0.25F, 0.1F, 0.613752902F, 0.306876451F, 0.12275058F)]
    [InlineData(false, 1000F, 1F, 1F, 1F, 1.00000012F, 1.00000012F, 1.00000012F)]
    [InlineData(false, 1000F, 1e-4F, 2e-4F, 3e-4F, 0.000418565556F, 0.000837131112F, 0.00125569676F)]
    [InlineData(false, 1000F, -0.1F, 0.2F, 0.05F, -0.141347989F, 0.282695979F, 0.0706739947F)]
    [InlineData(false, 1000F, 1.2F, 0.9F, -0.05F, 1.22234917F, 0.916761875F, -0.0509312153F)]
    [InlineData(false, 4000F, 0.5F, 0.25F, 0.1F, 0.745579123F, 0.372789562F, 0.149115831F)]
    [InlineData(false, 400F, -0.1F, 0.2F, 0.05F, -0.109170161F, 0.218340322F, 0.0545850806F)]
    [InlineData(false, 255F, 0.5F, 0.25F, 0.1F, 0.484554827F, 0.242277414F, 0.0969109684F)]
    [InlineData(false, 10000F, 1e-4F, 2e-4F, 3e-4F, 0.00346023845F, 0.00692047691F, 0.0103807161F)]
    [InlineData(true, 1000F, 0.5F, 0.25F, 0.1F, 0.60883379F, 0.304416895F, 0.121766761F)]
    [InlineData(true, 1000F, 1.2F, 0.9F, -0.05F, 1.21624792F, 0.912185907F, -0.0506769978F)]
    [InlineData(true, 4000F, 1e-4F, 2e-4F, 3e-4F, 0.00164739392F, 0.00329478784F, 0.00494218199F)]
    [InlineData(true, 255F, -0.1F, 0.2F, 0.05F, -0.0945742056F, 0.189148411F, 0.0472871028F)]
    [InlineData(true, 10000F, 0.5F, 0.25F, 0.1F, 0.814116955F, 0.407058477F, 0.162823394F)]
    public void ToSceneLight_Apply_GivesLibjxlsValue(bool bt2100, float displayLuminance, float r, float g, float b, float expectedR, float expectedG, float expectedB)
    {
        JxlHlgOotf ootf = JxlHlgOotf.ToSceneLight(displayLuminance, bt2100 ? Bt2100Luminances : SrgbLuminances);

        ootf.Apply(ref r, ref g, ref b);

        Assert.Equal(expectedR, r);
        Assert.Equal(expectedG, g);
        Assert.Equal(expectedB, b);
    }

    // Black stays black: libjxl's fast power of 0 is finite, so the ratio times 0 is 0.
    [Theory]
    [InlineData(1000F)]
    [InlineData(255F)]
    public void ToSceneLight_Apply_OfBlack_IsBlack(float displayLuminance)
    {
        float r = 0, g = 0, b = 0;

        JxlHlgOotf.ToSceneLight(displayLuminance, SrgbLuminances).Apply(ref r, ref g, ref b);

        Assert.Equal((0F, 0F, 0F), (r, g, b));
    }

    // At about 300 nits the OOTF's exponent is within 0.01 of 0 (libjxl gives 5.1e-4), so libjxl
    // skips it and the samples are unchanged.
    [Fact]
    public void ToSceneLight_NearThreeHundredNits_ChangesNothing()
    {
        float r = 0.5F, g = 0.25F, b = 0.1F;

        JxlHlgOotf.ToSceneLight(300F, Bt2100Luminances).Apply(ref r, ref g, ref b);

        Assert.Equal((0.5F, 0.25F, 0.1F), (r, g, b));
    }
}
