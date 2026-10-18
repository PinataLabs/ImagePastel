// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

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

    private int NumExtraChannels => this.numExtraChannels;

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
        byte[] codestream = file[0] == 0xFF ? file : JxlContainer.Read(file, readMetadata: false).Codestream;
        JxlCodecMetadata metadata = new();
        JxlBitReader reader = new(codestream, 2, codestream.Length - 2);
        JxlBundle.Read(reader, metadata.Size);
        JxlBundle.Read(reader, metadata.M);
        metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;
        JxlBundle.Read(reader, metadata.TransformData);
        reader.JumpToByteBoundary();
        int frameOffset = 2 + (int)(reader.TotalBitsConsumed / 8);
        JxlFrameHeader frameHeader = new(metadata);
        JxlBundle.Read(reader, frameHeader);
        uint[] sizes = JxlToc.ReadToc(1, reader);
        int start = 2 + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();
        if (metadata.M.XybEncoded || metadata.M.HaveAnimation || metadata.M.ColorEncoding.WantIcc || frameHeader.Encoding != JxlFrameEncoding.Modular)
        {
            throw new ArgumentException("The file must be a Modular, non-XYB, non-animated image with no ICC profile.", nameof(file));
        }

        section = codestream.AsSpan(start, (int)sizes[0]).ToArray();
        return new JxlTestLayers((int)metadata.XSize, (int)metadata.YSize)
        {
            imageHeader = codestream.AsSpan(0, frameOffset).ToArray(),
            numExtraChannels = metadata.M.NumExtraChannels,
            groupSizeShift = frameHeader.GroupSizeShift,
        };
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

        // SizeHeader: not small, BitsOffset(9, 1) for each side, no ratio.
        w.Bit(false).Write(2, 0).Write(9, (ulong)(this.Height - 1)).Write(3, 0).Write(2, 0).Write(9, (ulong)(this.Width - 1));

        // ImageMetadata.
        w.Bit(false).Bit(this.Animation);
        if (this.Animation)
        {
            w.Write(3, 0).Bit(false).Bit(false).Bit(true); // orientation 1, no intrinsic size, no preview, animation
            w.Write(2, 0).Write(2, 0).Write(2, 0).Bit(false); // 100 / 1 ticks per second, loop forever, no timecodes
        }

        w.Bit(false).Write(2, 0).Bit(true); // 8-bit integer samples, 16-bit buffers suffice
        if (!this.Alpha)
        {
            w.Write(2, 0);
        }
        else if (!this.Premultiplied)
        {
            w.Write(2, 1).Bit(true); // one extra channel, all default: 8-bit straight alpha
        }
        else
        {
            w.Write(2, 1).Bit(false).Write(2, 0).Bit(false).Write(2, 0).Write(2, 0).Write(2, 0).Bit(true); // 8-bit premultiplied alpha
        }

        w.Bit(false).Bit(true); // not XYB, sRGB
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
    private static byte[] Section(string hex)
    {
        byte[] file = Convert.FromHexString(hex);
        JxlCodecMetadata metadata = new();
        JxlBitReader reader = new(file, 2, file.Length - 2);
        JxlBundle.Read(reader, metadata.Size);
        JxlBundle.Read(reader, metadata.M);
        metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;
        JxlBundle.Read(reader, metadata.TransformData);
        reader.JumpToByteBoundary();
        JxlBundle.Read(reader, new JxlFrameHeader(metadata));
        uint[] sizes = JxlToc.ReadToc(1, reader);
        int start = 2 + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();
        return file.AsSpan(start, (int)sizes[0]).ToArray();
    }

    private void WriteFrame(JxlTestBitWriter w, JxlTestFrame frame)
    {
        bool regular = frame.Type is JxlFrameType.Regular or JxlFrameType.SkipProgressive;
        w.Bit(false).Write(2, (ulong)frame.Type);
        w.Bit(true).Write(2, 0).Bit(false); // Modular, no flags, no YCbCr
        w.Write(2, 0); // upsampling 1
        for (int i = 0; i < this.NumExtraChannels; i++)
        {
            w.Write(2, 0); // extra channel upsampling 1
        }

        w.Write(2, this.groupSizeShift); // 1 unless taken from a file: 256 x 256 groups
        if (frame.Type != JxlFrameType.ReferenceOnly)
        {
            w.Write(2, 0); // one pass
        }

        bool crop = frame.Crop || frame.XSize != 0;
        bool isPartialFrame = false;
        w.Bit(crop);
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
            WriteBlending(w, frame.Color, this.NumExtraChannels, isPartialFrame);
            for (int i = 0; i < this.NumExtraChannels; i++)
            {
                WriteBlending(w, frame.ExtraChannel, this.NumExtraChannels, isPartialFrame);
            }

            if (this.Animation)
            {
                WriteDuration(w, frame.Duration);
            }

            isLast = frame.IsLast;
            w.Bit(isLast);
        }

        if (!isLast)
        {
            w.Write(2, frame.SaveAsReference);
        }

        bool canBeReferenced = !isLast && (frame.Duration == 0 || frame.SaveAsReference != 0);
        if ((canBeReferenced && frame.Color.Mode == JxlBlendMode.Replace && !isPartialFrame && regular) || frame.Type == JxlFrameType.ReferenceOnly)
        {
            w.Bit(frame.SaveBeforeColorTransform);
        }

        w.Write(2, 0); // no name
        w.Bit(false).Bit(false).Write(2, 0).Write(2, 0); // loop filter: no Gaborish, no EPF, no extensions
        w.Write(2, 0); // no extensions

        // TOC: one section, no permutation.
        w.Bit(false).ZeroPadToByte();
        w.Write(2, 0).Write(10, (ulong)frame.Section.Length).ZeroPadToByte();
        foreach (byte b in frame.Section)
        {
            w.Write(8, b);
        }
    }
}

/// <summary>The blending of a frame's colour or extra channel.</summary>
internal readonly record struct JxlTestBlending(JxlBlendMode Mode, uint Source = 0, uint AlphaChannel = 0, bool Clamp = false);

/// <summary>
/// One frame of a <see cref="JxlTestLayers"/> codestream. A zero <see cref="XSize"/> means a full-size
/// frame, unless <see cref="Crop"/> forces the crop fields to be written.
/// </summary>
internal sealed record JxlTestFrame(byte[] Section)
{
    public JxlFrameType Type { get; init; } = JxlFrameType.Regular;

    public bool Crop { get; init; }

    public int X0 { get; init; }

    public int Y0 { get; init; }

    public int XSize { get; init; }

    public int YSize { get; init; }

    public JxlTestBlending Color { get; init; } = new(JxlBlendMode.Replace);

    public JxlTestBlending ExtraChannel { get; init; } = new(JxlBlendMode.Replace);

    public uint Duration { get; init; }

    public bool IsLast { get; init; }

    public uint SaveAsReference { get; init; }

    public bool SaveBeforeColorTransform { get; init; }
}
