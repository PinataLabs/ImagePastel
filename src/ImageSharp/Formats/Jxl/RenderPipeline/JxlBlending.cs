// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// NeedsBlending and PerformBlending in lib/jxl/blending.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Blends a frame onto a background, one row at a time, as libjxl's <c>blending.cc</c>.
/// </summary>
/// <remarks>
/// Deliberate departures from libjxl:
/// <list type="bullet">
/// <item>libjxl always has three colour planes. Here a grey image has one (<see cref="JxlImageMetadata.NumColorPlanes"/>);
/// libjxl's three grey planes are equal, so blending one gives the same values. Extra channel
/// <c>i</c> is plane <c>numColorPlanes + i</c> rather than <c>3 + i</c>.</item>
/// <item>libjxl takes a <c>PatchBlending</c>, which patches share. The blending stage maps
/// <c>kBlend</c> to <c>kBlendAbove</c> and <c>kAlphaWeightedAdd</c> to <c>kAlphaWeightedAddAbove</c>, and
/// never makes the <c>Below</c> or <c>None</c> modes, so this takes the frame's
/// <see cref="JxlBlendingInfo"/> and reads <see cref="JxlBlendMode.Blend"/> and
/// <see cref="JxlBlendMode.AlphaWeightedAdd"/> as the <c>Above</c> modes. Patches (a later slice) add
/// the other modes.</item>
/// </list>
/// </remarks>
internal static class JxlBlending
{
    /// <summary>
    /// Returns whether a frame is blended: a regular frame that is cropped or has a blend mode other
    /// than <see cref="JxlBlendMode.Replace"/> for its colour or any extra channel.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <returns>Whether the frame needs blending.</returns>
    public static bool NeedsBlending(JxlFrameHeader frameHeader)
    {
        if (!(frameHeader.FrameType == JxlFrameType.Regular || frameHeader.FrameType == JxlFrameType.SkipProgressive))
        {
            return false;
        }

        bool replaceAll = frameHeader.BlendingInfo.Mode == JxlBlendMode.Replace;
        foreach (JxlBlendingInfo ecInfo in frameHeader.ExtraChannelBlendingInfo)
        {
            if (ecInfo.Mode != JxlBlendMode.Replace)
            {
                replaceAll = false;
            }
        }

        // Replace the full frame: nothing to do.
        return frameHeader.CustomSizeOrOrigin || !replaceAll;
    }

