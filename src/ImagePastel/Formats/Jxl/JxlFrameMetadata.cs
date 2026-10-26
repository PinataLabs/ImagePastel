// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// The fields follow libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: JxlDecoderGetFrameHeader
// and JxlDecoderGetFrameName in lib/jxl/decode.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Provides JPEG XL specific metadata information for one displayed frame.
/// </summary>
public sealed class JxlFrameMetadata : IDeepCloneable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JxlFrameMetadata"/> class.
    /// </summary>
    public JxlFrameMetadata()
    {
    }

    private JxlFrameMetadata(JxlFrameMetadata other)
    {
        this.Duration = other.Duration;
        this.Name = other.Name;
    }

    /// <summary>
    /// Gets or sets how long the frame is shown, in ticks as stored: see
    /// <see cref="JxlMetadata.TicksPerSecondNumerator"/> and <see cref="JxlMetadata.TicksPerSecondDenominator"/>.
    /// It is 0 for a still image.
    /// </summary>
    public uint Duration { get; set; }

    /// <summary>
    /// Gets or sets the name of the frame, which may be empty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public IDeepCloneable DeepClone() => new JxlFrameMetadata(this);
}
