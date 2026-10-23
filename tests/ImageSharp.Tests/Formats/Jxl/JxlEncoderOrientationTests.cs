// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The encoder carries the Exif orientation into the codestream, as cjxl's <c>InterpretExif</c> does
/// (E6d, #334). The pixels and the Exif are written as they are stored, so a reader that applies the
/// codestream orientation shows the image as a reader that applies the Exif orientation does.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderOrientationTests
{
    private const int Width = 37;
    private const int Height = 23;

    public static TheoryData<ushort> Orientations => new() { 1, 2, 3, 4, 5, 6, 7, 8 };

    /// <summary>
    /// Gets Exif whose orientation tag cjxl ignores, each with a Software tag so that an Exif box is still
    /// written. Each row gives the IFD0 orientation entry's type, count and value fields as little-endian
    /// hex, and the orientation that SyncProfiles leaves in the box (0 for none).
    /// </summary>
    public static TheoryData<string, string, ushort> IgnoredOrientations => new()
    {
        { "value 0", "0300" + "01000000" + "00000000", 0 },
        { "value 9", "0300" + "01000000" + "09000000", 9 },
        { "LONG", "0400" + "01000000" + "06000000", 0 },
        { "count 2", "0300" + "02000000" + "06000600", 0 },
        { "BYTE", "0100" + "01000000" + "06000000", 0 },
    };

    [Theory]
    [MemberData(nameof(Orientations))]
    public void SaveAsJxl_ExifOrientation_OurDecoderGivesTheAutoOrientedPixels(ushort orientation)
    {
        using Image<Rgb24> image = CreateImage(orientation);
        using Image<Rgb24> expected = image.Clone(x => x.AutoOrient());

        using Image<Rgb24> decoded = Image.Load<Rgb24>(Save(image, Lossless));

        ImageComparer.Exact.VerifySimilarity(expected, decoded);

        // The decoder applied the orientation, so it resets the Exif orientation to 1 (D4).
        Assert.Equal((ushort)1, decoded.Metadata.ExifProfile.GetValue(ExifTag.Orientation).Value);
    }

    [Theory]
    [MemberData(nameof(Orientations))]
    public void SaveAsJxl_ExifOrientation_IdentifyGivesTheOrientedSize(ushort orientation)
    {
        using Image<Rgb24> image = CreateImage(orientation);

        IImageInfo info = Image.Identify(Save(image, Lossless));

        bool transposed = orientation >= 5;
        Assert.Equal(transposed ? Height : Width, info.Width);
        Assert.Equal(transposed ? Width : Height, info.Height);
        Assert.Equal((ushort)1, info.Metadata.ExifProfile.GetValue(ExifTag.Orientation).Value);
    }

    [Theory]
    [MemberData(nameof(Orientations))]
    public void SaveAsJxl_ExifOrientation_TheExifBoxKeepsIt(ushort orientation)
    {
        using Image<Rgb24> image = CreateImage(orientation);

        JxlBox exifBox = JxlBoxList.Read(Save(image, Lossless)).Single(b => b.Type == "Exif");

        ExifProfile boxed = new(exifBox.Contents[4..]);
        Assert.Equal(orientation, boxed.GetValue(ExifTag.Orientation).Value);
    }

    // The orientation is a header field: the frame's jxlp box is the same as for orientation 1, lossless
    // and lossy, and only the header's box differs.
    [Theory]
    [MemberData(nameof(Orientations))]
    public void SaveAsJxl_ExifOrientation_ChangesOnlyTheCodestreamHeader(ushort orientation)
    {
        foreach (float distance in new[] { 0f, 1f })
        {
            JxlEncoder encoder = new() { Distance = distance };
            using Image<Rgb24> upright = CreateImage(1);
            using Image<Rgb24> image = CreateImage(orientation);

            IReadOnlyList<JxlBox> expected = JxlBoxList.Read(Save(upright, encoder));
            IReadOnlyList<JxlBox> actual = JxlBoxList.Read(Save(image, encoder));

            Assert.Equal(new[] { "JXL ", "ftyp", "jxlp", "Exif", "jxlp" }, JxlBoxList.Types(actual));
            Assert.Equal(expected[4].Contents, actual[4].Contents);
            if (orientation == 1)
            {
                Assert.Equal(expected[2].Contents, actual[2].Contents);
            }
            else
            {
                Assert.NotEqual(expected[2].Contents, actual[2].Contents);
            }
        }
    }

    // ImageSharp has no Orientation tag that is not a SHORT: SyncProfiles parses the profile, which drops a
    // LONG, BYTE or two-value tag. The box then has no orientation, and value 0 or 9 is written as it is.
    // In each case the codestream is the one written with no Exif at all: orientation 1, as InterpretExif
    // leaves it.
    [Theory]
    [MemberData(nameof(IgnoredOrientations))]
    public void SaveAsJxl_IgnoredExifOrientation_LeavesOrientation1(string label, string orientationEntry, ushort boxedOrientation)
    {
        _ = label;
        using Image<Rgb24> bare = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Width, Height);
        using Image<Rgb24> image = bare.Clone();
        image.Metadata.ExifProfile = new ExifProfile(CreateExif(orientationEntry));

        byte[] encoded = Save(image, Lossless);

        JxlBox exifBox = JxlBoxList.Read(encoded).Single(b => b.Type == "Exif");
        ExifProfile boxed = new(exifBox.Contents[4..]);
        Assert.Equal("E6d", boxed.GetValue(ExifTag.Software).Value);
        Assert.Equal(boxedOrientation, boxed.GetValue(ExifTag.Orientation)?.Value ?? 0);

        byte[] codestream = JxlContainer.Read(encoded, readMetadata: false, TestContext.Current.CancellationToken).Codestream;
        Assert.Equal(Save(bare, Lossless), codestream);

        IImageInfo info = Image.Identify(encoded);
        Assert.Equal(Width, info.Width);
        Assert.Equal(Height, info.Height);
    }

    [Fact]
    public void SaveAsJxl_ExifWithoutOrientation_LeavesOrientation1()
    {
        using Image<Rgb24> bare = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Width, Height);
        using Image<Rgb24> image = bare.Clone();
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Software, "E6d");

        byte[] encoded = Save(image, Lossless);

        byte[] codestream = JxlContainer.Read(encoded, readMetadata: false, TestContext.Current.CancellationToken).Codestream;
        Assert.Equal(Save(bare, Lossless), codestream);
    }

    // InterpretExif (lib/jxl/base/exif.h) on Exif that the encoder cannot be given through ExifProfile.
    // The orientation starts at 1 and changes only for one SHORT from 1 to 8.
    [Theory]
    [InlineData("49492a00080000000100120103000100000006000000000000000000", 6u)] // little-endian SHORT
    [InlineData("4d4d002a000000080001011200030000000100060000000000000000", 6u)] // big-endian SHORT
    [InlineData("49492a00080000000100120103000100000008000000000000000000", 8u)] // SHORT 8
    [InlineData("49492a00080000000100120103000100000000000000000000000000", 1u)] // SHORT 0
    [InlineData("49492a00080000000100120103000100000009000000000000000000", 1u)] // SHORT 9
    [InlineData("49492a00080000000100120104000100000006000000000000000000", 1u)] // LONG
    [InlineData("49492a00080000000100120108000100000006000000000000000000", 1u)] // SSHORT
    [InlineData("49492a00080000000100120103000200000006000600000000000000", 1u)] // count 2
    [InlineData("49492a000800000002000f0102000100000041000000120103000100000006000000000000000000", 6u)] // second tag
    [InlineData("49492a000800000001003101020001000000410000000000000000000000", 1u)] // no orientation tag
    [InlineData("49492a00080000000100120103000100000006000000", 1u)] // the entry ends the Exif: libjxl wants one more byte
    [InlineData("49492a00ffffff7f0100120103000100000006000000000000000000", 1u)] // IFD offset past the end
    [InlineData("49492a00040000000100120103000100000006000000000000000000", 1u)] // IFD offset below 8
    [InlineData("49492b00080000000100120103000100000006000000000000000000", 1u)] // not a TIFF header
    [InlineData("4949", 1u)] // too short
    public void InterpretExif_FollowsCjxl(string exifHex, uint expected)
    {
        uint orientation = 1;

        JxlExif.InterpretExif(Convert.FromHexString(exifHex), ref orientation);

        Assert.Equal(expected, orientation);
    }

    // MagickReferenceDecoder drops metadata, so the orientation is read through MagickImage itself. Magick
    // reports the orientation and keeps the Exif as it is in the box. Magick also takes the orientation
    // from the Exif, so this passes with orientation 1 in the codestream too; jxlinfo and our decoder are
    // the checks of the codestream field.
    [Theory]
    [MemberData(nameof(Orientations))]
    public void SaveAsJxl_ExifOrientation_MagickReportsIt(ushort orientation)
    {
        JxlEncoderRoundTrip.SkipUnlessReaderRuns(JxlEncoderRoundTrip.Reader.Magick);
        using Image<Rgb24> image = CreateImage(orientation);

        using ImageMagick.MagickImage magick = new(Save(image, Lossless));

        Assert.Equal((ImageMagick.Orientation)orientation, magick.Orientation);

        // ImageMagick keeps Exif as a JPEG APP1 segment does: "Exif\0\0" in place of the box's 4-byte offset.
        byte[] magickExif = magick.GetExifProfile().ToByteArray();
        Assert.Equal(orientation, new ExifProfile(magickExif[6..]).GetValue(ExifTag.Orientation).Value);
    }

    [Theory]
    [MemberData(nameof(Orientations))]
    public async Task SaveAsJxl_ExifOrientation_JxlinfoReportsIt(ushort orientation)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        JxlTools.GetToolPath("jxlinfo");
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlEncoderOrientationTests));
        string path = Path.Combine(directory, $"orientation_{orientation}.jxl");
        using Image<Rgb24> image = CreateImage(orientation);
        await image.SaveAsJxlAsync(path, Lossless, cancellationToken);

        (int exitCode, string output) = await JxlTools.RunForExitCodeAsync("jxlinfo", cancellationToken, "-v", path);

        Assert.True(exitCode == 0, output);
        Assert.Contains($"Orientation: {orientation} (", output);
    }

    // A JPEG whose Exif says Orientation 6, saved as JPEG XL, decodes to the pixels that AutoOrient gives
    // for the JPEG: exactly for a lossless save, and within the lossy bound for the default save.
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void SaveAsJxl_JpegWithExifOrientation6_DecodesUpright(float distance)
    {
        using Image<Rgb24> jpeg = LoadJpegWithOrientation6();
        using Image<Rgb24> expected = jpeg.Clone(x => x.AutoOrient());

        byte[] encoded = Save(jpeg, new JxlEncoder { Distance = distance });
        using Image<Rgb24> decoded = Image.Load<Rgb24>(encoded);

        Assert.Equal(expected.Height, jpeg.Width);
        if (distance == 0)
        {
            ImageComparer.Exact.VerifySimilarity(expected, decoded);
        }
        else
        {
            JxlLossyEncoderTests.AssertWithinInterimBound(expected, decoded, $"JPEG with Orientation 6, d{distance}, {encoded.Length} bytes");
        }
    }

    private static JxlEncoder Lossless => new() { Distance = 0 };

    // A camera JPEG with its own Exif (Make, Model, sub-IFDs), with the orientation set to 6 and saved as a
    // JPEG again, then loaded as a JPEG: the Exif comes from a JPEG file, as a user's photo's does.
    private static Image<Rgb24> LoadJpegWithOrientation6()
    {
        using Image<Rgb24> source = Image.Load<Rgb24>(TestFile.GetInputFileFullPath(TestImages.Jpeg.Baseline.Jpeg420Exif));
        Assert.NotNull(source.Metadata.ExifProfile);
        source.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        using MemoryStream stream = new();
        source.SaveAsJpeg(stream);
        Image<Rgb24> jpeg = Image.Load<Rgb24>(stream.ToArray());
        Assert.Equal((ushort)6, jpeg.Metadata.ExifProfile.GetValue(ExifTag.Orientation).Value);
        return jpeg;
    }

    private static Image<Rgb24> CreateImage(ushort orientation)
    {
        Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Width, Height);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
        return image;
    }

    // Little-endian Exif with two IFD0 entries: the orientation (type, count and value from the argument),
    // then Software "E6d".
    private static byte[] CreateExif(string orientationEntry)
        => Convert.FromHexString("49492a00" + "08000000" + "0200" + "1201" + orientationEntry + "3101" + "0200" + "04000000" + "45366400" + "00000000");

    private static byte[] Save(Image image, JxlEncoder encoder)
    {
        using MemoryStream stream = new();
        image.SaveAsJxl(stream, encoder);
        return stream.ToArray();
    }
}
