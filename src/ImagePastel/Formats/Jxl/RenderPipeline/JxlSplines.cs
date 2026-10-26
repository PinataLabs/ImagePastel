// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// Splines and QuantizedSpline in lib/jxl/splines.h and the decoder parts of lib/jxl/splines.cc
// (ContinuousIDCT, DrawSegment, ComputeSegments, DrawSegments, SegmentsFromPoints,
// ValidateSplinePointPos, InvAdjustedQuant, DecodeAllStartingPoints, DrawCentripetalCatmullRomSpline,
// ForEachEquallySpacedPoint, QuantizedSpline::Dequantize, QuantizedSpline::Decode, Splines::Decode,
// Splines::InitializeDrawCache, Splines::AddToRow), for one lane.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// The splines of a frame: smooth curves, each drawn by normalized Gaussian splatting along a
/// centripetal Catmull-Rom spline through its control points, with a colour and a width that vary along
/// it. They are read from the DC global section and added to the three colour planes after the patches.
/// </summary>
/// <remarks>
/// <para>
/// A deliberate departure from libjxl: libjxl keeps one <c>Splines</c> object across frames and clears it
/// at the start of every frame. Here each frame with the splines flag decodes its own, and a frame
/// without the flag has none, so one frame's splines never reach the next.
/// </para>
/// <para>
/// A deliberate departure from libjxl (ADR 0004): each <c>MulAdd</c> and <c>MulSub</c> of libjxl's
/// Highway code is split into a multiply and an add, and the 32 terms of the continuous IDCT are summed
/// in order, as on Highway's scalar target.
/// </para>
/// </remarks>
internal sealed class JxlSplines
{
    // SplineEntropyContexts in lib/jxl/splines.h.
    private const int QuantizationAdjustmentContext = 0;
    private const int StartingPositionContext = 1;
    private const int NumSplinesContext = 2;
    private const int NumControlPointsContext = 3;
    private const int ControlPointsContext = 4;
    private const int DctContext = 5;
    private const int NumSplineContexts = 6;

    private const float DesiredRenderingDistance = 1f;

    // The number of DCT coefficients of each colour and of sigma: libjxl's Dct32.
    private const int DctSize = 32;

    // Maximum number of spline control points per frame is
    //   std::min(kMaxNumControlPoints, xsize * ysize / 2)
    private const long MaxNumControlPoints = 1L << 20;
    private const long MaxNumControlPointsPerPixelRatio = 2;

    // It is not in spec, but reasonable limit to avoid overflows.
    private const long SplinePosLimit = 1L << 23;

    // Maximal image dimension.
    private const long DeltaLimit = 1L << 30;

    // kSqrt2 and kSqrt0_5 of lib/jxl/dct_scales.h.
    private const float Sqrt2 = 1.41421356237f;
    private const float SqrtHalf = 0.70710678118f;

    // kPi of lib/jxl/base/common.h, a double.
    private const double Pi = 3.14159265358979323846264338327950288;

    // Decode and InitializeDrawCache check the cancellation token once every 4096 splines.
    private const int CancellationCheckMask = 4095;

    // X, Y, B, sigma.
    private static readonly float[] ChannelWeight = { 0.0042f, 0.075f, 0.07f, .3333f };

    // kMultipliers of ContinuousIDCT: kPi / 32 * i in double, stored as float.
    private static readonly float[] IdctMultipliers = CreateIdctMultipliers();

    private readonly List<QuantizedSpline> splines = new();
    private readonly List<SplinePoint> startingPoints = new();
    private readonly List<JxlSplineSegment> segments = new();
    private int[] segmentIndices = Array.Empty<int>();
    private int[] segmentYStart = Array.Empty<int>();
    private readonly long maxSegmentRows;
    private int quantizationAdjustment;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlSplines"/> class.
    /// </summary>
    public JxlSplines()
        : this(MaxArrayLength)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlSplines"/> class with a lower limit on the
    /// segment rows of the draw cache, for tests.
    /// </summary>
    /// <param name="maxSegmentRows">The most segment rows <see cref="InitializeDrawCache"/> allows.</param>
    internal JxlSplines(long maxSegmentRows) => this.maxSegmentRows = maxSegmentRows;

    private static int MaxArrayLength =>
#if NET6_0_OR_GREATER
        Array.MaxLength;
#else
        0x7FFFFFC7;
#endif

