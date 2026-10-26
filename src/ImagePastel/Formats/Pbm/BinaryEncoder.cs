// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.IO;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Pbm;

/// <summary>
/// Pixel encoding methods for the PBM binary encoding.
/// </summary>
internal class BinaryEncoder
{
    /// <summary>
    /// Decode pixels into the PBM binary encoding.
    /// </summary>
    /// <typeparam name="TPixel">The type of input pixel.</typeparam>
    /// <param name="configuration">The configuration.</param>
    /// <param name="stream">The bytestream to write to.</param>
    /// <param name="image">The input image.</param>
    /// <param name="colorType">The ColorType to use.</param>
    /// <param name="componentType">Data type of the pixles components.</param>
    /// <exception cref="InvalidImageContentException">
    /// Thrown if an invalid combination of setting is requested.
    /// </exception>
    public static void WritePixels<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image, PbmColorType colorType, PbmComponentType componentType)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (colorType == PbmColorType.Grayscale)
        {
            if (componentType == PbmComponentType.Byte)
            {
                WriteGrayscale(configuration, stream, image);
            }
            else
            {
                WriteWideGrayscale(configuration, stream, image);
            }
        }
        else if (colorType == PbmColorType.Rgb)
        {
            if (componentType == PbmComponentType.Byte)
            {
                WriteRgb(configuration, stream, image);
            }
            else
            {
                WriteWideRgb(configuration, stream, image);
            }
        }
        else
        {
            WriteBlackAndWhite(configuration, stream, image);
        }
    }

    private static void WriteGrayscale<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int width = image.Width;
        int height = image.Height;
        Buffer2D<TPixel> pixelBuffer = image.PixelBuffer;
        MemoryAllocator allocator = configuration.MemoryAllocator;
        using IMemoryOwner<byte> row = allocator.Allocate<byte>(width);
        Span<byte> rowSpan = row.GetSpan();

        for (int y = 0; y < height; y++)
        {
            Span<TPixel> pixelSpan = pixelBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToL8Bytes(
                configuration,
                pixelSpan,
                rowSpan,
                width);

            stream.Write(rowSpan);
        }
    }

    private static void WriteWideGrayscale<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int bytesPerPixel = 2;
        int width = image.Width;
        int height = image.Height;
        Buffer2D<TPixel> pixelBuffer = image.PixelBuffer;
        MemoryAllocator allocator = configuration.MemoryAllocator;
        using IMemoryOwner<byte> row = allocator.Allocate<byte>(width * bytesPerPixel);
        Span<byte> rowSpan = row.GetSpan();

        for (int y = 0; y < height; y++)
        {
            Span<TPixel> pixelSpan = pixelBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToL16Bytes(
                configuration,
                pixelSpan,
                rowSpan,
                width);

            SampleScaler.SwapShortsBigEndian(rowSpan);

            stream.Write(rowSpan);
        }
    }

    private static void WriteRgb<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int bytesPerPixel = 3;
        int width = image.Width;
        int height = image.Height;
        Buffer2D<TPixel> pixelBuffer = image.PixelBuffer;
        MemoryAllocator allocator = configuration.MemoryAllocator;
        using IMemoryOwner<byte> row = allocator.Allocate<byte>(width * bytesPerPixel);
        Span<byte> rowSpan = row.GetSpan();

        for (int y = 0; y < height; y++)
        {
            Span<TPixel> pixelSpan = pixelBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToRgb24Bytes(
                configuration,
                pixelSpan,
                rowSpan,
                width);

            stream.Write(rowSpan);
        }
    }

    private static void WriteWideRgb<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int bytesPerPixel = 6;
        int width = image.Width;
        int height = image.Height;
        Buffer2D<TPixel> pixelBuffer = image.PixelBuffer;
        MemoryAllocator allocator = configuration.MemoryAllocator;
        using IMemoryOwner<byte> row = allocator.Allocate<byte>(width * bytesPerPixel);
        Span<byte> rowSpan = row.GetSpan();

        for (int y = 0; y < height; y++)
        {
            Span<TPixel> pixelSpan = pixelBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToRgb48Bytes(
                configuration,
                pixelSpan,
                rowSpan,
                width);

            SampleScaler.SwapShortsBigEndian(rowSpan);

            stream.Write(rowSpan);
        }
    }

    private static void WriteBlackAndWhite<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int width = image.Width;
        int height = image.Height;
        Buffer2D<TPixel> pixelBuffer = image.PixelBuffer;
        MemoryAllocator allocator = configuration.MemoryAllocator;
        using IMemoryOwner<L8> row = allocator.Allocate<L8>(width);
        Span<L8> rowSpan = row.GetSpan();

        // Each row starts on a byte boundary; the unused bits of its last byte stay zero.
        int bytesPerRow = (width + 7) / 8;
        using IMemoryOwner<byte> packedRow = allocator.Allocate<byte>(bytesPerRow);
        Span<byte> packedSpan = packedRow.GetSpan();

        for (int y = 0; y < height; y++)
        {
            Span<TPixel> pixelSpan = pixelBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToL8(
                configuration,
                pixelSpan,
                rowSpan);

            packedSpan.Clear();
            for (int x = 0; x < width; x++)
            {
                if (rowSpan[x].PackedValue < 128)
                {
                    packedSpan[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
            }

            stream.Write(packedSpan);
        }
    }
}
