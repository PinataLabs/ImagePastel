// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// The encoder's hooks for tests, in one place (<see cref="IJxlEncoderOptions.TestHooks"/>). None of them is
/// one of the options of ADR 0003. Each is off by default, so an encode with default hooks, or with none,
/// is the encode that the options give.
/// </summary>
internal sealed class JxlEncoderTestHooks
{
    /// <summary>
    /// Gets or sets a value indicating whether a lossy frame keeps every coefficient order natural, instead
    /// of customising the DCT8 order as libjxl does. Tests use it to compare an encode with the
    /// natural-order encode of the same image.
    /// </summary>
    public bool NaturalCoeffOrders { get; set; }

    /// <summary>
    /// Gets or sets a callback that a lossy frame runs right after <c>ComputeARHeuristics</c> sets its EPF
    /// sharpness, before the AC metadata is written, or <see langword="null"/>. Tests use it to change the
    /// sharpness the file carries and to reconstruct the frame from the encoder's state there
    /// (<see cref="JxlEncReconstruct.ReconstructImage"/>), as the dynamic EPF search does.
    /// </summary>
    public Action<JxlFrameHeader, JxlPassesEncoderState> ArHeuristicsProbe { get; set; }
}
