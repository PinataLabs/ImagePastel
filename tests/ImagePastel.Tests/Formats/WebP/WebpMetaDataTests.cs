// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Webp;
using PinataLabs.ImagePastel.Metadata.Profiles.Exif;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.Metadata.Profiles.Xmp;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Webp;

[Trait("Format", "Webp")]
public class WebpMetaDataTests
{
    private static WebpDecoder WebpDecoder => new() { IgnoreMetadata = false };

    [Theory]
    [WithFile(TestImages.Webp.Lossy.WithExif, PixelTypes.Rgba32, false)]
    [WithFile(TestImages.Webp.Lossy.WithExif, PixelTypes.Rgba32, true)]
    [WithFile(TestImages.Webp.Lossless.WithExif, PixelTypes.Rgba32, false)]
    [WithFile(TestImages.Webp.Lossless.WithExif, PixelTypes.Rgba32, true)]
    public void IgnoreMetadata_ControlsWhetherExifIsParsed<TPixel>(TestImageProvider<TPixel> provider, bool ignoreMetadata)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var decoder = new WebpDecoder { IgnoreMetadata = ignoreMetadata };

        using Image<TPixel> image = provider.GetImage(decoder);
        if (ignoreMetadata)
        {
            Assert.Null(image.Metadata.ExifProfile);
        }
        else
        {
            ExifProfile exifProfile = image.Metadata.ExifProfile;
            Assert.NotNull(exifProfile);
            Assert.NotEmpty(exifProfile.Values);
            Assert.Contains(exifProfile.Values, m => m.Tag.Equals(ExifTag.Make) && m.GetValue().Equals("Canon"));
            Assert.Contains(exifProfile.Values, m => m.Tag.Equals(ExifTag.Model) && m.GetValue().Equals("Canon PowerShot S40"));
            Assert.Contains(exifProfile.Values, m => m.Tag.Equals(ExifTag.Software) && m.GetValue().Equals("GIMP 2.10.2"));
        }
    }

    [Theory]
    [WithFile(TestImages.Webp.Lossy.WithIccp, PixelTypes.Rgba32, false)]
    [WithFile(TestImages.Webp.Lossy.WithIccp, PixelTypes.Rgba32, true)]
    [WithFile(TestImages.Webp.Lossless.WithIccp, PixelTypes.Rgba32, false)]
    [WithFile(TestImages.Webp.Lossless.WithIccp, PixelTypes.Rgba32, true)]
    public void IgnoreMetadata_ControlsWhetherIccpIsParsed<TPixel>(TestImageProvider<TPixel> provider, bool ignoreMetadata)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var decoder = new WebpDecoder { IgnoreMetadata = ignoreMetadata };

        using Image<TPixel> image = provider.GetImage(decoder);
        if (ignoreMetadata)
        {
            Assert.Null(image.Metadata.IccProfile);
        }
        else
        {
            Assert.NotNull(image.Metadata.IccProfile);
            Assert.NotEmpty(image.Metadata.IccProfile.Entries);
        }
    }

    [Theory]
    [WithFile(TestImages.Webp.Lossy.WithXmp, PixelTypes.Rgba32, false)]
    [WithFile(TestImages.Webp.Lossy.WithXmp, PixelTypes.Rgba32, true)]
    public async Task IgnoreMetadata_ControlsWhetherXmpIsParsed<TPixel>(TestImageProvider<TPixel> provider, bool ignoreMetadata)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var decoder = new WebpDecoder { IgnoreMetadata = ignoreMetadata };

        using Image<TPixel> image = await provider.GetImageAsync(decoder);
        if (ignoreMetadata)
        {
            Assert.Null(image.Metadata.XmpProfile);
        }
        else
        {
            Assert.NotNull(image.Metadata.XmpProfile);
            Assert.NotEmpty(image.Metadata.XmpProfile.Data);
        }
    }

    [Theory]
    [InlineData(WebpFileFormatType.Lossy)]
    [InlineData(WebpFileFormatType.Lossless)]
    public void Encode_WritesExifWithPadding(WebpFileFormatType fileFormatType)
    {
        // arrange
        using var input = new Image<Rgba32>(25, 25);
        using var memoryStream = new MemoryStream();
        var expectedExif = new ExifProfile();
        string expectedSoftware = "ImageSharp";
        expectedExif.SetValue(ExifTag.Software, expectedSoftware);
        input.Metadata.ExifProfile = expectedExif;

        // act
        input.Save(memoryStream, new WebpEncoder() { FileFormat = fileFormatType });
        memoryStream.Position = 0;

        // assert
        using var image = Image.Load<Rgba32>(memoryStream);
        ExifProfile actualExif = image.Metadata.ExifProfile;
        Assert.NotNull(actualExif);
        Assert.Equal(expectedExif.Values.Count, actualExif.Values.Count);
        Assert.Equal(expectedSoftware, actualExif.GetValue(ExifTag.Software).Value);
    }

    [Theory]
    [WithFile(TestImages.Webp.Lossy.WithExif, PixelTypes.Rgba32)]
    public void EncodeLossyWebp_PreservesExif<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        using Image<TPixel> input = provider.GetImage(WebpDecoder);
        using var memoryStream = new MemoryStream();
        ExifProfile expectedExif = input.Metadata.ExifProfile;

        // act
        input.Save(memoryStream, new WebpEncoder() { FileFormat = WebpFileFormatType.Lossy });
        memoryStream.Position = 0;

        // assert
        using var image = Image.Load<Rgba32>(memoryStream);
        ExifProfile actualExif = image.Metadata.ExifProfile;
        Assert.NotNull(actualExif);
        Assert.Equal(expectedExif.Values.Count, actualExif.Values.Count);
    }

    [Theory]
    [WithFile(TestImages.Webp.Lossless.WithExif, PixelTypes.Rgba32)]
    public void EncodeLosslessWebp_PreservesExif<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        using Image<TPixel> input = provider.GetImage(WebpDecoder);
        using var memoryStream = new MemoryStream();
        ExifProfile expectedExif = input.Metadata.ExifProfile;

        // act
        input.Save(memoryStream, new WebpEncoder() { FileFormat = WebpFileFormatType.Lossless });
        memoryStream.Position = 0;

        // assert
        using var image = Image.Load<Rgba32>(memoryStream);
        ExifProfile actualExif = image.Metadata.ExifProfile;
        Assert.NotNull(actualExif);
        Assert.Equal(expectedExif.Values.Count, actualExif.Values.Count);
    }

    [Theory]
    [WithFile(TestImages.Webp.Lossy.WithExifNotEnoughData, PixelTypes.Rgba32)]
    public void WebpDecoder_IgnoresInvalidExifChunk<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Exception ex = Record.Exception(() =>
        {
            using Image<TPixel> image = provider.GetImage();
        });
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(WebpFileFormatType.Lossy, false, false)]
    [InlineData(WebpFileFormatType.Lossy, true, false)]
    [InlineData(WebpFileFormatType.Lossy, false, true)]
    [InlineData(WebpFileFormatType.Lossy, true, true)]
    [InlineData(WebpFileFormatType.Lossless, false, false)]
    [InlineData(WebpFileFormatType.Lossless, true, false)]
    [InlineData(WebpFileFormatType.Lossless, false, true)]
    [InlineData(WebpFileFormatType.Lossless, true, true)]
    public void Encode_WithExifAndXmp_WritesRiffSizeMatchingFileLength(WebpFileFormatType fileFormatType, bool withExif, bool withXmp)
    {
        // arrange
        using var input = new Image<Rgba32>(16, 16, Color.Red.ToPixel<Rgba32>());
        if (withExif)
        {
            var exifProfile = new ExifProfile();
            exifProfile.SetValue(ExifTag.Software, "ImageSharp");
            input.Metadata.ExifProfile = exifProfile;
        }

        if (withXmp)
        {
            input.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'></x:xmpmeta>"));
        }

        using var memoryStream = new MemoryStream();

        // act
        input.Save(memoryStream, new WebpEncoder() { FileFormat = fileFormatType });
        byte[] data = memoryStream.ToArray();

        // assert
        Assert.Equal((uint)data.Length - 8, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)));
        List<string> chunks = ReadChunkTypes(data);
        Assert.Equal(withExif || withXmp, chunks[0] == "VP8X");
        Assert.Equal(withExif, chunks.Contains("EXIF"));
        Assert.Equal(withXmp, chunks.Contains("XMP "));
        Assert.DoesNotContain("ICCP", chunks);

        memoryStream.Position = 0;
        using var image = Image.Load<Rgba32>(memoryStream);
        Assert.Equal(withExif, image.Metadata.ExifProfile != null);
        Assert.Equal(withXmp, image.Metadata.XmpProfile != null);
        Assert.Null(image.Metadata.IccProfile);
        if (withExif)
        {
            Assert.Equal("ImageSharp", image.Metadata.ExifProfile.GetValue(ExifTag.Software).Value);
        }

        if (withXmp)
        {
            Assert.Equal(input.Metadata.XmpProfile.Data, image.Metadata.XmpProfile.Data);
        }
    }

    [Theory]
    [WithFile(TestImages.Webp.Lossy.WithIccp, PixelTypes.Rgba32, WebpFileFormatType.Lossy)]
    [WithFile(TestImages.Webp.Lossy.WithIccp, PixelTypes.Rgba32, WebpFileFormatType.Lossless)]
    [WithFile(TestImages.Webp.Lossless.WithIccp, PixelTypes.Rgba32, WebpFileFormatType.Lossy)]
    [WithFile(TestImages.Webp.Lossless.WithIccp, PixelTypes.Rgba32, WebpFileFormatType.Lossless)]
    public void Encode_PreservesIccProfile<TPixel>(TestImageProvider<TPixel> provider, WebpFileFormatType fileFormatType)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        using Image<TPixel> input = provider.GetImage(WebpDecoder);
        byte[] expectedIcc = input.Metadata.IccProfile.ToByteArray();
        using var memoryStream = new MemoryStream();

        // act
        input.Save(memoryStream, new WebpEncoder() { FileFormat = fileFormatType });
        byte[] data = memoryStream.ToArray();

        // assert
        Assert.Equal((uint)data.Length - 8, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)));
        List<string> chunks = ReadChunkTypes(data);
        Assert.Equal("VP8X", chunks[0]);
        Assert.Equal("ICCP", chunks[1]);
        Assert.True((data[20] & 0x20) != 0, "VP8X ICC flag is not set.");

        memoryStream.Position = 0;
        using var image = Image.Load<Rgba32>(memoryStream);
        Assert.NotNull(image.Metadata.IccProfile);
        Assert.Equal(expectedIcc, image.Metadata.IccProfile.ToByteArray());
    }

    [Theory]
    [InlineData(WebpFileFormatType.Lossy, false, false, false)]
    [InlineData(WebpFileFormatType.Lossy, true, false, false)]
    [InlineData(WebpFileFormatType.Lossy, false, true, false)]
    [InlineData(WebpFileFormatType.Lossy, false, false, true)]
    [InlineData(WebpFileFormatType.Lossy, true, true, true)]
    [InlineData(WebpFileFormatType.Lossless, false, false, false)]
    [InlineData(WebpFileFormatType.Lossless, true, false, false)]
    [InlineData(WebpFileFormatType.Lossless, false, true, false)]
    [InlineData(WebpFileFormatType.Lossless, false, false, true)]
    [InlineData(WebpFileFormatType.Lossless, true, true, true)]
    public void Encode_WithIccProfile_WritesIccpChunkAfterVp8X(WebpFileFormatType fileFormatType, bool withAlpha, bool withExif, bool withXmp)
    {
        // arrange
        using var input = new Image<Rgba32>(16, 16, Color.Red.ToPixel<Rgba32>());
        if (withAlpha)
        {
            for (int y = 0; y < input.Height; y++)
            {
                input[3, y] = new Rgba32(10, 20, 30, 40);
            }
        }

        input.Metadata.IccProfile = new IccProfile(IccTestDataProfiles.Profile_Random_Array);
        if (withExif)
        {
            var exifProfile = new ExifProfile();
            exifProfile.SetValue(ExifTag.Software, "ImageSharp");
            input.Metadata.ExifProfile = exifProfile;
        }

        if (withXmp)
        {
            input.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'></x:xmpmeta>"));
        }

        using var memoryStream = new MemoryStream();

        // act
        input.Save(memoryStream, new WebpEncoder() { FileFormat = fileFormatType });
        byte[] data = memoryStream.ToArray();

        // assert
        Assert.Equal((uint)data.Length - 8, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)));

        var expectedChunks = new List<string> { "VP8X", "ICCP" };
        if (withAlpha && fileFormatType == WebpFileFormatType.Lossy)
        {
            expectedChunks.Add("ALPH");
        }

        expectedChunks.Add(fileFormatType == WebpFileFormatType.Lossy ? "VP8 " : "VP8L");
        if (withExif)
        {
            expectedChunks.Add("EXIF");
        }

        if (withXmp)
        {
            expectedChunks.Add("XMP ");
        }

        Assert.Equal(expectedChunks, ReadChunkTypes(data));

        byte flags = data[20];
        Assert.Equal(0x20, flags & 0x20);
        Assert.Equal(withAlpha ? 0x10 : 0, flags & 0x10);
        Assert.Equal(withExif ? 0x08 : 0, flags & 0x08);
        Assert.Equal(withXmp ? 0x04 : 0, flags & 0x04);

        memoryStream.Position = 0;
        using var image = Image.Load<Rgba32>(memoryStream);
        Assert.Equal(IccTestDataProfiles.Profile_Random_Array, image.Metadata.IccProfile.ToByteArray());
        Assert.Equal(withExif, image.Metadata.ExifProfile != null);
        Assert.Equal(withXmp, image.Metadata.XmpProfile != null);
        Assert.Equal(255, image[0, 0].A);
        if (withAlpha)
        {
            Assert.True(image[3, 5].A < 255, "Alpha was lost.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decode_OddLengthMetadataChunks_ReadsPayloadWithoutPaddingByte(bool ignoreMetadata)
    {
        // arrange
        byte[] icc = OddLengthIccProfile();
        byte[] exif = OddLengthExifProfile();
        byte[] xmp = Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'>" + new string('a', 953) + "</x:xmpmeta>");
        Assert.Equal(1001, xmp.Length);
        byte[] data = CreateExtendedWebp(icc, exif, xmp);

        // act
        using var image = Image.Load<Rgba32>(data, new WebpDecoder { IgnoreMetadata = ignoreMetadata });

        // assert
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[0, 0]);
        if (ignoreMetadata)
        {
            Assert.Null(image.Metadata.IccProfile);
            Assert.Null(image.Metadata.ExifProfile);
            Assert.Null(image.Metadata.XmpProfile);
        }
        else
        {
            Assert.Equal(icc, image.Metadata.IccProfile.ToByteArray());
            Assert.Equal(exif, image.Metadata.ExifProfile.ToByteArray());
            Assert.Equal(xmp, image.Metadata.XmpProfile.Data);
        }
    }

    /// <summary>
    /// Returns a valid ICC profile whose length, and the size stored in its header, is odd.
    /// </summary>
    private static byte[] OddLengthIccProfile()
    {
        byte[] icc = [.. IccTestDataProfiles.Profile_Random_Array, 0x2A];
        Assert.True(icc.Length % 2 == 1, "The ICC profile length must be odd.");
        BinaryPrimitives.WriteUInt32BigEndian(icc, (uint)icc.Length);
        Assert.True(new IccProfile(icc).CheckIsValid(), "The ICC profile must be valid.");
        return icc;
    }

    /// <summary>
    /// Returns raw EXIF data with an odd length.
    /// </summary>
    private static byte[] OddLengthExifProfile()
    {
        var exifProfile = new ExifProfile();
        exifProfile.SetValue(ExifTag.Software, "ImageSharp");
        byte[] exif = exifProfile.ToByteArray();
        return exif.Length % 2 == 1 ? exif : [.. exif, 0x2A];
    }

    /// <summary>
    /// Builds a 1x1 VP8X WebP file with ICCP, VP8L, EXIF and XMP chunks, padding each odd-length chunk with a zero byte.
    /// </summary>
    private static byte[] CreateExtendedWebp(byte[] icc, byte[] exif, byte[] xmp)
    {
        using var source = new Image<Rgba32>(1, 1, Color.Red.ToPixel<Rgba32>());
        using var sourceStream = new MemoryStream();
        source.Save(sourceStream, new WebpEncoder() { FileFormat = WebpFileFormatType.Lossless });
        byte[] simple = sourceStream.ToArray();
        Assert.Equal("VP8L", Encoding.ASCII.GetString(simple, 12, 4));

        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("RIFF"));
        output.Write(new byte[4]);
        output.Write(Encoding.ASCII.GetBytes("WEBP"));

        // VP8X with the ICC, EXIF and XMP flags, and a 1x1 canvas (width and height minus one are zero).
        WriteChunk(output, "VP8X", [0x2C, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        WriteChunk(output, "ICCP", icc);
        output.Write(simple, 12, simple.Length - 12);
        WriteChunk(output, "EXIF", exif);
        WriteChunk(output, "XMP ", xmp);

        byte[] data = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)data.Length - 8);
        return data;
    }

    private static void WriteChunk(Stream stream, string fourCc, byte[] payload)
    {
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)payload.Length);
        stream.Write(Encoding.ASCII.GetBytes(fourCc));
        stream.Write(size);
        stream.Write(payload);
        if (payload.Length % 2 == 1)
        {
            stream.WriteByte(0);
        }
    }

    /// <summary>
    /// Walks the chunks of a RIFF WebP file and returns their FourCCs in order.
    /// Fails if a chunk does not fit inside the file.
    /// </summary>
    private static List<string> ReadChunkTypes(byte[] data)
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(data, 8, 4));

        var chunks = new List<string>();
        int offset = 12;
        while (offset < data.Length)
        {
            Assert.True(offset + 8 <= data.Length, "Truncated chunk header.");
            string fourCc = Encoding.ASCII.GetString(data, offset, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            long next = offset + 8L + size + (size & 1);
            Assert.True(next <= data.Length, $"Chunk {fourCc} runs past the end of the file.");
            chunks.Add(fourCc);
            offset = (int)next;
        }

        return chunks;
    }
}
