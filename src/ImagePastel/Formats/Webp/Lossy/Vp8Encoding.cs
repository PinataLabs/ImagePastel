// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NETCOREAPP3_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace PinataLabs.ImagePastel.Formats.Webp.Lossy;

/// <summary>
/// Methods for encoding a VP8 frame.
/// </summary>
internal static unsafe class Vp8Encoding
{
    private const int KC1 = 20091 + (1 << 16);

    private const int KC2 = 35468;

    private static readonly byte[] Clip1 = new byte[255 + 510 + 1]; // clips [-255,510] to [0,255]

    private const int I16DC16 = 0 * 16 * WebpConstants.Bps;

    private const int I16TM16 = I16DC16 + 16;

    private const int I16VE16 = 1 * 16 * WebpConstants.Bps;

    private const int I16HE16 = I16VE16 + 16;

    private const int C8DC8 = 2 * 16 * WebpConstants.Bps;

    private const int C8TM8 = C8DC8 + (1 * 16);

    private const int C8VE8 = (2 * 16 * WebpConstants.Bps) + (8 * WebpConstants.Bps);

    private const int C8HE8 = C8VE8 + (1 * 16);

    public static readonly int[] Vp8I16ModeOffsets = { I16DC16, I16TM16, I16VE16, I16HE16 };

    public static readonly int[] Vp8UvModeOffsets = { C8DC8, C8TM8, C8VE8, C8HE8 };

    private const int I4DC4 = (3 * 16 * WebpConstants.Bps) + 0;

    private const int I4TM4 = I4DC4 + 4;

    private const int I4VE4 = I4DC4 + 8;

    private const int I4HE4 = I4DC4 + 12;

    private const int I4RD4 = I4DC4 + 16;

    private const int I4VR4 = I4DC4 + 20;

    private const int I4LD4 = I4DC4 + 24;

    private const int I4VL4 = I4DC4 + 28;

    private const int I4HD4 = (3 * 16 * WebpConstants.Bps) + (4 * WebpConstants.Bps);

    private const int I4HU4 = I4HD4 + 4;

    public static readonly int[] Vp8I4ModeOffsets = { I4DC4, I4TM4, I4VE4, I4HE4, I4RD4, I4VR4, I4LD4, I4VL4, I4HD4, I4HU4 };

#if NETCOREAPP3_0_OR_GREATER
#pragma warning disable SA1310 // Field names should not contain underscore
    private static readonly Vector128<short> K1 = Vector128.Create((short)20091).AsInt16();

    private static readonly Vector128<short> K2 = Vector128.Create((short)-30068).AsInt16();

    private static readonly Vector128<short> Four = Vector128.Create((short)4);

    private static readonly Vector128<short> Seven = Vector128.Create((short)7);

    private static readonly Vector128<short> K88p = Vector128.Create(8, 0, 8, 0, 8, 0, 8, 0, 8, 0, 8, 0, 8, 0, 8, 0).AsInt16();

    private static readonly Vector128<short> K88m = Vector128.Create(8, 0, 248, 255, 8, 0, 248, 255, 8, 0, 248, 255, 8, 0, 248, 255).AsInt16();

    private static readonly Vector128<short> K5352_2217p = Vector128.Create(232, 20, 169, 8, 232, 20, 169, 8, 232, 20, 169, 8, 232, 20, 169, 8).AsInt16();

    private static readonly Vector128<short> K5352_2217m = Vector128.Create(169, 8, 24, 235, 169, 8, 24, 235, 169, 8, 24, 235, 169, 8, 24, 235).AsInt16();

    private static readonly Vector128<int> K937 = Vector128.Create(937);

    private static readonly Vector128<int> K1812 = Vector128.Create(1812);

    private static readonly Vector128<short> K5352_2217 = Vector128.Create(169, 8, 232, 20, 169, 8, 232, 20, 169, 8, 232, 20, 169, 8, 232, 20).AsInt16();

    private static readonly Vector128<short> K2217_5352 = Vector128.Create(24, 235, 169, 8, 24, 235, 169, 8, 24, 235, 169, 8, 24, 235, 169, 8).AsInt16();

    private static readonly Vector128<int> K12000PlusOne = Vector128.Create(12000 + (1 << 16));

    private static readonly Vector128<int> K51000 = Vector128.Create(51000);

    private static readonly Vector256<short> K1Vec256 = Vector256.Create(K1, K1);

    private static readonly Vector256<short> K2Vec256 = Vector256.Create(K2, K2);

    private static readonly Vector256<short> FourVec256 = Vector256.Create(Four, Four);

    private static readonly Vector256<short> SevenVec256 = Vector256.Create(Seven, Seven);

    private static readonly Vector256<short> K88pVec256 = Vector256.Create(K88p, K88p);

    private static readonly Vector256<short> K88mVec256 = Vector256.Create(K88m, K88m);

    private static readonly Vector256<short> K5352_2217pVec256 = Vector256.Create(K5352_2217p, K5352_2217p);

    private static readonly Vector256<short> K5352_2217mVec256 = Vector256.Create(K5352_2217m, K5352_2217m);

    private static readonly Vector256<int> K937Vec256 = Vector256.Create(937);

    private static readonly Vector256<int> K1812Vec256 = Vector256.Create(1812);

    private static readonly Vector256<short> K5352_2217Vec256 = Vector256.Create(K5352_2217, K5352_2217);

    private static readonly Vector256<short> K2217_5352Vec256 = Vector256.Create(K2217_5352, K2217_5352);

    private static readonly Vector256<int> K12000PlusOneVec256 = Vector256.Create(12000 + (1 << 16));

    private static readonly Vector256<int> K51000Vec256 = Vector256.Create(51000);

    private const byte MmShuffle2301 = (2 << 6) | (3 << 4) | (0 << 2) | 1;

    private const byte MmShuffle1032 = (1 << 6) | (0 << 4) | (3 << 2) | 2;
#pragma warning restore SA1310 // Field names should not contain underscore
#endif

    static Vp8Encoding()
    {
        for (int i = -255; i <= 255 + 255; i++)
        {
            Clip1[255 + i] = Clip8b(i);
        }
    }

