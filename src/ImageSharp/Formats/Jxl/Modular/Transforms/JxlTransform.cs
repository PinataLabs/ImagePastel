// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/transform/transform.h and lib/jxl/modular/transform/transform.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;

/// <summary>A Modular transform and its parameters.</summary>
internal sealed class JxlTransform : JxlFields
{
    private uint id = (uint)JxlTransformId.Invalid;
    private uint predictor;

    public JxlTransform() => JxlBundle.Init(this);

    /// <summary>Gets or sets the transform.</summary>
    public JxlTransformId Id
    {
        get => (JxlTransformId)this.id;
        set => this.id = (uint)value;
    }

    /// <summary>Gets or sets the first channel the transform applies to. RCT and palette.</summary>
    public uint BeginC { get; set; }

    /// <summary>Gets or sets the RCT type: permutation * 7 + decorrelation. RCT only.</summary>
    public uint RctType { get; set; }

    /// <summary>Gets or sets the number of channels the palette replaces. Palette only.</summary>
    public uint NumC { get; set; }

    /// <summary>Gets or sets the number of palette colours. Palette only.</summary>
    public uint NbColors { get; set; }

    /// <summary>Gets or sets the number of delta palette entries, which are added to a prediction. Palette only.</summary>
    public uint NbDeltas { get; set; }

    /// <summary>Gets or sets the predictor of delta palette entries. Palette only.</summary>
    public JxlPredictor Predictor
    {
        get => (JxlPredictor)this.predictor;
        set => this.predictor = (uint)value;
    }

    /// <summary>Gets the squeeze steps. Squeeze only; empty means the default steps.</summary>
    public List<JxlSqueezeParams> Squeezes { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the encoder sorts the palette colours by luma. Encoder
    /// only; not written.
    /// </summary>
    public bool OrderedPalette { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the encoder may make a lossy palette. Encoder only; not
    /// written. Only lossy encoding sets it, and that path is not ported.
    /// </summary>
    public bool LossyPalette { get; set; }

    public override void VisitFields(JxlVisitor visitor)
    {
        this.id = visitor.U32(Val((uint)JxlTransformId.Rct), Val((uint)JxlTransformId.Palette), Val((uint)JxlTransformId.Squeeze), Val((uint)JxlTransformId.Invalid), (uint)JxlTransformId.Rct, this.id);
        if (this.Id == JxlTransformId.Invalid)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid transform ID");
        }

        if (visitor.Conditional(this.Id is JxlTransformId.Rct or JxlTransformId.Palette))
        {
            this.BeginC = visitor.U32(Bits(3), BitsOffset(6, 8), BitsOffset(10, 72), BitsOffset(13, 1096), 0, this.BeginC);
        }

        if (visitor.Conditional(this.Id == JxlTransformId.Rct))
        {
            // 0-41, default YCoCg.
            this.RctType = visitor.U32(Val(6), Bits(2), BitsOffset(4, 2), BitsOffset(6, 10), 6, this.RctType);
            if (this.RctType >= 42)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid transform RCT type");
            }
        }

        if (visitor.Conditional(this.Id == JxlTransformId.Palette))
        {
            this.NumC = visitor.U32(Val(1), Val(3), Val(4), BitsOffset(13, 1), 3, this.NumC);
            this.NbColors = visitor.U32(BitsOffset(8, 0), BitsOffset(10, 256), BitsOffset(12, 1280), BitsOffset(16, 5376), 256, this.NbColors);
            this.NbDeltas = visitor.U32(Val(0), BitsOffset(8, 1), BitsOffset(10, 257), BitsOffset(16, 1281), 0, this.NbDeltas);
            this.predictor = visitor.Bits(4, (uint)JxlPredictor.Zero, this.predictor);
            if (this.predictor >= (uint)JxlPredictor.Best)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid predictor");
            }
        }

