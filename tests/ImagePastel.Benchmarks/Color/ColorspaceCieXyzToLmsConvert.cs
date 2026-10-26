// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using BenchmarkDotNet.Attributes;

using Colourful;

using PinataLabs.ImagePastel.ColorSpaces;
using PinataLabs.ImagePastel.ColorSpaces.Conversion;

namespace PinataLabs.ImagePastel.Benchmarks.ColorSpaces;

public class ColorspaceCieXyzToLmsConvert
{
    private static readonly CieXyz CieXyz = new CieXyz(0.95047F, 1, 1.08883F);

    private static readonly XYZColor XYZColor = new XYZColor(0.95047, 1, 1.08883);

    private static readonly ColorSpaceConverter ColorSpaceConverter = new ColorSpaceConverter();

    private static readonly IColorConverter<XYZColor, LMSColor> ColourfulConverter = new ConverterBuilder().FromXYZ().ToLMS().Build();

    [Benchmark(Baseline = true, Description = "Colourful Convert")]
    public double ColourfulConvert()
    {
        return ColourfulConverter.Convert(XYZColor).L;
    }

    [Benchmark(Description = "ImageSharp Convert")]
    public float ColorSpaceConvert()
    {
        return ColorSpaceConverter.ToLms(CieXyz).L;
    }
}
