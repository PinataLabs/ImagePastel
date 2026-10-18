// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

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
}
