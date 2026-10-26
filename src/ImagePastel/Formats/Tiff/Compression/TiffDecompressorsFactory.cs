// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Tiff.Compression.Decompressors;
using PinataLabs.ImagePastel.Formats.Tiff.Constants;
using PinataLabs.ImagePastel.Formats.Tiff.PhotometricInterpretation;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Tiff.Compression;

internal static class TiffDecompressorsFactory
{
    public static TiffBaseDecompressor Create(
        Configuration configuration,
        TiffDecoderCompressionType method,
        MemoryAllocator allocator,
        TiffPhotometricInterpretation photometricInterpretation,
        int width,
        int bitsPerPixel,
        TiffColorType colorType,
        TiffPredictor predictor,
        FaxCompressionOptions faxOptions,
        byte[] jpegTables,
        TiffFillOrder fillOrder,
        ByteOrder byteOrder)
    {
        switch (method)
        {
            case TiffDecoderCompressionType.None:
                DebugGuard.IsTrue(predictor == TiffPredictor.None, "Predictor should only be used with lzw or deflate compression");
                DebugGuard.IsTrue(faxOptions == FaxCompressionOptions.None, "No fax compression options are expected");
                return new NoneTiffCompression(allocator, width, bitsPerPixel);

            case TiffDecoderCompressionType.PackBits:
                DebugGuard.IsTrue(predictor == TiffPredictor.None, "Predictor should only be used with lzw or deflate compression");
                DebugGuard.IsTrue(faxOptions == FaxCompressionOptions.None, "No fax compression options are expected");
                return new PackBitsTiffCompression(allocator, width, bitsPerPixel);

            case TiffDecoderCompressionType.Deflate:
                DebugGuard.IsTrue(faxOptions == FaxCompressionOptions.None, "No fax compression options are expected");
                return new DeflateTiffCompression(allocator, width, bitsPerPixel, colorType, predictor, byteOrder == ByteOrder.BigEndian);

            case TiffDecoderCompressionType.Lzw:
                DebugGuard.IsTrue(faxOptions == FaxCompressionOptions.None, "No fax compression options are expected");
                return new LzwTiffCompression(allocator, width, bitsPerPixel, colorType, predictor, byteOrder == ByteOrder.BigEndian);

            case TiffDecoderCompressionType.T4:
                DebugGuard.IsTrue(predictor == TiffPredictor.None, "Predictor should only be used with lzw or deflate compression");
                return new T4TiffCompression(allocator, fillOrder, width, bitsPerPixel, faxOptions, photometricInterpretation);

            case TiffDecoderCompressionType.T6:
                DebugGuard.IsTrue(predictor == TiffPredictor.None, "Predictor should only be used with lzw or deflate compression");
                return new T6TiffCompression(allocator, fillOrder, width, bitsPerPixel, photometricInterpretation);

            case TiffDecoderCompressionType.HuffmanRle:
                DebugGuard.IsTrue(predictor == TiffPredictor.None, "Predictor should only be used with lzw or deflate compression");
                return new ModifiedHuffmanTiffCompression(allocator, fillOrder, width, bitsPerPixel, photometricInterpretation);

            case TiffDecoderCompressionType.Jpeg:
                DebugGuard.IsTrue(predictor == TiffPredictor.None, "Predictor should only be used with lzw or deflate compression");
                return new JpegTiffCompression(configuration, allocator, width, bitsPerPixel, jpegTables, photometricInterpretation);

            default:
                throw TiffThrowHelper.NotSupportedDecompressor(nameof(method));
        }
    }
}
