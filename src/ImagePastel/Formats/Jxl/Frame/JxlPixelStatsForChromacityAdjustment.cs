// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_frame.cc (PixelStatsForChromacityAdjustment).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.Frame;

/// <summary>
/// libjxl's <c>PixelStatsForChromacityAdjustment</c>: the worst-case pixel differences of the X and B planes
/// of the XYB image, from which the encoder raises the X and B quantisation scales from Squirrel down.
/// The default value (no <see cref="Calc"/>) is not pixelized.
/// </summary>
internal struct JxlPixelStatsForChromacityAdjustment
{
    /// <summary>Gets or sets the largest step between neighbouring X samples.</summary>
    public float Dx { get; set; }

    /// <summary>Gets or sets the largest step between neighbouring B - Y samples.</summary>
    public float Db { get; set; }

    /// <summary>Gets or sets the largest exposed blue (B above 1.2 Y) times its local B steps.</summary>
    public float ExposedBlue { get; set; }

    /// <summary>The largest horizontal or vertical step of the plane inside the rect.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="rect">The rect.</param>
    /// <returns>The largest step.</returns>
    public static float CalcPlane(Buffer2D<float> plane, JxlRect rect)
    {
        float xmax = 0;
        float ymax = 0;
        int x0 = (int)rect.X0;
        for (long ty = 1; ty < rect.YSize; ++ty)
        {
            Span<float> row = plane.DangerousGetRowSpan((int)(rect.Y0 + ty)).Slice(x0);
            Span<float> prevRow = plane.DangerousGetRowSpan((int)(rect.Y0 + ty - 1)).Slice(x0);
            for (int tx = 1; tx < rect.XSize; ++tx)
            {
                float cur = row[tx];
                float prevRowValue = prevRow[tx];
                float prev = row[tx - 1];
                xmax = MathF.Max(xmax, MathF.Abs(cur - prev));
                ymax = MathF.Max(ymax, MathF.Abs(cur - prevRowValue));
            }
        }

        return MathF.Max(xmax, ymax);
    }

    /// <summary>Sets <see cref="ExposedBlue"/> and <see cref="Db"/> from the Y and B planes inside the rect.</summary>
    /// <param name="planeY">The Y plane.</param>
    /// <param name="planeB">The B plane.</param>
    /// <param name="rect">The rect.</param>
    public void CalcExposedBlue(Buffer2D<float> planeY, Buffer2D<float> planeB, JxlRect rect)
    {
        float eb = 0;
        float xmax = 0;
        float ymax = 0;
        int x0 = (int)rect.X0;
        for (long ty = 1; ty < rect.YSize; ++ty)
        {
            Span<float> rowY = planeY.DangerousGetRowSpan((int)(rect.Y0 + ty)).Slice(x0);
            Span<float> rowB = planeB.DangerousGetRowSpan((int)(rect.Y0 + ty)).Slice(x0);
            Span<float> prevRowY = planeY.DangerousGetRowSpan((int)(rect.Y0 + ty - 1)).Slice(x0);
            Span<float> prevRowB = planeB.DangerousGetRowSpan((int)(rect.Y0 + ty - 1)).Slice(x0);
            for (int tx = 1; tx < rect.XSize; ++tx)
            {
                float curY = rowY[tx];
                float curB = rowB[tx];

                // libjxl's 1.2 is a double literal, so the product and the difference are in double.
                float exposedB = (float)(curB - (curY * 1.2));
                float diffB = curB - curY;
                float prevRow = prevRowB[tx];
                float prev = rowB[tx - 1];
                float diffPrevRow = prevRow - prevRowY[tx];
                float diffPrev = prev - rowY[tx - 1];
                xmax = MathF.Max(xmax, MathF.Abs(diffB - diffPrev));
                ymax = MathF.Max(ymax, MathF.Abs(diffB - diffPrevRow));
                if (exposedB >= 0)
                {
                    exposedB *= MathF.Abs(curB - prev) + MathF.Abs(curB - prevRow);
                    eb = MathF.Max(eb, exposedB);
                }
            }
        }

        this.ExposedBlue = eb;
        this.Db = MathF.Max(xmax, ymax);
    }

    /// <summary>Measures the XYB image inside the rect.</summary>
    /// <param name="opsin">The X, Y and B planes.</param>
    /// <param name="rect">The rect.</param>
    public void Calc(Buffer2D<float>[] opsin, JxlRect rect)
    {
        this.Dx = CalcPlane(opsin[0], rect);
        this.CalcExposedBlue(opsin[1], opsin[2], rect);
    }

    /// <summary>How much to raise the X scale above 2, from 0 to 3.</summary>
    /// <returns>The raise.</returns>
    public readonly int HowMuchIsXChannelPixelized()
    {
        // The thresholds are double literals in libjxl, so the float is compared as a double.
        if (this.Dx >= 0.026)
        {
            return 3;
        }

        if (this.Dx >= 0.022)
        {
            return 2;
        }

        if (this.Dx >= 0.015)
        {
            return 1;
        }

        return 0;
    }

    /// <summary>How much to raise the B scale above 2, from 0 to 3.</summary>
    /// <returns>The raise.</returns>
    public readonly int HowMuchIsBChannelPixelized()
    {
        int add = this.ExposedBlue >= 0.13 ? 1 : 0;
        if (this.Db > 0.38)
        {
            return 2 + add;
        }

        if (this.Db > 0.33)
        {
            return 1 + add;
        }

        if (this.Db > 0.28)
        {
            return add;
        }

        return 0;
    }
}
