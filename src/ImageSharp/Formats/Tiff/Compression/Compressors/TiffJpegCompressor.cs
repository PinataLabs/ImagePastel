// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Tiff.Compression.Compressors;

internal class TiffJpegCompressor : TiffBaseCompressor
{
    public TiffJpegCompressor(Stream output, MemoryAllocator memoryAllocator, int width, int bitsPerPixel, TiffPredictor predictor = TiffPredictor.None)
        : base(output, memoryAllocator, width, bitsPerPixel, predictor)
    {
    }

    /// <inheritdoc/>
    public override TiffCompression Method => TiffCompression.Jpeg;

    /// <inheritdoc/>
    public override void Initialize(int rowsPerStrip)
    {
    }

    /// <inheritdoc/>
    public override void CompressStrip(Span<byte> rows, int height)
    {
        // One byte per pixel is gray (BlackIsZero or WhiteIsZero), three bytes per pixel are RGB.
        if (rows.Length / height == this.Width)
        {
            this.CompressStrip<L8>(rows, height, JpegColorType.Luminance);
        }
        else
        {
            this.CompressStrip<Rgb24>(rows, height, JpegColorType.Rgb);
        }
    }

    private void CompressStrip<TPixel>(Span<byte> rows, int height, JpegColorType colorType)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using var memoryStream = new MemoryStream();
        using var image = Image.LoadPixelData<TPixel>(rows, this.Width, height);
        image.Save(memoryStream, new JpegEncoder()
        {
            ColorType = colorType
        });
        memoryStream.Position = 0;
        memoryStream.WriteTo(this.Output);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
    }
}
