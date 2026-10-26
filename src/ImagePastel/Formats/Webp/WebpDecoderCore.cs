// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Webp.BitReader;
using PinataLabs.ImagePastel.Formats.Webp.Lossless;
using PinataLabs.ImagePastel.Formats.Webp.Lossy;
using PinataLabs.ImagePastel.IO;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.Metadata.Profiles.Exif;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.Metadata.Profiles.Xmp;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Webp;

/// <summary>
/// Performs the webp decoding operation.
/// </summary>
internal sealed class WebpDecoderCore : IImageDecoderInternals
{
    /// <summary>
    /// Reusable buffer.
    /// </summary>
    private readonly byte[] buffer = new byte[4];

    /// <summary>
    /// Used for allocating memory during processing operations.
    /// </summary>
    private readonly MemoryAllocator memoryAllocator;

    /// <summary>
    /// The stream to decode from.
    /// </summary>
    private Stream currentStream;

    /// <summary>
    /// The stream position where the RIFF payload ends. Bytes after it are not part of the image.
    /// </summary>
    private long riffEnd;

    /// <summary>
    /// A value indicating whether the stream ends before the end of the image data chunk.
    /// </summary>
    private bool imageDataTruncated;

    /// <summary>
    /// The webp specific metadata.
    /// </summary>
    private WebpMetadata webpMetadata;

    /// <summary>
    /// Information about the webp image.
    /// </summary>
    private WebpImageInfo webImageInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebpDecoderCore"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="options">The options.</param>
    public WebpDecoderCore(Configuration configuration, IWebpDecoderOptions options)
    {
        this.Configuration = configuration;
        this.memoryAllocator = configuration.MemoryAllocator;
        this.IgnoreMetadata = options.IgnoreMetadata;
    }

    /// <summary>
    /// Gets a value indicating whether the metadata should be ignored when the image is being decoded.
    /// </summary>
    public bool IgnoreMetadata { get; }

    /// <summary>
    /// Gets the <see cref="ImageMetadata"/> decoded by this decoder instance.
    /// </summary>
    public ImageMetadata Metadata { get; private set; }

    /// <inheritdoc/>
    public Configuration Configuration { get; }

    /// <summary>
    /// Gets the dimensions of the image.
    /// </summary>
    public Size Dimensions => this.webImageInfo == null ? default : new((int)this.webImageInfo.Width, (int)this.webImageInfo.Height);

    /// <summary>
    /// Gets the number of bytes left in the stream. Image data chunks are checked against this rather than the RIFF payload,
    /// so image data that runs past a too small RIFF size can still be decoded.
    /// </summary>
    private long RemainingStreamBytes => this.currentStream.Length - this.currentStream.Position;

