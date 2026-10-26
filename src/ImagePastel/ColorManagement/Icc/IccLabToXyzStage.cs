// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace PinataLabs.ImagePastel.ColorManagement.Icc;

/// <summary>
/// Converts PCS Lab to PCS XYZ in the first three channels.
/// </summary>
internal sealed class IccLabToXyzStage : IccStage
{
    /// <summary>
    /// The stage. It holds no state.
    /// </summary>
    public static readonly IccLabToXyzStage Instance = new();

    /// <inheritdoc/>
    public override void Invoke(Span<Vector4> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            ref Vector4 value = ref values[i];
            Vector3 xyz = IccConnectionSpace.LabToXyz(new Vector3(value.X, value.Y, value.Z));
            value = new Vector4(xyz, value.W);
        }
    }
}
