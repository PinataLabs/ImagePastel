// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Quality;

[Trait("Format", "Jxl")]
public class Ssimulacra2Tests
{
    [Fact]
    public void IdenticalImages_Score100()
    {
        using Image<Rgba32> original = Image.Load<Rgba32>(TestFile.GetInputFileFullPath("Png/CalliphoraPartial.png"));
        using Image<Rgba32> copy = original.Clone();

        Assert.Equal(100, Ssimulacra2.Compute(original, copy));
    }

    [Fact]
    public void DegradedCopy_ScoresLower_TheMoreItIsDegraded()
    {
        using Image<Rgba32> original = Image.Load<Rgba32>(TestFile.GetInputFileFullPath("Png/Bike.png"));
        using Image<Rgba32> slightlyBlurred = original.Clone(x => x.GaussianBlur(0.5F));
        using Image<Rgba32> blurred = original.Clone(x => x.GaussianBlur(2));

        double slight = Ssimulacra2.Compute(original, slightlyBlurred);
        double heavy = Ssimulacra2.Compute(original, blurred);

        Assert.InRange(slight, 50, 99);
        Assert.True(heavy < slight - 10, $"A heavier blur scored {heavy}, not well below {slight}.");
    }

    /// <summary>
    /// The score of a copy whose colour samples keep only their top four bits, as the libjxl v0.12.0
    /// <c>ssimulacra2</c> binary (Linux x64, AVX2) prints it for the same two PNGs. The port follows the
    /// binary's rounding, so it agrees to about 1e-5; bike-small has alpha, so it takes the worse of two
    /// backgrounds. The Windows binary prints about 0.02 less, because its C runtime's <c>cbrtf</c> of the
    /// opsin bias is one ulp above the correctly rounded value that glibc and the port use.
    /// </summary>
    /// <param name="file">The image.</param>
    /// <param name="binary">The binary's score.</param>
    [Theory]
    [InlineData("Png/Bike.png", 60.95262422)]
    [InlineData("Png/bike-small.png", 58.09856585)]
    [InlineData("Jxl/Calibration/gradient.png", 19.07678468)]
    public void QuantisedCopy_ScoresAsTheBinary(string file, double binary)
    {
        using Image<Rgba32> original = Image.Load<Rgba32>(TestFile.GetInputFileFullPath(file));
        using Image<Rgba32> quantised = original.Clone();
        quantised.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                foreach (ref Rgba32 pixel in accessor.GetRowSpan(y))
                {
                    pixel = new Rgba32((byte)(pixel.R & 0xF0), (byte)(pixel.G & 0xF0), (byte)(pixel.B & 0xF0), pixel.A);
                }
            }
        });

        Assert.Equal(binary, Ssimulacra2.Compute(original, quantised), 0.001);
    }

    [Fact]
    public void DifferentSizes_Throw()
    {
        using Image<Rgba32> a = new(16, 16);
        using Image<Rgba32> b = new(16, 17);

        Assert.Throws<ArgumentException>(() => Ssimulacra2.Compute(a, b));
    }

    [Fact]
    public void SmallerThan8x8_Throws()
    {
        using Image<Rgba32> a = new(8, 7);
        using Image<Rgba32> b = new(8, 7);

        Assert.Throws<ArgumentException>(() => Ssimulacra2.Compute(a, b));
    }
}
