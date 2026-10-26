// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PinataLabs.ImagePastel.Formats.Pbm;

/// <summary>
/// Scales samples from the range declared by the maxval header to the full range of a component.
/// </summary>
internal static class SampleScaler
{
    /// <summary>
    /// Scales a sample so that <paramref name="maxPixelValue"/> maps to <paramref name="targetMaxValue"/>, rounding to nearest.
    /// </summary>
    /// <param name="sample">The non-negative sample value. Values above maxval are treated as maxval.</param>
    /// <param name="maxPixelValue">The maxval from the header, between 1 and 65535.</param>
    /// <param name="targetMaxValue">The maximum value of the target component, 255 or 65535.</param>
    /// <returns>The scaled sample.</returns>
    public static int Scale(int sample, int maxPixelValue, int targetMaxValue)
    {
        // Both factors are at most 65535, so the product fits in an unsigned 32-bit integer.
        uint clamped = (uint)Math.Min(sample, maxPixelValue);
        uint maxval = (uint)maxPixelValue;
        return (int)(((clamped * (uint)targetMaxValue) + (maxval / 2)) / maxval);
    }

    /// <summary>
    /// Scales 8-bit samples in place to the range 0 to 255.
    /// </summary>
    /// <param name="samples">The samples to scale.</param>
    /// <param name="maxPixelValue">The maxval from the header, between 1 and 255.</param>
    public static void ScaleBytes(Span<byte> samples, int maxPixelValue)
    {
        if (maxPixelValue == byte.MaxValue)
        {
            return;
        }

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)Scale(samples[i], maxPixelValue, byte.MaxValue);
        }
    }

    /// <summary>
    /// Converts 16-bit samples in place between the big-endian byte order of the binary Netpbm formats
    /// and the in-memory layout of <see cref="PixelFormats.L16"/>. The conversion is its own inverse.
    /// </summary>
    /// <param name="samples">The samples to convert, two bytes per sample.</param>
    public static void SwapShortsBigEndian(Span<byte> samples)
    {
        if (!BitConverter.IsLittleEndian)
        {
            return;
        }

        Span<ushort> values = MemoryMarshal.Cast<byte, ushort>(samples);
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReverseEndianness(values[i]);
        }
    }

    /// <summary>
    /// Scales 16-bit samples in place to the range 0 to 65535.
    /// The samples use the same in-memory layout as <see cref="PixelFormats.L16"/>.
    /// </summary>
    /// <param name="samples">The samples to scale, two bytes per sample.</param>
    /// <param name="maxPixelValue">The maxval from the header, between 256 and 65535.</param>
    public static void ScaleShorts(Span<byte> samples, int maxPixelValue)
    {
        if (maxPixelValue == ushort.MaxValue)
        {
            return;
        }

        Span<ushort> values = MemoryMarshal.Cast<byte, ushort>(samples);
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (ushort)Scale(values[i], maxPixelValue, ushort.MaxValue);
        }
    }
}
