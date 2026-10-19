// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/color_encoding_internal.h and lib/jxl/color_encoding_internal.cc (Customxy,
// CustomTransferFunction, ColorEncoding fields), with the storage defaults and gamma limits of
// lib/jxl/cms/color_encoding_cms.h.
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
    public bool IsUnknown => !this.HaveGamma && this.TransferFunction == JxlTransferFunction.Unknown;

    /// <summary>Copies the fields of <paramref name="other"/> into this instance.</summary>
    /// <param name="other">The transfer function to copy.</param>
    public void CopyFrom(JxlCustomTransferFunction other)
    {
        this.NonserializedColorSpace = other.NonserializedColorSpace;
        this.HaveGamma = other.HaveGamma;
        this.Gamma = other.Gamma;
        this.TransferFunction = other.TransferFunction;
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
