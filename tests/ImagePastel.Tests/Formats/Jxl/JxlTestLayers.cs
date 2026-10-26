// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Builds small layered codestreams: an 8-bit RGBA (or RGB) image header written field by field, then
/// frames whose headers set the crop, blending, reference slot and frame type under test. Each frame's
/// pixels are one Modular section taken from a <c>jxl_from_tree</c> file. The trees predict every
/// sample exactly, so each token costs no bits and the section decodes to the same pattern at any frame
/// size up to one group (256 x 256).
/// </summary>
internal sealed class JxlTestLayers
{
    // jxl_from_tree v0.12.0 (tools/jxl_from_tree.cc), "jxl_from_tree <tree> out.jxl", with the trees
    //   ColourA: Width 16 / Height 16 / Alpha / if c > 2 (- Set 128) (if c > 1 (- Set 50) (if c > 0 (- Set 100) (- Set 200)))
    //   ColourB: Width 16 / Height 16 / Alpha / if c > 2 (- Set 64) (if c > 1 (- Set 160) (if c > 0 (- Set 220) (- Set 40)))
    //   Ramp:    Width 16 / Height 16 / Alpha / if c > 2 (- N +9) (if c > 1 (- N +5) (if c > 0 (- Set 90) (- W +7)))
    // written one node per line and indented as jxl_from_tree expects.
    private const string ColourAFile = "ff0a43804a081810004c004b3841bced543c0b05208323091e0985448201";
    private const string ColourBFile = "ff0a43804a081810004c004b3841bced543c0b0f2083021114170e428201";
    private const string RampFile = "ff0a43804a0818100048004b3840bced47c9a04e48a915c1d39035120c";

    private readonly List<JxlTestFrame> frames = new();

    // The image header of an existing file, used instead of the written one.
    private byte[] imageHeader;
    private int numExtraChannels;
    private uint groupSizeShift = 1;

    public JxlTestLayers(int width, int height, bool alpha = true, bool premultiplied = false, bool animation = false)
    {
        this.Width = width;
        this.Height = height;
        this.Alpha = alpha;
        this.Premultiplied = premultiplied;
        this.Animation = animation;
        this.numExtraChannels = alpha ? 1 : 0;
    }

    /// <summary>Gets an RGBA layer of (200, 100, 50, 128) in 8-bit samples.</summary>
    public static byte[] ColourA { get; } = Section(ColourAFile);

    /// <summary>Gets an RGBA layer of (40, 220, 160, 64) in 8-bit samples.</summary>
    public static byte[] ColourB { get; } = Section(ColourBFile);

    /// <summary>
    /// Gets an RGBA layer whose samples depend on the position: red is 7 (x + y + 1), green is 90, blue
    /// is 5 (x + y + 1) and alpha is 9 (x + y + 1), in 8-bit samples, so red, blue and alpha pass 255.
    /// </summary>
    public static byte[] Ramp { get; } = Section(RampFile);

    public int Width { get; }

    public int Height { get; }

    public bool Alpha { get; }

    public bool Premultiplied { get; }

    public bool Animation { get; }

    /// <summary>Gets the numerator of the animation's ticks per second.</summary>
    public uint TpsNumerator { get; init; } = 100;

    /// <summary>Gets the denominator of the animation's ticks per second, up to 1024.</summary>
    public uint TpsDenominator { get; init; } = 1;

    /// <summary>Gets the animation's loop count; 0 is forever.</summary>
    public uint NumLoops { get; init; }

    /// <summary>Gets a value indicating whether the animation header sets have_timecodes, which gives each frame a <see cref="JxlTestFrame.Timecode"/>.</summary>
    public bool Timecodes { get; init; }

    /// <summary>
    /// Gets the extra channels after the alpha, if any. Every frame gives them the blending of
    /// <see cref="JxlTestFrame.ExtraChannel"/>, so they need sections whose tree covers every channel
    /// after the third, as <see cref="ColourA"/>, <see cref="ColourB"/> and <see cref="Ramp"/> have.
    /// </summary>
    public JxlTestExtraChannel[] ExtraChannels { get; init; }

