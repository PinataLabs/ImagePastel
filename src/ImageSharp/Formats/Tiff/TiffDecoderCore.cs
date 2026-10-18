// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Formats.Tiff.Compression;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;
using SixLabors.ImageSharp.IO;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Tiff;

/// <summary>
/// Performs the tiff decoding operation.
/// </summary>
internal class TiffDecoderCore : IImageDecoderInternals
{
    /// <summary>
    /// The largest buffer, in bytes, used to read a band of rows from an uncompressed strip.
    /// </summary>
    private const int MaxUncompressedBandBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Used for allocating memory during processing operations.
    /// </summary>
    private readonly MemoryAllocator memoryAllocator;

    /// <summary>
    /// Gets or sets a value indicating whether the metadata should be ignored when the image is being decoded.
    /// </summary>
    private readonly bool ignoreMetadata;

    /// <summary>
    /// Gets the decoding mode for multi-frame images
    /// </summary>
    private readonly FrameDecodingMode decodingMode;

    /// <summary>
    /// The options for converting from the embedded color profile.
    /// </summary>
    private readonly IColorProfileDecoderOptions colorProfileOptions;

    /// <summary>
    /// The CMYK transforms built so far in this decode, by profile bytes.
    /// </summary>
    private readonly IccTransformCache cmykTransforms = new();

    /// <summary>
    /// The stream to decode from.
    /// </summary>
    private BufferedReadStream inputStream;

    /// <summary>
    /// Indicates the byte order of the stream.
    /// </summary>
    private ByteOrder byteOrder;

    /// <summary>
    /// Indicating whether is BigTiff format.
    /// </summary>
    private bool isBigTiff;

    /// <summary>
    /// Initializes a new instance of the <see cref="TiffDecoderCore" /> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="options">The decoder options.</param>
    public TiffDecoderCore(Configuration configuration, ITiffDecoderOptions options)
    {
        options ??= new TiffDecoder();

        this.Configuration = configuration ?? Configuration.Default;
        this.ignoreMetadata = options.IgnoreMetadata;
        this.decodingMode = options.DecodingMode;
        this.colorProfileOptions = options;
        this.memoryAllocator = this.Configuration.MemoryAllocator;
    }

    /// <summary>
    /// Gets or sets the bits per sample.
    /// </summary>
    public TiffBitsPerSample BitsPerSample { get; set; }

    /// <summary>
    /// Gets or sets the bits per pixel.
    /// </summary>
    public int BitsPerPixel { get; set; }

    /// <summary>
    /// Gets or sets the lookup table for RGB palette colored images.
    /// </summary>
    public ushort[] ColorMap { get; set; }

    /// <summary>
    /// Gets or sets the photometric interpretation implementation to use when decoding the image.
    /// </summary>
    public TiffColorType ColorType { get; set; }

    /// <summary>
    /// Gets or sets the reference black and white for decoding YCbCr pixel data.
    /// </summary>
    public Rational[] ReferenceBlackAndWhite { get; set; }

    /// <summary>
    /// Gets or sets the YCbCr coefficients.
    /// </summary>
    public Rational[] YcbcrCoefficients { get; set; }

    /// <summary>
    /// Gets or sets the YCbCr sub sampling.
    /// </summary>
    public ushort[] YcbcrSubSampling { get; set; }

    /// <summary>
    /// Gets or sets the compression used, when the image was encoded.
    /// </summary>
    public TiffDecoderCompressionType CompressionType { get; set; }

    /// <summary>
    /// Gets or sets the Fax specific compression options.
    /// </summary>
    public FaxCompressionOptions FaxCompressionOptions { get; set; }

    /// <summary>
    /// Gets or sets the the logical order of bits within a byte.
    /// </summary>
    public TiffFillOrder FillOrder { get; set; }

    /// <summary>
    /// Gets or sets the extra samples type.
    /// </summary>
    public TiffExtraSampleType? ExtraSamplesType { get; set; }

