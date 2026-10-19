// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <content>Gaborish: its supported-feature check (D9b ports its stage).</content>
internal static partial class JxlLoopFilterStages
{
    private static void CheckGaborishSupported(JxlFrameHeader frameHeader)
    {
        if (frameHeader.LoopFilter.Gab)
        {
            JxlThrowHelper.ThrowNotSupportedException("Loop filters (Gaborish)");
        }
    }
}
