// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// ReferenceFrame in lib/jxl/passes_state.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.Frame;

/// <summary>
/// A frame kept in a reference slot, for later frames to blend onto: one float plane per channel,
/// colour planes first, as the render pipeline holds them.
/// </summary>
internal sealed class JxlReferenceFrame : IDisposable
{
    public JxlReferenceFrame(Buffer2D<float>[] planes, int width, int height, bool isInXyb)
    {
        this.Planes = planes;
        this.Width = width;
        this.Height = height;
        this.IsInXyb = isInXyb;
    }

    /// <summary>Gets the planes, at least <see cref="Width"/> by <see cref="Height"/>: padded VarDCT colour planes are larger.</summary>
    public Buffer2D<float>[] Planes { get; private set; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// Gets a value indicating whether the frame was saved before its colour transform: libjxl's
    /// <c>ib_is_in_xyb</c>, which it sets from <c>save_before_color_transform</c> for every image.
    /// </summary>
    public bool IsInXyb { get; }

    public void Dispose()
    {
        JxlPlanes.DisposeAll(this.Planes);
        this.Planes = Array.Empty<Buffer2D<float>>();
    }
}
