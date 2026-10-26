// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// BitsToFloat, RandomImage, Random3Planes and PrepareNoiseInput in lib/jxl/dec_noise.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// Fills the three noise planes of the render pipeline with random floats in <c>[1, 2)</c>, group by
/// group, from a generator seeded by the frame counters and the position of each tile.
/// </summary>
/// <remarks>
/// libjxl converts the random words with Highway vectors. Each pixel takes the same word at any vector
/// width: a row takes one batch of 16 floats for every 16 pixels, and its last, partial batch from the
/// start of a new batch. So this port converts them one at a time.
/// </remarks>
internal static class JxlNoise
{
    // Xorshift128Plus::N * sizeof(uint64_t) / sizeof(float).
    private const int FloatsPerBatch = JxlXorshift128Plus.N * sizeof(ulong) / sizeof(float);

    /// <summary>
    /// Converts 32 random bits to a float in <c>[1, 2)</c>: 1.0 + 23 random mantissa bits.
    /// NOTE: as the convolution kernel sums to 0, it doesn't matter if inputs are in [0, 1) or in [1, 2).
    /// </summary>
    /// <param name="randomBits">The random bits.</param>
    /// <returns>The float.</returns>
    public static float BitsToFloat(uint randomBits) => BitConverter.Int32BitsToSingle((int)((randomBits >> 9) | 0x3F800000u));

    /// <summary>
    /// Fills the noise planes of group <paramref name="groupIndex"/>. When the colour is upsampled, the
    /// group covers <c>upsampling²</c> tiles of the group size at the upsampled size, and each tile has a
    /// generator of its own.
    /// </summary>
    /// <param name="pipeline">The pipeline, with its noise planes.</param>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="upsampling">The frame's upsampling.</param>
    /// <param name="groupIndex">The group.</param>
    /// <param name="visibleFrameIndex">libjxl's <c>visible_frame_index</c>.</param>
    /// <param name="nonvisibleFrameIndex">libjxl's <c>nonvisible_frame_index</c>.</param>
    public static void PrepareNoiseInput(JxlRenderPipeline pipeline, JxlFrameDimensions frameDim, uint upsampling, long groupIndex, long visibleFrameIndex, long nonvisibleFrameIndex)
    {
        long groupDim = frameDim.GroupDimension;
        long gx = groupIndex % frameDim.XSizeGroups;
        long gy = groupIndex / frameDim.XSizeGroups;
        int noiseCStart = pipeline.NoisePlaneStart;
        Span<JxlRect> groupRects = stackalloc JxlRect[3];
        for (int c = 0; c < 3; c++)
        {
            groupRects[c] = pipeline.GetGroupRect(groupIndex, noiseCStart + c);
        }

        // When the color channels are downsampled, we need to generate more noise
        // input for the current group than just the group dimensions.
        Span<JxlRect> rects = stackalloc JxlRect[3];
        ulong[] batch = new ulong[JxlXorshift128Plus.N];
        for (long iy = 0; iy < upsampling; iy++)
        {
            for (long ix = 0; ix < upsampling; ix++)
            {
                for (int c = 0; c < 3; c++)
                {
                    JxlRect r = groupRects[c];
                    long x1 = r.X0 + r.XSize;
                    long y1 = r.Y0 + r.YSize;
                    rects[c] = new JxlRect(r.X0 + (ix * groupDim), r.Y0 + (iy * groupDim), groupDim, groupDim, x1, y1);
                }

                // libjxl passes the counters and the position as size_t to the generator's uint32_t seeds.
                JxlXorshift128Plus rng = new(
                    unchecked((uint)visibleFrameIndex),
                    unchecked((uint)nonvisibleFrameIndex),
                    unchecked((uint)(((gx * upsampling) + ix) * groupDim)),
                    unchecked((uint)(((gy * upsampling) + iy) * groupDim)));
                for (int c = 0; c < 3; c++)
                {
                    RandomImage(rng, rects[c], pipeline.GetPlane(noiseCStart + c), batch);
                }
            }
        }
    }

    private static void RandomImage(JxlXorshift128Plus rng, JxlRect rect, Buffer2D<float> noise, ulong[] batch64)
    {
        int xsize = (int)rect.XSize;
        int ysize = (int)rect.YSize;
        for (int y = 0; y < ysize; y++)
        {
            // A rect clipped to no columns still has rows, and each row still takes a batch.
            Span<float> row = xsize > 0 ? noise.DangerousGetRowSpan((int)(rect.Y0 + y)).Slice((int)rect.X0, xsize) : Span<float>.Empty;

            int x = 0;

            // Only entire batches (avoids exceeding the image padding).
            for (; x + FloatsPerBatch < xsize; x += FloatsPerBatch)
            {
                rng.Fill(batch64);
                for (int i = 0; i < FloatsPerBatch; i++)
                {
                    row[x + i] = BitsToFloat(Word(batch64, i));
                }
            }

            // Any remaining pixels, from the start of one more batch.
            rng.Fill(batch64);
            for (int batchPos = 0; x < xsize; x++, batchPos++)
            {
                row[x] = BitsToFloat(Word(batch64, batchPos));
            }
        }
    }

    // libjxl copies the 64-bit words into 32-bit words on a little-endian machine: the low half first.
    private static uint Word(ulong[] batch64, int i) => (uint)(batch64[i >> 1] >> ((i & 1) * 32));
}
