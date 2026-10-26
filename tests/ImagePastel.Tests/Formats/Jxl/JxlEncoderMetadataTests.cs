// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The Exif and XMP boxes the encoder writes in a JPEG XL container (E6b, #332), checked through
/// <c>SaveAsJxl</c> with <see cref="Configuration.Default"/>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderMetadataTests
{
    private const string Xmp =
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
        + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:creator>E6b</dc:creator>"
        + "</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";

    [Fact]
    public void SaveAsJxl_ExifAndXmp_WritesLibjxlsBoxLayout()
    {
        using Image<Rgba32> image = CreateImage(withExif: true, withXmp: true);

        IReadOnlyList<JxlBox> boxes = JxlBoxList.Read(JxlEncoderRoundTrip.SaveAsJxl(image));

        Assert.Equal(new[] { "JXL ", "ftyp", "jxlp", "Exif", "xml ", "jxlp" }, JxlBoxList.Types(boxes));
        Assert.All(boxes, b => Assert.Equal(8, b.HeaderSize));

        // MakeContainerHeader: the signature, then major brand "jxl ", version 0, compatible brand "jxl ".
        Assert.Equal(new byte[] { 0x0D, 0x0A, 0x87, 0x0A }, boxes[0].Contents);
        Assert.Equal("jxl \0\0\0\0jxl "u8.ToArray(), boxes[1].Contents);
        Assert.Equal(0u, JxlBoxList.JxlpIndex(boxes[2]));
        Assert.Equal(0x80000001u, JxlBoxList.JxlpIndex(boxes[5]));

        // The Exif box: a TIFF-header offset of 0, then the profile.
        Assert.Equal(new byte[4], boxes[3].Contents[..4]);
        Assert.Equal(image.Metadata.ExifProfile.ToByteArray(), boxes[3].Contents[4..]);
        Assert.Equal(image.Metadata.XmpProfile.ToByteArray(), boxes[4].Contents);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void SaveAsJxl_ExifAndXmp_OurDecoderReadsTheProfilesBack(float distance)
    {
        using Image<Rgba32> image = CreateImage(withExif: true, withXmp: true);
        byte[] encoded = JxlEncoderRoundTrip.SaveAsJxl(image, new JxlEncoder { Distance = distance });

        // Image.Load with Configuration.Default reads .jxl with our decoder.
        using Image<Rgba32> decoded = Image.Load<Rgba32>(encoded);

        // SaveAsJxl synchronised the source's profile, as every encoder does.
        Assert.NotNull(decoded.Metadata.ExifProfile);
        Assert.Equal(image.Metadata.ExifProfile.ToByteArray(), decoded.Metadata.ExifProfile.ToByteArray());
        Assert.NotNull(decoded.Metadata.XmpProfile);
        Assert.Equal(image.Metadata.XmpProfile.ToByteArray(), decoded.Metadata.XmpProfile.ToByteArray());
    }

    // MagickReferenceDecoder drops metadata, so the profiles are read through MagickImage itself.
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void SaveAsJxl_ExifAndXmp_MagickReadsTheProfilesBack(float distance)
    {
        JxlEncoderRoundTrip.SkipUnlessReaderRuns(JxlEncoderRoundTrip.Reader.Magick);
        using Image<Rgba32> image = CreateImage(withExif: true, withXmp: true);
        byte[] encoded = JxlEncoderRoundTrip.SaveAsJxl(image, new JxlEncoder { Distance = distance });

        using ImageMagick.MagickImage magick = new(encoded);

        ImageMagick.IExifProfile magickExif = magick.GetExifProfile();
        Assert.NotNull(magickExif);

        // ImageMagick keeps Exif as a JPEG APP1 segment does: "Exif\0\0" in place of the box's 4-byte offset.
        byte[] magickExifBytes = magickExif.ToByteArray();
        Assert.Equal("Exif\0\0"u8.ToArray(), magickExifBytes[..6]);
        ExifProfile exif = new(magickExifBytes[6..]);
        ExifProfile expected = image.Metadata.ExifProfile;
        Assert.Equal(expected.Values.Count, exif.Values.Count);
        foreach (IExifValue value in expected.Values)
        {
            Assert.Equal(value.GetValue(), exif.GetValueInternal(value.Tag)?.GetValue());
        }

        Assert.Equal(image.Metadata.XmpProfile.ToByteArray(), magick.GetXmpProfile()?.ToByteArray());
    }

    // cjxl v0.12.0 with --compress_boxes=0 on the same image saved as PNG: the same boxes in the same order,
    // the same ftyp, and the same Exif offset and data. Then jxlinfo reads our file's boxes.
    [Fact]
    public async Task SaveAsJxl_ExifAndXmp_MatchesCjxlsBoxLayout()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        JxlTools.GetToolPath("cjxl");
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlEncoderMetadataTests));
        string pngPath = Path.Combine(directory, "ExifAndXmp.png");
        string cjxlPath = Path.Combine(directory, "ExifAndXmp.cjxl.jxl");
        string oursPath = Path.Combine(directory, "ExifAndXmp.jxl");

        using Image<Rgba32> image = CreateImage(withExif: true, withXmp: true);
        await image.SaveAsPngAsync(pngPath, cancellationToken);
        await image.SaveAsJxlAsync(oursPath, cancellationToken);
        File.Delete(cjxlPath);
        await JxlTools.RunAsync("cjxl", cancellationToken, pngPath, cjxlPath, "-d", "0", "-e", "1", "--compress_boxes=0");

        IReadOnlyList<JxlBox> ours = JxlBoxList.Read(await File.ReadAllBytesAsync(oursPath, cancellationToken));
        IReadOnlyList<JxlBox> cjxl = JxlBoxList.Read(await File.ReadAllBytesAsync(cjxlPath, cancellationToken));

        Assert.Equal(JxlBoxList.Types(cjxl), JxlBoxList.Types(ours));
        Assert.Equal(cjxl[1].Contents, ours[1].Contents);
        Assert.Equal(JxlBoxList.JxlpIndex(cjxl[2]), JxlBoxList.JxlpIndex(ours[2]));
        Assert.Equal(JxlBoxList.JxlpIndex(cjxl[5]), JxlBoxList.JxlpIndex(ours[5]));
        Assert.Equal(cjxl[3].Contents, ours[3].Contents);
        Assert.Equal(cjxl[4].Contents, ours[4].Contents);

        (int exitCode, string output) = await JxlTools.RunForExitCodeAsync("jxlinfo", cancellationToken, oursPath);
        Assert.True(exitCode == 0, output);

        // jxlinfo gives the size of the whole box, its 8-byte header included.
        Assert.Contains($"Uncompressed Exif metadata: {8 + ours[3].Contents.Length} bytes", output);
        Assert.Contains($"Uncompressed xml  metadata: {8 + ours[4].Contents.Length} bytes", output);
    }

    [Theory]
    [InlineData(Profiles.None, new string[0])]
    [InlineData(Profiles.Exif, new[] { "JXL ", "ftyp", "jxlp", "Exif", "jxlp" })]
    [InlineData(Profiles.Xmp, new[] { "JXL ", "ftyp", "jxlp", "xml ", "jxlp" })]
    [InlineData(Profiles.Exif | Profiles.Xmp, new[] { "JXL ", "ftyp", "jxlp", "Exif", "xml ", "jxlp" })]
    [InlineData(Profiles.EmptyExif, new string[0])]
    [InlineData(Profiles.ExifWithoutTiffHeader, new string[0])]
    [InlineData(Profiles.EmptyXmp, new string[0])]
    [InlineData(Profiles.EmptyExif | Profiles.Xmp, new[] { "JXL ", "ftyp", "jxlp", "xml ", "jxlp" })]
    public void SaveAsJxl_Profiles_GiveTheirBoxesAroundTheBareCodestream(Profiles profiles, string[] expectedBoxes)
    {
        foreach (float distance in new[] { 0f, 1f })
        {
            JxlEncoder encoder = new() { Distance = distance };
            using Image<Rgba32> bare = JxlEncoderRoundTrip.CreateImage<Rgba32>("gradient", 17, 23);
            using Image<Rgba32> image = bare.Clone();
            SetProfiles(image, profiles);

            byte[] expectedCodestream = JxlEncoderRoundTrip.SaveAsJxl(bare, encoder);
            byte[] encoded = JxlEncoderRoundTrip.SaveAsJxl(image, encoder);

            if (expectedBoxes.Length == 0)
            {
                // No box: the bare codestream, byte for byte.
                Assert.Equal(expectedCodestream, encoded);
            }
            else
            {
                Assert.Equal(expectedBoxes, JxlBoxList.Types(JxlBoxList.Read(encoded)));

                // The codestream that JxlContainer.Read joins is the bare encode's, so no codestream pin moves.
                Assert.Equal(expectedCodestream, JxlContainer.Read(encoded, readMetadata: false, TestContext.Current.CancellationToken).Codestream);
            }

            using Image<Rgba32> expectedPixels = Image.Load<Rgba32>(expectedCodestream);
            using Image<Rgba32> decoded = Image.Load<Rgba32>(encoded);
            ImageComparer.Exact.VerifySimilarity(expectedPixels, decoded);
        }
    }

    [Fact]
    public void SaveAsJxl_XmpOverTheDecodersCap_ThrowsAndWritesNothing()
    {
        using Image<Rgba32> image = JxlEncoderRoundTrip.CreateImage<Rgba32>("gradient", 17, 23);
        image.Metadata.XmpProfile = new XmpProfile(new byte[JxlContainer.BlockSizeLimit + 1]);
        using MemoryStream stream = new();

        ImageFormatException ex = Assert.Throws<ImageFormatException>(() => image.SaveAsJxl(stream));

        Assert.Contains("XMP", ex.Message);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void SaveAsJxl_XmpAtTheDecodersCap_IsWrittenAndReadBack()
    {
        byte[] xmp = new byte[JxlContainer.BlockSizeLimit];
        xmp[0] = (byte)'<';
        xmp[^1] = (byte)'>';
        using Image<Rgba32> image = JxlEncoderRoundTrip.CreateImage<Rgba32>("gradient", 17, 23);
        image.Metadata.XmpProfile = new XmpProfile(xmp);

        IImageInfo info = Image.Identify(JxlEncoderRoundTrip.SaveAsJxl(image));

        Assert.Equal(xmp, info.Metadata.XmpProfile?.ToByteArray());
    }

    /// <summary>The profiles an image carries.</summary>
    [Flags]
    public enum Profiles
    {
        /// <summary>No profile.</summary>
        None = 0,

        /// <summary>An Exif profile with values.</summary>
        Exif = 1,

        /// <summary>An XMP profile.</summary>
        Xmp = 2,

        /// <summary>An Exif profile with no values.</summary>
        EmptyExif = 4,

        /// <summary>An Exif profile made from bytes that do not start with a TIFF header.</summary>
        ExifWithoutTiffHeader = 8,

        /// <summary>An XMP profile with no bytes.</summary>
        EmptyXmp = 16,
    }

    private static void SetProfiles(Image image, Profiles profiles)
    {
        if (profiles.HasFlag(Profiles.Exif))
        {
            ExifProfile exif = new();
            exif.SetValue(ExifTag.Software, "E6b");
            exif.SetValue(ExifTag.Copyright, "PinataLabs");
            image.Metadata.ExifProfile = exif;
        }

        if (profiles.HasFlag(Profiles.EmptyExif))
        {
            image.Metadata.ExifProfile = new ExifProfile();
        }

        if (profiles.HasFlag(Profiles.ExifWithoutTiffHeader))
        {
            // The JPEG APP1 marker's "Exif\0\0" in front of the TIFF header.
            ExifProfile exif = new();
            exif.SetValue(ExifTag.Software, "E6b");
            image.Metadata.ExifProfile = new ExifProfile([.. "Exif\0\0"u8, .. exif.ToByteArray()]);
        }

        if (profiles.HasFlag(Profiles.Xmp))
        {
            image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes(Xmp));
        }

        if (profiles.HasFlag(Profiles.EmptyXmp))
        {
            image.Metadata.XmpProfile = new XmpProfile(Array.Empty<byte>());
        }
    }

    private static Image<Rgba32> CreateImage(bool withExif, bool withXmp)
    {
        Image<Rgba32> image = JxlEncoderRoundTrip.CreateImage<Rgba32>("gradient", 17, 23);
        SetProfiles(image, (withExif ? Profiles.Exif : Profiles.None) | (withXmp ? Profiles.Xmp : Profiles.None));
        return image;
    }
}