    /// <summary>
    /// Given a set of DCT coefficients, this returns the result of performing cosine interpolation on the
    /// original samples.
    /// </summary>
    /// <param name="dct">The 32 coefficients.</param>
    /// <param name="t">The position along the samples, 0 to 31.</param>
    /// <returns>The interpolated value.</returns>
    public static float ContinuousIdct(ReadOnlySpan<float> dct, float t)
    {
        // We compute here the DCT-3 of the `dct` vector, rescaled by a factor of
        // sqrt(32). This is such that an input vector vector {x, 0, ..., 0} produces
        // a constant result of x. dct[0] was scaled in Dequantize() to allow uniform
        // treatment of all the coefficients.
        float result = 0;
        float tandhalf = t + 0.5f;
        for (int i = 0; i < DctSize; i++)
        {
            float cosArg = IdctMultipliers[i] * tandhalf;
            float cos = JxlFastMath.FastCosf(cosArg);
            float localRes = dct[i] * cos;

            // libjxl: result = MulAdd(Set(df, kSqrt2), local_res, result).
            result = (Sqrt2 * localRes) + result;
        }

        return result;
    }

    /// <summary>
    /// Adds the segment at row <paramref name="y"/> to the pixels <paramref name="x0"/> to
    /// <paramref name="x1"/> (exclusive) of the three rows, whose first sample is pixel <paramref name="x0"/>.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <param name="y">The row.</param>
    /// <param name="x0">The first pixel of the rows.</param>
    /// <param name="x1">The pixel after the last.</param>
    /// <param name="rowX">The X row.</param>
    /// <param name="rowY">The Y row.</param>
    /// <param name="rowB">The B row.</param>
    public static void DrawSegment(in JxlSplineSegment segment, long y, long x0, long x1, Span<float> rowX, Span<float> rowY, Span<float> rowB)
    {
        long start = LlRound(segment.CenterX - segment.MaximumDistance);
        long end = LlRound(segment.CenterX + segment.MaximumDistance);
        if (end < x0 || start >= x1)
        {
            return; // span does not intersect scan
        }

        int spanX0 = (int)(Math.Max(x0, start) - x0);
        int spanX1 = (int)(Math.Min(x1, end + 1) - x0); // exclusive
        float invSigma = segment.InvSigma;
        const float half = 0.5f;
        const float oneOver2s2 = 0.353553391f;
        float sigmaOver4TimesIntensity = segment.SigmaOver4TimesIntensity;

        // libjxl: Set(df, y - segment.center_y), with y converted to float.
        float dy = (float)y - segment.CenterY;
        float dy2 = dy * dy;
        for (int x = spanX0; x < spanX1; x++)
        {
            // libjxl: ConvertTo(df, Iota(di, x + x0)), an int32 lane.
            float dx = (float)unchecked((int)(x + x0)) - segment.CenterX;

            // libjxl: MulAdd(dx, dx, Mul(dy, dy)).
            float sqd = (dx * dx) + dy2;
            float distance = MathF.Sqrt(sqd);

            // libjxl: MulAdd(distance, half, one_over_2s2) and MulSub(distance, half, one_over_2s2).
            float oneDimensionalFactor =
                JxlFastMath.FastErff(((distance * half) + oneOver2s2) * invSigma)
                - JxlFastMath.FastErff(((distance * half) - oneOver2s2) * invSigma);
            float localIntensity = sigmaOver4TimesIntensity * (oneDimensionalFactor * oneDimensionalFactor);

            // libjxl: StoreU(MulAdd(cm, local_intensity, in), ...).
            rowX[x] = (segment.ColorX * localIntensity) + rowX[x];
            rowY[x] = (segment.ColorY * localIntensity) + rowY[x];
            rowB[x] = (segment.ColorB * localIntensity) + rowB[x];
        }
    }