    // Transforms (Paragraph 14.4)
    // Does two inverse transforms.
    public static void ITransformTwo(Span<byte> reference, Span<short> input, Span<byte> dst, Span<int> scratch)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (Sse2.IsSupported)
        {
            // This implementation makes use of 16-bit fixed point versions of two
            // multiply constants:
            //    K1 = sqrt(2) * cos (pi/8) ~= 85627 / 2^16
            //    K2 = sqrt(2) * sin (pi/8) ~= 35468 / 2^16
            //
            // To be able to use signed 16-bit integers, we use the following trick to
            // have constants within range:
            // - Associated constants are obtained by subtracting the 16-bit fixed point
            //   version of one:
            //      k = K - (1 << 16)  =>  K = k + (1 << 16)
            //      K1 = 85267  =>  k1 =  20091
            //      K2 = 35468  =>  k2 = -30068
            // - The multiplication of a variable by a constant become the sum of the
            //   variable and the multiplication of that variable by the associated
            //   constant:
            //      (x * K) >> 16 = (x * (k + (1 << 16))) >> 16 = ((x * k ) >> 16) + x

            // Load and concatenate the transform coefficients (we'll do two inverse
            // transforms in parallel). In the case of only one inverse transform, the
            // second half of the vectors will just contain random value we'll never
            // use nor store.
            ref short inputRef = ref MemoryMarshal.GetReference(input);
            var in0 = Vector128.Create(Unsafe.As<short, long>(ref inputRef), 0);
            var in1 = Vector128.Create(Unsafe.As<short, long>(ref Unsafe.Add(ref inputRef, 4)), 0);
            var in2 = Vector128.Create(Unsafe.As<short, long>(ref Unsafe.Add(ref inputRef, 8)), 0);
            var in3 = Vector128.Create(Unsafe.As<short, long>(ref Unsafe.Add(ref inputRef, 12)), 0);

            // a00 a10 a20 a30   x x x x
            // a01 a11 a21 a31   x x x x
            // a02 a12 a22 a32   x x x x
            // a03 a13 a23 a33   x x x x
            var inb0 = Vector128.Create(Unsafe.As<short, long>(ref Unsafe.Add(ref inputRef, 16)), 0);
            var inb1 = Vector128.Create(Unsafe.As<short, long>(ref Unsafe.Add(ref inputRef, 20)), 0);
            var inb2 = Vector128.Create(Unsafe.As<short, long>(ref Unsafe.Add(ref inputRef, 24)), 0);
            var inb3 = Vector128.Create(Unsafe.As<short, long>(ref Unsafe.Add(ref inputRef, 28)), 0);

            in0 = Sse2.UnpackLow(in0, inb0);
            in1 = Sse2.UnpackLow(in1, inb1);
            in2 = Sse2.UnpackLow(in2, inb2);
            in3 = Sse2.UnpackLow(in3, inb3);

            // a00 a10 a20 a30   b00 b10 b20 b30
            // a01 a11 a21 a31   b01 b11 b21 b31
            // a02 a12 a22 a32   b02 b12 b22 b32
            // a03 a13 a23 a33   b03 b13 b23 b33
            InverseTransformTwo(in0, in1, in2, in3, ref MemoryMarshal.GetReference(reference), ref MemoryMarshal.GetReference(dst));
        }
        else
#endif
        {
            ITransformOne(reference, input, dst, scratch);
            ITransformOne(reference.Slice(4), input.Slice(16), dst.Slice(4), scratch);
        }
    }

    /// <summary>
    /// Does four inverse transforms over a 16x4 strip: the two blocks in <paramref name="input01"/> fill
    /// columns 0-7 and the two blocks in <paramref name="input23"/> fill columns 8-15.
    /// Gives the same result as two <see cref="ITransformTwo"/> calls.
    /// </summary>
    public static void ITransformFour(Span<byte> reference, Span<short> input01, Span<short> input23, Span<byte> dst, Span<int> scratch)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (Avx2.IsSupported)
        {
            // Each 128-bit lane holds the operands ITransformTwo holds for one block pair,
            // and every operation below stays within its lane.
            ref short input01Ref = ref MemoryMarshal.GetReference(input01);
            ref short input23Ref = ref MemoryMarshal.GetReference(input23);
            Vector256<long> a = Unsafe.As<short, Vector256<long>>(ref input01Ref);
            Vector256<long> b = Unsafe.As<short, Vector256<long>>(ref Unsafe.Add(ref input01Ref, 16));
            Vector256<long> c = Unsafe.As<short, Vector256<long>>(ref input23Ref);
            Vector256<long> d = Unsafe.As<short, Vector256<long>>(ref Unsafe.Add(ref input23Ref, 16));

            Vector256<long> abLow = Avx2.UnpackLow(a, b);
            Vector256<long> abHigh = Avx2.UnpackHigh(a, b);
            Vector256<long> cdLow = Avx2.UnpackLow(c, d);
            Vector256<long> cdHigh = Avx2.UnpackHigh(c, d);
            Vector256<short> in0 = Avx2.Permute2x128(abLow, cdLow, 0x20).AsInt16();
            Vector256<short> in1 = Avx2.Permute2x128(abHigh, cdHigh, 0x20).AsInt16();
            Vector256<short> in2 = Avx2.Permute2x128(abLow, cdLow, 0x31).AsInt16();
            Vector256<short> in3 = Avx2.Permute2x128(abHigh, cdHigh, 0x31).AsInt16();

            InverseTransformPassAvx2(in0, in2, in1, in3, Vector256<short>.Zero, out Vector256<short> tmp0, out Vector256<short> tmp1, out Vector256<short> tmp2, out Vector256<short> tmp3);
            Transpose2x4x4Avx2(ref tmp0, ref tmp1, ref tmp2, ref tmp3);
            InverseTransformPassAvx2(tmp0, tmp2, tmp1, tmp3, FourVec256, out tmp0, out tmp1, out tmp2, out tmp3);
            tmp0 = Avx2.ShiftRightArithmetic(tmp0, 3);
            tmp1 = Avx2.ShiftRightArithmetic(tmp1, 3);
            tmp2 = Avx2.ShiftRightArithmetic(tmp2, 3);
            tmp3 = Avx2.ShiftRightArithmetic(tmp3, 3);
            Transpose2x4x4Avx2(ref tmp0, ref tmp1, ref tmp2, ref tmp3);

            ref byte referenceRef = ref MemoryMarshal.GetReference(reference);
            ref byte outputRef = ref MemoryMarshal.GetReference(dst);
            AddToReferenceRowAvx2(ref referenceRef, ref outputRef, tmp0);
            AddToReferenceRowAvx2(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps), ref Unsafe.Add(ref outputRef, WebpConstants.Bps), tmp1);
            AddToReferenceRowAvx2(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 2), ref Unsafe.Add(ref outputRef, WebpConstants.Bps * 2), tmp2);
            AddToReferenceRowAvx2(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 3), ref Unsafe.Add(ref outputRef, WebpConstants.Bps * 3), tmp3);
        }
        else
#endif
        {
            ITransformTwo(reference, input01, dst, scratch);
            ITransformTwo(reference.Slice(8), input23, dst.Slice(8), scratch);
        }
    }

#if NETCOREAPP3_0_OR_GREATER
    // One pass of the inverse transform, as in InverseTransformVerticalPass/InverseTransformHorizontalPass
    // (the horizontal pass adds four to the DC term before the final shift).
    [MethodImpl(InliningOptions.ShortMethod)]
    private static void InverseTransformPassAvx2(Vector256<short> in0, Vector256<short> in2, Vector256<short> in1, Vector256<short> in3, Vector256<short> rounder, out Vector256<short> tmp0, out Vector256<short> tmp1, out Vector256<short> tmp2, out Vector256<short> tmp3)
    {
        Vector256<short> dc = Avx2.Add(in0, rounder);
        Vector256<short> a = Avx2.Add(dc, in2);
        Vector256<short> b = Avx2.Subtract(dc, in2);

        Vector256<short> c1 = Avx2.MultiplyHigh(in1, K2Vec256);
        Vector256<short> c2 = Avx2.MultiplyHigh(in3, K1Vec256);
        Vector256<short> c3 = Avx2.Subtract(in1, in3);
        Vector256<short> c4 = Avx2.Subtract(c1, c2);
        Vector256<short> c = Avx2.Add(c3, c4);

        Vector256<short> d1 = Avx2.MultiplyHigh(in1, K1Vec256);
        Vector256<short> d2 = Avx2.MultiplyHigh(in3, K2Vec256);
        Vector256<short> d3 = Avx2.Add(in1, in3);
        Vector256<short> d4 = Avx2.Add(d1, d2);
        Vector256<short> d = Avx2.Add(d3, d4);

        tmp0 = Avx2.Add(a, d);
        tmp1 = Avx2.Add(b, c);
        tmp2 = Avx2.Subtract(b, c);
        tmp3 = Avx2.Subtract(a, d);
    }

    // LossyUtils.Vp8Transpose_2_4x4_16b applied to each 128-bit lane.
    [MethodImpl(InliningOptions.ShortMethod)]
    private static void Transpose2x4x4Avx2(ref Vector256<short> b0, ref Vector256<short> b1, ref Vector256<short> b2, ref Vector256<short> b3)
    {
        Vector256<short> transpose00 = Avx2.UnpackLow(b0, b1);
        Vector256<short> transpose01 = Avx2.UnpackLow(b2, b3);
        Vector256<short> transpose02 = Avx2.UnpackHigh(b0, b1);
        Vector256<short> transpose03 = Avx2.UnpackHigh(b2, b3);

        Vector256<int> transpose10 = Avx2.UnpackLow(transpose00.AsInt32(), transpose01.AsInt32());
        Vector256<int> transpose11 = Avx2.UnpackLow(transpose02.AsInt32(), transpose03.AsInt32());
        Vector256<int> transpose12 = Avx2.UnpackHigh(transpose00.AsInt32(), transpose01.AsInt32());
        Vector256<int> transpose13 = Avx2.UnpackHigh(transpose02.AsInt32(), transpose03.AsInt32());

        b0 = Avx2.UnpackLow(transpose10.AsInt64(), transpose11.AsInt64()).AsInt16();
        b1 = Avx2.UnpackHigh(transpose10.AsInt64(), transpose11.AsInt64()).AsInt16();
        b2 = Avx2.UnpackLow(transpose12.AsInt64(), transpose13.AsInt64()).AsInt16();
        b3 = Avx2.UnpackHigh(transpose12.AsInt64(), transpose13.AsInt64()).AsInt16();
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static void AddToReferenceRowAvx2(ref byte reference, ref byte dst, Vector256<short> residual)
    {
        Vector256<short> sum = Avx2.Add(Avx2.ConvertToVector256Int16(Unsafe.ReadUnaligned<Vector128<byte>>(ref reference)), residual);
        Unsafe.WriteUnaligned(ref dst, Sse2.PackUnsignedSaturate(sum.GetLower(), sum.GetUpper()));
    }
#endif

    public static void ITransformOne(Span<byte> reference, Span<short> input, Span<byte> dst, Span<int> scratch)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (Sse2.IsSupported)
        {
            // Load and concatenate the transform coefficients (we'll do two inverse
            // transforms in parallel). In the case of only one inverse transform, the
            // second half of the vectors will just contain random value we'll never
            // use nor store.
            ref short inputRef = ref MemoryMarshal.GetReference(input);
            var in0 = Vector128.Create(Unsafe.As<short, long>(ref inputRef), 0);
            var in1 = Vector128.Create(Unsafe.As<short, long>(ref Unsafe.Add(ref inputRef, 4)), 0);
            var in2 = Vector128.Create(Unsafe.As<short, long>(ref Unsafe.Add(ref inputRef, 8)), 0);
            var in3 = Vector128.Create(Unsafe.As<short, long>(ref Unsafe.Add(ref inputRef, 12)), 0);

            // a00 a10 a20 a30   x x x x
            // a01 a11 a21 a31   x x x x
            // a02 a12 a22 a32   x x x x
            // a03 a13 a23 a33   x x x x

            // Vertical pass and subsequent transpose.
            // First pass, c and d calculations are longer because of the "trick" multiplications.
            InverseTransformVerticalPass(in0, in2, in1, in3, out Vector128<short> tmp0, out Vector128<short> tmp1, out Vector128<short> tmp2, out Vector128<short> tmp3);

            // Transpose the two 4x4.
            LossyUtils.Vp8Transpose_2_4x4_16b(tmp0, tmp1, tmp2, tmp3, out Vector128<long> t0, out Vector128<long> t1, out Vector128<long> t2, out Vector128<long> t3);

            // Horizontal pass and subsequent transpose.
            // First pass, c and d calculations are longer because of the "trick" multiplications.
            InverseTransformHorizontalPass(t0, t2, t1, t3, out Vector128<short> shifted0, out Vector128<short> shifted1, out Vector128<short> shifted2, out Vector128<short> shifted3);

            // Transpose the two 4x4.
            LossyUtils.Vp8Transpose_2_4x4_16b(shifted0, shifted1, shifted2, shifted3, out t0, out t1, out t2, out t3);

            // Add inverse transform to 'ref' and store.
            // Load the reference(s).
            Vector128<byte> ref0 = Vector128<byte>.Zero;
            Vector128<byte> ref1 = Vector128<byte>.Zero;
            Vector128<byte> ref2 = Vector128<byte>.Zero;
            Vector128<byte> ref3 = Vector128<byte>.Zero;
            ref byte referenceRef = ref MemoryMarshal.GetReference(reference);

            // Load four bytes/pixels per line.
            ref0 = Sse2.ConvertScalarToVector128Int32(Unsafe.As<byte, int>(ref referenceRef)).AsByte();
            ref1 = Sse2.ConvertScalarToVector128Int32(Unsafe.As<byte, int>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps))).AsByte();
            ref2 = Sse2.ConvertScalarToVector128Int32(Unsafe.As<byte, int>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 2))).AsByte();
            ref3 = Sse2.ConvertScalarToVector128Int32(Unsafe.As<byte, int>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 3))).AsByte();

            // Convert to 16b.
            ref0 = Sse2.UnpackLow(ref0, Vector128<byte>.Zero);
            ref1 = Sse2.UnpackLow(ref1, Vector128<byte>.Zero);
            ref2 = Sse2.UnpackLow(ref2, Vector128<byte>.Zero);
            ref3 = Sse2.UnpackLow(ref3, Vector128<byte>.Zero);

            // Add the inverse transform(s).
            Vector128<short> ref0InvAdded = Sse2.Add(ref0.AsInt16(), t0.AsInt16());
            Vector128<short> ref1InvAdded = Sse2.Add(ref1.AsInt16(), t1.AsInt16());
            Vector128<short> ref2InvAdded = Sse2.Add(ref2.AsInt16(), t2.AsInt16());
            Vector128<short> ref3InvAdded = Sse2.Add(ref3.AsInt16(), t3.AsInt16());

            // Unsigned saturate to 8b.
            ref0 = Sse2.PackUnsignedSaturate(ref0InvAdded, ref0InvAdded);
            ref1 = Sse2.PackUnsignedSaturate(ref1InvAdded, ref1InvAdded);
            ref2 = Sse2.PackUnsignedSaturate(ref2InvAdded, ref2InvAdded);
            ref3 = Sse2.PackUnsignedSaturate(ref3InvAdded, ref3InvAdded);

            // Unsigned saturate to 8b.
            ref byte outputRef = ref MemoryMarshal.GetReference(dst);

            // Store four bytes/pixels per line.
            int output0 = Sse2.ConvertToInt32(ref0.AsInt32());
            int output1 = Sse2.ConvertToInt32(ref1.AsInt32());
            int output2 = Sse2.ConvertToInt32(ref2.AsInt32());
            int output3 = Sse2.ConvertToInt32(ref3.AsInt32());

            Unsafe.As<byte, int>(ref outputRef) = output0;
            Unsafe.As<byte, int>(ref Unsafe.Add(ref outputRef, WebpConstants.Bps)) = output1;
            Unsafe.As<byte, int>(ref Unsafe.Add(ref outputRef, WebpConstants.Bps * 2)) = output2;
            Unsafe.As<byte, int>(ref Unsafe.Add(ref outputRef, WebpConstants.Bps * 3)) = output3;
        }
        else
