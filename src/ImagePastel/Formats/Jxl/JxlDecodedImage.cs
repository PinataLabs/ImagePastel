// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// A decoded JPEG XL image as float planes, for the conformance tests: every frame holds one plane per
/// channel (colour channels, then extra channels in codestream order). Values are unclamped, alpha is
/// as coded and spot colours are not rendered. A plane may be larger than the frame: the colour planes
/// of a VarDCT frame keep their padding to whole blocks.
/// </summary>
internal sealed class JxlDecodedImage : IDisposable
{
    /// <summary>Gets or sets the width of every frame.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the height of every frame.</summary>
    public int Height { get; set; }

    /// <summary>Gets the frames.</summary>
    public List<JxlDecodedFrame> Frames { get; } = new();

    public void Dispose()
    {
        foreach (JxlDecodedFrame frame in this.Frames)
        {
            frame.Dispose();
        }

        this.Frames.Clear();
    }
}
