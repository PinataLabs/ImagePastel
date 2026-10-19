// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/transform/enc_transform.h and lib/jxl/modular/transform/enc_transform.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;

/// <summary>Applies Modular transforms on the encoder side; <see cref="JxlTransform.Inverse"/> undoes them.</summary>
internal static class JxlTransformEncoder
{
    /// <summary>
    /// Applies <paramref name="t"/> to <paramref name="input"/>. A palette writes the number of colours
    /// and deltas, and the predictor, that it chose back into <paramref name="t"/>.
    /// </summary>
    /// <param name="t">The transform.</param>
    /// <param name="input">The image.</param>
    /// <returns>Whether the transform was applied; if not, the image is unchanged.</returns>
    public static bool TransformForward(JxlTransform t, JxlModularImage input)
    {
        switch (t.Id)
        {
            case JxlTransformId.Rct:
                return JxlRctEncoder.FwdRct(input, (int)t.BeginC, (int)t.RctType);
            case JxlTransformId.Palette:
                // The lossy palette is not ported; only lossy encoding asks for it.
                if (t.LossyPalette)
                {
                    JxlThrowHelper.ThrowNotSupportedException("Lossy palette");
                }

                uint nbColors = t.NbColors;
                uint nbDeltas = t.NbDeltas;
                JxlPredictor predictor = t.Predictor;
                bool done = JxlPaletteEncoder.FwdPalette(input, t.BeginC, t.BeginC + t.NumC - 1, ref nbColors, ref nbDeltas, t.OrderedPalette, ref predictor);
                t.NbColors = nbColors;
                t.NbDeltas = nbDeltas;
                t.Predictor = predictor;
                return done;
            case JxlTransformId.Squeeze:
                // Only responsive (progressive) encoding squeezes.
                JxlThrowHelper.ThrowNotSupportedException("Squeeze encoding");
                return false;
            default:
                JxlThrowHelper.ThrowImageFormatException($"Unknown transformation (ID={(uint)t.Id})");
                return false;
        }
    }

    /// <summary>Returns the smallest and largest sample of <paramref name="ch"/>.</summary>
    /// <param name="ch">The channel.</param>
    /// <param name="min">The smallest sample, or <see cref="int.MaxValue"/> for an empty channel.</param>
    /// <param name="max">The largest sample, or <see cref="int.MinValue"/> for an empty channel.</param>
    public static void ComputeMinMax(JxlModularChannel ch, out int min, out int max)
    {
        int realmin = int.MaxValue;
        int realmax = int.MinValue;
        for (int y = 0; y < ch.H; y++)
        {
            Span<int> p = ch.Row(y);
            for (int x = 0; x < ch.W; x++)
            {
                if (p[x] < realmin)
                {
                    realmin = p[x];
                }

                if (p[x] > realmax)
                {
                    realmax = p[x];
                }
            }
        }

        min = realmin;
        max = realmax;
    }
}