    /// <inheritdoc />
    public Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel> image = null;
        try
        {
            this.Metadata = new ImageMetadata();
            this.currentStream = stream;

            uint fileSize = this.ReadImageHeader();

            using (this.webImageInfo = this.ReadVp8Info())
            {
                if (this.webImageInfo.Features is { Animation: true })
                {
                    WebpThrowHelper.ThrowNotSupportedException("Animations are not supported");
                }

                image = new Image<TPixel>(this.Configuration, (int)this.webImageInfo.Width, (int)this.webImageInfo.Height, this.Metadata);
                Buffer2D<TPixel> pixels = image.GetRootFramePixelBuffer();
                try
                {
                    if (this.webImageInfo.IsLossless)
                    {
                        var losslessDecoder = new WebpLosslessDecoder(this.webImageInfo.Vp8LBitReader, this.memoryAllocator, this.Configuration);
                        losslessDecoder.Decode(pixels, image.Width, image.Height);
                    }
                    else
                    {
                        var lossyDecoder = new WebpLossyDecoder(this.webImageInfo.Vp8BitReader, this.memoryAllocator, this.Configuration);
                        lossyDecoder.Decode(pixels, image.Width, image.Height, this.webImageInfo);
                    }
                }
                catch (ImageFormatException ex) when (this.imageDataTruncated && ex is not InvalidImageContentException)
                {
                    // The missing end of the image data is read as zeros. When that is not a valid bitstream, report the truncation.
                    throw new InvalidImageContentException("Not enough data to decode the image", ex);
                }

                // There can be optional chunks after the image data, like EXIF and XMP.
                if (this.webImageInfo.Features != null)
                {
                    this.ParseOptionalChunks(this.webImageInfo.Features);
                }

                return image;
            }
        }
        catch
        {
            image?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public IImageInfo Identify(BufferedReadStream stream, CancellationToken cancellationToken)
    {
        this.currentStream = stream;

        this.ReadImageHeader();
        using (this.webImageInfo = this.ReadVp8Info())
        {
            return new ImageInfo(new PixelTypeInfo((int)this.webImageInfo.BitsPerPixel), (int)this.webImageInfo.Width, (int)this.webImageInfo.Height, this.Metadata);
        }
    }

    /// <summary>
    /// Reads and skips over the image header.
    /// </summary>
    /// <returns>The file size in bytes.</returns>
    private uint ReadImageHeader()
    {
        long riffStart = this.currentStream.Position;

        // Skip FourCC header, we already know its a RIFF file at this point.
        this.currentStream.Skip(4);

        // Read file size.
        // The size of the file in bytes starting at offset 8.
        // The file size in the header is the total size of the chunks that follow plus 4 bytes for the ‘WEBP’ FourCC.
        uint fileSize = this.ReadChunkSize();

        // Chunks are only read up to the end of the RIFF payload, so trailing bytes after it are ignored.
        // A stream shorter than the declared size is truncated, and reading stops at its end.
        this.riffEnd = Math.Min(riffStart + 8 + fileSize, this.currentStream.Length);

        // Skip 'WEBP' from the header.
        this.currentStream.Skip(4);

        return fileSize;
    }

    /// <summary>
    /// Reads information present in the image header, about the image content and how to decode the image.
    /// </summary>
    /// <returns>Information about the webp image.</returns>
    private WebpImageInfo ReadVp8Info()
    {
        this.Metadata = new ImageMetadata();
        this.webpMetadata = this.Metadata.GetFormatMetadata(WebpFormat.Instance);

        WebpChunkType chunkType = this.ReadChunkType();

        switch (chunkType)
        {
            case WebpChunkType.Vp8:
                return this.ReadVp8Header();
            case WebpChunkType.Vp8L:
                return this.ReadVp8LHeader();
            case WebpChunkType.Vp8X:
                return this.ReadVp8XHeader();
            default:
                WebpThrowHelper.ThrowImageFormatException("Unrecognized VP8 header");
                return new WebpImageInfo(); // this return will never be reached, because throw helper will throw an exception.
        }
    }

    /// <summary>
    /// Reads an the extended webp file header. An extended file header consists of:
    /// - A 'VP8X' chunk with information about features used in the file.
    /// - An optional 'ICCP' chunk with color profile.
    /// - An optional 'XMP' chunk with metadata.
    /// - An optional 'ANIM' chunk with animation control data.
    /// - An optional 'ALPH' chunk with alpha channel data.
    /// After the image header, image data will follow. After that optional image metadata chunks (EXIF and XMP) can follow.
    /// </summary>
    /// <returns>Information about this webp image.</returns>
    private WebpImageInfo ReadVp8XHeader()
    {
        var features = new WebpFeatures();
        uint vp8XChunkSize = this.ReadPayloadSize();
        if (vp8XChunkSize < WebpConstants.Vp8XChunkSize)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("VP8X chunk is smaller than its 10 byte payload");
        }

        // The first byte contains information about the image features used.
        int imageFeatures = this.currentStream.ReadByte();
        if (imageFeatures == -1)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("VP8X header doe not contain enough data");
        }

        // The first two bits and the last bit are reserved. Readers must ignore them.

        // If bit 3 is set, a ICC Profile Chunk should be present.
        features.IccProfile = (imageFeatures & (1 << 5)) != 0;

        // If bit 4 is set, any of the frames of the image contain transparency information ("alpha" chunk).
        features.Alpha = (imageFeatures & (1 << 4)) != 0;

        // If bit 5 is set, a EXIF metadata should be present.
        features.ExifProfile = (imageFeatures & (1 << 3)) != 0;

