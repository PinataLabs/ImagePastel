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
/// Blends a frame or a patch onto a background, one row at a time, as libjxl's <c>blending.cc</c>.
/// </summary>
/// <remarks>
/// Deliberate departure from libjxl: libjxl always has three colour planes. Here a grey image that is not
/// XYB may have one (<see cref="JxlImageMetadata.NumColorPlanes"/>), where no later stage mixes or keeps
/// the colour planes (<see cref="JxlRenderPipeline.Finish"/>). Blending works on each colour channel
/// alone, so the one plane gets libjxl's first channel. Extra channel <c>i</c> is plane
/// <c>numColorPlanes + i</c> rather than <c>3 + i</c>.
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
    /// Returns whether any extra channel is an alpha channel. libjxl's <c>PerformBlending</c> works this
    /// out on every call; it depends only on the image, so the stages work it out once.
    /// </summary>
    /// <param name="extraChannelInfo">The extra channels of the image.</param>
    /// <returns>Whether the image has an alpha channel.</returns>
    public static bool HasAlpha(List<JxlExtraChannelInfo> extraChannelInfo)
    {
        foreach (JxlExtraChannelInfo info in extraChannelInfo)
        {
            if (info.Type == JxlExtraChannelType.Alpha)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Blends <paramref name="xsize"/> pixels of one row of the foreground onto the background and
    /// writes the result to <paramref name="output"/>, which may be the background or the foreground:
    /// libjxl's blending stage writes over the foreground (<c>out == fg</c>) and its patches stage over
    /// the background (<c>out == bg</c>).
    /// </summary>
    /// <param name="bg">The background rows, one per channel; an empty span stands for libjxl's zeros.</param>
    /// <param name="fg">The foreground rows, one per channel.</param>
    /// <param name="output">The output rows, one per channel.</param>
    /// <param name="numColorPlanes">The number of colour planes: 1 or 3.</param>
    /// <param name="colorBlending">The blending of the colour planes.</param>
    /// <param name="ecBlending">The blending of each extra channel.</param>
    /// <param name="extraChannelInfo">The extra channels of the image.</param>
    /// <param name="hasAlpha">Whether any extra channel is an alpha channel: <see cref="HasAlpha"/>.</param>
    /// <param name="tmp">Scratch rows: at least one per channel, each at least <paramref name="xsize"/> long.</param>
    /// <param name="xsize">The number of pixels.</param>
    public static void PerformBlending(
        JxlBlendingRows bg,
        JxlBlendingRows fg,
        JxlBlendingRows output,
        int numColorPlanes,
        JxlPatchBlending colorBlending,
        ReadOnlySpan<JxlPatchBlending> ecBlending,
        List<JxlExtraChannelInfo> extraChannelInfo,
        bool hasAlpha,
        Buffer2D<float> tmp,
        int xsize)
    {
        int numEc = extraChannelInfo.Count;
        int nc = numColorPlanes;
        Span<float> Tmp(int c) => tmp.DangerousGetRowSpan(c).Slice(0, xsize);

        // Blend extra channels first so that we use the pre-blending alpha.
        for (int i = 0; i < numEc; i++)
        {
            JxlPatchBlending info = ecBlending[i];
            int alpha = (int)info.AlphaChannel;
            switch (info.Mode)
            {
                case JxlPatchBlendMode.Add:
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

                case JxlPatchBlendMode.BlendAbove:
                {
                    bool isPremultiplied = extraChannelInfo[alpha].AlphaAssociated;
                    BlendExtraChannel(bg[nc + i], bg[nc + alpha], fg[nc + i], fg[nc + alpha], Tmp(nc + i), alpha == i, isPremultiplied, info.Clamp);
                    continue;
                }

                case JxlPatchBlendMode.BlendBelow:
                {
                    bool isPremultiplied = extraChannelInfo[alpha].AlphaAssociated;
                    BlendExtraChannel(fg[nc + i], fg[nc + alpha], bg[nc + i], bg[nc + alpha], Tmp(nc + i), alpha == i, isPremultiplied, info.Clamp);
                    continue;
                }

                case JxlPatchBlendMode.AlphaWeightedAddAbove:
                    JxlAlpha.PerformAlphaWeightedAdd(bg[nc + i], fg[nc + i], fg[nc + alpha], Tmp(nc + i), info.Clamp, alpha == i);
                    continue;

                case JxlPatchBlendMode.AlphaWeightedAddBelow:
                    // libjxl also copies when both background rows are its zero buffer; the sum it
                    // would compute instead is the same, as the background is zero.
                    JxlAlpha.PerformAlphaWeightedAdd(fg[nc + i], bg[nc + i], bg[nc + alpha], Tmp(nc + i), info.Clamp, alpha == i);
                    continue;

                case JxlPatchBlendMode.Mul:
                    JxlAlpha.PerformMulBlending(bg[nc + i], fg[nc + i], Tmp(nc + i), info.Clamp);
                    continue;

                case JxlPatchBlendMode.Replace:
                    fg[nc + i].CopyTo(Tmp(nc + i));
                    continue;

                case JxlPatchBlendMode.None:
                    bg[nc + i].CopyTo(Tmp(nc + i));
                    continue;

                default:
                    ThrowUnknownMode(info.Mode);
                    break;
            }
        }

        switch (colorBlending.Mode)
        {
            case JxlPatchBlendMode.Add:
                Add(bg, fg, nc, tmp, xsize);
                break;

            case JxlPatchBlendMode.AlphaWeightedAddAbove:
                if (hasAlpha)
                {
                    AddWeighted(bg, fg, nc, colorBlending, tmp, xsize);
                }
                else
                {
                    Add(bg, fg, nc, tmp, xsize);
                }

                break;

            case JxlPatchBlendMode.AlphaWeightedAddBelow:
                if (hasAlpha)
                {
                    AddWeighted(fg, bg, nc, colorBlending, tmp, xsize);
                }
                else
                {
                    Add(bg, fg, nc, tmp, xsize);
                }

                break;

            case JxlPatchBlendMode.BlendAbove:
                if (hasAlpha)
                {
                    BlendWeighted(bg, fg, nc, colorBlending, extraChannelInfo, tmp, xsize);
                }
                else
                {
                    Copy(fg, nc, tmp, xsize);
                }

                break;

            case JxlPatchBlendMode.BlendBelow:
                if (hasAlpha)
                {
                    BlendWeighted(fg, bg, nc, colorBlending, extraChannelInfo, tmp, xsize);
                }
                else
                {
                    Copy(fg, nc, tmp, xsize);
                }

                break;

            case JxlPatchBlendMode.Mul:
                for (int p = 0; p < nc; p++)
                {
                    JxlAlpha.PerformMulBlending(bg[p], fg[p], Tmp(p), colorBlending.Clamp);
                }

                break;

            case JxlPatchBlendMode.Replace:
                Copy(fg, nc, tmp, xsize);
                break;

            case JxlPatchBlendMode.None:
                Copy(bg, nc, tmp, xsize);
                break;

            default:
                ThrowUnknownMode(colorBlending.Mode);
                break;
        }

        for (int i = 0; i < nc + numEc; i++)
        {
            Tmp(i).CopyTo(output[i]);
        }
    }

    // Unreachable: the patch dictionary and the frame header give only the eight modes. libjxl's switches
    // have no default; one here keeps a tmp row that was not written from reaching the output.
    private static void ThrowUnknownMode(JxlPatchBlendMode mode)
        => JxlThrowHelper.ThrowInvalidImageContentException($"Unknown patch blend mode {(int)mode}");

    // One extra channel blended by an alpha. libjxl's single-channel PerformAlphaBlending spots the alpha
    // channel itself by pointer equality; here the caller says so with isAlpha.
    private static void BlendExtraChannel(ReadOnlySpan<float> bottom, ReadOnlySpan<float> bottomAlpha, ReadOnlySpan<float> top, ReadOnlySpan<float> topAlpha, Span<float> output, bool isAlpha, bool isPremultiplied, bool clamp)
    {
        if (isAlpha)
        {
            JxlAlpha.PerformAlphaBlendingOfAlpha(bottomAlpha, topAlpha, output, clamp);
        }
        else
        {
            JxlAlpha.PerformAlphaBlending(bottom, bottomAlpha, top, topAlpha, output, isPremultiplied, clamp);
        }
    }

    // blend_weighted: the colour and the alpha of the layer. The alpha overwrites what the extra channel
    // loop wrote for that channel.
    private static void BlendWeighted(JxlBlendingRows bottom, JxlBlendingRows top, int nc, JxlPatchBlending colorBlending, List<JxlExtraChannelInfo> extraChannelInfo, Buffer2D<float> tmp, int xsize)
    {
        int alpha = (int)colorBlending.AlphaChannel;
        bool isPremultiplied = extraChannelInfo[alpha].AlphaAssociated;
        for (int c = 0; c < nc; c++)
        {
            JxlAlpha.PerformAlphaBlending(bottom[c], bottom[nc + alpha], top[c], top[nc + alpha], tmp.DangerousGetRowSpan(c).Slice(0, xsize), isPremultiplied, colorBlending.Clamp);
        }

        JxlAlpha.PerformAlphaBlendingOfAlpha(bottom[nc + alpha], top[nc + alpha], tmp.DangerousGetRowSpan(nc + alpha).Slice(0, xsize), colorBlending.Clamp);
    }

    // add_weighted: the colour of the top layer, weighted by its alpha, added to the bottom layer.
    private static void AddWeighted(JxlBlendingRows bottom, JxlBlendingRows top, int nc, JxlPatchBlending colorBlending, Buffer2D<float> tmp, int xsize)
    {
        int alpha = (int)colorBlending.AlphaChannel;
        for (int c = 0; c < nc; c++)
        {
            JxlAlpha.PerformAlphaWeightedAdd(bottom[c], top[c], top[nc + alpha], tmp.DangerousGetRowSpan(c).Slice(0, xsize), colorBlending.Clamp, false);
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
