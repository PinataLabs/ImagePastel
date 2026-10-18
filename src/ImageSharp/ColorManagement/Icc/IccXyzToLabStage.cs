// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// Converts PCS XYZ to PCS Lab in the first three channels.
/// </summary>
internal sealed class IccXyzToLabStage : IccStage
{
    /// <summary>
    /// The stage. It holds no state.
    /// </summary>
    public static readonly IccXyzToLabStage Instance = new();

    /// <inheritdoc/>
    public override void Invoke(Span<Vector4> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            ref Vector4 value = ref values[i];
            Vector3 lab = IccConnectionSpace.XyzToLab(new Vector3(value.X, value.Y, value.Z));
            value = new Vector4(lab, value.W);
        }
    }
}
