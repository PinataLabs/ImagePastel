// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Metadata.Profiles.IPTC;

public class IptcProfileTests
{
    private static JpegDecoder JpegDecoder => new JpegDecoder() { IgnoreMetadata = false };

    private static TiffDecoder TiffDecoder => new TiffDecoder() { IgnoreMetadata = false };

    public static IEnumerable<object[]> AllIptcTags()
    {
        foreach (object tag in Enum.GetValues(typeof(IptcTag)))
        {
            yield return new object[] { tag };
        }
    }

    [Theory]
    [MemberData(nameof(AllIptcTags))]
    public void IptcProfile_SetValue_WithStrictEnabled_Works(IptcTag tag)
    {
        // arrange
        var profile = new IptcProfile();
        var value = new string('s', tag.MaxLength() + 1);
        int expectedLength = tag.MaxLength();

        // act
        profile.SetValue(tag, value);

        // assert
        IptcValue actual = profile.GetValues(tag).First();
        Assert.Equal(expectedLength, actual.Value.Length);
    }

    [Theory]
    [MemberData(nameof(AllIptcTags))]
    public void IptcProfile_SetValue_WithStrictDisabled_Works(IptcTag tag)
    {
        // arrange
        var profile = new IptcProfile();
        var value = new string('s', tag.MaxLength() + 1);
        int expectedLength = value.Length;

        // act
        profile.SetValue(tag, value, false);

        // assert
        IptcValue actual = profile.GetValues(tag).First();
        Assert.Equal(expectedLength, actual.Value.Length);
    }

    [Theory]
    [InlineData(IptcTag.DigitalCreationDate)]
    [InlineData(IptcTag.ExpirationDate)]
    [InlineData(IptcTag.CreatedDate)]
    [InlineData(IptcTag.ReferenceDate)]
    [InlineData(IptcTag.ReleaseDate)]
    public void IptcProfile_SetDateValue_Works(IptcTag tag)
    {
        // arrange
        var profile = new IptcProfile();
        var datetime = new DateTimeOffset(new DateTime(1994, 3, 17));

        // act
        profile.SetDateTimeValue(tag, datetime);

        // assert
        IptcValue actual = profile.GetValues(tag).First();
        Assert.Equal("19940317", actual.Value);
    }

    [Theory]
    [InlineData(IptcTag.CreatedTime)]
    [InlineData(IptcTag.DigitalCreationTime)]
    [InlineData(IptcTag.ExpirationTime)]
    [InlineData(IptcTag.ReleaseTime)]
    public void IptcProfile_SetTimeValue_Works(IptcTag tag)
    {
        // arrange
        var profile = new IptcProfile();
        var dateTimeUtc = new DateTime(1994, 3, 17, 14, 15, 16, DateTimeKind.Utc);
        DateTimeOffset dateTimeOffset = new DateTimeOffset(dateTimeUtc).ToOffset(TimeSpan.FromHours(2));

        // act
        profile.SetDateTimeValue(tag, dateTimeOffset);

        // assert
        IptcValue actual = profile.GetValues(tag).First();
        Assert.Equal("161516+0200", actual.Value);
    }

