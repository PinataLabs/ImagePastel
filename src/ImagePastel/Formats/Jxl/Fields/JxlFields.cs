// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/field_encodings.h (Fields, EnumValid) and lib/jxl/fields.h (Visitor, VisitorBase).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.Fields;

/// <summary>
/// A bundle of header fields. One <see cref="VisitFields"/> method describes the fields once, and each
/// visitor gives it a meaning: read from the bitstream, or set to defaults.
/// </summary>
internal abstract class JxlFields
{
    /// <summary>Visits every field of the bundle, in bitstream order.</summary>
    /// <param name="visitor">The visitor.</param>
    public abstract void VisitFields(JxlVisitor visitor);
}
