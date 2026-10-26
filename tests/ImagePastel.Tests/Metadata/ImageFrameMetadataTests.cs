// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Gif;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.Metadata.Profiles.Xmp;
using Xunit;
using ExifProfile = PinataLabs.ImagePastel.Metadata.Profiles.Exif.ExifProfile;
using ExifTag = PinataLabs.ImagePastel.Metadata.Profiles.Exif.ExifTag;

namespace PinataLabs.ImagePastel.Tests.Metadata;

/// <summary>
/// Tests the <see cref="ImageFrameMetadataTests"/> class.
/// </summary>
public class ImageFrameMetadataTests
{
    [Fact]
    public void ConstructorImageFrameMetadata()
    {
        const int frameDelay = 42;
        const int colorTableLength = 128;
        const GifDisposalMethod disposalMethod = GifDisposalMethod.RestoreToBackground;

        var metaData = new ImageFrameMetadata();
        GifFrameMetadata gifFrameMetadata = metaData.GetGifMetadata();
        gifFrameMetadata.FrameDelay = frameDelay;
        gifFrameMetadata.ColorTableLength = colorTableLength;
        gifFrameMetadata.DisposalMethod = disposalMethod;

        var clone = new ImageFrameMetadata(metaData);
        GifFrameMetadata cloneGifFrameMetadata = clone.GetGifMetadata();

        Assert.Equal(frameDelay, cloneGifFrameMetadata.FrameDelay);
        Assert.Equal(colorTableLength, cloneGifFrameMetadata.ColorTableLength);
        Assert.Equal(disposalMethod, cloneGifFrameMetadata.DisposalMethod);
    }

    [Fact]
    public void CloneIsDeep()
    {
        // arrange
        var exifProfile = new ExifProfile();
        exifProfile.SetValue(ExifTag.Software, "UnitTest");
        exifProfile.SetValue(ExifTag.Artist, "UnitTest");
        var xmpProfile = new XmpProfile(new byte[0]);
        var iccProfile = new IccProfile()
        {
            Header = new IccProfileHeader()
            {
                CmmType = "Unittest"
            }
        };
        var iptcProfile = new ImagePastel.Metadata.Profiles.Iptc.IptcProfile();
        var metaData = new ImageFrameMetadata()
        {
            XmpProfile = xmpProfile,
            ExifProfile = exifProfile,
            IccProfile = iccProfile,
            IptcProfile = iptcProfile
        };

        // act
        ImageFrameMetadata clone = metaData.DeepClone();

        // assert
        Assert.NotNull(clone);
        Assert.NotNull(clone.ExifProfile);
        Assert.NotNull(clone.XmpProfile);
        Assert.NotNull(clone.IccProfile);
        Assert.NotNull(clone.IptcProfile);
        Assert.False(metaData.ExifProfile.Equals(clone.ExifProfile));
        Assert.True(metaData.ExifProfile.Values.Count == clone.ExifProfile.Values.Count);
        Assert.False(ReferenceEquals(metaData.XmpProfile, clone.XmpProfile));
        Assert.True(metaData.XmpProfile.Data.Equals(clone.XmpProfile.Data));
        Assert.False(metaData.GetGifMetadata().Equals(clone.GetGifMetadata()));
        Assert.False(metaData.IccProfile.Equals(clone.IccProfile));
        Assert.False(metaData.IptcProfile.Equals(clone.IptcProfile));
    }
}
