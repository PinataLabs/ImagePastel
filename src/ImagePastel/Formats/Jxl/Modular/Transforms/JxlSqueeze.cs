// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/transform/squeeze.h and lib/jxl/modular/transform/squeeze.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Threading;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;

/// <summary>
/// The squeeze transform: a Haar-like wavelet that halves a channel in one direction into an
/// average channel and a residual channel. Repeated steps, alternating directions, downscale the image.
/// </summary>
/// <remarks>
/// Only libjxl's scalar unsqueeze is ported. Its SIMD path (<c>FastUnsqueeze</c>) computes the same
/// values for every valid image.
/// </remarks>
internal static class JxlSqueeze
{
    /// <summary>
    /// The most channels a Modular image can have after its squeezes; see <see cref="MetaSqueeze"/>. No
    /// file libjxl writes comes near it: its encoder squeezes with the default steps only, which add
    /// one residual per channel and step. A frame under the 2^28 pixel cap takes at most 25 steps (a
    /// 2^28 x 1 frame squeezed down to 8 wide), plus two chroma steps, so its 259 channels (3 colour and
    /// 256 extra) become at most about 6,700. The cap is kept that low because each step inserts its
    /// residuals in O(channels): filling 2^14 channels one at a time costs about 1.3e8 moves, where 2^16
    /// would cost 2.1e9, over a second.
    /// </summary>
    public const int MaxChannels = 1 << 14;

    // libjxl's kMaxFirstPreviewSize: the default squeeze stops once both sides are at most this.
    private const int MaxFirstPreviewSize = 8;

    // A B   -> (A+B)>>1     in one channel (average)
    //          A-B - tendency in a new channel (the residual that makes the transform reversible)
    //
    //         |A B|C D|E F|
    //           p   a   n             p=avg(A,B), a=avg(C,D), n=avg(E,F)
    //
    // The tendency estimates C-D from B, a and n: (4*B - 3*n - a) / 12, but only where B, a and n
    // are monotonic, and clamped so that C and D stay between B and n. That avoids ringing.

    /// <summary>Estimates the difference of the two samples behind average <paramref name="a"/>.</summary>
    /// <param name="b">The sample before them.</param>
    /// <param name="a">Their average.</param>
    /// <param name="n">The next average.</param>
    /// <returns>The tendency.</returns>
    public static long SmoothTendency(long b, long a, long n)
    {
        long diff = 0;
        if (b >= a && a >= n)
        {
            diff = ((4 * b) - (3 * n) - a + 6) / 12;

            // 2C = a<<1 + diff - diff&1 <= 2B  so diff - diff&1 <= 2B - 2a
            // 2D = a<<1 - diff - diff&1 >= 2n  so diff + diff&1 <= 2a - 2n
            if (diff - (diff & 1) > 2 * (b - a))
            {
                diff = (2 * (b - a)) + 1;
            }

            if (diff + (diff & 1) > 2 * (a - n))
            {
                diff = 2 * (a - n);
            }
        }
        else if (b <= a && a <= n)
        {
            diff = ((4 * b) - (3 * n) - a - 6) / 12;

            // 2C = a<<1 + diff + diff&1 >= 2B  so diff + diff&1 >= 2B - 2a
            // 2D = a<<1 - diff + diff&1 <= 2n  so diff - diff&1 >= 2a - 2n
            if (diff + (diff & 1) < 2 * (b - a))
            {
                diff = (2 * (b - a)) - 1;
            }

            if (diff - (diff & 1) < 2 * (a - n))
            {
                diff = 2 * (a - n);
            }
        }

        return diff;
    }

