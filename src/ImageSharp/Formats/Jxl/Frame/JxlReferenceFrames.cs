// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// PassesSharedState::reference_frames and dc_frames in lib/jxl/passes_state.h, and the saving step of
// FrameDecoder::FinalizeFrame in lib/jxl/dec_frame.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>
/// The four reference slots a frame can be saved to (<c>save_as_reference</c>) and later frames
/// blend onto (<c>BlendingInfo.source</c>). A slot no frame has saved is empty, and libjxl blends
/// against zeros there. Beside them are the four DC slots (<c>dc_frames</c>): a DC frame at level
/// <c>n</c> writes its XYB planes into slot <c>n - 1</c>, and a frame that uses a DC frame takes its DC
/// from the slot of its own DC level.
/// </summary>
internal sealed class JxlReferenceFrames : IDisposable
{
    /// <summary>The number of slots: libjxl's <c>kMaxNumReferenceFrames</c>.</summary>
    public const int MaxNumReferenceFrames = 4;

    /// <summary>The number of DC slots: the size of libjxl's <c>dc_frames</c>.</summary>
    public const int NumDcSlots = 4;

    private readonly JxlReferenceFrame[] slots = new JxlReferenceFrame[MaxNumReferenceFrames];
    private readonly JxlReferenceFrame[] dcSlots = new JxlReferenceFrame[NumDcSlots];

    /// <summary>Returns the frame in a slot, or <see langword="null"/> if the slot is empty.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The frame.</returns>
    public JxlReferenceFrame this[uint slot] => this.slots[slot];

    /// <summary>Puts a frame in a slot, replacing the frame there.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="frame">The frame, which the slots now own.</param>
    public void Save(uint slot, JxlReferenceFrame frame) => Replace(this.slots, slot, frame);

    /// <summary>Returns the planes in a DC slot, or <see langword="null"/> if no DC frame wrote it.</summary>
    /// <param name="slot">The DC slot, 0 to 3.</param>
    /// <returns>The X, Y and B planes of the DC frame.</returns>
    public JxlReferenceFrame GetDc(uint slot) => this.dcSlots[slot];

    /// <summary>Puts the planes of a DC frame in a DC slot, replacing those there.</summary>
    /// <param name="slot">The DC slot, 0 to 3.</param>
    /// <param name="frame">The X, Y and B planes, which the slots now own.</param>
    public void SaveDc(uint slot, JxlReferenceFrame frame) => Replace(this.dcSlots, slot, frame);

    public void Dispose()
    {
        Clear(this.slots);
        Clear(this.dcSlots);
    }

    // Disposes the frame in a slot of either kind, and puts another, or null, in its place.
    private static void Replace(JxlReferenceFrame[] slots, uint slot, JxlReferenceFrame frame)
    {
        slots[slot]?.Dispose();
        slots[slot] = frame;
    }

    // Empties every slot of one kind.
    private static void Clear(JxlReferenceFrame[] slots)
    {
        for (uint i = 0; i < slots.Length; i++)
        {
            Replace(slots, i, null);
        }
    }
}
