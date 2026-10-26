// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Bmp;
using PinataLabs.ImagePastel.Formats.Gif;
using PinataLabs.ImagePastel.Formats.Jpeg;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Pbm;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.Formats.Tga;
using PinataLabs.ImagePastel.Formats.Tiff;
using PinataLabs.ImagePastel.Formats.Webp;
using PinataLabs.ImagePastel.Tests.Formats.Jxl;
using PinataLabs.ImagePastel.Tests.TestUtilities.ReferenceCodecs;

namespace PinataLabs.ImagePastel.Tests;

public static partial class TestEnvironment
{
    private static readonly Lazy<Configuration> ConfigurationLazy = new Lazy<Configuration>(CreateDefaultConfiguration);

    internal static Configuration Configuration => ConfigurationLazy.Value;

    internal static IImageDecoder GetReferenceDecoder(string filePath)
    {
        IImageFormat format = GetImageFormat(filePath);
        return Configuration.ImageFormatsManager.FindDecoder(format);
    }

    internal static IImageEncoder GetReferenceEncoder(string filePath)
    {
        IImageFormat format = GetImageFormat(filePath);
        return Configuration.ImageFormatsManager.FindEncoder(format);
    }

    internal static IImageFormat GetImageFormat(string filePath)
    {
        string extension = Path.GetExtension(filePath);

        return Configuration.ImageFormatsManager.FindFormatByFileExtension(extension);
    }

    private static void ConfigureCodecs(
        this Configuration cfg,
        IImageFormat imageFormat,
        IImageDecoder decoder,
        IImageEncoder encoder,
        IImageFormatDetector detector)
    {
        cfg.ImageFormatsManager.SetDecoder(imageFormat, decoder);
        cfg.ImageFormatsManager.SetEncoder(imageFormat, encoder);
        cfg.ImageFormatsManager.AddImageFormatDetector(detector);
    }

    private static Configuration CreateDefaultConfiguration()
    {
        var cfg = new Configuration(
            new JpegConfigurationModule(),
            new GifConfigurationModule(),
            new PbmConfigurationModule(),
            new TgaConfigurationModule(),
            new WebpConfigurationModule(),
            new TiffConfigurationModule());

        IImageEncoder pngEncoder = IsWindows ? (IImageEncoder)SystemDrawingReferenceEncoder.Png : new ImageSharpPngEncoderWithDefaultConfiguration();
        IImageEncoder bmpEncoder = IsWindows ? (IImageEncoder)SystemDrawingReferenceEncoder.Bmp : new BmpEncoder();

        // Magick codecs should work on all platforms
        cfg.ConfigureCodecs(
            PngFormat.Instance,
            MagickReferenceDecoder.Instance,
            pngEncoder,
            new PngImageFormatDetector());

        cfg.ConfigureCodecs(
            BmpFormat.Instance,
            IsWindows ? (IImageDecoder)SystemDrawingReferenceDecoder.Instance : MagickReferenceDecoder.Instance,
            bmpEncoder,
            new BmpImageFormatDetector());

        // Magick reads JPEG XL through 16-bit samples, so 9- to 16-bit images keep their precision. The
        // reference encoder is lossless at Fastest, so a test that saves and reloads through this
        // configuration compares exact pixels; the default encoder is lossy.
        cfg.ImageFormatsManager.SetDecoder(JxlFormat.Instance, MagickReferenceDecoder.HighBitDepthInstance);
        cfg.ImageFormatsManager.SetEncoder(JxlFormat.Instance, JxlEncoderRoundTrip.LosslessFastest);
        cfg.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());

        return cfg;
    }
}
