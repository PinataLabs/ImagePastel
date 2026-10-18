// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// One step of an <see cref="IccPipeline"/>. A stage transforms a row of values in place. Each
/// <see cref="Vector4"/> holds up to four channels in X, Y, Z and W. A stage that works on three
/// channels leaves W alone, which is how alpha passes through an RGB or gray conversion untouched.
/// </summary>
/// <remarks>
/// The stage kinds are curves (<see cref="IccCurveStage"/>), matrices
/// (<see cref="IccMatrixStage"/>), the PCS encodings (<see cref="IccXyzToLabStage"/>,
/// <see cref="IccLabToXyzStage"/>), color lookup tables (<see cref="IccClutStage"/>) and
/// <see cref="IccOpaqueStage"/>.
/// </remarks>
internal abstract class IccStage
{
    /// <summary>
    /// Transforms the values in place.
    /// </summary>
    /// <param name="values">The values.</param>
    public abstract void Invoke(Span<Vector4> values);
}
