// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// ImageFeatures in lib/jxl/passes_state.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

namespace PinataLabs.ImagePastel.Formats.Jxl.Frame;

/// <summary>
/// The features of a frame that its DC global section reads before the dequantization matrices: the
/// patch dictionary, the splines and the noise parameters. Each is <see langword="null"/> when the frame
/// does not have its flag.
/// </summary>
/// <remarks>
/// libjxl keeps one <c>ImageFeatures</c> across frames and clears each part for a frame without it. Here
/// each frame makes its own (see <see cref="JxlPatchDictionary"/> and <see cref="JxlSplines"/>).
/// </remarks>
internal sealed class JxlImageFeatures
{
    /// <summary>A frame with none of the features.</summary>
    public static readonly JxlImageFeatures None = new(null, null, null);

    /// <summary>Initializes a new instance of the <see cref="JxlImageFeatures"/> class.</summary>
    /// <param name="patches">The patch dictionary, or <see langword="null"/>.</param>
    /// <param name="splines">The splines, or <see langword="null"/>.</param>
    /// <param name="noise">The noise parameters, or <see langword="null"/>.</param>
    public JxlImageFeatures(JxlPatchDictionary patches, JxlSplines splines, JxlNoiseParams noise)
    {
        this.Patches = patches;
        this.Splines = splines;
        this.Noise = noise;
    }

    /// <summary>Gets the patch dictionary, or <see langword="null"/> if the frame has no patches.</summary>
    public JxlPatchDictionary Patches { get; }

    /// <summary>Gets the splines, or <see langword="null"/> if the frame has no splines.</summary>
    public JxlSplines Splines { get; }

    /// <summary>Gets the noise parameters, or <see langword="null"/> if the frame has no noise.</summary>
    public JxlNoiseParams Noise { get; }
}
