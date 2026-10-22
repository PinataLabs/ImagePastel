// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/color_encoding_internal.h and lib/jxl/color_encoding_internal.cc (Customxy,
// CustomTransferFunction, ColorEncoding fields), with the storage defaults, gamma limits and the
// IsUnknown, IsSRGB, IsLinear, IsPQ, IsHLG, Is709 and IsDCI predicates and CustomTransferFunction::SetGamma
// of lib/jxl/cms/color_encoding_cms.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <summary>A transfer function: an enum value or a gamma exponent.</summary>
internal sealed class JxlCustomTransferFunction : JxlFields
{
    /// <summary>The largest allowed 1 / gamma.</summary>
    public const uint MaxGamma = 8192;

    /// <summary>The fixed-point scale of <see cref="Gamma"/>.</summary>
    public const uint GammaMul = 10000000;

    public JxlCustomTransferFunction() => JxlBundle.Init(this);

    /// <summary>Gets or sets the colour space of the encoding, which is not serialized here.</summary>
    public JxlColorSpace NonserializedColorSpace { get; set; } = JxlColorSpace.Rgb;

    /// <summary>Gets or sets a value indicating whether the transfer function is a gamma value.</summary>
    public bool HaveGamma { get; set; }

    /// <summary>Gets or sets the gamma value times <see cref="GammaMul"/>, in (0, 1]. Only valid if <see cref="HaveGamma"/>.</summary>
    public uint Gamma { get; set; }

    /// <summary>Gets or sets the transfer function. Only valid if not <see cref="HaveGamma"/>.</summary>
    public JxlTransferFunction TransferFunction { get; set; } = JxlTransferFunction.Srgb;

    /// <summary>Gets a value indicating whether the transfer function is unknown.</summary>
    public bool IsUnknown => this.Is(JxlTransferFunction.Unknown);

    /// <summary>Gets a value indicating whether the transfer function is sRGB's, libjxl's <c>IsSRGB</c>.</summary>
    public bool IsSrgb => this.Is(JxlTransferFunction.Srgb);

    /// <summary>Gets a value indicating whether the transfer function is linear, libjxl's <c>IsLinear</c>.</summary>
    public bool IsLinear => this.Is(JxlTransferFunction.Linear);

    /// <summary>Gets a value indicating whether the transfer function is PQ, libjxl's <c>IsPQ</c>.</summary>
    public bool IsPq => this.Is(JxlTransferFunction.Pq);

    /// <summary>Gets a value indicating whether the transfer function is HLG, libjxl's <c>IsHLG</c>.</summary>
    public bool IsHlg => this.Is(JxlTransferFunction.Hlg);

    /// <summary>Gets a value indicating whether the transfer function is BT.709's, libjxl's <c>Is709</c>.</summary>
    public bool IsBt709 => this.Is(JxlTransferFunction.Bt709);

    /// <summary>Gets a value indicating whether the transfer function is DCI's 2.6 gamma, libjxl's <c>IsDCI</c>.</summary>
    public bool IsDci => this.Is(JxlTransferFunction.Dci);

    /// <summary>Copies the fields of <paramref name="other"/> into this instance.</summary>
    /// <param name="other">The transfer function to copy.</param>
    public void CopyFrom(JxlCustomTransferFunction other)
    {
        this.NonserializedColorSpace = other.NonserializedColorSpace;
        this.HaveGamma = other.HaveGamma;
        this.Gamma = other.Gamma;
        this.TransferFunction = other.TransferFunction;
    }

    /// <summary>
    /// Sets the transfer function to the gamma <paramref name="newGamma"/>, as libjxl's
    /// <c>CustomTransferFunction::SetGamma</c>: within 1e-3 of 1 it is the linear transfer function and of
    /// 1 / 2.6 the DCI one; otherwise the gamma is kept, times 10^7.
    /// </summary>
    /// <param name="newGamma">The encoding exponent, the reciprocal of the display gamma.</param>
    /// <returns><see langword="false"/> if the gamma is out of range (below 1 / 8192 or above 1).</returns>
    public bool SetGamma(double newGamma)
    {
        if (newGamma < (1.0 / MaxGamma) || newGamma > 1.0)
        {
            return false;
        }

        this.HaveGamma = false;
        if (Math.Abs(newGamma - 1.0) <= 1e-3)
        {
            this.TransferFunction = JxlTransferFunction.Linear;
            return true;
        }

        if (Math.Abs(newGamma - (1.0 / 2.6)) <= 1e-3)
        {
            this.TransferFunction = JxlTransferFunction.Dci;
            return true;
        }

        // Don't translate 0.45.. to kSRGB nor k709 - that might change pixel values because those curves
        // also have a linear part.
        this.HaveGamma = true;
        this.Gamma = (uint)MathF.Round((float)(newGamma * GammaMul), MidpointRounding.AwayFromZero);
        this.TransferFunction = JxlTransferFunction.Unknown;
        return true;
    }

    public override void VisitFields(JxlVisitor visitor)
    {
        if (visitor.Conditional(!this.SetImplicit()))
        {
            this.HaveGamma = visitor.Bool(false, this.HaveGamma);

            if (visitor.Conditional(this.HaveGamma))
            {
                this.Gamma = visitor.Bits(24, GammaMul, this.Gamma);
                if (this.Gamma > GammaMul || (ulong)this.Gamma * MaxGamma < GammaMul)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"Invalid gamma {this.Gamma}");
                }
            }

            if (visitor.Conditional(!this.HaveGamma))
            {
                this.TransferFunction = visitor.Enum(JxlTransferFunction.Srgb, this.TransferFunction);
            }
        }
    }

    // libjxl's IsX(): no gamma value, and the transfer function X.
    private bool Is(JxlTransferFunction transferFunction) => !this.HaveGamma && this.TransferFunction == transferFunction;

    // XYB implies a gamma of 1/3 (cms::CustomTransferFunction::SetGamma).
    private bool SetImplicit()
    {
        if (this.NonserializedColorSpace == JxlColorSpace.Xyb)
        {
            this.HaveGamma = true;
            this.Gamma = (uint)MathF.Round((float)(1.0 / 3 * GammaMul));
            this.TransferFunction = JxlTransferFunction.Unknown;
            return true;
        }

        return false;
    }
}