        // If bit 6 is set, XMP metadata should be present.
        features.XmpMetaData = (imageFeatures & (1 << 2)) != 0;

        // If bit 7 is set, animation should be present.
        features.Animation = (imageFeatures & (1 << 1)) != 0;

        // 3 reserved bytes follow. Writers set them to zero, but readers must ignore them.
        int bytesRead = this.currentStream.Read(this.buffer, 0, 3);
        if (bytesRead != 3)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("VP8X header does not contain enough data");
        }

        // 3 bytes for the width.
        bytesRead = this.currentStream.Read(this.buffer, 0, 3);
        if (bytesRead != 3)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("VP8 header does not contain enough data to read the width");
        }

        this.buffer[3] = 0;
        uint width = (uint)BinaryPrimitives.ReadInt32LittleEndian(this.buffer) + 1;

        // 3 bytes for the height.
        bytesRead = this.currentStream.Read(this.buffer, 0, 3);
        if (bytesRead != 3)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("VP8 header does not contain enough data to read the height");
        }

        this.buffer[3] = 0;
        uint height = (uint)BinaryPrimitives.ReadInt32LittleEndian(this.buffer) + 1;

        // A longer chunk can hold more data in a later version of the format. Skip it, with the padding byte of an odd size.
        this.SkipChunkData(PaddedSize(vp8XChunkSize) - WebpConstants.Vp8XChunkSize);

        // Read all the chunks in the order they occur.
        var info = new WebpImageInfo();
        try
        {
            // A chunk header that does not fit before the RIFF end is not part of the file.
            while (this.riffEnd - this.currentStream.Position >= WebpConstants.ChunkHeaderSize)
            {
                WebpChunkType chunkType = this.ReadChunkType();
                if (chunkType is WebpChunkType.Vp8 or WebpChunkType.Vp8L)
                {
                    WebpImageInfo imageInfo = chunkType == WebpChunkType.Vp8 ? this.ReadVp8Header(features) : this.ReadVp8LHeader(features);

                    // A later image chunk replaces an earlier one, so release the earlier image data.
                    // The alpha data belongs to the shared features and stays.
                    info.Vp8BitReader?.Dispose();
                    info.Vp8LBitReader?.Dispose();
                    info = imageInfo;
                }
                else if (IsOptionalVp8XChunk(chunkType))
                {
                    this.ParseOptionalExtendedChunks(chunkType, features);
                }
                else
                {
                    // Ignore unknown chunks.
                    uint chunkSize = this.ReadChunkSize();
                    this.SkipChunkData(chunkSize);
                }
            }

            if (features.Animation)
            {
                // TODO: Animations are not yet supported.
                return new WebpImageInfo() { Width = width, Height = height, Features = features };
            }

            if (info.Vp8BitReader == null && info.Vp8LBitReader == null)
            {
                WebpThrowHelper.ThrowInvalidImageContentException("No VP8 or VP8L image data chunk found");
            }

            return info;
        }
        catch
        {
            // A chunk that can not be read leaves the buffers of the chunks before it unowned.
            info.Dispose();
            features.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the header of a lossy webp image.
    /// </summary>
    /// <param name="features">Webp features.</param>
    /// <returns>Information about this webp image.</returns>
    private WebpImageInfo ReadVp8Header(WebpFeatures features = null)
    {
        this.webpMetadata.FileFormat = WebpFileFormatType.Lossy;

        // VP8 data size (not including this 4 bytes).
        int bytesRead = this.currentStream.Read(this.buffer, 0, 4);
        if (bytesRead != 4)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("Not enough data to read the VP8 data size");
        }

        uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(this.buffer);
        if (dataSize < WebpConstants.Vp8FrameHeaderSize)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("VP8 data size is smaller than the frame header");
        }

        // remaining counts the available image data payload.
        uint remaining = dataSize;

        // Paragraph 9.1 https://tools.ietf.org/html/rfc6386#page-30
        // Frame tag that contains four fields:
        // - A 1-bit frame type (0 for key frames, 1 for interframes).
        // - A 3-bit version number.
        // - A 1-bit show_frame flag.
        // - A 19-bit field containing the size of the first data partition in bytes.
        bytesRead = this.currentStream.Read(this.buffer, 0, 3);
        if (bytesRead != 3)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("Not enough data to read the VP8 frame tag");
        }

        uint frameTag = (uint)(this.buffer[0] | (this.buffer[1] << 8) | (this.buffer[2] << 16));
        remaining -= 3;
        bool isNoKeyFrame = (frameTag & 0x1) == 1;
        if (isNoKeyFrame)
        {
            WebpThrowHelper.ThrowImageFormatException("VP8 header indicates the image is not a key frame");
        }

        uint version = (frameTag >> 1) & 0x7;
        if (version > 3)
        {
            WebpThrowHelper.ThrowImageFormatException($"VP8 header indicates unknown profile {version}");
        }

        bool invisibleFrame = ((frameTag >> 4) & 0x1) == 0;
        if (invisibleFrame)
        {
            WebpThrowHelper.ThrowImageFormatException("VP8 header indicates that the first frame is invisible");
        }

        uint partitionLength = frameTag >> 5;
        if (partitionLength > dataSize)
        {
            WebpThrowHelper.ThrowImageFormatException("VP8 header contains inconsistent size information");
        }

        // Check for VP8 magic bytes.
        bytesRead = this.currentStream.Read(this.buffer, 0, 3);
        if (bytesRead != 3)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("Not enough data to read the VP8 magic bytes");
        }

        if (!this.buffer.AsSpan(0, 3).SequenceEqual(WebpConstants.Vp8HeaderMagicBytes))
        {
            WebpThrowHelper.ThrowImageFormatException("VP8 magic bytes not found");
        }

        bytesRead = this.currentStream.Read(this.buffer, 0, 4);
        if (bytesRead != 4)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("VP8 header does not contain enough data to read the image width and height");
        }

        uint tmp = (uint)BinaryPrimitives.ReadInt16LittleEndian(this.buffer);
        uint width = tmp & 0x3fff;
        sbyte xScale = (sbyte)(tmp >> 6);
        tmp = (uint)BinaryPrimitives.ReadInt16LittleEndian(this.buffer.AsSpan(2));
        uint height = tmp & 0x3fff;
        sbyte yScale = (sbyte)(tmp >> 6);
        remaining -= 7;
        if (width == 0 || height == 0)
        {
            WebpThrowHelper.ThrowImageFormatException("width or height can not be zero");
        }

        if (partitionLength > remaining)
        {
            WebpThrowHelper.ThrowImageFormatException("bad partition length");
        }

        var vp8FrameHeader = new Vp8FrameHeader()
        {
            KeyFrame = true,
            Profile = (sbyte)version,
            PartitionLength = partitionLength
        };

        // The bit reader holds the data in a single buffer, which can not be larger than int.MaxValue.
        if (remaining >= int.MaxValue)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("VP8 data size is too large");
        }

        this.imageDataTruncated = remaining > this.RemainingStreamBytes;
        var bitReader = new Vp8BitReader(
            this.currentStream,
            remaining,
            this.memoryAllocator,
            partitionLength)
        {
            Remaining = remaining
        };

        // Skip the padding byte of an odd-sized chunk, so a following chunk is read from its start.
        this.SkipChunkData(dataSize & 1);

        return new WebpImageInfo()
        {
            Width = width,
            Height = height,
            XScale = xScale,
            YScale = yScale,
            BitsPerPixel = features?.Alpha == true ? WebpBitsPerPixel.Pixel32 : WebpBitsPerPixel.Pixel24,
            IsLossless = false,
            Features = features,
            Vp8Profile = (sbyte)version,
            Vp8FrameHeader = vp8FrameHeader,
            Vp8BitReader = bitReader
        };
    }

    /// <summary>
    /// Reads the header of a lossless webp image.
    /// </summary>
    /// <param name="features">Webp image features.</param>
    /// <returns>Information about this image.</returns>
    private WebpImageInfo ReadVp8LHeader(WebpFeatures features = null)
    {
        this.webpMetadata.FileFormat = WebpFileFormatType.Lossless;

        // VP8L data size, without the padding byte of an odd-sized chunk.
        uint imageDataSize = this.ReadPayloadSize();

        // The bit reader holds the data in a single buffer, which can not be larger than int.MaxValue.
        if (imageDataSize >= int.MaxValue)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("VP8L data size is too large");
        }

        this.imageDataTruncated = imageDataSize > this.RemainingStreamBytes;

        // The header has a 1 byte signature and 32 bits for the size, alpha flag and version.
        if (this.RemainingStreamBytes < 5)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("Not enough data to read the VP8L header");
        }

        var bitReader = new Vp8LBitReader(this.currentStream, imageDataSize, this.memoryAllocator);
        try
        {
            // Skip the padding byte of an odd-sized chunk, so a following chunk is read from its start.
            this.SkipChunkData(imageDataSize & 1);
            return this.ReadVp8LHeaderFields(bitReader, features);
        }
        catch
        {
            bitReader.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the VP8L header fields from the bit reader.
    /// </summary>
    /// <param name="bitReader">The bit reader positioned at the VP8L signature.</param>
    /// <param name="features">Webp image features.</param>
    /// <returns>Information about this image.</returns>
    private WebpImageInfo ReadVp8LHeaderFields(Vp8LBitReader bitReader, WebpFeatures features)
    {
        // One byte signature, should be 0x2f.
        uint signature = bitReader.ReadValue(8);
        if (signature != WebpConstants.Vp8LHeaderMagicByte)
        {
            WebpThrowHelper.ThrowImageFormatException("Invalid VP8L signature");
        }

        // The first 28 bits of the bitstream specify the width and height of the image.
        uint width = bitReader.ReadValue(WebpConstants.Vp8LImageSizeBits) + 1;
        uint height = bitReader.ReadValue(WebpConstants.Vp8LImageSizeBits) + 1;
        if (width == 0 || height == 0)
        {
            WebpThrowHelper.ThrowImageFormatException("invalid width or height read");
        }

        // The alphaIsUsed flag should be set to 0 when all alpha values are 255 in the picture, and 1 otherwise.
        // TODO: this flag value is not used yet
        bool alphaIsUsed = bitReader.ReadBit();

        // The next 3 bits are the version. The version number is a 3 bit code that must be set to 0.
        // Any other value should be treated as an error.
        uint version = bitReader.ReadValue(WebpConstants.Vp8LVersionBits);
        if (version != 0)
        {
            WebpThrowHelper.ThrowNotSupportedException($"Unexpected version number {version} found in VP8L header");
        }

        return new WebpImageInfo()
        {
            Width = width,
            Height = height,
            BitsPerPixel = WebpBitsPerPixel.Pixel32,
            IsLossless = true,
            Features = features,
            Vp8LBitReader = bitReader
        };
    }

    /// <summary>
    /// Parses optional VP8X chunks, which can be ICCP, XMP, ANIM or ALPH chunks.
    /// </summary>
    /// <param name="chunkType">The chunk type.</param>
    /// <param name="features">The webp image features.</param>
    private void ParseOptionalExtendedChunks(WebpChunkType chunkType, WebpFeatures features)
    {
        switch (chunkType)
        {
            case WebpChunkType.Iccp:
                this.ReadIccProfile();
                break;

            case WebpChunkType.Exif:
                this.ReadExifProfile();
                break;

            case WebpChunkType.Xmp:
                this.ReadXmpProfile();
                break;

            case WebpChunkType.Animation:
                // TODO: Decoding animation is not implemented yet.
                break;

            case WebpChunkType.Alpha:
                uint alphaChunkSize = this.ReadPayloadSize();

                // The chunk holds a 1 byte header and the alpha data. Check its size before allocating the data.
                if (alphaChunkSize == 0 || alphaChunkSize > this.RemainingStreamBytes)
                {
                    WebpThrowHelper.ThrowInvalidImageContentException("Not enough data to read the alpha chunk");
                }

                features.AlphaChunkHeader = (byte)this.currentStream.ReadByte();
                int alphaDataSize = (int)(alphaChunkSize - 1);

                // A later alpha chunk replaces an earlier one, so release the earlier alpha data.
                features.AlphaData?.Dispose();
                features.AlphaData = null;
                features.AlphaData = this.memoryAllocator.Allocate<byte>(alphaDataSize);
                int bytesRead = this.currentStream.Read(features.AlphaData.Memory.Span, 0, alphaDataSize);
                if (bytesRead != alphaDataSize)
                {
                    WebpThrowHelper.ThrowInvalidImageContentException("Not enough data to read the alpha chunk");
                }

                // Skip the padding byte of an odd-sized chunk, so a following chunk is read from its start.
                this.SkipChunkData(alphaChunkSize & 1);
                break;
            default:
                WebpThrowHelper.ThrowImageFormatException("Unexpected chunk followed VP8X header");
                break;
        }
    }

    /// <summary>
    /// Parses optional metadata chunks. There SHOULD be at most one chunk of each type ('EXIF' and 'XMP ').
    /// If there are more such chunks, readers MAY ignore all except the first one.
    /// Also, a file may possibly contain both 'EXIF' and 'XMP ' chunks.
    /// </summary>
    /// <param name="features">The webp features.</param>
    private void ParseOptionalChunks(WebpFeatures features)
    {
        if (this.IgnoreMetadata || (features.ExifProfile == false && features.XmpMetaData == false))
        {
            return;
        }

        while (this.riffEnd - this.currentStream.Position >= WebpConstants.ChunkHeaderSize)
        {
            // Read chunk header.
            WebpChunkType chunkType = this.ReadChunkType();
            if (chunkType == WebpChunkType.Exif && this.Metadata.ExifProfile == null)
            {
                this.ReadExifProfile();
            }
            else if (chunkType == WebpChunkType.Xmp && this.Metadata.XmpProfile == null)
            {
                this.ReadXmpProfile();
            }
            else
            {
                // Skip duplicate XMP or EXIF chunk.
                uint chunkLength = this.ReadChunkSize();
                this.SkipChunkData(chunkLength);
            }
        }
    }

    /// <summary>
    /// Reads the EXIF profile from the stream.
    /// </summary>
    private void ReadExifProfile()
    {
        uint exifChunkSize = this.ReadPayloadSize();
        if (this.IgnoreMetadata)
        {
            this.SkipChunkData(PaddedSize(exifChunkSize));
        }
        else
        {
            byte[] exifData = this.ReadChunkData(exifChunkSize);
            if (exifData == null)
            {
                // Ignore invalid chunk.
                return;
            }

            var profile = new ExifProfile(exifData);
            this.Metadata.ExifProfile = profile;
        }
    }

    /// <summary>
    /// Reads the XMP profile the stream.
    /// </summary>
    private void ReadXmpProfile()
    {
        uint xmpChunkSize = this.ReadPayloadSize();
        if (this.IgnoreMetadata)
        {
            this.SkipChunkData(PaddedSize(xmpChunkSize));
        }
        else
        {
            byte[] xmpData = this.ReadChunkData(xmpChunkSize);
            if (xmpData == null)
            {
                // Ignore invalid chunk.
                return;
            }

            var profile = new XmpProfile(xmpData);
            this.Metadata.XmpProfile = profile;
        }
    }

    /// <summary>
    /// Reads the ICCP chunk from the stream.
    /// </summary>
    private void ReadIccProfile()
    {
        uint iccpChunkSize = this.ReadPayloadSize();
        if (this.IgnoreMetadata)
        {
            this.SkipChunkData(PaddedSize(iccpChunkSize));
        }
        else
        {
            byte[] iccpData = this.ReadChunkData(iccpChunkSize);
            if (iccpData == null)
            {
                WebpThrowHelper.ThrowInvalidImageContentException("Not enough data to read the iccp chunk");
            }

            // WebP pixels are always color, so only an RGB profile describes them.
            var profile = new IccProfile(iccpData);
            if (profile.CheckIsValid() && DecoderColorProfileConversion.CanBePixelProfile(profile, gray: false))
            {
                this.Metadata.IccProfile = profile;
            }
        }
    }

    /// <summary>
    /// Reads the data of a metadata chunk and skips the padding byte that follows odd-sized data.
    /// The data is only allocated once the chunk is known to fit in the rest of the RIFF payload.
    /// </summary>
    /// <param name="payloadSize">The chunk payload size in bytes, without the padding byte.</param>
    /// <returns>The chunk data, or null if the chunk extends past the end of the RIFF payload.</returns>
    private byte[] ReadChunkData(uint payloadSize)
    {
        if (payloadSize > this.riffEnd - this.currentStream.Position)
        {
            // Nothing after a chunk with an invalid size can be located, so skip to the end of the payload.
            this.currentStream.Position = this.riffEnd;
            return null;
        }

        byte[] data = new byte[payloadSize];
        int bytesRead = this.currentStream.Read(data, 0, (int)payloadSize);
        if (bytesRead != payloadSize)
        {
            return null;
        }

        this.SkipChunkData(payloadSize & 1);
        return data;
    }

    /// <summary>
    /// Skips the data of a chunk, stopping at the end of the RIFF payload if the chunk extends past it.
    /// </summary>
    /// <param name="chunkSize">The chunk size in bytes.</param>
    private void SkipChunkData(uint chunkSize)
        => this.currentStream.Position = Math.Min(this.currentStream.Position + chunkSize, this.riffEnd);

    /// <summary>
    /// Identifies the chunk type from the chunk.
    /// </summary>
    /// <exception cref="InvalidImageContentException">
    /// Thrown if the stream ends inside the chunk header.
    /// </exception>
    private WebpChunkType ReadChunkType()
    {
        if (this.currentStream.Read(this.buffer, 0, 4) == 4)
        {
            var chunkType = (WebpChunkType)BinaryPrimitives.ReadUInt32BigEndian(this.buffer);
            return chunkType;
        }

        throw new InvalidImageContentException("Not enough data to read the chunk type");
    }

    /// <summary>
    /// Reads the chunk size. If Chunk Size is odd, a single padding byte will be added to the payload,
    /// so the chunk size will be increased by 1 in those cases.
    /// </summary>
    /// <returns>The chunk size in bytes.</returns>
    /// <exception cref="InvalidImageContentException">
    /// Thrown if the stream ends inside the chunk header.
    /// </exception>
    private uint ReadChunkSize() => PaddedSize(this.ReadPayloadSize());

    /// <summary>
    /// Reads the chunk size as stored in the chunk header, without the padding byte of an odd-sized chunk.
    /// </summary>
    /// <returns>The chunk payload size in bytes.</returns>
    /// <exception cref="InvalidImageContentException">
    /// Thrown if the stream ends inside the chunk header.
    /// </exception>
    private uint ReadPayloadSize()
    {
        if (this.currentStream.Read(this.buffer, 0, 4) == 4)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(this.buffer);
        }

        throw new InvalidImageContentException("Not enough data to read the chunk size");
    }

    /// <summary>
    /// Adds the padding byte that follows an odd-sized chunk payload.
    /// uint.MaxValue is left as is rather than wrapped to zero. No stream is long enough to hold it anyway.
    /// </summary>
    /// <param name="payloadSize">The chunk payload size in bytes.</param>
    /// <returns>The chunk size in bytes, including the padding byte.</returns>
    private static uint PaddedSize(uint payloadSize)
        => (payloadSize % 2 == 0 || payloadSize == uint.MaxValue) ? payloadSize : payloadSize + 1;

    /// <summary>
    /// Determines if the chunk type is an optional VP8X chunk.
    /// </summary>
    /// <param name="chunkType">The chunk type.</param>
    /// <returns>True, if its an optional chunk type.</returns>
    private static bool IsOptionalVp8XChunk(WebpChunkType chunkType)
    {
        switch (chunkType)
        {
            case WebpChunkType.Alpha:
            case WebpChunkType.Iccp:
            case WebpChunkType.Exif:
            case WebpChunkType.Xmp:
                return true;
            case WebpChunkType.AnimationParameter:
            case WebpChunkType.Animation:
                WebpThrowHelper.ThrowNotSupportedException("Animated webp are not yet supported.");
                return false;
            default:
                return false;
        }
    }
}
