// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using PinataLabs.ImagePastel.Common.Helpers;
using PinataLabs.ImagePastel.Compression.Zlib;
using PinataLabs.ImagePastel.Formats.Png.Chunks;
using PinataLabs.ImagePastel.Formats.Png.Filters;
using PinataLabs.ImagePastel.IO;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.Metadata.Profiles.Exif;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.Metadata.Profiles.Xmp;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Png;

/// <summary>
/// Performs the png decoding operation.
/// </summary>
internal sealed class PngDecoderCore : IImageDecoderInternals
{
    /// <summary>
    /// Reusable buffer.
    /// </summary>
    private readonly byte[] buffer = new byte[4];

    /// <summary>
    /// Gets or sets a value indicating whether the metadata should be ignored when the image is being decoded.
    /// </summary>
    private readonly bool ignoreMetadata;

    /// <summary>
    /// Gets or sets a value indicating whether to read the IHDR and tRNS chunks only.
    /// </summary>
    private readonly bool colorMetadataOnly;

    /// <summary>
    /// Used the manage memory allocations.
    /// </summary>
    private readonly MemoryAllocator memoryAllocator;

    /// <summary>
    /// The cICP, sRGB, cHRM and gAMA chunks read before the image data.
    /// </summary>
    private readonly PngColorSpaceChunks colorSpaceChunks = new();

    /// <summary>
    /// The stream to decode from.
    /// </summary>
    private BufferedReadStream currentStream;

    /// <summary>
    /// The png header.
    /// </summary>
    private PngHeader header;

    /// <summary>
    /// The number of bytes per pixel.
    /// </summary>
    private int bytesPerPixel;

    /// <summary>
    /// The number of bytes per sample.
    /// </summary>
    private int bytesPerSample;

    /// <summary>
    /// The number of bytes per scanline.
    /// </summary>
    private int bytesPerScanline;

    /// <summary>
    /// The palette containing color information for indexed png's.
    /// </summary>
    private byte[] palette;

    /// <summary>
    /// The palette containing alpha channel color information for indexed png's.
    /// </summary>
    private byte[] paletteAlpha;

    /// <summary>
    /// Previous scanline processed.
    /// </summary>
    private IMemoryOwner<byte> previousScanline;

    /// <summary>
    /// The current scanline that is being processed.
    /// </summary>
    private IMemoryOwner<byte> scanline;

    /// <summary>
    /// The index of the current scanline being processed.
    /// </summary>
    private int currentRow = Adam7.FirstRow[0];

    /// <summary>
    /// The current number of bytes read in the current scanline.
    /// </summary>
    private int currentRowBytesRead;

    /// <summary>
    /// Gets or sets the png color type.
    /// </summary>
    private PngColorType pngColorType;

    /// <summary>
    /// The next chunk of data to return.
    /// </summary>
    private PngChunk? nextChunk;

    /// <summary>
    /// A value indicating whether the image data has been read.
    /// </summary>
    private bool hasImageData;

    /// <summary>
    /// Initializes a new instance of the <see cref="PngDecoderCore"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="options">The decoder options.</param>
    public PngDecoderCore(Configuration configuration, IPngDecoderOptions options)
    {
        this.Configuration = configuration ?? Configuration.Default;
        this.memoryAllocator = this.Configuration.MemoryAllocator;
        this.ignoreMetadata = options.IgnoreMetadata;
    }

    internal PngDecoderCore(Configuration configuration, bool colorMetadataOnly)
    {
        this.Configuration = configuration ?? Configuration.Default;
        this.memoryAllocator = this.Configuration.MemoryAllocator;
        this.colorMetadataOnly = colorMetadataOnly;
        this.ignoreMetadata = true;
    }

    /// <inheritdoc/>
    public Configuration Configuration { get; }

    /// <summary>
    /// Gets the dimensions of the image.
    /// </summary>
    public Size Dimensions => new(this.header.Width, this.header.Height);

