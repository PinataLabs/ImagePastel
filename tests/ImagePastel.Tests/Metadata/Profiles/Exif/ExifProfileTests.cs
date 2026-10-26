// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

using Xunit;

namespace SixLabors.ImageSharp.Tests.Metadata.Profiles.Exif;

[Trait("Profile", "Exif")]
public class ExifProfileTests
{
    public enum TestImageWriteFormat
    {
        /// <summary>
        /// Writes a jpg file.
        /// </summary>
        Jpeg,

        /// <summary>
        /// Writes a png file.
        /// </summary>
        Png,

        /// <summary>
        /// Writes a lossless webp file.
        /// </summary>
        WebpLossless,

        /// <summary>
        /// Writes a lossy webp file.
        /// </summary>
        WebpLossy,

        /// <summary>
        /// Writes a tiff file.
        /// </summary>
        Tiff
    }

    private static readonly Dictionary<ExifTag, object> TestProfileValues = new Dictionary<ExifTag, object>
    {
        { ExifTag.Software, "Software" },
        { ExifTag.Copyright, "Copyright" },
        { ExifTag.Orientation, (ushort)5 },
        { ExifTag.ShutterSpeedValue, new SignedRational(75.55) },
        { ExifTag.ImageDescription, "ImageDescription" },
        { ExifTag.ExposureTime, new Rational(1.0 / 1600.0) },
        { ExifTag.Model, "Model" },
        { ExifTag.XPAuthor, "The XPAuthor text" },
        { ExifTag.UserComment, new EncodedString(EncodedString.CharacterCode.Unicode, "The Unicode text") },
        { ExifTag.GPSAreaInformation, new EncodedString("Default constructor text (GPSAreaInformation)") },
    };

    [Theory]
    [InlineData(TestImageWriteFormat.Jpeg)]
    [InlineData(TestImageWriteFormat.Png)]
    [InlineData(TestImageWriteFormat.WebpLossless)]
    [InlineData(TestImageWriteFormat.WebpLossy)]
    public void Constructor(TestImageWriteFormat imageFormat)
    {
        Image<Rgba32> image = TestFile.Create(TestImages.Jpeg.Baseline.Calliphora).CreateRgba32Image();

        Assert.Null(image.Metadata.ExifProfile);

        const string expected = "Dirk Lemstra";
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Copyright, expected);

        image = WriteAndRead(image, imageFormat);

        Assert.NotNull(image.Metadata.ExifProfile);
        Assert.Equal(1, image.Metadata.ExifProfile.Values.Count);

        IExifValue<string> value = image.Metadata.ExifProfile.GetValue(ExifTag.Copyright);

