// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// PassesSharedState::reference_frames in lib/jxl/passes_state.h, and the saving step of
// FrameDecoder::FinalizeFrame in lib/jxl/dec_frame.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>
/// The four reference slots a frame can be saved to (<c>save_as_reference</c>) and later frames
/// blend onto (<c>BlendingInfo.source</c>). A slot no frame has saved is empty, and libjxl blends
/// against zeros there.
/// </summary>
internal sealed class JxlReferenceFrames : IDisposable
{
    /// <summary>The number of slots: libjxl's <c>kMaxNumReferenceFrames</c>.</summary>
    public const int MaxNumReferenceFrames = 4;

    private readonly JxlReferenceFrame[] slots = new JxlReferenceFrame[MaxNumReferenceFrames];

    /// <summary>Returns the frame in a slot, or <see langword="null"/> if the slot is empty.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The frame.</returns>
    public JxlReferenceFrame this[uint slot] => this.slots[slot];

    /// <summary>Puts a frame in a slot, replacing the frame there.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="frame">The frame, which the slots now own.</param>
    public void Save(uint slot, JxlReferenceFrame frame)
    {
        this.slots[slot]?.Dispose();
        this.slots[slot] = frame;
    }

    public void Dispose()
    {
        for (int i = 0; i < this.slots.Length; i++)
        {
            this.slots[i]?.Dispose();
            this.slots[i] = null;
        }
    }
}
