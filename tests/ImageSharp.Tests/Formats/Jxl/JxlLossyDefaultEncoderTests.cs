// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lossy encoding at Default (libjxl effort 5, <c>kHare</c>), slice E4. E4a is the tracer: Gaborish,
/// near-lossless DC, the <c>kACMeta</c> tree for the AC metadata and a learned tree for alpha are libjxl's
/// Hare stages, and the heuristics that later tickets port keep their Fastest form for now (a constant
/// quant field, DCT8 everywhere, the default chroma-from-luma and block context maps).
/// </summary>
/// <remarks>
/// The colour is checked against E3a's interim bound (<see cref="JxlLossyEncoderTests"/>): each channel's
/// RMSE against the source is at most 20 8-bit steps.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlLossyDefaultEncoderTests
{
    public static TheoryData<string, float> PhotoReaderCases { get; } = CreatePhotoReaderCases();

    [Theory]
    [MemberData(nameof(PhotoReaderCases))]
    public async Task Encode_Photo_DecodesThroughEachReaderWithinTheInterimBound(string reader, float distance)
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        using Image<Rgb24> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyDefaultEncoder", $"photo_d{distance}");
        JxlLossyEncoderTests.AssertWithinInterimBound(image, decoded, $"d{distance}, {encoded.Length} bytes");
    }

    internal static JxlEncoder Lossy(float distance) => new() { Distance = distance, Effort = JxlEffort.Default };

    private static TheoryData<string, float> CreatePhotoReaderCases()
    {
        TheoryData<string, float> cases = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach (float distance in new[] { 1.0f, 2.0f, 4.0f })
            {
                cases.Add(reader.ToString(), distance);
            }
        }

        return cases;
    }
}