    /// <summary>Gets a value indicating whether the image is XYB-encoded, which gives every frame the XYB colour transform.</summary>
    public bool Xyb { get; init; }

    /// <summary>
    /// Gets the transfer function of the colour encoding: RGB, D65, sRGB primaries, relative intent.
    /// <see langword="null"/> with no <see cref="Gamma"/> writes the all-default sRGB encoding.
    /// </summary>
    public JxlTransferFunction? TransferFunction { get; init; }

    /// <summary>Gets the gamma of the colour encoding times 10^7, or 0 for none; it replaces <see cref="TransferFunction"/>.</summary>
    public uint Gamma { get; init; }

    /// <summary>
    /// Returns layers that keep the image header of a single-frame, single-group Modular file, which
    /// must not be XYB or animated, and gives that frame's section. The section only decodes in a
    /// full-size frame.
    /// </summary>
    /// <param name="file">The file: a bare codestream or a container.</param>
    /// <param name="section">The section of the file's frame.</param>
    /// <returns>The layers.</returns>
    public static JxlTestLayers FromFile(byte[] file, out byte[] section)
    {
        byte[] codestream = file[0] == 0xFF ? file : JxlContainer.Read(file, readMetadata: false, CancellationToken.None).Codestream;
        section = FirstSection(codestream, out JxlCodecMetadata metadata, out JxlFrameHeader frameHeader, out int frameOffset);
        if (metadata.M.XybEncoded || metadata.M.HaveAnimation || metadata.M.ColorEncoding.WantIcc || frameHeader.Encoding != JxlFrameEncoding.Modular)
        {
            throw new ArgumentException("The file must be a Modular, non-XYB, non-animated image with no ICC profile.", nameof(file));
        }

        return new JxlTestLayers((int)metadata.XSize, (int)metadata.YSize)
        {
            imageHeader = codestream.AsSpan(0, frameOffset).ToArray(),
            numExtraChannels = metadata.M.NumExtraChannels,
            groupSizeShift = frameHeader.GroupSizeShift,
        };
    }

    /// <summary>Reads the header of the first frame of a single-group codestream.</summary>
    /// <param name="codestream">The codestream.</param>
    /// <returns>The frame header.</returns>
    public static JxlFrameHeader ReadFirstFrameHeader(byte[] codestream)
    {
        FirstSection(codestream, out _, out JxlFrameHeader frameHeader, out _);
        return frameHeader;
    }

    public JxlTestLayers Add(JxlTestFrame frame)
    {
        this.frames.Add(frame);
        return this;
    }

