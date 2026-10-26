// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Dithering;
using PinataLabs.ImagePastel.Processing.Processors.Quantization;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Processors.Quantization;

[Trait("Category", "Processors")]
public class QuantizerTests
{
    /// <summary>
    /// Something is causing tests to fail on NETFX in CI.
    /// Could be a JIT error as everything runs well and is identical to .NET Core output.
    /// Not worth investigating for now.
    /// <see href="https://github.com/SixLabors/ImageSharp/pull/1114/checks?check_run_id=448891164#step:11:631"/>
    /// </summary>
    private static readonly bool SkipAllQuantizerTests = TestEnvironment.IsFramework;

    public static readonly string[] CommonTestImages =
    {
        TestImages.Png.CalliphoraPartial,
        TestImages.Png.Bike
    };

    private static readonly QuantizerOptions NoDitherOptions = new QuantizerOptions { Dither = null };
    private static readonly QuantizerOptions DiffuserDitherOptions = new QuantizerOptions { Dither = KnownDitherings.FloydSteinberg };
    private static readonly QuantizerOptions OrderedDitherOptions = new QuantizerOptions { Dither = KnownDitherings.Bayer8x8 };

    private static readonly QuantizerOptions Diffuser0_ScaleDitherOptions = new QuantizerOptions
    {
        Dither = KnownDitherings.FloydSteinberg,
        DitherScale = 0F
    };

    private static readonly QuantizerOptions Diffuser0_25_ScaleDitherOptions = new QuantizerOptions
    {
        Dither = KnownDitherings.FloydSteinberg,
        DitherScale = .25F
    };

    private static readonly QuantizerOptions Diffuser0_5_ScaleDitherOptions = new QuantizerOptions
    {
        Dither = KnownDitherings.FloydSteinberg,
        DitherScale = .5F
    };

    private static readonly QuantizerOptions Diffuser0_75_ScaleDitherOptions = new QuantizerOptions
    {
        Dither = KnownDitherings.FloydSteinberg,
        DitherScale = .75F
    };

    private static readonly QuantizerOptions Ordered0_ScaleDitherOptions = new QuantizerOptions
    {
        Dither = KnownDitherings.Bayer8x8,
        DitherScale = 0F
    };

    private static readonly QuantizerOptions Ordered0_25_ScaleDitherOptions = new QuantizerOptions
    {
        Dither = KnownDitherings.Bayer8x8,
        DitherScale = .25F
    };

    private static readonly QuantizerOptions Ordered0_5_ScaleDitherOptions = new QuantizerOptions
    {
        Dither = KnownDitherings.Bayer8x8,
        DitherScale = .5F
    };

    private static readonly QuantizerOptions Ordered0_75_ScaleDitherOptions = new QuantizerOptions
    {
        Dither = KnownDitherings.Bayer8x8,
        DitherScale = .75F
    };

    public static readonly TheoryData<IQuantizer> Quantizers
    = new TheoryData<IQuantizer>(new IQuantizer[]
    {
        // Known uses error diffusion by default.
        KnownQuantizers.Octree,
        KnownQuantizers.WebSafe,
        KnownQuantizers.Werner,
        KnownQuantizers.Wu,
        new OctreeQuantizer(NoDitherOptions),
        new WebSafePaletteQuantizer(NoDitherOptions),
        new WernerPaletteQuantizer(NoDitherOptions),
        new WuQuantizer(NoDitherOptions),
        new OctreeQuantizer(OrderedDitherOptions),
        new WebSafePaletteQuantizer(OrderedDitherOptions),
        new WernerPaletteQuantizer(OrderedDitherOptions),
        new WuQuantizer(OrderedDitherOptions)
    });