    /// <summary>Fills <paramref name="parameters"/> with the squeeze steps used when a transform signals none.</summary>
    /// <param name="parameters">The list to fill.</param>
    /// <param name="image">The image.</param>
    public static void DefaultSqueezeParameters(List<JxlSqueezeParams> parameters, JxlModularImage image)
    {
        int nbChannels = image.Channel.Count - image.NbMetaChannels;

        // libjxl reads channel[nb_meta_channels] without a check; an image of only meta channels
        // would read past the end, so it is rejected here.
        if (nbChannels <= 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Squeezing an image with no channels");
        }

        parameters.Clear();
        long w = image.Channel[image.NbMetaChannels].W;
        long h = image.Channel[image.NbMetaChannels].H;

        // do horizontal first on wide images; vertical first on tall images
        bool wide = w > h;

        if (nbChannels > 2 && image.Channel[image.NbMetaChannels + 1].W == w && image.Channel[image.NbMetaChannels + 1].H == h)
        {
            // assume channels 1 and 2 are chroma, and can be squeezed first for 4:2:0 previews
            uint chroma = (uint)image.NbMetaChannels + 1;
            parameters.Add(Create(horizontal: true, inPlace: false, chroma, 2));
            parameters.Add(Create(horizontal: false, inPlace: false, chroma, 2));
        }

        uint beginC = (uint)image.NbMetaChannels;
        uint numC = (uint)nbChannels;
        if (!wide)
        {
            if (h > MaxFirstPreviewSize)
            {
                parameters.Add(Create(horizontal: false, inPlace: true, beginC, numC));
                h = (h + 1) / 2;
            }
        }

        while (w > MaxFirstPreviewSize || h > MaxFirstPreviewSize)
        {
            if (w > MaxFirstPreviewSize)
            {
                parameters.Add(Create(horizontal: true, inPlace: true, beginC, numC));
                w = (w + 1) / 2;
            }

            if (h > MaxFirstPreviewSize)
            {
                parameters.Add(Create(horizontal: false, inPlace: true, beginC, numC));
                h = (h + 1) / 2;
            }
        }
    }

