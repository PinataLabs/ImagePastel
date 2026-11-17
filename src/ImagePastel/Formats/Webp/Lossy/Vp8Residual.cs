// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NETCOREAPP3_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace PinataLabs.ImagePastel.Formats.Webp.Lossy;

/// <summary>
/// On-the-fly info about the current set of residuals.
/// </summary>
internal class Vp8Residual
{
#if NETCOREAPP3_0_OR_GREATER
    private static readonly Vector128<byte> Cst2 = Vector128.Create((byte)2);

    private static readonly Vector128<byte> Cst67 = Vector128.Create((byte)67);
#endif

    private ushort[] levelCosts;

    public int First { get; set; }

    public int Last { get; set; }

    public int CoeffType { get; set; }

    public short[] Coeffs { get; } = new short[16];

    public Vp8BandProbas[] Prob { get; set; }

    public Vp8Stats[] Stats { get; set; }

    public void Init(int first, int coeffType, Vp8EncProba prob)
    {
        this.First = first;
        this.CoeffType = coeffType;
        this.Prob = prob.Coeffs[this.CoeffType];
        this.Stats = prob.Stats[this.CoeffType];
        this.levelCosts = prob.LevelCosts;
    }

    public void SetCoeffs(Span<short> coeffs)
    {
        this.Last = FindLast(coeffs);
        coeffs.Slice(0, 16).CopyTo(this.Coeffs);
    }

    // Simulate block coding, but only record statistics.
    // Note: no need to record the fixed probas.
    public int RecordCoeffs(int ctx)
    {
        int n = this.First;
        Vp8StatsArray s = this.Stats[n].Stats[ctx];
        if (this.Last < 0)
        {
            this.RecordStats(0, s, 0);
            return 0;
        }

        while (n <= this.Last)
        {
            int v;
            this.RecordStats(1, s, 0);  // order of record doesn't matter
            while ((v = this.Coeffs[n++]) == 0)
            {
                this.RecordStats(0, s, 1);
                s = this.Stats[WebpConstants.Vp8EncBands[n]].Stats[0];
            }

            this.RecordStats(1, s, 1);
            bool bit = (uint)(v + 1) > 2u;
            if (this.RecordStats(bit ? 1 : 0, s, 2) == 0)
            {
                // v = -1 or 1
                s = this.Stats[WebpConstants.Vp8EncBands[n]].Stats[1];
            }
            else
            {
                v = Math.Abs(v);
                if (v > WebpConstants.MaxVariableLevel)
                {
                    v = WebpConstants.MaxVariableLevel;
                }

                int bits = WebpLookupTables.Vp8LevelCodes[v - 1][1];
                int pattern = WebpLookupTables.Vp8LevelCodes[v - 1][0];
                int i;
                for (i = 0; (pattern >>= 1) != 0; i++)
                {
                    int mask = 2 << i;
                    if ((pattern & 1) != 0)
                    {
                        this.RecordStats((bits & mask) != 0 ? 1 : 0, s, 3 + i);
                    }
                }

                s = this.Stats[WebpConstants.Vp8EncBands[n]].Stats[2];
            }
        }

        if (n < 16)
        {
            this.RecordStats(0, s, 0);
        }

        return 1;
    }

