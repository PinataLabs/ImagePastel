// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Memory;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// <see cref="JxlBlending.PerformBlending"/> on its own, for what no codestream reaches: the patch
/// dictionary and the frame header only give the eight blend modes.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlBlendingTests
{
    private const JxlPatchBlendMode UnknownMode = (JxlPatchBlendMode)8;

    [Fact]
    public void PerformBlending_UnknownColourMode_Throws()
        => Assert.Throws<InvalidImageContentException>(() => Blend(new JxlPatchBlending(UnknownMode, 0, false), new JxlPatchBlending(JxlPatchBlendMode.Replace, 0, false)));

    [Fact]
    public void PerformBlending_UnknownExtraChannelMode_Throws()
        => Assert.Throws<InvalidImageContentException>(() => Blend(new JxlPatchBlending(JxlPatchBlendMode.Replace, 0, false), new JxlPatchBlending(UnknownMode, 0, false)));

    // Blends one grey and alpha pixel.
    private static void Blend(JxlPatchBlending color, JxlPatchBlending extraChannel)
    {
        MemoryAllocator allocator = Configuration.Default.MemoryAllocator;
        using Buffer2D<float> bgGrey = allocator.Allocate2D<float>(1, 1);
        using Buffer2D<float> bgAlpha = allocator.Allocate2D<float>(1, 1);
        using Buffer2D<float> fgGrey = allocator.Allocate2D<float>(1, 1);
        using Buffer2D<float> fgAlpha = allocator.Allocate2D<float>(1, 1);
        using Buffer2D<float> tmp = allocator.Allocate2D<float>(1, 2);
        JxlBlendingRows bg = new(new[] { bgGrey, bgAlpha }, Memory<float>.Empty);
        JxlBlendingRows fg = new(new[] { fgGrey, fgAlpha }, Memory<float>.Empty);
        bg.PointAt(0, 0, 1);
        fg.PointAt(0, 0, 1);
        List<JxlExtraChannelInfo> extraChannels = new() { new JxlExtraChannelInfo() };

        JxlBlending.PerformBlending(bg, fg, bg, 1, color, new[] { extraChannel }, extraChannels, JxlBlending.HasAlpha(extraChannels), tmp, 1);
    }
}
