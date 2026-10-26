// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Gif;

using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Gif.Sections;

public class GifLogicalScreenDescriptorTests
{
    [Fact]
    public void TestPackedValue()
    {
        Assert.Equal(0, GifLogicalScreenDescriptor.GetPackedValue(false, 0, false, 0));
        Assert.Equal(128, GifLogicalScreenDescriptor.GetPackedValue(true, 0, false, 0)); // globalColorTableFlag
        Assert.Equal(8, GifLogicalScreenDescriptor.GetPackedValue(false, 0, true, 0)); // sortFlag
        Assert.Equal(48, GifLogicalScreenDescriptor.GetPackedValue(false, 3, false, 0));
        Assert.Equal(155, GifLogicalScreenDescriptor.GetPackedValue(true, 1, true, 3));
        Assert.Equal(55, GifLogicalScreenDescriptor.GetPackedValue(false, 3, false, 7));
    }
}
