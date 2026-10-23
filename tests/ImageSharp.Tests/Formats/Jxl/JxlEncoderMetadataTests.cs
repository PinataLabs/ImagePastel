// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Text;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
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
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
        + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:creator>E6b</dc:creator>"
        + "</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";

    [Fact]
    public void SaveAsJxl_ExifAndXmp_WritesLibjxlsBoxLayout()
    {
        using Image<Rgba32> image = CreateImage(withExif: true, withXmp: true);

        IReadOnlyList<JxlBox> boxes = JxlBoxList.Read(Save(image));

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

    private static Image<Rgba32> CreateImage(bool withExif, bool withXmp)
    {
        Image<Rgba32> image = JxlEncoderRoundTrip.CreateImage<Rgba32>("gradient", 17, 23);
        if (withExif)
        {
            ExifProfile exif = new();
            exif.SetValue(ExifTag.Software, "E6b");
            exif.SetValue(ExifTag.Copyright, "PinataLabs");
            image.Metadata.ExifProfile = exif;
        }

        if (withXmp)
        {
            image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes(Xmp));
        }

        return image;
    }

    private static byte[] Save(Image image, JxlEncoder encoder = null)
    {
        using MemoryStream stream = new();
        if (encoder is null)
        {
            image.SaveAsJxl(stream);
        }
        else
        {
            image.SaveAsJxl(stream, encoder);
        }

        return stream.ToArray();
    }
}