    public byte[] ToArray()
    {
        JxlTestBitWriter w = new();
        if (this.imageHeader != null)
        {
            foreach (byte b in this.imageHeader)
            {
                w.Write(8, b);
            }

            foreach (JxlTestFrame frame in this.frames)
            {
                this.WriteFrame(w, frame);
            }

            return w.ToArray();
        }

        w.Write(8, 0xFF).Write(8, 0x0A);

        // SizeHeader: not small, BitsOffset(9, 1) or BitsOffset(13, 1) for each side, no ratio.
        w.Bit(false);
        WriteSide(w, this.Height);
        w.Write(3, 0);
        WriteSide(w, this.Width);

        // ImageMetadata.
        w.Bit(false).Bit(this.Animation);
        if (this.Animation)
        {
            w.Write(3, 0).Bit(false).Bit(false).Bit(true); // orientation 1, no intrinsic size, no preview, animation
            if (this.TpsNumerator == 100 && this.TpsDenominator == 1 && this.NumLoops == 0)
            {
                w.Write(2, 0).Write(2, 0).Write(2, 0); // 100 / 1 ticks per second, loop forever
            }
            else
            {
                // The widest U32 choice of each field: BitsOffset(30, 1), BitsOffset(10, 1) and Bits(32).
                w.Write(2, 3).Write(30, this.TpsNumerator - 1).Write(2, 3).Write(10, this.TpsDenominator - 1).Write(2, 3).Write(32, this.NumLoops);
            }

            w.Bit(this.Timecodes);
        }

        // 8-bit integer samples; 16-bit buffers suffice unless an extra channel has more than 12 bits,
        // as libjxl's encoder sets it.
        JxlTestExtraChannel[] extraChannels = this.ExtraChannels ?? Array.Empty<JxlTestExtraChannel>();
        w.Bit(false).Write(2, 0).Bit(Array.TrueForAll(extraChannels, c => c.Bits <= 12));

        // num_extra_channels: U32(Val(0), Val(1), BitsOffset(4, 2), BitsOffset(12, 1)).
        this.numExtraChannels = (this.Alpha ? 1 : 0) + extraChannels.Length;
        if (this.numExtraChannels < 2)
        {
            w.Write(2, (ulong)this.numExtraChannels);
        }
        else if (this.numExtraChannels < 18)
        {
            w.Write(2, 2).Write(4, (ulong)this.numExtraChannels - 2);
        }
        else
        {
            w.Write(2, 3).Write(12, (ulong)this.numExtraChannels - 1);
        }

        if (this.Alpha && !this.Premultiplied)
        {
            w.Bit(true); // all default: 8-bit straight alpha
        }
        else if (this.Alpha)
        {
            w.Bit(false).Write(2, 0).Bit(false).Write(2, 0).Write(2, 0).Write(2, 0).Bit(true); // 8-bit premultiplied alpha
        }

        foreach (JxlTestExtraChannel extraChannel in extraChannels)
        {
            extraChannel.Write(w);
        }

        w.Bit(this.Xyb);
        if (this.TransferFunction == null && this.Gamma == 0)
        {
            w.Bit(true); // sRGB
        }
        else
        {
            // ColorEncoding: not all default, no ICC, RGB, D65, sRGB primaries, then the transfer
            // function and the relative intent.
            w.Bit(false).Bit(false);
            w.WriteEnum((uint)JxlColorSpace.Rgb);
            w.WriteEnum((uint)JxlWhitePoint.D65);
            w.WriteEnum((uint)JxlPrimaries.Srgb);
            w.Bit(this.Gamma != 0);
            if (this.Gamma != 0)
            {
                w.Write(24, this.Gamma);
            }
            else
            {
                w.WriteEnum((uint)this.TransferFunction.Value);
            }

            w.WriteEnum((uint)JxlRenderingIntent.Relative);
        }

        if (this.Animation)
        {
            w.Bit(true); // default tone mapping
        }

        w.Write(2, 0).Bit(true); // no extensions, default transform data
        w.ZeroPadToByte();

        foreach (JxlTestFrame frame in this.frames)
        {
            this.WriteFrame(w, frame);
        }

        return w.ToArray();
    }

    private static void WriteSide(JxlTestBitWriter w, int size)
    {
        if (size <= 512)
        {
            w.Write(2, 0).Write(9, (ulong)(size - 1));
        }
        else
        {
            w.Write(2, 1).Write(13, (ulong)(size - 1));
        }
    }

    private static void WriteCrop(JxlTestBitWriter w, uint value)
    {
        if (value < 256)
        {
            w.Write(2, 0).Write(8, value);
        }
        else if (value < 2304)
        {
            w.Write(2, 1).Write(11, value - 256);
        }
        else if (value < 18688)
        {
            w.Write(2, 2).Write(14, value - 2304);
        }
        else
        {
            w.Write(2, 3).Write(30, value - 18688);
        }
    }

    private static void WriteBlending(JxlTestBitWriter w, JxlTestBlending blending, int numExtraChannels, bool isPartialFrame)
    {
        switch (blending.Mode)
        {
            case JxlBlendMode.AlphaWeightedAdd:
                w.Write(2, 3).Write(2, 0);
                break;
            case JxlBlendMode.Mul:
                w.Write(2, 3).Write(2, 1);
                break;
            default:
                w.Write(2, (ulong)blending.Mode);
                break;
        }

        bool usesAlpha = numExtraChannels > 0 && blending.Mode is JxlBlendMode.Blend or JxlBlendMode.AlphaWeightedAdd;
        if (usesAlpha)
        {
            w.Write(2, blending.AlphaChannel); // up to 2
        }

        if (usesAlpha || blending.Mode == JxlBlendMode.Mul)
        {
            w.Bit(blending.Clamp);
        }

        if (blending.Mode != JxlBlendMode.Replace || isPartialFrame)
        {
            w.Write(2, blending.Source);
        }
    }

