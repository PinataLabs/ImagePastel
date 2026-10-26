// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Jxl.Base;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Checks the scalar tone mapping that the ICC builder uses for the 3D-LUT profiles of PQ and HLG images
/// (libjxl's <c>Rec2408ToneMapperBase</c>, <c>HlgOOTF_Base</c> and <c>GamutMapScalar</c>) against the
/// values libjxl's own code gives when it is built without FMA (<c>-ffp-contract=off</c>; ADR 0004).
/// </summary>
[Trait("Format", "Jxl")]
public class JxlToneMappingTests
{
    private static readonly float[] Bt2100Luminances = { 0.2627F, 0.678F, 0.0593F };

    private static readonly float[] SrgbLuminances = { 0.2126F, 0.7152F, 0.0722F };

    // The PQ tone mapping of ToneMapPixel: from 0-10,000 nits to 0-250 nits, on samples at 0 and 1, a
    // colour, samples near 0 and below libjxl's 1e-6 cap, a colour out of gamut, and pure green. The
    // samples are linear, 1 = 10,000 nits. Measured: exact.
    [Theory]
    [InlineData(true, 0F, 0F, 0F, 0F, 0F, 0F)]
    [InlineData(true, 1F, 1F, 1F, 1F, 1F, 1F)]
    [InlineData(true, 0.5F, 0.25F, 0.1F, 1.6010294F, 0.800514698F, 0.320205897F)]
    [InlineData(true, 0.01F, 0.02F, 0.005F, 0.278976411F, 0.557952821F, 0.139488205F)]
    [InlineData(true, 1e-4F, 2e-4F, 3e-4F, 0.00400000019F, 0.00800000038F, 0.012000002F)]
    [InlineData(true, 1e-8F, 1e-8F, 1e-8F, 3.99999948e-07F, 3.99999948e-07F, 3.99999948e-07F)]
    [InlineData(true, 1.2F, -0.1F, 0.3F, 4.41089249F, -0.367574364F, 1.10272312F)]
    [InlineData(true, 0F, 1F, 0F, 0F, 1.47402406F, 0F)]
    [InlineData(true, 0.025F, 0.025F, 0.025F, 0.56804651F, 0.56804651F, 0.56804651F)]
    [InlineData(false, 1F, 1F, 1F, 0.99999994F, 0.99999994F, 0.99999994F)]
    [InlineData(false, 0.5F, 0.25F, 0.1F, 1.6763438F, 0.838171899F, 0.335268766F)]
    [InlineData(false, 1e-4F, 2e-4F, 3e-4F, 0.00399999972F, 0.00799999945F, 0.0119999992F)]
    [InlineData(false, -0.05F, 0.3F, 0.9F, -0.181416288F, 1.08849776F, 3.26549315F)]
    public void Rec2408ToneMap_GivesLibjxlsValue(bool bt2100, float r, float g, float b, float expectedR, float expectedG, float expectedB)
    {
        JxlRec2408ToneMapperBase toneMapper = new((0F, 10000F), (0F, 250F), bt2100 ? Bt2100Luminances : SrgbLuminances);
        float[] rgb = { r, g, b };

        toneMapper.ToneMap(rgb);

        Assert.Equal(new[] { expectedR, expectedG, expectedB }, rgb);
    }

