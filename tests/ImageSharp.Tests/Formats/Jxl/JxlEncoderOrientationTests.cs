// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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

    private static JxlEncoder Lossless => new() { Distance = 0 };

    private static Image<Rgb24> CreateImage(ushort orientation)
    {
        Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", Width, Height);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
        return image;
    }

    private static byte[] Save(Image image, JxlEncoder encoder)
    {
        using MemoryStream stream = new();
        image.SaveAsJxl(stream, encoder);
        return stream.ToArray();
    }
}
