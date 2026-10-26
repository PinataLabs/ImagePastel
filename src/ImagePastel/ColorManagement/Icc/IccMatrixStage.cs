// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// Applies a 3x3 matrix and an offset to the first three channels.
/// </summary>
internal sealed class IccMatrixStage : IccStage
{
    private readonly Vector3 row1;
    private readonly Vector3 row2;
    private readonly Vector3 row3;
    private readonly Vector3 offset;

    /// <summary>
    /// Initializes a new instance of the <see cref="IccMatrixStage"/> class.
    /// </summary>
    /// <param name="matrix">The matrix.</param>
    public IccMatrixStage(IccMatrix matrix)
    {
        this.Matrix = matrix;
        this.row1 = ToVector3(matrix.Row1);
        this.row2 = ToVector3(matrix.Row2);
        this.row3 = ToVector3(matrix.Row3);
        this.offset = ToVector3(matrix.Offset);
    }

    /// <summary>
    /// Gets the matrix, so that adjacent matrix stages can be combined into one.
    /// </summary>
    public IccMatrix Matrix { get; }

    /// <inheritdoc/>
    public override void Invoke(Span<Vector4> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            ref Vector4 value = ref values[i];
            Vector3 v = new(value.X, value.Y, value.Z);
            value.X = Vector3.Dot(this.row1, v) + this.offset.X;
            value.Y = Vector3.Dot(this.row2, v) + this.offset.Y;
            value.Z = Vector3.Dot(this.row3, v) + this.offset.Z;
        }
    }

    private static Vector3 ToVector3((double X, double Y, double Z) v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
