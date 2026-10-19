// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/transform/palette.h and lib/jxl/modular/transform/palette.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using static SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms.JxlTransform;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;

/// <summary>
/// Undoes the palette transform. Indices below zero select delta palette entries; indices past the
/// palette select an implicit colour cube.
/// </summary>
internal static class JxlPalette
{
    /// <summary>The largest sample range the encoder counts with a lookup table instead of a set.</summary>
    public const int MaxPaletteLookupTableSize = 1 << 16;

    /// <summary>The number of implicit colours: both colour cubes.</summary>
    public const int ImplicitPaletteSize = LargeCubeOffset + (LargeCube * LargeCube * LargeCube);

    private const int RgbChannels = 3;

    // 5x5x5 color cube for the larger cube.
    private const int LargeCube = 5;

    // Smaller interleaved color cube to fill the holes of the larger cube.
    private const int SmallCube = 4;
    private const int SmallCubeBits = 2;

    // kSmallCube ** 3
    private const int LargeCubeOffset = SmallCube * SmallCube * SmallCube;

    // 72 entries of (R, G, B).
    private static readonly int[] DeltaPalette =
    {
        0, 0, 0, 4, 4, 4, 11, 0, 0,
        0, 0, -13, 0, -12, 0, -10, -10, -10,
        -18, -18, -18, -27, -27, -27, -18, -18, 0,
        0, 0, -32, -32, 0, 0, -37, -37, -37,
        0, -32, -32, 24, 24, 45, 50, 50, 50,
        -45, -24, -24, -24, -45, -45, 0, -24, -24,
        -34, -34, 0, -24, 0, -24, -45, -45, -24,
        64, 64, 64, -32, 0, -32, 0, -32, 0,
        -32, 0, 32, -24, -45, -24, 45, 24, 45,
        24, -24, -45, -45, -24, 24, 80, 80, 80,
        64, 0, 0, 0, 0, -64, 0, -64, -64,
        -24, -24, 45, 96, 96, 96, 64, 64, 0,
        45, -24, -24, 34, -34, 0, 112, 112, 112,
        24, -45, -45, 45, 45, -24, 0, -32, 32,
        24, -24, 45, 0, 96, 96, 45, -24, 24,
        24, -45, -24, -24, -45, 24, 0, -64, 0,
        96, 0, 0, 128, 128, 128, 64, 0, 64,
        144, 144, 144, 96, 96, 0, -36, -36, 36,
        45, -24, -45, 45, -45, -24, 0, 0, -96,
        0, 128, 128, 0, 96, 0, 45, 24, -45,
        -128, 0, 0, 24, -45, 24, -45, 24, -45,
        64, 0, -64, 64, -64, -64, 96, 0, 96,
        45, -45, 24, 24, 45, -45, 64, 64, -64,
        128, 128, 0, 0, 0, -128, -24, 45, -45,
    };

    /// <summary>
    /// Replaces channels <paramref name="beginC"/> to <paramref name="endC"/> by one index channel,
    /// and inserts the palette as a meta channel at the front.
    /// </summary>
    /// <param name="input">The image.</param>
    /// <param name="beginC">The first channel.</param>
    /// <param name="endC">The last channel.</param>
    /// <param name="nbColors">The number of colours.</param>
    /// <param name="nbDeltas">The number of delta entries.</param>
    public static void MetaPalette(JxlModularImage input, uint beginC, uint endC, uint nbColors, uint nbDeltas)
    {
        CheckEqualChannels(input, beginC, endC);

        int nb = (int)(endC - beginC + 1);
        if (beginC >= input.NbMetaChannels)
        {
            // Palette was done on normal channels
            input.NbMetaChannels++;
        }
        else
        {
            // Palette was done on metachannels
            if (endC >= input.NbMetaChannels)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Palette over meta and normal channels");
            }

            // we remove nb-1 metachannels and add one
            input.NbMetaChannels += 2 - nb;
        }

        List<JxlModularChannel> channels = input.Channel;
        for (int c = (int)beginC + 1; c <= endC; c++)
        {
            channels[c].Dispose();
        }

        channels.RemoveRange((int)beginC + 1, nb - 1);