    /// <summary>Checks that a step's channels exist. Throws <see cref="InvalidImageContentException"/> otherwise.</summary>
    /// <param name="parameter">The step.</param>
    /// <param name="numChannels">The number of channels in the image.</param>
    public static void CheckMetaSqueezeParams(JxlSqueezeParams parameter, int numChannels)
    {
        long c1 = parameter.BeginC;
        long c2 = (long)parameter.BeginC + parameter.NumC - 1;
        if (c1 < 0 || c1 >= numChannels || c2 < 0 || c2 >= numChannels || c2 < c1)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid channel range");
        }
    }

    /// <summary>
    /// Changes the channel list the way the squeeze steps did: each squeezed channel is halved and
    /// gets a residual channel. Fills in the default steps if the transform signals none.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="parameters">The steps.</param>
    /// <param name="cancellationToken">The cancellation token, checked per step. libjxl has no cancellation (#293).</param>
    public static void MetaSqueeze(JxlModularImage image, List<JxlSqueezeParams> parameters, CancellationToken cancellationToken)
    {
        if (parameters.Count == 0)
        {
            DefaultSqueezeParameters(parameters, image);
        }

        foreach (JxlSqueezeParams parameter in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckMetaSqueezeParams(parameter, image.Channel.Count);

            // Departure (#161): libjxl at ef67fde2 has no bound on the number of channels. Meta channels
            // keep a shift of -1, so the shift > 30 check below never stops repeated squeezes of them,
            // and a stream of tens of kilobytes can make about 1.5 million channels, each inserted in
            // O(n). Squeezes are the only transform that adds more than one channel per transform.
            if (image.Channel.Count + (long)parameter.NumC > MaxChannels)
            {
                JxlThrowHelper.ThrowInvalidImageContentException($"Too many Modular channels: over {MaxChannels}");
            }

            bool horizontal = parameter.Horizontal;
            bool inPlace = parameter.InPlace;
            int beginC = (int)parameter.BeginC;
            int endC = (int)(parameter.BeginC + parameter.NumC - 1);

            if (beginC < image.NbMetaChannels)
            {
                if (endC >= image.NbMetaChannels)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid squeeze: mix of meta and nonmeta channels");
                }

                if (!inPlace)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid squeeze: meta channels require in-place residuals");
                }

                image.NbMetaChannels += (int)parameter.NumC;
            }

            int offset = inPlace ? endC + 1 : image.Channel.Count;
            for (int c = beginC; c <= endC; c++)
            {
                JxlModularChannel channel = image.Channel[c];
                if (channel.HShift > 30 || channel.VShift > 30)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Too many squeezes: shift > 30");
                }

                int w = channel.W;
                int h = channel.H;
                if (w == 0 || h == 0)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Squeezing empty channel");
                }

                if (horizontal)
                {
                    channel.W = (w + 1) / 2;
                    if (channel.HShift >= 0)
                    {
                        channel.HShift++;
                    }

                    w -= (w + 1) / 2;
                }
                else
                {
                    channel.H = (h + 1) / 2;
                    if (channel.VShift >= 0)
                    {
                        channel.VShift++;
                    }

                    h -= (h + 1) / 2;
                }

                channel.Shrink();
                JxlModularChannel placeholder = JxlModularChannel.Create(image.Allocator, w, h, channel.HShift, channel.VShift);
                placeholder.Component = channel.Component;
                image.Channel.Insert(offset + (c - beginC), placeholder);
            }
        }
    }

    /// <summary>Undoes the squeeze steps, last step first. Throws <see cref="InvalidImageContentException"/> on failure.</summary>
    /// <param name="input">The image.</param>
    /// <param name="parameters">The steps, as <see cref="MetaSqueeze"/> left them.</param>
    /// <param name="cancellationToken">The cancellation token, checked per step. libjxl has no cancellation (#293).</param>
    public static void InvSqueeze(JxlModularImage input, List<JxlSqueezeParams> parameters, CancellationToken cancellationToken)
    {
        List<JxlModularChannel> channels = input.Channel;
        for (int i = parameters.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckMetaSqueezeParams(parameters[i], channels.Count);
            bool horizontal = parameters[i].Horizontal;
            bool inPlace = parameters[i].InPlace;
            int beginC = (int)parameters[i].BeginC;
            int endC = (int)(parameters[i].BeginC + parameters[i].NumC - 1);
            int offset = inPlace ? endC + 1 : channels.Count + beginC - endC - 1;
            if (beginC < input.NbMetaChannels)
            {
                // This is checked in MetaSqueeze.
                if (input.NbMetaChannels <= parameters[i].NumC)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Squeezed meta channels are missing");
                }

                input.NbMetaChannels -= (int)parameters[i].NumC;
            }

            for (int c = beginC; c <= endC; c++)
            {
                int rc = offset + c - beginC;

                // MetaApply should imply that rc is within range; libjxl ensures it.
                if (rc >= channels.Count)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Squeeze residual channel is missing");
                }

                if (channels[c].W < channels[rc].W || channels[c].H < channels[rc].H)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Corrupted squeeze transform");
                }

                if (horizontal)
                {
                    InvHSqueeze(input, c, rc);
                }
                else
                {
                    InvVSqueeze(input, c, rc);
                }
            }

            int count = endC - beginC + 1;
            for (int c = offset; c < offset + count; c++)
            {
                channels[c].Dispose();
            }

            channels.RemoveRange(offset, count);
        }
    }

    private static void InvHSqueeze(JxlModularImage input, int c, int rc)
    {
        JxlModularChannel chin = input.Channel[c];
        JxlModularChannel chinResidual = input.Channel[rc];

        // These must be valid since MetaApply ran already; libjxl ensures them.
        if (chin.W != (chin.W + chinResidual.W + 1) / 2 || chin.H != chinResidual.H)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Squeeze channels do not match");
        }

        if (chinResidual.W == 0)
        {
            // Short-circuit: output channel has same dimensions as input.
            chin.HShift--;
            return;
        }

        // Note: chin.W >= chinResidual.W and at most 1 different.
        JxlModularChannel chout = JxlModularChannel.Create(input.Allocator, chin.W + chinResidual.W, chin.H, chin.HShift - 1, chin.VShift);

        // A residual with no rows short-circuits: the output channel has no pixels either.
        if (chinResidual.H != 0)
        {
            for (int y = 0; y < chin.H; y++)
            {
                ReadOnlySpan<int> pResidual = chinResidual.Row(y);
                ReadOnlySpan<int> pAvg = chin.Row(y);
                Span<int> pOut = chout.Row(y);
                for (int x = 0; x < chinResidual.W; x++)
                {
                    long diffMinusTendency = pResidual[x];
                    long avg = pAvg[x];
                    long nextAvg = x + 1 < chin.W ? pAvg[x + 1] : avg;
                    long left = x != 0 ? pOut[(x << 1) - 1] : avg;
                    long tendency = SmoothTendency(left, avg, nextAvg);
                    long diff = diffMinusTendency + tendency;
                    long a = avg + (diff / 2);
                    pOut[x << 1] = (int)a;
                    long b = a - diff;
                    pOut[(x << 1) + 1] = (int)b;
                }

                if ((chout.W & 1) != 0)
                {
                    pOut[chout.W - 1] = pAvg[chin.W - 1];
                }
            }
        }

        chin.Dispose();
        input.Channel[c] = chout;
    }

    private static void InvVSqueeze(JxlModularImage input, int c, int rc)
    {
        JxlModularChannel chin = input.Channel[c];
        JxlModularChannel chinResidual = input.Channel[rc];

        // These must be valid since MetaApply ran already; libjxl ensures them.
        if (chin.H != (chin.H + chinResidual.H + 1) / 2 || chin.W != chinResidual.W)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Squeeze channels do not match");
        }

        if (chinResidual.H == 0)
        {
            // Short-circuit: output channel has same dimensions as input.
            chin.VShift--;
            return;
        }

        // Note: chin.H >= chinResidual.H and at most 1 different.
        JxlModularChannel chout = JxlModularChannel.Create(input.Allocator, chin.W, chin.H + chinResidual.H, chin.HShift, chin.VShift - 1);

        // A residual with no columns short-circuits: the output channel has no pixels either.
        if (chinResidual.W != 0)
        {
            // Only up to chinResidual.H rows of averages have a residual.
            for (int y = 0; y < chinResidual.H; y++)
            {
                ReadOnlySpan<int> pResidual = chinResidual.Row(y);
                ReadOnlySpan<int> pAvg = chin.Row(y);
                ReadOnlySpan<int> pNavg = chin.Row(y + 1 < chin.H ? y + 1 : y);
                Span<int> pOut = chout.Row(y << 1);
                Span<int> pNout = chout.Row((y << 1) + 1);
                ReadOnlySpan<int> pPout = y > 0 ? chout.Row((y << 1) - 1) : pAvg;
                for (int x = 0; x < chin.W; x++)
                {
                    long avg = pAvg[x];
                    long nextAvg = pNavg[x];
                    long top = pPout[x];
                    long tendency = SmoothTendency(top, avg, nextAvg);
                    long diffMinusTendency = pResidual[x];
                    long diff = diffMinusTendency + tendency;
                    long output = avg + (diff / 2);
                    pOut[x] = (int)output;

                    // If chinResidual.H == chin.H, the output has an even number of rows, so the next
                    // row exists. Otherwise this loop does not reach the last output row, which is
                    // filled below.
                    pNout[x] = (int)(output - diff);
                }
            }

            if ((chout.H & 1) != 0)
            {
                chin.Row(chin.H - 1).CopyTo(chout.Row(chout.H - 1));
            }
        }

        chin.Dispose();
        input.Channel[c] = chout;
    }

    private static JxlSqueezeParams Create(bool horizontal, bool inPlace, uint beginC, uint numC)
        => new() { Horizontal = horizontal, InPlace = inPlace, BeginC = beginC, NumC = numC };
}
