// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Checks the metadata a JPEG XL file reports: Exif and XMP from its boxes, and its ICC profile.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlMetadataTests
{
    private static string GeneratedDirectory => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated");

    private static string JpegReconstructionDirectory => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "libjxl", "jxl", "jpeg_reconstruction");

    [Fact]
    public void Identify_ExifAndXmpBoxes_GiveTheProfilesOfTheJpegTheyCameFrom()
    {
        ImageMetadata expected = Image.Identify(Path.Combine(JpegReconstructionDirectory, "1x1_exif_xmp.jpg")).Metadata;

        ImageMetadata metadata = Identify(Path.Combine(JpegReconstructionDirectory, "1x1_exif_xmp.jxl"));

        AssertSameProfiles(expected, metadata);
    }

    [Fact]
    public void Load_ExifAndXmp_GivesTheProfilesAndThePixels()
    {
        ImageMetadata expected = Image.Identify(Path.Combine(JpegReconstructionDirectory, "1x1_exif_xmp.jpg")).Metadata;

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), Path.Combine(GeneratedDirectory, "exif_xmp.jxl"));
        using Image<Rgb24> reference = Image.Load<Rgb24>(GeneratedReferencePath("exif_xmp"));

        ImageComparer.Exact.VerifySimilarity(reference, image);
        AssertSameProfiles(expected, image.Metadata);
    }

    [Fact]
    public void Identify_BrotliCompressedBoxes_GiveTheSameProfiles()
    {
        ImageMetadata expected = Identify(Path.Combine(GeneratedDirectory, "exif_xmp.jxl"));

        ImageMetadata metadata = Identify(Path.Combine(GeneratedDirectory, "exif_xmp_brob.jxl"));

        Assert.NotNull(expected.ExifProfile);
        AssertSameProfiles(expected, metadata);
    }

    [Theory]
    [InlineData("exif_xmp")]
    [InlineData("exif_xmp_brob")]
    public void Decode_IgnoreMetadata_LeavesTheProfilesUnset(string name)
    {
        using FileStream stream = File.OpenRead(Path.Combine(GeneratedDirectory, name + ".jxl"));

        using Image<Rgb24> image = new JxlDecoder { IgnoreMetadata = true }.Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None);

        Assert.Null(image.Metadata.ExifProfile);
        Assert.Null(image.Metadata.XmpProfile);
    }

    [Theory]
    [InlineData(1000u)] // the TIFF header offset points past the box
    [InlineData(4u)] // the bytes at the offset are not a TIFF header
    public void Identify_ExifBoxWithoutATiffHeaderAtItsOffset_GivesNoExifProfile(uint offset)
    {
        // libjxl's djxl warns and keeps the file's other data; ImageSharp has no profile for bytes
        // that are not Exif.
        byte[] exif = new byte[4 + 8 + 16];
        BinaryPrimitives.WriteUInt32BigEndian(exif, offset);
        Encoding.ASCII.GetBytes("II*\0").CopyTo(exif, 4);

        ImageMetadata metadata = Identify(WithBoxes(Box("Exif", exif)));

        Assert.Null(metadata.ExifProfile);
    }

    [Fact]
    public void Identify_ExifBoxWithAnOffset_SkipsTheBytesBeforeTheTiffHeader()
    {
        ImageMetadata metadata = Identify(WithBoxes(Box("Exif", ExifBox("D4b", offset: 3))));

        Assert.Equal("D4b", metadata.ExifProfile.GetValue(ExifTag.Software).Value);
    }

    [Fact]
    public void Identify_TwoExifBoxes_GivesTheLast()
    {
        // djxl keeps the last box of each kind.
        ImageMetadata metadata = Identify(WithBoxes(Box("Exif", ExifBox("first")), Box("Exif", ExifBox("last"))));

        Assert.Equal("last", metadata.ExifProfile.GetValue(ExifTag.Software).Value);
    }

    [Fact]
    public void Identify_CorruptBrotliBox_ThrowsInvalidImageContent()
    {
        byte[] contents = new byte[4 + 16];
        Encoding.ASCII.GetBytes("Exif").CopyTo(contents, 0);
        contents.AsSpan(4).Fill(0xFF);

        Assert.Throws<InvalidImageContentException>(() => Identify(WithBoxes(Box("brob", contents))));
    }

    [Fact]
    public void Identify_PatchesLossless_ReportsItsExifAndXmp()
    {
        ImageMetadata metadata = Identify(JxlConformanceVector.Load("patches_lossless").CommittedInputPath);

        Assert.NotNull(metadata.ExifProfile);
        Assert.NotNull(metadata.XmpProfile);
    }

    public static TheoryData<string> VectorsWithAnOriginalIcc()
    {
        TheoryData<string> data = new();
        foreach (string id in JxlConformanceVector.AllIds)
        {
            JxlConformanceVector vector = JxlConformanceVector.Load(id);
            if (vector.Sha256Sums.ContainsKey("original.icc") && File.Exists(vector.CommittedInputPath))
            {
                data.Add(id);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(VectorsWithAnOriginalIcc))]
    public void Identify_EmbeddedIcc_IsTheVectorsOriginalIcc(string id)
    {
        JxlConformanceVector vector = JxlConformanceVector.Load(id);

        JxlMetadata jxl = Identify(vector.CommittedInputPath).GetFormatMetadata(JxlFormat.Instance);

        if (EmbedsIcc(vector.CommittedInputPath))
        {
            Assert.NotNull(jxl.OriginalIccProfile);
            Assert.Equal(vector.Sha256Sums["original.icc"], Sha256(jxl.OriginalIccProfile.ToByteArray()));
        }
        else
        {
            Assert.Null(jxl.OriginalIccProfile);
        }
    }

    // jxlinfo calls the XYB vectors lossy; cmyk_layers is CMYK.
    [Theory]
    [InlineData("bench_oriented_brg", true)]
    [InlineData("cmyk_layers", false)]
    [InlineData("grayscale", false)]
    [InlineData("grayscale_jpeg", true)]
    [InlineData("patches", false)]
    [InlineData("patches_lossless", true)]
    [InlineData("spot", true)]
    public void Identify_EmbeddedIcc_IsThePixelProfileUnlessXybOrCmyk(string id, bool isPixelProfile)
    {
        ImageMetadata metadata = Identify(JxlConformanceVector.Load(id).CommittedInputPath);
        JxlMetadata jxl = metadata.GetFormatMetadata(JxlFormat.Instance);

        Assert.NotNull(jxl.OriginalIccProfile);
        if (isPixelProfile)
        {
            Assert.Equal(jxl.OriginalIccProfile.ToByteArray(), metadata.IccProfile.ToByteArray());
        }
        else
        {
            Assert.Null(metadata.IccProfile);
        }
    }

    [Fact]
    public void Load_EmbeddedIcc_GivesThePixelsAndTheProfileDjxlGives()
    {
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), Path.Combine(GeneratedDirectory, "custom_icc.jxl"));
        using Image<Rgb24> reference = Image.Load<Rgb24>(GeneratedReferencePath("custom_icc"));

        ImageComparer.Exact.VerifySimilarity(reference, image);
        Assert.Equal(reference.Metadata.IccProfile.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
        Assert.Equal(reference.Metadata.IccProfile.ToByteArray(), image.Metadata.GetFormatMetadata(JxlFormat.Instance).OriginalIccProfile.ToByteArray());
    }

    [Fact]
    public void Decode_EmbeddedIcc_IgnoreMetadata_LeavesTheProfilesUnsetAndStillDecodes()
    {
        using FileStream stream = File.OpenRead(Path.Combine(GeneratedDirectory, "custom_icc.jxl"));

        using Image<Rgb24> image = new JxlDecoder { IgnoreMetadata = true }.Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None);
        using Image<Rgb24> reference = Image.Load<Rgb24>(GeneratedReferencePath("custom_icc"));

        ImageComparer.Exact.VerifySimilarity(reference, image);
        Assert.Null(image.Metadata.IccProfile);
        Assert.Null(image.Metadata.GetFormatMetadata(JxlFormat.Instance).OriginalIccProfile);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(100)]
    public void Identify_TruncatedIccStream_ThrowsInvalidImageContent(int length)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(GeneratedDirectory, "custom_icc.jxl")).AsSpan(0, length).ToArray();

        Assert.Throws<InvalidImageContentException>(() => Identify(data));
    }

    [Fact]
    public void DeepClone_CopiesTheOriginalIccProfile()
    {
        JxlMetadata jxl = Identify(Path.Combine(GeneratedDirectory, "custom_icc.jxl")).GetFormatMetadata(JxlFormat.Instance);

        JxlMetadata clone = (JxlMetadata)jxl.DeepClone();

        Assert.NotSame(jxl.OriginalIccProfile, clone.OriginalIccProfile);
        Assert.Equal(jxl.OriginalIccProfile.ToByteArray(), clone.OriginalIccProfile.ToByteArray());
    }

    // Whether the codestream header signals an embedded ICC profile.
    private static bool EmbedsIcc(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.AsSpan().StartsWith(JxlConstants.ContainerSignature))
        {
            data = JxlContainer.Read(data, readMetadata: false).Codestream;
        }

        JxlCodecMetadata metadata = new();
        JxlBitReader reader = new(data, 2, data.Length - 2);
        JxlBundle.Read(reader, metadata.Size);
        JxlBundle.Read(reader, metadata.M);
        reader.Close();
        return metadata.M.ColorEncoding.WantIcc;
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static void AssertSameProfiles(ImageMetadata expected, ImageMetadata actual)
    {
        Assert.NotNull(actual.ExifProfile);
        Assert.Equal(expected.ExifProfile.ToByteArray(), actual.ExifProfile.ToByteArray());
        Assert.NotNull(actual.XmpProfile);
        Assert.Equal(expected.XmpProfile.ToByteArray(), actual.XmpProfile.ToByteArray());
    }

    private static ImageMetadata Identify(string path) => Identify(File.ReadAllBytes(path));

    private static ImageMetadata Identify(byte[] data)
    {
        using MemoryStream stream = new(data);
        return new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None).Metadata;
    }

    // delta_palette's codestream in a container, with the boxes before it.
    private static byte[] WithBoxes(params byte[][] boxes)
    {
        byte[] codestream = File.ReadAllBytes(JxlConformanceVector.Load("delta_palette").CommittedInputPath);
        byte[] ftyp = new byte[12];
        Encoding.ASCII.GetBytes("jxl ").CopyTo(ftyp, 0);
        Encoding.ASCII.GetBytes("jxl ").CopyTo(ftyp, 8);

        List<byte> data = new(JxlConstants.ContainerSignature);
        data.AddRange(Box("ftyp", ftyp));
        foreach (byte[] box in boxes)
        {
            data.AddRange(box);
        }

        data.AddRange(Box("jxlc", codestream));
        return data.ToArray();
    }

    // An Exif box: the TIFF header offset, that many bytes, then a profile whose Software tag is the
    // given text.
    private static byte[] ExifBox(string software, uint offset = 0)
    {
        ExifProfile profile = new();
        profile.SetValue(ExifTag.Software, software);
        byte[] tiff = profile.ToByteArray();
        byte[] exif = new byte[4 + offset + tiff.Length];
        BinaryPrimitives.WriteUInt32BigEndian(exif, offset);
        tiff.CopyTo(exif, 4 + (int)offset);
        return exif;
    }

    private static byte[] Box(string type, byte[] contents)
    {
        byte[] box = new byte[8 + contents.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        contents.CopyTo(box, 8);
        return box;
    }

    private static string GeneratedReferencePath(string name) => Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".png");

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