    public static readonly TheoryData<IQuantizer> DitherScaleQuantizers
    = new TheoryData<IQuantizer>(new IQuantizer[]
    {
        new OctreeQuantizer(Diffuser0_ScaleDitherOptions),
        new WebSafePaletteQuantizer(Diffuser0_ScaleDitherOptions),
        new WernerPaletteQuantizer(Diffuser0_ScaleDitherOptions),
        new WuQuantizer(Diffuser0_ScaleDitherOptions),

        new OctreeQuantizer(Diffuser0_25_ScaleDitherOptions),
        new WebSafePaletteQuantizer(Diffuser0_25_ScaleDitherOptions),
        new WernerPaletteQuantizer(Diffuser0_25_ScaleDitherOptions),
        new WuQuantizer(Diffuser0_25_ScaleDitherOptions),

        new OctreeQuantizer(Diffuser0_5_ScaleDitherOptions),
        new WebSafePaletteQuantizer(Diffuser0_5_ScaleDitherOptions),
        new WernerPaletteQuantizer(Diffuser0_5_ScaleDitherOptions),
        new WuQuantizer(Diffuser0_5_ScaleDitherOptions),

        new OctreeQuantizer(Diffuser0_75_ScaleDitherOptions),
        new WebSafePaletteQuantizer(Diffuser0_75_ScaleDitherOptions),
        new WernerPaletteQuantizer(Diffuser0_75_ScaleDitherOptions),
        new WuQuantizer(Diffuser0_75_ScaleDitherOptions),

        new OctreeQuantizer(DiffuserDitherOptions),
        new WebSafePaletteQuantizer(DiffuserDitherOptions),
        new WernerPaletteQuantizer(DiffuserDitherOptions),
        new WuQuantizer(DiffuserDitherOptions),

        new OctreeQuantizer(Ordered0_ScaleDitherOptions),
        new WebSafePaletteQuantizer(Ordered0_ScaleDitherOptions),
        new WernerPaletteQuantizer(Ordered0_ScaleDitherOptions),
        new WuQuantizer(Ordered0_ScaleDitherOptions),

        new OctreeQuantizer(Ordered0_25_ScaleDitherOptions),
        new WebSafePaletteQuantizer(Ordered0_25_ScaleDitherOptions),
        new WernerPaletteQuantizer(Ordered0_25_ScaleDitherOptions),
        new WuQuantizer(Ordered0_25_ScaleDitherOptions),

        new OctreeQuantizer(Ordered0_5_ScaleDitherOptions),
        new WebSafePaletteQuantizer(Ordered0_5_ScaleDitherOptions),
        new WernerPaletteQuantizer(Ordered0_5_ScaleDitherOptions),
        new WuQuantizer(Ordered0_5_ScaleDitherOptions),

        new OctreeQuantizer(Ordered0_75_ScaleDitherOptions),
        new WebSafePaletteQuantizer(Ordered0_75_ScaleDitherOptions),
        new WernerPaletteQuantizer(Ordered0_75_ScaleDitherOptions),
        new WuQuantizer(Ordered0_75_ScaleDitherOptions),

        new OctreeQuantizer(OrderedDitherOptions),
        new WebSafePaletteQuantizer(OrderedDitherOptions),
        new WernerPaletteQuantizer(OrderedDitherOptions),
        new WuQuantizer(OrderedDitherOptions),
    });

    public static readonly TheoryData<IDither> DefaultInstanceDitherers
        = new TheoryData<IDither>(new IDither[]
        {
            default(ErrorDither),
            default(OrderedDither)
        });

    private static readonly ImageComparer ValidatorComparer = ImageComparer.TolerantPercentage(0.05F);

    [Theory]
    [WithFileCollection(nameof(CommonTestImages), nameof(Quantizers), PixelTypes.Rgba32)]
    public void ApplyQuantizationInBox<TPixel>(TestImageProvider<TPixel> provider, IQuantizer quantizer)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (SkipAllQuantizerTests)
        {
            return;
        }

        string quantizerName = quantizer.GetType().Name;
        string ditherName = quantizer.Options.Dither?.GetType()?.Name ?? "NoDither";
        string testOutputDetails = $"{quantizerName}_{ditherName}";