    /// <summary>
    /// Adds the segment of one point of a spline, and its rows, unless its width or intensity is not
    /// finite or it lies outside the image's rows.
    /// </summary>
    /// <param name="imageYSize">The height of the image.</param>
    /// <param name="centerX">The x of the point.</param>
    /// <param name="centerY">The y of the point.</param>
    /// <param name="intensity">The intensity: the distance to the previous point.</param>
    /// <param name="color">The X, Y and B colour at the point.</param>
    /// <param name="sigma">The width at the point.</param>
    /// <param name="segments">The segments to add to.</param>
    /// <param name="segmentSpans">The rows of each segment, the end exclusive, to add to.</param>
    public static void ComputeSegments(long imageYSize, float centerX, float centerY, float intensity, ReadOnlySpan<float> color, float sigma, List<JxlSplineSegment> segments, List<(long Start, long End)> segmentSpans)
    {
        // Sanity check sigma, inverse sigma and intensity
        if (!(float.IsFinite(sigma) && sigma != 0.0f && float.IsFinite(1.0f / sigma) && float.IsFinite(intensity)))
        {
            return;
        }

        // JXL_HIGH_PRECISION is 1 in lib/jxl/common.h.
        const float distanceExp = 5;

        // We cap from below colors to at least 0.01.
        float maxColor = 0.01f;
        for (int c = 0; c < 3; c++)
        {
            maxColor = Max(maxColor, MathF.Abs(color[c] * intensity));
        }

        // Distance beyond which max_color*intensity*exp(-d^2 / (2 * sigma^2)) drops
        // below 10^-kDistanceExp.
        float maximumDistance = MathF.Sqrt(-2.0f * sigma * sigma * ((MathF.Log(0.1f) * distanceExp) - MathF.Log(maxColor)));

        long y0 = LlRound(centerY - maximumDistance);
        y0 = Math.Max(y0, 0);
        long y1 = LlRound(centerY + maximumDistance) + 1;
        y1 = Math.Min(y1, imageYSize);
        if (y1 <= y0)
        {
            return;
        }

        segments.Add(new JxlSplineSegment(centerX, centerY, maximumDistance, 1.0f / sigma, .25f * sigma * intensity, color[0], color[1], color[2]));
        segmentSpans.Add((y0, y1));
    }

    /// <summary>
    /// Reads the splines from the frame's DC global section. Throws
    /// <see cref="InvalidImageContentException"/> for invalid data.
    /// </summary>
    /// <param name="br">The reader.</param>
    /// <param name="numPixels">The number of pixels of the frame, at its coded size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public void Decode(JxlBitReader br, long numPixels, CancellationToken cancellationToken)
    {
        JxlAnsCode code = new();
        byte[] contextMap = JxlEntropyDecoder.DecodeHistograms(br, NumSplineContexts, code);
        JxlAnsSymbolReader decoder = new(code, br);
        long numSplines = decoder.ReadHybridUint(NumSplinesContext, br, contextMap);
        long maxControlPoints = Math.Min(MaxNumControlPoints, numPixels / MaxNumControlPointsPerPixelRatio);
        if (numSplines > maxControlPoints || numSplines + 1 > maxControlPoints)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Too many splines: {numSplines}");
        }

        numSplines++;
        this.DecodeAllStartingPoints(br, decoder, contextMap, (int)numSplines);

        this.quantizationAdjustment = JxlBits.UnpackSigned(decoder.ReadHybridUint(QuantizationAdjustmentContext, br, contextMap));

        this.splines.Clear();
        long numControlPoints = numSplines;
        for (int i = 0; i < numSplines; i++)
        {
            if ((i & CancellationCheckMask) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            QuantizedSpline spline = new();
            spline.Decode(contextMap, decoder, br, maxControlPoints, ref numControlPoints);
            this.splines.Add(spline);
        }

        if (!decoder.CheckAnsFinalState)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("ANS checksum failure.");
        }