    private static void WriteDuration(JxlTestBitWriter w, uint duration)
    {
        if (duration <= 1)
        {
            w.Write(2, duration);
        }
        else if (duration < 256)
        {
            w.Write(2, 2).Write(8, duration);
        }
        else
        {
            w.Write(2, 3).Write(32, duration);
        }
    }

    // The first frame's only section in a jxl_from_tree file.
    private static byte[] Section(string hex) => FirstSection(Convert.FromHexString(hex), out _, out _, out _);

    // Reads the image header and the first frame's header and TOC, and returns that frame's first
    // section. frameOffset is where the frame header starts.
    private static byte[] FirstSection(byte[] codestream, out JxlCodecMetadata metadata, out JxlFrameHeader frameHeader, out int frameOffset)
    {
        metadata = JxlCodestreamFrames.ReadImageHeader(codestream, out frameOffset);
        JxlBitReader reader = new(codestream, frameOffset, codestream.Length - frameOffset);
        frameHeader = new JxlFrameHeader(metadata);
        JxlBundle.Read(reader, frameHeader);
        uint[] sizes = JxlToc.ReadGroupOffsets(1, reader).Sizes;
        int start = frameOffset + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();
        return codestream.AsSpan(start, (int)sizes[0]).ToArray();
    }

    private void WriteFrame(JxlTestBitWriter w, JxlTestFrame frame)
    {
        bool regular = frame.Type is JxlFrameType.Regular or JxlFrameType.SkipProgressive;
        ulong flags = frame.Patches != null ? frame.Flags | (ulong)JxlFrameFlags.Patches : frame.Flags;
        flags = frame.Splines != null ? flags | (ulong)JxlFrameFlags.Splines : flags;
        flags = frame.NoiseLut != null ? flags | (ulong)JxlFrameFlags.Noise : flags;
        w.Bit(false).Write(2, (ulong)frame.Type);
        w.Bit(frame.Encoding == JxlFrameEncoding.Modular);
        if (flags == 0)
        {
            w.Write(2, 0);
        }
        else if (flags <= 16)
        {
            w.Write(2, 1).Write(4, flags - 1); // U64: 1 to 16
        }
        else
        {
            w.Write(2, 2).Write(8, flags - 17); // U64: 17 to 272
        }

        bool useDcFrame = (flags & (ulong)JxlFrameFlags.UseDcFrame) != 0;
        if (!this.Xyb)
        {
            w.Bit(frame.YCbCr);
            if (frame.YCbCr && !useDcFrame)
            {
                // The chroma subsampling mode of each channel, Cb, Y, Cr: 0 (none) unless set.
                foreach (int mode in frame.ChromaSubsampling ?? new[] { 0, 0, 0 })
                {
                    w.Write(2, (ulong)mode);
                }
            }
        }

        if (!useDcFrame)
        {
            // U32(Val(1), Val(2), Val(4), Val(8)): the selector is log2 of the upsampling.
            ulong upsampling = (ulong)JxlBits.FloorLog2Nonzero(frame.Upsampling);
            w.Write(2, upsampling);
            ulong extraChannelUpsampling = frame.ExtraChannelUpsampling != 0 ? (ulong)JxlBits.FloorLog2Nonzero(frame.ExtraChannelUpsampling) : upsampling;
            for (int i = 0; i < this.numExtraChannels; i++)
            {
                w.Write(2, extraChannelUpsampling);
            }
        }

        if (frame.Encoding == JxlFrameEncoding.Modular)
        {
            w.Write(2, this.groupSizeShift); // 1 unless taken from a file: 256 x 256 groups
        }
        else if (this.Xyb)
        {
            w.Write(3, 3).Write(3, 2); // the default x_qm_scale and b_qm_scale
        }

        if (frame.Type != JxlFrameType.ReferenceOnly)
        {
            if (frame.Passes != null)
            {
                frame.Passes(w);
            }
            else
            {
                w.Write(2, 0); // one pass
            }
        }

        // A DC frame has a DC level, U32(Val(1), Val(2), Val(3), Val(4)), and no crop or reference slot.
        bool dcFrame = frame.Type == JxlFrameType.Dc;
        if (dcFrame)
        {
            w.Write(2, frame.DcLevel - 1);
        }

        bool crop = !dcFrame && (frame.Crop || frame.XSize != 0);
        bool isPartialFrame = false;
        if (!dcFrame)
        {
            w.Bit(crop);
        }

        if (crop)
        {
            if (regular)
            {
                WriteCrop(w, JxlBits.PackSigned(frame.X0));
                WriteCrop(w, JxlBits.PackSigned(frame.Y0));
                isPartialFrame = frame.X0 > 0 || frame.Y0 > 0 || frame.XSize + frame.X0 < this.Width || frame.YSize + frame.Y0 < this.Height;
            }

            WriteCrop(w, (uint)frame.XSize);
            WriteCrop(w, (uint)frame.YSize);
        }

        bool isLast = false;
        if (regular)
        {
            WriteBlending(w, frame.Color, this.numExtraChannels, isPartialFrame);
            for (int i = 0; i < this.numExtraChannels; i++)
            {
                JxlTestBlending extraChannel = frame.ExtraChannels != null ? frame.ExtraChannels[i] : frame.ExtraChannel;
                WriteBlending(w, extraChannel, this.numExtraChannels, isPartialFrame);
            }

            if (this.Animation)
            {
                WriteDuration(w, frame.Duration);
                if (this.Timecodes)
                {
                    w.Write(32, frame.Timecode);
                }
            }

            isLast = frame.IsLast;
            w.Bit(isLast);
        }

        if (!isLast && !dcFrame)
        {
            w.Write(2, frame.SaveAsReference);
        }

        bool canBeReferenced = !isLast && (frame.Duration == 0 || frame.SaveAsReference != 0);
        if ((canBeReferenced && frame.Color.Mode == JxlBlendMode.Replace && !isPartialFrame && regular) || frame.Type == JxlFrameType.ReferenceOnly)
        {
            w.Bit(frame.SaveBeforeColorTransform);
        }

        w.WriteName(frame.Name);
        w.Bit(false).Bit(false).Write(2, 0).Write(2, 0); // loop filter: no Gaborish, no EPF, no extensions
        w.Write(2, 0); // no extensions

        // The patch dictionary, the spline dictionary, then the noise look-up table come first in the DC
        // global section, which a single group and pass puts at the start of the frame's only section.
        byte[] section = frame.Section;
        if (frame.Patches != null || frame.Splines != null || frame.NoiseLut != null)
        {
            JxlTestBitWriter withFeatures = new();
            frame.Patches?.Write(withFeatures, this.numExtraChannels);
            frame.Splines?.Write(withFeatures);
            foreach (uint entry in frame.NoiseLut ?? Array.Empty<uint>())
            {
                withFeatures.Write(10, entry);
            }

            foreach (byte b in frame.Section)
            {
                withFeatures.Write(8, b);
            }

            section = withFeatures.ToArray();
        }

        // TOC: the permuted bit and the permutation, if any, then the size of each section as
        // U32(Bits(10), BitsOffset(14, 1024), BitsOffset(22, 17408), BitsOffset(30, 4211712)).
        byte[][] sections = frame.Sections ?? new[] { section };
        w.Bit(frame.TocPermutation != null);
        frame.TocPermutation?.Invoke(w);
        w.ZeroPadToByte();
        foreach (byte[] s in sections)
        {
            WriteSectionSize(w, (ulong)s.Length);
        }

        w.ZeroPadToByte();
        foreach (byte[] s in sections)
        {
            foreach (byte b in s)
            {
                w.Write(8, b);
            }
        }
    }

