// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The decoder applies the codestream orientation, as djxl does by default. The files are one 37 x 23
/// RGBA image with each of the eight orientations; the references are plain djxl output.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlOrientationTests
{
    private static string GeneratedDirectory => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated");

    private static string ExifPath => Path.Combine(GeneratedDirectory, "orientation_exif.jxl");

    public static TheoryData<int> Orientations => new() { 1, 2, 3, 4, 5, 6, 7, 8 };

    [Theory]
    [MemberData(nameof(Orientations))]
    public void Load_EachOrientation_MatchesDjxl(int orientation)
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), OrientationPath(orientation));
        using Image<Rgba32> reference = Image.Load<Rgba32>(ReferencePath($"orientation_{orientation}"));

        ImageComparer.Exact.VerifySimilarity(reference, image);
    }

    [Theory]
    [MemberData(nameof(Orientations))]
    public void DecodeToPlanes_EachOrientation_MatchesDjxl(int orientation)
    {
        using FileStream stream = File.OpenRead(OrientationPath(orientation));
        using JxlDecodedImage planes = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
        using Image<Rgba32> reference = Image.Load<Rgba32>(ReferencePath($"orientation_{orientation}"));

        Assert.Equal(reference.Width, planes.Width);
        Assert.Equal(reference.Height, planes.Height);
        IReadOnlyList<Buffer2D<float>> frame = planes.Frames[0].Planes;
        Assert.Equal(4, frame.Count);
        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = 0; x < reference.Width; x++)
            {
                Rgba32 expected = reference[x, y];
                Assert.Equal(expected.R, Quantize8(frame[0].DangerousGetRowSpan(y)[x]));
                Assert.Equal(expected.G, Quantize8(frame[1].DangerousGetRowSpan(y)[x]));
                Assert.Equal(expected.B, Quantize8(frame[2].DangerousGetRowSpan(y)[x]));
                Assert.Equal(expected.A, Quantize8(frame[3].DangerousGetRowSpan(y)[x]));
            }
        }
    }

    [Theory]
    [InlineData(1, 37, 23)]
    [InlineData(2, 37, 23)]
    [InlineData(3, 37, 23)]
    [InlineData(4, 37, 23)]
    [InlineData(5, 23, 37)]
    [InlineData(6, 23, 37)]
    [InlineData(7, 23, 37)]
    [InlineData(8, 23, 37)]
    public void Identify_EachOrientation_GivesTheOrientedSize(int orientation, int width, int height)
    {
        IImageInfo info = Image.Identify(CreateConfiguration(), OrientationPath(orientation), out _);

        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
    }

    [Fact]
    public void Load_OrientedImageWithExif_SetsTheExifOrientationTo1()
    {
        // The codestream orientation is 6, and so is the Exif orientation in the file's Exif box.
        JxlContainerContents contents = JxlContainer.Read(File.ReadAllBytes(ExifPath), readMetadata: true, CancellationToken.None);
        Assert.Equal((ushort)6, new ExifProfile(contents.Exif).GetValue(ExifTag.Orientation).Value);

        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), ExifPath);
        using Image<Rgba32> reference = Image.Load<Rgba32>(ReferencePath("orientation_exif"));

        // djxl orients the pixels and writes the Exif with its orientation reset to 1.
        ImageComparer.Exact.VerifySimilarity(reference, image);
        Assert.Equal((ushort)1, image.Metadata.ExifProfile.GetValue(ExifTag.Orientation).Value);
        Assert.Equal((ushort)1, reference.Metadata.ExifProfile.GetValue(ExifTag.Orientation).Value);
        Assert.Equal(reference.Metadata.ExifProfile.ToByteArray(), image.Metadata.ExifProfile.ToByteArray());
    }

    [Fact]
    public void Identify_OrientedImageWithExif_ReportsTheExifOrientationAs1()
    {
        // Identify reports the image as Load gives it: oriented, so the Exif orientation is 1.
        IImageInfo info = Image.Identify(CreateConfiguration(), ExifPath, out _);

        Assert.Equal(23, info.Width);
        Assert.Equal(37, info.Height);
        Assert.Equal((ushort)1, info.Metadata.ExifProfile.GetValue(ExifTag.Orientation).Value);
    }

    [Theory]
    [InlineData("49492a00080000000100120103000100000006000000000000000000", "49492a00080000000100120103000100000001000000000000000000")] // little-endian SHORT: reset
    [InlineData("4d4d002a000000080001011200030000000100060000000000000000", "4d4d002a000000080001011200030000000100010000000000000000")] // big-endian SHORT: reset
    [InlineData("49492a00080000000100120104000100000006000000000000000000", "49492a00080000000100120104000100000006000000000000000000")] // LONG: left as it is
    [InlineData("49492a00080000000100120103000200000006000600000000000000", "49492a00080000000100120103000200000006000600000000000000")] // count 2: left as it is
    [InlineData("49492a000800000002000f0102000100000041000000120103000100000006000000000000000000", "49492a000800000002000f0102000100000041000000120103000100000001000000000000000000")] // second tag: reset
    [InlineData("49492a0008000000010012010300010000000600", "49492a0008000000010012010300010000000600")] // the entry runs to the end: left as it is
    [InlineData("49492a00ffffff7f0100120103000100000006000000000000000000", "49492a00ffffff7f0100120103000100000006000000000000000000")] // IFD offset past the end
    [InlineData("49492a00040000000100120103000100000006000000000000000000", "49492a00040000000100120103000100000006000000000000000000")] // IFD offset below 8
    [InlineData("4949", "4949")] // too short
    [InlineData("49492b00080000000100120103000100000006000000000000000000", "49492b00080000000100120103000100000006000000000000000000")] // not a TIFF header
    [InlineData("49492a00080000000100120103000100000006000000", "49492a00080000000100120103000100000006000000")] // the entry ends the Exif: libjxl wants one more byte
    public void ResetExifOrientation_FollowsDjxl(string exifHex, string expectedHex)
    {
        byte[] exif = Convert.FromHexString(exifHex);

        JxlExif.ResetOrientation(exif);

        Assert.Equal(expectedHex, Convert.ToHexString(exif).ToLowerInvariant());
    }

    private static byte Quantize8(float value) => (byte)MathF.Round(Math.Clamp(value, 0F, 1F) * 255F);

    private static string OrientationPath(int orientation) => Path.Combine(GeneratedDirectory, $"orientation_{orientation}.jxl");

    private static string ReferencePath(string name) => Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".png");

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
