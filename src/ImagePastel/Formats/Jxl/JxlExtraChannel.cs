// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Describes one extra channel of a JPEG XL image, as its header gives it. The decoder keeps only the
/// first alpha channel in the pixels, so this is how a caller learns about the others.
/// </summary>
public sealed class JxlExtraChannel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JxlExtraChannel"/> class.
    /// </summary>
    /// <param name="type">The channel type.</param>
    /// <param name="name">The channel name; empty when there is none.</param>
    /// <param name="bitsPerSample">The bits per sample.</param>
    /// <param name="exponentBitsPerSample">The exponent bits per sample; 0 for integer samples.</param>
    /// <param name="isFloatingPoint">Whether the samples are floating point.</param>
    internal JxlExtraChannel(JxlExtraChannelType type, string name, int bitsPerSample, int exponentBitsPerSample, bool isFloatingPoint)
    {
        this.Type = type;
        this.Name = name;
        this.BitsPerSample = bitsPerSample;
        this.ExponentBitsPerSample = exponentBitsPerSample;
        this.IsFloatingPoint = isFloatingPoint;
    }

    /// <summary>Gets the channel type.</summary>
    public JxlExtraChannelType Type { get; }

    /// <summary>Gets the channel name, empty when the header has none.</summary>
    public string Name { get; }

    /// <summary>Gets the bits per sample.</summary>
    public int BitsPerSample { get; }

    /// <summary>Gets the exponent bits per sample; 0 for integer samples.</summary>
    public int ExponentBitsPerSample { get; }

    /// <summary>Gets a value indicating whether the samples are floating point.</summary>
    public bool IsFloatingPoint { get; }
}
