// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Drawing.Imaging;
using System.IO;
using BenchmarkDotNet.Attributes;
using PinataLabs.ImagePastel.Formats.Gif;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Quantization;
using PinataLabs.ImagePastel.Tests;
using SDImage = System.Drawing.Image;

namespace PinataLabs.ImagePastel.Benchmarks.Codecs;

[Config(typeof(Config.ShortMultiFramework))]
public class EncodeGif
{
    // System.Drawing needs this.
    private Stream bmpStream;
    private SDImage bmpDrawing;
    private Image<Rgba32> bmpCore;

    // Try to get as close to System.Drawing's output as possible
    private readonly GifEncoder encoder = new GifEncoder
    {
        Quantizer = new WebSafePaletteQuantizer(new QuantizerOptions { Dither = KnownDitherings.Bayer4x4 })
    };

    [Params(TestImages.Bmp.Car, TestImages.Png.Rgb48Bpp)]
    public string TestImage { get; set; }

    [GlobalSetup]
    public void ReadImages()
    {
        if (this.bmpStream == null)
        {
            this.bmpStream = File.OpenRead(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, this.TestImage));
            this.bmpCore = Image.Load<Rgba32>(this.bmpStream);
            this.bmpStream.Position = 0;
            this.bmpDrawing = SDImage.FromStream(this.bmpStream);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.bmpStream.Dispose();
        this.bmpStream = null;
        this.bmpCore.Dispose();
        this.bmpDrawing.Dispose();
    }

    [Benchmark(Baseline = true, Description = "System.Drawing Gif")]
    public void GifSystemDrawing()
    {
        using var memoryStream = new MemoryStream();
        this.bmpDrawing.Save(memoryStream, ImageFormat.Gif);
    }

    [Benchmark(Description = "ImageSharp Gif")]
    public void GifImageSharp()
    {
        using var memoryStream = new MemoryStream();
        this.bmpCore.SaveAsGif(memoryStream, this.encoder);
    }
}
