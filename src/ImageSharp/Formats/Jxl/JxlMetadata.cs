// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Provides JPEG XL specific metadata information for the image.
/// </summary>
internal sealed class JxlMetadata : IDeepCloneable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JxlMetadata"/> class.
    /// </summary>
    public JxlMetadata()
    {
    }

    private JxlMetadata(JxlMetadata other)
    {
        this.ColorEncoding = other.ColorEncoding;
        this.IsXybEncoded = other.IsXybEncoded;
        this.BitsPerSample = other.BitsPerSample;
        this.ExponentBitsPerSample = other.ExponentBitsPerSample;
        this.IntensityTarget = other.IntensityTarget;
        this.OriginalIccProfile = other.OriginalIccProfile?.DeepClone();
        this.LoopCount = other.LoopCount;
        this.TicksPerSecondNumerator = other.TicksPerSecondNumerator;
        this.TicksPerSecondDenominator = other.TicksPerSecondDenominator;

        // The descriptions are immutable, so a copy of the list is independent.
        this.ExtraChannels = other.ExtraChannels.ToArray();
    }

    /// <summary>
    /// Gets or sets the enumerated colour encoding of the image, or null when the stream signals an ICC
    /// profile instead.
    /// </summary>
    public JxlColorEncoding ColorEncoding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the image is coded in the XYB colour space. Such images
    /// decode to the colour their encoding or profile describes, not to XYB.
    /// </summary>
    public bool IsXybEncoded { get; set; }

    /// <summary>
    /// Gets or sets the bits per colour sample, including the sign and exponent bits of float samples.
    /// The decoder sets it from the image header. When it is not 0, the encoder checks that it is a
    /// valid depth, and writes it if it holds every sample of the pixel type losslessly; otherwise the
    /// encoder takes the depth of the pixel type.
    /// </summary>
    public uint BitsPerSample { get; set; }

    /// <summary>
    /// Gets or sets the exponent bits per colour sample: 0 for integer samples, 8 for float32. It is
    /// read and written with <see cref="BitsPerSample"/>.
    /// </summary>
    public uint ExponentBitsPerSample { get; set; }

    /// <summary>
    /// Gets or sets the intensity target: the luminance, in nits, that the largest sample stands for. The
    /// decoder sets it from the image header. When it is not 0, the encoder writes it; otherwise the encoder
    /// writes libjxl's default for the transfer function: 10,000 nits for PQ, 1,000 for HLG and 255 for
    /// anything else.
    /// </summary>
    public float IntensityTarget { get; set; }

    /// <summary>
    /// Gets or sets the ICC profile embedded in the codestream, or null when the stream describes its
    /// colour with a colour encoding. It is set even where it does not describe the decoded pixels
    /// (XYB and CMYK images), so it is not always the pixel profile.
    /// </summary>
    public IccProfile OriginalIccProfile { get; set; }

    /// <summary>
    /// Gets or sets how many times an animation plays; 0 means forever. It is 0 for a still image.
    /// </summary>
    public int LoopCount { get; set; }

    /// <summary>
    /// Gets or sets the numerator of the ticks per second, the unit of <see cref="JxlFrameMetadata.Duration"/>.
    /// It is 0 for a still image.
    /// </summary>
    public uint TicksPerSecondNumerator { get; set; }

    /// <summary>
    /// Gets or sets the denominator of the ticks per second, the unit of <see cref="JxlFrameMetadata.Duration"/>.
    /// It is 0 for a still image.
    /// </summary>
    public uint TicksPerSecondDenominator { get; set; }

    /// <summary>
    /// Gets or sets the extra channels of the image, in stream order: every one the header describes, including
    /// those the decoder leaves out of the pixels. It is empty, not null, for an image with none.
    /// </summary>
    public IReadOnlyList<JxlExtraChannel> ExtraChannels { get; internal set; } = Array.Empty<JxlExtraChannel>();

    /// <inheritdoc/>
    public IDeepCloneable DeepClone() => new JxlMetadata(this);
}