#endif
        {
            int i;
            Span<int> tmp = scratch.Slice(0, 16);
            for (i = 0; i < 4; i++)
            {
                // vertical pass.
                int a = input[0] + input[8];
                int b = input[0] - input[8];
                int c = Mul(input[4], KC2) - Mul(input[12], KC1);
                int d = Mul(input[4], KC1) + Mul(input[12], KC2);
                tmp[0] = a + d;
                tmp[1] = b + c;
                tmp[2] = b - c;
                tmp[3] = a - d;
                tmp = tmp.Slice(4);
                input = input.Slice(1);
            }

            tmp = scratch;
            for (i = 0; i < 4; i++)
            {
                // horizontal pass.
                int dc = tmp[0] + 4;
                int a = dc + tmp[8];
                int b = dc - tmp[8];
                int c = Mul(tmp[4], KC2) - Mul(tmp[12], KC1);
                int d = Mul(tmp[4], KC1) + Mul(tmp[12], KC2);
                Store(dst, reference, 0, i, a + d);
                Store(dst, reference, 1, i, b + c);
                Store(dst, reference, 2, i, b - c);
                Store(dst, reference, 3, i, a - d);
                tmp = tmp.Slice(1);
            }
        }
    }

#if NETCOREAPP3_0_OR_GREATER
    /// <summary>
    /// Inverse transforms two blocks of coefficients held in registers, <paramref name="a0"/>/<paramref name="a8"/> and
    /// <paramref name="b0"/>/<paramref name="b8"/>, onto two predictors that sit side by side in <paramref name="reference"/>.
    /// Gives the same samples as <see cref="ITransformTwo"/>.
    /// </summary>
    public static void ITransformPair(Vector128<short> a0, Vector128<short> a8, Vector128<short> b0, Vector128<short> b8, Span<byte> reference, Span<byte> dst)
    {
        _ = reference[(3 * WebpConstants.Bps) + 7];
        _ = dst[(3 * WebpConstants.Bps) + 7];
        InverseTransformTwo(
            Sse2.UnpackLow(a0.AsInt64(), b0.AsInt64()),
            Sse2.UnpackHigh(a0.AsInt64(), b0.AsInt64()),
            Sse2.UnpackLow(a8.AsInt64(), b8.AsInt64()),
            Sse2.UnpackHigh(a8.AsInt64(), b8.AsInt64()),
            ref MemoryMarshal.GetReference(reference),
            ref MemoryMarshal.GetReference(dst));
    }

    // Each of in0..in3 holds one row of four coefficients for each of the two blocks.
    private static void InverseTransformTwo(Vector128<long> in0, Vector128<long> in1, Vector128<long> in2, Vector128<long> in3, ref byte referenceRef, ref byte outputRef)
    {
        // Vertical pass and subsequent transpose.
        // First pass, c and d calculations are longer because of the "trick" multiplications.
        InverseTransformVerticalPass(in0, in2, in1, in3, out Vector128<short> tmp0, out Vector128<short> tmp1, out Vector128<short> tmp2, out Vector128<short> tmp3);

        // Transpose the two 4x4.
        LossyUtils.Vp8Transpose_2_4x4_16b(tmp0, tmp1, tmp2, tmp3, out Vector128<long> t0, out Vector128<long> t1, out Vector128<long> t2, out Vector128<long> t3);

        // Horizontal pass and subsequent transpose.
        // First pass, c and d calculations are longer because of the "trick" multiplications.
        InverseTransformHorizontalPass(t0, t2, t1, t3, out Vector128<short> shifted0, out Vector128<short> shifted1, out Vector128<short> shifted2, out Vector128<short> shifted3);

        // Transpose the two 4x4.
        LossyUtils.Vp8Transpose_2_4x4_16b(shifted0, shifted1, shifted2, shifted3, out t0, out t1, out t2, out t3);

        // Add inverse transform to 'ref' and store.
        // Load the reference(s).
        Vector128<byte> ref0 = Vector128<byte>.Zero;
        Vector128<byte> ref1 = Vector128<byte>.Zero;
        Vector128<byte> ref2 = Vector128<byte>.Zero;
        Vector128<byte> ref3 = Vector128<byte>.Zero;

        // Load eight bytes/pixels per line.
        ref0 = Vector128.Create(Unsafe.As<byte, long>(ref referenceRef), 0).AsByte();
        ref1 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps)), 0).AsByte();
        ref2 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 2)), 0).AsByte();
        ref3 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 3)), 0).AsByte();

        // Convert to 16b.
        ref0 = Sse2.UnpackLow(ref0, Vector128<byte>.Zero);
        ref1 = Sse2.UnpackLow(ref1, Vector128<byte>.Zero);
        ref2 = Sse2.UnpackLow(ref2, Vector128<byte>.Zero);
        ref3 = Sse2.UnpackLow(ref3, Vector128<byte>.Zero);

        // Add the inverse transform(s).
        Vector128<short> ref0InvAdded = Sse2.Add(ref0.AsInt16(), t0.AsInt16());
        Vector128<short> ref1InvAdded = Sse2.Add(ref1.AsInt16(), t1.AsInt16());
        Vector128<short> ref2InvAdded = Sse2.Add(ref2.AsInt16(), t2.AsInt16());
        Vector128<short> ref3InvAdded = Sse2.Add(ref3.AsInt16(), t3.AsInt16());

        // Unsigned saturate to 8b.
        ref0 = Sse2.PackUnsignedSaturate(ref0InvAdded, ref0InvAdded);
        ref1 = Sse2.PackUnsignedSaturate(ref1InvAdded, ref1InvAdded);
        ref2 = Sse2.PackUnsignedSaturate(ref2InvAdded, ref2InvAdded);
        ref3 = Sse2.PackUnsignedSaturate(ref3InvAdded, ref3InvAdded);

        // Store eight bytes/pixels per line.
        Unsafe.As<byte, Vector64<byte>>(ref outputRef) = ref0.GetLower();
        Unsafe.As<byte, Vector64<byte>>(ref Unsafe.Add(ref outputRef, WebpConstants.Bps)) = ref1.GetLower();
        Unsafe.As<byte, Vector64<byte>>(ref Unsafe.Add(ref outputRef, WebpConstants.Bps * 2)) = ref2.GetLower();
        Unsafe.As<byte, Vector64<byte>>(ref Unsafe.Add(ref outputRef, WebpConstants.Bps * 3)) = ref3.GetLower();
    }

    private static void InverseTransformVerticalPass(Vector128<long> in0, Vector128<long> in2, Vector128<long> in1, Vector128<long> in3, out Vector128<short> tmp0, out Vector128<short> tmp1, out Vector128<short> tmp2, out Vector128<short> tmp3)
    {
        Vector128<short> a = Sse2.Add(in0.AsInt16(), in2.AsInt16());
        Vector128<short> b = Sse2.Subtract(in0.AsInt16(), in2.AsInt16());

        // c = MUL(in1, K2) - MUL(in3, K1) = MUL(in1, k2) - MUL(in3, k1) + in1 - in3
        Vector128<short> c1 = Sse2.MultiplyHigh(in1.AsInt16(), K2);
        Vector128<short> c2 = Sse2.MultiplyHigh(in3.AsInt16(), K1);
        Vector128<short> c3 = Sse2.Subtract(in1.AsInt16(), in3.AsInt16());
        Vector128<short> c4 = Sse2.Subtract(c1, c2);
        Vector128<short> c = Sse2.Add(c3, c4);

        // d = MUL(in1, K1) + MUL(in3, K2) = MUL(in1, k1) + MUL(in3, k2) + in1 + in3
        Vector128<short> d1 = Sse2.MultiplyHigh(in1.AsInt16(), K1);
        Vector128<short> d2 = Sse2.MultiplyHigh(in3.AsInt16(), K2);
        Vector128<short> d3 = Sse2.Add(in1.AsInt16(), in3.AsInt16());
        Vector128<short> d4 = Sse2.Add(d1, d2);
        Vector128<short> d = Sse2.Add(d3, d4);

        // Second pass.
        tmp0 = Sse2.Add(a, d);
        tmp1 = Sse2.Add(b, c);
        tmp2 = Sse2.Subtract(b, c);
        tmp3 = Sse2.Subtract(a, d);
    }

    private static void InverseTransformHorizontalPass(Vector128<long> t0, Vector128<long> t2, Vector128<long> t1, Vector128<long> t3, out Vector128<short> shifted0, out Vector128<short> shifted1, out Vector128<short> shifted2, out Vector128<short> shifted3)
    {
        Vector128<short> dc = Sse2.Add(t0.AsInt16(), Four);
        Vector128<short> a = Sse2.Add(dc, t2.AsInt16());
        Vector128<short> b = Sse2.Subtract(dc, t2.AsInt16());

        // c = MUL(T1, K2) - MUL(T3, K1) = MUL(T1, k2) - MUL(T3, k1) + T1 - T3
        Vector128<short> c1 = Sse2.MultiplyHigh(t1.AsInt16(), K2);
        Vector128<short> c2 = Sse2.MultiplyHigh(t3.AsInt16(), K1);
        Vector128<short> c3 = Sse2.Subtract(t1.AsInt16(), t3.AsInt16());
        Vector128<short> c4 = Sse2.Subtract(c1, c2);
        Vector128<short> c = Sse2.Add(c3, c4);

        // d = MUL(T1, K1) + MUL(T3, K2) = MUL(T1, k1) + MUL(T3, k2) + T1 + T3
        Vector128<short> d1 = Sse2.MultiplyHigh(t1.AsInt16(), K1);
        Vector128<short> d2 = Sse2.MultiplyHigh(t3.AsInt16(), K2);
        Vector128<short> d3 = Sse2.Add(t1.AsInt16(), t3.AsInt16());
        Vector128<short> d4 = Sse2.Add(d1, d2);
        Vector128<short> d = Sse2.Add(d3, d4);

        // Second pass.
        Vector128<short> tmp0 = Sse2.Add(a, d);
        Vector128<short> tmp1 = Sse2.Add(b, c);
        Vector128<short> tmp2 = Sse2.Subtract(b, c);
        Vector128<short> tmp3 = Sse2.Subtract(a, d);
        shifted0 = Sse2.ShiftRightArithmetic(tmp0, 3);
        shifted1 = Sse2.ShiftRightArithmetic(tmp1, 3);
        shifted2 = Sse2.ShiftRightArithmetic(tmp2, 3);
        shifted3 = Sse2.ShiftRightArithmetic(tmp3, 3);
    }
