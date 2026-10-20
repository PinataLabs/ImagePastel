// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Frames with splines: libjxl's spline test data against djxl v0.12.0's float planes, hand-built spline
/// dictionaries against djxl, and the bounds of the spline dictionary on hand-built streams.
/// </summary>
/// <remarks>
/// libjxl's "Decoded splines but got none" cannot be reached from a stream: <c>Splines::Decode</c> reads
/// one spline more than the count it is given, so it always has at least one. The check is ported as it is.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlSplinesTests
{
    private const int Width = 16;
    private const int Height = 16;

    // The largest delta of a control point, and the largest position, that the bounds allow: 2^23 - 1.
    private const int MaxDelta = (1 << 23) - 1;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // splines.jxl: one Modular frame, not XYB, 2048 x 2048, with splines. spline_on_first_frame.jxl: a
    // first frame with splines, kept as a reference, then a frame without them that blends over it. The
    // spline drawing is float maths, so the planes are compared at the lossy thresholds.
    // Measured: splines worst channel RMSE 9.0e-8, peak 2.0e-6; spline_on_first_frame RMSE 6.3e-7, peak 3.2e-6.
    [Theory]
    [InlineData("splines")]
    [InlineData("spline_on_first_frame")]
    public void DecodeToPlanes_LibjxlSplineData_MatchesDjxl(string name)
    {
        NpyArray expected = NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy"));

        using FileStream stream = File.OpenRead(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "libjxl", "jxl", name + ".jxl"));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        Assert.Equal(expected.Shape, actual.Shape);
        foreach (JxlConformanceFrameResult result in JxlLossyComparer.AssertWithinThresholds(expected, actual))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Frame {result.Frame}: worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");
        }
    }

    // Measured: largest difference 0.50 of one 8-bit step (both files), which is the rounding.
    [Theory]
    [InlineData("splines")]
    [InlineData("spline_on_first_frame")]
    public void Load_LibjxlSplineData_IsRgb24WithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy"));

        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        using Image image = Image.Load(configuration, Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "libjxl", "jxl", name + ".jxl"));

        Assert.IsType<Image<Rgb24>>(image);
        using Image<Rgb24> pixels = image.CloneAs<Rgb24>();
        JxlConformanceFrameResult[] results = JxlConformanceComparer.Measure(expected, JxlConformanceImages.ToNpy(pixels, expected.Shape[3]), JxlConformanceComparison.ClampToUnitRange);
        Assert.Equal(expected.Shape[0], results.Length);
        foreach (JxlConformanceFrameResult result in results)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Frame {result.Frame}: peak error {result.PeakError * 255:G3} of one 8-bit step");
            Assert.True(result.PeakError <= (1.0 / 255) + 1e-6, $"Frame {result.Frame}: peak error {result.PeakError * 255:G4} steps");
        }
    }

    // Hand-built spline dictionaries on a Modular frame, not XYB, against djxl v0.12.0.
    // Measured: worst channel RMSE 4.6e-8, peak 2.4e-7 ("point" draws nothing: exact).
    [Theory]
    [InlineData("curve")]
    [InlineData("several")]
    [InlineData("quantization")]
    [InlineData("outside")]
    [InlineData("point")]
    [InlineData("noise")]
    public async Task DecodeToPlanes_HandBuiltSplines_MatchDjxl(string name)
    {
        // "noise": the curve on a frame with noise too, which the pipeline adds after the splines.
        byte[] codestream = name == "noise"
            ? new JxlTestLayers(Width, Height).Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Splines = Case("curve"), NoiseLut = new uint[] { 0, 40, 80, 120, 160, 200, 240, 280 } }).ToArray()
            : Layers(Case(name));
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlSplinesTests));
        string input = Path.Combine(directory, name + ".jxl");
        string output = Path.Combine(directory, name + ".npy");
        await File.WriteAllBytesAsync(input, codestream, CancellationToken);

        await JxlTools.RunAsync("djxl", CancellationToken, input, output);

        NpyArray expected = NpyArray.Load(output);
        using JxlDecodedImage decoded = DecodeToPlanes(codestream);
        foreach (JxlConformanceFrameResult result in JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Frame {result.Frame}: worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");
        }
    }

    [Fact]
    public void DecodeToPlanes_Spline_ChangesThePixelsAlongIt()
    {
        using JxlDecodedImage plain = DecodeToPlanes(new JxlTestLayers(Width, Height).Add(JxlTestFrame.Last(JxlTestLayers.ColourA)).ToArray());
        using JxlDecodedImage drawn = DecodeToPlanes(Layers(Case("curve")));

        Assert.NotEqual(plain.Frames[0].Planes[1][8, 8], drawn.Frames[0].Planes[1][8, 8]);
        Assert.Equal(plain.Frames[0].Planes[3][8, 8], drawn.Frames[0].Planes[3][8, 8]); // alpha is not drawn
    }

    // A 16 x 16 frame allows min(2^20, 256 / 2) = 128 splines and control points; the count is coded minus one.
    [Fact]
    public void DecodeToPlanes_SplinesAtTheLimit_Decode()
    {
        JxlTestSpline[] splines = Enumerable.Range(0, 127).Select(_ => new JxlTestSpline(4, 4)).ToArray();

        using JxlDecodedImage decoded = DecodeToPlanes(Layers(new JxlTestSplines(splines)));

        Assert.Single(decoded.Frames);
    }

    [Fact]
    public void DecodeToPlanes_TooManySplines_Throws()
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(4, 4)) { NumSplinesMinusOne = 128 }, "Too many splines");

    [Fact]
    public void DecodeToPlanes_TooManyControlPointsInOneSpline_Throws()
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(4, 4) { NumControlPoints = 129 }), "Too many control points");

    // The total starts at the number of splines, so one spline of 128 control points is one too many.
    [Fact]
    public void DecodeToPlanes_TooManyControlPointsInAll_Throws()
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(4, 4, Enumerable.Repeat((1, 0), 128).ToArray())), "Too many control points");

    [Theory]
    [InlineData(1 << 30, 0)]
    [InlineData(0, -(1 << 30))]
    public void DecodeToPlanes_DeltaDeltaOutOfBounds_Throws(int dx, int dy)
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(4, 4, (dx, dy))), "delta-delta is out of bounds");

    [Theory]
    [InlineData(1 << 23, 0)]
    [InlineData(0, 1 << 23)]
    public void DecodeToPlanes_StartingPointOutOfBounds_Throws(long x, long y)
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(x, y)), "Spline coordinates out of bounds");

    // A second spline's starting point is a delta from the first.
    [Fact]
    public void DecodeToPlanes_LaterStartingPointOutOfBounds_Throws()
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(MaxDelta, 4), new JxlTestSpline(1L << 23, 4)), "Spline coordinates out of bounds");

    [Fact]
    public void DecodeToPlanes_ControlPointDeltaOutOfBounds_Throws()
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(4, 4, (1 << 23, 0))), "Spline coordinates out of bounds");

    [Fact]
    public void DecodeToPlanes_ControlPointOutOfBounds_Throws()
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(MaxDelta, 4, (1, 0))), "Spline coordinates out of bounds");

    // The DCT coefficient int.MinValue, coded as 2^32 - 1.
    [Fact]
    public void DecodeToPlanes_WeirdDctCoefficient_Throws()
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(4, 4) { RawDctValue = uint.MaxValue }), "The weird number in spline DCT");

    // A 32 x 32 frame allows a Manhattan distance of min(1024 * 1024 + 2^32, 2^42). Each control point
    // here moves 2^23 - 1 along both axes and back, so the 257th goes past it. (With fewer, the total
    // estimated area, at least 32 times the distance, is too large instead.)
    [Fact]
    public void DecodeToPlanes_ManhattanDistance_IsBounded()
    {
        (int, int)[] deltaDeltas = new (int, int)[257];
        for (int i = 0; i < deltaDeltas.Length; i++)
        {
            int dd = i == 0 ? MaxDelta : (i % 2 == 1 ? -2 : 2) * MaxDelta;
            deltaDeltas[i] = (dd, dd);
        }

        byte[] codestream = Layers(new JxlTestSplines(new JxlTestSpline(0, 0, deltaDeltas)), 32, 32);
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => DecodeToPlanes(codestream));
        Assert.Contains("Too large manhattan_distance", ex.Message, StringComparison.Ordinal);
    }

    // One spline of Manhattan distance 1 and no colour estimates 32 * sigma^2: a sigma of 65536 in each
    // coefficient is far above the limit of 1024 * 256 + 2^32 for a 16 x 16 frame.
    [Fact]
    public void DecodeToPlanes_TotalEstimatedArea_IsBounded()
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(4, 4, (1, 0)) { Sigma = Enumerable.Repeat(65536, 32).ToArray() }), "Too large total_estimated_area");

    // A deliberate departure from libjxl, which only warns: a total estimated area of 32 * 8192^2 = 2^31 is
    // within libjxl's limit of 1024 * 256 + 2^32, but above the 2^30 that this port allows.
    [Fact]
    public void DecodeToPlanes_TotalSplineArea_IsBounded()
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(4, 4, (1, 0)) { Sigma = Enumerable.Repeat(8192, 32).ToArray() }), "Total spline area is too large");

    // A guard of this port, not of libjxl: the draw cache indexes the segments of each row with ints, so
    // their total must fit an array. Within the 2^30 spline area the total stays far below Array.MaxLength,
    // so the test lowers the limit: the segments of the curve cover 199 rows in all on a 16 x 16 frame.
    [Theory]
    [InlineData(198, true)]
    [InlineData(199, false)]
    public void InitializeDrawCache_SegmentRowsAboveTheLimit_Throw(long maxSegmentRows, bool throws)
    {
        JxlTestBitWriter w = new();
        Case("curve").Write(w);
        JxlSplines splines = new(maxSegmentRows);
        splines.Decode(w.ToReader(), Width * Height, CancellationToken);

        void Initialize() => splines.InitializeDrawCache(Width, Height, 0, 1, CancellationToken);

        if (throws)
        {
            InvalidMemoryOperationException ex = Assert.Throws<InvalidMemoryOperationException>(Initialize);
            Assert.Contains("segment rows", ex.Message, StringComparison.Ordinal);
        }
        else
        {
            Initialize();
        }
    }

    // The control points (4, 4), (4, 4); and (4, 4), (5, 4), (5, 4).
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, -1)]
    public void DecodeToPlanes_IdenticalSuccessiveControlPoints_Throws(int first, int second)
    {
        JxlTestSpline spline = first == 0 ? new JxlTestSpline(4, 4, (0, 0)) : new JxlTestSpline(4, 4, (first, 0), (second, 0));
        AssertInvalid(new JxlTestSplines(spline), "identical successive control points");
    }

    // A dictionary coded with ANS whose final state is not the signature.
    [Fact]
    public void DecodeToPlanes_AnsFinalState_IsChecked()
        => AssertInvalid(new JxlTestSplines(new JxlTestSpline(0, 0)) { AnsState = 0x12345 }, "ANS checksum failure");

    [Fact]
    public void DecodeToPlanes_AnsDictionaryWithTheSignature_Decodes()
        => DecodeToPlanes(Layers(new JxlTestSplines(new JxlTestSpline(0, 0)) { AnsState = 0x130000 })).Dispose();

    private static JxlTestSplines Case(string name) => name switch
    {
        // A curve through four control points, in green with a little red and blue, of width about 1.3.
        "curve" => new JxlTestSplines(new JxlTestSpline(2, 3, (4, 2), (-1, 3), (-2, -6))
        {
            ColorX = new[] { 40, -10, 5 },
            ColorY = new[] { 6, 2, -1 },
            ColorB = new[] { -8, 0, 4 },
            Sigma = new[] { 6, 1 },
        }),

        // Three splines from three starting points, one a single point, so it draws nothing.
        "several" => new JxlTestSplines(
            new JxlTestSpline(1, 1, (12, 3)) { ColorY = new[] { 5 }, Sigma = new[] { 4 } },
            new JxlTestSpline(14, 2, (-3, 6), (-4, 2)) { ColorX = new[] { -30, 0, 0, 12 }, ColorB = new[] { 10 }, Sigma = new[] { 3, 0, -1 } },
            new JxlTestSpline(8, 8)),

        // A negative quantization adjustment, which divides the weights by 1 + 3/8.
        "quantization" => new JxlTestSplines(new JxlTestSpline(3, 12, (8, -6)) { ColorY = new[] { 7, -3 }, Sigma = new[] { 5 } }) { QuantizationAdjustment = -3 },

        // A spline that starts above and left of the frame and leaves it on the right.
        "outside" => new JxlTestSplines(new JxlTestSpline(0, 0, (30, 10)) { ColorY = new[] { 4 }, Sigma = new[] { 8 } }),

        // One control point: the arc length is 0, so the spline has no effect.
        "point" => new JxlTestSplines(new JxlTestSpline(8, 8) { ColorY = new[] { 9 }, Sigma = new[] { 6 } }),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static byte[] Layers(JxlTestSplines splines, int width = Width, int height = Height)
        => new JxlTestLayers(width, height).Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Splines = splines }).ToArray();

    private static void AssertInvalid(JxlTestSplines splines, string message)
    {
        byte[] codestream = Layers(splines);
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => DecodeToPlanes(codestream));
        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    private static JxlDecodedImage DecodeToPlanes(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        return new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
    }
}
