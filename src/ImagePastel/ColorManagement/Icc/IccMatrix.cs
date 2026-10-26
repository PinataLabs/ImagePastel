// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace PinataLabs.ImagePastel.ColorManagement.Icc;

/// <summary>
/// A 3x3 matrix with an offset, in double precision: <c>out = M * in + offset</c>, with column vectors.
/// The engine builds and combines its matrices with this type and evaluates them in single precision.
/// </summary>
internal readonly struct IccMatrix
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IccMatrix"/> struct.
    /// </summary>
    /// <param name="row1">The first row.</param>
    /// <param name="row2">The second row.</param>
    /// <param name="row3">The third row.</param>
    /// <param name="offset">The offset added after the multiplication.</param>
    public IccMatrix(
        (double X, double Y, double Z) row1,
        (double X, double Y, double Z) row2,
        (double X, double Y, double Z) row3,
        (double X, double Y, double Z) offset = default)
    {
        this.Row1 = row1;
        this.Row2 = row2;
        this.Row3 = row3;
        this.Offset = offset;
    }

    /// <summary>
    /// Gets the first row.
    /// </summary>
    public (double X, double Y, double Z) Row1 { get; }

    /// <summary>
    /// Gets the second row.
    /// </summary>
    public (double X, double Y, double Z) Row2 { get; }

    /// <summary>
    /// Gets the third row.
    /// </summary>
    public (double X, double Y, double Z) Row3 { get; }

    /// <summary>
    /// Gets the offset.
    /// </summary>
    public (double X, double Y, double Z) Offset { get; }

    /// <summary>
    /// Creates the matrix whose columns are the three given vectors, such as an RGB profile's colorants.
    /// </summary>
    /// <param name="column1">The first column.</param>
    /// <param name="column2">The second column.</param>
    /// <param name="column3">The third column.</param>
    /// <returns>The matrix.</returns>
    public static IccMatrix FromColumns(Vector3 column1, Vector3 column2, Vector3 column3)
        => new(
            (column1.X, column2.X, column3.X),
            (column1.Y, column2.Y, column3.Y),
            (column1.Z, column2.Z, column3.Z));

    /// <summary>
    /// Creates a diagonal matrix.
    /// </summary>
    /// <param name="d1">The first diagonal entry.</param>
    /// <param name="d2">The second diagonal entry.</param>
    /// <param name="d3">The third diagonal entry.</param>
    /// <returns>The matrix.</returns>
    public static IccMatrix Diagonal(double d1, double d2, double d3)
        => new((d1, 0, 0), (0, d2, 0), (0, 0, d3));

    /// <summary>
    /// Returns the matrix that applies <paramref name="first"/> and then <paramref name="second"/>.
    /// </summary>
    /// <param name="second">The matrix applied last.</param>
    /// <param name="first">The matrix applied first.</param>
    /// <returns>The combined matrix.</returns>
    public static IccMatrix Multiply(in IccMatrix second, in IccMatrix first)
    {
        (double X, double Y, double Z) column1 = second.Transform(first.Row1.X, first.Row2.X, first.Row3.X, false);
        (double X, double Y, double Z) column2 = second.Transform(first.Row1.Y, first.Row2.Y, first.Row3.Y, false);
        (double X, double Y, double Z) column3 = second.Transform(first.Row1.Z, first.Row2.Z, first.Row3.Z, false);
        (double X, double Y, double Z) offset = second.Transform(first.Offset.X, first.Offset.Y, first.Offset.Z, true);

        return new(
            (column1.X, column2.X, column3.X),
            (column1.Y, column2.Y, column3.Y),
            (column1.Z, column2.Z, column3.Z),
            offset);
    }

    /// <summary>
    /// Applies the matrix and the offset to a vector.
    /// </summary>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    /// <param name="z">The third component.</param>
    /// <returns>The transformed vector.</returns>
    public (double X, double Y, double Z) Transform(double x, double y, double z) => this.Transform(x, y, z, true);

    /// <summary>
    /// Inverts the matrix, offset included.
    /// </summary>
    /// <param name="inverse">The inverse.</param>
    /// <returns><see langword="false"/> if the matrix is singular.</returns>
    public bool TryInvert(out IccMatrix inverse)
    {
        (double a, double b, double c) = this.Row1;
        (double d, double e, double f) = this.Row2;
        (double g, double h, double i) = this.Row3;

        double cofactor1 = (e * i) - (f * h);
        double cofactor2 = (f * g) - (d * i);
        double cofactor3 = (d * h) - (e * g);
        double determinant = (a * cofactor1) + (b * cofactor2) + (c * cofactor3);

        // A determinant this small counts as singular, as in Little CMS (MATRIX_DET_TOLERANCE).
        if (!(Math.Abs(determinant) >= 1e-4))
        {
            inverse = default;
            return false;
        }

        double s = 1 / determinant;
        IccMatrix linear = new(
            (cofactor1 * s, ((c * h) - (b * i)) * s, ((b * f) - (c * e)) * s),
            (cofactor2 * s, ((a * i) - (c * g)) * s, ((c * d) - (a * f)) * s),
            (cofactor3 * s, ((b * g) - (a * h)) * s, ((a * e) - (b * d)) * s));

        inverse = new(linear.Row1, linear.Row2, linear.Row3, linear.Transform(-this.Offset.X, -this.Offset.Y, -this.Offset.Z, false));
        return true;
    }

    private (double X, double Y, double Z) Transform(double x, double y, double z, bool addOffset)
    {
        double offsetX = addOffset ? this.Offset.X : 0;
        double offsetY = addOffset ? this.Offset.Y : 0;
        double offsetZ = addOffset ? this.Offset.Z : 0;
        return ((this.Row1.X * x) + (this.Row1.Y * y) + (this.Row1.Z * z) + offsetX,
            (this.Row2.X * x) + (this.Row2.Y * y) + (this.Row2.Z * z) + offsetY,
            (this.Row3.X * x) + (this.Row3.Y * y) + (this.Row3.Z * z) + offsetZ);
    }
}