    /// <summary>
    /// Gets or sets the JPEG tables when jpeg compression is used.
    /// </summary>
    public byte[] JpegTables { get; set; }

    /// <summary>
    /// Gets or sets the planar configuration type to use when decoding the image.
    /// </summary>
    public TiffPlanarConfiguration PlanarConfiguration { get; set; }

    /// <summary>
    /// Gets or sets the photometric interpretation.
    /// </summary>
    public TiffPhotometricInterpretation PhotometricInterpretation { get; set; }

    /// <summary>
    /// Gets or sets the sample format.
    /// </summary>
    public TiffSampleFormat SampleFormat { get; set; }

    /// <summary>
    /// Gets or sets the horizontal predictor.
    /// </summary>
    public TiffPredictor Predictor { get; set; }

    /// <summary>
    /// Gets or sets the transform from the current frame's embedded CMYK profile, or
    /// <see langword="null"/> to convert CMYK samples with the CMYK-to-RGB formula.
    /// </summary>
    public IccColorTransform CmykTransform { get; set; }

    /// <inheritdoc/>
    public Configuration Configuration { get; }

    /// <inheritdoc/>
    public Size Dimensions { get; private set; }

    /// <inheritdoc/>
    public Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var frames = new List<ImageFrame<TPixel>>();
        try
        {
            this.inputStream = stream;
            var reader = new DirectoryReader(stream, this.Configuration.MemoryAllocator);

            IEnumerable<ExifProfile> directories = reader.Read();
            this.byteOrder = reader.ByteOrder;
            this.isBigTiff = reader.IsBigTiff;

            foreach (ExifProfile ifd in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ImageFrame<TPixel> frame = this.DecodeFrame<TPixel>(ifd, cancellationToken);
                frames.Add(frame);

                if (this.decodingMode is FrameDecodingMode.First)
                {
                    break;
                }
            }

            ImageMetadata metadata = TiffDecoderMetadataCreator.Create(frames, this.ignoreMetadata, reader.ByteOrder, reader.IsBigTiff);

            // TODO: Tiff frames can have different sizes.
            ImageFrame<TPixel> root = frames[0];
            this.Dimensions = root.Size();
            foreach (ImageFrame<TPixel> frame in frames)
            {
                if (frame.Size() != root.Size())
                {
                    TiffThrowHelper.ThrowNotSupported("Images with different sizes are not supported");
                }
            }

            return new Image<TPixel>(this.Configuration, metadata, frames);
        }
        catch
        {
            foreach (ImageFrame<TPixel> f in frames)
            {
                f.Dispose();
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public IImageInfo Identify(BufferedReadStream stream, CancellationToken cancellationToken)
    {
        this.inputStream = stream;
        var reader = new DirectoryReader(stream, this.Configuration.MemoryAllocator);
        IEnumerable<ExifProfile> directories = reader.Read();

        ExifProfile rootFrameExifProfile = directories.First();
        var rootMetadata = TiffFrameMetadata.Parse(rootFrameExifProfile);

        ImageMetadata metadata = TiffDecoderMetadataCreator.Create(reader.ByteOrder, reader.IsBigTiff, rootFrameExifProfile);
        if (!this.ignoreMetadata)
        {
            var rootFrameMetadata = new ImageFrameMetadata { ExifProfile = rootFrameExifProfile };
            TiffDecoderMetadataCreator.SetFrameProfiles(rootFrameMetadata, rootMetadata.PhotometricInterpretation);
            TiffDecoderMetadataCreator.SetImageProfiles(metadata, rootFrameMetadata);
        }

        int width = GetImageWidth(rootFrameExifProfile);
        int height = GetImageHeight(rootFrameExifProfile);

        return new ImageInfo(new PixelTypeInfo((int)rootMetadata.BitsPerPixel), width, height, metadata);
    }

    /// <summary>
    /// Decodes the image data from a specified IFD.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="tags">The IFD tags.</param>
    /// <param name="cancellationToken">The token to monitor cancellation.</param>
    /// <returns> The tiff frame. </returns>
    private ImageFrame<TPixel> DecodeFrame<TPixel>(ExifProfile tags, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var imageFrameMetaData = new ImageFrameMetadata();
        if (!this.ignoreMetadata)
        {
            imageFrameMetaData.ExifProfile = tags;
        }

        TiffFrameMetadata tiffFrameMetaData = imageFrameMetaData.GetTiffMetadata();
        TiffFrameMetadata.Parse(tiffFrameMetaData, tags);

        this.VerifyAndParse(tags, tiffFrameMetaData);
        this.CmykTransform = this.CreateCmykTransform(tags);

        int width = GetImageWidth(tags);
        int height = GetImageHeight(tags);
        int rowsPerStrip = GetRowsPerStrip(tags, height);

        var stripOffsetsArray = (Array)tags.GetValueInternal(ExifTag.StripOffsets).GetValue();
        var stripByteCountsArray = (Array)tags.GetValueInternal(ExifTag.StripByteCounts).GetValue();
        if (stripByteCountsArray.Length < stripOffsetsArray.Length)
        {
            TiffThrowHelper.ThrowImageFormatException("The TIFF image has fewer StripByteCounts than StripOffsets.");
        }

        using IMemoryOwner<ulong> stripOffsetsMemory = this.ConvertNumbers(stripOffsetsArray, out Span<ulong> stripOffsets);
        using IMemoryOwner<ulong> stripByteCountsMemory = this.ConvertNumbers(stripByteCountsArray, out Span<ulong> stripByteCounts);

        // Set the dimensions first, so that a failure to allocate the frame reports its real size.
        this.Dimensions = new Size(width, height);
        var frame = new ImageFrame<TPixel>(this.Configuration, width, height, imageFrameMetaData);

        try
        {
            if (this.PlanarConfiguration == TiffPlanarConfiguration.Planar)
            {
                this.DecodeStripsPlanar(
                    frame,
                    rowsPerStrip,
                    stripOffsets,
                    stripByteCounts,
                    cancellationToken);
            }
            else
            {
                this.DecodeStripsChunky(
                    frame,
                    rowsPerStrip,
                    stripOffsets,
                    stripByteCounts,
                    cancellationToken);
            }
        }
        catch
        {
            frame.Dispose();
            throw;
        }

        if (this.CmykTransform is not null)
        {
            // The pixels now hold the target's colors, so the embedded profile no longer describes
            // them. The frame keeps the target instead, and the post-decode conversion finds nothing to do.
            tiffFrameMetaData.EmbeddedIccProfile = new IccProfile(tags.GetValue(ExifTag.IccProfile).Value);
            tags.RemoveValue(ExifTag.IccProfile);
            imageFrameMetaData.IccProfile = DecoderColorProfileConversion.GetConvertedProfile(this.colorProfileOptions);
        }

        return frame;
    }

    /// <summary>
    /// Builds the transform for CMYK samples from the frame's embedded profile, when the options ask
    /// for a conversion and the profile is a CMYK profile the engine can use.
    /// </summary>
    /// <param name="tags">The IFD tags.</param>
    /// <returns>The transform, or <see langword="null"/> to keep the CMYK-to-RGB formula.</returns>
    private IccColorTransform CreateCmykTransform(ExifProfile tags)
    {
        if (this.ColorType is not (TiffColorType.Cmyk8888 or TiffColorType.Cmyk16161616 or TiffColorType.Cmyk8888Planar or TiffColorType.Cmyk16161616Planar))
        {
            return null;
        }

        byte[] profileBytes = tags.GetValue(ExifTag.IccProfile)?.Value;
        if (profileBytes is null)
        {
            return null;
        }

        // Frames usually repeat the same profile, so they share its transform.
        return this.cmykTransforms.GetOrCreate(
            profileBytes,
            () => DecoderColorProfileConversion.TryCreateCmykTransform(this.colorProfileOptions, new IccProfile(profileBytes), out IccColorTransform transform) ? transform : null);
    }

    private IMemoryOwner<ulong> ConvertNumbers(Array array, out Span<ulong> span)
    {
        if (array is Number[] numbers)
        {
            IMemoryOwner<ulong> memory = this.memoryAllocator.Allocate<ulong>(numbers.Length);
            span = memory.GetSpan();
            for (int i = 0; i < numbers.Length; i++)
            {
                span[i] = (uint)numbers[i];
            }

            return memory;
        }

        DebugGuard.IsTrue(array is ulong[], $"Expected {nameof(UInt64)} array.");
        span = (ulong[])array;
        return null;
    }

    /// <summary>
    /// Calculates the size (in bytes) of one row of strip data using the determined color format.
    /// </summary>
    /// <param name="width">The width of the image.</param>
    /// <param name="plane">The index of the plane for planar image configuration (or -1 for chunky).</param>
    /// <returns>The size (in bytes) of one row.</returns>
    private long CalculateBytesPerRow(int width, int plane = -1)
    {
        DebugGuard.MustBeLessThanOrEqualTo(plane, 3, nameof(plane));

        int bitsPerPixel = 0;

        if (this.PlanarConfiguration == TiffPlanarConfiguration.Chunky)
        {
            DebugGuard.IsTrue(plane == -1, "Expected Chunky planar.");
            bitsPerPixel = this.BitsPerPixel;
        }
        else
        {
            switch (plane)
            {
                case 0:
                    bitsPerPixel = this.BitsPerSample.Channel0;
                    break;
                case 1:
                    bitsPerPixel = this.BitsPerSample.Channel1;
                    break;
                case 2:
                    bitsPerPixel = this.BitsPerSample.Channel2;
                    break;
                case 3:
                    bitsPerPixel = this.BitsPerSample.Channel3;
                    break;
                default:
                    TiffThrowHelper.ThrowNotSupported("More then 4 color channels are not supported");
                    break;
            }
        }

        return (((long)width * bitsPerPixel) + 7) / 8;
    }

    /// <summary>
    /// Calculates how many rows of a strip are decoded at a time. Uncompressed strips are read in bands of rows,
    /// so a large strip does not need one large buffer. Other strips are decoded whole.
    /// </summary>
    /// <param name="width">The width of the image.</param>
    /// <param name="rowsPerStrip">The number of rows per strip.</param>
    /// <param name="bytesPerRow">The size (in bytes) of one row in the largest strip buffer.</param>
    /// <returns>The number of rows decoded at a time.</returns>
    private int CalculateRowsPerBand(int width, int rowsPerStrip, long bytesPerRow)
    {
        int bufferLimit = this.memoryAllocator.SingleBufferAllocationLimitBytes;
        int rowsPerBand = rowsPerStrip;
        if (this.CanReadStripsInBands())
        {
            long bandBytes = Math.Min(MaxUncompressedBandBytes, bufferLimit);
            rowsPerBand = (int)Math.Clamp(bandBytes / Math.Max(bytesPerRow, 1), 1, rowsPerStrip);
        }

        long bufferBytes = bytesPerRow * rowsPerBand;
        if (bufferBytes > bufferLimit)
        {
            TiffThrowHelper.ThrowInvalidImageContentException($"Cannot decode TIFF strip data of {width}x{rowsPerBand} pixels. It needs a buffer of {bufferBytes} bytes, which exceeds the limit of {bufferLimit} bytes.");
        }

        return rowsPerBand;
    }

    /// <summary>
    /// Gets a value indicating whether a strip can be read a band of rows at a time.
    /// This needs uncompressed data where every row has the same size, so YCbCr data is read whole.
    /// </summary>
    private bool CanReadStripsInBands()
        => this.CompressionType == TiffDecoderCompressionType.None
        && this.PhotometricInterpretation != TiffPhotometricInterpretation.YCbCr;

    /// <summary>
    /// Decompresses a band of rows of a strip into the buffer.
    /// </summary>
    /// <param name="decompressor">The decompressor.</param>
    /// <param name="stripOffset">The byte offset of the strip.</param>
    /// <param name="stripByteCount">The size of the strip (in bytes).</param>
    /// <param name="bandTop">The first row of the band, relative to the top of the strip.</param>
    /// <param name="bandHeight">The number of rows in the band.</param>
    /// <param name="bytesPerRow">The size (in bytes) of one row.</param>
    /// <param name="buffer">The output buffer for uncompressed data.</param>
    private void DecompressBand(TiffBaseDecompressor decompressor, ulong stripOffset, ulong stripByteCount, int bandTop, int bandHeight, long bytesPerRow, Span<byte> buffer)
    {
        if (this.CanReadStripsInBands())
        {
            ulong skippedBytes = (ulong)(bandTop * bytesPerRow);
            ulong bandBytes = (ulong)(bandHeight * bytesPerRow);
            ulong streamLength = (ulong)this.inputStream.Length;

            // Measure the data left in the file from the strip offset before adding the band position,
            // so a malformed offset near the end of the 64-bit range cannot wrap around to the file start.
            ulong availableBytes = stripOffset < streamLength ? streamLength - stripOffset : 0;
            stripByteCount = stripByteCount > skippedBytes ? Math.Min(stripByteCount - skippedBytes, bandBytes) : 0;
            stripByteCount = availableBytes > skippedBytes ? Math.Min(stripByteCount, availableBytes - skippedBytes) : 0;
            stripOffset += skippedBytes;

            // Do not decode stale rows of the previous band when the strip, or the file, holds less data than the band needs.
            if (stripByteCount < bandBytes)
            {
                buffer.Slice(0, (int)bandBytes).Clear();
                if (stripByteCount == 0)
                {
                    return;
                }
            }
        }

        decompressor.Decompress(this.inputStream, stripOffset, stripByteCount, bandHeight, buffer);
    }

    /// <summary>
    /// Decodes the image data for planar encoded pixel data.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="frame">The image frame to decode data into.</param>
    /// <param name="rowsPerStrip">The number of rows per strip of data.</param>
    /// <param name="stripOffsets">An array of byte offsets to each strip in the image.</param>
    /// <param name="stripByteCounts">An array of the size of each strip (in bytes).</param>
    /// <param name="cancellationToken">The token to monitor cancellation.</param>
    private void DecodeStripsPlanar<TPixel>(ImageFrame<TPixel> frame, int rowsPerStrip, Span<ulong> stripOffsets, Span<ulong> stripByteCounts, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int stripsPerPixel = this.BitsPerSample.Channels;
        int stripsPerPlane = stripOffsets.Length / stripsPerPixel;
        if (stripsPerPlane == 0)
        {
            TiffThrowHelper.ThrowImageFormatException("The planar TIFF image has fewer StripOffsets than planes.");
        }

        int bitsPerPixel = this.BitsPerPixel;

        Buffer2D<TPixel> pixels = frame.PixelBuffer;

        var stripBuffers = new IMemoryOwner<byte>[stripsPerPixel];
        Span<long> bytesPerRow = stackalloc long[stripsPerPixel];
        long maxBytesPerRow = 0;
        for (int planeIndex = 0; planeIndex < stripsPerPixel; planeIndex++)
        {
            bytesPerRow[planeIndex] = this.CalculateBytesPerRow(frame.Width, planeIndex);
            maxBytesPerRow = Math.Max(maxBytesPerRow, bytesPerRow[planeIndex]);
        }

        int rowsPerBand = this.CalculateRowsPerBand(frame.Width, rowsPerStrip, maxBytesPerRow);

        try
        {
            for (int planeIndex = 0; planeIndex < stripBuffers.Length; planeIndex++)
            {
                stripBuffers[planeIndex] = this.memoryAllocator.Allocate<byte>((int)(bytesPerRow[planeIndex] * rowsPerBand));
            }

            using TiffBaseDecompressor decompressor = TiffDecompressorsFactory.Create(
                this.Configuration,
                this.CompressionType,
                this.memoryAllocator,
                this.PhotometricInterpretation,
                frame.Width,
                bitsPerPixel,
                this.ColorType,
                this.Predictor,
                this.FaxCompressionOptions,
                this.JpegTables,
                this.FillOrder,
                this.byteOrder);

            TiffBasePlanarColorDecoder<TPixel> colorDecoder = TiffColorDecoderFactory<TPixel>.CreatePlanar(
                this.Configuration,
                this.memoryAllocator,
                this.ColorType,
                this.BitsPerSample,
                this.ExtraSamplesType,
                this.ColorMap,
                this.ReferenceBlackAndWhite,
                this.YcbcrCoefficients,
                this.YcbcrSubSampling,
                this.byteOrder,
                this.CmykTransform);

            for (int i = 0; i < stripsPerPlane; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Make sure we ignore any strips that are not needed for the image (if too many are present).
                long top = (long)rowsPerStrip * i;
                if (top >= frame.Height)
                {
                    break;
                }

                int stripTop = (int)top;
                int stripHeight = Math.Min(rowsPerStrip, frame.Height - stripTop);
                for (int bandTop = 0; bandTop < stripHeight; bandTop += rowsPerBand)
                {
                    int bandHeight = Math.Min(rowsPerBand, stripHeight - bandTop);
                    int stripIndex = i;
                    for (int planeIndex = 0; planeIndex < stripsPerPixel; planeIndex++)
                    {
                        this.DecompressBand(
                            decompressor,
                            stripOffsets[stripIndex],
                            stripByteCounts[stripIndex],
                            bandTop,
                            bandHeight,
                            bytesPerRow[planeIndex],
                            stripBuffers[planeIndex].GetSpan());

                        stripIndex += stripsPerPlane;
                    }

                    colorDecoder.Decode(stripBuffers, pixels, 0, stripTop + bandTop, frame.Width, bandHeight);
                }
            }
        }
        finally
        {
            foreach (IMemoryOwner<byte> buf in stripBuffers)
            {
                buf?.Dispose();
            }
        }
    }

    /// <summary>
    /// Decodes the image data for chunky encoded pixel data.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="frame">The image frame to decode data into.</param>
    /// <param name="rowsPerStrip">The rows per strip.</param>
    /// <param name="stripOffsets">The strip offsets.</param>
    /// <param name="stripByteCounts">The strip byte counts.</param>
    /// <param name="cancellationToken">The token to monitor cancellation.</param>
    private void DecodeStripsChunky<TPixel>(ImageFrame<TPixel> frame, int rowsPerStrip, Span<ulong> stripOffsets, Span<ulong> stripByteCounts, CancellationToken cancellationToken)
       where TPixel : unmanaged, IPixel<TPixel>
    {
        long bytesPerRow = this.CalculateBytesPerRow(frame.Width);
        int rowsPerBand = this.CalculateRowsPerBand(frame.Width, rowsPerStrip, bytesPerRow);
        int bitsPerPixel = this.BitsPerPixel;

        using IMemoryOwner<byte> stripBuffer = this.memoryAllocator.Allocate<byte>((int)(bytesPerRow * rowsPerBand), AllocationOptions.Clean);
        Span<byte> stripBufferSpan = stripBuffer.GetSpan();
        Buffer2D<TPixel> pixels = frame.PixelBuffer;

        using TiffBaseDecompressor decompressor = TiffDecompressorsFactory.Create(
            this.Configuration,
            this.CompressionType,
            this.memoryAllocator,
            this.PhotometricInterpretation,
            frame.Width,
            bitsPerPixel,
            this.ColorType,
            this.Predictor,
            this.FaxCompressionOptions,
            this.JpegTables,
            this.FillOrder,
            this.byteOrder);

        TiffBaseColorDecoder<TPixel> colorDecoder = TiffColorDecoderFactory<TPixel>.Create(
            this.Configuration,
            this.memoryAllocator,
            this.ColorType,
            this.BitsPerSample,
            this.ExtraSamplesType,
            this.ColorMap,
            this.ReferenceBlackAndWhite,
            this.YcbcrCoefficients,
            this.YcbcrSubSampling,
            this.byteOrder,
            this.CmykTransform);

        for (int stripIndex = 0; stripIndex < stripOffsets.Length; stripIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Make sure we ignore any strips that are not needed for the image (if too many are present).
            long top = (long)rowsPerStrip * stripIndex;
            if (top >= frame.Height)
            {
                break;
            }

            int stripTop = (int)top;
            int stripHeight = Math.Min(rowsPerStrip, frame.Height - stripTop);
            for (int bandTop = 0; bandTop < stripHeight; bandTop += rowsPerBand)
            {
                int bandHeight = Math.Min(rowsPerBand, stripHeight - bandTop);
                this.DecompressBand(
                    decompressor,
                    stripOffsets[stripIndex],
                    stripByteCounts[stripIndex],
                    bandTop,
                    bandHeight,
                    bytesPerRow,
                    stripBufferSpan);

                colorDecoder.Decode(stripBufferSpan, pixels, 0, stripTop + bandTop, frame.Width, bandHeight);
            }
        }
    }

    /// <summary>
    /// Gets the number of rows per strip, limited to the image height.
    /// </summary>
    /// <param name="exifProfile">The image frame exif profile.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The number of rows per strip.</returns>
    private static int GetRowsPerStrip(ExifProfile exifProfile, int height)
    {
        // A missing RowsPerStrip, or one larger than the height, means that the entire image is one strip.
        IExifValue<Number> rowsPerStrip = exifProfile.GetValue(ExifTag.RowsPerStrip);
        if (rowsPerStrip == null)
        {
            return height;
        }

        int rows = (int)rowsPerStrip.Value;
        if (rows <= 0)
        {
            TiffThrowHelper.ThrowImageFormatException("The TIFF RowsPerStrip value must be greater than zero.");
        }

        return Math.Min(rows, height);
    }

    /// <summary>
    /// Gets the width of the image frame.
    /// </summary>
    /// <param name="exifProfile">The image frame exif profile.</param>
    /// <returns>The image width.</returns>
    private static int GetImageWidth(ExifProfile exifProfile)
    {
        IExifValue<Number> width = exifProfile.GetValue(ExifTag.ImageWidth);
        if (width == null)
        {
            TiffThrowHelper.ThrowImageFormatException("The TIFF image frame is missing the ImageWidth");
        }

        DebugGuard.MustBeLessThanOrEqualTo((ulong)width.Value, (ulong)int.MaxValue, nameof(ExifTag.ImageWidth));

        if ((int)width.Value <= 0)
        {
            TiffThrowHelper.ThrowInvalidImageContentException("The TIFF ImageWidth must be greater than zero.");
        }

        return (int)width.Value;
    }

    /// <summary>
    /// Gets the height of the image frame.
    /// </summary>
    /// <param name="exifProfile">The image frame exif profile.</param>
    /// <returns>The image height.</returns>
    private static int GetImageHeight(ExifProfile exifProfile)
    {
        IExifValue<Number> height = exifProfile.GetValue(ExifTag.ImageLength);
        if (height == null)
        {
            TiffThrowHelper.ThrowImageFormatException("The TIFF image frame is missing the ImageLength");
        }

        if ((int)height.Value <= 0)
        {
            TiffThrowHelper.ThrowInvalidImageContentException("The TIFF ImageLength must be greater than zero.");
        }

        return (int)height.Value;
    }
}
