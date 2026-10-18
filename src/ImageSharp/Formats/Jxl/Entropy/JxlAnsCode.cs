// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_ans.h and lib/jxl/dec_ans.cc (HybridUintConfig, LZ77Params, ANSCode).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>
/// The decoded histograms of an entropy-coded stream: an alias table per histogram for ANS,
/// or a prefix code per histogram, plus a hybrid uint configuration per histogram.
/// </summary>
internal sealed class JxlAnsCode
{
    /// <summary>Gets or sets the alias tables, one block of 2^<see cref="LogAlphaSize"/> entries per histogram. ANS only.</summary>
    public JxlAliasEntry[] AliasTables { get; set; }

    /// <summary>Gets or sets the prefix codes, one per histogram. Prefix codes only.</summary>
    public JxlHuffmanDecodingData[] HuffmanData { get; set; }

    /// <summary>Gets or sets the hybrid uint configuration of each histogram.</summary>
    public JxlHybridUintConfig[] UintConfig { get; set; }

    /// <summary>
    /// Gets or sets, for each histogram, the only symbol it can produce, or -1. A symbol
    /// is degenerate when every other count is zero.
    /// </summary>
    public int[] DegenerateSymbols { get; set; }

    /// <summary>Gets or sets a value indicating whether the stream uses prefix codes rather than ANS.</summary>
    public bool UsePrefixCode { get; set; }

    /// <summary>Gets or sets log2 of the ANS alphabet size.</summary>
    public int LogAlphaSize { get; set; }

    /// <summary>Gets the LZ77 parameters.</summary>
    public JxlLz77Params Lz77 { get; } = new();
}
