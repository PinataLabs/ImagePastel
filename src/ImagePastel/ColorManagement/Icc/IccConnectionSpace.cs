// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace PinataLabs.ImagePastel.ColorManagement.Icc;

/// <summary>
/// The ICC profile connection space (PCS). Pipelines carry PCS values unencoded: XYZ relative to
/// a white of Y = 1, and Lab with L* from 0 to 100, both for the D50 PCS illuminant.
/// </summary>
internal static class IccConnectionSpace
{
    /// <summary>
    /// The X component of the PCS illuminant, D50, as ICC.1:2022 section 7.2.16 gives it.
    /// </summary>
    public const double D50X = 0.9642;

    /// <summary>
    /// The Y component of the PCS illuminant.
    /// </summary>
    public const double D50Y = 1;

    /// <summary>
    /// The Z component of the PCS illuminant.
    /// </summary>
    public const double D50Z = 0.8249;

    // CIE 15:2004: the cube root applies above (6/29)^3; below it the function is linear.
    private const float Epsilon = 216F / 24389F;
    private const float Kappa = 24389F / 27F;

    /// <summary>
    /// The PCS illuminant in single precision.
    /// </summary>
    public static readonly Vector3 D50 = new((float)D50X, (float)D50Y, (float)D50Z);

    /// <summary>
    /// Converts PCS XYZ to PCS Lab.
    /// </summary>
    /// <param name="xyz">The XYZ value.</param>
    /// <returns>The Lab value: L*, a*, b*.</returns>
    public static Vector3 XyzToLab(Vector3 xyz)
    {
        float fx = F(xyz.X / D50.X);
        float fy = F(xyz.Y / D50.Y);
        float fz = F(xyz.Z / D50.Z);
        return new((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>
    /// Converts PCS Lab to PCS XYZ.
    /// </summary>
    /// <param name="lab">The Lab value: L*, a*, b*.</param>
    /// <returns>The XYZ value.</returns>
    public static Vector3 LabToXyz(Vector3 lab)
    {
        float fy = (lab.X + 16) / 116;
        float fx = fy + (lab.Y / 500);
        float fz = fy - (lab.Z / 200);
        return new(D50.X * FInverse(fx), D50.Y * FInverse(fy), D50.Z * FInverse(fz));
    }

    private static float F(float t) => t > Epsilon ? MathF.Cbrt(t) : ((Kappa * t) + 16) / 116;

    private static float FInverse(float t)
    {
        float cube = t * t * t;
        return cube > Epsilon ? cube : ((116 * t) - 16) / Kappa;
    }
}
