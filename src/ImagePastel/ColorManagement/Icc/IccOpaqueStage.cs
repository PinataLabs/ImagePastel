// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// Sets the fourth channel (W) to 1. A four-channel device value, such as CMYK, leaves its last
/// channel in W after its table has turned it into three PCS channels. The device value has no
/// alpha, so the converted color is opaque.
/// </summary>
internal sealed class IccOpaqueStage : IccStage
{
    /// <summary>
    /// The stage. It holds no state.
    /// </summary>
    public static readonly IccOpaqueStage Instance = new();

    /// <inheritdoc/>
    public override void Invoke(Span<Vector4> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i].W = 1;
        }
    }
}
