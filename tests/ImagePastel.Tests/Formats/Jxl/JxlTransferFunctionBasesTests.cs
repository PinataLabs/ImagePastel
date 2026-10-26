// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Checks the scalar double-precision transfer functions that the ICC builder uses against the values
/// libjxl's <c>TF_PQ_Base</c> and <c>TF_HLG_Base</c> give, built without FMA.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlTransferFunctionBasesTests
{
    // libjxl rounds both results to float (copysignf); allow one float step for a CRT pow or exp that
    // differs by one ulp.
    private const double FloatStep = 1.0 / (1 << 23);

    // Measured: 0 (every value equal to libjxl's).
    [Theory]
    [InlineData(10000F, 0.0, 0.0)]
    [InlineData(10000F, 1e-3, 4.2013073020541469e-09)]
    [InlineData(10000F, 0.1, 3.2456558983540162e-05)]
    [InlineData(10000F, 0.25, 0.00051541760331019759)]
    [InlineData(10000F, 0.5, 0.0092245712876319885)]
    [InlineData(10000F, 0.75, 0.09833778440952301)]
    [InlineData(10000F, 0.9, 0.39056447148323059)]
    [InlineData(10000F, 1.0, 1.0)]
    [InlineData(10000F, 1.25, 12.866804122924805)]
    [InlineData(10000F, -0.6, -0.024400519207119942)]
    [InlineData(1000F, 0.1, 0.00032456559711135924)]
    [InlineData(1000F, 0.5, 0.092245705425739288)]
    [InlineData(1000F, 1.0, 10.0)]
    [InlineData(1000F, -0.6, -0.24400518834590912)]
    public void PqDisplayFromEncoded_GivesLibjxlsValue(float intensityTarget, double encoded, double expected)
        => AssertWithinOneFloatStep(expected, JxlTfPqBase.DisplayFromEncoded(intensityTarget, encoded));

    // The inverse, which the Rec. 2408 tone mapper uses with a target of 1 nit. Measured: 0 (every value
    // equal to libjxl's).
    [Theory]
    [InlineData(1F, 0.0, 0.0)]
    [InlineData(1F, 1e-6, 0.00011948233441216871)]
    [InlineData(1F, 1e-4, 0.0016671882476657629)]
    [InlineData(1F, 0.01, 0.021486213430762291)]
    [InlineData(1F, 0.025, 0.033541299402713776)]
    [InlineData(1F, 0.1, 0.062336865812540054)]
    [InlineData(1F, 0.5, 0.11745962500572205)]
    [InlineData(1F, 1.0, 0.14994573593139648)]
    [InlineData(1F, -0.01, -0.021486213430762291)]
    [InlineData(10000F, 1e-6, 0.021486213430762291)]
    [InlineData(10000F, 0.01, 0.50807839632034302)]
    [InlineData(10000F, 0.1, 0.75182712078094482)]
    [InlineData(10000F, 0.5, 0.92654669284820557)]
    [InlineData(10000F, 1.0, 1.0)]
    [InlineData(10000F, -0.01, -0.50807839632034302)]
    public void PqEncodedFromDisplay_GivesLibjxlsValue(float intensityTarget, double display, double expected)
        => AssertWithinOneFloatStep(expected, JxlTfPqBase.EncodedFromDisplay(intensityTarget, display));

    // Measured: 0 (every value equal to libjxl's).
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1e-3, 3.3333333249174757e-07)]
    [InlineData(0.1, 0.0033333334140479565)]
    [InlineData(0.25, 0.02083333395421505)]
    [InlineData(0.5, 0.083333335816860199)]
    [InlineData(0.6, 0.12799625098705292)]
    [InlineData(0.75, 0.26496255397796631)]
    [InlineData(0.9, 0.58183759450912476)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.25, 3.9746317863464355)]
    [InlineData(-0.6, -0.12799625098705292)]
    public void HlgDisplayFromEncoded_GivesLibjxlsValue(double encoded, double expected)
        => AssertWithinOneFloatStep(expected, JxlTfHlgBase.DisplayFromEncoded(encoded));

    [Fact]
    public void DisplayFromEncoded_RoundsToFloat()
    {
        // libjxl returns the double through copysignf, so every result is a float.
        double pq = JxlTfPqBase.DisplayFromEncoded(10000F, 0.3);
        double hlg = JxlTfHlgBase.DisplayFromEncoded(0.7);

        Assert.Equal((double)(float)pq, pq);
        Assert.Equal((double)(float)hlg, hlg);
    }

    private static void AssertWithinOneFloatStep(double expected, double actual)
    {
        double difference = Math.Abs(expected - actual);
        TestContext.Current.TestOutputHelper?.WriteLine($"Expected {expected:R}, got {actual:R}, difference {difference:G4}");
        Assert.True(difference <= Math.Abs(expected) * FloatStep, $"Expected {expected:R}, got {actual:R}.");
    }
}