#endif

    public static void FTransform2(Span<byte> src, Span<byte> reference, Span<short> output, Span<short> output2, Span<int> scratch)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (Sse2.IsSupported)
        {
            ref byte srcRef = ref MemoryMarshal.GetReference(src);
            ref byte referenceRef = ref MemoryMarshal.GetReference(reference);

            // Load src.
            var src0 = Vector128.Create(Unsafe.As<byte, long>(ref srcRef), 0);
            var src1 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref srcRef, WebpConstants.Bps)), 0);
            var src2 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref srcRef, WebpConstants.Bps * 2)), 0);
            var src3 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref srcRef, WebpConstants.Bps * 3)), 0);

            // Load ref.
            var ref0 = Vector128.Create(Unsafe.As<byte, long>(ref referenceRef), 0);
            var ref1 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps)), 0);
            var ref2 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 2)), 0);
            var ref3 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 3)), 0);

            // Convert both to 16 bit.
            Vector128<byte> srcLow0 = Sse2.UnpackLow(src0.AsByte(), Vector128<byte>.Zero);
            Vector128<byte> srcLow1 = Sse2.UnpackLow(src1.AsByte(), Vector128<byte>.Zero);
            Vector128<byte> srcLow2 = Sse2.UnpackLow(src2.AsByte(), Vector128<byte>.Zero);
            Vector128<byte> srcLow3 = Sse2.UnpackLow(src3.AsByte(), Vector128<byte>.Zero);
            Vector128<byte> refLow0 = Sse2.UnpackLow(ref0.AsByte(), Vector128<byte>.Zero);
            Vector128<byte> refLow1 = Sse2.UnpackLow(ref1.AsByte(), Vector128<byte>.Zero);
            Vector128<byte> refLow2 = Sse2.UnpackLow(ref2.AsByte(), Vector128<byte>.Zero);
            Vector128<byte> refLow3 = Sse2.UnpackLow(ref3.AsByte(), Vector128<byte>.Zero);

            // Compute difference. -> 00 01 02 03  00' 01' 02' 03'
            Vector128<short> diff0 = Sse2.Subtract(srcLow0.AsInt16(), refLow0.AsInt16());
            Vector128<short> diff1 = Sse2.Subtract(srcLow1.AsInt16(), refLow1.AsInt16());
            Vector128<short> diff2 = Sse2.Subtract(srcLow2.AsInt16(), refLow2.AsInt16());
            Vector128<short> diff3 = Sse2.Subtract(srcLow3.AsInt16(), refLow3.AsInt16());

            // Unpack and shuffle.
            // 00 01 02 03   0 0 0 0
            // 10 11 12 13   0 0 0 0
            // 20 21 22 23   0 0 0 0
            // 30 31 32 33   0 0 0 0
            Vector128<int> shuf01l = Sse2.UnpackLow(diff0.AsInt32(), diff1.AsInt32());
            Vector128<int> shuf23l = Sse2.UnpackLow(diff2.AsInt32(), diff3.AsInt32());
            Vector128<int> shuf01h = Sse2.UnpackHigh(diff0.AsInt32(), diff1.AsInt32());
            Vector128<int> shuf23h = Sse2.UnpackHigh(diff2.AsInt32(), diff3.AsInt32());

            // First pass.
            FTransformPass1SSE2(shuf01l.AsInt16(), shuf23l.AsInt16(), out Vector128<int> v01l, out Vector128<int> v32l);
            FTransformPass1SSE2(shuf01h.AsInt16(), shuf23h.AsInt16(), out Vector128<int> v01h, out Vector128<int> v32h);

            // Second pass.
            FTransformPass2SSE2(v01l, v32l, output);
            FTransformPass2SSE2(v01h, v32h, output2);
        }
        else
