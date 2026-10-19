// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/transform/enc_palette.h and lib/jxl/modular/transform/enc_palette.cc (FwdPalette and
// FwdPaletteIteration, lossless paths only).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Linq;
using static SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms.JxlTransform;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;

/// <summary>
/// Applies the palette transform; the inverse is <see cref="JxlPalette.InvPalette"/>. Only libjxl's
/// lossless palette is ported: the lossy palette, its delta entries and its dithering are used only by
/// lossy encoding.
/// </summary>
internal static class JxlPaletteEncoder
{
    /// <summary>
    /// Replaces channels <paramref name="beginC"/> to <paramref name="endC"/> by one channel of indices
    /// into a palette of at most <paramref name="nbColors"/> colours, inserted as a meta channel at the
    /// front. Returns <see langword="false"/>, with the image unchanged, when the channels have too many
    /// colours or only one.
    /// </summary>
    /// <param name="input">The image.</param>
    /// <param name="beginC">The first channel.</param>
    /// <param name="endC">The last channel.</param>
    /// <param name="nbColors">The most colours to allow; on success, the number of explicit colours.</param>
    /// <param name="nbDeltas">On success, the number of delta entries: always 0.</param>
    /// <param name="ordered">Whether to sort the colours of a palette of three or more channels by luma.</param>
    /// <param name="predictor">On success, the predictor of delta entries: always <see cref="JxlPredictor.Zero"/>.</param>
    /// <returns>Whether the transform was applied.</returns>
    public static bool FwdPalette(JxlModularImage input, uint beginC, uint endC, ref uint nbColors, ref uint nbDeltas, bool ordered, ref JxlPredictor predictor)
    {
        // libjxl makes a first pass here for a lossy palette, then the final one. The lossy pass and
        // its PaletteIterationData are not ported, so only the final pass runs.
        return FwdPaletteIteration(input, beginC, endC, ref nbColors, ref nbDeltas, ordered, ref predictor);
    }

    private static bool FwdPaletteIteration(JxlModularImage input, uint beginC, uint endC, ref uint nbColors, ref uint nbDeltas, bool ordered, ref JxlPredictor predictor)
    {
        CheckEqualChannels(input, beginC, endC);
        if (beginC < input.NbMetaChannels)
        {
            JxlThrowHelper.ThrowImageFormatException("Palette over meta channels");
        }

        int nb = (int)(endC - beginC + 1);
        List<JxlModularChannel> channels = input.Channel;
        int w = channels[(int)beginC].W;
        int h = channels[(int)beginC].H;
        if (input.BitDepth >= 32)
        {
            return false;
        }

        if (nbColors < 2)
        {
            return false;
        }

        if (nb == 1)
        {
            return FwdChannelPalette(input, (int)beginC, ref nbColors, ref predictor);
        }

        nbDeltas = 0;

        // libjxl keeps the colours seen in a std::set and their counts in a std::map. Only membership
        // and counts are used, so one dictionary holds both.
        ColorComparer comparer = new();
        Dictionary<int[], int> colorFreqMap = new(comparer);
        List<int[]> candidatePaletteImageOrder = new();
        Dictionary<int[], int> invPalette = new(comparer);

        HashSet<int[]> implicitColor = new(comparer);
        List<int[]> implicitColors = new(JxlPalette.ImplicitPaletteSize);
        for (int k = 0; k < JxlPalette.ImplicitPaletteSize; k++)
        {
            int[] implicitValue = new int[nb];
            for (int i = 0; i < nb; i++)
            {
                implicitValue[i] = JxlPalette.GetPaletteValue(null, k, i, 0, input.BitDepth);
            }

            implicitColor.Add(implicitValue);
            implicitColors.Add(implicitValue);
        }

        int[] color = new int[nb];
        uint implicitColorsUsed = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < nb; c++)
                {
                    color[c] = channels[(int)beginC + c].Row(y)[x];
                }

                if (colorFreqMap.TryGetValue(color, out int freq))
                {
                    colorFreqMap[color] = freq + 1;
                    continue;
                }

