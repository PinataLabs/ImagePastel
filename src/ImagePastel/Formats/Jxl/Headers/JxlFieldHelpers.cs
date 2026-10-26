// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/image_metadata.h and lib/jxl/image_metadata.cc (BitDepth, ExtraChannelInfo,
// OpsinInverseMatrix, ToneMapping, CustomTransformData, ImageMetadata, CodecMetadata),
// VisitNameString from lib/jxl/frame_header.h, and the default constants of
// lib/jxl/cms/opsin_params.h, lib/jxl/quantizer.h and lib/jxl/base/common.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.Headers;

/// <summary>Field helpers shared by several bundles.</summary>
internal static class JxlFieldHelpers
{
    /// <summary>Visits a UTF-8 name of up to 1071 bytes.</summary>
    /// <param name="visitor">The visitor.</param>
    /// <param name="name">The current name bytes.</param>
    /// <returns>The new name bytes.</returns>
    public static byte[] VisitNameString(JxlVisitor visitor, byte[] name)
    {
        uint nameLength = visitor.U32(Val(0), Bits(4), BitsOffset(5, 16), BitsOffset(10, 48), 0, (uint)name.Length);
        if (visitor.IsReading)
        {
            Array.Resize(ref name, (int)nameLength);
        }

        for (int i = 0; i < nameLength; i++)
        {
            name[i] = (byte)visitor.Bits(8, 0, name[i]);
        }

        return name;
    }
}