#endif
        {
            FTransform(src, reference, output, scratch);
            FTransform(src.Slice(4), reference.Slice(4), output2, scratch);
        }
    }

    /// <summary>
    /// Does four forward transforms over a 16x4 strip: columns 0-7 go to the two blocks in <paramref name="output01"/>
    /// and columns 8-15 to the two blocks in <paramref name="output23"/>.
    /// Gives the same result as two <see cref="FTransform2"/> calls.
    /// </summary>
    public static void FTransform4(Span<byte> src, Span<byte> reference, Span<short> output01, Span<short> output23, Span<int> scratch)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (Avx2.IsSupported)
        {
            // Each 128-bit lane holds the operands FTransform2 holds for one block pair,
            // and every operation below stays within its lane.
            ref byte srcRef = ref MemoryMarshal.GetReference(src);
            ref byte referenceRef = ref MemoryMarshal.GetReference(reference);
            Vector256<int> diff0 = LoadDifferenceRowAvx2(ref srcRef, ref referenceRef);
            Vector256<int> diff1 = LoadDifferenceRowAvx2(ref Unsafe.Add(ref srcRef, WebpConstants.Bps), ref Unsafe.Add(ref referenceRef, WebpConstants.Bps));
            Vector256<int> diff2 = LoadDifferenceRowAvx2(ref Unsafe.Add(ref srcRef, WebpConstants.Bps * 2), ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 2));
            Vector256<int> diff3 = LoadDifferenceRowAvx2(ref Unsafe.Add(ref srcRef, WebpConstants.Bps * 3), ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 3));

            Vector256<int> shuf01l = Avx2.UnpackLow(diff0, diff1);
            Vector256<int> shuf23l = Avx2.UnpackLow(diff2, diff3);
            Vector256<int> shuf01h = Avx2.UnpackHigh(diff0, diff1);
            Vector256<int> shuf23h = Avx2.UnpackHigh(diff2, diff3);

            FTransformPass1Avx2(shuf01l.AsInt16(), shuf23l.AsInt16(), out Vector256<int> v01l, out Vector256<int> v32l);
            FTransformPass1Avx2(shuf01h.AsInt16(), shuf23h.AsInt16(), out Vector256<int> v01h, out Vector256<int> v32h);

            FTransformPass2Avx2(v01l, v32l, out Vector256<short> d0g1Left, out Vector256<short> d2f3Left);
            FTransformPass2Avx2(v01h, v32h, out Vector256<short> d0g1Right, out Vector256<short> d2f3Right);

            ref short output01Ref = ref MemoryMarshal.GetReference(output01);
            ref short output23Ref = ref MemoryMarshal.GetReference(output23);
            Unsafe.As<short, Vector256<short>>(ref output01Ref) = Avx2.Permute2x128(d0g1Left, d2f3Left, 0x20);
            Unsafe.As<short, Vector256<short>>(ref Unsafe.Add(ref output01Ref, 16)) = Avx2.Permute2x128(d0g1Right, d2f3Right, 0x20);
            Unsafe.As<short, Vector256<short>>(ref output23Ref) = Avx2.Permute2x128(d0g1Left, d2f3Left, 0x31);
            Unsafe.As<short, Vector256<short>>(ref Unsafe.Add(ref output23Ref, 16)) = Avx2.Permute2x128(d0g1Right, d2f3Right, 0x31);
        }
        else
#endif
        {
            FTransform2(src, reference, output01, output01.Slice(16), scratch);
            FTransform2(src.Slice(8), reference.Slice(8), output23, output23.Slice(16), scratch);
        }
    }

    public static void FTransform(Span<byte> src, Span<byte> reference, Span<short> output, Span<int> scratch)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (Sse2.IsSupported)
        {
            ref byte srcRef = ref MemoryMarshal.GetReference(src);
            ref byte referenceRef = ref MemoryMarshal.GetReference(reference);

            // Load src.
            var src0 = Vector128.Create(Unsafe.As<byte, long>(ref srcRef), 0);
            var src1 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref srcRef, WebpConstants.Bps)), 0);
            var src2 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref srcRef, WebpConstants.Bps * 2)), 0);
            var src3 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref srcRef, WebpConstants.Bps * 3)), 0);

            // Load ref.
            var ref0 = Vector128.Create(Unsafe.As<byte, long>(ref referenceRef), 0);
            var ref1 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps)), 0);
            var ref2 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 2)), 0);
            var ref3 = Vector128.Create(Unsafe.As<byte, long>(ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 3)), 0);

            // 00 01 02 03 *
            // 10 11 12 13 *
            // 20 21 22 23 *
            // 30 31 32 33 *
            // Shuffle.
            Vector128<short> srcLow0 = Sse2.UnpackLow(src0.AsInt16(), src1.AsInt16());
            Vector128<short> srcLow1 = Sse2.UnpackLow(src2.AsInt16(), src3.AsInt16());
            Vector128<short> refLow0 = Sse2.UnpackLow(ref0.AsInt16(), ref1.AsInt16());
            Vector128<short> refLow1 = Sse2.UnpackLow(ref2.AsInt16(), ref3.AsInt16());

            // 00 01 10 11 02 03 12 13 * * ...
            // 20 21 30 31 22 22 32 33 * * ...

            // Convert both to 16 bit.
            Vector128<byte> src0_16b = Sse2.UnpackLow(srcLow0.AsByte(), Vector128<byte>.Zero);
            Vector128<byte> src1_16b = Sse2.UnpackLow(srcLow1.AsByte(), Vector128<byte>.Zero);
            Vector128<byte> ref0_16b = Sse2.UnpackLow(refLow0.AsByte(), Vector128<byte>.Zero);
            Vector128<byte> ref1_16b = Sse2.UnpackLow(refLow1.AsByte(), Vector128<byte>.Zero);

            // Compute the difference.
            Vector128<short> row01 = Sse2.Subtract(src0_16b.AsInt16(), ref0_16b.AsInt16());
            Vector128<short> row23 = Sse2.Subtract(src1_16b.AsInt16(), ref1_16b.AsInt16());

            // First pass.
            FTransformPass1SSE2(row01, row23, out Vector128<int> v01, out Vector128<int> v32);

            // Second pass.
            FTransformPass2SSE2(v01, v32, output);
        }
        else
#endif
        {
            int i;
            Span<int> tmp = scratch.Slice(0, 16);

            int srcIdx = 0;
            int refIdx = 0;
            for (i = 0; i < 4; i++)
            {
                int d3 = src[srcIdx + 3] - reference[refIdx + 3];
                int d2 = src[srcIdx + 2] - reference[refIdx + 2];
                int d1 = src[srcIdx + 1] - reference[refIdx + 1];
                int d0 = src[srcIdx] - reference[refIdx]; // 9bit dynamic range ([-255,255])
                int a0 = d0 + d3; // 10b                      [-510,510]
                int a1 = d1 + d2;
                int a2 = d1 - d2;
                int a3 = d0 - d3;
                tmp[3 + (i * 4)] = ((a3 * 2217) - (a2 * 5352) + 937) >> 9;
                tmp[2 + (i * 4)] = (a0 - a1) * 8;
                tmp[1 + (i * 4)] = ((a2 * 2217) + (a3 * 5352) + 1812) >> 9; // [-7536,7542]
                tmp[0 + (i * 4)] = (a0 + a1) * 8; // 14b                      [-8160,8160]

                srcIdx += WebpConstants.Bps;
                refIdx += WebpConstants.Bps;
            }

            for (i = 0; i < 4; i++)
            {
                int t12 = tmp[12 + i]; // 15b
                int t8 = tmp[8 + i];

                int a1 = tmp[4 + i] + t8;
                int a2 = tmp[4 + i] - t8;
                int a0 = tmp[0 + i] + t12; // 15b
                int a3 = tmp[0 + i] - t12;

                output[12 + i] = (short)(((a3 * 2217) - (a2 * 5352) + 51000) >> 16);
                output[8 + i] = (short)((a0 - a1 + 7) >> 4);
                output[4 + i] = (short)((((a2 * 2217) + (a3 * 5352) + 12000) >> 16) + (a3 != 0 ? 1 : 0));
                output[0 + i] = (short)((a0 + a1 + 7) >> 4); // 12b
            }
        }
    }