    private static void WriteSectionSize(JxlTestBitWriter w, ulong length)
    {
        if (length < 1024)
        {
            w.Write(2, 0).Write(10, length);
        }
        else if (length < 17408)
        {
            w.Write(2, 1).Write(14, length - 1024);
        }
        else if (length < 4211712)
        {
            w.Write(2, 2).Write(22, length - 17408);
        }
        else
        {
            w.Write(2, 3).Write(30, length - 4211712);
        }
    }
}

/// <summary>The blending of a frame's colour or extra channel.</summary>
internal readonly record struct JxlTestBlending(JxlBlendMode Mode, uint Source = 0, uint AlphaChannel = 0, bool Clamp = false);

/// <summary>
/// The header of one extra channel (libjxl's <c>ExtraChannelInfo</c>), written field by field and never
/// as all default. An alpha is straight, a spot colour is opaque red and a CFA channel has index 1.
/// </summary>
/// <param name="Type">The type; any value of libjxl's ExtraChannel enum, valid or not, can be written.</param>
/// <param name="Name">The name, up to 47 UTF-8 bytes.</param>
/// <param name="Bits">The bits per sample, including the exponent and sign of a float.</param>
/// <param name="Float">Whether the samples are floating point.</param>
/// <param name="ExponentBits">The exponent bits of a float, 2 to 8; 0 for integers.</param>
/// <param name="DimShift">The dim_shift, 0 to 3.</param>
internal readonly record struct JxlTestExtraChannel(JxlExtraChannelType Type, string Name = "", uint Bits = 8, bool Float = false, uint ExponentBits = 0, uint DimShift = 0)
{
    public void Write(JxlTestBitWriter w)
    {
        w.Bit(false); // not all default
        w.WriteEnum((uint)this.Type);

        // BitDepth: the float flag, bits_per_sample as U32(Val(8), Val(10), Val(12), BitsOffset(6, 1)) for
        // integers or U32(Val(32), Val(16), Val(24), BitsOffset(6, 1)) for floats, then a float's
        // exponent_bits_per_sample - 1 in 4 bits.
        w.Bit(this.Float);
        uint[] direct = this.Float ? new uint[] { 32, 16, 24 } : new uint[] { 8, 10, 12 };
        int selector = Array.IndexOf(direct, this.Bits);
        if (selector >= 0)
        {
            w.Write(2, (ulong)selector);
        }
        else
        {
            w.Write(2, 3).Write(6, this.Bits - 1);
        }

        if (this.Float)
        {
            w.Write(4, this.ExponentBits - 1);
        }

        // dim_shift: U32(Val(0), Val(3), Val(4), BitsOffset(3, 1)).
        switch (this.DimShift)
        {
            case 0:
                w.Write(2, 0);
                break;
            case 3:
                w.Write(2, 1);
                break;
            default:
                w.Write(2, 3).Write(3, this.DimShift - 1);
                break;
        }

        w.WriteName(this.Name);
        switch (this.Type)
        {
            case JxlExtraChannelType.Alpha:
                w.Bit(false); // straight alpha
                break;
            case JxlExtraChannelType.SpotColor:
                w.Write(16, 0x3C00).Write(16, 0).Write(16, 0).Write(16, 0x3C00); // red, solidity 1, as F16
                break;
            case JxlExtraChannelType.Cfa:
                w.Write(2, 0); // cfa_channel: Val(1)
                break;
        }
    }
}

