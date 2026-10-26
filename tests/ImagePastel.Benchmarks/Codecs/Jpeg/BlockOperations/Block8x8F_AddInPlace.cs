// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using BenchmarkDotNet.Attributes;
using PinataLabs.ImagePastel.Formats.Jpeg.Components;

namespace PinataLabs.ImagePastel.Benchmarks.Codecs.Jpeg.BlockOperations;

[Config(typeof(Config.HwIntrinsics_SSE_AVX))]
public class Block8x8F_AddInPlace
{
    [Benchmark]
    public float AddInplace()
    {
        float f = 42F;
        Block8x8F b = default;
        b.AddInPlace(f);
        return f;
    }
}