#if NETCOREAPP3_0_OR_GREATER
    /// <summary>
    /// Forward transforms one source block against two predictors that sit side by side in <paramref name="reference"/>.
    /// Gives the same coefficients as <see cref="FTransform"/> on each predictor, as rows 0-1 and rows 2-3 of each block.
    /// </summary>
    public static void FTransformPair(Span<byte> src, Span<byte> reference, out Vector128<short> a0, out Vector128<short> a8, out Vector128<short> b0, out Vector128<short> b8)
    {
        _ = src[(3 * WebpConstants.Bps) + 3];
        _ = reference[(3 * WebpConstants.Bps) + 7];
        ref byte srcRef = ref MemoryMarshal.GetReference(src);
        ref byte referenceRef = ref MemoryMarshal.GetReference(reference);

        Vector128<short> diff0 = FTransformPairDiff(ref srcRef, ref referenceRef);
        Vector128<short> diff1 = FTransformPairDiff(ref Unsafe.Add(ref srcRef, WebpConstants.Bps), ref Unsafe.Add(ref referenceRef, WebpConstants.Bps));
        Vector128<short> diff2 = FTransformPairDiff(ref Unsafe.Add(ref srcRef, WebpConstants.Bps * 2), ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 2));
        Vector128<short> diff3 = FTransformPairDiff(ref Unsafe.Add(ref srcRef, WebpConstants.Bps * 3), ref Unsafe.Add(ref referenceRef, WebpConstants.Bps * 3));

        Vector128<int> shuf01l = Sse2.UnpackLow(diff0.AsInt32(), diff1.AsInt32());
        Vector128<int> shuf23l = Sse2.UnpackLow(diff2.AsInt32(), diff3.AsInt32());
        Vector128<int> shuf01h = Sse2.UnpackHigh(diff0.AsInt32(), diff1.AsInt32());
        Vector128<int> shuf23h = Sse2.UnpackHigh(diff2.AsInt32(), diff3.AsInt32());

        FTransformPass1SSE2(shuf01l.AsInt16(), shuf23l.AsInt16(), out Vector128<int> v01l, out Vector128<int> v32l);
        FTransformPass1SSE2(shuf01h.AsInt16(), shuf23h.AsInt16(), out Vector128<int> v01h, out Vector128<int> v32h);

        FTransformPass2SSE2(v01l, v32l, out a0, out a8);
        FTransformPass2SSE2(v01h, v32h, out b0, out b8);
    }

    // One row of four source samples minus the same row of each of the two predictors.
    [MethodImpl(InliningOptions.ShortMethod)]
    private static Vector128<short> FTransformPairDiff(ref byte srcRef, ref byte referenceRef)
    {
        Vector128<byte> src = Vector128.Create(Unsafe.ReadUnaligned<uint>(ref srcRef)).AsByte();
        Vector128<byte> reference = Vector128.CreateScalarUnsafe(Unsafe.ReadUnaligned<ulong>(ref referenceRef)).AsByte();
        return Sse2.Subtract(
            Sse2.UnpackLow(src, Vector128<byte>.Zero).AsInt16(),
            Sse2.UnpackLow(reference, Vector128<byte>.Zero).AsInt16());
    }

    public static void FTransformPass1SSE2(Vector128<short> row01, Vector128<short> row23, out Vector128<int> out01, out Vector128<int> out32)
    {
        // *in01 = 00 01 10 11 02 03 12 13
        // *in23 = 20 21 30 31 22 23 32 33
        Vector128<short> shuf01_p = Sse2.ShuffleHigh(row01, MmShuffle2301);
        Vector128<short> shuf32_p = Sse2.ShuffleHigh(row23, MmShuffle2301);

        // 00 01 10 11 03 02 13 12
        // 20 21 30 31 23 22 33 32
        Vector128<long> s01 = Sse2.UnpackLow(shuf01_p.AsInt64(), shuf32_p.AsInt64());
        Vector128<long> s32 = Sse2.UnpackHigh(shuf01_p.AsInt64(), shuf32_p.AsInt64());

        // 00 01 10 11 20 21 30 31
        // 03 02 13 12 23 22 33 32
        Vector128<short> a01 = Sse2.Add(s01.AsInt16(), s32.AsInt16());
        Vector128<short> a32 = Sse2.Subtract(s01.AsInt16(), s32.AsInt16());

        // [d0 + d3 | d1 + d2 | ...] = [a0 a1 | a0' a1' | ... ]
        // [d0 - d3 | d1 - d2 | ...] = [a3 a2 | a3' a2' | ... ]
        Vector128<int> tmp0 = Sse2.MultiplyAddAdjacent(a01, K88p); // [ (a0 + a1) << 3, ... ]
        Vector128<int> tmp2 = Sse2.MultiplyAddAdjacent(a01, K88m); // [ (a0 - a1) << 3, ... ]
        Vector128<int> tmp11 = Sse2.MultiplyAddAdjacent(a32, K5352_2217p);
        Vector128<int> tmp31 = Sse2.MultiplyAddAdjacent(a32, K5352_2217m);
        Vector128<int> tmp12 = Sse2.Add(tmp11, K1812);
        Vector128<int> tmp32 = Sse2.Add(tmp31, K937);
        Vector128<int> tmp1 = Sse2.ShiftRightArithmetic(tmp12, 9);
        Vector128<int> tmp3 = Sse2.ShiftRightArithmetic(tmp32, 9);
        Vector128<short> s03 = Sse2.PackSignedSaturate(tmp0, tmp2);
        Vector128<short> s12 = Sse2.PackSignedSaturate(tmp1, tmp3);
        Vector128<short> slo = Sse2.UnpackLow(s03, s12); // 0 1 0 1 0 1...
        Vector128<short> shi = Sse2.UnpackHigh(s03, s12); // 2 3 2 3 2 3
        Vector128<int> v23 = Sse2.UnpackHigh(slo.AsInt32(), shi.AsInt32());
        out01 = Sse2.UnpackLow(slo.AsInt32(), shi.AsInt32());
        out32 = Sse2.Shuffle(v23, MmShuffle1032);
    }

    public static void FTransformPass2SSE2(Vector128<int> v01, Vector128<int> v32, Span<short> output)
    {
        FTransformPass2SSE2(v01, v32, out Vector128<short> out0, out Vector128<short> out8);

        ref short outputRef = ref MemoryMarshal.GetReference(output);
        Unsafe.As<short, Vector128<short>>(ref outputRef) = out0;
        Unsafe.As<short, Vector128<short>>(ref Unsafe.Add(ref outputRef, 8)) = out8;
    }

    public static void FTransformPass2SSE2(Vector128<int> v01, Vector128<int> v32, out Vector128<short> out0, out Vector128<short> out8)
    {
        // Same operations are done on the (0,3) and (1,2) pairs.
        // a3 = v0 - v3
        // a2 = v1 - v2
        Vector128<short> a32 = Sse2.Subtract(v01.AsInt16(), v32.AsInt16());
        Vector128<long> a22 = Sse2.UnpackHigh(a32.AsInt64(), a32.AsInt64());

        Vector128<short> b23 = Sse2.UnpackLow(a22.AsInt16(), a32.AsInt16());
        Vector128<int> c1 = Sse2.MultiplyAddAdjacent(b23, K5352_2217);
        Vector128<int> c3 = Sse2.MultiplyAddAdjacent(b23, K2217_5352);
        Vector128<int> d1 = Sse2.Add(c1, K12000PlusOne);
        Vector128<int> d3 = Sse2.Add(c3, K51000);
        Vector128<int> e1 = Sse2.ShiftRightArithmetic(d1, 16);
        Vector128<int> e3 = Sse2.ShiftRightArithmetic(d3, 16);

        // f1 = ((b3 * 5352 + b2 * 2217 + 12000) >> 16)
        // f3 = ((b3 * 2217 - b2 * 5352 + 51000) >> 16)
        Vector128<short> f1 = Sse2.PackSignedSaturate(e1, e1);
        Vector128<short> f3 = Sse2.PackSignedSaturate(e3, e3);

        // g1 = f1 + (a3 != 0);
        // The compare will return (0xffff, 0) for (==0, !=0). To turn that into the
        // desired (0, 1), we add one earlier through k12000_plus_one.
        // -> g1 = f1 + 1 - (a3 == 0)
        Vector128<short> g1 = Sse2.Add(f1, Sse2.CompareEqual(a32, Vector128<short>.Zero));

        // a0 = v0 + v3
        // a1 = v1 + v2
        Vector128<short> a01 = Sse2.Add(v01.AsInt16(), v32.AsInt16());
        Vector128<short> a01Plus7 = Sse2.Add(a01.AsInt16(), Seven);
        Vector128<short> a11 = Sse2.UnpackHigh(a01.AsInt64(), a01.AsInt64()).AsInt16();
        Vector128<short> c0 = Sse2.Add(a01Plus7, a11);
        Vector128<short> c2 = Sse2.Subtract(a01Plus7, a11);

        // d0 = (a0 + a1 + 7) >> 4;
        // d2 = (a0 - a1 + 7) >> 4;
        Vector128<short> d0 = Sse2.ShiftRightArithmetic(c0, 4);
        Vector128<short> d2 = Sse2.ShiftRightArithmetic(c2, 4);

        out0 = Sse2.UnpackLow(d0.AsInt64(), g1.AsInt64()).AsInt16();
        out8 = Sse2.UnpackLow(d2.AsInt64(), f3.AsInt64()).AsInt16();
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static Vector256<int> LoadDifferenceRowAvx2(ref byte src, ref byte reference)
    {
        Vector256<short> src16 = Avx2.ConvertToVector256Int16(Unsafe.ReadUnaligned<Vector128<byte>>(ref src));
        Vector256<short> ref16 = Avx2.ConvertToVector256Int16(Unsafe.ReadUnaligned<Vector128<byte>>(ref reference));
        return Avx2.Subtract(src16, ref16).AsInt32();
    }

    // FTransformPass1SSE2 applied to each 128-bit lane.
    [MethodImpl(InliningOptions.ShortMethod)]
    private static void FTransformPass1Avx2(Vector256<short> row01, Vector256<short> row23, out Vector256<int> out01, out Vector256<int> out32)
    {
        Vector256<short> shuf01_p = Avx2.ShuffleHigh(row01, MmShuffle2301);
        Vector256<short> shuf32_p = Avx2.ShuffleHigh(row23, MmShuffle2301);

        Vector256<long> s01 = Avx2.UnpackLow(shuf01_p.AsInt64(), shuf32_p.AsInt64());
        Vector256<long> s32 = Avx2.UnpackHigh(shuf01_p.AsInt64(), shuf32_p.AsInt64());

        Vector256<short> a01 = Avx2.Add(s01.AsInt16(), s32.AsInt16());
        Vector256<short> a32 = Avx2.Subtract(s01.AsInt16(), s32.AsInt16());

        Vector256<int> tmp0 = Avx2.MultiplyAddAdjacent(a01, K88pVec256);
        Vector256<int> tmp2 = Avx2.MultiplyAddAdjacent(a01, K88mVec256);
        Vector256<int> tmp11 = Avx2.MultiplyAddAdjacent(a32, K5352_2217pVec256);
        Vector256<int> tmp31 = Avx2.MultiplyAddAdjacent(a32, K5352_2217mVec256);
        Vector256<int> tmp12 = Avx2.Add(tmp11, K1812Vec256);
        Vector256<int> tmp32 = Avx2.Add(tmp31, K937Vec256);
        Vector256<int> tmp1 = Avx2.ShiftRightArithmetic(tmp12, 9);
        Vector256<int> tmp3 = Avx2.ShiftRightArithmetic(tmp32, 9);
        Vector256<short> s03 = Avx2.PackSignedSaturate(tmp0, tmp2);
        Vector256<short> s12 = Avx2.PackSignedSaturate(tmp1, tmp3);
        Vector256<short> slo = Avx2.UnpackLow(s03, s12);
        Vector256<short> shi = Avx2.UnpackHigh(s03, s12);
        Vector256<int> v23 = Avx2.UnpackHigh(slo.AsInt32(), shi.AsInt32());
        out01 = Avx2.UnpackLow(slo.AsInt32(), shi.AsInt32());
        out32 = Avx2.Shuffle(v23, MmShuffle1032);
    }

    // FTransformPass2SSE2 applied to each 128-bit lane; returns the two halves each lane would store.
    [MethodImpl(InliningOptions.ShortMethod)]
    private static void FTransformPass2Avx2(Vector256<int> v01, Vector256<int> v32, out Vector256<short> d0g1, out Vector256<short> d2f3)
    {
        Vector256<short> a32 = Avx2.Subtract(v01.AsInt16(), v32.AsInt16());
        Vector256<long> a22 = Avx2.UnpackHigh(a32.AsInt64(), a32.AsInt64());

        Vector256<short> b23 = Avx2.UnpackLow(a22.AsInt16(), a32);
        Vector256<int> c1 = Avx2.MultiplyAddAdjacent(b23, K5352_2217Vec256);
        Vector256<int> c3 = Avx2.MultiplyAddAdjacent(b23, K2217_5352Vec256);
        Vector256<int> d1 = Avx2.Add(c1, K12000PlusOneVec256);
        Vector256<int> d3 = Avx2.Add(c3, K51000Vec256);
        Vector256<int> e1 = Avx2.ShiftRightArithmetic(d1, 16);
        Vector256<int> e3 = Avx2.ShiftRightArithmetic(d3, 16);

        Vector256<short> f1 = Avx2.PackSignedSaturate(e1, e1);
        Vector256<short> f3 = Avx2.PackSignedSaturate(e3, e3);

        Vector256<short> g1 = Avx2.Add(f1, Avx2.CompareEqual(a32, Vector256<short>.Zero));

        Vector256<short> a01 = Avx2.Add(v01.AsInt16(), v32.AsInt16());
        Vector256<short> a01Plus7 = Avx2.Add(a01, SevenVec256);
        Vector256<short> a11 = Avx2.UnpackHigh(a01.AsInt64(), a01.AsInt64()).AsInt16();
        Vector256<short> c0 = Avx2.Add(a01Plus7, a11);
        Vector256<short> c2 = Avx2.Subtract(a01Plus7, a11);

        Vector256<short> d0 = Avx2.ShiftRightArithmetic(c0, 4);
        Vector256<short> d2 = Avx2.ShiftRightArithmetic(c2, 4);

        d0g1 = Avx2.UnpackLow(d0.AsInt64(), g1.AsInt64()).AsInt16();
        d2f3 = Avx2.UnpackLow(d2.AsInt64(), f3.AsInt64()).AsInt16();
    }
#endif

    public static void FTransformWht(Span<short> input, Span<short> output, Span<int> scratch)
    {
        Span<int> tmp = scratch.Slice(0, 16);

        int i;
        int inputIdx = 0;
        for (i = 0; i < 4; i++)
        {
            int a1 = input[inputIdx + (1 * 16)] + input[inputIdx + (3 * 16)];
            int a2 = input[inputIdx + (1 * 16)] - input[inputIdx + (3 * 16)];
            int a0 = input[inputIdx + (0 * 16)] + input[inputIdx + (2 * 16)];  // 13b
            int a3 = input[inputIdx + (0 * 16)] - input[inputIdx + (2 * 16)];
            tmp[3 + (i * 4)] = a0 - a1;
            tmp[2 + (i * 4)] = a3 - a2;
            tmp[1 + (i * 4)] = a3 + a2;
            tmp[0 + (i * 4)] = a0 + a1;   // 14b

            inputIdx += 64;
        }

        for (i = 0; i < 4; i++)
        {
            int t12 = tmp[12 + i];
            int t8 = tmp[8 + i];

            int a1 = tmp[4 + i] + t12;
            int a2 = tmp[4 + i] - t12;
            int a0 = tmp[0 + i] + t8;  // 15b
            int a3 = tmp[0 + i] - t8;

            int b0 = a0 + a1;    // 16b
            int b1 = a3 + a2;
            int b2 = a3 - a2;
            int b3 = a0 - a1;

            output[12 + i] = (short)(b3 >> 1);
            output[8 + i] = (short)(b2 >> 1);
            output[4 + i] = (short)(b1 >> 1);
            output[0 + i] = (short)(b0 >> 1);     // 15b
        }
    }

    // luma 16x16 prediction (paragraph 12.3).
    public static void EncPredLuma16(Span<byte> dst, Span<byte> left, Span<byte> top)
    {
        DcMode(dst.Slice(I16DC16), left, top, 16, 16, 5);
        VerticalPred(dst.Slice(I16VE16), top, 16);
        HorizontalPred(dst.Slice(I16HE16), left, 16);
        TrueMotion(dst.Slice(I16TM16), left, top, 16);
    }

    // Chroma 8x8 prediction (paragraph 12.2).
    public static void EncPredChroma8(Span<byte> dst, Span<byte> left, Span<byte> top)
    {
        // U block.
        DcMode(dst.Slice(C8DC8), left, top, 8, 8, 4);
        VerticalPred(dst.Slice(C8VE8), top, 8);
        HorizontalPred(dst.Slice(C8HE8), left, 8);
        TrueMotion(dst.Slice(C8TM8), left, top, 8);

        // V block.
        dst = dst.Slice(8);
        if (!top.IsEmpty)
        {
            top = top.Slice(8);
        }

        if (!left.IsEmpty)
        {
            left = left.Slice(16);
        }

        DcMode(dst.Slice(C8DC8), left, top, 8, 8, 4);
        VerticalPred(dst.Slice(C8VE8), top, 8);
        HorizontalPred(dst.Slice(C8HE8), left, 8);
        TrueMotion(dst.Slice(C8TM8), left, top, 8);
    }

    // The 13 edge samples sit contiguously in top[topOffset - 5 .. topOffset + 7]:
    // left L K J I (bottom to top), top-left X, top A B C D, top-right E F G H.
    // Every predictor pixel is one of: an edge sample, Avg2 of two neighbours, or Avg3 centred on a
    // sample (Avg2 and Avg3 are symmetric, so each value is computed once and shared).
    public static void EncPredLuma4(Span<byte> dst, Span<byte> top, int topOffset)
    {
        ReadOnlySpan<byte> edge = top.Slice(topOffset - 5, 13);
        byte h = edge[12];
        byte g = edge[11];
        byte f = edge[10];
        byte e = edge[9];
        byte d = edge[8];
        byte c = edge[7];
        byte b = edge[6];
        byte a = edge[5];
        byte x = edge[4];
        byte i = edge[3];
        byte j = edge[2];
        byte k = edge[1];
        byte l = edge[0];

        byte l3 = LossyUtils.Avg3(k, l, l);
        byte k3 = LossyUtils.Avg3(j, k, l);
        byte j3 = LossyUtils.Avg3(i, j, k);
        byte i3 = LossyUtils.Avg3(x, i, j);
        byte x3 = LossyUtils.Avg3(a, x, i);
        byte a3 = LossyUtils.Avg3(b, a, x);
        byte b3 = LossyUtils.Avg3(c, b, a);
        byte c3 = LossyUtils.Avg3(d, c, b);
        byte d3 = LossyUtils.Avg3(e, d, c);
        byte e3 = LossyUtils.Avg3(f, e, d);
        byte f3 = LossyUtils.Avg3(g, f, e);
        byte g3 = LossyUtils.Avg3(h, g, f);
        byte h3 = LossyUtils.Avg3(h, h, g);

        byte lk = LossyUtils.Avg2(l, k);
        byte kj = LossyUtils.Avg2(k, j);
        byte ji = LossyUtils.Avg2(j, i);
        byte ix = LossyUtils.Avg2(i, x);
        byte xa = LossyUtils.Avg2(x, a);
        byte ab = LossyUtils.Avg2(a, b);
        byte bc = LossyUtils.Avg2(b, c);
        byte cd = LossyUtils.Avg2(c, d);
        byte de = LossyUtils.Avg2(d, e);

        ref byte p = ref MemoryMarshal.GetReference(dst.Slice(0, I4HU4 + (3 * WebpConstants.Bps) + 4));

        uint dc = 0x01010101U * (uint)((a + b + c + d + l + k + j + i + 4) >> 3);
        Put4(ref p, I4DC4, dc, dc, dc, dc);

        Put4(
            ref p,
            I4TM4,
            Pack(Clip8b(i + a - x), Clip8b(i + b - x), Clip8b(i + c - x), Clip8b(i + d - x)),
            Pack(Clip8b(j + a - x), Clip8b(j + b - x), Clip8b(j + c - x), Clip8b(j + d - x)),
            Pack(Clip8b(k + a - x), Clip8b(k + b - x), Clip8b(k + c - x), Clip8b(k + d - x)),
            Pack(Clip8b(l + a - x), Clip8b(l + b - x), Clip8b(l + c - x), Clip8b(l + d - x)));

        uint ve = Pack(a3, b3, c3, d3);
        Put4(ref p, I4VE4, ve, ve, ve, ve);

        Put4(ref p, I4HE4, 0x01010101U * i3, 0x01010101U * j3, 0x01010101U * k3, 0x01010101U * l3);
        Put4(ref p, I4RD4, Pack(x3, a3, b3, c3), Pack(i3, x3, a3, b3), Pack(j3, i3, x3, a3), Pack(k3, j3, i3, x3));
        Put4(ref p, I4VR4, Pack(xa, ab, bc, cd), Pack(x3, a3, b3, c3), Pack(i3, xa, ab, bc), Pack(j3, x3, a3, b3));
        Put4(ref p, I4LD4, Pack(b3, c3, d3, e3), Pack(c3, d3, e3, f3), Pack(d3, e3, f3, g3), Pack(e3, f3, g3, h3));
        Put4(ref p, I4VL4, Pack(ab, bc, cd, de), Pack(b3, c3, d3, e3), Pack(bc, cd, de, f3), Pack(c3, d3, e3, g3));
        Put4(ref p, I4HD4, Pack(ix, x3, a3, b3), Pack(ji, i3, ix, x3), Pack(kj, j3, ji, i3), Pack(lk, k3, kj, j3));
        Put4(ref p, I4HU4, Pack(ji, j3, kj, k3), Pack(kj, k3, lk, l3), Pack(lk, l3, l, l), 0x01010101U * l);
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static uint Pack(byte p0, byte p1, byte p2, byte p3) => p0 | ((uint)p1 << 8) | ((uint)p2 << 16) | ((uint)p3 << 24);

    [MethodImpl(InliningOptions.ShortMethod)]
    private static void Put4(ref byte dst, int offset, uint row0, uint row1, uint row2, uint row3)
    {
        ref byte block = ref Unsafe.Add(ref dst, offset);
        PutRow(ref block, row0);
        PutRow(ref Unsafe.Add(ref block, WebpConstants.Bps), row1);
        PutRow(ref Unsafe.Add(ref block, 2 * WebpConstants.Bps), row2);
        PutRow(ref Unsafe.Add(ref block, 3 * WebpConstants.Bps), row3);
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static void PutRow(ref byte dst, uint row) =>
        Unsafe.WriteUnaligned(ref dst, BitConverter.IsLittleEndian ? row : BinaryPrimitives.ReverseEndianness(row));

    private static void VerticalPred(Span<byte> dst, Span<byte> top, int size)
    {
        if (!top.IsEmpty)
        {
            for (int j = 0; j < size; j++)
            {
                top.Slice(0, size).CopyTo(dst.Slice(j * WebpConstants.Bps));
            }
        }
        else
        {
            Fill(dst, 127, size);
        }
    }

    public static void HorizontalPred(Span<byte> dst, Span<byte> left, int size)
    {
        if (!left.IsEmpty)
        {
            left = left.Slice(1); // in the reference implementation, left starts at - 1.
            for (int j = 0; j < size; j++)
            {
                dst.Slice(j * WebpConstants.Bps, size).Fill(left[j]);
            }
        }
        else
        {
            Fill(dst, 129, size);
        }
    }

    public static void TrueMotion(Span<byte> dst, Span<byte> left, Span<byte> top, int size)
    {
        if (!left.IsEmpty)
        {
            if (!top.IsEmpty)
            {
                Span<byte> clip = Clip1.AsSpan(255 - left[0]); // left [0] instead of left[-1], original left starts at -1
                for (int y = 0; y < size; y++)
                {
                    Span<byte> clipTable = clip.Slice(left[y + 1]); // left[y]
                    for (int x = 0; x < size; x++)
                    {
                        dst[x] = clipTable[top[x]];
                    }

                    dst = dst.Slice(WebpConstants.Bps);
                }
            }
            else
            {
                HorizontalPred(dst, left, size);
            }
        }
        else
        {
            // true motion without left samples (hence: with default 129 value)
            // is equivalent to VE prediction where you just copy the top samples.
            // Note that if top samples are not available, the default value is
            // then 129, and not 127 as in the VerticalPred case.
            if (!top.IsEmpty)
            {
                VerticalPred(dst, top, size);
            }
            else
            {
                Fill(dst, 129, size);
            }
        }
    }

    private static void DcMode(Span<byte> dst, Span<byte> left, Span<byte> top, int size, int round, int shift)
    {
        int dc = 0;
        int j;
        if (!top.IsEmpty)
        {
            for (j = 0; j < size; j++)
            {
                dc += top[j];
            }

            if (!left.IsEmpty)
            {
                // top and left present.
                left = left.Slice(1); // in the reference implementation, left starts at -1.
                for (j = 0; j < size; j++)
                {
                    dc += left[j];
                }
            }
            else
            {
                // top, but no left.
                dc += dc;
            }

            dc = (dc + round) >> shift;
        }
        else if (!left.IsEmpty)
        {
            // left but no top.
            left = left.Slice(1); // in the reference implementation, left starts at -1.
            for (j = 0; j < size; j++)
            {
                dc += left[j];
            }

            dc += dc;
            dc = (dc + round) >> shift;
        }
        else
        {
            // no top, no left, nothing.
            dc = 0x80;
        }

        Fill(dst, dc, size);
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static void Fill(Span<byte> dst, int value, int size)
    {
        for (int j = 0; j < size; j++)
        {
            dst.Slice(j * WebpConstants.Bps, size).Fill((byte)value);
        }
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static byte Clip8b(int v) => (v & ~0xff) == 0 ? (byte)v : v < 0 ? (byte)0 : (byte)255;

    [MethodImpl(InliningOptions.ShortMethod)]
    private static void Store(Span<byte> dst, Span<byte> reference, int x, int y, int v) => dst[x + (y * WebpConstants.Bps)] = LossyUtils.Clip8B(reference[x + (y * WebpConstants.Bps)] + (v >> 3));

    [MethodImpl(InliningOptions.ShortMethod)]
    private static int Mul(int a, int b) => (a * b) >> 16;
}