        // Splines::Decode reads one spline more than the count it is given, so this never fails; it is
        // kept as libjxl has it.
        if (this.splines.Count == 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Decoded splines but got none");
        }
    }

    /// <summary>
    /// Dequantizes the splines with the base ratios of the colour-correlation map and computes the
    /// segments to draw and the segments of each row. Throws <see cref="InvalidImageContentException"/>
    /// for splines too large to draw.
    /// </summary>
    /// <param name="imageXSize">The width of the frame, upsampled.</param>
    /// <param name="imageYSize">The height of the frame, upsampled.</param>
    /// <param name="yToX">The base ratio of X to Y (<c>YtoXRatio(0)</c>).</param>
    /// <param name="yToB">The base ratio of B to Y (<c>YtoBRatio(0)</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public void InitializeDrawCache(long imageXSize, long imageYSize, float yToX, float yToB, CancellationToken cancellationToken)
    {
        // TODO(veluca): avoid storing segments that are entirely outside image
        // boundaries.
        this.segments.Clear();
        this.segmentIndices = Array.Empty<int>();
        this.segmentYStart = Array.Empty<int>();
        List<(long Start, long End)> segmentsSpans = new();
        List<SplinePoint> intermediatePoints = new();
        List<(SplinePoint Point, float Multiplier)> pointsToDraw = new();
        ulong totalEstimatedAreaReached = 0;
        Spline[] splines = new Spline[this.splines.Count];
        for (int i = 0; i < splines.Length; i++)
        {
            if ((i & CancellationCheckMask) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Spline spline = this.splines[i].Dequantize(this.startingPoints[i], this.quantizationAdjustment, yToX, yToB, (ulong)(imageXSize * imageYSize), ref totalEstimatedAreaReached);
            List<SplinePoint> points = spline.ControlPoints;
            for (int j = 1; j < points.Count; j++)
            {
                if (points[j - 1].IsCloseTo(points[j]))
                {
                    // Otherwise division by zero might occur. Once control points coincide,
                    // the direction of curve is undefined...
                    JxlThrowHelper.ThrowInvalidImageContentException($"identical successive control points in spline {i}");
                }
            }

            splines[i] = spline;
        }

        // TODO(firsching) Change this into a JXL_FAILURE for level 5 codestreams.
        // A deliberate departure from libjxl: above min(8 * xsize * ysize + 2^25, 2^30) libjxl only warns
        // ("Large total_estimated_area_reached, expect slower decoding"), and fails only in fuzzing builds
        // (FUZZING_BUILD_MODE_UNSAFE_FOR_PRODUCTION). This port fails above the 2^30 cap of that limit, so
        // that a small, hostile stream cannot make the decoder draw splines for a long time. It does not
        // fail at the per-pixel term, because libjxl's own splines.jxl (2048 x 2048) is above it
        // (244,616,490 against 67,108,864) and djxl decodes it.
        const ulong totalAreaLimit = 1UL << 30;
        if (totalEstimatedAreaReached > totalAreaLimit)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Total spline area is too large: {totalEstimatedAreaReached}");
        }

        for (int i = 0; i < splines.Length; i++)
        {
            if ((i & CancellationCheckMask) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Spline spline = splines[i];
            pointsToDraw.Clear();
            intermediatePoints.Clear();
            DrawCentripetalCatmullRomSpline(spline.ControlPoints, intermediatePoints);
            ForEachEquallySpacedPoint(intermediatePoints, pointsToDraw);
            float arcLength = ((pointsToDraw.Count - 2) * DesiredRenderingDistance) + pointsToDraw[^1].Multiplier;
            if (arcLength <= 0.0f)
            {
                // This spline wouldn't have any effect.
                continue;
            }

            SegmentsFromPoints(imageYSize, spline, pointsToDraw, arcLength, this.segments, segmentsSpans);
        }

        // A departure from libjxl, as a guard: libjxl counts the segments of each row in size_t and
        // allocates their indices with no limit. Here they are int indices into an array, so their total
        // must fit an array, or the casts below would wrap. Within the 2^30 area limit above the total
        // stays far below Array.MaxLength (the rows of each segment grow with its sigma, and the number of
        // segments with the arc length, whose product the area estimate bounds), so this is not expected
        // to throw; it keeps a stream that gets past the estimate from corrupting the indices.
        int[] segmentYStart = new int[imageYSize + 2];
        const int population = 1; // population[y] is segmentYStart[population + y]
        foreach ((long start, long end) in segmentsSpans)
        {
            segmentYStart[population + start]++;
            segmentYStart[population + end]--;
        }

        // Turn to cumulative.
        long total = 0;
        long coverage = 0;
        for (long y = 0; y < imageYSize; y++)
        {
            coverage += segmentYStart[population + y];
            segmentYStart[population + y] = (int)total;
            total += coverage;
            if (total > this.maxSegmentRows)
            {
                throw new InvalidMemoryOperationException($"The JPEG XL splines need {total} segment rows, more than the limit of {this.maxSegmentRows}.");
            }
        }

        // population[0] == 0 (that is segment_y_start[1])
        // We are going to place segments using population[y] as index for elements
        // of y-th line (and increment them); that way final value of population[y]
        // is the staring index for (y+1)-th line. Thus segment_y_start will contain
        // indices without an offset.
        int[] segmentIndices = new int[total];
        for (int i = 0; i < this.segments.Count; i++)
        {
            (long start, long end) = segmentsSpans[i];
            for (long y = start; y < end; y++)
            {
                segmentIndices[segmentYStart[population + y]++] = i;
            }
        }

        this.segmentIndices = segmentIndices;
        this.segmentYStart = segmentYStart;
    }

    /// <summary>
    /// Adds the splines to the pixels <paramref name="x0"/> to <paramref name="x1"/> (exclusive) of row
    /// <paramref name="y"/>, whose first sample is pixel <paramref name="x0"/>.
    /// </summary>
    /// <param name="rowX">The X row.</param>
    /// <param name="rowY">The Y row.</param>
    /// <param name="rowB">The B row.</param>
    /// <param name="y">The row.</param>
    /// <param name="x0">The first pixel.</param>
    /// <param name="x1">The pixel after the last.</param>
    public void AddToRow(Span<float> rowX, Span<float> rowY, Span<float> rowB, long y, long x0, long x1)
    {
        if (this.segments.Count == 0)
        {
            return;
        }

        // DrawSegments.
        for (int i = this.segmentYStart[y]; i < this.segmentYStart[y + 1]; i++)
        {
            JxlSplineSegment segment = this.segments[this.segmentIndices[i]];
            DrawSegment(in segment, y, x0, x1, rowX, rowY, rowB);
        }
    }

    private static float[] CreateIdctMultipliers()
    {
        float[] multipliers = new float[DctSize];
        for (int i = 0; i < DctSize; i++)
        {
            multipliers[i] = (float)(Pi / DctSize * i);
        }

        return multipliers;
    }

    // std::llround: rounds half away from zero.
    // C++ leaves llround of a NaN, an infinity or a value out of range unspecified. On x86-64 (glibc, and
    // the Windows runtimes djxl is built with) it gives LONG_MIN, the "integer indefinite" of cvtss2si.
    // A .NET cast is not the same on every runtime: from .NET 9 on it gives 0 for a NaN and saturates an
    // infinity, and before it gives long.MinValue. So such a value maps to long.MinValue here, on every
    // runtime: a segment with a NaN or infinite maximum distance then has an empty span, as in libjxl.
    // The spline bounds keep such values out of a stream (JxlSplineMathsTests).
    private static long LlRound(float v)
    {
        float rounded = MathF.Round(v, MidpointRounding.AwayFromZero);

        // 2^63 is exact as a float; every float below it fits a long.
        const float twoTo63 = 9223372036854775808F;
        return rounded >= -twoTo63 && rounded < twoTo63 ? (long)rounded : long.MinValue;
    }

    // std::max of two floats: (a < b) ? b : a.
    private static float Max(float a, float b) => a < b ? b : a;

    // std::min of two floats: (b < a) ? b : a.
    private static float Min(float a, float b) => b < a ? b : a;

    // hypotf. A departure from libjxl: .NET has no single-precision hypot, so it is computed in double and
    // rounded once to float, as glibc's and the Microsoft C runtime's hypotf do.
    private static float Hypot(float x, float y) => (float)Math.Sqrt((x * (double)x) + (y * (double)y));

    private static float InvAdjustedQuant(int adjustment)
        => adjustment >= 0 ? 1.0f / (1.0f + (.125f * adjustment)) : 1.0f - (.125f * adjustment);

    private static void ValidateSplinePointPos(long x, long y)
    {
        if (x >= SplinePosLimit || x <= -SplinePosLimit || y >= SplinePosLimit || y <= -SplinePosLimit)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Spline coordinates out of bounds");
        }
    }

    private static void ValidateSplinePointPos(float x, float y)
    {
        if (x >= SplinePosLimit || x <= -SplinePosLimit || y >= SplinePosLimit || y <= -SplinePosLimit)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Spline coordinates out of bounds");
        }
    }

    // TODO(eustas): avoid making a copy of "points".
    private static void DrawCentripetalCatmullRomSpline(List<SplinePoint> controlPoints, List<SplinePoint> result)
    {
        if (controlPoints.Count == 0)
        {
            return;
        }

        if (controlPoints.Count == 1)
        {
            result.Add(controlPoints[0]);
            return;
        }

        // Number of points to compute between each control point.
        const int numPoints = 16;
        List<SplinePoint> points = new(controlPoints.Count + 2);
        points.Add(controlPoints[0] + (controlPoints[0] - controlPoints[1]));
        points.AddRange(controlPoints);
        points.Add(points[^1] + (points[^1] - points[^2]));

        // points has at least 4 elements at this point.
        Span<float> d = stackalloc float[3];
        Span<float> t = stackalloc float[4];
        Span<SplinePoint> a = stackalloc SplinePoint[3];
        Span<SplinePoint> b = stackalloc SplinePoint[2];
        Span<SplinePoint> p = stackalloc SplinePoint[4];
        for (int start = 0; start < points.Count - 3; start++)
        {
            // 4 of them are used, and we draw from p[1] to p[2].
            for (int k = 0; k < 4; k++)
            {
                p[k] = points[start + k];
            }

            result.Add(p[1]);
            t[0] = 0;
            for (int k = 0; k < 3; k++)
            {
                // TODO(eustas): for each segment delta is calculated 3 times...
                // TODO(eustas): restrict d[k] with reasonable limit and spec it.
                d[k] = MathF.Sqrt(Hypot(p[k + 1].X - p[k].X, p[k + 1].Y - p[k].Y));
                t[k + 1] = t[k] + d[k];
            }

            for (int i = 1; i < numPoints; i++)
            {
                float tt = d[0] + (((float)i / numPoints) * d[1]);
                for (int k = 0; k < 3; k++)
                {
                    // TODO(eustas): reciprocal multiplication would be faster.
                    a[k] = p[k] + (((tt - t[k]) / d[k]) * (p[k + 1] - p[k]));
                }

                for (int k = 0; k < 2; k++)
                {
                    b[k] = a[k] + (((tt - t[k]) / (d[k] + d[k + 1])) * (a[k + 1] - a[k]));
                }

                result.Add(b[0] + (((tt - t[1]) / d[1]) * (b[1] - b[0])));
            }
        }

        result.Add(points[^2]);
    }

    // Move along the line segments defined by `points`, `kDesiredRenderingDistance`
    // pixels at a time, and call `functor` with each point and the actual distance
    // to the previous point (which will always be kDesiredRenderingDistance except
    // possibly for the very last point).
    // TODO(eustas): this method always adds the last point, but never the first
    //               (unless those are one); I believe both ends matter.
    private static void ForEachEquallySpacedPoint(List<SplinePoint> points, List<(SplinePoint Point, float Multiplier)> pointsToDraw)
    {
        DebugGuard.IsTrue(points.Count > 0, "A spline has at least one control point");
        SplinePoint current = points[0];
        pointsToDraw.Add((current, DesiredRenderingDistance));
        int next = 0;
        while (next != points.Count)
        {
            SplinePoint previous = current;
            float arclengthFromPrevious = 0.0f;
            while (true)
            {
                if (next == points.Count)
                {
                    pointsToDraw.Add((previous, arclengthFromPrevious));
                    return;
                }

                float arclengthToNext = MathF.Sqrt((points[next] - previous).SquaredNorm());
                if (arclengthFromPrevious + arclengthToNext >= DesiredRenderingDistance)
                {
                    current = previous + (((DesiredRenderingDistance - arclengthFromPrevious) / arclengthToNext) * (points[next] - previous));
                    pointsToDraw.Add((current, DesiredRenderingDistance));
                    break;
                }

                arclengthFromPrevious += arclengthToNext;
                previous = points[next];
                next++;
            }
        }
    }

    private static void SegmentsFromPoints(long imageYSize, Spline spline, List<(SplinePoint Point, float Multiplier)> pointsToDraw, float arcLength, List<JxlSplineSegment> segments, List<(long Start, long End)> segmentsSpans)
    {
        float invArcLength = 1.0f / arcLength;
        int k = 0;
        Span<float> color = stackalloc float[3];
        foreach ((SplinePoint point, float multiplier) in pointsToDraw)
        {
            float progressAlongArc = Min(1.0f, (k * DesiredRenderingDistance) * invArcLength);
            k++;
            for (int c = 0; c < 3; c++)
            {
                color[c] = ContinuousIdct(spline.ColorDct[c], (DctSize - 1) * progressAlongArc);
            }

            float sigma = ContinuousIdct(spline.SigmaDct, (DctSize - 1) * progressAlongArc);
            ComputeSegments(imageYSize, point.X, point.Y, multiplier, color, sigma, segments, segmentsSpans);
        }
    }

    private void DecodeAllStartingPoints(JxlBitReader br, JxlAnsSymbolReader reader, byte[] contextMap, int numSplines)
    {
        this.startingPoints.Clear();
        long lastX = 0;
        long lastY = 0;
        for (int i = 0; i < numSplines; i++)
        {
            uint dx = reader.ReadHybridUint(StartingPositionContext, br, contextMap);
            uint dy = reader.ReadHybridUint(StartingPositionContext, br, contextMap);
            long x;
            long y;
            if (i != 0)
            {
                x = JxlBits.UnpackSigned(dx) + lastX;
                y = JxlBits.UnpackSigned(dy) + lastY;
            }
            else
            {
                x = dx;
                y = dy;
            }

            ValidateSplinePointPos(x, y);
            this.startingPoints.Add(new SplinePoint(x, y));
            lastX = x;
            lastY = y;
        }
    }

    // Spline::Point.
    private readonly struct SplinePoint
    {
        public SplinePoint(float x, float y)
        {
            this.X = x;
            this.Y = y;
        }

        public float X { get; }

        public float Y { get; }

        public static SplinePoint operator +(SplinePoint p, SplinePoint vec) => new(p.X + vec.X, p.Y + vec.Y);

        public static SplinePoint operator -(SplinePoint a, SplinePoint b) => new(a.X - b.X, a.Y - b.Y);

        public static SplinePoint operator *(float k, SplinePoint vec) => new(k * vec.X, k * vec.Y);

        public float SquaredNorm() => (this.X * this.X) + (this.Y * this.Y);

        // Spline::Point::operator==.
        public bool IsCloseTo(SplinePoint other)
            => MathF.Abs(this.X - other.X) < 1e-3f && MathF.Abs(this.Y - other.Y) < 1e-3f;
    }

    // Spline: the control points and the dequantized DCTs.
    private sealed class Spline
    {
        public Spline(int numControlPoints)
        {
            this.ControlPoints = new List<SplinePoint>(numControlPoints);
            this.ColorDct = new[] { new float[DctSize], new float[DctSize], new float[DctSize] };
            this.SigmaDct = new float[DctSize];
        }

        public List<SplinePoint> ControlPoints { get; }

        // X, Y, B.
        public float[][] ColorDct { get; }

        // Splines are draws by normalized Gaussian splatting. This controls the
        // Gaussian's parameter along the spline.
        public float[] SigmaDct { get; }
    }

    // QuantizedSpline.
    private sealed class QuantizedSpline
    {
        private readonly int[][] colorDct = { new int[DctSize], new int[DctSize], new int[DctSize] };
        private readonly int[] sigmaDct = new int[DctSize];

        // Double delta-encoded.
        private (long X, long Y)[] controlPoints = Array.Empty<(long, long)>();

        public void Decode(byte[] contextMap, JxlAnsSymbolReader decoder, JxlBitReader br, long maxControlPoints, ref long totalNumControlPoints)
        {
            long numControlPoints = decoder.ReadHybridUint(NumControlPointsContext, br, contextMap);
            if (numControlPoints > maxControlPoints)
            {
                JxlThrowHelper.ThrowInvalidImageContentException($"Too many control points: {numControlPoints}");
            }

            totalNumControlPoints += numControlPoints;
            if (totalNumControlPoints > maxControlPoints)
            {
                JxlThrowHelper.ThrowInvalidImageContentException($"Too many control points: {totalNumControlPoints}");
            }

            this.controlPoints = new (long, long)[numControlPoints];
            for (int i = 0; i < this.controlPoints.Length; i++)
            {
                long first = JxlBits.UnpackSigned(decoder.ReadHybridUint(ControlPointsContext, br, contextMap));
                long second = JxlBits.UnpackSigned(decoder.ReadHybridUint(ControlPointsContext, br, contextMap));

                // Check delta-deltas are not outrageous; it is not in spec, but there is
                // no reason to allow larger values.
                if (first >= DeltaLimit || first <= -DeltaLimit || second >= DeltaLimit || second <= -DeltaLimit)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Spline delta-delta is out of bounds");
                }

                this.controlPoints[i] = (first, second);
            }

            foreach (int[] dct in this.colorDct)
            {
                DecodeDct(dct, decoder, br, contextMap);
            }

            DecodeDct(this.sigmaDct, decoder, br, contextMap);
        }

        public Spline Dequantize(SplinePoint startingPoint, int quantizationAdjustment, float yToX, float yToB, ulong imageSize, ref ulong totalEstimatedAreaReached)
        {
            const ulong one = 1;
            ulong areaLimit = Math.Min((1024 * imageSize) + (one << 32), one << 42);

            Spline result = new(this.controlPoints.Length + 1);
            float px = MathF.Round(startingPoint.X, MidpointRounding.AwayFromZero);
            float py = MathF.Round(startingPoint.Y, MidpointRounding.AwayFromZero);
            ValidateSplinePointPos(px, py);
            int currentX = (int)px;
            int currentY = (int)py;
            result.ControlPoints.Add(new SplinePoint(currentX, currentY));
            int currentDeltaX = 0;
            int currentDeltaY = 0;
            ulong manhattanDistance = 0;
            foreach ((long x, long y) in this.controlPoints)
            {
                // The sums fit an int: the previous delta is within 2^23 and the delta-delta within 2^30.
                currentDeltaX += (int)x;
                currentDeltaY += (int)y;
                manhattanDistance += (ulong)(Math.Abs(currentDeltaX) + Math.Abs(currentDeltaY));
                if (manhattanDistance > areaLimit)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"Too large manhattan_distance reached: {manhattanDistance}");
                }

                ValidateSplinePointPos(currentDeltaX, currentDeltaY);
                currentX += currentDeltaX;
                currentY += currentDeltaY;
                ValidateSplinePointPos(currentX, currentY);
                result.ControlPoints.Add(new SplinePoint(currentX, currentY));
            }

            float invQuant = InvAdjustedQuant(quantizationAdjustment);
            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < DctSize; i++)
                {
                    float invDctFactor = i == 0 ? SqrtHalf : 1.0f;
                    result.ColorDct[c][i] = this.colorDct[c][i] * invDctFactor * ChannelWeight[c] * invQuant;
                }
            }

            for (int i = 0; i < DctSize; i++)
            {
                result.ColorDct[0][i] += yToX * result.ColorDct[1][i];
                result.ColorDct[2][i] += yToB * result.ColorDct[1][i];
            }

            ulong widthEstimate = 0;

            Span<ulong> color = stackalloc ulong[3];
            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < DctSize; i++)
                {
                    color[c] += (ulong)MathF.Ceiling(invQuant * Math.Abs(this.colorDct[c][i]));
                }
            }

            color[0] += (ulong)MathF.Ceiling(MathF.Abs(yToX)) * color[1];
            color[2] += (ulong)MathF.Ceiling(MathF.Abs(yToB)) * color[1];

            // This is not taking kChannelWeight into account, but up to constant factors
            // it gives an indication of the influence of the color values on the area
            // that will need to be rendered.
            ulong maxColor = Math.Max(color[1], Math.Max(color[0], color[2]));
            ulong logColor = Math.Max(one, (ulong)JxlBits.CeilLog2Nonzero(one + maxColor));

            float weightLimit = MathF.Ceiling(MathF.Sqrt(((float)areaLimit / logColor) / Math.Max(1UL, manhattanDistance)));

            for (int i = 0; i < DctSize; i++)
            {
                float invDctFactor = i == 0 ? SqrtHalf : 1.0f;
                result.SigmaDct[i] = this.sigmaDct[i] * invDctFactor * ChannelWeight[3] * invQuant;

                // If we include the factor kChannelWeight[3]=.3333f here, we get a
                // realistic area estimate. We leave it out to simplify the calculations,
                // and understand that this way we underestimate the area by a factor of
                // 1/(0.3333*0.3333). This is taken into account in the limits below.
                float weightF = MathF.Ceiling(invQuant * Math.Abs(this.sigmaDct[i]));
                ulong weight = (ulong)Min(weightLimit, Max(1.0f, weightF));
                widthEstimate += weight * weight * logColor;
            }

            totalEstimatedAreaReached += widthEstimate * manhattanDistance;
            if (totalEstimatedAreaReached > areaLimit)
            {
                JxlThrowHelper.ThrowInvalidImageContentException($"Too large total_estimated_area eached: {totalEstimatedAreaReached}");
            }

            return result;
        }

        private static void DecodeDct(int[] dct, JxlAnsSymbolReader decoder, JxlBitReader br, byte[] contextMap)
        {
            for (int i = 0; i < DctSize; i++)
            {
                dct[i] = JxlBits.UnpackSigned(decoder.ReadHybridUint(DctContext, br, contextMap));
                if (dct[i] == int.MinValue)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("The weird number in spline DCT");
                }
            }
        }
    }
}
