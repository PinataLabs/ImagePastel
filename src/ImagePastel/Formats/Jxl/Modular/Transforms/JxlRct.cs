// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/transform/rct.h and lib/jxl/modular/transform/rct.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using static PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms.JxlTransform;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;

/// <summary>Undoes the reversible colour transform.</summary>
internal static class JxlRct
{
    /// <summary>
    /// Undoes RCT <paramref name="rctType"/> on the three channels from <paramref name="beginC"/>.
    /// </summary>
    /// <param name="input">The image.</param>
    /// <param name="beginC">The first channel.</param>
    /// <param name="rctType">The RCT type: permutation * 7 + decorrelation.</param>
    public static void InvRct(JxlModularImage input, int beginC, int rctType)
    {
        CheckEqualChannels(input, (uint)beginC, (uint)beginC + 2);
        int m = beginC;
        List<JxlModularChannel> channels = input.Channel;
        JxlModularChannel c0 = channels[m];
        int w = c0.W;
        int h = c0.H;
        if (rctType == 0)
        {
            // noop
            return;
        }

        // Permutation: 0=RGB, 1=GBR, 2=BRG, 3=RBG, 4=GRB, 5=BGR
        int permutation = rctType / 7;
        if (permutation >= 6)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid RCT permutation");
        }

        // 0-5 values have the low bit corresponding to Third and the high bits corresponding to
        // Second. 6 corresponds to YCoCg.
        //
        // Second: 0=nop, 1=SubtractFirst, 2=SubtractAvgFirstThird
        //
        // Third: 0=nop, 1=SubtractFirst
        int custom = rctType % 7;
        int out0Index = m + (permutation % 3);
        int out1Index = m + ((permutation + 1 + (permutation / 3)) % 3);
        int out2Index = m + ((permutation + 2 - (permutation / 3)) % 3);
        if (custom == 0)
        {
            // Special case: permute-only. Swap channels around.
            JxlModularChannel ch0 = channels[m];
            JxlModularChannel ch1 = channels[m + 1];
            JxlModularChannel ch2 = channels[m + 2];
            channels[out0Index] = ch0;
            channels[out1Index] = ch1;
            channels[out2Index] = ch2;
            return;
        }

        // libjxl processes rows with Highway vectors and wrapping adds; the scalar rows give the
        // same result.
        JxlModularChannel in0 = channels[m];
        JxlModularChannel in1 = channels[m + 1];
        JxlModularChannel in2 = channels[m + 2];
        JxlModularChannel out0 = channels[out0Index];
        JxlModularChannel out1 = channels[out1Index];
        JxlModularChannel out2 = channels[out2Index];
        for (int y = 0; y < h; y++)
        {
            InvRctRow(custom, in0.Row(y), in1.Row(y), in2.Row(y), out0.Row(y), out1.Row(y), out2.Row(y), w);
        }
    }

    private static void InvRctRow(int transformType, Span<int> in0, Span<int> in1, Span<int> in2, Span<int> out0, Span<int> out1, Span<int> out2, int w)
    {
        int second = transformType >> 1;
        int third = transformType & 1;
        for (int x = 0; x < w; x++)
        {
            if (transformType == 6)
            {
                int y = in0[x];
                int co = in1[x];
                int cg = in2[x];
                int tmp = PixelAdd(y, -(cg >> 1));
                int g = PixelAdd(cg, tmp);
                int b = PixelAdd(tmp, -(co >> 1));
                int r = PixelAdd(b, co);
                out0[x] = r;
                out1[x] = g;
                out2[x] = b;
            }
            else
            {
                int first = in0[x];
                int secondValue = in1[x];
                int thirdValue = in2[x];
                if (third != 0)
                {
                    thirdValue = PixelAdd(thirdValue, first);
                }

                if (second == 1)
                {
                    secondValue = PixelAdd(secondValue, first);
                }
                else if (second == 2)
                {
                    secondValue = PixelAdd(secondValue, PixelAdd(first, thirdValue) >> 1);
                }

                out0[x] = first;
                out1[x] = secondValue;
                out2[x] = thirdValue;
            }
        }
    }
}
