// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using Xunit;

namespace PinataLabs.ImagePastel.Tests;

public class ImageRotationTests
{
    [Fact]
    public void RotateImageByMinus90Degrees()
    {
        (Size original, Size rotated) = Rotate(-90);
        Assert.Equal(new Size(original.Height, original.Width), rotated);
    }

    [Fact]
    public void RotateImageBy90Degrees()
    {
        (Size original, Size rotated) = Rotate(90);
        Assert.Equal(new Size(original.Height, original.Width), rotated);
    }

    [Fact]
    public void RotateImageBy180Degrees()
    {
        (Size original, Size rotated) = Rotate(180);
        Assert.Equal(original, rotated);
    }

    [Fact]
    public void RotateImageBy270Degrees()
    {
        (Size original, Size rotated) = Rotate(270);
        Assert.Equal(new Size(original.Height, original.Width), rotated);
    }

    [Fact]
    public void RotateImageBy360Degrees()
    {
        (Size original, Size rotated) = Rotate(360);
        Assert.Equal(original, rotated);
    }

    private static (Size Original, Size Rotated) Rotate(int angle)
    {
        var file = TestFile.Create(TestImages.Bmp.Car);
        using (var image = Image.Load<Rgba32>(file.FullPath))
        {
            Size original = image.Size();
            image.Mutate(x => x.Rotate(angle));
            return (original, image.Size());
        }
    }
}
