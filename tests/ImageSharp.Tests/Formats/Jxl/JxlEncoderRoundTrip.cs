// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Tests.TestUtilities.ReferenceCodecs;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The encoder round trip: encode an image, then check that our decoder, Magick.NET and djxl each give
/// back exactly the same pixels. Each E1 slice adds its cases to <see cref="Cases"/>; the pixel type
/// names map to pixel types through <see cref="JxlPixelTypes"/>.
/// </summary>
internal static class JxlEncoderRoundTrip
{
    /// <summary>Gets the supported encoder setting: lossless at Fastest.</summary>
    public static JxlEncoder LosslessFastest => new() { Distance = 0, Effort = JxlEffort.Fastest };

    /// <summary>
    /// Gets the round-trip cases: pixel type, pattern (see <see cref="CreateImage{TPixel}"/>), width and height.
    /// </summary>
    public static TheoryData<string, string, int, int> Cases { get; } = CreateCases();

    /// <summary>
    /// Gets the lossless corpus cases: a repo test image (relative to the input images folder) and the
    /// pixel type to load it as.
    /// </summary>
    public static TheoryData<string, string> CorpusCases { get; } = CreateCorpusCases();

    /// <summary>Which decoder reads the encoded file back.</summary>
    public enum Reader
    {
        /// <summary>Our <see cref="JxlDecoder"/>.</summary>
        Ours,

        /// <summary>Magick.NET's libjxl delegate; skips where it has none.</summary>
        Magick,

        /// <summary>djxl from <c>JXL_TOOLS_DIR</c>; skips when unset and fails when the tool is missing.</summary>
        Djxl,
    }

    /// <summary>Runs one case of <see cref="Cases"/> through one reader.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="pixelType">The pixel type name.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>A task.</returns>
    public static Task RunAsync(Reader reader, string pixelType, string pattern, int width, int height)
        => JxlPixelTypes.Call(pixelType, new PatternCase(reader, pattern, width, height));

    /// <summary>Runs one case of <see cref="CorpusCases"/> through one reader.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="file">The test image, relative to the input images folder.</param>
    /// <param name="pixelType">The pixel type name.</param>
    /// <returns>A task.</returns>
    public static Task RunCorpusAsync(Reader reader, string file, string pixelType)
        => JxlPixelTypes.Call(pixelType, new CorpusCase(reader, file));

    // Eight 8-bit colours (times 257) for the icon pattern, none of them in the palette's implicit colour cubes.
    private static readonly Rgba64[] IconColours =
    {
        new Rgba64(51400, 7710, 10280, ushort.MaxValue),
        new Rgba64(7710, 41120, 17990, ushort.MaxValue),
        new Rgba64(5140, 10280, 46260, ushort.MaxValue),
        new Rgba64(59110, 53970, 5140, ushort.MaxValue),
        new Rgba64(30840, 5140, 35980, ushort.MaxValue),
        new Rgba64(5140, 51400, 53970, ushort.MaxValue),
        new Rgba64(61680, 61680, 59110, ushort.MaxValue),
        new Rgba64(2570, 2570, 5140, ushort.MaxValue),
    };

    // The alpha of each icon colour in the translucent-icon pattern, so that it still has eight colours.
    private static readonly ushort[] IconAlphas = { 0, 65535, 257 * 64, 257 * 128, 257 * 32, 257 * 192, 257 * 16, 257 * 240 };