    // The HLG OOTF of ToneMapPixel, from 300 nits to 80, and one from 1000 nits to 4000, on samples at 0
    // and 1, a colour, samples near 0, a colour out of gamut and pure green. Measured: exact.
    [Theory]
    [InlineData(true, 300F, 80F, 0F, 0F, 0F, 0F, 0F, 0F)]
    [InlineData(true, 300F, 80F, 1F, 1F, 1F, 1F, 1F, 1F)]
    [InlineData(true, 300F, 80F, 0.5F, 0.25F, 0.1F, 0.619861901F, 0.30993095F, 0.123972379F)]
    [InlineData(true, 300F, 80F, 0.01F, 0.02F, 0.005F, 0.0210981797F, 0.0421963595F, 0.0105490899F)]
    [InlineData(true, 300F, 80F, 1e-8F, 1e-8F, 1e-8F, 2.85016029e-07F, 2.85016029e-07F, 2.85016029e-07F)]
    [InlineData(true, 300F, 80F, 1.2F, -0.1F, 0.3F, 1.52756774F, -0.127297312F, 0.381891936F)]
    [InlineData(true, 300F, 80F, 0F, 1F, 0F, 0F, 1.07322884F, 0F)]
    [InlineData(true, 300F, 80F, -0.05F, 0.3F, 0.9F, -0.0646393076F, 0.38783586F, 1.16350746F)]
    [InlineData(true, 1000F, 4000F, 0.5F, 0.25F, 0.1F, 0.379072994F, 0.189536497F, 0.0758145973F)]
    [InlineData(true, 1000F, 4000F, 1e-4F, 2e-4F, 3e-4F, 1.32537798e-05F, 2.65075596e-05F, 3.97613403e-05F)]
    [InlineData(false, 300F, 80F, 0.5F, 0.25F, 0.1F, 0.625328541F, 0.31266427F, 0.125065714F)]
    [InlineData(false, 300F, 80F, 1e-4F, 2e-4F, 3e-4F, 0.000476912101F, 0.000953824201F, 0.00143073639F)]
    [InlineData(false, 300F, 80F, 1.2F, -0.1F, 0.3F, 1.60045946F, -0.133371621F, 0.400114864F)]
    [InlineData(false, 1000F, 4000F, -0.05F, 0.3F, 0.9F, -0.0367548801F, 0.220529288F, 0.661587834F)]
    public void HlgOotfBaseApply_GivesLibjxlsValue(bool bt2100, float sourceLuminance, float targetLuminance, float r, float g, float b, float expectedR, float expectedG, float expectedB)
    {
        JxlHlgOotfBase ootf = new(sourceLuminance, targetLuminance, bt2100 ? Bt2100Luminances : SrgbLuminances);
        float[] rgb = { r, g, b };

        ootf.Apply(rgb);

        Assert.Equal(new[] { expectedR, expectedG, expectedB }, rgb);
    }

    // From 300 nits to 301 the exponent is within 0.01 of 0, so libjxl skips the OOTF.
    [Fact]
    public void HlgOotfBaseApply_BetweenNearlyEqualLuminances_ChangesNothing()
    {
        float[] rgb = { 1.2F, -0.1F, 0.3F };

        new JxlHlgOotfBase(300F, 301F, Bt2100Luminances).Apply(rgb);

        Assert.Equal(new[] { 1.2F, -0.1F, 0.3F }, rgb);
    }

    // The gamut map of ToneMapPixel, which preserves saturation 0.3: samples at 0 and 1 and colours in
    // gamut are unchanged; a colour with a negative channel is mixed with grey, and one above 1 is
    // normalized. Measured: exact.
    [Theory]
    [InlineData(true, 0F, 0F, 0F, 0F, 0F, 0F)]
    [InlineData(true, 1F, 1F, 1F, 1F, 1F, 1F)]
    [InlineData(true, 0.5F, 0.25F, 0.1F, 0.5F, 0.25F, 0.1F)]
    [InlineData(true, 0F, 1F, 0F, 0F, 1F, 0F)]
    [InlineData(true, 1.2F, -0.1F, 0.3F, 0.944059908F, 7.4505806e-09F, 0.290479988F)]
    [InlineData(true, -0.05F, 0.3F, 0.9F, 0F, 0.290402204F, 0.788234532F)]
    [InlineData(true, 1.5F, 1.2F, 0.2F, 1F, 1F, 1F)]
    [InlineData(true, -0.2F, -0.1F, 0.5F, -0.0906900018F, -0.0906900018F, -0.0906900167F)]
    [InlineData(true, 2F, 0.5F, -0.3F, 1F, 0.609826922F, 0.40173465F)]
    [InlineData(false, 1.2F, -0.1F, 0.3F, 0.874133587F, 0F, 0.268964171F)]
    [InlineData(false, -0.05F, 0.3F, 0.9F, 3.7252903e-09F, 0.295125604F, 0.801055133F)]
    public void GamutMapScalar_GivesLibjxlsValue(bool bt2100, float r, float g, float b, float expectedR, float expectedG, float expectedB)
    {
        float[] rgb = { r, g, b };

        JxlGamutMap.GamutMapScalar(rgb, bt2100 ? Bt2100Luminances : SrgbLuminances, 0.3F);

        Assert.Equal(new[] { expectedR, expectedG, expectedB }, rgb);
    }
}