        provider.RunRectangleConstrainedValidatingProcessorTest(
            (x, rect) => x.Quantize(quantizer, rect),
            testOutputDetails: testOutputDetails,
            comparer: ValidatorComparer,
            appendPixelTypeToFileName: false);
    }

    [Theory]
    [WithFileCollection(nameof(CommonTestImages), nameof(Quantizers), PixelTypes.Rgba32)]
    public void ApplyQuantization<TPixel>(TestImageProvider<TPixel> provider, IQuantizer quantizer)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (SkipAllQuantizerTests)
        {
            return;
        }

        string quantizerName = quantizer.GetType().Name;
        string ditherName = quantizer.Options.Dither?.GetType()?.Name ?? "NoDither";
        string testOutputDetails = $"{quantizerName}_{ditherName}";

        provider.RunValidatingProcessorTest(
            x => x.Quantize(quantizer),
            comparer: ValidatorComparer,
            testOutputDetails: testOutputDetails,
            appendPixelTypeToFileName: false);
    }

    [Theory]
    [WithFile(TestImages.Png.David, nameof(DitherScaleQuantizers), PixelTypes.Rgba32)]
    public void ApplyQuantizationWithDitheringScale<TPixel>(TestImageProvider<TPixel> provider, IQuantizer quantizer)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (SkipAllQuantizerTests)
        {
            return;
        }

        string quantizerName = quantizer.GetType().Name;
        string ditherName = quantizer.Options.Dither.GetType().Name;
        float ditherScale = quantizer.Options.DitherScale;
        string testOutputDetails = FormattableString.Invariant($"{quantizerName}_{ditherName}_{ditherScale}");

        provider.RunValidatingProcessorTest(
            x => x.Quantize(quantizer),
            comparer: ValidatorComparer,
            testOutputDetails: testOutputDetails,
            appendPixelTypeToFileName: false);
    }

    [Theory]
    [InlineData(0, 0, 20, 8)]
    [InlineData(3, 5, 9, 6)]
    [InlineData(11, 10, 9, 6)]
    [InlineData(11, 10, 20, 20)]
    public void QuantizeInRectangleChangesOnlyPixelsInside(int x, int y, int width, int height)
    {
        Rgba32 dark = new(30, 30, 30);
        Rgba32 light = new(220, 220, 220);
        static bool IsDark(int px, int py) => ((px * 7) + (py * 3)) % 5 < 2;

        using var image = new Image<Rgba32>(20, 16);
        for (int py = 0; py < image.Height; py++)
        {
            for (int px = 0; px < image.Width; px++)
            {
                image[px, py] = IsDark(px, py) ? dark : light;
            }
        }

        var rectangle = new Rectangle(x, y, width, height);
        var quantizer = new PaletteQuantizer(new[] { Color.White, Color.Black }, NoDitherOptions);
        image.Mutate(c => c.Quantize(quantizer, rectangle));

        Rgba32 black = Color.Black.ToPixel<Rgba32>();
        Rgba32 white = Color.White.ToPixel<Rgba32>();
        for (int py = 0; py < image.Height; py++)
        {
            for (int px = 0; px < image.Width; px++)
            {
                Rgba32 expected = rectangle.Contains(px, py)
                    ? (IsDark(px, py) ? black : white)
                    : (IsDark(px, py) ? dark : light);

                Assert.True(expected == image[px, py], $"Pixel ({px}, {py}): expected {expected}, got {image[px, py]}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(DefaultInstanceDitherers))]
    public void ShouldThrowForDefaultDitherInstance(IDither dither)
    {
        void Command()
        {
            using var image = new Image<Rgba32>(10, 10);
            var quantizer = new WebSafePaletteQuantizer();
            quantizer.Options.Dither = dither;
            image.Mutate(x => x.Quantize(quantizer));
        }

        Assert.Throws<ImageProcessingException>(Command);
    }
}