        Assert.NotNull(value);
        Assert.Equal(expected, value.Value);
        image.Dispose();
    }

    [Fact]
    public void ConstructorEmpty()
    {
        new ExifProfile(null);
        new ExifProfile(Array.Empty<byte>());
    }

    [Fact]
    public void EmptyWriter()
    {
        var profile = new ExifProfile() { Parts = ExifParts.GpsTags };
        profile.SetValue(ExifTag.Copyright, "Copyright text");

        byte[] bytes = profile.ToByteArray();

        Assert.NotNull(bytes);
        Assert.Empty(bytes);
    }

    [Fact]
    public void ConstructorCopy()
    {
        Assert.Throws<NullReferenceException>(() => ((ExifProfile)null).DeepClone());

        ExifProfile profile = GetExifProfile();

        ExifProfile clone = profile.DeepClone();
        TestProfile(clone);

        profile.SetValue(ExifTag.ColorSpace, (ushort)2);

        clone = profile.DeepClone();
        TestProfile(clone);
    }

    [Theory]
    [InlineData(TestImageWriteFormat.Jpeg)]
    [InlineData(TestImageWriteFormat.Png)]
    [InlineData(TestImageWriteFormat.WebpLossless)]
    [InlineData(TestImageWriteFormat.WebpLossy)]
    public void WriteFraction(TestImageWriteFormat imageFormat)
    {
        using (var memStream = new MemoryStream())
        {
            double exposureTime = 1.0 / 1600;

            ExifProfile profile = GetExifProfile();

            profile.SetValue(ExifTag.ExposureTime, new Rational(exposureTime));

            var image = new Image<Rgba32>(1, 1);
            image.Metadata.ExifProfile = profile;

            image = WriteAndRead(image, imageFormat);

            profile = image.Metadata.ExifProfile;
            Assert.NotNull(profile);

            IExifValue<Rational> value = profile.GetValue(ExifTag.ExposureTime);
            Assert.NotNull(value);
            Assert.NotEqual(exposureTime, value.Value.ToDouble());

            memStream.Position = 0;
            profile = GetExifProfile();

            profile.SetValue(ExifTag.ExposureTime, new Rational(exposureTime, true));
            image.Metadata.ExifProfile = profile;

            image = WriteAndRead(image, imageFormat);

            profile = image.Metadata.ExifProfile;
            Assert.NotNull(profile);

            value = profile.GetValue(ExifTag.ExposureTime);
            Assert.Equal(exposureTime, value.Value.ToDouble());

            image.Dispose();
        }
    }

    [Theory]
    [InlineData(TestImageWriteFormat.Jpeg)]
    [InlineData(TestImageWriteFormat.Png)]
    [InlineData(TestImageWriteFormat.WebpLossless)]
    [InlineData(TestImageWriteFormat.WebpLossy)]
    public void ReadWriteInfinity(TestImageWriteFormat imageFormat)
    {
        Image<Rgba32> image = TestFile.Create(TestImages.Jpeg.Baseline.Floorplan).CreateRgba32Image();
        image.Metadata.ExifProfile.SetValue(ExifTag.ExposureBiasValue, new SignedRational(double.PositiveInfinity));

        image = WriteAndReadJpeg(image);
        IExifValue<SignedRational> value = image.Metadata.ExifProfile.GetValue(ExifTag.ExposureBiasValue);
        Assert.NotNull(value);
        Assert.Equal(new SignedRational(double.PositiveInfinity), value.Value);

        image.Metadata.ExifProfile.SetValue(ExifTag.ExposureBiasValue, new SignedRational(double.NegativeInfinity));

        image = WriteAndRead(image, imageFormat);
        value = image.Metadata.ExifProfile.GetValue(ExifTag.ExposureBiasValue);
        Assert.NotNull(value);
        Assert.Equal(new SignedRational(double.NegativeInfinity), value.Value);

        image.Metadata.ExifProfile.SetValue(ExifTag.FlashEnergy, new Rational(double.NegativeInfinity));

        image = WriteAndRead(image, imageFormat);
        IExifValue<Rational> value2 = image.Metadata.ExifProfile.GetValue(ExifTag.FlashEnergy);
        Assert.NotNull(value2);
        Assert.Equal(new Rational(double.PositiveInfinity), value2.Value);

        image.Dispose();
    }

    [Theory]
    /* The original exif profile has 19 values, the written profile should be 3 less.
     1 x due to setting of null "ReferenceBlackWhite" value.
     2 x due to use of non-standard padding tag 0xEA1C listed in EXIF Tool. We can read those values but adhere
     strictly to the 2.3.1 specification when writing. (TODO: Support 2.3.2)
     https://exiftool.org/TagNames/EXIF.html */
    [InlineData(TestImageWriteFormat.Jpeg, 18)]
    [InlineData(TestImageWriteFormat.Png, 18)]
    [InlineData(TestImageWriteFormat.WebpLossless, 18)]
    public void SetValue(TestImageWriteFormat imageFormat, int expectedProfileValueCount)
    {
        Image<Rgba32> image = TestFile.Create(TestImages.Jpeg.Baseline.Floorplan).CreateRgba32Image();
        image.Metadata.ExifProfile.SetValue(ExifTag.Software, "ImageSharp");

        IExifValue<string> software = image.Metadata.ExifProfile.GetValue(ExifTag.Software);
        Assert.Equal("ImageSharp", software.Value);

        // ExifString can set integer values.
        Assert.True(software.TrySetValue(15));
        Assert.False(software.TrySetValue(15F));

        image.Metadata.ExifProfile.SetValue(ExifTag.ShutterSpeedValue, new SignedRational(75.55));

        IExifValue<SignedRational> shutterSpeed = image.Metadata.ExifProfile.GetValue(ExifTag.ShutterSpeedValue);

        Assert.Equal(new SignedRational(7555, 100), shutterSpeed.Value);
        Assert.False(shutterSpeed.TrySetValue(75));

        image.Metadata.ExifProfile.SetValue(ExifTag.XResolution, new Rational(150.0));

        // We also need to change this value because this overrides XResolution when the image is written.
        image.Metadata.HorizontalResolution = 150.0;

        IExifValue<Rational> xResolution = image.Metadata.ExifProfile.GetValue(ExifTag.XResolution);
        Assert.Equal(new Rational(150, 1), xResolution.Value);

        Assert.False(xResolution.TrySetValue("ImageSharp"));

        image.Metadata.ExifProfile.SetValue(ExifTag.ReferenceBlackWhite, null);

        IExifValue<Rational[]> referenceBlackWhite = image.Metadata.ExifProfile.GetValue(ExifTag.ReferenceBlackWhite);
        Assert.Null(referenceBlackWhite.Value);

        var expectedLatitude = new Rational[] { new Rational(12.3), new Rational(4.56), new Rational(789.0) };
        image.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitude, expectedLatitude);

        IExifValue<Rational[]> latitude = image.Metadata.ExifProfile.GetValue(ExifTag.GPSLatitude);
        Assert.Equal(expectedLatitude, latitude.Value);

        // todo: duplicate tags
        Assert.Equal(2, image.Metadata.ExifProfile.Values.Count(v => (ushort)v.Tag == 59932));

        image = WriteAndRead(image, imageFormat);

        Assert.NotNull(image.Metadata.ExifProfile);
        Assert.Equal(0, image.Metadata.ExifProfile.Values.Count(v => (ushort)v.Tag == 59932));

        Assert.Equal(expectedProfileValueCount, image.Metadata.ExifProfile.Values.Count);

        software = image.Metadata.ExifProfile.GetValue(ExifTag.Software);
        Assert.Equal("15", software.Value);

        shutterSpeed = image.Metadata.ExifProfile.GetValue(ExifTag.ShutterSpeedValue);
        Assert.Equal(new SignedRational(75.55), shutterSpeed.Value);

        xResolution = image.Metadata.ExifProfile.GetValue(ExifTag.XResolution);
        Assert.Equal(new Rational(150.0), xResolution.Value);

        referenceBlackWhite = image.Metadata.ExifProfile.GetValue(ExifTag.ReferenceBlackWhite);
        Assert.Null(referenceBlackWhite);

        latitude = image.Metadata.ExifProfile.GetValue(ExifTag.GPSLatitude);
        Assert.Equal(expectedLatitude, latitude.Value);

        image.Dispose();
    }

    [Theory]
    [InlineData(TestImageWriteFormat.Jpeg)]
    [InlineData(TestImageWriteFormat.Png)]
    [InlineData(TestImageWriteFormat.WebpLossless)]
    [InlineData(TestImageWriteFormat.WebpLossy)]
    public void WriteOnlyExifTags_Works(TestImageWriteFormat imageFormat)
    {
        // Arrange
        Image<Rgba32> image = TestFile.Create(TestImages.Jpeg.Baseline.Floorplan).CreateRgba32Image();
        image.Metadata.ExifProfile.Parts = ExifParts.ExifTags;

        // Act
        image = WriteAndRead(image, imageFormat);

        // Assert
        Assert.NotNull(image.Metadata.ExifProfile);
        Assert.Equal(7, image.Metadata.ExifProfile.Values.Count);
        foreach (IExifValue exifProfileValue in image.Metadata.ExifProfile.Values)
        {
            Assert.True(ExifTags.GetPart(exifProfileValue.Tag) == ExifParts.ExifTags);
        }

        image.Dispose();
    }

    [Fact]
    public void RemoveEntry_Works()
    {
        // Arrange
        using Image<Rgba32> image = TestFile.Create(TestImages.Jpeg.Baseline.Floorplan).CreateRgba32Image();
        int profileCount = image.Metadata.ExifProfile.Values.Count;

        // Assert
        Assert.NotNull(image.Metadata.ExifProfile.GetValue(ExifTag.ColorSpace));
        Assert.True(image.Metadata.ExifProfile.RemoveValue(ExifTag.ColorSpace));
        Assert.False(image.Metadata.ExifProfile.RemoveValue(ExifTag.ColorSpace));
        Assert.Null(image.Metadata.ExifProfile.GetValue(ExifTag.ColorSpace));
        Assert.Equal(profileCount - 1, image.Metadata.ExifProfile.Values.Count);
    }

    [Fact]
    public void Syncs()
    {
        var exifProfile = new ExifProfile();
        exifProfile.SetValue(ExifTag.XResolution, new Rational(200));
        exifProfile.SetValue(ExifTag.YResolution, new Rational(300));

        var metaData = new ImageMetadata
        {
            ExifProfile = exifProfile,
            HorizontalResolution = 200,
            VerticalResolution = 300
        };

        metaData.HorizontalResolution = 100;

        Assert.Equal(200, metaData.ExifProfile.GetValue(ExifTag.XResolution).Value.ToDouble());
        Assert.Equal(300, metaData.ExifProfile.GetValue(ExifTag.YResolution).Value.ToDouble());

        exifProfile.Sync(metaData);

        Assert.Equal(100, metaData.ExifProfile.GetValue(ExifTag.XResolution).Value.ToDouble());
        Assert.Equal(300, metaData.ExifProfile.GetValue(ExifTag.YResolution).Value.ToDouble());

        metaData.VerticalResolution = 150;

        Assert.Equal(100, metaData.ExifProfile.GetValue(ExifTag.XResolution).Value.ToDouble());
        Assert.Equal(300, metaData.ExifProfile.GetValue(ExifTag.YResolution).Value.ToDouble());

        exifProfile.Sync(metaData);

        Assert.Equal(100, metaData.ExifProfile.GetValue(ExifTag.XResolution).Value.ToDouble());
        Assert.Equal(150, metaData.ExifProfile.GetValue(ExifTag.YResolution).Value.ToDouble());
    }

    [Fact]
    public void Values()
    {
        ExifProfile profile = GetExifProfile();

        TestProfile(profile);

        using Image thumbnail = profile.CreateThumbnail();
        Assert.NotNull(thumbnail);
        Assert.Equal(256, thumbnail.Width);
        Assert.Equal(170, thumbnail.Height);

        using Image<Rgba32> genericThumbnail = profile.CreateThumbnail<Rgba32>();
        Assert.NotNull(genericThumbnail);
        Assert.Equal(256, genericThumbnail.Width);
        Assert.Equal(170, genericThumbnail.Height);
    }

    [Fact]
    public void ReadWriteLargeProfileJpg()
    {
        ExifTag<string>[] tags = { ExifTag.Software, ExifTag.Copyright, ExifTag.Model, ExifTag.ImageDescription };
        foreach (ExifTag<string> tag in tags)
        {
            // Arrange
            var junk = new StringBuilder();
            for (int i = 0; i < 65600; i++)
            {
                junk.Append('a');
            }

            var image = new Image<Rgba32>(100, 100);
            ExifProfile expectedProfile = CreateExifProfile();
            var expectedProfileTags = expectedProfile.Values.Select(x => x.Tag).ToList();
            expectedProfile.SetValue(tag, junk.ToString());
            image.Metadata.ExifProfile = expectedProfile;

            // Act
            Image<Rgba32> reloadedImage = WriteAndRead(image, TestImageWriteFormat.Jpeg);

            // Assert
            ExifProfile actualProfile = reloadedImage.Metadata.ExifProfile;
            Assert.NotNull(actualProfile);

            foreach (ExifTag expectedProfileTag in expectedProfileTags)
            {
                IExifValue actualProfileValue = actualProfile.GetValueInternal(expectedProfileTag);
                IExifValue expectedProfileValue = expectedProfile.GetValueInternal(expectedProfileTag);
                Assert.Equal(expectedProfileValue.GetValue(), actualProfileValue.GetValue());
            }

            IExifValue<string> expected = expectedProfile.GetValue(tag);
            IExifValue<string> actual = actualProfile.GetValue(tag);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ExifTypeUndefined()
    {
        // This image contains an 802 byte EXIF profile.
        // It has a tag with an index offset of 18,481,152 bytes (overrunning the data)
        using Image<Rgba32> image = TestFile.Create(TestImages.Jpeg.Progressive.Bad.ExifUndefType).CreateRgba32Image();
        Assert.NotNull(image);

        ExifProfile profile = image.Metadata.ExifProfile;
        Assert.NotNull(profile);

        foreach (ExifValue value in profile.Values)
        {
            if (value.DataType == ExifDataType.Undefined)
            {
                Assert.True(value.IsArray);
                Assert.Equal(4U, 4 * ExifDataTypes.GetSize(value.DataType));
            }
        }
    }

    [Fact]
    public void TestArrayValueWithUnspecifiedSize()
    {
        // This images contains array in the exif profile that has zero components.
        using Image<Rgba32> image = TestFile.Create(TestImages.Jpeg.Issues.InvalidCast520).CreateRgba32Image();

        ExifProfile profile = image.Metadata.ExifProfile;
        Assert.NotNull(profile);

        // Force parsing of the profile.
        Assert.Equal(25, profile.Values.Count);

        // todo: duplicate tags (from root container and subIfd)
        Assert.Equal(2, profile.Values.Count(v => (ExifTagValue)(ushort)v.Tag == ExifTagValue.DateTime));

        byte[] bytes = profile.ToByteArray();
        Assert.Equal(531, bytes.Length);

        var profile2 = new ExifProfile(bytes);
        Assert.Equal(25, profile2.Values.Count);
    }

    [Theory]
    [InlineData(TestImageWriteFormat.Jpeg)]
    [InlineData(TestImageWriteFormat.Png)]
    [InlineData(TestImageWriteFormat.WebpLossless)]
    [InlineData(TestImageWriteFormat.WebpLossy)]
    public void WritingImagePreservesExifProfile(TestImageWriteFormat imageFormat)
    {
        // Arrange
        var image = new Image<Rgba32>(1, 1);
        image.Metadata.ExifProfile = CreateExifProfile();

        // Act
        using Image<Rgba32> reloadedImage = WriteAndRead(image, imageFormat);

        // Assert
        ExifProfile actual = reloadedImage.Metadata.ExifProfile;
        Assert.NotNull(actual);
        foreach (KeyValuePair<ExifTag, object> expectedProfileValue in TestProfileValues)
        {
            IExifValue actualProfileValue = actual.GetValueInternal(expectedProfileValue.Key);
            Assert.NotNull(actualProfileValue);
            Assert.Equal(expectedProfileValue.Value, actualProfileValue.GetValue());
        }
    }

    [Fact]
    public void ProfileToByteArray()
    {
        // Arrange
        byte[] exifBytesWithoutExifCode = ExifConstants.LittleEndianByteOrderMarker.ToArray();
        ExifProfile expectedProfile = CreateExifProfile();
        var expectedProfileTags = expectedProfile.Values.Select(x => x.Tag).ToList();

        // Act
        byte[] actualBytes = expectedProfile.ToByteArray();
        var actualProfile = new ExifProfile(actualBytes);

        // Assert
        Assert.NotNull(actualBytes);
        Assert.NotEmpty(actualBytes);
        Assert.Equal(exifBytesWithoutExifCode, actualBytes.Take(exifBytesWithoutExifCode.Length).ToArray());
        foreach (ExifTag expectedProfileTag in expectedProfileTags)
        {
            IExifValue actualProfileValue = actualProfile.GetValueInternal(expectedProfileTag);
            IExifValue expectedProfileValue = expectedProfile.GetValueInternal(expectedProfileTag);
            Assert.Equal(expectedProfileValue.GetValue(), actualProfileValue.GetValue());
        }
    }

    private static ExifProfile CreateExifProfile()
    {
        var profile = new ExifProfile();

        foreach (KeyValuePair<ExifTag, object> exifProfileValue in TestProfileValues)
        {
            profile.SetValueInternal(exifProfileValue.Key, exifProfileValue.Value);
        }

        return profile;
    }

    [Fact]
    public void IfdStructure()
    {
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.XPAuthor, "Dan Petitt");

        Span<byte> actualBytes = exif.ToByteArray();

        // Assert
        int ifdOffset = ExifConstants.LittleEndianByteOrderMarker.Length;
        Assert.Equal(8U, BinaryPrimitives.ReadUInt32LittleEndian(actualBytes.Slice(ifdOffset, 4)));

        int nextIfdPointerOffset = ExifConstants.LittleEndianByteOrderMarker.Length + 4 + 2 + 12;
        Assert.Equal(0U, BinaryPrimitives.ReadUInt32LittleEndian(actualBytes.Slice(nextIfdPointerOffset, 4)));
    }

    [Theory]
    [InlineData((ushort)ExifTagValue.SubIFDOffset, (ushort)ExifTagValue.UserComment, true)]
    [InlineData((ushort)ExifTagValue.GPSIFDOffset, (ushort)ExifTagValue.GPSProcessingMethod, true)]
    [InlineData((ushort)ExifTagValue.GPSIFDOffset, (ushort)ExifTagValue.GPSAreaInformation, true)]
    [InlineData((ushort)ExifTagValue.SubIFDOffset, (ushort)ExifTagValue.UserComment, false)]
    [InlineData((ushort)ExifTagValue.GPSIFDOffset, (ushort)ExifTagValue.GPSProcessingMethod, false)]
    [InlineData((ushort)ExifTagValue.GPSIFDOffset, (ushort)ExifTagValue.GPSAreaInformation, false)]
    public void EncodedString_Unicode_ReadsTextInProfileByteOrder(ushort pointerTag, ushort tag, bool bigEndian)
    {
        const string text = "Grüße, 世界!";
        byte[] data = CreateEncodedStringExif(bigEndian, pointerTag, tag, text);

        var profile = new ExifProfile(data);

        EncodedString value = GetEncodedString(profile, tag);
        Assert.Equal(EncodedString.CharacterCode.Unicode, value.Code);
        Assert.Equal(text, value.Text);
    }

    [Theory]
    [InlineData((ushort)ExifTagValue.SubIFDOffset, (ushort)ExifTagValue.UserComment)]
    [InlineData((ushort)ExifTagValue.GPSIFDOffset, (ushort)ExifTagValue.GPSProcessingMethod)]
    [InlineData((ushort)ExifTagValue.GPSIFDOffset, (ushort)ExifTagValue.GPSAreaInformation)]
    public void EncodedString_Unicode_FromBigEndianProfile_SurvivesJpegRoundTrip(ushort pointerTag, ushort tag)
    {
        const string text = "Grüße, 世界!";
        var image = new Image<Rgba32>(8, 8);
        image.Metadata.ExifProfile = new ExifProfile(CreateEncodedStringExif(true, pointerTag, tag, text));

        using Image<Rgba32> reloaded = WriteAndReadJpeg(image);

        EncodedString value = GetEncodedString(reloaded.Metadata.ExifProfile, tag);
        Assert.Equal(EncodedString.CharacterCode.Unicode, value.Code);
        Assert.Equal(text, value.Text);
    }

    [Fact]
    public void NestedSubIfdPointers_AreFollowed()
    {
        // IFD0 at 8 points to the EXIF IFD at 26, which points to the GPS IFD at 44.
        var builder = new ExifBuilder(false);
        builder.WriteIfd((ExifTagValue.SubIFDOffset, ExifDataType.Long, 1, builder.Long(26)));
        builder.WriteIfd((ExifTagValue.GPSIFDOffset, ExifDataType.Long, 1, builder.Long(44)));
        builder.WriteIfd((ExifTagValue.GPSVersionID, ExifDataType.Byte, 4, new byte[] { 2, 2, 0, 0 }));

        var profile = new ExifProfile(builder.ToArray());

        IExifValue value = Assert.Single(profile.Values);
        Assert.Equal(ExifTag.GPSVersionID, value.Tag);
        Assert.Equal(new byte[] { 2, 2, 0, 0 }, value.GetValue());
    }

    [Theory]
    [InlineData(8)]
    [InlineData(26)]
    public void CyclicSubIfdPointers_AreReadOnce(uint target)
    {
        // IFD0 at 8 points to the EXIF IFD at 26, which points back to IFD0 or to itself.
        var builder = new ExifBuilder(false);
        builder.WriteIfd((ExifTagValue.SubIFDOffset, ExifDataType.Long, 1, builder.Long(26)));
        builder.WriteIfd(
            (ExifTagValue.GPSIFDOffset, ExifDataType.Long, 1, builder.Long(target)),
            (ExifTagValue.ColorSpace, ExifDataType.Short, 1, new byte[] { 1, 0, 0, 0 }));

        var profile = new ExifProfile(builder.ToArray());

        IExifValue value = Assert.Single(profile.Values);
        Assert.Equal(ExifTag.ColorSpace, value.Tag);
        Assert.Equal((ushort)1, value.GetValue());
    }

    [Theory]
    [InlineData(new short[] { -5 })]
    [InlineData(new short[] { -8, 9 })]
    public void TimeZoneOffset_IsWrittenAsSignedShort(short[] offset)
    {
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.TimeZoneOffset, offset);

        byte[] data = profile.ToByteArray();

        // IFD0 holds only the pointer to the EXIF IFD, which holds only the time zone offset.
        Span<byte> ifd0 = data.AsSpan(8);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ifd0));
        Assert.Equal((ushort)ExifTagValue.SubIFDOffset, BinaryPrimitives.ReadUInt16LittleEndian(ifd0.Slice(2)));
        Span<byte> exifIfd = data.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(ifd0.Slice(10)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(exifIfd));
        Assert.Equal((ushort)ExifTagValue.TimeZoneOffset, BinaryPrimitives.ReadUInt16LittleEndian(exifIfd.Slice(2)));
        Assert.Equal((ushort)ExifDataType.SignedShort, BinaryPrimitives.ReadUInt16LittleEndian(exifIfd.Slice(4)));
        Assert.Equal((uint)offset.Length, BinaryPrimitives.ReadUInt32LittleEndian(exifIfd.Slice(6)));
        for (int i = 0; i < offset.Length; i++)
        {
            Assert.Equal(offset[i], BinaryPrimitives.ReadInt16LittleEndian(exifIfd.Slice(10 + (2 * i))));
        }
    }

    [Theory]
    [InlineData(TestImageWriteFormat.Jpeg, new short[] { -5 })]
    [InlineData(TestImageWriteFormat.Jpeg, new short[] { -8, 9 })]
    [InlineData(TestImageWriteFormat.Tiff, new short[] { -5 })]
    [InlineData(TestImageWriteFormat.Tiff, new short[] { -8, 9 })]
    public void TimeZoneOffset_NegativeValues_RoundTrip(TestImageWriteFormat imageFormat, short[] offset)
    {
        // The TIFF encoder writes the EXIF profile of the root frame.
        static ExifProfile GetProfile(Image image, TestImageWriteFormat imageFormat)
            => imageFormat == TestImageWriteFormat.Tiff ? image.Frames.RootFrame.Metadata.ExifProfile : image.Metadata.ExifProfile;

        var image = new Image<Rgba32>(8, 8);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Frames.RootFrame.Metadata.ExifProfile = new ExifProfile();
        GetProfile(image, imageFormat).SetValue(ExifTag.TimeZoneOffset, offset);

        using Image<Rgba32> reloaded = WriteAndRead(image, imageFormat);

        Assert.Equal(offset, GetProfile(reloaded, imageFormat).GetValue(ExifTag.TimeZoneOffset).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimeZoneOffset_StoredAsSignedShort_ReadsSignedValues(bool bigEndian)
    {
        var builder = new ExifBuilder(bigEndian);
        byte[] value = builder.Short(unchecked((ushort)-5)).Concat(builder.Short(unchecked((ushort)-4))).ToArray();
        builder.WriteIfd((ExifTagValue.SubIFDOffset, ExifDataType.Long, 1, builder.Long(26)));
        builder.WriteIfd((ExifTagValue.TimeZoneOffset, ExifDataType.SignedShort, 2, value));

        var profile = new ExifProfile(builder.ToArray());

        Assert.Equal(new short[] { -5, -4 }, profile.GetValue(ExifTag.TimeZoneOffset).Value);
    }

    [Fact]
    public void TimeZoneOffset_StoredAsLong_ReadsValues()
    {
        // Earlier versions of this library wrote the time zone offset as LONG.
        var builder = new ExifBuilder(false);
        builder.WriteIfd((ExifTagValue.SubIFDOffset, ExifDataType.Long, 1, builder.Long(26)));
        builder.WriteIfd((ExifTagValue.TimeZoneOffset, ExifDataType.Long, 1, builder.Long(9)));

        var profile = new ExifProfile(builder.ToArray());

        Assert.Equal(new short[] { 9 }, profile.GetValue(ExifTag.TimeZoneOffset).Value);
    }

    [Fact]
    public void TimeZoneOffset_StoredAsLongPair_ReadsValues()
    {
        var builder = new ExifBuilder(false);
        builder.WriteIfd((ExifTagValue.SubIFDOffset, ExifDataType.Long, 1, builder.Long(26)));
        builder.WriteIfd((ExifTagValue.TimeZoneOffset, ExifDataType.Long, 2, builder.Long(44)));
        builder.WriteBytes(builder.Long(9));
        builder.WriteBytes(builder.Long(10));

        var profile = new ExifProfile(builder.ToArray());

        Assert.Equal(new short[] { 9, 10 }, profile.GetValue(ExifTag.TimeZoneOffset).Value);
    }

    [Theory]
    [InlineData(40000U)]
    [InlineData(0xFFFFFFF7U)]
    public void TimeZoneOffset_StoredAsLongOutOfRange_IsSkipped(uint offset)
    {
        // LONG is unsigned, so a large value must not be reinterpreted as a negative offset.
        var builder = new ExifBuilder(false);
        builder.WriteIfd((ExifTagValue.SubIFDOffset, ExifDataType.Long, 1, builder.Long(26)));
        builder.WriteIfd((ExifTagValue.TimeZoneOffset, ExifDataType.Long, 1, builder.Long(offset)));

        var profile = new ExifProfile(builder.ToArray());

        Assert.Null(profile.GetValue(ExifTag.TimeZoneOffset));
    }

    internal static ExifProfile GetExifProfile()
    {
        using Image<Rgba32> image = TestFile.Create(TestImages.Jpeg.Baseline.Floorplan).CreateRgba32Image();

        ExifProfile profile = image.Metadata.ExifProfile;
        Assert.NotNull(profile);

        return profile;
    }

    private static Image<Rgba32> WriteAndRead(Image<Rgba32> image, TestImageWriteFormat imageFormat)
    {
        switch (imageFormat)
        {
            case TestImageWriteFormat.Jpeg:
                return WriteAndReadJpeg(image);
            case TestImageWriteFormat.Png:
                return WriteAndReadPng(image);
            case TestImageWriteFormat.WebpLossless:
                return WriteAndReadWebp(image, WebpFileFormatType.Lossless);
            case TestImageWriteFormat.WebpLossy:
                return WriteAndReadWebp(image, WebpFileFormatType.Lossy);
            case TestImageWriteFormat.Tiff:
                return WriteAndReadTiff(image);
            default:
                throw new ArgumentException("Unexpected test image format");
        }
    }

    private static Image<Rgba32> WriteAndReadJpeg(Image<Rgba32> image)
    {
        using (var memStream = new MemoryStream())
        {
            image.SaveAsJpeg(memStream);
            image.Dispose();

            memStream.Position = 0;
            return Image.Load<Rgba32>(memStream);
        }
    }

    private static Image<Rgba32> WriteAndReadPng(Image<Rgba32> image)
    {
        using (var memStream = new MemoryStream())
        {
            image.SaveAsPng(memStream);
            image.Dispose();

            memStream.Position = 0;
            return Image.Load<Rgba32>(memStream);
        }
    }

    private static Image<Rgba32> WriteAndReadTiff(Image<Rgba32> image)
    {
        using (var memStream = new MemoryStream())
        {
            image.SaveAsTiff(memStream);
            image.Dispose();

            memStream.Position = 0;
            return Image.Load<Rgba32>(memStream);
        }
    }

    private static Image<Rgba32> WriteAndReadWebp(Image<Rgba32> image, WebpFileFormatType fileFormat)
    {
        using (var memStream = new MemoryStream())
        {
            image.SaveAsWebp(memStream, new WebpEncoder() { FileFormat = fileFormat });
            image.Dispose();

            memStream.Position = 0;
            return Image.Load<Rgba32>(memStream, new WebpDecoder());
        }
    }

    private static void TestProfile(ExifProfile profile)
    {
        Assert.NotNull(profile);

        // todo: duplicate tags
        Assert.Equal(2, profile.Values.Count(v => (ushort)v.Tag == 59932));

        Assert.Equal(18, profile.Values.Count);

        foreach (IExifValue value in profile.Values)
        {
            Assert.NotNull(value.GetValue());
        }

        IExifValue<string> software = profile.GetValue(ExifTag.Software);
        Assert.Equal("Windows Photo Editor 10.0.10011.16384", software.Value);

        IExifValue<Rational> xResolution = profile.GetValue(ExifTag.XResolution);
        Assert.Equal(new Rational(300.0), xResolution.Value);

        IExifValue<Number> xDimension = profile.GetValue(ExifTag.PixelXDimension);
        Assert.Equal(2338U, xDimension.Value);
    }

    private static EncodedString GetEncodedString(ExifProfile profile, ushort tag)
    {
        IExifValue value = profile.Values.Single(v => (ushort)v.Tag == tag);
        return (EncodedString)value.GetValue();
    }

    // IFD0 at offset 8 points to a sub-IFD at offset 26, whose only entry is the encoded string stored at offset 44.
    private static byte[] CreateEncodedStringExif(bool bigEndian, ushort pointerTag, ushort tag, string text)
    {
        Encoding encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        byte[] value = Encoding.ASCII.GetBytes("UNICODE\0").Concat(encoding.GetBytes(text)).ToArray();

        var builder = new ExifBuilder(bigEndian);
        builder.WriteIfd(((ExifTagValue)pointerTag, ExifDataType.Long, 1, builder.Long(26)));
        builder.WriteIfd(((ExifTagValue)tag, ExifDataType.Undefined, (uint)value.Length, builder.Long(44)));
        builder.WriteBytes(value);
        return builder.ToArray();
    }

    /// <summary>
    /// Writes raw EXIF data in either byte order: a TIFF header pointing to an IFD at offset 8, then whatever is written.
    /// </summary>
    private sealed class ExifBuilder
    {
        private readonly bool bigEndian;
        private readonly List<byte> data = new();

        public ExifBuilder(bool bigEndian)
        {
            this.bigEndian = bigEndian;
            this.WriteBytes(bigEndian ? new byte[] { 0x4D, 0x4D } : new byte[] { 0x49, 0x49 });
            this.WriteBytes(this.Short(0x002A));
            this.WriteBytes(this.Long(8));
        }

        public byte[] Short(ushort value)
        {
            byte[] bytes = new byte[2];
            if (this.bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            }

            return bytes;
        }

        public byte[] Long(uint value)
        {
            byte[] bytes = new byte[4];
            if (this.bigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            }

            return bytes;
        }

        /// <summary>
        /// Writes an IFD of 2 + (12 * entry count) + 4 bytes with no next IFD.
        /// Each entry value is the raw 4-byte value or offset field.
        /// </summary>
        public void WriteIfd(params (ExifTagValue Tag, ExifDataType DataType, uint Count, byte[] Value)[] entries)
        {
            this.WriteBytes(this.Short((ushort)entries.Length));
            foreach ((ExifTagValue tag, ExifDataType dataType, uint count, byte[] value) in entries)
            {
                this.WriteBytes(this.Short((ushort)tag));
                this.WriteBytes(this.Short((ushort)dataType));
                this.WriteBytes(this.Long(count));
                this.WriteBytes(value);
            }

            this.WriteBytes(this.Long(0));
        }

        public void WriteBytes(byte[] bytes) => this.data.AddRange(bytes);

        public byte[] ToArray() => this.data.ToArray();
    }
}
