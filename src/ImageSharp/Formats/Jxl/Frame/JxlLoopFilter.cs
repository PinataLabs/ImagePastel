// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/loop_filter.h and lib/jxl/loop_filter.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Fields;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>The loop-filter fields of a frame: Gaborish and the edge-preserving filter (EPF).</summary>
internal sealed class JxlLoopFilter : JxlFields
{
    private const int EpfSharpEntries = 8;

    public JxlLoopFilter() => JxlBundle.Init(this);

    /// <summary>Gets or sets a value indicating whether the Gaborish filter is on.</summary>
    public bool Gab { get; set; }

    public bool GabCustom { get; set; }

    public float GabXWeight1 { get; set; }

    public float GabXWeight2 { get; set; }

    public float GabYWeight1 { get; set; }

    public float GabYWeight2 { get; set; }

    public float GabBWeight1 { get; set; }

    public float GabBWeight2 { get; set; }

    /// <summary>Gets or sets the number of EPF iterations, 0 to 3.</summary>
    public uint EpfIters { get; set; }

    public bool EpfSharpCustom { get; set; }

    public float[] EpfSharpLut { get; } = new float[EpfSharpEntries];

    public bool EpfWeightCustom { get; set; }

    public float[] EpfChannelScale { get; } = new float[3];

    public float EpfPass1Zeroflush { get; set; }

    public float EpfPass2Zeroflush { get; set; }

    public bool EpfSigmaCustom { get; set; }

    public float EpfQuantMul { get; set; }

    public float EpfPass0SigmaScale { get; set; }

    public float EpfPass2SigmaScale { get; set; }

    public float EpfBorderSadMul { get; set; }

    public float EpfSigmaForModular { get; set; }

    public ulong Extensions { get; set; }

    /// <summary>Gets or sets a value indicating whether the frame is Modular, which is not serialized here.</summary>
    public bool NonserializedIsModular { get; set; }

    public override void VisitFields(JxlVisitor visitor)
    {
        // Must come before AllDefault.
        if (visitor.AllDefault(this))
        {
            // Overwrite all serialized fields, but not any nonserialized_*.
            visitor.SetDefault(this);
            return;
        }

        this.Gab = visitor.Bool(true, this.Gab);
        if (visitor.Conditional(this.Gab))
        {
            this.GabCustom = visitor.Bool(false, this.GabCustom);
            if (visitor.Conditional(this.GabCustom))
            {
                this.GabXWeight1 = visitor.F16((float)(1.1 * 0.104699568f), this.GabXWeight1);
                this.GabXWeight2 = visitor.F16((float)(1.1 * 0.055680538f), this.GabXWeight2);
                CheckGabWeights(this.GabXWeight1, this.GabXWeight2, "x");
                this.GabYWeight1 = visitor.F16((float)(1.1 * 0.104699568f), this.GabYWeight1);
                this.GabYWeight2 = visitor.F16((float)(1.1 * 0.055680538f), this.GabYWeight2);
                CheckGabWeights(this.GabYWeight1, this.GabYWeight2, "y");
                this.GabBWeight1 = visitor.F16((float)(1.1 * 0.104699568f), this.GabBWeight1);
                this.GabBWeight2 = visitor.F16((float)(1.1 * 0.055680538f), this.GabBWeight2);
                CheckGabWeights(this.GabBWeight1, this.GabBWeight2, "b");
            }
        }

        this.EpfIters = visitor.Bits(2, 2, this.EpfIters);
        if (visitor.Conditional(this.EpfIters > 0))
        {
            if (visitor.Conditional(!this.NonserializedIsModular))
            {
                this.EpfSharpCustom = visitor.Bool(false, this.EpfSharpCustom);
                if (visitor.Conditional(this.EpfSharpCustom))
                {
                    for (int i = 0; i < EpfSharpEntries; ++i)
                    {
                        this.EpfSharpLut[i] = visitor.F16(i / (float)(EpfSharpEntries - 1), this.EpfSharpLut[i]);
                    }
                }
            }

            this.EpfWeightCustom = visitor.Bool(false, this.EpfWeightCustom);
            if (visitor.Conditional(this.EpfWeightCustom))
            {
                this.EpfChannelScale[0] = visitor.F16(40.0f, this.EpfChannelScale[0]);
                this.EpfChannelScale[1] = visitor.F16(5.0f, this.EpfChannelScale[1]);
                this.EpfChannelScale[2] = visitor.F16(3.5f, this.EpfChannelScale[2]);
                this.EpfPass1Zeroflush = visitor.F16(0.45f, this.EpfPass1Zeroflush);
                this.EpfPass2Zeroflush = visitor.F16(0.6f, this.EpfPass2Zeroflush);
            }

            this.EpfSigmaCustom = visitor.Bool(false, this.EpfSigmaCustom);
            if (visitor.Conditional(this.EpfSigmaCustom))
            {
                if (visitor.Conditional(!this.NonserializedIsModular))
                {
                    this.EpfQuantMul = visitor.F16(0.46f, this.EpfQuantMul);
                }

                this.EpfPass0SigmaScale = visitor.F16(0.9f, this.EpfPass0SigmaScale);
                this.EpfPass2SigmaScale = visitor.F16(6.5f, this.EpfPass2SigmaScale);
                this.EpfBorderSadMul = visitor.F16(0.6666666666666666f, this.EpfBorderSadMul);
            }

            if (visitor.Conditional(this.NonserializedIsModular))
            {
                this.EpfSigmaForModular = visitor.F16(1.0f, this.EpfSigmaForModular);
                if (this.EpfSigmaForModular < 1e-8)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("EPF: sigma for modular is too small");
                }
            }
        }

        this.Extensions = visitor.BeginExtensions(this.Extensions);

        // Extensions: in chronological order of being added to the format.
        visitor.EndExtensions();
    }

    private static void CheckGabWeights(float weight1, float weight2, string channel)
    {
        if (Math.Abs(1.0f + ((weight1 + weight2) * 4)) < 1e-8)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Gaborish {channel} weights lead to near 0 unnormalized kernel");
        }
    }
}
