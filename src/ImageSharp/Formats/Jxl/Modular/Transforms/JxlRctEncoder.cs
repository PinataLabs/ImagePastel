// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/transform/enc_rct.h and lib/jxl/modular/transform/enc_rct.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using static SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms.JxlTransform;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;

/// <summary>Applies the reversible colour transform; the inverse is <see cref="JxlRct.InvRct"/>.</summary>
internal static class JxlRctEncoder
{
    /// <summary>
    /// Applies RCT <paramref name="rctType"/> in place to the three channels from <paramref name="beginC"/>.
    /// </summary>
    /// <param name="input">The image.</param>
    /// <param name="beginC">The first channel.</param>
    /// <param name="rctType">The RCT type: permutation * 7 + decorrelation.</param>
    /// <returns><see langword="false"/> for type 0, which changes nothing; otherwise <see langword="true"/>.</returns>
    public static bool FwdRct(JxlModularImage input, int beginC, int rctType)
    {
        CheckEqualChannels(input, (uint)beginC, (uint)beginC + 2);
        if (rctType == 0)
        {
            // noop
            return false;
        }

        JxlModularChannel[] chs = { input.Channel[beginC], input.Channel[beginC + 1], input.Channel[beginC + 2] };

        // Permutation: 0=RGB, 1=GBR, 2=BRG, 3=RBG, 4=GRB, 5=BGR
        int permutation = rctType / 7;

        // 0-5 values have the low bit corresponding to Third and the high bits corresponding to
        // Second. 6 corresponds to YCoCg.
        //
        // Second: 0=nop, 1=SubtractFirst, 2=SubtractAvgFirstThird
        //
        // Third: 0=nop, 1=SubtractFirst
        int transform = rctType % 7;
        JxlModularChannel[] inp = RctPermute(chs, permutation);
        int w = chs[0].W;
        int h = chs[0].H;

        // libjxl processes rows with Highway vectors; this is its scalar row, which gives the same
        // result. Each sample reads all three inputs before it writes, so the rows can be the same.
        for (int y = 0; y < h; y++)
        {
            FwdRctRow(transform, w, inp[0].Row(y), inp[1].Row(y), inp[2].Row(y), chs[0].Row(y), chs[1].Row(y), chs[2].Row(y));
        }

        return true;
    }

    private static JxlModularChannel[] RctPermute(JxlModularChannel[] input, int permutation)
        => new[]
        {
            input[permutation % 3],
            input[(permutation + 1 + (permutation / 3)) % 3],
            input[(permutation + 2 - (permutation / 3)) % 3],
        };

    // libjxl's pixel_type arithmetic wraps in its vector code; C# int arithmetic wraps too.
    private static void FwdRctRow(int transform, int w, Span<int> in0, Span<int> in1, Span<int> in2, Span<int> out0, Span<int> out1, Span<int> out2)
    {
        int second = transform >> 1;
        int third = transform & 1;
        if (transform == 6)
        {
            for (int x = 0; x < w; x++)
            {
                int r = in0[x];
                int g = in1[x];
                int b = in2[x];
                int o1 = unchecked(r - b);
                int tmp = unchecked(b + (o1 >> 1));
                int o2 = unchecked(g - tmp);
                out0[x] = unchecked(tmp + (o2 >> 1));
                out1[x] = o1;
                out2[x] = o2;
            }

            return;
        }

        for (int x = 0; x < w; x++)
        {
            int first = in0[x];
            int secondValue = in1[x];
            int thirdValue = in2[x];
            if (second == 1)
            {
                secondValue = unchecked(secondValue - first);
            }
            else if (second == 2)
            {
                secondValue = unchecked(secondValue - ((first + thirdValue) >> 1));
            }

            if (third != 0)
            {
                thirdValue = unchecked(thirdValue - first);
            }

            out0[x] = first;
            out1[x] = secondValue;
            out2[x] = thirdValue;
        }
    }
}