    /// <inheritdoc/>
    public Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var metadata = new ImageMetadata();
        PngMetadata pngMetadata = metadata.GetPngMetadata();
        this.currentStream = stream;
        this.currentStream.Skip(8);
        Image<TPixel> image = null;
        try
        {
            while (this.TryReadChunk(out PngChunk chunk))
            {
                try
                {
                    switch (chunk.Type)
                    {
                        case PngChunkType.Header:
                            this.ReadHeaderChunk(pngMetadata, chunk.Data.GetSpan());
                            break;
                        case PngChunkType.Physical:
                            this.ReadPhysicalChunk(metadata, chunk.Data.GetSpan());
                            break;
                        case PngChunkType.Gamma:
                            this.ReadGammaChunk(pngMetadata, chunk.Data.GetSpan());
                            this.ReadColorSpaceChunk(chunk, image is not null);
                            break;
                        case PngChunkType.Chroma:
                        case PngChunkType.StandardRgbColourSpace:
                        case PngChunkType.CodingIndependentCodePoints:
                            this.ReadColorSpaceChunk(chunk, image is not null);
                            break;
                        case PngChunkType.Data:
                            if (image is null)
                            {
                                this.InitializeImage(metadata, out image);
                            }

                            this.ReadScanlines(chunk, image.Frames.RootFrame, pngMetadata);

                            break;
                        case PngChunkType.Palette:
                            byte[] pal = new byte[chunk.Length];
                            chunk.Data.GetSpan().CopyTo(pal);
                            this.palette = pal;
                            break;
                        case PngChunkType.Transparency:
                            byte[] alpha = new byte[chunk.Length];
                            chunk.Data.GetSpan().CopyTo(alpha);
                            this.paletteAlpha = alpha;
                            this.AssignTransparentMarkers(alpha, pngMetadata);
                            break;
                        case PngChunkType.Text:
                            this.ReadTextChunk(metadata, pngMetadata, chunk.Data.GetSpan());
                            break;
                        case PngChunkType.CompressedText:
                            this.ReadCompressedTextChunk(metadata, pngMetadata, chunk.Data.GetSpan());
                            break;
                        case PngChunkType.InternationalText:
                            this.ReadInternationalTextChunk(metadata, chunk.Data.GetSpan());
                            break;
                        case PngChunkType.EmbeddedColorProfile:
                            // The spec places iCCP before the image data, so a later one is out of place.
                            if (image is null)
                            {
                                this.ReadIccProfileChunk(metadata, chunk.Data.GetSpan());
                            }

                            break;
                        case PngChunkType.Exif:
                            if (!this.ignoreMetadata)
                            {
                                byte[] exifData = new byte[chunk.Length];
                                chunk.Data.GetSpan().CopyTo(exifData);
                                this.MergeOrSetExifProfile(metadata, new ExifProfile(exifData), replaceExistingKeys: true);
                            }

                            break;
                        case PngChunkType.End:
                            goto EOF;
                        case PngChunkType.ProprietaryApple:
                            PngThrowHelper.ThrowInvalidChunkType("Proprietary Apple PNG detected! This PNG file is not conform to the specification and cannot be decoded.");
                            break;
                    }
                }
                finally
                {
                    chunk.Data?.Dispose(); // Data is rented in ReadChunkData()
                }
            }

            EOF:
            if (image is null)
            {
                PngThrowHelper.ThrowNoData();
            }

            this.SetPixelProfile(metadata);
            return image;
        }
        catch
        {
            image?.Dispose();
            throw;
        }
        finally
        {
            this.scanline?.Dispose();
            this.previousScanline?.Dispose();
            this.nextChunk?.Data?.Dispose();
        }
    }

    /// <inheritdoc/>
    public IImageInfo Identify(BufferedReadStream stream, CancellationToken cancellationToken)
    {
        var metadata = new ImageMetadata();
        PngMetadata pngMetadata = metadata.GetPngMetadata();
        this.currentStream = stream;
        this.currentStream.Skip(8);
        bool hasSeenImageData = false;
        try
        {
            while (this.TryReadChunk(out PngChunk chunk))
            {
                try
                {
                    switch (chunk.Type)
                    {
                        case PngChunkType.Header:
                            this.ReadHeaderChunk(pngMetadata, chunk.Data.GetSpan());
                            break;
                        case PngChunkType.Physical:
                            if (this.colorMetadataOnly)
                            {
                                this.SkipChunkDataAndCrc(chunk);
                                break;
                            }

                            this.ReadPhysicalChunk(metadata, chunk.Data.GetSpan());
                            break;
                        case PngChunkType.Gamma:
                            if (this.colorMetadataOnly)
                            {
                                this.SkipChunkDataAndCrc(chunk);
                                break;
                            }

                            this.ReadGammaChunk(pngMetadata, chunk.Data.GetSpan());
                            this.ReadColorSpaceChunk(chunk, hasSeenImageData);
                            break;
                        case PngChunkType.Chroma:
                        case PngChunkType.StandardRgbColourSpace:
                        case PngChunkType.CodingIndependentCodePoints:
                            if (this.colorMetadataOnly)
                            {
                                this.SkipChunkDataAndCrc(chunk);
                                break;
                            }

                            this.ReadColorSpaceChunk(chunk, hasSeenImageData);
                            break;
                        case PngChunkType.Data:

                            // Spec says tRNS must be before IDAT so safe to exit.
                            if (this.colorMetadataOnly)
                            {
                                goto EOF;
                            }

                            hasSeenImageData = true;
                            this.SkipChunkDataAndCrc(chunk);
                            break;
                        case PngChunkType.Transparency:
                            byte[] alpha = new byte[chunk.Length];
                            chunk.Data.GetSpan().CopyTo(alpha);
                            this.paletteAlpha = alpha;
                            this.AssignTransparentMarkers(alpha, pngMetadata);

                            if (this.colorMetadataOnly)
                            {
                                goto EOF;
                            }

                            break;
                        case PngChunkType.Text:
                            if (this.colorMetadataOnly)
                            {
                                this.SkipChunkDataAndCrc(chunk);
                                break;
                            }

                            this.ReadTextChunk(metadata, pngMetadata, chunk.Data.GetSpan());
                            break;
                        case PngChunkType.CompressedText:
                            if (this.colorMetadataOnly)
                            {
                                this.SkipChunkDataAndCrc(chunk);
                                break;
                            }

                            this.ReadCompressedTextChunk(metadata, pngMetadata, chunk.Data.GetSpan());
                            break;
                        case PngChunkType.InternationalText:
                            if (this.colorMetadataOnly)
                            {
                                this.SkipChunkDataAndCrc(chunk);
                                break;
                            }

                            this.ReadInternationalTextChunk(metadata, chunk.Data.GetSpan());
                            break;
                        case PngChunkType.EmbeddedColorProfile:
                            if (this.colorMetadataOnly)
                            {
                                this.SkipChunkDataAndCrc(chunk);
                                break;
                            }

                            // The spec places iCCP before the image data, so a later one is out of place.
                            if (!hasSeenImageData)
                            {
                                this.ReadIccProfileChunk(metadata, chunk.Data.GetSpan());
                            }

                            break;
                        case PngChunkType.Exif:
                            if (this.colorMetadataOnly)
                            {
                                this.SkipChunkDataAndCrc(chunk);
                                break;
                            }

                            if (!this.ignoreMetadata)
                            {
                                byte[] exifData = new byte[chunk.Length];
                                chunk.Data.GetSpan().CopyTo(exifData);
                                this.MergeOrSetExifProfile(metadata, new ExifProfile(exifData), replaceExistingKeys: true);
                            }

                            break;
                        case PngChunkType.End:
                            goto EOF;

                        default:
                            if (this.colorMetadataOnly)
                            {
                                this.SkipChunkDataAndCrc(chunk);
                            }

                            break;
                    }
                }
                finally
                {
                    chunk.Data?.Dispose(); // Data is rented in ReadChunkData()
                }
            }

            EOF:
            if (this.header.Width == 0 && this.header.Height == 0)
            {
                PngThrowHelper.ThrowNoHeader();
            }

            this.SetPixelProfile(metadata);
            return new ImageInfo(new PixelTypeInfo(this.CalculateBitsPerPixel()), this.header.Width, this.header.Height, metadata);
        }
        finally
        {
            this.scanline?.Dispose();
            this.previousScanline?.Dispose();
        }
    }

    /// <summary>
    /// Reads the least significant bits from the byte pair with the others set to 0.
    /// </summary>
    /// <param name="buffer">The source buffer</param>
    /// <param name="offset">THe offset</param>
    /// <returns>The <see cref="int"/></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ReadByteLittleEndian(ReadOnlySpan<byte> buffer, int offset)
        => (byte)(((buffer[offset] & 0xFF) << 16) | (buffer[offset + 1] & 0xFF));

    /// <summary>
    /// Attempts to convert a byte array to a new array where each value in the original array is represented by the
    /// specified number of bits.
    /// </summary>
    /// <param name="source">The bytes of one scanline, without the filter byte. Cannot be empty.</param>
    /// <param name="bits">The number of bits per value.</param>
    /// <param name="buffer">The new array.</param>
    /// <returns>True if the bytes were scaled into <paramref name="buffer"/>; false if no scaling is needed.</returns>
    private bool TryScaleUpTo8BitArray(ReadOnlySpan<byte> source, int bits, out IMemoryOwner<byte> buffer)
    {
        if (bits >= 8)
        {
            buffer = null;
            return false;
        }

        buffer = this.memoryAllocator.Allocate<byte>(source.Length * 8 / bits, AllocationOptions.Clean);
        ref byte resultRef = ref buffer.GetReference();
        int mask = 0xFF >> (8 - bits);
        int resultOffset = 0;

        for (int i = 0; i < source.Length; i++)
        {
            byte b = source[i];
            for (int shift = 0; shift < 8; shift += bits)
            {
                int colorIndex = (b >> (8 - bits - shift)) & mask;
                Unsafe.Add(ref resultRef, resultOffset) = (byte)colorIndex;
                resultOffset++;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the data chunk containing physical dimension data.
    /// </summary>
    /// <param name="metadata">The metadata to read to.</param>
    /// <param name="data">The data containing physical data.</param>
    private void ReadPhysicalChunk(ImageMetadata metadata, ReadOnlySpan<byte> data)
    {
        if (data.Length < PhysicalChunkData.Size)
        {
            // Ignore invalid physical chunks.
            return;
        }

        var physicalChunk = PhysicalChunkData.Parse(data);

        metadata.ResolutionUnits = physicalChunk.UnitSpecifier == byte.MinValue
            ? PixelResolutionUnit.AspectRatio
            : PixelResolutionUnit.PixelsPerMeter;

        metadata.HorizontalResolution = physicalChunk.XAxisPixelsPerUnit;
        metadata.VerticalResolution = physicalChunk.YAxisPixelsPerUnit;
    }

    /// <summary>
    /// Reads the data chunk containing gamma data.
    /// </summary>
    /// <param name="pngMetadata">The metadata to read to.</param>
    /// <param name="data">The data containing physical data.</param>
    private void ReadGammaChunk(PngMetadata pngMetadata, ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            // Ignore invalid gamma chunks.
            return;
        }

        // For example, a gamma of 1/2.2 would be stored as 45455.
        // The value is encoded as a 4-byte unsigned integer, representing gamma times 100000.
        pngMetadata.Gamma = BinaryPrimitives.ReadUInt32BigEndian(data) * 1e-5F;
    }

    /// <summary>
    /// Reads a cICP, sRGB, cHRM or gAMA chunk for the pixel profile. Like iCCP, these chunks belong
    /// before the image data, so a later one is out of place and ignored.
    /// </summary>
    /// <param name="chunk">The chunk.</param>
    /// <param name="afterImageData">Whether the image data has been seen.</param>
    private void ReadColorSpaceChunk(in PngChunk chunk, bool afterImageData)
    {
        if (this.ignoreMetadata || afterImageData)
        {
            return;
        }

        ReadOnlySpan<byte> data = chunk.Data.GetSpan();
        switch (chunk.Type)
        {
            case PngChunkType.CodingIndependentCodePoints:
                this.colorSpaceChunks.ReadCicp(data);
                break;
            case PngChunkType.StandardRgbColourSpace:
                this.colorSpaceChunks.ReadSrgb(data);
                break;
            case PngChunkType.Chroma:
                this.colorSpaceChunks.ReadChromaticities(data);
                break;
            case PngChunkType.Gamma:
                this.colorSpaceChunks.ReadGamma(data);
                break;
        }
    }

    /// <summary>
    /// Sets the pixel profile from the colour space chunks, with the precedence of the PNG specification.
    /// </summary>
    /// <param name="metadata">The metadata, holding the iCCP profile if there is one.</param>
    private void SetPixelProfile(ImageMetadata metadata)
    {
        if (this.ignoreMetadata)
        {
            return;
        }

        // The PNG specification requires a gray iCCP profile for a grayscale image and an RGB one
        // otherwise; any other profile is ignored, so the other chunks apply.
        bool gray = this.pngColorType is PngColorType.Grayscale or PngColorType.GrayscaleWithAlpha;
        IccProfile embeddedProfile = metadata.IccProfile;
        if (embeddedProfile is not null && !DecoderColorProfileConversion.CanBePixelProfile(embeddedProfile, gray))
        {
            embeddedProfile = null;
        }

        metadata.IccProfile = this.colorSpaceChunks.GetPixelProfile(embeddedProfile, gray);
    }

    /// <summary>
    /// Initializes the image and various buffers needed for processing
    /// </summary>
    /// <typeparam name="TPixel">The type the pixels will be</typeparam>
    /// <param name="metadata">The metadata information for the image</param>
    /// <param name="image">The image that we will populate</param>
    private void InitializeImage<TPixel>(ImageMetadata metadata, out Image<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        image = new Image<TPixel>(
            this.Configuration,
            this.header.Width,
            this.header.Height,
            metadata);

        this.bytesPerPixel = this.CalculateBytesPerPixel();
        this.bytesPerScanline = this.CalculateScanlineLength(this.header.Width) + 1;
        this.bytesPerSample = 1;
        if (this.header.BitDepth >= 8)
        {
            this.bytesPerSample = this.header.BitDepth / 8;
        }

        this.previousScanline?.Dispose();
        this.scanline?.Dispose();
        this.previousScanline = this.memoryAllocator.Allocate<byte>(this.bytesPerScanline, AllocationOptions.Clean);
        this.scanline = this.Configuration.MemoryAllocator.Allocate<byte>(this.bytesPerScanline, AllocationOptions.Clean);
    }

    /// <summary>
    /// Calculates the correct number of bits per pixel for the given color type.
    /// </summary>
    /// <returns>The <see cref="int"/></returns>
    private int CalculateBitsPerPixel()
    {
        switch (this.pngColorType)
        {
            case PngColorType.Grayscale:
            case PngColorType.Palette:
                return this.header.BitDepth;
            case PngColorType.GrayscaleWithAlpha:
                return this.header.BitDepth * 2;
            case PngColorType.Rgb:
                return this.header.BitDepth * 3;
            case PngColorType.RgbWithAlpha:
                return this.header.BitDepth * 4;
            default:
                PngThrowHelper.ThrowNotSupportedColor();
                return -1;
        }
    }

    /// <summary>
    /// Calculates the correct number of bytes per pixel for the given color type.
    /// </summary>
    /// <returns>The <see cref="int"/></returns>
    private int CalculateBytesPerPixel()
    {
        switch (this.pngColorType)
        {
            case PngColorType.Grayscale:
                return this.header.BitDepth == 16 ? 2 : 1;

            case PngColorType.GrayscaleWithAlpha:
                return this.header.BitDepth == 16 ? 4 : 2;

            case PngColorType.Palette:
                return 1;

            case PngColorType.Rgb:
                return this.header.BitDepth == 16 ? 6 : 3;

            case PngColorType.RgbWithAlpha:
            default:
                return this.header.BitDepth == 16 ? 8 : 4;
        }
    }

    /// <summary>
    /// Calculates the scanline length.
    /// </summary>
    /// <param name="width">The width of the row.</param>
    /// <returns>
    /// The <see cref="int"/> representing the length.
    /// </returns>
    private int CalculateScanlineLength(int width)
    {
        int mod = this.header.BitDepth == 16 ? 16 : 8;
        int scanlineLength = width * this.header.BitDepth * this.bytesPerPixel;

        int amount = scanlineLength % mod;
        if (amount != 0)
        {
            scanlineLength += mod - amount;
        }

        return scanlineLength / mod;
    }

    /// <summary>
    /// Reads the scanlines within the image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="chunk">The png chunk containing the compressed scanline data.</param>
    /// <param name="image"> The pixel data.</param>
    /// <param name="pngMetadata">The png metadata</param>
    private void ReadScanlines<TPixel>(PngChunk chunk, ImageFrame<TPixel> image, PngMetadata pngMetadata)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using var deframeStream = new ZlibInflateStream(this.currentStream, this.ReadNextDataChunk);
        if (!deframeStream.AllocateNewBytes(chunk.Length, !this.hasImageData))
        {
            return;
        }

        DeflateStream dataStream = deframeStream.CompressedStream;

        try
        {
            if (this.header.InterlaceMethod == PngInterlaceMode.Adam7)
            {
                this.DecodeInterlacedPixelData(dataStream, image, pngMetadata);
            }
            else
            {
                this.DecodePixelData(dataStream, image, pngMetadata);
            }

            // The deflate stream stops reading once the rows are decoded, so skip what is left of the
            // image data (the zlib checksum, padding and any further data chunks) to reach the next chunk.
            deframeStream.SkipRemainingData();
        }
        catch (InvalidDataException ex)
        {
            // The deflate stream reports corrupt compressed data with a generic exception.
            PngThrowHelper.ThrowInvalidImageContentException("PNG image data is corrupt.", ex);
        }
    }

    /// <summary>
    /// Decodes the raw pixel data row by row
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="compressedStream">The compressed pixel data stream.</param>
    /// <param name="image">The image to decode to.</param>
    /// <param name="pngMetadata">The png metadata</param>
    private void DecodePixelData<TPixel>(DeflateStream compressedStream, ImageFrame<TPixel> image, PngMetadata pngMetadata)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        while (this.currentRow < this.header.Height)
        {
            Span<byte> scanlineSpan = this.scanline.GetSpan();
            while (this.currentRowBytesRead < this.bytesPerScanline)
            {
                int bytesRead = compressedStream.Read(scanlineSpan, this.currentRowBytesRead, this.bytesPerScanline - this.currentRowBytesRead);
                if (bytesRead <= 0)
                {
                    goto EXIT;
                }

                this.currentRowBytesRead += bytesRead;
            }

            this.currentRowBytesRead = 0;

            switch ((FilterType)scanlineSpan[0])
            {
                case FilterType.None:
                    break;

                case FilterType.Sub:
                    SubFilter.Decode(scanlineSpan, this.bytesPerPixel);
                    break;

                case FilterType.Up:
                    UpFilter.Decode(scanlineSpan, this.previousScanline.GetSpan());
                    break;

                case FilterType.Average:
                    AverageFilter.Decode(scanlineSpan, this.previousScanline.GetSpan(), this.bytesPerPixel);
                    break;

                case FilterType.Paeth:
                    PaethFilter.Decode(scanlineSpan, this.previousScanline.GetSpan(), this.bytesPerPixel);
                    break;

                default:
                    PngThrowHelper.ThrowUnknownFilter();
                    break;
            }

            this.ProcessDefilteredScanline(scanlineSpan, image, pngMetadata);

            this.SwapScanlineBuffers();
            this.currentRow++;
        }

        EXIT:
        this.hasImageData = true;
    }

    /// <summary>
    /// Decodes the raw interlaced pixel data row by row
    /// <see href="https://github.com/juehv/DentalImageViewer/blob/8a1a4424b15d6cc453b5de3f273daf3ff5e3a90d/DentalImageViewer/lib/jiu-0.14.3/net/sourceforge/jiu/codecs/PNGCodec.java"/>
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="compressedStream">The compressed pixel data stream.</param>
    /// <param name="image">The current image.</param>
    /// <param name="pngMetadata">The png metadata.</param>
    private void DecodeInterlacedPixelData<TPixel>(DeflateStream compressedStream, ImageFrame<TPixel> image, PngMetadata pngMetadata)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (this.hasImageData)
        {
            // The image data has already ended. A later data chunk cannot continue it, as the pass it
            // stopped in is not kept, so decoding it would write its rows to the wrong place.
            return;
        }

        int pass = 0;
        int width = this.header.Width;
        Buffer2D<TPixel> imageBuffer = image.PixelBuffer;
        while (true)
        {
            int numColumns = Adam7.ComputeColumns(width, pass);
            int bytesPerInterlaceScanline = this.CalculateScanlineLength(numColumns) + 1;

            // A pass with no columns has no scanlines, so it reads no data and moves on to the next pass.
            while (numColumns > 0 && this.currentRow < this.header.Height)
            {
                while (this.currentRowBytesRead < bytesPerInterlaceScanline)
                {
                    int bytesRead = compressedStream.Read(this.scanline.GetSpan(), this.currentRowBytesRead, bytesPerInterlaceScanline - this.currentRowBytesRead);
                    if (bytesRead <= 0)
                    {
                        goto EXIT;
                    }

                    this.currentRowBytesRead += bytesRead;
                }

                this.currentRowBytesRead = 0;

                Span<byte> scanSpan = this.scanline.Slice(0, bytesPerInterlaceScanline);
                Span<byte> prevSpan = this.previousScanline.Slice(0, bytesPerInterlaceScanline);

                switch ((FilterType)scanSpan[0])
                {
                    case FilterType.None:
                        break;

                    case FilterType.Sub:
                        SubFilter.Decode(scanSpan, this.bytesPerPixel);
                        break;

                    case FilterType.Up:
                        UpFilter.Decode(scanSpan, prevSpan);
                        break;

                    case FilterType.Average:
                        AverageFilter.Decode(scanSpan, prevSpan, this.bytesPerPixel);
                        break;

                    case FilterType.Paeth:
                        PaethFilter.Decode(scanSpan, prevSpan, this.bytesPerPixel);
                        break;

                    default:
                        PngThrowHelper.ThrowUnknownFilter();
                        break;
                }

                Span<TPixel> rowSpan = imageBuffer.DangerousGetRowSpan(this.currentRow);
                this.ProcessInterlacedDefilteredScanline(scanSpan, rowSpan, pngMetadata, Adam7.FirstColumn[pass], Adam7.ColumnIncrement[pass]);

                this.SwapScanlineBuffers();

                this.currentRow += Adam7.RowIncrement[pass];
            }

            pass++;
            this.previousScanline.Clear();

            if (pass < 7)
            {
                this.currentRow = Adam7.FirstRow[pass];
            }
            else
            {
                break;
            }
        }

        // Leave the loop when the data ends early too, as retrying the pass would read nothing again.
        EXIT:
        this.hasImageData = true;
    }

    /// <summary>
    /// Processes the de-filtered scanline filling the image pixel data
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="defilteredScanline">The de-filtered scanline</param>
    /// <param name="pixels">The image</param>
    /// <param name="pngMetadata">The png metadata.</param>
    private void ProcessDefilteredScanline<TPixel>(ReadOnlySpan<byte> defilteredScanline, ImageFrame<TPixel> pixels, PngMetadata pngMetadata)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Span<TPixel> rowSpan = pixels.PixelBuffer.DangerousGetRowSpan(this.currentRow);

        // Trim the first marker byte from the buffer
        ReadOnlySpan<byte> trimmed = defilteredScanline.Slice(1, defilteredScanline.Length - 1);

        // Convert 1, 2, and 4 bit pixel data into the 8 bit equivalent.
        IMemoryOwner<byte> buffer = null;
        try
        {
            ReadOnlySpan<byte> scanlineSpan = this.TryScaleUpTo8BitArray(
                trimmed,
                this.header.BitDepth,
                out buffer)
            ? buffer.GetSpan()
            : trimmed;

            switch (this.pngColorType)
            {
                case PngColorType.Grayscale:
                    PngScanlineProcessor.ProcessGrayscaleScanline(
                        this.header,
                        scanlineSpan,
                        rowSpan,
                        pngMetadata.HasTransparency,
                        pngMetadata.TransparentL16.GetValueOrDefault(),
                        pngMetadata.TransparentL8.GetValueOrDefault());

                    break;

                case PngColorType.GrayscaleWithAlpha:
                    PngScanlineProcessor.ProcessGrayscaleWithAlphaScanline(
                        this.header,
                        scanlineSpan,
                        rowSpan,
                        this.bytesPerPixel,
                        this.bytesPerSample);

                    break;

                case PngColorType.Palette:
                    PngScanlineProcessor.ProcessPaletteScanline(
                        this.header,
                        scanlineSpan,
                        rowSpan,
                        this.palette,
                        this.paletteAlpha);

                    break;

                case PngColorType.Rgb:
                    PngScanlineProcessor.ProcessRgbScanline(
                        this.Configuration,
                        this.header,
                        scanlineSpan,
                        rowSpan,
                        this.bytesPerPixel,
                        this.bytesPerSample,
                        pngMetadata.HasTransparency,
                        pngMetadata.TransparentRgb48.GetValueOrDefault(),
                        pngMetadata.TransparentRgb24.GetValueOrDefault());

                    break;

                case PngColorType.RgbWithAlpha:
                    PngScanlineProcessor.ProcessRgbaScanline(
                        this.Configuration,
                        this.header,
                        scanlineSpan,
                        rowSpan,
                        this.bytesPerPixel,
                        this.bytesPerSample);

                    break;
            }
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    /// <summary>
    /// Processes the interlaced de-filtered scanline filling the image pixel data
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="defilteredScanline">The de-filtered scanline</param>
    /// <param name="rowSpan">The current image row.</param>
    /// <param name="pngMetadata">The png metadata.</param>
    /// <param name="pixelOffset">The column start index. Always 0 for none interlaced images.</param>
    /// <param name="increment">The column increment. Always 1 for none interlaced images.</param>
    private void ProcessInterlacedDefilteredScanline<TPixel>(ReadOnlySpan<byte> defilteredScanline, Span<TPixel> rowSpan, PngMetadata pngMetadata, int pixelOffset = 0, int increment = 1)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Trim the first marker byte from the buffer
        ReadOnlySpan<byte> trimmed = defilteredScanline.Slice(1, defilteredScanline.Length - 1);

        // Convert 1, 2, and 4 bit pixel data into the 8 bit equivalent.
        IMemoryOwner<byte> buffer = null;
        try
        {
            ReadOnlySpan<byte> scanlineSpan = this.TryScaleUpTo8BitArray(
                trimmed,
                this.header.BitDepth,
                out buffer)
            ? buffer.GetSpan()
            : trimmed;

            switch (this.pngColorType)
            {
                case PngColorType.Grayscale:
                    PngScanlineProcessor.ProcessInterlacedGrayscaleScanline(
                        this.header,
                        scanlineSpan,
                        rowSpan,
                        pixelOffset,
                        increment,
                        pngMetadata.HasTransparency,
                        pngMetadata.TransparentL16.GetValueOrDefault(),
                        pngMetadata.TransparentL8.GetValueOrDefault());

                    break;

                case PngColorType.GrayscaleWithAlpha:
                    PngScanlineProcessor.ProcessInterlacedGrayscaleWithAlphaScanline(
                        this.header,
                        scanlineSpan,
                        rowSpan,
                        pixelOffset,
                        increment,
                        this.bytesPerPixel,
                        this.bytesPerSample);

                    break;

                case PngColorType.Palette:
                    PngScanlineProcessor.ProcessInterlacedPaletteScanline(
                        this.header,
                        scanlineSpan,
                        rowSpan,
                        pixelOffset,
                        increment,
                        this.palette,
                        this.paletteAlpha);

                    break;

                case PngColorType.Rgb:
                    PngScanlineProcessor.ProcessInterlacedRgbScanline(
                        this.header,
                        scanlineSpan,
                        rowSpan,
                        pixelOffset,
                        increment,
                        this.bytesPerPixel,
                        this.bytesPerSample,
                        pngMetadata.HasTransparency,
                        pngMetadata.TransparentRgb48.GetValueOrDefault(),
                        pngMetadata.TransparentRgb24.GetValueOrDefault());

                    break;

                case PngColorType.RgbWithAlpha:
                    PngScanlineProcessor.ProcessInterlacedRgbaScanline(
                        this.header,
                        scanlineSpan,
                        rowSpan,
                        pixelOffset,
                        increment,
                        this.bytesPerPixel,
                        this.bytesPerSample);

                    break;
            }
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    /// <summary>
    /// Decodes and assigns marker colors that identify transparent pixels in non indexed images,
    /// and flags indexed images whose palette has any entry that is not fully opaque.
    /// </summary>
    /// <param name="alpha">The alpha tRNS array.</param>
    /// <param name="pngMetadata">The png metadata.</param>
    private void AssignTransparentMarkers(ReadOnlySpan<byte> alpha, PngMetadata pngMetadata)
    {
        if (this.pngColorType == PngColorType.Rgb)
        {
            if (alpha.Length >= 6)
            {
                if (this.header.BitDepth == 16)
                {
                    // PNG stores 16-bit samples big-endian.
                    ushort rc = BinaryPrimitives.ReadUInt16BigEndian(alpha.Slice(0, 2));
                    ushort gc = BinaryPrimitives.ReadUInt16BigEndian(alpha.Slice(2, 2));
                    ushort bc = BinaryPrimitives.ReadUInt16BigEndian(alpha.Slice(4, 2));

                    pngMetadata.TransparentRgb48 = new Rgb48(rc, gc, bc);
                    pngMetadata.HasTransparency = true;
                    return;
                }

                byte r = ReadByteLittleEndian(alpha, 0);
                byte g = ReadByteLittleEndian(alpha, 2);
                byte b = ReadByteLittleEndian(alpha, 4);
                pngMetadata.TransparentRgb24 = new Rgb24(r, g, b);
                pngMetadata.HasTransparency = true;
            }
        }
        else if (this.pngColorType == PngColorType.Grayscale)
        {
            if (alpha.Length >= 2)
            {
                if (this.header.BitDepth == 16)
                {
                    pngMetadata.TransparentL16 = new L16(BinaryPrimitives.ReadUInt16BigEndian(alpha.Slice(0, 2)));
                }
                else
                {
                    pngMetadata.TransparentL8 = new L8(ReadByteLittleEndian(alpha, 0));
                }

                pngMetadata.HasTransparency = true;
            }
        }
        else if (this.pngColorType == PngColorType.Palette && HasTransparentPaletteEntry(alpha))
        {
            // The alpha values themselves are stored as the palette alpha by the caller.
            pngMetadata.HasTransparency = true;
        }
    }

    /// <summary>
    /// Returns a value indicating whether any palette alpha value in the tRNS array is below fully opaque.
    /// </summary>
    /// <param name="alpha">The alpha tRNS array.</param>
    /// <returns><see langword="true"/> if any entry is not fully opaque; otherwise, <see langword="false"/>.</returns>
    private static bool HasTransparentPaletteEntry(ReadOnlySpan<byte> alpha)
    {
        foreach (byte value in alpha)
        {
            if (value < byte.MaxValue)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a header chunk from the data.
    /// </summary>
    /// <param name="pngMetadata">The png metadata.</param>
    /// <param name="data">The <see cref="T:ReadOnlySpan{byte}"/> containing data.</param>
    private void ReadHeaderChunk(PngMetadata pngMetadata, ReadOnlySpan<byte> data)
    {
        this.header = PngHeader.Parse(data);

        this.header.Validate();

        pngMetadata.BitDepth = (PngBitDepth)this.header.BitDepth;
        pngMetadata.ColorType = this.header.ColorType;
        pngMetadata.InterlaceMethod = this.header.InterlaceMethod;

        this.pngColorType = this.header.ColorType;
    }

    /// <summary>
    /// Reads a text chunk containing image properties from the data.
    /// </summary>
    /// <param name="baseMetadata">The <see cref="ImageMetadata"/> object.</param>
    /// <param name="metadata">The metadata to decode to.</param>
    /// <param name="data">The <see cref="T:Span"/> containing the data.</param>
    private void ReadTextChunk(ImageMetadata baseMetadata, PngMetadata metadata, ReadOnlySpan<byte> data)
    {
        if (this.ignoreMetadata)
        {
            return;
        }

        int zeroIndex = data.IndexOf((byte)0);

        // Keywords are restricted to 1 to 79 bytes in length.
        if (zeroIndex is < PngConstants.MinTextKeywordLength or > PngConstants.MaxTextKeywordLength)
        {
            return;
        }

        ReadOnlySpan<byte> keywordBytes = data.Slice(0, zeroIndex);
        if (!this.TryReadTextKeyword(keywordBytes, out string name))
        {
            return;
        }

        string value = PngConstants.Encoding.GetString(data.Slice(zeroIndex + 1));

        if (!this.TryReadTextChunkMetadata(baseMetadata, name, value))
        {
            metadata.TextData.Add(new PngTextData(name, value, string.Empty, string.Empty));
        }
    }

    /// <summary>
    /// Reads the compressed text chunk. Contains a uncompressed keyword and a compressed text string.
    /// </summary>
    /// <param name="baseMetadata">The <see cref="ImageMetadata"/> object.</param>
    /// <param name="metadata">The metadata to decode to.</param>
    /// <param name="data">The <see cref="T:Span"/> containing the data.</param>
    private void ReadCompressedTextChunk(ImageMetadata baseMetadata, PngMetadata metadata, ReadOnlySpan<byte> data)
    {
        if (this.ignoreMetadata)
        {
            return;
        }

        int zeroIndex = data.IndexOf((byte)0);
        if (zeroIndex < PngConstants.MinTextKeywordLength || zeroIndex > PngConstants.MaxTextKeywordLength)
        {
            return;
        }

        // The keyword must be followed by the compression method.
        if (data.Length < zeroIndex + 2)
        {
            return;
        }

        byte compressionMethod = data[zeroIndex + 1];
        if (compressionMethod != 0)
        {
            // Only compression method 0 is supported (zlib datastream with deflate compression).
            return;
        }

        ReadOnlySpan<byte> keywordBytes = data.Slice(0, zeroIndex);
        if (!this.TryReadTextKeyword(keywordBytes, out string name))
        {
            return;
        }

        ReadOnlySpan<byte> compressedData = data.Slice(zeroIndex + 2);

        if (this.TryUncompressTextData(compressedData, PngConstants.Encoding, out string uncompressed) &&
            !this.TryReadTextChunkMetadata(baseMetadata, name, uncompressed))
        {
            metadata.TextData.Add(new PngTextData(name, uncompressed, string.Empty, string.Empty));
        }
    }

    /// <summary>
    /// Checks if the given text chunk is actually storing parsable metadata.
    /// </summary>
    /// <param name="baseMetadata">The <see cref="ImageMetadata"/> object to store the parsed metadata in.</param>
    /// <param name="chunkName">The name of the text chunk.</param>
    /// <param name="chunkText">The contents of the text chunk.</param>
    /// <returns>True if metadata was successfully parsed from the text chunk. False if the
    /// text chunk was not identified as metadata, and should be stored in the metadata
    /// object unmodified.</returns>
    private bool TryReadTextChunkMetadata(ImageMetadata baseMetadata, string chunkName, string chunkText)
    {
        if (chunkName.Equals("Raw profile type exif", StringComparison.OrdinalIgnoreCase) &&
            this.TryReadLegacyExifTextChunk(baseMetadata, chunkText))
        {
            // Successfully parsed legacy exif data from text
            return true;
        }

        // TODO: "Raw profile type iptc", potentially others?

        // No special chunk data identified
        return false;
    }

    /// <summary>
    /// Reads exif data encoded into a text chunk with the name "raw profile type exif".
    /// This method was used by ImageMagick, exiftool, exiv2, digiKam, etc, before the
    /// 2017 update to png that allowed a true exif chunk.
    /// </summary>
    /// <param name="metadata">The <see cref="ImageMetadata"/> to store the decoded exif tags into.</param>
    /// <param name="data">The contents of the "raw profile type exif" text chunk.</param>
    private bool TryReadLegacyExifTextChunk(ImageMetadata metadata, string data)
    {
        ReadOnlySpan<char> dataSpan = data.AsSpan();
        dataSpan = dataSpan.TrimStart();

        if (dataSpan.Length < 4 || !StringEqualsInsensitive(dataSpan.Slice(0, 4), "exif".AsSpan()))
        {
            // "exif" identifier is missing from the beginning of the text chunk
            return false;
        }

        // Skip to the data length
        dataSpan = dataSpan.Slice(4).TrimStart();
        int dataLengthEnd = dataSpan.IndexOf('\n');
        if (dataLengthEnd < 0 || !TryParseInt32(dataSpan.Slice(0, dataLengthEnd), out int dataLength))
        {
            // The data length is missing, unterminated or not a number
            return false;
        }

        // Skip to the hex-encoded data
        dataSpan = dataSpan.Slice(dataLengthEnd).Trim();

        // Sequence of bytes for the exif header ("Exif" ASCII and two zero bytes).
        // This doesn't actually allocate.
        ReadOnlySpan<byte> exifHeader = new byte[] { 0x45, 0x78, 0x69, 0x66, 0x00, 0x00 };

        if (dataLength < exifHeader.Length)
        {
            // Not enough room for the required exif header, this data couldn't possibly be valid
            return false;
        }

        if (dataLength > dataSpan.Length / 2)
        {
            // Each byte takes two hex digits, so the text is too short to hold the declared length
            return false;
        }

        // Parse the hex-encoded data into the byte array we are going to hand off to ExifProfile
        byte[] exifBlob = new byte[dataLength - exifHeader.Length];

        try
        {
            // Check for the presence of the exif header in the hex-encoded binary data
            byte[] tempExifBuf = exifBlob;
            if (exifBlob.Length < exifHeader.Length)
            {
                // Need to allocate a temporary array, this should be an extremely uncommon (TODO: impossible?) case
                tempExifBuf = new byte[exifHeader.Length];
            }

            HexConverter.HexStringToBytes(dataSpan.Slice(0, exifHeader.Length * 2), tempExifBuf);
            if (!tempExifBuf.AsSpan().Slice(0, exifHeader.Length).SequenceEqual(exifHeader))
            {
                // Exif header in the hex data is not valid
                return false;
            }

            // Skip over the exif header we just tested
            dataSpan = dataSpan.Slice(exifHeader.Length * 2);
            dataLength -= exifHeader.Length;

            // Load the hex-encoded data, one line at a time
            int i = 0;
            while (i < dataLength)
            {
                ReadOnlySpan<char> lineSpan = dataSpan;

                int newlineIndex = dataSpan.IndexOf('\n');
                if (newlineIndex != -1)
                {
                    lineSpan = dataSpan.Slice(0, newlineIndex);
                }

                i += HexConverter.HexStringToBytes(lineSpan, exifBlob.AsSpan().Slice(i));

                if (newlineIndex == -1)
                {
                    // That was the last line
                    break;
                }

                dataSpan = dataSpan.Slice(newlineIndex + 1);
            }

            if (i < dataLength)
            {
                // The hex data ends before the declared length
                return false;
            }
        }
        catch
        {
            return false;
        }

        this.MergeOrSetExifProfile(metadata, new ExifProfile(exifBlob), replaceExistingKeys: false);
        return true;
    }

    /// <summary>
    /// Compares two ReadOnlySpan&lt;char&gt;s in a case-insensitive method.
    /// This is only needed because older frameworks are missing the extension method.
    /// </summary>
    /// <param name="span1">The first <see cref="Span{T}"/> to compare.</param>
    /// <param name="span2">The second <see cref="Span{T}"/> to compare.</param>
    /// <returns>True if the spans were identical, false otherwise.</returns>
    private static bool StringEqualsInsensitive(ReadOnlySpan<char> span1, ReadOnlySpan<char> span2)
    {
#pragma warning disable IDE0022 // Use expression body for methods
        return span1.Equals(span2, StringComparison.OrdinalIgnoreCase);
#pragma warning restore IDE0022 // Use expression body for methods
    }

    /// <summary>
    /// int.TryParse() a ReadOnlySpan&lt;char&gt; holding a non-negative decimal number.
    /// </summary>
    /// <param name="span">The characters to parse.</param>
    /// <param name="value">The parsed <see cref="int"/>.</param>
    /// <returns>True if the characters hold a number that fits in an <see cref="int"/>.</returns>
    private static bool TryParseInt32(ReadOnlySpan<char> span, out int value)
        => int.TryParse(span, NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Sets the <see cref="ExifProfile"/> in <paramref name="metadata"/> to <paramref name="newProfile"/>,
    /// or copies exif tags if <paramref name="metadata"/> already contains an <see cref="ExifProfile"/>.
    /// </summary>
    /// <param name="metadata">The <see cref="ImageMetadata"/> to store the exif data in.</param>
    /// <param name="newProfile">The <see cref="ExifProfile"/> to copy exif tags from.</param>
    /// <param name="replaceExistingKeys">If <paramref name="metadata"/> already contains an <see cref="ExifProfile"/>,
    /// controls whether existing exif tags in <paramref name="metadata"/> will be overwritten with any conflicting
    /// tags from <paramref name="newProfile"/>.</param>
    private void MergeOrSetExifProfile(ImageMetadata metadata, ExifProfile newProfile, bool replaceExistingKeys)
    {
        if (metadata.ExifProfile is null)
        {
            // No exif metadata was loaded yet, so just assign it
            metadata.ExifProfile = newProfile;
        }
        else
        {
            // Try to merge existing keys with the ones from the new profile
            foreach (IExifValue newKey in newProfile.Values)
            {
                if (replaceExistingKeys || metadata.ExifProfile.GetValueInternal(newKey.Tag) is null)
                {
                    metadata.ExifProfile.SetValueInternal(newKey.Tag, newKey.GetValue());
                }
            }
        }
    }

    /// <summary>
    /// Reads a iTXt chunk, which contains international text data. It contains:
    /// - A uncompressed keyword.
    /// - Compression flag, indicating if a compression is used.
    /// - Compression method.
    /// - Language tag (optional).
    /// - A translated keyword (optional).
    /// - Text data, which is either compressed or uncompressed.
    /// </summary>
    /// <param name="metadata">The metadata to decode to.</param>
    /// <param name="data">The <see cref="T:Span"/> containing the data.</param>
    private void ReadInternationalTextChunk(ImageMetadata metadata, ReadOnlySpan<byte> data)
    {
        if (this.ignoreMetadata)
        {
            return;
        }

        PngMetadata pngMetadata = metadata.GetPngMetadata();
        int zeroIndexKeyword = data.IndexOf((byte)0);
        if (zeroIndexKeyword < PngConstants.MinTextKeywordLength || zeroIndexKeyword > PngConstants.MaxTextKeywordLength)
        {
            return;
        }

        // The keyword must be followed by the compression flag and the compression method.
        if (data.Length < zeroIndexKeyword + 3)
        {
            return;
        }

        byte compressionFlag = data[zeroIndexKeyword + 1];
        if (!(compressionFlag == 0 || compressionFlag == 1))
        {
            return;
        }

        byte compressionMethod = data[zeroIndexKeyword + 2];
        if (compressionMethod != 0)
        {
            // Only compression method 0 is supported (zlib datastream with deflate compression).
            return;
        }

        int langStartIdx = zeroIndexKeyword + 3;
        int languageLength = data.Slice(langStartIdx).IndexOf((byte)0);
        if (languageLength < 0)
        {
            return;
        }

        string language = PngConstants.LanguageEncoding.GetString(data.Slice(langStartIdx, languageLength));

        int translatedKeywordStartIdx = langStartIdx + languageLength + 1;
        int translatedKeywordLength = data.Slice(translatedKeywordStartIdx).IndexOf((byte)0);
        if (translatedKeywordLength < 0)
        {
            return;
        }

        string translatedKeyword = PngConstants.TranslatedEncoding.GetString(data.Slice(translatedKeywordStartIdx, translatedKeywordLength));

        ReadOnlySpan<byte> keywordBytes = data.Slice(0, zeroIndexKeyword);
        if (!this.TryReadTextKeyword(keywordBytes, out string keyword))
        {
            return;
        }

        int dataStartIdx = translatedKeywordStartIdx + translatedKeywordLength + 1;
        if (compressionFlag == 1)
        {
            ReadOnlySpan<byte> compressedData = data.Slice(dataStartIdx);

            if (this.TryUncompressTextData(compressedData, PngConstants.TranslatedEncoding, out string uncompressed))
            {
                pngMetadata.TextData.Add(new PngTextData(keyword, uncompressed, language, translatedKeyword));
            }
        }
        else if (this.IsXmpTextData(keywordBytes))
        {
            XmpProfile xmpProfile = new XmpProfile(data.Slice(dataStartIdx).ToArray());
            metadata.XmpProfile = xmpProfile;
        }
        else
        {
            string value = PngConstants.TranslatedEncoding.GetString(data.Slice(dataStartIdx));
            pngMetadata.TextData.Add(new PngTextData(keyword, value, language, translatedKeyword));
        }
    }

    /// <summary>
    /// Reads the iCCP chunk, which holds an embedded ICC profile. The chunk contains:
    /// - A profile name of 1-79 Latin-1 bytes, followed by a null separator.
    /// - The compression method, which must be 0 (zlib datastream with deflate compression).
    /// - The compressed profile.
    /// A malformed chunk is ignored. The spec allows one iCCP chunk, so only the first is read.
    /// See: https://www.w3.org/TR/png/#11iCCP
    /// </summary>
    /// <param name="metadata">The metadata to decode to.</param>
    /// <param name="data">The <see cref="T:Span"/> containing the data.</param>
    private void ReadIccProfileChunk(ImageMetadata metadata, ReadOnlySpan<byte> data)
    {
        if (this.ignoreMetadata || metadata.IccProfile != null)
        {
            return;
        }

        int zeroIndex = data.IndexOf((byte)0);
        if (zeroIndex < PngConstants.MinTextKeywordLength || zeroIndex > PngConstants.MaxTextKeywordLength)
        {
            return;
        }

        // The profile name must be followed by the compression method.
        if (data.Length < zeroIndex + 2 || data[zeroIndex + 1] != 0)
        {
            return;
        }

        if (!this.TryUncompressIccProfile(data.Slice(zeroIndex + 2), out byte[] profileBytes))
        {
            return;
        }

        var profile = new IccProfile(profileBytes);
        if (profile.CheckIsValid())
        {
            metadata.IccProfile = profile;
        }
    }

    /// <summary>
    /// Decompresses the zlib compressed data of an ICC profile.
    /// </summary>
    /// <param name="compressedData">The compressed profile bytes.</param>
    /// <param name="profileBytes">The uncompressed profile bytes.</param>
    /// <returns>True if the data was decompressed; false if it is corrupt or too large to be a profile.</returns>
    private bool TryUncompressIccProfile(ReadOnlySpan<byte> compressedData, out byte[] profileBytes)
    {
        // The smallest and largest profile sizes IccProfile.CheckIsValid accepts. The upper bound limits
        // the memory a small, highly compressed chunk can make the decoder allocate.
        const int MinProfileSize = 128;
        const int MaxProfileSize = 50_000_000;

        profileBytes = null;
        using var memoryStream = new MemoryStream(compressedData.ToArray());
        using var bufferedStream = new BufferedReadStream(this.Configuration, memoryStream);
        using var inflateStream = new ZlibInflateStream(bufferedStream);
        if (!inflateStream.AllocateNewBytes(compressedData.Length, false))
        {
            return false;
        }

        try
        {
            using var uncompressed = new MemoryStream();
            byte[] readBuffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = inflateStream.CompressedStream.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                if (uncompressed.Length + bytesRead > MaxProfileSize)
                {
                    return false;
                }

                long previousLength = uncompressed.Length;
                uncompressed.Write(readBuffer, 0, bytesRead);

                // The profile starts with its size, so stop as soon as that size cannot be valid.
                if (previousLength < 4 && uncompressed.Length >= 4)
                {
                    uint declaredSize = BinaryPrimitives.ReadUInt32BigEndian(uncompressed.GetBuffer());
                    if (declaredSize is < MinProfileSize or >= MaxProfileSize)
                    {
                        return false;
                    }
                }
            }

            profileBytes = uncompressed.ToArray();
            return true;
        }
        catch (InvalidDataException)
        {
            // The deflate stream reports corrupt compressed data with a generic exception.
            return false;
        }
    }

    /// <summary>
    /// Decompresses a byte array with zlib compressed text data.
    /// </summary>
    /// <param name="compressedData">Compressed text data bytes.</param>
    /// <param name="encoding">The string encoding to use.</param>
    /// <param name="value">The uncompressed value.</param>
    /// <returns>The <see cref="bool"/>.</returns>
    private bool TryUncompressTextData(ReadOnlySpan<byte> compressedData, Encoding encoding, out string value)
    {
        using (var memoryStream = new MemoryStream(compressedData.ToArray()))
        using (var bufferedStream = new BufferedReadStream(this.Configuration, memoryStream))
        using (var inflateStream = new ZlibInflateStream(bufferedStream))
        {
            if (!inflateStream.AllocateNewBytes(compressedData.Length, false))
            {
                value = null;
                return false;
            }

            var uncompressedBytes = new List<byte>();

            // Note: this uses a buffer which is only 4 bytes long to read the stream, maybe allocating a larger buffer makes sense here.
            int bytesRead = inflateStream.CompressedStream.Read(this.buffer, 0, this.buffer.Length);
            while (bytesRead != 0)
            {
                uncompressedBytes.AddRange(this.buffer.AsSpan(0, bytesRead).ToArray());
                bytesRead = inflateStream.CompressedStream.Read(this.buffer, 0, this.buffer.Length);
            }

            value = encoding.GetString(uncompressedBytes.ToArray());
            return true;
        }
    }

    /// <summary>
    /// Reads the next data chunk.
    /// </summary>
    /// <returns>Count of bytes in the next data chunk, or 0 if there are no more data chunks left.</returns>
    private int ReadNextDataChunk()
    {
        if (this.nextChunk != null)
        {
            return 0;
        }

        // Zero-length IDAT chunks are valid, so skip them rather than treating them as the end of the data.
        while (true)
        {
            // Skip the CRC of the previous chunk.
            this.currentStream.Read(this.buffer, 0, 4);

            if (!this.TryReadChunk(out PngChunk chunk))
            {
                return 0;
            }

            if (chunk.Type != PngChunkType.Data)
            {
                this.nextChunk = chunk;
                return 0;
            }

            chunk.Data?.Dispose();
            if (chunk.Length > 0)
            {
                return chunk.Length;
            }
        }
    }

    /// <summary>
    /// Reads a chunk from the stream.
    /// </summary>
    /// <param name="chunk">The image format chunk.</param>
    /// <returns>
    /// The <see cref="PngChunk"/>.
    /// </returns>
    private bool TryReadChunk(out PngChunk chunk)
    {
        if (this.nextChunk != null)
        {
            chunk = this.nextChunk.Value;

            this.nextChunk = null;

            return true;
        }

        if (!this.TryReadChunkLength(out int length))
        {
            chunk = default;

            // IEND
            return false;
        }

        while (length < 0 || length > (this.currentStream.Length - this.currentStream.Position))
        {
            // Not a valid chunk so try again until we reach a known chunk.
            if (!this.TryReadChunkLength(out length))
            {
                chunk = default;

                return false;
            }
        }

        PngChunkType type = this.ReadChunkType();

        // If we're reading color metadata only we're only interested in the IHDR and tRNS chunks.
        // We can skip all other chunk data in the stream for better performance.
        if (this.colorMetadataOnly && type != PngChunkType.Header && type != PngChunkType.Transparency)
        {
            chunk = new PngChunk(length, type);

            return true;
        }

        long pos = this.currentStream.Position;
        chunk = new PngChunk(
            length: length,
            type: type,
            data: this.ReadChunkData(length));

        this.ValidateChunk(chunk);

        // Restore the stream position for IDAT chunks, because it will be decoded later and
        // was only read to verifying the CRC is correct.
        if (type == PngChunkType.Data)
        {
            this.currentStream.Position = pos;
        }

        return true;
    }

    /// <summary>
    /// Validates the png chunk.
    /// </summary>
    /// <param name="chunk">The <see cref="PngChunk"/>.</param>
    private void ValidateChunk(in PngChunk chunk)
    {
        uint inputCrc = this.ReadChunkCrc();

        if (chunk.IsCritical)
        {
            Span<byte> chunkType = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(chunkType, (uint)chunk.Type);

            uint validCrc = Crc32.Calculate(chunkType);
            validCrc = Crc32.Calculate(validCrc, chunk.Data.GetSpan());

            if (validCrc != inputCrc)
            {
                string chunkTypeName = Encoding.ASCII.GetString(chunkType);

                // ensure when throwing we dispose the data back to the memory allocator
                chunk.Data?.Dispose();
                PngThrowHelper.ThrowInvalidChunkCrc(chunkTypeName);
            }
        }
    }

    /// <summary>
    /// Reads the cycle redundancy chunk from the data.
    /// </summary>
    [MethodImpl(InliningOptions.ShortMethod)]
    private uint ReadChunkCrc()
    {
        uint crc = 0;
        if (this.currentStream.Read(this.buffer, 0, 4) == 4)
        {
            crc = BinaryPrimitives.ReadUInt32BigEndian(this.buffer);
        }

        return crc;
    }

    /// <summary>
    /// Skips the chunk data and the cycle redundancy chunk read from the data.
    /// </summary>
    /// <param name="chunk">The image format chunk.</param>
    [MethodImpl(InliningOptions.ShortMethod)]
    private void SkipChunkDataAndCrc(in PngChunk chunk)
    {
        this.currentStream.Skip(chunk.Length);
        this.currentStream.Skip(4);
    }

    /// <summary>
    /// Reads the chunk data from the stream.
    /// </summary>
    /// <param name="length">The length of the chunk data to read.</param>
    [MethodImpl(InliningOptions.ShortMethod)]
    private IMemoryOwner<byte> ReadChunkData(int length)
    {
        // We rent the buffer here to return it afterwards in Decode()
        // We don't want to throw a degenerated memory exception here as we want to allow partial decoding
        // so limit the length.
        length = (int)Math.Min(length, this.currentStream.Length - this.currentStream.Position);
        IMemoryOwner<byte> buffer = this.Configuration.MemoryAllocator.Allocate<byte>(length, AllocationOptions.Clean);

        this.currentStream.Read(buffer.GetSpan(), 0, length);

        return buffer;
    }

    /// <summary>
    /// Identifies the chunk type from the chunk.
    /// </summary>
    /// <exception cref="ImageFormatException">
    /// Thrown if the input stream is not valid.
    /// </exception>
    [MethodImpl(InliningOptions.ShortMethod)]
    private PngChunkType ReadChunkType()
    {
        if (this.currentStream.Read(this.buffer, 0, 4) == 4)
        {
            return (PngChunkType)BinaryPrimitives.ReadUInt32BigEndian(this.buffer);
        }
        else
        {
            PngThrowHelper.ThrowInvalidChunkType();

            // The IDE cannot detect the throw here.
            return default;
        }
    }

    /// <summary>
    /// Attempts to read the length of the next chunk.
    /// </summary>
    /// <returns>
    /// Whether the length was read.
    /// </returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    private bool TryReadChunkLength(out int result)
    {
        if (this.currentStream.Read(this.buffer, 0, 4) == 4)
        {
            result = BinaryPrimitives.ReadInt32BigEndian(this.buffer);

            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Tries to reads a text chunk keyword, which have some restrictions to be valid:
    /// Keywords shall contain only printable Latin-1 characters and should not have leading or trailing whitespace.
    /// See: https://www.w3.org/TR/PNG/#11zTXt
    /// </summary>
    /// <param name="keywordBytes">The keyword bytes.</param>
    /// <param name="name">The name.</param>
    /// <returns>True, if the keyword could be read and is valid.</returns>
    private bool TryReadTextKeyword(ReadOnlySpan<byte> keywordBytes, out string name)
    {
        name = string.Empty;

        // Keywords shall contain only printable Latin-1.
        foreach (byte c in keywordBytes)
        {
            if (!((c >= 32 && c <= 126) || (c >= 161 && c <= 255)))
            {
                return false;
            }
        }

        // Keywords should not be empty or have leading or trailing whitespace.
        name = PngConstants.Encoding.GetString(keywordBytes);
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith(" ") || name.EndsWith(" "))
        {
            return false;
        }

        return true;
    }

    private bool IsXmpTextData(ReadOnlySpan<byte> keywordBytes) => keywordBytes.SequenceEqual(PngConstants.XmpKeyword);

    private void SwapScanlineBuffers()
    {
        IMemoryOwner<byte> temp = this.previousScanline;
        this.previousScanline = this.scanline;
        this.scanline = temp;
    }
}
