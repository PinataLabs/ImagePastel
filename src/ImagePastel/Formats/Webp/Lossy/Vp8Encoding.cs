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
            ref byte outputRef = ref MemoryMarshal.GetReference(dst);
            Unsafe.As<byte, Vector64<byte>>(ref outputRef) = ref0.GetLower();
            Unsafe.As<byte, Vector64<byte>>(ref Unsafe.Add(ref outputRef, WebpConstants.Bps)) = ref1.GetLower();
            Unsafe.As<byte, Vector64<byte>>(ref Unsafe.Add(ref outputRef, WebpConstants.Bps * 2)) = ref2.GetLower();
            Unsafe.As<byte, Vector64<byte>>(ref Unsafe.Add(ref outputRef, WebpConstants.Bps * 3)) = ref3.GetLower();
        }
        else
#endif
        {
            ITransformOne(reference, input, dst, scratch);
            ITransformOne(reference.Slice(4), input.Slice(16), dst.Slice(4), scratch);
        }
    }

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

        Vector128<long> d0g1 = Sse2.UnpackLow(d0.AsInt64(), g1.AsInt64());
        Vector128<long> d2f3 = Sse2.UnpackLow(d2.AsInt64(), f3.AsInt64());

        ref short outputRef = ref MemoryMarshal.GetReference(output);
        Unsafe.As<short, Vector128<short>>(ref outputRef) = d0g1.AsInt16();
        Unsafe.As<short, Vector128<short>>(ref Unsafe.Add(ref outputRef, 8)) = d2f3.AsInt16();
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