/// <summary>
/// One frame of a <see cref="JxlTestLayers"/> codestream. A zero <see cref="XSize"/> means a full-size
/// frame, unless <see cref="Crop"/> forces the crop fields to be written.
/// </summary>
internal sealed record JxlTestFrame(byte[] Section)
{
    public JxlFrameType Type { get; init; } = JxlFrameType.Regular;

    /// <summary>Gets the frame flags, up to 272.</summary>
    public ulong Flags { get; init; }

    /// <summary>Gets the frame encoding. A VarDCT frame's section is still Modular data, so it only reaches the header checks.</summary>
    public JxlFrameEncoding Encoding { get; init; } = JxlFrameEncoding.Modular;

    /// <summary>Gets a value indicating whether the frame has the YCbCr colour transform, with no chroma subsampling; not for XYB images.</summary>
    public bool YCbCr { get; init; }

    /// <summary>
    /// Gets the chroma subsampling mode of each channel of a <see cref="YCbCr"/> frame, in Cb, Y, Cr
    /// order: 0 (4:4:4), 1 (4:2:0), 2 (4:2:2) or 3 (4:4:0). <see langword="null"/> means none.
    /// </summary>
    public int[] ChromaSubsampling { get; init; }

    /// <summary>Gets the upsampling of the colour and extra channels: 1, 2, 4 or 8.</summary>
    public uint Upsampling { get; init; } = 1;