    public int GetResidualCost(int ctx0, ReadOnlySpan<short> coeffs, out bool nonZero)
    {
        coeffs = coeffs.Slice(0, 16);
        int n = this.First;
        int last = FindLast(coeffs);
        nonZero = last >= 0;
        int p0 = this.Prob[n].Probabilities[ctx0].Probabilities[0];
        if (last < 0)
        {
            return LossyUtils.Vp8BitCost(0, (byte)p0);
        }

        // bitCost(1, p0) is already incorporated in the level costs, but only if ctx != 0
        // (RFC 6386 section 13.3). For ctx0 == 0, we need to add it here or it'll
        // be missing during the loop.
        int cost = ctx0 == 0 ? LossyUtils.Vp8BitCost(1, (byte)p0) : 0;

        ref ushort typeCosts = ref MemoryMarshal.GetReference(this.levelCosts.AsSpan(this.CoeffType * Vp8EncProba.LevelCostsPerType, Vp8EncProba.LevelCostsPerType));
        ref short coeffsRef = ref MemoryMarshal.GetReference(coeffs);
        int ctx = ctx0;
        int v;
#if NETCOREAPP3_0_OR_GREATER
        if (Avx2.IsSupported)
        {
            Vector256<short> c = Unsafe.As<short, Vector256<short>>(ref coeffsRef);
            Vector256<short> abs = Avx2.Max(c, Avx2.Subtract(Vector256<short>.Zero, c));

            // Avx2.PackSignedSaturate packs within each 128-bit lane, so pack the two halves together instead.
            Vector128<byte> abs8 = Sse2.PackSignedSaturate(abs.GetLower(), abs.GetUpper()).AsByte();
            Vector128<byte> ctxs = Sse2.Min(abs8, Cst2);
            Vector128<byte> levels = Sse2.Min(abs8, Cst67);
            ref ushort absRef = ref Unsafe.As<Vector256<short>, ushort>(ref abs);
            ref byte ctxsRef = ref Unsafe.As<Vector128<byte>, byte>(ref ctxs);
            ref byte levelsRef = ref Unsafe.As<Vector128<byte>, byte>(ref levels);
            for (; n < last; ++n)
            {
                cost += WebpLookupTables.Vp8LevelFixedCosts[Unsafe.Add(ref absRef, n)] + Unsafe.Add(ref typeCosts, CostIndex(n, ctx, Unsafe.Add(ref levelsRef, n)));
                ctx = Unsafe.Add(ref ctxsRef, n);
            }

            v = Unsafe.Add(ref absRef, n);
        }
        else
#endif
        {
            for (; n < last; ++n)
            {
                v = Abs(Unsafe.Add(ref coeffsRef, n));
                cost += LevelCost(ref typeCosts, n, ctx, v);
                ctx = v >= 2 ? 2 : v;
            }

            v = Abs(Unsafe.Add(ref coeffsRef, n));
        }

        cost += LevelCost(ref typeCosts, n, ctx, v);
        if (n < 15)
        {
            int b = WebpConstants.Vp8EncBands[n + 1];
            ctx = v == 1 ? 1 : 2;
            int lastP0 = this.Prob[b].Probabilities[ctx].Probabilities[0];
            cost += LossyUtils.Vp8BitCost(0, (byte)lastP0);
        }

        return cost;
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static int Abs(int v)
    {
        int sign = v >> 31;
        return (v ^ sign) - sign;
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static int LevelCost(ref ushort typeCosts, int n, int ctx, int level)
        => WebpLookupTables.Vp8LevelFixedCosts[level]
           + Unsafe.Add(ref typeCosts, CostIndex(n, ctx, level > WebpConstants.MaxVariableLevel ? WebpConstants.MaxVariableLevel : level));

    [MethodImpl(InliningOptions.ShortMethod)]
    private static int CostIndex(int n, int ctx, int clampedLevel)
        => (n * Vp8EncProba.LevelCostsPerPosition) + (ctx * Vp8EncProba.LevelCostsPerCtx) + clampedLevel;

    private static int FindLast(ReadOnlySpan<short> coeffs)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (Sse2.IsSupported)
        {
            ref short coeffsRef = ref MemoryMarshal.GetReference(coeffs.Slice(0, 16));
            Vector128<byte> c0 = Unsafe.As<short, Vector128<byte>>(ref coeffsRef);
            Vector128<byte> c1 = Unsafe.As<short, Vector128<byte>>(ref Unsafe.Add(ref coeffsRef, 8));

            Vector128<sbyte> m0 = Sse2.PackSignedSaturate(c0.AsInt16(), c1.AsInt16());
            Vector128<sbyte> m1 = Sse2.CompareEqual(m0, Vector128<sbyte>.Zero);

            uint mask = 0x0000ffffu ^ (uint)Sse2.MoveMask(m1);

            return mask != 0 ? Numerics.Log2(mask) : -1;
        }
#endif

        for (int n = 15; n >= 0; --n)
        {
            if (coeffs[n] != 0)
            {
                return n;
            }
        }

        return -1;
    }

    private int RecordStats(int bit, Vp8StatsArray statsArr, int idx)
    {
        // An overflow is inbound. Note we handle this at 0xfffe0000u instead of
        // 0xffff0000u to make sure p + 1u does not overflow.
        if (statsArr.Stats[idx] >= 0xfffe0000u)
        {
            statsArr.Stats[idx] = ((statsArr.Stats[idx] + 1u) >> 1) & 0x7fff7fffu;  // -> divide the stats by 2.
        }

        // Record bit count (lower 16 bits) and increment total count (upper 16 bits).
        statsArr.Stats[idx] += 0x00010000u + (uint)bit;

        return bit;
    }
}