        if (visitor.Conditional(this.Id == JxlTransformId.Squeeze))
        {
            uint numSqueezes = (uint)this.Squeezes.Count;
            numSqueezes = visitor.U32(Val(0), BitsOffset(4, 1), BitsOffset(6, 9), BitsOffset(8, 41), 0, numSqueezes);
            if (visitor.IsReading)
            {
                this.Squeezes.Clear();
                for (uint i = 0; i < numSqueezes; i++)
                {
                    this.Squeezes.Add(new JxlSqueezeParams());
                }
            }

            for (int i = 0; i < numSqueezes; i++)
            {
                visitor.VisitNested(this.Squeezes[i]);
            }
        }
    }

    /// <summary>Undoes the transform. Throws <see cref="InvalidImageContentException"/> on failure.</summary>
    /// <param name="input">The image.</param>
    /// <param name="wpHeader">The weighted predictor parameters.</param>
    /// <param name="cancellationToken">
    /// The cancellation token, checked once per transform, or per step for a squeeze (#293).
    /// </param>
    public void Inverse(JxlModularImage input, JxlWeightedHeader wpHeader, CancellationToken cancellationToken)
    {
        this.CheckCancellation(cancellationToken);
        switch (this.Id)
        {
            case JxlTransformId.Rct:
                JxlRct.InvRct(input, (int)this.BeginC, (int)this.RctType);
                break;
            case JxlTransformId.Squeeze:
                JxlSqueeze.InvSqueeze(input, this.Squeezes, cancellationToken);
                break;
            case JxlTransformId.Palette:
                JxlPalette.InvPalette(input, this.BeginC, this.NbColors, this.NbDeltas, this.Predictor, wpHeader);
                break;
            default:
                JxlThrowHelper.ThrowInvalidImageContentException($"Unknown transformation (ID={this.id})");
                break;
        }
    }

    /// <summary>
    /// Changes the channel list the way the transform's forward step did, so channels can be
    /// decoded before the transform is undone.
    /// </summary>
    /// <param name="input">The image.</param>
    /// <param name="cancellationToken">
    /// The cancellation token, checked once per transform, or per step for a squeeze (#293).
    /// </param>
    public void MetaApply(JxlModularImage input, CancellationToken cancellationToken)
    {
        this.CheckCancellation(cancellationToken);
        switch (this.Id)
        {
            case JxlTransformId.Rct:
                CheckEqualChannels(input, this.BeginC, this.BeginC + 2);
                break;
            case JxlTransformId.Squeeze:
                JxlSqueeze.MetaSqueeze(input, this.Squeezes, cancellationToken);
                break;
            case JxlTransformId.Palette:
                JxlPalette.MetaPalette(input, this.BeginC, this.BeginC + this.NumC - 1, this.NbColors, this.NbDeltas);
                break;
            default:
                JxlThrowHelper.ThrowInvalidImageContentException($"Unknown transformation (ID={this.id})");
                break;
        }
    }

    /// <summary>
    /// Checks that channels <paramref name="c1"/> to <paramref name="c2"/> exist, are all meta or all
    /// not, and have the same size and shifts. Throws <see cref="InvalidImageContentException"/> otherwise.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="c1">The first channel.</param>
    /// <param name="c2">The last channel.</param>
    public static void CheckEqualChannels(JxlModularImage image, uint c1, uint c2)
    {
        if (c1 > image.Channel.Count || c2 >= image.Channel.Count || c2 < c1)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Invalid channel range: {c1}..{c2} (there are only {image.Channel.Count} channels)");
        }

        if (c1 < image.NbMetaChannels && c2 >= image.NbMetaChannels)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid: transforming mix of meta and nonmeta");
        }

        JxlModularChannel ch1 = image.Channel[(int)c1];
        for (int c = (int)c1 + 1; c <= c2; c++)
        {
            JxlModularChannel ch2 = image.Channel[c];
            if (ch1.W != ch2.W || ch1.H != ch2.H || ch1.HShift != ch2.HShift || ch1.VShift != ch2.VShift)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Transformed channels differ in size");
            }
        }
    }

    /// <summary>Adds two samples with wraparound.</summary>
    /// <param name="a">The first sample.</param>
    /// <param name="b">The second sample.</param>
    /// <returns>The sum.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static int PixelAdd(int a, int b) => unchecked((int)((uint)a + (uint)b));

    // The one cancellation check of a transform. A squeeze checks before each of its steps instead
    // (JxlSqueeze), so it is not checked twice in a row. libjxl has no cancellation (#293).
    private void CheckCancellation(CancellationToken cancellationToken)
    {
        if (this.Id != JxlTransformId.Squeeze)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