    [Theory]
    [WithFile(TestImages.Jpeg.Baseline.Iptc, PixelTypes.Rgba32)]
    public void ReadIptcMetadata_FromJpg_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(JpegDecoder))
        {
            Assert.NotNull(image.Metadata.IptcProfile);
            var iptcValues = image.Metadata.IptcProfile.Values.ToList();
            IptcProfileContainsExpectedValues(iptcValues);
        }
    }

    [Theory]
    [WithFile(TestImages.Tiff.IptcData, PixelTypes.Rgba32)]
    public void ReadIptcMetadata_FromTiff_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(TiffDecoder))
        {
            IptcProfile iptc = image.Frames.RootFrame.Metadata.IptcProfile;
            Assert.NotNull(iptc);
            var iptcValues = iptc.Values.ToList();
            IptcProfileContainsExpectedValues(iptcValues);
        }
    }

    private static void IptcProfileContainsExpectedValues(List<IptcValue> iptcValues)
    {
        ContainsIptcValue(iptcValues, IptcTag.Caption, "description");
        ContainsIptcValue(iptcValues, IptcTag.CaptionWriter, "description writer");
        ContainsIptcValue(iptcValues, IptcTag.Headline, "headline");
        ContainsIptcValue(iptcValues, IptcTag.SpecialInstructions, "special instructions");
        ContainsIptcValue(iptcValues, IptcTag.Byline, "author");
        ContainsIptcValue(iptcValues, IptcTag.BylineTitle, "author title");
        ContainsIptcValue(iptcValues, IptcTag.Credit, "credits");
        ContainsIptcValue(iptcValues, IptcTag.Source, "source");
        ContainsIptcValue(iptcValues, IptcTag.Name, "title");
        ContainsIptcValue(iptcValues, IptcTag.CreatedDate, "20200414");
        ContainsIptcValue(iptcValues, IptcTag.City, "city");
        ContainsIptcValue(iptcValues, IptcTag.SubLocation, "sublocation");
        ContainsIptcValue(iptcValues, IptcTag.ProvinceState, "province-state");
        ContainsIptcValue(iptcValues, IptcTag.Country, "country");
        ContainsIptcValue(iptcValues, IptcTag.Category, "category");
        ContainsIptcValue(iptcValues, IptcTag.Urgency, "1");
        ContainsIptcValue(iptcValues, IptcTag.Keywords, "keywords");
        ContainsIptcValue(iptcValues, IptcTag.CopyrightNotice, "copyright");
    }

    [Theory]
    [WithFile(TestImages.Jpeg.Baseline.App13WithEmptyIptc, PixelTypes.Rgba32)]
    public void ReadApp13_WithEmptyIptc_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage(JpegDecoder);
        Assert.Null(image.Metadata.IptcProfile);
    }

    [Fact]
    public void IptcProfile_ToAndFromByteArray_Works()
    {
        // arrange
        var profile = new IptcProfile();
        var expectedCaptionWriter = "unittest";
        var expectedCaption = "test";
        profile.SetValue(IptcTag.CaptionWriter, expectedCaptionWriter);
        profile.SetValue(IptcTag.Caption, expectedCaption);

        // act
        profile.UpdateData();
        byte[] profileBytes = profile.Data;
        var profileFromBytes = new IptcProfile(profileBytes);

        // assert
        var iptcValues = profileFromBytes.Values.ToList();
        ContainsIptcValue(iptcValues, IptcTag.CaptionWriter, expectedCaptionWriter);
        ContainsIptcValue(iptcValues, IptcTag.Caption, expectedCaption);
    }

    [Fact]
    public void IptcProfile_WithUtf8Data_WritesEnvelopeRecord_Works()
    {
        // arrange
        var profile = new IptcProfile();
        profile.SetValue(IptcTag.Caption, "ESPAÑA");
        byte[] expectedEnvelopeData = { 0x1C, 0x01, 0x5A, 0x00, 0x03, 0x1B, 0x25, 0x47 };

        // act
        profile.UpdateData();
        byte[] profileBytes = profile.Data;

        // assert
        Assert.Equal(expectedEnvelopeData, profileBytes.AsSpan(0, expectedEnvelopeData.Length).ToArray());
    }

    [Fact]
    public void IptcProfile_WithAsciiData_DoesNotWriteEnvelopeRecord()
    {
        // arrange
        var profile = new IptcProfile();
        profile.SetValue(IptcTag.Caption, "test");

        // act
        profile.UpdateData();

        // assert
        Assert.Equal(new byte[] { 0x1C, 0x02, (byte)IptcTag.Caption }, profile.Data.AsSpan(0, 3).ToArray());
    }

    [Fact]
    public void IptcProfile_WithMixedEncodings_DoesNotWriteEnvelopeRecord()
    {
        // arrange
        var profile = new IptcProfile();
        profile.SetValue(IptcTag.Caption, "ESPAÑA");
        profile.SetValue(IptcTag.City, Encoding.Latin1, "Málaga");

        // act
        profile.UpdateData();

        // assert
        Assert.Equal(new byte[] { 0x1C, 0x02, (byte)IptcTag.Caption }, profile.Data.AsSpan(0, 3).ToArray());
    }

    [Fact]
    public void IptcProfile_WithDecodedLatin1Data_DoesNotWriteEnvelopeRecord()
    {
        // arrange
        byte[] latin1Profile = { 0x1C, 0x02, (byte)IptcTag.Caption, 0x00, 0x06, 0x45, 0x53, 0x50, 0x41, 0xD1, 0x41 };
        var profile = new IptcProfile(latin1Profile);

        // act
        profile.UpdateData();

        // assert
        Assert.Equal(latin1Profile, profile.Data);
    }

    [Fact]
    public void IptcProfile_WithUtf8Data_ToAndFromByteArray_DoesNotAddEnvelopeValue()
    {
        // arrange
        var profile = new IptcProfile();
        const string expectedCaption = "ESPAÑA";
        profile.SetValue(IptcTag.Caption, expectedCaption);

        // act
        profile.UpdateData();
        var profileFromBytes = new IptcProfile(profile.Data);

        // assert
        IptcValue actual = Assert.Single(profileFromBytes.Values);
        Assert.Equal(IptcTag.Caption, actual.Tag);
        Assert.Equal(expectedCaption, actual.Value);
    }

    [Fact]
    public void IptcProfile_CloneIsDeep()
    {
        // arrange
        var profile = new IptcProfile();
        var captionWriter = "unittest";
        var caption = "test";
        profile.SetValue(IptcTag.CaptionWriter, captionWriter);
        profile.SetValue(IptcTag.Caption, caption);

        // act
        IptcProfile clone = profile.DeepClone();
        clone.SetValue(IptcTag.Caption, "changed");

        // assert
        Assert.Equal(2, clone.Values.Count());
        var cloneValues = clone.Values.ToList();
        ContainsIptcValue(cloneValues, IptcTag.CaptionWriter, captionWriter);
        ContainsIptcValue(cloneValues, IptcTag.Caption, "changed");
        ContainsIptcValue(profile.Values.ToList(), IptcTag.Caption, caption);
    }

    [Fact]
    public void IptcValue_CloneIsDeep()
    {
        // arrange
        var iptcValue = new IptcValue(IptcTag.Caption, System.Text.Encoding.UTF8, "test", true);

        // act
        IptcValue clone = iptcValue.DeepClone();
        clone.Value = "changed";

        // assert
        Assert.NotEqual(iptcValue.Value, clone.Value);
    }

    [Fact]
    public void WritingImage_PreservesIptcProfile()
    {
        // arrange
        var image = new Image<Rgba32>(1, 1);
        image.Metadata.IptcProfile = new IptcProfile();
        var expectedCaptionWriter = "unittest";
        var expectedCaption = "test";
        image.Metadata.IptcProfile.SetValue(IptcTag.CaptionWriter, expectedCaptionWriter);
        image.Metadata.IptcProfile.SetValue(IptcTag.Caption, expectedCaption);

        // act
        using Image<Rgba32> reloadedImage = WriteAndReadJpeg(image);

        // assert
        IptcProfile actual = reloadedImage.Metadata.IptcProfile;
        Assert.NotNull(actual);
        var iptcValues = actual.Values.ToList();
        ContainsIptcValue(iptcValues, IptcTag.CaptionWriter, expectedCaptionWriter);
        ContainsIptcValue(iptcValues, IptcTag.Caption, expectedCaption);
    }

    [Theory]
    [InlineData(IptcTag.ObjectAttribute)]
    [InlineData(IptcTag.SubjectReference)]
    [InlineData(IptcTag.SupplementalCategories)]
    [InlineData(IptcTag.Keywords)]
    [InlineData(IptcTag.LocationCode)]
    [InlineData(IptcTag.LocationName)]
    [InlineData(IptcTag.ReferenceService)]
    [InlineData(IptcTag.ReferenceDate)]
    [InlineData(IptcTag.ReferenceNumber)]
    [InlineData(IptcTag.Byline)]
    [InlineData(IptcTag.BylineTitle)]
    [InlineData(IptcTag.Contact)]
    [InlineData(IptcTag.LocalCaption)]
    [InlineData(IptcTag.CaptionWriter)]
    public void IptcProfile_AddRepeatable_Works(IptcTag tag)
    {
        // arrange
        var profile = new IptcProfile();
        var expectedValue1 = "test";
        var expectedValue2 = "another one";
        profile.SetValue(tag, expectedValue1, false);

        // act
        profile.SetValue(tag, expectedValue2, false);

        // assert
        var values = profile.Values.ToList();
        Assert.Equal(2, values.Count);
        ContainsIptcValue(values, tag, expectedValue1);
        ContainsIptcValue(values, tag, expectedValue2);
    }

    [Theory]
    [InlineData(IptcTag.RecordVersion)]
    [InlineData(IptcTag.ObjectType)]
    [InlineData(IptcTag.Name)]
    [InlineData(IptcTag.EditStatus)]
    [InlineData(IptcTag.EditorialUpdate)]
    [InlineData(IptcTag.Urgency)]
    [InlineData(IptcTag.Category)]
    [InlineData(IptcTag.FixtureIdentifier)]
    [InlineData(IptcTag.ReleaseDate)]
    [InlineData(IptcTag.ReleaseTime)]
    [InlineData(IptcTag.ExpirationDate)]
    [InlineData(IptcTag.ExpirationTime)]
    [InlineData(IptcTag.SpecialInstructions)]
    [InlineData(IptcTag.ActionAdvised)]
    [InlineData(IptcTag.CreatedDate)]
    [InlineData(IptcTag.CreatedTime)]
    [InlineData(IptcTag.DigitalCreationDate)]
    [InlineData(IptcTag.DigitalCreationTime)]
    [InlineData(IptcTag.OriginatingProgram)]
    [InlineData(IptcTag.ProgramVersion)]
    [InlineData(IptcTag.ObjectCycle)]
    [InlineData(IptcTag.City)]
    [InlineData(IptcTag.SubLocation)]
    [InlineData(IptcTag.ProvinceState)]
    [InlineData(IptcTag.CountryCode)]
    [InlineData(IptcTag.Country)]
    [InlineData(IptcTag.OriginalTransmissionReference)]
    [InlineData(IptcTag.Headline)]
    [InlineData(IptcTag.Credit)]
    [InlineData(IptcTag.CopyrightNotice)]
    [InlineData(IptcTag.Caption)]
    [InlineData(IptcTag.ImageType)]
    [InlineData(IptcTag.ImageOrientation)]
    public void IptcProfile_AddNoneRepeatable_DoesOverrideOldValue(IptcTag tag)
    {
        // arrange
        var profile = new IptcProfile();
        var expectedValue = "another one";
        profile.SetValue(tag, "test", false);

        // act
        profile.SetValue(tag, expectedValue, false);

        // assert
        var values = profile.Values.ToList();
        Assert.Equal(1, values.Count);
        ContainsIptcValue(values, tag, expectedValue);
    }

    [Fact]
    public void IptcProfile_RemoveByTag_RemovesAllEntrys()
    {
        // arrange
        var profile = new IptcProfile();
        profile.SetValue(IptcTag.Byline, "test");
        profile.SetValue(IptcTag.Byline, "test2");

        // act
        var result = profile.RemoveValue(IptcTag.Byline);

        // assert
        Assert.True(result, "removed result should be true");
        Assert.Empty(profile.Values);
    }

    [Fact]
    public void IptcProfile_RemoveByTagAndValue_Works()
    {
        // arrange
        var profile = new IptcProfile();
        profile.SetValue(IptcTag.Byline, "test");
        profile.SetValue(IptcTag.Byline, "test2");

        // act
        var result = profile.RemoveValue(IptcTag.Byline, "test2");

        // assert
        Assert.True(result, "removed result should be true");
        ContainsIptcValue(profile.Values.ToList(), IptcTag.Byline, "test");
    }

    [Fact]
    public void IptcProfile_GetValue_RetrievesAllEntries()
    {
        // arrange
        var profile = new IptcProfile();
        profile.SetValue(IptcTag.Byline, "test");
        profile.SetValue(IptcTag.Byline, "test2");
        profile.SetValue(IptcTag.Caption, "test");

        // act
        List<IptcValue> result = profile.GetValues(IptcTag.Byline);

        // assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void IptcProfile_WithUtf8Value_WritesCodedCharacterSetFirst()
    {
        var profile = new IptcProfile();
        profile.SetValue(IptcTag.Caption, "ESPAÑA");

        profile.UpdateData();

        // 1:90 Coded Character Set holding ESC % G (UTF-8), then 2:120 Caption.
        byte[] expected =
        {
            0x1C, 0x01, 0x5A, 0x00, 0x03, 0x1B, 0x25, 0x47,
            0x1C, 0x02, 0x78, 0x00, 0x07, 0x45, 0x53, 0x50, 0x41, 0xC3, 0x91, 0x41
        };
        Assert.Equal(expected, profile.Data);
    }

    [Fact]
    public void IptcProfile_WithUtf8Value_JpegRoundTrip_KeepsValueAndOneCodedCharacterSet()
    {
        var image = new Image<Rgba32>(8, 8);
        image.Metadata.IptcProfile = new IptcProfile();
        image.Metadata.IptcProfile.SetValue(IptcTag.Caption, "ESPAÑA");

        using Image<Rgba32> once = WriteAndReadJpeg(image);
        using Image<Rgba32> twice = WriteAndReadJpeg(once.Clone());

        IptcProfile profile = twice.Metadata.IptcProfile;
        IptcValue value = Assert.Single(profile.Values);
        Assert.Equal(IptcTag.Caption, value.Tag);
        Assert.Equal("ESPAÑA", value.Value);
        Assert.Equal(new byte[] { 0x1C, 0x01, 0x5A, 0x00, 0x03, 0x1B, 0x25, 0x47 }, profile.Data.Take(8));
        Assert.Equal(20, profile.Data.Length);
    }

    [Fact]
    public void IptcProfile_WithAsciiValues_DoesNotWriteCodedCharacterSet()
    {
        var profile = new IptcProfile();
        profile.SetValue(IptcTag.Caption, "SPAIN");
        profile.SetValue(IptcTag.City, "Madrid");

        profile.UpdateData();

        Assert.Equal(new byte[] { 0x1C, 0x02, 0x78, 0x00, 0x05 }, profile.Data.Take(5));
        Assert.Equal(5 + 5 + 5 + 6, profile.Data.Length);
    }

    [Fact]
    public void IptcProfile_WithNonUtf8Value_DoesNotWriteCodedCharacterSet()
    {
        var profile = new IptcProfile();
        profile.SetValue(IptcTag.Caption, "ESPAÑA");
        profile.SetValue(IptcTag.City, System.Text.Encoding.Latin1, "Logroño");

        profile.UpdateData();

        Assert.Equal(new byte[] { 0x1C, 0x02, 0x78 }, profile.Data.Take(3));
    }

    [Fact]
    public void IptcProfile_WithDecodedNonUtf8Bytes_DoesNotWriteCodedCharacterSet()
    {
        // 2:120 Caption holding "ESPAÑA" in Latin-1, with no character set declared.
        byte[] data = { 0x1C, 0x02, 0x78, 0x00, 0x06, 0x45, 0x53, 0x50, 0x41, 0xD1, 0x41 };
        var profile = new IptcProfile(data);

        profile.UpdateData();

        Assert.Equal(data, profile.Data);
    }

    [Fact]
    public void IptcProfile_WithCodedCharacterSet_DoesNotReadItAsValue()
    {
        byte[] data =
        {
            0x1C, 0x01, 0x5A, 0x00, 0x03, 0x1B, 0x25, 0x47,
            0x1C, 0x02, 0x78, 0x00, 0x07, 0x45, 0x53, 0x50, 0x41, 0xC3, 0x91, 0x41
        };

        var profile = new IptcProfile(data);

        IptcValue value = Assert.Single(profile.Values);
        Assert.Equal(IptcTag.Caption, value.Tag);
        Assert.Equal("ESPAÑA", value.Value);
    }

    private static void ContainsIptcValue(List<IptcValue> values, IptcTag tag, string value)
    {
        Assert.True(values.Any(val => val.Tag == tag), $"Missing iptc tag {tag}");
        Assert.True(values.Contains(new IptcValue(tag, System.Text.Encoding.UTF8.GetBytes(value), false)), $"expected iptc value '{value}' was not found for tag '{tag}'");
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
}
