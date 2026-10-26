// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ans.h (ANSEncSymbolInfo, ANSCoder).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>
/// The ANS state machine of the writer. libjxl can divide by multiplying with a reciprocal
/// (<c>USE_MULT_BY_RECIPROCAL</c>); this port divides, which gives the same state.
/// </summary>
internal struct JxlAnsCoder
{
    private uint state;

    /// <summary>Initializes a new instance of the <see cref="JxlAnsCoder"/> struct.</summary>
    public JxlAnsCoder() => this.state = JxlAnsParams.AnsSignature << 16;

    /// <summary>Gets the state, which the stream starts with.</summary>
    public readonly uint State => this.state;

    /// <summary>
    /// Pushes a symbol and returns the bits it pushes out of the state, which are written in reverse
    /// order with the rest of the stream.
    /// </summary>
    /// <param name="t">The symbol's encoding information.</param>
    /// <param name="nbits">The number of bits returned: 0 or 16.</param>
    /// <returns>The bits.</returns>
    public uint PutSymbol(in JxlAnsEncSymbolInfo t, out int nbits)
    {
        uint bits = 0;
        nbits = 0;
        if ((this.state >> (32 - JxlAnsParams.AnsLogTabSize)) >= t.Freq)
        {
            bits = this.state & 0xffff;
            this.state >>= 16;
            nbits = 16;
        }

        this.state = ((this.state / t.Freq) << JxlAnsParams.AnsLogTabSize) + t.ReverseMap[this.state % t.Freq];
        return bits;
    }
}

/// <summary>How one symbol of a histogram is encoded, with ANS or with a prefix code.</summary>
internal struct JxlAnsEncSymbolInfo
{
    /// <summary>The ANS frequency.</summary>
    public ushort Freq;

    /// <summary>For each of the symbol's <see cref="Freq"/> slots, its position in the alias table.</summary>
    public ushort[] ReverseMap;

    /// <summary>The prefix code length.</summary>
    public byte Depth;

    /// <summary>The prefix code bits.</summary>
    public ushort Bits;
}
