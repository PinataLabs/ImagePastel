// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using BenchmarkDotNet.Attributes;
using PinataLabs.ImagePastel.Formats.Webp;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests;
using SkiaSharp;

namespace PinataLabs.ImagePastel.Benchmarks.Codecs.Webp;

[MemoryDiagnoser]
public class EncodeWebpComparison
{
    private const int Quality = 75;

    private byte[] jpegBytes;
    private MemoryStream destination;
    private Image<Rgb24> imagePastel;
    private SKBitmap skiaBitmap;
    private WebpEncoder encoder;
    private SKWebpEncoderOptions skiaOptions;

    [Params(TestImages.Jpeg.Baseline.ForestBridgeDifferentComponentsQuality, TestImages.Jpeg.Baseline.Calliphora)]
    public string TestImage { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.jpegBytes = File.ReadAllBytes(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, this.TestImage));
        this.imagePastel = Image.Load<Rgb24>(this.jpegBytes);
        this.skiaBitmap = SKBitmap.Decode(this.jpegBytes);
        this.encoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = Quality };
        this.skiaOptions = new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, Quality);
        this.destination = new MemoryStream();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.imagePastel.Dispose();
        this.skiaBitmap.Dispose();
        this.destination.Dispose();
    }

    [Benchmark(Baseline = true, Description = "SkiaSharp encode")]
    public long SkiaSharpEncode()
    {
        this.destination.SetLength(0);
        using SKPixmap pixmap = this.skiaBitmap.PeekPixels();
        pixmap.Encode(this.destination, this.skiaOptions);
        return this.destination.Length;
    }

    [Benchmark(Description = "ImagePastel encode")]
    public long ImagePastelEncode()
    {
        this.destination.SetLength(0);
        this.imagePastel.Save(this.destination, this.encoder);
        return this.destination.Length;
    }

    [Benchmark(Description = "SkiaSharp JPEG to WebP")]
    public long SkiaSharpConvert()
    {
        this.destination.SetLength(0);
        using SKBitmap bitmap = SKBitmap.Decode(this.jpegBytes);
        using SKPixmap pixmap = bitmap.PeekPixels();
        pixmap.Encode(this.destination, this.skiaOptions);
        return this.destination.Length;
    }

    [Benchmark(Description = "ImagePastel JPEG to WebP")]
    public long ImagePastelConvert()
    {
        this.destination.SetLength(0);
        using Image image = Image.Load(this.jpegBytes);
        image.Save(this.destination, this.encoder);
        return this.destination.Length;
    }
}
