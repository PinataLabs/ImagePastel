// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using BenchmarkDotNet.Attributes;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;

namespace PinataLabs.ImagePastel.Benchmarks.Processing;

[Config(typeof(Config.MultiFramework))]
public class BokehBlur
{
    [Benchmark]
    public void Blur()
    {
        using var image = new Image<Rgba32>(Configuration.Default, 400, 400, Color.White);
        image.Mutate(c => c.BokehBlur());
    }
}