    /// <summary>
    /// Blends <paramref name="xsize"/> pixels of one row of the foreground onto the background and
    /// writes the result over the foreground, as libjxl's blending stage calls it (<c>out == fg</c>).
    /// </summary>
    /// <param name="bg">The background rows, one per channel; an empty span stands for libjxl's zeros.</param>
    /// <param name="fg">The foreground rows, one per channel, also the output.</param>
    /// <param name="numColorPlanes">The number of colour planes: 1 or 3.</param>
    /// <param name="colorBlending">The blending of the colour planes.</param>
    /// <param name="ecBlending">The blending of each extra channel.</param>
    /// <param name="extraChannelInfo">The extra channels of the image.</param>
    /// <param name="tmp">Scratch rows: at least one per channel, each at least <paramref name="xsize"/> long.</param>
    /// <param name="xsize">The number of pixels.</param>
    public static void PerformBlending(
        JxlBlendingRows bg,
        JxlBlendingRows fg,
        int numColorPlanes,
        JxlBlendingInfo colorBlending,
        List<JxlBlendingInfo> ecBlending,
        List<JxlExtraChannelInfo> extraChannelInfo,
        Buffer2D<float> tmp,
        int xsize)
    {
        bool hasAlpha = false;
        int numEc = extraChannelInfo.Count;
        for (int i = 0; i < numEc; i++)
        {
            if (extraChannelInfo[i].Type == JxlExtraChannelType.Alpha)
            {
                hasAlpha = true;
                break;
            }
        }

        int nc = numColorPlanes;
        Span<float> Tmp(int c) => tmp.DangerousGetRowSpan(c).Slice(0, xsize);

        // Blend extra channels first so that we use the pre-blending alpha.
        for (int i = 0; i < numEc; i++)
        {
            JxlBlendingInfo info = ecBlending[i];
            int alpha = (int)info.AlphaChannel;
            switch (info.Mode)
            {
                case JxlBlendMode.Add:
                {
                    ReadOnlySpan<float> b = bg[nc + i];
                    ReadOnlySpan<float> f = fg[nc + i];
                    Span<float> o = Tmp(nc + i);
                    for (int x = 0; x < xsize; x++)
                    {
                        o[x] = b[x] + f[x];
                    }

                    continue;
                }

                case JxlBlendMode.Blend:
                {
                    // kBlendAbove.
                    bool isPremultiplied = extraChannelInfo[alpha].AlphaAssociated;
                    if (alpha == i)
                    {
                        JxlAlpha.PerformAlphaBlendingOfAlpha(bg[nc + alpha], fg[nc + alpha], Tmp(nc + i), info.Clamp);
                    }
                    else
                    {
                        JxlAlpha.PerformAlphaBlending(bg[nc + i], bg[nc + alpha], fg[nc + i], fg[nc + alpha], Tmp(nc + i), isPremultiplied, info.Clamp);
                    }

                    continue;
                }

                case JxlBlendMode.AlphaWeightedAdd:
                    // kAlphaWeightedAddAbove.
                    JxlAlpha.PerformAlphaWeightedAdd(bg[nc + i], fg[nc + i], fg[nc + alpha], Tmp(nc + i), info.Clamp, alpha == i);
                    continue;

                case JxlBlendMode.Mul:
                    JxlAlpha.PerformMulBlending(bg[nc + i], fg[nc + i], Tmp(nc + i), info.Clamp);
                    continue;

                default:
                    // kReplace.
                    fg[nc + i].CopyTo(Tmp(nc + i));
                    continue;
            }
        }

        int colorAlpha = (int)colorBlending.AlphaChannel;
        switch (colorBlending.Mode)
        {
            case JxlBlendMode.Add:
                Add(bg, fg, nc, tmp, xsize);
                break;

            case JxlBlendMode.AlphaWeightedAdd:
                // kAlphaWeightedAddAbove.
                if (hasAlpha)
                {
                    for (int c = 0; c < nc; c++)
                    {
                        JxlAlpha.PerformAlphaWeightedAdd(bg[c], fg[c], fg[nc + colorAlpha], Tmp(c), colorBlending.Clamp, false);
                    }
                }
                else
                {
                    Add(bg, fg, nc, tmp, xsize);
                }

                break;

            case JxlBlendMode.Blend:
                // kBlendAbove.
                if (hasAlpha)
                {
                    // blend_weighted: the colour and the alpha of the layer. The alpha overwrites what
                    // the extra channel loop wrote for that channel.
                    bool isPremultiplied = extraChannelInfo[colorAlpha].AlphaAssociated;
                    for (int c = 0; c < nc; c++)
                    {
                        JxlAlpha.PerformAlphaBlending(bg[c], bg[nc + colorAlpha], fg[c], fg[nc + colorAlpha], Tmp(c), isPremultiplied, colorBlending.Clamp);
                    }

                    JxlAlpha.PerformAlphaBlendingOfAlpha(bg[nc + colorAlpha], fg[nc + colorAlpha], Tmp(nc + colorAlpha), colorBlending.Clamp);
                }
                else
                {
                    Copy(fg, nc, tmp, xsize);
                }

                break;

            case JxlBlendMode.Mul:
                for (int p = 0; p < nc; p++)
                {
                    JxlAlpha.PerformMulBlending(bg[p], fg[p], Tmp(p), colorBlending.Clamp);
                }

                break;

            default:
                // kReplace.
                Copy(fg, nc, tmp, xsize);
                break;
        }

        for (int i = 0; i < nc + numEc; i++)
        {
            Tmp(i).CopyTo(fg[i]);
        }
    }

    private static void Add(JxlBlendingRows bg, JxlBlendingRows fg, int nc, Buffer2D<float> tmp, int xsize)
    {
        for (int p = 0; p < nc; p++)
        {
            ReadOnlySpan<float> b = bg[p];
            ReadOnlySpan<float> f = fg[p];
            Span<float> o = tmp.DangerousGetRowSpan(p).Slice(0, xsize);
            for (int x = 0; x < xsize; x++)
            {
                o[x] = b[x] + f[x];
            }
        }
    }

    private static void Copy(JxlBlendingRows src, int nc, Buffer2D<float> tmp, int xsize)
    {
        for (int p = 0; p < nc; p++)
        {
            src[p].CopyTo(tmp.DangerousGetRowSpan(p).Slice(0, xsize));
        }
    }
}
