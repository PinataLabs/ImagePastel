// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// SplineStage in lib/jxl/render_pipeline/stage_splines.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// Adds the splines of a frame to its three colour planes, in place, one row at a time.
/// </summary>
/// <remarks>
/// libjxl's stage also draws into the columns its later stages read past the image edge, but the
/// pipeline mirrors those columns again before a stage reads them, so drawing the frame's own columns
/// gives the same result.
/// </remarks>
internal sealed class JxlSplinesStage
{
    private readonly JxlSplines splines;
    private float[] discard = Array.Empty<float>();

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlSplinesStage"/> class.
    /// </summary>
    /// <param name="splines">The splines of the frame, read with its DC global section.</param>
    public JxlSplinesStage(JxlSplines splines) => this.splines = splines;

    /// <summary>Adds the splines to row <paramref name="ypos"/> of the frame.</summary>
    /// <param name="planes">The frame planes: the colour planes, then the extra channels, which it ignores.</param>
    /// <param name="numColorPlanes">The number of colour planes: three, or one for a grey image that is not XYB and a frame that drops to grey before the patches.</param>
    /// <param name="xsize">The width of the frame.</param>
    /// <param name="ypos">The row.</param>
    public void ProcessRow(Buffer2D<float>[] planes, int numColorPlanes, int xsize, int ypos)
    {
        Span<float> rowX = planes[0].DangerousGetRowSpan(ypos);
        if (numColorPlanes == 3)
        {
            this.splines.AddToRow(rowX, planes[1].DangerousGetRowSpan(ypos), planes[2].DangerousGetRowSpan(ypos), ypos, 0, xsize);
            return;
        }

        // A grey image that is not XYB has one colour plane here unless a later stage of the frame mixes
        // or keeps them (JxlRenderPipeline.Finish). libjxl has three and outputs the first, so only the
        // first colour of each spline is kept.
        if (this.discard.Length < 2 * xsize)
        {
            this.discard = new float[2 * xsize];
        }

        this.splines.AddToRow(rowX, this.discard.AsSpan(0, xsize), this.discard.AsSpan(xsize, xsize), ypos, 0, xsize);
    }
}
