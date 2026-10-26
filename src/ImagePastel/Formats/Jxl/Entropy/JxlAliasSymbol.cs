// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/ans_params.h, lib/jxl/ans_common.h and lib/jxl/ans_common.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>One symbol looked up in an alias table.</summary>
internal struct JxlAliasSymbol
{
    public int Value;
    public uint Offset;
    public uint Freq;
}
