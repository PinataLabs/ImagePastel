// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>One decoded frame: one float plane per channel.</summary>
internal sealed class JxlDecodedFrame : IDisposable
{
    public JxlDecodedFrame(Buffer2D<float>[] planes) => this.Planes = planes;

    /// <summary>Gets the planes, colour channels first.</summary>
    public IReadOnlyList<Buffer2D<float>> Planes { get; }

    public void Dispose()
    {
        foreach (Buffer2D<float> plane in this.Planes)
        {
            plane.Dispose();
        }
    }
}
