// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_modular.cc (float_to_int and the channel copy of ModularFrameEncoder::ComputeEncodingData),
// lib/jxl/enc_external_image.cc (ConvertFromExternalPlaneNoSizeCheck: integers scaled, floats as they are),
// lib/jxl/enc_frame.cc (CopyColorChannels) and
// lib/jxl/encode.cc (CheckValidBitdepth).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers;
using System.Numerics;
using PinataLabs.ImagePastel.Advanced;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Formats.Jxl.Modular;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Jxl;

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
    // The sample layout of the pixel type, from the table in GetLayout.
    private static readonly PixelLayout Layout = GetLayout();

    // How the samples of a pixel type are read and stored. Integers are read through La32 (grey) or
    // Rgba64 (colour) and stored at 8 or 16 bits. A half is read through ToVector4 and stored as f16
    // (16 bits, 5 exponent bits), which holds it exactly. A float is read through ToVector4 and stored
    // as float32. A signed sample, or one outside [0, 1], is read through its scaled vector (the mapping
    // that ImageSharp's other encoders and the decoder use), which float32 holds exactly but 16-bit
    // integers clamp, and stored as float32.
    private enum SampleKind
    {
        Integer8,
        Integer16,
        Half,
        Float,
        ScaledFloat,
    }

    /// <summary>
    /// Gets a value indicating whether the pixel type is greyscale, which gives one colour channel
    /// and a grey colour encoding.
    /// </summary>
    public static bool IsGray => Layout.IsGray;

    /// <summary>
    /// Gets a value indicating whether the pixel type has alpha, which gives one straight alpha extra
    /// channel at the colour depth.
    /// </summary>
    public static bool HasAlpha => Layout.HasAlpha;

    /// <summary>
    /// Checks that a bit depth can be written, as libjxl's <c>CheckValidBitdepth</c>: 1 to 24 bits for
    /// integer samples, and for floats at most 8 exponent bits and between 3 and 24 mantissa bits.
    /// </summary>
    /// <param name="bitDepth">The bit depth.</param>
    public static void CheckValidBitdepth(JxlBitDepth bitDepth)
    {
        uint bits = bitDepth.BitsPerSample;
        uint exponentBits = bitDepth.ExponentBitsPerSample;
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
    /// Chooses the stored depth of the samples of <paramref name="image"/>. A requested depth (from
    /// <see cref="JxlMetadata"/>, as cjxl keeps the depth of the file it reads) must be valid, and is the
    /// stored depth when it holds every sample losslessly: an integer depth holds integer samples that
    /// each convert to it and back unchanged, as those of a file decoded at that depth do, and a float
    /// layout holds float samples whose own layout has no more exponent or mantissa bits. Otherwise the
    /// stored depth is the depth of the pixel type: 8 or 16 bits for integer types, f16 for halves, and
    /// float32 for other float and signed types.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="requested">The requested depth, or <see langword="null"/> when none is set.</param>
    /// <returns>The stored depth.</returns>
    public static JxlBitDepth ChooseBitDepth(Image<TPixel> image, JxlBitDepth requested)
    {
        if (requested is not null)
        {
            CheckValidBitdepth(requested);
            if (HoldsSamples(image, requested))
            {
                return requested;
            }
        }

        return CreateBitDepth(Layout.BitsPerSample, Layout.ExponentBitsPerSample);
    }

    /// <summary>Creates a bit depth.</summary>
    /// <param name="bits">The bits per sample.</param>
    /// <param name="exponentBits">The exponent bits per sample, 0 for integers.</param>
    /// <returns>The bit depth.</returns>
    public static JxlBitDepth CreateBitDepth(uint bits, uint exponentBits) => new()
    {
        BitsPerSample = bits,
        ExponentBitsPerSample = exponentBits,
        FloatingPointSample = exponentBits != 0,
    };

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
    /// Copies the colour samples of <paramref name="image"/> into the top-left of three float planes, as
    /// libjxl's <c>CopyColorChannels</c> loads the caller's buffer for a lossy frame
    /// (<c>ConvertFromExternalPlaneNoSizeCheck</c>): an integer sample is scaled to [0, 1] by the depth of
    /// the pixel format, times 1 / (2^bits - 1), a multiply by the reciprocal; a float sample is taken as
    /// it is, outside [0, 1] too. A grey sample goes into all three planes.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="planes">The three planes, at least the size of the image.</param>
    public static void CopyColorPlanes(Image<TPixel> image, Buffer2D<float>[] planes)
    {
        using RowReader reader = new(image);
        for (int y = 0; y < image.Height; y++)
        {
            reader.Read(y);
            for (int c = 0; c < 3; c++)
            {
                Span<float> output = planes[c].DangerousGetRowSpan(y).Slice(0, image.Width);
                if (IsGray && c > 0)
                {
                    planes[0].DangerousGetRowSpan(y).Slice(0, image.Width).CopyTo(output);
                    continue;
                }

                ReadPlaneRow(reader, c, output);
            }
        }
    }

    /// <summary>
    /// Copies the alpha samples of <paramref name="image"/> into the top-left of a float plane, loaded as
    /// <see cref="CopyColorPlanes"/> loads the colour: libjxl's interleaved alpha of
    /// <c>CopyColorChannels</c>, which a lossy frame reads to simplify the invisible pixels. Float alpha is
    /// not rescaled, so a negative alpha is neither 0 nor visible to <c>SimplifyInvisible</c>.
    /// </summary>
    /// <param name="image">The image, of a pixel type with alpha.</param>
    /// <param name="plane">The plane, at least the size of the image.</param>
    public static void CopyAlphaPlane(Image<TPixel> image, Buffer2D<float> plane)
    {
        using RowReader reader = new(image);
        for (int y = 0; y < image.Height; y++)
        {
            reader.Read(y);
            ReadPlaneRow(reader, 3, plane.DangerousGetRowSpan(y).Slice(0, image.Width));
        }
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

    // One component of the row read last, as libjxl loads it into a float plane: integers scaled to
    // [0, 1] by the depth of the pixel type, floats as they are. A half is read exactly; a signed or
    // out-of-range sample is read through its scaled vector, the floats that lossless stores.
    private static void ReadPlaneRow(RowReader reader, int component, Span<float> output)
    {
        if (Layout.ExponentBitsPerSample != 0)
        {
            reader.GetFloats(component, output);
            return;
        }

        float scale = Layout.Kind == SampleKind.Integer8 ? 1F / 255 : 1F / 65535;
        Span<int> samples = reader.Samples;
        reader.GetIntegers(component, samples);
        for (int x = 0; x < samples.Length; x++)
        {
            output[x] = samples[x] * scale;
        }
    }

    // Whether the stored depth `bitDepth` holds every sample of the image losslessly (see ChooseBitDepth).
    private static bool HoldsSamples(Image<TPixel> image, JxlBitDepth bitDepth)
    {
        bool integerSamples = Layout.ExponentBitsPerSample == 0;
        if (bitDepth.FloatingPointSample)
        {
            uint mantissaBits = bitDepth.BitsPerSample - bitDepth.ExponentBitsPerSample - 1;
            uint ownMantissaBits = Layout.BitsPerSample - Layout.ExponentBitsPerSample - 1;
            return !integerSamples && bitDepth.ExponentBitsPerSample >= Layout.ExponentBitsPerSample && mantissaBits >= ownMantissaBits;
        }

        if (!integerSamples)
        {
            return false;
        }

        int bits = (int)bitDepth.BitsPerSample;
        if (bits >= Layout.BitsPerSample)
        {
            return true;
        }

        // Each row is stored as CopyChannels stores it, then turned back into pixels as the decoder
        // does: the integers scaled to [0, 1] in float (libjxl's SingleFromSingle below 23 bits), a grey
        // plane in every colour component, then the scaled conversion to the pixel type. The depth holds
        // the samples when that gives every pixel back.
        int width = image.Width;
        int components = IsGray ? 1 : 3;
        double max = (1u << bits) - 1;
        float inverse = (float)(1.0 / max);
        float scale = Layout.Kind == SampleKind.Integer8 ? 1F / 255 : 1F / 65535;
        Configuration configuration = image.GetConfiguration();
        using RowReader reader = new(image);
        using IMemoryOwner<Vector4> decodedOwner = configuration.MemoryAllocator.Allocate<Vector4>(width);
        using IMemoryOwner<TPixel> pixelsOwner = configuration.MemoryAllocator.Allocate<TPixel>(width);
        Span<Vector4> decoded = decodedOwner.Memory.Span.Slice(0, width);
        Span<TPixel> pixels = pixelsOwner.Memory.Span.Slice(0, width);
        Span<int> samples = reader.Samples;
        Span<float> floats = reader.Floats;
        for (int y = 0; y < image.Height; y++)
        {
            reader.Read(y);
            decoded.Fill(new Vector4(0, 0, 0, 1));
            for (int i = 0; i < components + (HasAlpha ? 1 : 0); i++)
            {
                int component = i < components ? i : 3;
                reader.GetIntegers(component, samples);
                for (int x = 0; x < width; x++)
                {
                    floats[x] = samples[x] * scale;
                }

                FloatToInt(floats, samples, bits, 0, false, max);
                for (int x = 0; x < width; x++)
                {
                    float value = samples[x] * inverse;
                    ref Vector4 v = ref decoded[x];
                    if (IsGray && component == 0)
                    {
                        v.X = value;
                        v.Y = value;
                        v.Z = value;
                    }
                    else
                    {
                        SetComponent(ref v, component, value);
                    }
                }
            }

            PixelOperations<TPixel>.Instance.FromVector4Destructive(configuration, decoded, pixels, PixelConversionModifiers.Scale);
            Span<TPixel> row = image.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
                if (!pixels[x].Equals(row[x]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Copies `count` components of each pixel, from component `first` (R, G, B, A; grey is R and its
    // alpha A), into the channels of gi from `c`.
    private static void CopyChannels(Image<TPixel> image, int first, int count, JxlBitDepth bitDepth, JxlModularImage gi, int c)
    {
        int bits = (int)bitDepth.BitsPerSample;
        int expBits = (int)bitDepth.ExponentBitsPerSample;
        bool fp = bitDepth.FloatingPointSample;
        double factor = fp ? 1 : (1u << bits) - 1;

        // The integer samples go through float only when the stored depth is not the depth they have.
        bool integerSamples = Layout.ExponentBitsPerSample == 0;
        bool copyIntegers = integerSamples && !fp && bits == Layout.BitsPerSample;
        float scale = Layout.Kind == SampleKind.Integer8 ? 1F / 255 : 1F / 65535;

        using RowReader reader = new(image);
        Span<int> samples = reader.Samples;
        Span<float> floats = reader.Floats;
        for (int y = 0; y < image.Height; y++)
        {
            reader.Read(y);
            for (int i = 0; i < count; i++)
            {
                int component = first + i;
                Span<int> output = gi.Channel[c + i].Row(y);
                if (!integerSamples)
                {
                    // ChooseBitDepth stores float samples only in a float layout that holds them.
                    DebugGuard.IsTrue(fp, "Float samples have a float layout");
                    reader.GetFloats(component, floats);
                    FloatToInt(floats, output, bits, expBits, fp, factor);
                    continue;
                }

                reader.GetIntegers(component, samples);
                if (copyIntegers)
                {
                    samples.CopyTo(output);
                    continue;
                }

                for (int x = 0; x < samples.Length; x++)
                {
                    floats[x] = samples[x] * scale;
                }

                FloatToInt(floats, output, bits, expBits, fp, factor);
            }
        }
    }

    // One row per pixel type: how its samples are read, whether it is grey, and whether it has alpha.
    // Another pixel type is read as 16-bit colour (no other type has more than 16 bits per unsigned
    // integer component), with alpha unless its pixel type info says it has none.
    private static PixelLayout GetLayout()
    {
        (Type Type, SampleKind Kind, bool IsGray, bool HasAlpha)[] table =
        {
            (typeof(L8), SampleKind.Integer8, true, false),
            (typeof(La16), SampleKind.Integer8, true, true),
            (typeof(L16), SampleKind.Integer16, true, false),
            (typeof(La32), SampleKind.Integer16, true, true),
            (typeof(A8), SampleKind.Integer8, false, true),
            (typeof(Rgb24), SampleKind.Integer8, false, false),
            (typeof(Bgr24), SampleKind.Integer8, false, false),
            (typeof(Rgba32), SampleKind.Integer8, false, true),
            (typeof(Bgra32), SampleKind.Integer8, false, true),
            (typeof(Argb32), SampleKind.Integer8, false, true),
            (typeof(Abgr32), SampleKind.Integer8, false, true),
            (typeof(Byte4), SampleKind.Integer8, false, true),
            (typeof(Rgb48), SampleKind.Integer16, false, false),
            (typeof(Rgba64), SampleKind.Integer16, false, true),
            (typeof(Rg32), SampleKind.Integer16, false, false),
            (typeof(Bgr565), SampleKind.Integer16, false, false),
            (typeof(Bgra4444), SampleKind.Integer16, false, true),
            (typeof(Bgra5551), SampleKind.Integer16, false, true),
            (typeof(Rgba1010102), SampleKind.Integer16, false, true),
            (typeof(HalfSingle), SampleKind.Half, false, false),
            (typeof(HalfVector2), SampleKind.Half, false, false),
            (typeof(HalfVector4), SampleKind.Half, false, true),
            (typeof(RgbaVector), SampleKind.Float, false, true),
            (typeof(NormalizedByte2), SampleKind.ScaledFloat, false, false),
            (typeof(NormalizedByte4), SampleKind.ScaledFloat, false, true),
            (typeof(NormalizedShort2), SampleKind.ScaledFloat, false, false),
            (typeof(NormalizedShort4), SampleKind.ScaledFloat, false, true),
            (typeof(Short2), SampleKind.ScaledFloat, false, false),
            (typeof(Short4), SampleKind.ScaledFloat, false, true),
        };

        foreach ((Type type, SampleKind kind, bool isGray, bool hasAlpha) in table)
        {
            if (type == typeof(TPixel))
            {
                return new PixelLayout(kind, isGray, hasAlpha);
            }
        }

        PixelAlphaRepresentation? alpha = PixelOperations<TPixel>.Instance.GetPixelTypeInfo()?.AlphaRepresentation;
        return new PixelLayout(SampleKind.Integer16, false, alpha != PixelAlphaRepresentation.None);
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

    private static void SetComponent(ref Vector4 v, int component, float value)
    {
        switch (component)
        {
            case 0:
                v.X = value;
                break;
            case 1:
                v.Y = value;
                break;
            case 2:
                v.Z = value;
                break;
            default:
                v.W = value;
                break;
        }
    }

    // The sample layout of a pixel type and its own bit depth.
    private readonly struct PixelLayout
    {
        public PixelLayout(SampleKind kind, bool isGray, bool hasAlpha)
        {
            this.Kind = kind;
            this.IsGray = isGray;
            this.HasAlpha = hasAlpha;
            (this.BitsPerSample, this.ExponentBitsPerSample) = kind switch
            {
                SampleKind.Integer8 => (8u, 0u),
                SampleKind.Integer16 => (16u, 0u),
                SampleKind.Half => (16u, 5u),
                _ => (32u, 8u),
            };
        }

        public SampleKind Kind { get; }

        public bool IsGray { get; }

        public bool HasAlpha { get; }

        public uint BitsPerSample { get; }

        public uint ExponentBitsPerSample { get; }
    }

    // Reads the rows of an image as integers or floats, one row at a time, into buffers it owns.
    private sealed class RowReader : IDisposable
    {
        private readonly Image<TPixel> image;
        private readonly Configuration configuration;
        private readonly IMemoryOwner<Vector4> vectorsOwner;
        private readonly IMemoryOwner<La32> greyOwner;
        private readonly IMemoryOwner<Rgba64> colorOwner;
        private readonly IMemoryOwner<int> samplesOwner;
        private readonly IMemoryOwner<float> floatsOwner;

        public RowReader(Image<TPixel> image)
        {
            this.image = image;
            this.configuration = image.GetConfiguration();
            MemoryAllocator allocator = this.configuration.MemoryAllocator;
            int width = image.Width;
            this.vectorsOwner = allocator.Allocate<Vector4>(width);
            this.greyOwner = allocator.Allocate<La32>(width);
            this.colorOwner = allocator.Allocate<Rgba64>(width);
            this.samplesOwner = allocator.Allocate<int>(width);
            this.floatsOwner = allocator.Allocate<float>(width);
        }

        // A row of samples for the caller to fill.
        public Span<int> Samples => this.samplesOwner.Memory.Span.Slice(0, this.image.Width);

        // A row of floats for the caller to fill.
        public Span<float> Floats => this.floatsOwner.Memory.Span.Slice(0, this.image.Width);

        private Span<Vector4> Vectors => this.vectorsOwner.Memory.Span.Slice(0, this.image.Width);

        private Span<La32> Grey => this.greyOwner.Memory.Span.Slice(0, this.image.Width);

        private Span<Rgba64> Color => this.colorOwner.Memory.Span.Slice(0, this.image.Width);

        // Converts row y to the form that the pixel type's samples are read in.
        public void Read(int y)
        {
            Span<TPixel> row = this.image.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);
            PixelOperations<TPixel> operations = PixelOperations<TPixel>.Instance;
            switch (Layout.Kind)
            {
                case SampleKind.Half:
                case SampleKind.Float:
                    operations.ToVector4(this.configuration, row, this.Vectors, PixelConversionModifiers.None);
                    break;
                case SampleKind.ScaledFloat:
                    operations.ToVector4(this.configuration, row, this.Vectors, PixelConversionModifiers.Scale);
                    break;
                default:
                    if (IsGray)
                    {
                        operations.To(this.configuration, row, this.Grey);
                    }
                    else
                    {
                        operations.To(this.configuration, row, this.Color);
                    }

                    break;
            }
        }

        // The integer samples of one component of the row read last, at the pixel type's depth.
        public void GetIntegers(int component, Span<int> samples)
        {
            Span<La32> grey = this.Grey;
            Span<Rgba64> color = this.Color;
            for (int x = 0; x < samples.Length; x++)
            {
                int v16 = IsGray ? (component == 3 ? grey[x].A : grey[x].L) : GetComponent(color[x], component);
                samples[x] = Layout.Kind == SampleKind.Integer8 ? v16 / 257 : v16;
            }
        }

        // The float samples of one component of the row read last.
        public void GetFloats(int component, Span<float> floats)
        {
            Span<Vector4> vectors = this.Vectors;
            for (int x = 0; x < floats.Length; x++)
            {
                floats[x] = GetComponent(vectors[x], component);
            }
        }

        public void Dispose()
        {
            this.vectorsOwner.Dispose();
            this.greyOwner.Dispose();
            this.colorOwner.Dispose();
            this.samplesOwner.Dispose();
            this.floatsOwner.Dispose();
        }
    }
}
