// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// Applies one curve per channel (<c>curveType</c> or <c>parametricCurveType</c>), or the inverse of each.
/// </summary>
internal sealed class IccCurveStage : IccStage
{
    private readonly IccTagDataEntry[] curves;
    private readonly bool inverse;

    /// <summary>
    /// Initializes a new instance of the <see cref="IccCurveStage"/> class.
    /// </summary>
    /// <param name="curves">One curve for each of the first one to four channels.</param>
    /// <param name="inverse">Whether to apply the inverse curves.</param>
    public IccCurveStage(IccTagDataEntry[] curves, bool inverse)
    {
        DebugGuard.MustBeBetweenOrEqualTo(curves.Length, 1, 4, nameof(curves));
        this.curves = curves;
        this.inverse = inverse;
    }

    /// <inheritdoc/>
    public override void Invoke(Span<Vector4> values)
    {
        for (int channel = 0; channel < this.curves.Length; channel++)
        {
            IccTagDataEntry curve = this.curves[channel];
            for (int i = 0; i < values.Length; i++)
            {
                ref Vector4 value = ref values[i];
                switch (channel)
                {
                    case 0:
                        value.X = this.Apply(curve, value.X);
                        break;
                    case 1:
                        value.Y = this.Apply(curve, value.Y);
                        break;
                    case 2:
                        value.Z = this.Apply(curve, value.Z);
                        break;
                    default:
                        value.W = this.Apply(curve, value.W);
                        break;
                }
            }
        }
    }

    private float Apply(IccTagDataEntry curve, float value)
    {
        float result;
        if (this.inverse)
        {
            IccCurveEvaluator.TryEvaluateInverse(curve, value, out result);
        }
        else
        {
            IccCurveEvaluator.TryEvaluate(curve, value, out result);
        }

        return result;
    }
}