                int[] newColor = (int[])color.Clone();
                colorFreqMap.Add(newColor, 1);
                if (implicitColor.Contains(newColor))
                {
                    implicitColorsUsed++;
                }
                else
                {
                    candidatePaletteImageOrder.Add(newColor);
                    if (candidatePaletteImageOrder.Count > nbColors)
                    {
                        // too many colors
                        return false;
                    }
                }
            }
        }

        nbColors = nbDeltas + (uint)candidatePaletteImageOrder.Count;

        // not useful to make a single-color palette
        if (nbColors + implicitColorsUsed == 1)
        {
            return false;
        }

        for (int k = 0; k < JxlPalette.ImplicitPaletteSize; k++)
        {
            // still add the color to the explicit palette if it is frequent enough
            int[] implicitValue = implicitColors[k];
            if (colorFreqMap.TryGetValue(implicitValue, out int freq) && freq > 10)
            {
                nbColors++;
                candidatePaletteImageOrder.Add(implicitValue);
            }
        }

        for (int k = 0; k < JxlPalette.ImplicitPaletteSize; k++)
        {
            invPalette[implicitColors[k]] = (int)nbColors + k;
        }

        JxlModularChannel pch = JxlModularChannel.Create(input.Allocator, (int)nbColors, nb, -1, -1);

        // Separate the palette in two buckets, first the common colors, then the rare colors.
        // Within each bucket, the colors are sorted on luma (times alpha).
        const float freqThreshold = 4; // arbitrary threshold
        if (ordered && nb >= 3)
        {
            // sort on luma (multiplied by alpha if available). libjxl uses std::sort, which is not
            // stable, so colours with equal keys may come out in another order than libjxl's. A stable
            // sort keeps this deterministic, and any order decodes to the same pixels.
            candidatePaletteImageOrder = candidatePaletteImageOrder.OrderBy(SortKey).ToList();

            float SortKey(int[] p)
            {
                float key = (0.299f * p[0]) + (0.587f * p[1]) + (0.114f * p[2]) + 0.1f;
                if (p.Length > 3)
                {
                    key *= 1f + p[3];
                }

                // put common colors first, transparent dark to opaque bright, then rare colors, bright
                // to dark
                return colorFreqMap.TryGetValue(p, out int freq) && freq > freqThreshold ? -key : key;
            }
        }

        int clr = 0;
        foreach (int[] pcol in candidatePaletteImageOrder)
        {
            for (int i = 0; i < nb; i++)
            {
                pch.Row(i)[(int)nbDeltas + clr] = pcol[i];
            }

            invPalette[pcol] = clr;
            clr++;
        }

        for (int y = 0; y < h; y++)
        {
            Span<int> p = channels[(int)beginC].Row(y);
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < nb; c++)
                {
                    color[c] = channels[(int)beginC + c].Row(y)[x];
                }

                p[x] = invPalette[color];
            }
        }

        predictor = JxlPredictor.Zero;
        input.NbMetaChannels++;
        for (int c = (int)beginC + 1; c <= endC; c++)
        {
            channels[c].Dispose();
        }

        channels.RemoveRange((int)beginC + 1, nb - 1);
        channels.Insert(0, pch);
        nbColors -= nbDeltas;
        return true;
    }

    // Channel palette special case (libjxl's FLIF-style channel compaction): the values a single
    // channel uses, in ascending order.
    private static bool FwdChannelPalette(JxlModularImage input, int beginC, ref uint nbColors, ref JxlPredictor predictor)
    {
        if (nbColors == 0)
        {
            return false;
        }

        JxlModularChannel channel = input.Channel[beginC];
        int w = channel.W;
        int h = channel.H;
        JxlTransformEncoder.ComputeMinMax(channel, out int minval, out int maxval);
        long lookupTableSize = (long)maxval - minval + 1;
        int[] palette;
        if (lookupTableSize > JxlPalette.MaxPaletteLookupTableSize)
        {
            // a lookup table would use too much memory, instead use a slower approach with std::set
            SortedSet<int> chpalette = new();
            for (int y = 0; y < h; y++)
            {
                Span<int> p = channel.Row(y);
                for (int x = 0; x < w; x++)
                {
                    if (chpalette.Add(p[x]) && chpalette.Count > (int)nbColors)
                    {
                        return false;
                    }
                }
            }

            palette = chpalette.ToArray();
            for (int y = 0; y < h; y++)
            {
                Span<int> p = channel.Row(y);
                for (int x = 0; x < w; x++)
                {
                    // libjxl searches the palette linearly; the palette is sorted, so a binary search
                    // finds the same index.
                    p[x] = Array.BinarySearch(palette, p[x]);
                }
            }
        }
        else
        {
            int[] lookup = new int[lookupTableSize];
            int idx = 0;
            for (int y = 0; y < h; y++)
            {
                Span<int> p = channel.Row(y);
                for (int x = 0; x < w; x++)
                {
                    if (lookup[p[x] - minval] == 0)
                    {
                        lookup[p[x] - minval] = 1;
                        idx++;
                        if (idx > (int)nbColors)
                        {
                            return false;
                        }
                    }
                }
            }

            palette = new int[idx];
            idx = 0;
            for (int i = 0; i < lookupTableSize; i++)
            {
                if (lookup[i] != 0)
                {
                    palette[idx] = i + minval;
                    lookup[i] = idx;
                    idx++;
                }
            }

            for (int y = 0; y < h; y++)
            {
                Span<int> p = channel.Row(y);
                for (int x = 0; x < w; x++)
                {
                    p[x] = lookup[p[x] - minval];
                }
            }
        }

        JxlModularChannel pch = JxlModularChannel.Create(input.Allocator, palette.Length, 1, -1, -1);
        palette.CopyTo(pch.Row(0));
        nbColors = (uint)palette.Length;
        predictor = JxlPredictor.Zero;
        input.NbMetaChannels++;
        input.Channel.Insert(0, pch);
        return true;
    }

    // Compares colours by value, as libjxl's std::vector keys do.
    private sealed class ColorComparer : IEqualityComparer<int[]>
    {
        public bool Equals(int[] x, int[] y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(int[] obj)
        {
            HashCode hash = default;
            foreach (int v in obj)
            {
                hash.Add(v);
            }

            return hash.ToHashCode();
        }
    }
}