        long paletteWidth = (long)nbColors + nbDeltas;
        if (paletteWidth > int.MaxValue)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Palette too large");
        }

        JxlModularChannel pch = JxlModularChannel.Create(input.Allocator, (int)paletteWidth, nb);
        pch.HShift = -1;
        pch.VShift = -1;
        channels.Insert(0, pch);
    }

    /// <summary>Undoes a palette transform.</summary>
    /// <param name="input">The image.</param>
    /// <param name="beginC">The index channel's position before the palette is removed, minus 1.</param>
    /// <param name="nbColors">The number of colours.</param>
    /// <param name="nbDeltas">The number of delta entries.</param>
    /// <param name="predictor">The predictor of delta entries.</param>
    /// <param name="wpHeader">The weighted predictor parameters.</param>
    public static void InvPalette(JxlModularImage input, uint beginC, uint nbColors, uint nbDeltas, JxlPredictor predictor, JxlWeightedHeader wpHeader)
    {
        if (input.NbMetaChannels < 1)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Error: Palette transform without palette.");
        }

        List<JxlModularChannel> channels = input.Channel;
        int nb = channels[0].H;
        uint c0 = beginC + 1;
        if (c0 >= channels.Count)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Channel is out of range.");
        }

        int index0 = (int)c0;
        int w = channels[index0].W;
        int h = channels[index0].H;
        if (nb < 1)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Corrupted transforms");
        }

        for (int i = 1; i < nb; i++)
        {
            channels.Insert(index0 + 1, JxlModularChannel.Create(input.Allocator, w, h, channels[index0].HShift, channels[index0].VShift));
        }

        JxlModularChannel palette = channels[0];
        int paletteSize = palette.W;
        int bitDepth = Math.Min(input.BitDepth, 24);

        if (w == 0)
        {
            // Nothing to do.
            // Avoid touching "empty" channels with non-zero height.
        }
        else if (nbDeltas == 0 && predictor == JxlPredictor.Zero)
        {
            if (nb == 1)
            {
                for (int y = 0; y < h; y++)
                {
                    Span<int> p = channels[index0].Row(y);
                    for (int x = 0; x < w; x++)
                    {
                        // Not Math.Clamp: an empty palette gives hi < lo, and libjxl's Clamp1 then yields -1.
                        int index = Math.Min(Math.Max(p[x], 0), paletteSize - 1);
                        p[x] = GetPaletteValue(palette, index, 0, paletteSize, bitDepth);
                    }
                }
            }
            else
            {
                for (int y = 0; y < h; y++)
                {
                    Span<int> pIndex = channels[index0].Row(y);
                    for (int x = 0; x < w; x++)
                    {
                        // The first output channel holds the indices, so read the index before writing.
                        int index = pIndex[x];
                        for (int c = 0; c < nb; c++)
                        {
                            channels[index0 + c].Row(y)[x] = GetPaletteValue(palette, index, c, paletteSize, bitDepth);
                        }
                    }
                }
            }
        }
        else
        {
            // Parallelized per channel.
            using JxlModularChannel indices = JxlModularChannel.Create(input.Allocator, w, h);
            channels[index0].SwapPlane(indices);
            if (predictor == JxlPredictor.Weighted)
            {
                for (int c = 0; c < nb; c++)
                {
                    JxlModularChannel channel = channels[index0 + c];
                    JxlWeightedState wpState = new(wpHeader, channel.W);
                    UndoDeltaChannel(channel, indices, palette, c, paletteSize, nbDeltas, predictor, bitDepth, wpState);
                }
            }
            else
            {
                for (int c = 0; c < nb; c++)
                {
                    UndoDeltaChannel(channels[index0 + c], indices, palette, c, paletteSize, nbDeltas, predictor, bitDepth, null);
                }
            }
        }

        if (c0 >= input.NbMetaChannels)
        {
            // Palette was done on normal channels
            input.NbMetaChannels--;
        }
        else
        {
            // Palette was done on metachannels
            if (input.NbMetaChannels < 2 - nb)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Corrupted transforms");
            }

            input.NbMetaChannels -= 2 - nb;
            if (beginC + nb - 1 >= input.NbMetaChannels)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Corrupted transforms");
            }
        }

        channels[0].Dispose();
        channels.RemoveAt(0);
    }

    /// <summary>
    /// Returns channel <paramref name="c"/> of palette entry <paramref name="index"/>: a delta entry
    /// below zero, an explicit colour below <paramref name="paletteSize"/>, and an implicit colour from
    /// the colour cubes after it. Implicit colours do not read <paramref name="palette"/>, which may then be null.
    /// </summary>
    /// <param name="palette">The palette channel.</param>
    /// <param name="index">The index.</param>
    /// <param name="c">The channel.</param>
    /// <param name="paletteSize">The number of explicit colours.</param>
    /// <param name="bitDepth">The bit depth.</param>
    /// <returns>The sample.</returns>
    public static int GetPaletteValue(JxlModularChannel palette, int index, int c, int paletteSize, int bitDepth)
    {
        if (index < 0)
        {
            if (c >= RgbChannels)
            {
                return 0;
            }

            // Do not open the brackets, otherwise INT32_MIN negation could overflow.
            index = unchecked(-(index + 1));
            index %= 1 + (2 * ((DeltaPalette.Length / 3) - 1));
            int multiplier = (index & 1) == 0 ? -1 : 1;
            int result = DeltaPalette[(3 * ((index + 1) >> 1)) + c] * multiplier;
            if (bitDepth > 8)
            {
                result *= 1 << (bitDepth - 8);
            }

            return result;
        }

        if (paletteSize <= index && index < paletteSize + LargeCubeOffset)
        {
            if (c >= RgbChannels)
            {
                return 0;
            }

            index -= paletteSize;
            index >>= c * SmallCubeBits;
            return Scale4((ulong)(index % SmallCube), bitDepth) + (1 << Math.Max(0, bitDepth - 3));
        }

        if (paletteSize + LargeCubeOffset <= index)
        {
            if (c >= RgbChannels)
            {
                return 0;
            }

            index -= paletteSize + LargeCubeOffset;

            // TODO(eustas): should we take care of ambiguity created by
            //               index >= kLargeCube ** 3 ?
            switch (c)
            {
                case 1:
                    index /= LargeCube;
                    break;
                case 2:
                    index /= LargeCube * LargeCube;
                    break;
            }

            return Scale4((ulong)(index % LargeCube), bitDepth);
        }

        return palette.Row(c)[index];
    }

    private static void UndoDeltaChannel(
        JxlModularChannel channel,
        JxlModularChannel indices,
        JxlModularChannel palette,
        int c,
        int paletteSize,
        uint nbDeltas,
        JxlPredictor predictor,
        int bitDepth,
        JxlWeightedState wpState)
    {
        for (int y = 0; y < channel.H; y++)
        {
            Span<int> p = channel.Row(y);
            ReadOnlySpan<int> top = y > 0 ? channel.Row(y - 1) : Span<int>.Empty;
            ReadOnlySpan<int> topTop = y > 1 ? channel.Row(y - 2) : Span<int>.Empty;
            ReadOnlySpan<int> idx = indices.Row(y);
            for (int x = 0; x < channel.W; x++)
            {
                int index = idx[x];
                long val;
                int paletteEntry = GetPaletteValue(palette, index, c, paletteSize, bitDepth);

                // libjxl always predicts with the weighted predictor, so its state sees every sample.
                if (wpState != null)
                {
                    JxlPredictionResult pred = JxlModularPredict.PredictNoTree(channel.W, p, top, topTop, x, y, predictor, wpState);
                    val = index < (int)nbDeltas ? pred.Guess + paletteEntry : paletteEntry;
                    p[x] = unchecked((int)val);
                    wpState.UpdateErrors(p[x], x, y, channel.W);
                }
                else
                {
                    if (index < (int)nbDeltas)
                    {
                        JxlPredictionResult pred = JxlModularPredict.PredictNoTree(channel.W, p, top, topTop, x, y, predictor, null);
                        val = pred.Guess + paletteEntry;
                    }
                    else
                    {
                        val = paletteEntry;
                    }

                    p[x] = unchecked((int)val);
                }
            }
        }
    }

    // We only call this with kSmallCube or kLargeCube - 1 as the denominator, both of which are 4.
    private static int Scale4(ulong value, int bitDepth) => unchecked((int)((value * ((1UL << bitDepth) - 1)) >> 2));
}
