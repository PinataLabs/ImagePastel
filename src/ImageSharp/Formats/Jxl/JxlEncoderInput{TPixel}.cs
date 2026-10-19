// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_modular.cc (float_to_int and the channel copy of ModularFrameEncoder::ComputeEncodingData),
// lib/jxl/enc_external_image.cc (the integer scale of ConvertFromExternalPlaneNoSizeCheck) and
// lib/jxl/encode.cc (CheckValidBitdepth).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// The pixels the encoder takes: which channels and bit depth a pixel type gives, and the copy of
/// its samples into Modular channels. libjxl loads the caller's buffer into float planes, scaled to
/// [0, 1] for integer buffers (<c>ConvertFromExternalPlaneNoSizeCheck</c>), and converts them back to
/// integers at the stored depth (<c>float_to_int</c> in <c>enc_modular.cc</c>). This port does the same
/// one row at a time, and skips the float step where the stored depth is the depth of the pixel type,
/// which gives the same integers.
/// </summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
internal static class JxlEncoderInput<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private static readonly SampleKind Kind = GetSampleKind();

    // How the samples of a pixel type are read: as 8-bit or 16-bit integers through La32 (grey) or
    // Rgba64 (colour), or as floats through Vector4.
    private enum SampleKind
    {
        Integer8,
        Integer16,
        Float,
    }

    /// <summary>
    /// Gets a value indicating whether the pixel type is greyscale, which gives one colour channel
    /// and a grey colour encoding.
    /// </summary>
    public static bool IsGray { get; } = typeof(TPixel) == typeof(L8) || typeof(TPixel) == typeof(L16) || typeof(TPixel) == typeof(La16) || typeof(TPixel) == typeof(La32);

    /// <summary>
    /// Gets a value indicating whether the pixel type has alpha, which gives one straight alpha extra
    /// channel at the colour depth.
    /// </summary>
    public static bool HasAlpha { get; } = GetHasAlpha();

    /// <summary>
    /// Gets the bits per sample the pixel type holds: 8 for 8-bit types, 32 for float types, and 16 for
    /// 16-bit types and every other type.
    /// </summary>
    public static uint BitsPerSample => Kind switch
    {
        SampleKind.Integer8 => 8,
        SampleKind.Float => 32,
        _ => 16,
    };

    /// <summary>Gets the exponent bits per sample the pixel type holds: 8 for float types, otherwise 0.</summary>
    public static uint ExponentBitsPerSample => Kind == SampleKind.Float ? 8u : 0u;

    /// <summary>
    /// Checks that a bit depth can be written, as libjxl's <c>CheckValidBitdepth</c>: 1 to 24 bits for
    /// integer samples, and for floats at most 8 exponent bits and between 3 and 24 mantissa bits.
    /// </summary>
    /// <param name="bits">The bits per sample.</param>
    /// <param name="exponentBits">The exponent bits per sample.</param>
    public static void CheckValidBitdepth(uint bits, uint exponentBits)
    {
        if (exponentBits == 0)
        {
            if (bits is < 1 or > 24)
            {
                JxlThrowHelper.ThrowImageFormatException($"Invalid JPEG XL bit depth: {bits} bits per integer sample.");
            }
        }
        else if (exponentBits > 8 || bits < 3 + exponentBits || bits > 24 + exponentBits)
        {
            JxlThrowHelper.ThrowImageFormatException($"Invalid JPEG XL bit depth: {bits} bits with {exponentBits} exponent bits per float sample.");
        }
    }

    /// <summary>
    /// Copies the colour samples of <paramref name="image"/> into the first channels of
    /// <paramref name="gi"/>, one for grey and three for RGB, at the depth <paramref name="bitDepth"/>.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="bitDepth">The stored depth of the colour samples.</param>
    /// <param name="gi">The Modular image, with full-size channels.</param>
    public static void CopyColorChannels(Image<TPixel> image, JxlBitDepth bitDepth, JxlModularImage gi)
    {
        int colorChannels = IsGray ? 1 : 3;
        CopyChannels(image, 0, colorChannels, bitDepth, gi, 0);
    }

    /// <summary>
    /// Copies the alpha samples of <paramref name="image"/> into channel <paramref name="c"/> of
    /// <paramref name="gi"/>, at the depth of <paramref name="eci"/>. It is libjxl's extra channel loop
    /// for the one extra channel a pixel type can have.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="eci">The alpha channel's description.</param>
    /// <param name="gi">The Modular image, with full-size channels.</param>
    /// <param name="c">The index of the alpha channel in <paramref name="gi"/>.</param>
    public static void CopyAlphaChannel(Image<TPixel> image, JxlExtraChannelInfo eci, JxlModularImage gi, int c)
        => CopyChannels(image, 3, 1, eci.BitDepth, gi, c);

    /// <summary>
    /// Converts floats to stored samples of <paramref name="bits"/> bits, as libjxl's <c>float_to_int</c>:
    /// integers are scaled by <paramref name="dfactor"/> and rounded half away from zero; float32 keeps its
    /// bit pattern; other float layouts are repacked, and a value they cannot hold exactly is an error.
    /// </summary>
    /// <param name="rowIn">The floats.</param>
    /// <param name="rowOut">The stored samples.</param>
    /// <param name="bits">The bits per sample.</param>
    /// <param name="expBits">The exponent bits per sample.</param>
    /// <param name="fp">Whether the samples are floats.</param>
    /// <param name="dfactor">The largest integer sample, for integer samples.</param>
    internal static void FloatToInt(ReadOnlySpan<float> rowIn, Span<int> rowOut, int bits, int expBits, bool fp, double dfactor)
    {
        if (!fp)
        {
            if (bits > 22)
            {
                for (int x = 0; x < rowIn.Length; ++x)
                {
                    rowOut[x] = (int)((rowIn[x] * dfactor) + (rowIn[x] < 0 ? -0.5 : 0.5));
                }
            }
            else
            {
                float factor = (float)dfactor;
                for (int x = 0; x < rowIn.Length; ++x)
                {
                    rowOut[x] = (int)((rowIn[x] * factor) + (rowIn[x] < 0 ? -0.5f : 0.5f));
                }
            }

            return;
        }

        if (bits == 32)
        {
            DebugGuard.IsTrue(expBits == 8, "A 32-bit float has 8 exponent bits");
            for (int x = 0; x < rowIn.Length; ++x)
            {
                rowOut[x] = BitConverter.SingleToInt32Bits(rowIn[x]);
            }

            return;
        }

        int expBias = (1 << (expBits - 1)) - 1;
        int maxExp = (1 << expBits) - 1;
        uint sign = 1u << (bits - 1);
        int mantBits = bits - expBits - 1;
        int mantShift = 23 - mantBits;
        for (int x = 0; x < rowIn.Length; ++x)
        {
            uint f = unchecked((uint)BitConverter.SingleToInt32Bits(rowIn[x]));
            int signbit = (int)(f >> 31);
            f &= 0x7fffffff;
            if (f == 0)
            {
                rowOut[x] = (int)(signbit != 0 ? sign : 0);
                continue;
            }

            int exp = (int)(f >> 23) - 127;
            int mantissa = (int)(f & 0x007fffff);

            // broke up the binary32 into its parts, now reassemble into arbitrary float
            if (exp == 128)
            {
                // NaN or infinity
                f = signbit != 0 ? sign : 0;
                f |= (uint)((1 << expBits) - 1) << mantBits;
                f |= (uint)mantissa >> mantShift;
                rowOut[x] = unchecked((int)f);
                continue;
            }

            exp += expBias;
            if (exp <= 0)
            {
                // will become a subnormal number
                // add implicit leading 1 to mantissa
                mantissa |= 0x00800000;
                if (exp < -mantBits)
                {
                    JxlThrowHelper.ThrowImageFormatException($"Invalid float number: {rowIn[x]} cannot be represented with {expBits} exp_bits and {mantBits} mant_bits (exp {exp})");
                }

                mantissa >>= 1 - exp;
                exp = 0;
            }

            // exp should be representable in exp_bits, otherwise input was invalid; max_exp is NaN or
            // infinity
            if (exp >= maxExp)
            {
                JxlThrowHelper.ThrowImageFormatException("Invalid float exponent");
            }

            if ((mantissa & ((1 << mantShift) - 1)) != 0)
            {
                JxlThrowHelper.ThrowImageFormatException($"{rowIn[x]} is losing precision (mant: {mantissa:x})");
            }

            mantissa >>= mantShift;
            f = signbit != 0 ? sign : 0;
            f |= (uint)exp << mantBits;
            f |= (uint)mantissa;
            rowOut[x] = unchecked((int)f);
        }
    }

    // Copies `count` components of each pixel, from component `first` (R, G, B, A; grey is R and its
    // alpha A), into the channels of gi from `c`.
    private static void CopyChannels(Image<TPixel> image, int first, int count, JxlBitDepth bitDepth, JxlModularImage gi, int c)
    {
        Configuration configuration = image.GetConfiguration();
        MemoryAllocator allocator = configuration.MemoryAllocator;
        Buffer2D<TPixel> pixels = image.Frames.RootFrame.PixelBuffer;
        int width = image.Width;
        int bits = (int)bitDepth.BitsPerSample;
        int expBits = (int)bitDepth.ExponentBitsPerSample;
        bool fp = bitDepth.FloatingPointSample;
        double factor = fp ? 1 : (1u << bits) - 1;

        // The integer samples go through float only when the stored depth is not the depth they have.
        bool copyIntegers = Kind != SampleKind.Float && !fp && bits == BitsPerSample;
        float scale = Kind == SampleKind.Integer8 ? 1F / 255 : 1F / 65535;

        using IMemoryOwner<Vector4> vectorsOwner = allocator.Allocate<Vector4>(width);
        using IMemoryOwner<La32> greyOwner = allocator.Allocate<La32>(width);
        using IMemoryOwner<Rgba64> colorOwner = allocator.Allocate<Rgba64>(width);
        using IMemoryOwner<int> samplesOwner = allocator.Allocate<int>(width);
        using IMemoryOwner<float> floatsOwner = allocator.Allocate<float>(width);
        Span<Vector4> vectors = vectorsOwner.Memory.Span.Slice(0, width);
        Span<La32> grey = greyOwner.Memory.Span.Slice(0, width);
        Span<Rgba64> color = colorOwner.Memory.Span.Slice(0, width);
        Span<int> samples = samplesOwner.Memory.Span.Slice(0, width);
        Span<float> floats = floatsOwner.Memory.Span.Slice(0, width);
        for (int y = 0; y < image.Height; y++)
        {
            Span<TPixel> row = pixels.DangerousGetRowSpan(y);
            if (Kind == SampleKind.Float)
            {
                PixelOperations<TPixel>.Instance.ToVector4(configuration, row, vectors, PixelConversionModifiers.None);
            }
            else if (IsGray)
            {
                PixelOperations<TPixel>.Instance.To(configuration, row, grey);
            }
            else
            {
                PixelOperations<TPixel>.Instance.To(configuration, row, color);
            }

            for (int i = 0; i < count; i++)
            {
                int component = first + i;
                Span<int> output = gi.Channel[c + i].Row(y);
                if (Kind == SampleKind.Float)
                {
                    for (int x = 0; x < width; x++)
                    {
                        floats[x] = GetComponent(vectors[x], component);
                    }

                    if (!fp)
                    {
                        // Departure (ADR 0001): libjxl converts floats outside [0, 1] to integers outside the
                        // stored range, and a float too large for an int has no defined conversion in
                        // C#. The decoder clamps to [0, 1], so clamping first gives the same pixels.
                        for (int x = 0; x < width; x++)
                        {
                            floats[x] = Math.Clamp(floats[x], 0F, 1F);
                        }
                    }

                    FloatToInt(floats, output, bits, expBits, fp, factor);
                    continue;
                }

                for (int x = 0; x < width; x++)
                {
                    int v16 = IsGray ? (component == 3 ? grey[x].A : grey[x].L) : GetComponent(color[x], component);
                    samples[x] = Kind == SampleKind.Integer8 ? v16 / 257 : v16;
                }

                if (copyIntegers)
                {
                    samples.CopyTo(output);
                    continue;
                }

                for (int x = 0; x < width; x++)
                {
                    floats[x] = samples[x] * scale;
                }

                FloatToInt(floats, output, bits, expBits, fp, factor);
            }
        }
    }

    private static float GetComponent(Vector4 v, int component) => component switch
    {
        0 => v.X,
        1 => v.Y,
        2 => v.Z,
        _ => v.W,
    };

    private static ushort GetComponent(Rgba64 v, int component) => component switch
    {
        0 => v.R,
        1 => v.G,
        2 => v.B,
        _ => v.A,
    };

    private static SampleKind GetSampleKind()
    {
        Type type = typeof(TPixel);
        if (type == typeof(L8) || type == typeof(La16) || type == typeof(Rgb24) || type == typeof(Bgr24)
            || type == typeof(Rgba32) || type == typeof(Bgra32) || type == typeof(Argb32) || type == typeof(Abgr32))
        {
            return SampleKind.Integer8;
        }

        if (type == typeof(RgbaVector))
        {
            return SampleKind.Float;
        }

        // L16, La32, Rgb48 and Rgba64, and every other pixel type: no other type has more than 16 bits
        // per integer component, so 16 bits keep it.
        return SampleKind.Integer16;
    }

    private static bool GetHasAlpha()
    {
        Type type = typeof(TPixel);
        if (type == typeof(La16) || type == typeof(La32) || type == typeof(Rgba32) || type == typeof(Bgra32)
            || type == typeof(Argb32) || type == typeof(Abgr32) || type == typeof(Rgba64) || type == typeof(RgbaVector))
        {
            return true;
        }

        if (type == typeof(L8) || type == typeof(L16) || type == typeof(Rgb24) || type == typeof(Bgr24) || type == typeof(Rgb48))
        {
            return false;
        }

        // Another pixel type keeps an alpha channel unless it says it has none.
        PixelAlphaRepresentation? alpha = PixelOperations<TPixel>.Instance.GetPixelTypeInfo()?.AlphaRepresentation;
        return alpha != PixelAlphaRepresentation.None;
    }
}
