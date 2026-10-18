// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_ans.h and lib/jxl/dec_ans.cc (ANSSymbolReader).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>
/// Reads hybrid-uint values from an entropy-coded stream, with ANS or prefix codes.
/// </summary>
/// <remarks>
/// libjxl's reader also decodes LZ77 copies, through a template flag. LZ77 arrives in a later slice;
/// until then <see cref="JxlEntropyDecoder.DecodeHistograms"/> rejects streams that enable it, so this
/// reader only has the literal path.
/// </remarks>
internal sealed class JxlAnsSymbolReader
{
    private readonly JxlAliasEntry[] aliasTables;
    private readonly JxlHuffmanDecodingData[] huffmanData;
    private readonly bool usePrefixCode;
    private readonly JxlHybridUintConfig[] configs;
    private readonly int logAlphaSize;
    private readonly int logEntrySize;
    private readonly uint entrySizeMinus1;
    private uint state = JxlAnsParams.AnsSignature << 16;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlAnsSymbolReader"/> class. For ANS, this reads
    /// the initial 32-bit state.
    /// </summary>
    /// <param name="code">The histograms.</param>
    /// <param name="br">The reader.</param>
    public JxlAnsSymbolReader(JxlAnsCode code, JxlBitReader br)
    {
        this.aliasTables = code.AliasTables;
        this.huffmanData = code.HuffmanData;
        this.usePrefixCode = code.UsePrefixCode;
        this.configs = code.UintConfig;
        if (!this.usePrefixCode)
        {
            this.state = (uint)br.ReadBits(32);
            this.logAlphaSize = code.LogAlphaSize;
            this.logEntrySize = JxlAnsParams.AnsLogTabSize - code.LogAlphaSize;
            this.entrySizeMinus1 = (1u << this.logEntrySize) - 1;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the ANS state is back at its initial value, which a valid
    /// stream guarantees at its end. Always <see langword="true"/> for prefix codes.
    /// </summary>
    public bool CheckAnsFinalState => this.state == JxlAnsParams.AnsSignature << 16;

    /// <summary>
    /// Reads the bits of a value after its token. Tokens below the split are the value itself.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="token">The token.</param>
    /// <param name="br">The reader, refilled by the caller.</param>
    /// <returns>The value.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static uint ReadHybridUintConfig(in JxlHybridUintConfig config, uint token, JxlBitReader br)
    {
        uint splitToken = config.SplitToken;
        int msbInToken = (int)config.MsbInToken;
        int lsbInToken = (int)config.LsbInToken;
        uint splitExponent = config.SplitExponent;

        // Fast-track version of hybrid integer decoding.
        if (token < splitToken)
        {
            return token;
        }

        uint nbits = splitExponent - (uint)(msbInToken + lsbInToken) + ((token - splitToken) >> (msbInToken + lsbInToken));

        // A count above 31 is invalid; libjxl clamps it rather than rejecting it.
        nbits &= 31u;
        ulong low = token & ((1u << lsbInToken) - 1);
        ulong t = token >> lsbInToken;
        ulong bits = br.PeekBits((int)nbits);
        br.Consume((int)nbits);
        ulong ret = (((((1UL << msbInToken) | (t & ((1UL << msbInToken) - 1))) << (int)nbits) | bits) << lsbInToken) | low;
        return unchecked((uint)ret);
    }

    /// <summary>Reads one value with the histogram <paramref name="ctx"/> (a cluster, after the context map).</summary>
    /// <param name="ctx">The histogram index.</param>
    /// <param name="br">The reader.</param>
    /// <returns>The value.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public uint ReadHybridUintClustered(int ctx, JxlBitReader br)
    {
        // Covers ReadSymbolWithoutRefill and PeekBits.
        br.Refill();
        uint token = this.ReadSymbolWithoutRefill(ctx, br);
        return ReadHybridUintConfig(in this.configs[ctx], token, br);
    }

    /// <summary>Reads one value for context <paramref name="ctx"/>, mapped to a histogram by <paramref name="contextMap"/>.</summary>
    /// <param name="ctx">The context.</param>
    /// <param name="br">The reader.</param>
    /// <param name="contextMap">The context map.</param>
    /// <returns>The value.</returns>
    public uint ReadHybridUint(int ctx, JxlBitReader br, byte[] contextMap)
        => this.ReadHybridUintClustered(contextMap[ctx], br);

    [MethodImpl(InliningOptions.ShortMethod)]
    private uint ReadSymbolWithoutRefill(int histoIdx, JxlBitReader br)
    {
        if (this.usePrefixCode)
        {
            return (uint)this.huffmanData[histoIdx].ReadSymbol(br);
        }

        return this.ReadSymbolAnsWithoutRefill(histoIdx, br);
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private uint ReadSymbolAnsWithoutRefill(int histoIdx, JxlBitReader br)
    {
        uint res = this.state & (JxlAnsParams.AnsTabSize - 1u);

        JxlAliasSymbol symbol = JxlAliasTable.Lookup(this.aliasTables, histoIdx << this.logAlphaSize, res, this.logEntrySize, this.entrySizeMinus1);
        this.state = unchecked((symbol.Freq * (this.state >> JxlAnsParams.AnsLogTabSize)) + symbol.Offset);

        // Renormalize: shift in 16 bits when the state drops below 2^16.
        if (this.state < (1u << 16))
        {
            this.state = (this.state << 16) | (uint)br.PeekBits(16);
            br.Consume(16);
        }

        return (uint)symbol.Value;
    }
}