    /// <summary>Gets the upsampling of every extra channel, 1, 2, 4 or 8, if it differs from <see cref="Upsampling"/>; 0 means the same.</summary>
    public uint ExtraChannelUpsampling { get; init; }

    public bool Crop { get; init; }

    public int X0 { get; init; }

    public int Y0 { get; init; }

    public int XSize { get; init; }

    public int YSize { get; init; }

    public JxlTestBlending Color { get; init; } = new(JxlBlendMode.Replace);

    public JxlTestBlending ExtraChannel { get; init; } = new(JxlBlendMode.Replace);

    /// <summary>Gets the blending of each extra channel, when they differ; it overrides <see cref="ExtraChannel"/>.</summary>
    public JxlTestBlending[] ExtraChannels { get; init; }

    public uint Duration { get; init; }

    /// <summary>Gets the timecode, 0xHHMMSSFF, written only when the layers set <see cref="JxlTestLayers.Timecodes"/>.</summary>
    public uint Timecode { get; init; }

    /// <summary>Gets the DC level of a <see cref="JxlFrameType.Dc"/> frame, 1 to 4.</summary>
    public uint DcLevel { get; init; } = 1;

    /// <summary>Gets the frame name, up to 47 UTF-8 bytes.</summary>
    public string Name { get; init; }

    public bool IsLast { get; init; }

    public uint SaveAsReference { get; init; }

    public bool SaveBeforeColorTransform { get; init; }

    /// <summary>
    /// Gets the frame's sections in TOC order, for a frame of more than one section; when set, it
    /// replaces <see cref="Section"/>, and the frame cannot have patches or splines.
    /// </summary>
    public byte[][] Sections { get; init; }

    /// <summary>
    /// Gets the writer of the frame header's passes field (libjxl's <c>Passes</c>), for a frame of more
    /// than one pass. When it is not set, the frame has one pass.
    /// </summary>
    public Action<JxlTestBitWriter> Passes { get; init; }

    /// <summary>
    /// Gets the writer of the TOC's permutation: its histograms, then its tokens. When set, the TOC's
    /// permuted bit is set, and <see cref="Sections"/> are in the order the permutation stores them.
    /// </summary>
    public Action<JxlTestBitWriter> TocPermutation { get; init; }

    /// <summary>Gets the patch dictionary; when set, the patches flag is set too.</summary>
    public JxlTestPatchDictionary Patches { get; init; }

    /// <summary>Gets the spline dictionary; when set, the splines flag is set too.</summary>
    public JxlTestSplines Splines { get; init; }

    /// <summary>
    /// Gets the eight entries of the noise look-up table, in units of 1/1024 (10 bits each); when set,
    /// the noise flag is set too.
    /// </summary>
    public uint[] NoiseLut { get; init; }

    /// <summary>Returns a frame of <paramref name="section"/> that is the last of the codestream.</summary>
    /// <param name="section">The frame's section.</param>
    /// <returns>The frame.</returns>
    public static JxlTestFrame Last(byte[] section) => new(section) { IsLast = true };
}