    /// <summary>
    /// Creates a deterministic test image: <c>solid</c> (one colour), <c>gradient</c> (a different ramp in
    /// each channel), <c>noise</c> (fixed-seed random samples), <c>icon</c> (eight colours in 8x8 blocks),
    /// <c>translucent</c> (the gradient, with random alpha that is 0 or 65535 in places) or
    /// <c>translucent-icon</c> (the icon, with a different alpha for each colour, 0 and 65535 included).
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="pattern">The pattern.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The image.</returns>
    public static Image<TPixel> CreateImage<TPixel>(string pattern, int width, int height)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel> image = new(width, height);
        Random random = new((width * 31) + height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Rgba64 source = pattern switch
                {
                    "solid" => new Rgba64(0x5A5A, 0xC3C3, 0x1E1E, ushort.MaxValue),
                    "gradient" => new Rgba64(
                        (ushort)(x * 65535 / Math.Max(1, width - 1)),
                        (ushort)(y * 65535 / Math.Max(1, height - 1)),
                        (ushort)((x + y) * 65535 / Math.Max(1, width + height - 2)),
                        ushort.MaxValue),
                    "noise" => new Rgba64((ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536), ushort.MaxValue),
                    "icon" => IconColours[((x / 8) + (3 * (y / 8))) % IconColours.Length],
                    "translucent-icon" => WithAlpha(IconColours[((x / 8) + (3 * (y / 8))) % IconColours.Length], IconAlphas[((x / 8) + (3 * (y / 8))) % IconAlphas.Length]),
                    "translucent" => new Rgba64(
                        (ushort)(x * 65535 / Math.Max(1, width - 1)),
                        (ushort)(y * 65535 / Math.Max(1, height - 1)),
                        (ushort)((x + y) * 65535 / Math.Max(1, width + height - 2)),
                        (ushort)((x % 7) switch { 0 => 0, 1 => 65535, _ => random.Next(65536) })),
                    _ => throw new ArgumentException($"Unknown pattern {pattern}", nameof(pattern)),
                };

                TPixel pixel = default;
                pixel.FromRgba64(source);
                image[x, y] = pixel;
            }
        }

        return image;
    }

    /// <summary>Encodes <paramref name="image"/> with <paramref name="encoder"/>, or lossless at Fastest.</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="image">The image.</param>
    /// <param name="encoder">The encoder.</param>
    /// <returns>The codestream.</returns>
    public static byte[] Encode<TPixel>(Image<TPixel> image, JxlEncoder encoder = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using MemoryStream stream = new();
        image.Save(stream, encoder ?? LosslessFastest);
        return stream.ToArray();
    }

    /// <summary>Gets a configuration with the JPEG XL decoder and detector registered.</summary>
    /// <returns>The configuration.</returns>
    public static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.SetEncoder(JxlFormat.Instance, LosslessFastest);
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }

    // Each slice adds its cases with one call here and its own Add method below.
    private static TheoryData<string, string, int, int> CreateCases()
    {
        TheoryData<string, string, int, int> cases = new();
        AddSingleGroup8BitCases(cases);
        AddMultiGroup8BitCases(cases);
        AddPrefixCodeCases(cases);
        AddSampleFormatCases(cases);
        AddTransformCases(cases);
        return cases;
    }

    // E1g: small images whose streams use prefix codes (fewer than 100 tokens), and the first sizes
    // past that, which use ANS.
    private static void AddPrefixCodeCases(TheoryData<string, string, int, int> cases)
    {
        foreach (string pattern in new[] { "gradient", "noise" })
        {
            foreach ((int width, int height) in new[] { (2, 3), (4, 4), (10, 9), (10, 10) })
            {
                cases.Add(nameof(L8), pattern, width, height);
            }

            foreach ((int width, int height) in new[] { (4, 4), (5, 6), (6, 6) })
            {
                cases.Add(nameof(Rgb24), pattern, width, height);
            }
        }
    }

    // E1b: 8-bit grey and RGB in one group, each pattern at 1x1, an odd size and the largest default group.
    private static void AddSingleGroup8BitCases(TheoryData<string, string, int, int> cases)
    {
        foreach (string pixelType in new[] { nameof(L8), nameof(Rgb24) })
        {
            foreach (string pattern in new[] { "solid", "gradient", "noise" })
            {
                foreach ((int width, int height) in new[] { (1, 1), (17, 23), (256, 256) })
                {
                    cases.Add(pixelType, pattern, width, height);
                }
            }
        }
    }

    // E1e: the icon pattern takes the global palette in RGB and a channel palette in grey; the other
    // patterns already cover YCoCg and channel palettes.
    private static void AddTransformCases(TheoryData<string, string, int, int> cases)
    {
        foreach (string pixelType in new[] { nameof(L8), nameof(Rgb24) })
        {
            foreach ((int width, int height) in new[] { (17, 23), (64, 64), (256, 256) })
            {
                cases.Add(pixelType, "icon", width, height);
            }
        }
    }

    // E1c: 8-bit grey and RGB larger than one group. libjxl's group size grows to 512 for images up to
    // 400 pixels on each side, so 257x1 and 1x257 are still one group; the others cross the 256-pixel
    // group and the 2048-pixel LF group boundaries, or are exact multiples of them.
    private static void AddMultiGroup8BitCases(TheoryData<string, string, int, int> cases)
    {
        foreach (string pixelType in new[] { nameof(L8), nameof(Rgb24) })
        {
            foreach ((int width, int height) in new[] { (257, 1), (1, 257), (300, 513), (2049, 3), (256, 512) })
            {
                cases.Add(pixelType, "noise", width, height);
                cases.Add(pixelType, "gradient", width, height);
            }
        }

        // The large cases are slow to encode, so each runs once: an exact multiple of the LF group,
        // and four LF groups with partial groups at both edges.
        cases.Add(nameof(L8), "gradient", 2048, 2048);
        cases.Add(nameof(Rgb24), "noise", 2051, 2309);
    }

    // Each slice adds its corpus files with one call here and its own Add method below.
    private static TheoryData<string, string> CreateCorpusCases()
    {
        TheoryData<string, string> cases = new();
        AddCorpus8BitCases(cases);
        AddCorpus16BitCases(cases);
        return cases;
    }

    // E1c: 8-bit grey and RGB repo PNGs and TIFFs, in one group and in several (Bike has 6 groups and
    // ColorsSaturationLightness 9, more than the 8 above which libjxl would stream).
    private static void AddCorpus8BitCases(TheoryData<string, string> cases)
    {
        cases.Add(TestImages.Png.Bike, nameof(Rgb24));
        cases.Add(TestImages.Png.ColorsSaturationLightness, nameof(Rgb24));
        cases.Add(TestImages.Png.Pd, nameof(Rgb24));
        cases.Add(TestImages.Png.Bradley02, nameof(L8));
        cases.Add(TestImages.Tiff.Calliphora_RgbLzwPredictor, nameof(Rgb24));
        cases.Add(TestImages.Tiff.Calliphora_GrayscaleUncompressed, nameof(L8));
        cases.Add(TestImages.Tiff.FlowerRgb888Contiguous, nameof(Rgb24));
        cases.Add(TestImages.Tiff.Flower8BitGray, nameof(L8));
    }

    // E1d: 16-bit grey, grey with alpha, RGB and RGBA repo PNGs and TIFFs. rgb-48bpp.png is 896x896, so
    // it also crosses group boundaries; rgb-16-tRNS.png has its alpha from a tRNS colour.
    private static void AddCorpus16BitCases(TheoryData<string, string> cases)
    {
        cases.Add(TestImages.Png.L16Bit, nameof(L16));
        cases.Add(TestImages.Png.GrayAlpha16Bit, nameof(La32));
        cases.Add(TestImages.Png.Rgb48Bpp, nameof(Rgb48));
        cases.Add(TestImages.Png.Rgb48BppTrans, nameof(Rgba64));
        cases.Add(TestImages.Png.Rgba64Bpp, nameof(Rgba64));
        cases.Add(TestImages.Tiff.Flower16BitGray, nameof(L16));
        cases.Add(TestImages.Tiff.FlowerRgb161616Contiguous, nameof(Rgb48));
        cases.Add(TestImages.Tiff.Issues1716Rgb161616BitLittleEndian, nameof(Rgb48));
        cases.Add(TestImages.Tiff.Rgba16BitUnassociatedAlphaLittleEndian, nameof(Rgba64));
    }

    // E1d: 16-bit, float32 and alpha, grey and colour, each pattern at 1x1 and an odd size, and the
    // translucent pattern filling the largest default group.
    private static void AddSampleFormatCases(TheoryData<string, string, int, int> cases)
    {
        foreach (string pixelType in new[] { nameof(L16), nameof(La16), nameof(La32), nameof(Rgb48), nameof(Rgba32), nameof(Rgba64), nameof(RgbaVector) })
        {
            foreach (string pattern in new[] { "solid", "gradient", "noise", "translucent" })
            {
                foreach ((int width, int height) in new[] { (1, 1), (17, 23) })
                {
                    cases.Add(pixelType, pattern, width, height);
                }
            }

            cases.Add(pixelType, "translucent", 256, 256);
        }

        // E1e's palette with alpha: the palette covers the alpha channel too.
        foreach (string pixelType in new[] { nameof(La16), nameof(Rgba32), nameof(Rgba64) })
        {
            cases.Add(pixelType, "translucent-icon", 64, 64);
        }
    }

    private static Rgba64 WithAlpha(Rgba64 colour, ushort alpha)
    {
        colour.A = alpha;
        return colour;
    }

    private static async Task RunCorpusAsync<TPixel>(Reader reader, string file)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = Image.Load<TPixel>(TestFile.GetInputFileFullPath(file));
        await RunAsync(reader, image, Path.GetFileNameWithoutExtension(file) + "_" + typeof(TPixel).Name);
    }

    private static async Task RunAsync<TPixel>(Reader reader, Image<TPixel> image, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] encoded = Encode(image);
        switch (reader)
        {
            case Reader.Ours:
                AssertOurDecoderExact(image, encoded);
                break;
            case Reader.Magick:
                AssertMagickExact(image, encoded);
                break;
            case Reader.Djxl:
                await AssertDjxlExactAsync(image, encoded, name, TestContext.Current.CancellationToken);
                break;
        }
    }

    private static async Task RunAsync<TPixel>(Reader reader, string pattern, int width, int height)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = CreateImage<TPixel>(pattern, width, height);
        byte[] encoded = Encode(image);
        switch (reader)
        {
            case Reader.Ours:
                AssertOurDecoderExact(image, encoded);
                break;
            case Reader.Magick:
                AssertMagickExact(image, encoded);
                break;
            case Reader.Djxl:
                await AssertDjxlExactAsync(image, encoded, $"{typeof(TPixel).Name}_{pattern}_{width}x{height}", TestContext.Current.CancellationToken);
                break;
        }
    }

    private static void AssertOurDecoderExact<TPixel>(Image<TPixel> expected, byte[] encoded)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> actual = Image.Load<TPixel>(CreateConfiguration(), encoded);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
        if (expected is Image<RgbaVector> expectedFloats)
        {
            // ImageComparer compares at 16 bits, so float samples are also compared bit for bit.
            AssertFloatBitsEqual(expectedFloats, encoded);
        }
    }

    /// <summary>
    /// Decodes <paramref name="encoded"/> to unclamped float planes and checks that each sample has the bit
    /// pattern of the matching sample of <paramref name="expected"/>, alpha included.
    /// </summary>
    /// <param name="expected">The image that was encoded.</param>
    /// <param name="encoded">The codestream.</param>
    public static void AssertFloatBitsEqual(Image<RgbaVector> expected, byte[] encoded)
    {
        using MemoryStream stream = new(encoded);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
        IReadOnlyList<SixLabors.ImageSharp.Memory.Buffer2D<float>> planes = decoded.Frames[0].Planes;
        Assert.Equal(4, planes.Count);
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                RgbaVector e = expected[x, y];
                float[] samples = { e.R, e.G, e.B, e.A };
                for (int c = 0; c < 4; c++)
                {
                    int actualBits = BitConverter.SingleToInt32Bits(planes[c].DangerousGetRowSpan(y)[x]);
                    if (BitConverter.SingleToInt32Bits(samples[c]) != actualBits)
                    {
                        Assert.Fail($"Channel {c} at ({x}, {y}): expected {samples[c]:R}, got {BitConverter.Int32BitsToSingle(actualBits):R}.");
                    }
                }
            }
        }
    }

    public static void AssertMagickExact<TPixel>(Image<TPixel> expected, byte[] encoded)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        MagickReferenceDecoder.SkipUnlessJxlIsSupported();

        using MemoryStream stream = new(encoded);
        using Image<TPixel> actual = MagickReferenceDecoder.HighBitDepthInstance.Decode<TPixel>(Configuration.Default, stream, CancellationToken.None);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    public static async Task AssertDjxlExactAsync<TPixel>(Image<TPixel> expected, byte[] encoded, string name, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Resolve the tool first, so the test skips before it writes anything.
        JxlTools.GetToolPath("djxl");

        // Write to fresh files: the output folder may hold a longer file from an earlier run.
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", "Encoder");
        string jxlPath = Path.Combine(directory, name + ".jxl");
        string pngPath = Path.Combine(directory, name + ".djxl.png");
        File.Delete(pngPath);
        await File.WriteAllBytesAsync(jxlPath, encoded, cancellationToken);

        await JxlTools.RunAsync("djxl", cancellationToken, jxlPath, pngPath);
        if (typeof(TPixel) == typeof(RgbaVector))
        {
            // djxl writes float samples to a PNG at 16 bits, so only its acceptance of the file is checked.
            return;
        }

        // ImageSharp's own PNG decoder reads djxl's output; djxl writes 8-bit PNGs for 8-bit input and
        // 16-bit PNGs for 9 to 16 bits.
        using Image<TPixel> actual = Image.Load<TPixel>(pngPath);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    private sealed record PatternCase(Reader Reader, string Pattern, int Width, int Height) : IJxlPixelTypeCall<Task>
    {
        public Task Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
            => RunAsync<TPixel>(this.Reader, this.Pattern, this.Width, this.Height);
    }

    private sealed record CorpusCase(Reader Reader, string File) : IJxlPixelTypeCall<Task>
    {
        public Task Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
            => RunCorpusAsync<TPixel>(this.Reader, this.File);
    }
}
