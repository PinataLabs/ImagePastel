// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <content>The edge-preserving filter (EPF): its supported-feature check (D9c ports its stages).</content>
internal static partial class JxlLoopFilterStages
{
    private static void CheckEpfSupported(JxlFrameHeader frameHeader)
    {
        if (frameHeader.LoopFilter.EpfIters > 0)
        {
            JxlThrowHelper.ThrowNotSupportedException("Loop filters (EPF)");
        }
    }
}
