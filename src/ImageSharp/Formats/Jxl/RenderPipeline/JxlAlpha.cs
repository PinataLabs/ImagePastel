// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// PerformAlphaBlending, PerformAlphaWeightedAdd and PerformMulBlending in lib/jxl/alpha.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// The per-row blend operations of libjxl's <c>alpha.cc</c>. Each writes <c>output</c>, which must
/// not overlap the inputs.
/// </summary>
/// <remarks>
/// libjxl has two <c>PerformAlphaBlending</c> overloads: one for a whole layer (r, g, b and alpha) and
/// one for a single channel, which spots the alpha channel itself by pointer equality
/// (<c>bg == bga &amp;&amp; fg == fga</c>). Spans cannot be compared that way, so the alpha case is its
/// own method, <see cref="PerformAlphaBlendingOfAlpha"/>. A layer is the single-channel blend for each
/// colour plane plus <see cref="PerformAlphaBlendingOfAlpha"/>: the arithmetic is the same.
/// </remarks>
internal static class JxlAlpha
{
    /// <summary>
    /// Blends a channel that is not alpha: the colour of a layer, or an extra channel weighted by an alpha.
    /// </summary>
    /// <param name="bg">The background channel.</param>
    /// <param name="bga">The background alpha.</param>
    /// <param name="fg">The foreground channel.</param>
    /// <param name="fga">The foreground alpha.</param>
    /// <param name="output">The output.</param>
    /// <param name="alphaIsPremultiplied">Whether the channels are premultiplied by the alpha.</param>
    /// <param name="clamp">Whether the foreground alpha is clamped to [0, 1].</param>
    public static void PerformAlphaBlending(ReadOnlySpan<float> bg, ReadOnlySpan<float> bga, ReadOnlySpan<float> fg, ReadOnlySpan<float> fga, Span<float> output, bool alphaIsPremultiplied, bool clamp)
    {
        int numPixels = output.Length;
        if (alphaIsPremultiplied)
        {
            for (int x = 0; x < numPixels; ++x)
            {
                float fa = clamp ? Clamp(fga[x]) : fga[x];
                output[x] = fg[x] + (bg[x] * (1F - fa));
            }
        }
        else
        {
            for (int x = 0; x < numPixels; ++x)
            {
                float fa = clamp ? Clamp(fga[x]) : fga[x];
                float newA = 1F - ((1F - fa) * (1F - bga[x]));
                float rnewA = newA > 0 ? 1F / newA : 0F;
                output[x] = ((fg[x] * fa) + (bg[x] * bga[x] * (1F - fa))) * rnewA;
            }
        }
    }

    /// <summary>
    /// Blends an alpha channel: the alpha of a layer, or an extra channel that is its own alpha.
    /// </summary>
    /// <param name="bga">The background alpha.</param>
    /// <param name="fga">The foreground alpha.</param>
    /// <param name="output">The output.</param>
    /// <param name="clamp">Whether the foreground alpha is clamped to [0, 1].</param>
    public static void PerformAlphaBlendingOfAlpha(ReadOnlySpan<float> bga, ReadOnlySpan<float> fga, Span<float> output, bool clamp)
    {
        int numPixels = output.Length;
        for (int x = 0; x < numPixels; ++x)
        {
            float fa = clamp ? Clamp(fga[x]) : fga[x];
            output[x] = 1F - ((1F - fa) * (1F - bga[x]));
        }
    }

    /// <summary>
    /// Adds the foreground weighted by its alpha. libjxl copies the background when the foreground is
    /// its own alpha (<c>fg == fga</c>): that is <paramref name="foregroundIsAlpha"/>.
    /// </summary>
    /// <param name="bg">The background channel.</param>
    /// <param name="fg">The foreground channel.</param>
    /// <param name="fga">The foreground alpha.</param>
    /// <param name="output">The output.</param>
    /// <param name="clamp">Whether the foreground alpha is clamped to [0, 1].</param>
    /// <param name="foregroundIsAlpha">Whether the foreground channel is the alpha channel itself.</param>
    public static void PerformAlphaWeightedAdd(ReadOnlySpan<float> bg, ReadOnlySpan<float> fg, ReadOnlySpan<float> fga, Span<float> output, bool clamp, bool foregroundIsAlpha)
    {
        int numPixels = output.Length;
        if (foregroundIsAlpha)
        {
            bg.Slice(0, numPixels).CopyTo(output);
        }
        else if (clamp)
        {
            for (int x = 0; x < numPixels; ++x)
            {
                output[x] = bg[x] + (fg[x] * Clamp(fga[x]));
            }
        }
        else
        {
            for (int x = 0; x < numPixels; ++x)
            {
                output[x] = bg[x] + (fg[x] * fga[x]);
            }
        }
    }

    /// <summary>Multiplies the background by the foreground.</summary>
    /// <param name="bg">The background channel.</param>
    /// <param name="fg">The foreground channel.</param>
    /// <param name="output">The output.</param>
    /// <param name="clamp">Whether the foreground is clamped to [0, 1].</param>
    public static void PerformMulBlending(ReadOnlySpan<float> bg, ReadOnlySpan<float> fg, Span<float> output, bool clamp)
    {
        int numPixels = output.Length;
        if (clamp)
        {
            for (int x = 0; x < numPixels; ++x)
            {
                output[x] = bg[x] * Clamp(fg[x]);
            }
        }
        else
        {
            for (int x = 0; x < numPixels; ++x)
            {
                output[x] = bg[x] * fg[x];
            }
        }
    }

    // jxl::Clamp1(x, 0.0f, 1.0f): std::min(std::max(x, low), high).
    private static float Clamp(float x) => MathF.Min(MathF.Max(x, 0F), 1F);
}
