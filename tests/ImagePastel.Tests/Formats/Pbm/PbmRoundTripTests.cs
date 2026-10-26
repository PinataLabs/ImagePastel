// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using PinataLabs.ImagePastel.Formats.Pbm;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;
using static PinataLabs.ImagePastel.Tests.TestImages.Pbm;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Pbm;

[Trait("Format", "Pbm")]
public class PbmRoundTripTests
{
    [Theory]
    [InlineData(BlackAndWhitePlain)]
    [InlineData(BlackAndWhiteBinary)]
    [InlineData(GrayscalePlain)]
    [InlineData(GrayscalePlainNormalized)]
    [InlineData(GrayscalePlainMagick)]
    [InlineData(GrayscaleBinary)]
    public void PbmGrayscaleImageCanRoundTrip(string imagePath)
    {
        // Arrange
        var testFile = TestFile.Create(imagePath);
        using var stream = new MemoryStream(testFile.Bytes, false);

        // Act
        using var originalImage = Image.Load(stream);
        using Image<Rgb24> colorImage = originalImage.CloneAs<Rgb24>();
        using Image<Rgb24> encodedImage = this.RoundTrip(colorImage);

        // Assert
        Assert.NotNull(encodedImage);
        ImageComparer.Exact.VerifySimilarity(colorImage, encodedImage);
    }

    [Theory]
    [InlineData(RgbPlain)]
    [InlineData(RgbPlainNormalized)]
    [InlineData(RgbPlainMagick)]
    [InlineData(RgbBinary)]
    public void PbmColorImageCanRoundTrip(string imagePath)
    {
        // Arrange
        var testFile = TestFile.Create(imagePath);
        using var stream = new MemoryStream(testFile.Bytes, false);

        // Act
        using var originalImage = Image.Load<Rgb24>(stream);
        using Image<Rgb24> encodedImage = this.RoundTrip(originalImage);

        // Assert
        Assert.NotNull(encodedImage);
        ImageComparer.Exact.VerifySimilarity(originalImage, encodedImage);
    }

    [Theory]
    [InlineData(1, PbmEncoding.Plain)]
    [InlineData(7, PbmEncoding.Plain)]
    [InlineData(8, PbmEncoding.Plain)]
    [InlineData(9, PbmEncoding.Plain)]
    [InlineData(10, PbmEncoding.Plain)]
    [InlineData(13, PbmEncoding.Plain)]
    [InlineData(17, PbmEncoding.Plain)]
    [InlineData(1, PbmEncoding.Binary)]
    [InlineData(7, PbmEncoding.Binary)]
    [InlineData(8, PbmEncoding.Binary)]
    [InlineData(9, PbmEncoding.Binary)]
    [InlineData(10, PbmEncoding.Binary)]
    [InlineData(13, PbmEncoding.Binary)]
    [InlineData(17, PbmEncoding.Binary)]
    public void PbmBlackAndWhiteImageCanRoundTripAtAnyWidth(int width, PbmEncoding encoding)
    {
        // Arrange
        const int height = 3;
        using Image<L8> originalImage = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                originalImage[x, y] = ((x * 7) + (y * 3)) % 5 < 2 ? new L8(0) : new L8(255);
            }
        }

        var encoder = new PbmEncoder
        {
            ColorType = PbmColorType.BlackAndWhite,
            ComponentType = PbmComponentType.Byte,
            Encoding = encoding
        };

        // Act
        using var stream = new MemoryStream();
        originalImage.Save(stream, encoder);
        stream.Position = 0;
        using Image<L8> encodedImage = Image.Load<L8>(stream);

        // Assert
        ImageComparer.Exact.VerifySimilarity(originalImage, encodedImage);
    }

    private Image<TPixel> RoundTrip<TPixel>(Image<TPixel> originalImage)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using var decodedStream = new MemoryStream();
        originalImage.SaveAsPbm(decodedStream);
        decodedStream.Seek(0, SeekOrigin.Begin);
        var encodedImage = Image.Load<TPixel>(decodedStream);
        return encodedImage;
    }
}
