// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/ans_params.h, lib/jxl/ans_common.h and lib/jxl/ans_common.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>Constants of the entropy coder.</summary>
internal static class JxlAnsParams
{
    /// <summary>Log2 of the ANS table size.</summary>
    public const int AnsLogTabSize = 12;

    /// <summary>The ANS table size: every ANS distribution sums to this.</summary>
    public const int AnsTabSize = 1 << AnsLogTabSize;

    /// <summary>The largest ANS alphabet.</summary>
    public const int AnsMaxAlphabetSize = 256;

    /// <summary>The longest prefix code; the prefix alphabet is at most 2^15.</summary>
    public const int PrefixMaxBits = 15;

    /// <summary>The initial ANS state, also checked at the end of a stream like a CRC.</summary>
    public const uint AnsSignature = 0x13;
}
