// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Numerics;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// Keeps the rendered float planes as they are: unclamped, every channel. It backs
/// <see cref="JxlDecoderCore.DecodeToPlanes"/>, which the conformance tests compare with the references.
/// </summary>
internal sealed class JxlPlanesSink : IJxlRenderSink
{
    /// <summary>Gets the decoded image the frames are added to.</summary>
    public JxlDecodedImage Image { get; } = new();

    /// <summary>Gets a value indicating whether the sink takes display-encoded samples: it keeps djxl's linear output.</summary>
    public bool DisplayEncoded => false;

    public bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height)
    {
        this.Image.Width = width;
        this.Image.Height = height;
        this.Image.Frames.Add(new JxlDecodedFrame(planes));
        return true;
    }
}
