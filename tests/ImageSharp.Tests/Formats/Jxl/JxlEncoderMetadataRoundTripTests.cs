// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Tests.TestUtilities.ReferenceCodecs;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// E6's done-check (#335): an image with Exif (with an orientation), XMP and an ICC profile, saved through
/// <c>SaveAsJxl</c> with <see cref="Configuration.Default"/>, lossless and lossy, with and without alpha,
/// then read back through each reader.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderMetadataRoundTripTests
{
    private const int Width = 37;
    private const int Height = 23;

    // Orientation 6 (rotate 90° clockwise): the oriented image is Height x Width.
    private const ushort Orientation = 6;

    private const string Xmp =
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
        + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:creator>E6e</dc:creator>"
        + "</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";

    /// <summary>Gets the distance and whether the pixels have alpha: lossless and lossy, with and without alpha.</summary>
    public static TheoryData<float, bool> Cases { get; } = new()
    {
        { 0f, false },
        { 0f, true },
        { 1f, false },
        { 1f, true },
    };

    // Our decoder applies the codestream orientation and resets the Exif orientation to 1 (D4). The other
    // Exif values and the XMP bytes come back as written. Lossless keeps the ICC profile byte for byte;
    // lossy writes the colour encoding the profile describes, and the decoder attaches a profile it builds
    // for that encoding (#115).
    [Theory]
    [MemberData(nameof(Cases))]
    public void SaveAsJxl_Metadata_OurDecoderGivesItBackWithTheOrientedPixels(float distance, bool alpha)
    {
        using Image image = CreateImage(alpha);
        byte[] encoded = JxlEncoderRoundTrip.SaveAsJxl(image, new JxlEncoder { Distance = distance });

        using Image<Rgba32> decoded = Image.Load<Rgba32>(encoded);

        ExifProfile exif = decoded.Metadata.ExifProfile;
        Assert.NotNull(exif);
        Assert.Equal((ushort)1, exif.GetValue(ExifTag.Orientation).Value);
        AssertExifValuesExceptOrientation(image.Metadata.ExifProfile, exif);
        Assert.Equal(image.Metadata.XmpProfile.ToByteArray(), decoded.Metadata.XmpProfile?.ToByteArray());

        JxlMetadata jxlMetadata = decoded.Metadata.GetFormatMetadata(JxlFormat.Instance);
        byte[] icc = image.Metadata.IccProfile.ToByteArray();
        if (distance == 0)
        {
            Assert.Equal(icc, jxlMetadata.EmbeddedIccProfile?.ToByteArray());
            Assert.Equal(icc, decoded.Metadata.IccProfile?.ToByteArray());
        }
        else
        {
            Assert.Null(jxlMetadata.EmbeddedIccProfile);
            Assert.NotNull(decoded.Metadata.IccProfile);
            AssertSameColourEncoding(image.Metadata.IccProfile, decoded.Metadata.IccProfile);
        }

        AssertOrientedPixels(image, decoded, distance, "our decoder");
    }

    // MagickReferenceDecoder drops metadata, so the profiles and the orientation are read through MagickImage
    // itself. Magick keeps the Exif as it is in the box, orientation included, and reports the orientation.
    // It gets the ICC profile of a lossless file byte for byte; for a lossy file libjxl gives a profile it
    // builds for the colour encoding, which is not compared. The images are small, so this runs under
    // coverage too, and skips only where Magick.NET has no JPEG XL delegate.
    [Theory]
    [MemberData(nameof(Cases))]
    public void SaveAsJxl_Metadata_MagickGivesItBack(float distance, bool alpha)
    {
        MagickReferenceDecoder.SkipUnlessJxlIsSupported();
        using Image image = CreateImage(alpha);
        byte[] encoded = JxlEncoderRoundTrip.SaveAsJxl(image, new JxlEncoder { Distance = distance });

        using ImageMagick.MagickImage magick = new(encoded);

        Assert.Equal(ImageMagick.Orientation.RightTop, magick.Orientation);

        // ImageMagick keeps Exif as a JPEG APP1 segment does: "Exif\0\0" in place of the box's 4-byte offset.
        byte[] magickExif = magick.GetExifProfile()?.ToByteArray();
        Assert.NotNull(magickExif);
        Assert.Equal("Exif\0\0"u8.ToArray(), magickExif[..6]);
        ExifProfile exif = new(magickExif[6..]);
        Assert.Equal(Orientation, exif.GetValue(ExifTag.Orientation).Value);
        AssertExifValuesExceptOrientation(image.Metadata.ExifProfile, exif);

        Assert.Equal(image.Metadata.XmpProfile.ToByteArray(), magick.GetXmpProfile()?.ToByteArray());

        if (distance == 0)
        {
            Assert.Equal(image.Metadata.IccProfile.ToByteArray(), magick.GetColorProfile()?.ToByteArray());
        }
    }

    // djxl applies the codestream orientation, as libjxl's decoder does by default.
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task SaveAsJxl_Metadata_DjxlGivesTheOrientedPixels(float distance, bool alpha)
    {
        JxlEncoderRoundTrip.SkipUnlessReaderRuns(JxlEncoderRoundTrip.Reader.Djxl);
        using Image image = CreateImage(alpha);
        byte[] encoded = JxlEncoderRoundTrip.SaveAsJxl(image, new JxlEncoder { Distance = distance });

        using Image<Rgba32> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgba32>(JxlEncoderRoundTrip.Reader.Djxl, encoded, nameof(JxlEncoderMetadataRoundTripTests), CaseName(distance, alpha));

        AssertOrientedPixels(image, decoded, distance, "djxl");
    }

    // jxlinfo lists our boxes with their sizes, which it gives with the 8-byte box header, and the
    // codestream orientation.
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task SaveAsJxl_Metadata_JxlinfoListsTheBoxes(float distance, bool alpha)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        JxlTools.GetToolPath("jxlinfo");
        string path = Path.Combine(OutputDirectory(), CaseName(distance, alpha) + ".jxl");
        using Image image = CreateImage(alpha);
        byte[] encoded = JxlEncoderRoundTrip.SaveAsJxl(image, new JxlEncoder { Distance = distance });
        await File.WriteAllBytesAsync(path, encoded, cancellationToken);
        IReadOnlyList<JxlBox> boxes = JxlBoxList.Read(encoded);

        (int exitCode, string output) = await JxlTools.RunForExitCodeAsync("jxlinfo", cancellationToken, "-v", path);

        Assert.True(exitCode == 0, output);
        Assert.Contains($"Uncompressed Exif metadata: {8 + boxes.Single(b => b.Type == "Exif").Contents.Length} bytes", output);
        Assert.Contains($"Uncompressed xml  metadata: {8 + boxes.Single(b => b.Type == "xml ").Contents.Length} bytes", output);
        Assert.Contains($"Orientation: {Orientation} (", output);
    }

    // cjxl v0.12.0 with --compress_boxes=0 on the same image saved as PNG (which keeps the Exif, the XMP and
    // the ICC profile): the same boxes in the same order, the same ftyp and jxlp indexes, and the same Exif
    // and XMP contents.
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task SaveAsJxl_Metadata_MatchesCjxlsBoxLayout(float distance, bool alpha)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        JxlTools.GetToolPath("cjxl");
        string directory = OutputDirectory();
        string name = CaseName(distance, alpha);
        string pngPath = Path.Combine(directory, name + ".png");
        string cjxlPath = Path.Combine(directory, name + ".cjxl.jxl");
        using Image image = CreateImage(alpha);
        byte[] encoded = JxlEncoderRoundTrip.SaveAsJxl(image, new JxlEncoder { Distance = distance });
        await image.SaveAsPngAsync(pngPath, cancellationToken);
        File.Delete(cjxlPath);

        await JxlTools.RunAsync("cjxl", cancellationToken, pngPath, cjxlPath, "-d", distance.ToString(CultureInfo.InvariantCulture), "-e", "1", "--compress_boxes=0");

        IReadOnlyList<JxlBox> ours = JxlBoxList.Read(encoded);
        IReadOnlyList<JxlBox> cjxl = JxlBoxList.Read(await File.ReadAllBytesAsync(cjxlPath, cancellationToken));
        Assert.Equal(new[] { "JXL ", "ftyp", "jxlp", "Exif", "xml ", "jxlp" }, JxlBoxList.Types(ours));
        Assert.Equal(JxlBoxList.Types(cjxl), JxlBoxList.Types(ours));
        Assert.Equal(cjxl[1].Contents, ours[1].Contents);
        Assert.Equal(JxlBoxList.JxlpIndex(cjxl[2]), JxlBoxList.JxlpIndex(ours[2]));
        Assert.Equal(cjxl[3].Contents, ours[3].Contents);
        Assert.Equal(cjxl[4].Contents, ours[4].Contents);
        Assert.Equal(JxlBoxList.JxlpIndex(cjxl[5]), JxlBoxList.JxlpIndex(ours[5]));
    }

    internal static Image CreateImage(bool alpha)
    {
        Image image = alpha
            ? JxlEncoderRoundTrip.CreateImage<Rgba32>("translucent", Width, Height)
            : JxlEncoderRoundTrip.CreateImage<Rgb24>("gradient", Width, Height);

        ExifProfile exif = new();
        exif.SetValue(ExifTag.Orientation, Orientation);
        exif.SetValue(ExifTag.Make, "PinataLabs");
        exif.SetValue(ExifTag.Model, "E6e");
        exif.SetValue(ExifTag.Software, "ImageSharp");
        exif.SetValue(ExifTag.Copyright, "PinataLabs");
        exif.SetValue(ExifTag.DateTimeOriginal, "2026:10:23 12:34:56");
        exif.SetValue(ExifTag.ExposureTime, new Rational(1, 250));
        exif.SetValue(ExifTag.ISOSpeedRatings, new ushort[] { 200 });
        image.Metadata.ExifProfile = exif;
        image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes(Xmp));
        image.Metadata.IccProfile = new IccProfile(File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Icc.CompactDisplayP3V4)));
        return image;
    }

    // Every value of the source's Exif (which SaveAsJxl synchronised, as every encoder does), except the
    // orientation, with the same value.
    internal static void AssertExifValuesExceptOrientation(ExifProfile expected, ExifProfile actual)
    {
        Assert.Equal(expected.Values.Count, actual.Values.Count);
        foreach (IExifValue value in expected.Values)
        {
            if (value.Tag != ExifTag.Orientation)
            {
                Assert.Equal(value.GetValue(), actual.GetValueInternal(value.Tag)?.GetValue());
            }
        }
    }

    // The decoded pixels against the source after AutoOrient: exactly for lossless; for lossy, the colour
    // within the interim bound and the alpha exactly (alpha is lossless at any distance).
    internal static void AssertOrientedPixels(Image image, Image<Rgba32> decoded, float distance, string reader)
    {
        using Image<Rgba32> expected = image.CloneAs<Rgba32>();
        expected.Mutate(x => x.AutoOrient());
        Assert.Equal(Height, expected.Width);
        Assert.Equal(Width, expected.Height);

        if (distance == 0)
        {
            ImageComparer.Exact.VerifySimilarity(expected, decoded);
            return;
        }

        using Image<Rgb24> expectedColour = expected.CloneAs<Rgb24>();
        using Image<Rgb24> decodedColour = decoded.CloneAs<Rgb24>();
        JxlLossyEncoderTests.AssertWithinInterimBound(expectedColour, decodedColour, $"metadata round trip, d{distance}, through {reader}");
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected[x, y].A, decoded[x, y].A);
            }
        }
    }

    private static string CaseName(float distance, bool alpha) => $"d{distance.ToString(CultureInfo.InvariantCulture)}_{(alpha ? "alpha" : "opaque")}";

    private static string OutputDirectory() => TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlEncoderMetadataRoundTripTests));

    private static void AssertSameColourEncoding(IccProfile expected, IccProfile actual)
    {
        Assert.True(JxlColorEncodingHeader.TryFromIccProfile(expected, out JxlColorEncodingHeader expectedEncoding));
        Assert.True(JxlColorEncodingHeader.TryFromIccProfile(actual, out JxlColorEncodingHeader actualEncoding));
        JxlColorEncodingFromIccTests.AssertSameEncoding(expectedEncoding, actualEncoding);
    }
}
