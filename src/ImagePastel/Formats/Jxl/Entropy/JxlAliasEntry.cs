// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/ans_params.h, lib/jxl/ans_common.h and lib/jxl/ans_common.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>One entry of an alias table, which maps an ANS state to a symbol in constant time.</summary>
internal struct JxlAliasEntry
{
    /// <summary>Positions below the cutoff belong to the entry's own symbol; the others to <see cref="RightValue"/>.</summary>
    public byte Cutoff;

    /// <summary>The other symbol of the entry; below the alphabet size.</summary>
    public byte RightValue;

    public ushort Freq0;

    /// <summary>At most the ANS table size; it may wrap below zero, as in libjxl's 16-bit field.</summary>
    public ushort Offsets1;

    public ushort Freq1XorFreq0;
}
