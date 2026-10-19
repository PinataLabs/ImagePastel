// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
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
/// back exactly the same pixels. Each E1 slice adds its cases to <see cref="Cases"/> and, for a new
/// pixel type, a branch to <see cref="RunAsync(Reader, string, string, int, int)"/>.
/// </summary>
internal static class JxlEncoderRoundTrip
{
    /// <summary>Gets the supported encoder setting: lossless at Fastest.</summary>
    public static JxlEncoder LosslessFastest => new() { Distance = 0, Effort = JxlEffort.Fastest };

    /// <summary>
    /// Gets the round-trip cases: pixel type, pattern (see <see cref="CreateImage{TPixel}"/>), width and height.
    /// </summary>
    public static TheoryData<string, string, int, int> Cases { get; } = CreateCases();

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
        => pixelType switch
        {
            nameof(L8) => RunAsync<L8>(reader, pattern, width, height),
            nameof(Rgb24) => RunAsync<Rgb24>(reader, pattern, width, height),
            _ => throw new ArgumentException($"No round trip for {pixelType}", nameof(pixelType)),
        };

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

    /// <summary>
    /// Creates a deterministic test image: <c>solid</c> (one colour), <c>gradient</c> (a different ramp in
    /// each channel), <c>noise</c> (fixed-seed random samples) or <c>icon</c> (eight colours in 8x8 blocks).
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
        configuration.ImageFormatsManager.SetEncoder(JxlFormat.Instance, new JxlEncoder { Distance = 0, Effort = JxlEffort.Fastest });
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }

    // Each slice adds its cases with one call here and its own Add method below.
    private static TheoryData<string, string, int, int> CreateCases()
    {
        TheoryData<string, string, int, int> cases = new();
        AddSingleGroup8BitCases(cases);
        AddTransformCases(cases);
        return cases;
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
    }

    private static void AssertMagickExact<TPixel>(Image<TPixel> expected, byte[] encoded)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Assert.SkipUnless(ImageMagick.MagickFormatInfo.Create(ImageMagick.MagickFormat.Jxl)?.SupportsReading == true, "This Magick.NET build has no libjxl.");

        using MemoryStream stream = new(encoded);
        using Image<TPixel> actual = MagickReferenceDecoder.HighBitDepthInstance.Decode<TPixel>(Configuration.Default, stream, CancellationToken.None);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    private static async Task AssertDjxlExactAsync<TPixel>(Image<TPixel> expected, byte[] encoded, string name, CancellationToken cancellationToken)
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

        // ImageSharp's own PNG decoder reads djxl's output; djxl writes 8-bit PNGs for 8-bit input.
        using Image<TPixel> actual = Image.Load<TPixel>(pngPath);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }
}
