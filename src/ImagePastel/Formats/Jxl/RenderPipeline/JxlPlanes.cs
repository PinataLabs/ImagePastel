// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// Helpers for the one-plane-per-channel arrays that the render pipeline, its sinks and the reference
/// slots pass around.
/// </summary>
internal static class JxlPlanes
{
    /// <summary>
    /// Disposes every plane of <paramref name="planes"/>. The array may be half built: planes that were
    /// never allocated are <see langword="null"/> and are skipped.
    /// </summary>
    /// <param name="planes">The planes.</param>
    public static void DisposeAll(Buffer2D<float>[] planes)
    {
        foreach (Buffer2D<float> plane in planes)
        {
            plane?.Dispose();
        }
    }

    /// <summary>
    /// Copies the top-left <paramref name="width"/> by <paramref name="height"/> samples of
    /// <paramref name="source"/> into <paramref name="destination"/>, row by row.
    /// </summary>
    /// <param name="source">The plane to copy from.</param>
    /// <param name="destination">The plane to copy into, at least <paramref name="width"/> by <paramref name="height"/>.</param>
    /// <param name="width">The number of samples to copy from each row.</param>
    /// <param name="height">The number of rows to copy.</param>
    public static void CopyRows(Buffer2D<float> source, Buffer2D<float> destination, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            source.DangerousGetRowSpan(y).Slice(0, width).CopyTo(destination.DangerousGetRowSpan(y));
        }
    }
}
