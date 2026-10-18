// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// sRGB (IEC 61966-2-1) as a matrix/TRC model, built from its published parameters rather than
/// from a profile file. It stands for an image that has no pixel profile.
/// </summary>
/// <remarks>
/// The primaries-to-XYZ and Bradford math repeats <see cref="IccProfileBuilder"/>'s on purpose. The
/// builder follows libjxl in single precision and its colorants are rounded to s15Fixed16, so JPEG XL
/// profiles stay byte-identical to libjxl's; this model computes them in double precision, so the
/// engine's sRGB matches Little CMS's built-in sRGB more closely. Sharing one would change the other's results.
/// </remarks>
internal static class IccSrgb
{
    /// <summary>
    /// Gets the matrix from linear sRGB to PCS XYZ: the BT.709 primaries with a D65 white
    /// (x = 0.3127, y = 0.3290), adapted to the D50 PCS white with the Bradford transform, in
    /// double precision.
    /// </summary>
    internal static IccMatrix Colorants { get; } = ComputeColorants();

    /// <summary>
    /// Creates the sRGB model.
    /// </summary>
    /// <param name="role">"source" or "target", for error messages.</param>
    /// <returns>The model.</returns>
    public static IccMatrixTrcModel Create(string role)
    {
        // The sRGB transfer function as an ICC parametric curve of function type 3.
        IccParametricCurveTagDataEntry curve = new(
            new IccParametricCurve(2.4F, 1 / 1.055F, 0.055F / 1.055F, 1 / 12.92F, 0.04045F));

        return new IccMatrixTrcModel(Colorants, [curve, curve, curve], IccColorSpaceType.CieXyz, role);
    }

    private static IccMatrix ComputeColorants()
    {
        // The XYZ of each primary with Y = 1, as columns.
        (double X, double Y, double Z) red = XyToXyz(0.64, 0.33);
        (double X, double Y, double Z) green = XyToXyz(0.30, 0.60);
        (double X, double Y, double Z) blue = XyToXyz(0.15, 0.06);
        IccMatrix primaries = new(
            (red.X, green.X, blue.X),
            (red.Y, green.Y, blue.Y),
            (red.Z, green.Z, blue.Z));
        (double whiteX, double whiteY, double whiteZ) = XyToXyz(0.3127, 0.3290);

        // Scale each primary so that RGB (1, 1, 1) gives the white point.
        if (!primaries.TryInvert(out IccMatrix inverse))
        {
            throw new InvalidOperationException("The sRGB primaries are degenerate.");
        }

        (double sr, double sg, double sb) = inverse.Transform(whiteX, whiteY, whiteZ);
        IccMatrix rgbToXyz = IccMatrix.Multiply(primaries, IccMatrix.Diagonal(sr, sg, sb));

        return IccMatrix.Multiply(Bradford(whiteX, whiteY, whiteZ), rgbToXyz);
    }

    private static (double X, double Y, double Z) XyToXyz(double x, double y) => (x / y, 1, (1 - x - y) / y);

    // The Bradford chromatic adaptation from the given white to the D50 PCS white.
    private static IccMatrix Bradford(double whiteX, double whiteY, double whiteZ)
    {
        IccMatrix cone = new(
            (0.8951, 0.2664, -0.1614),
            (-0.7502, 1.7135, 0.0367),
            (0.0389, -0.0685, 1.0296));
        cone.TryInvert(out IccMatrix coneInverse);

        (double sourceL, double sourceM, double sourceS) = cone.Transform(whiteX, whiteY, whiteZ);
        (double targetL, double targetM, double targetS) = cone.Transform(
            IccConnectionSpace.D50X, IccConnectionSpace.D50Y, IccConnectionSpace.D50Z);

        IccMatrix scale = IccMatrix.Diagonal(targetL / sourceL, targetM / sourceM, targetS / sourceS);
        return IccMatrix.Multiply(coneInverse, IccMatrix.Multiply(scale, cone));
    }
}
