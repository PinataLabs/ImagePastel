// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/encode.cc (VerifyLevelSettings, and the auto level of JxlEncoder::ProcessOneEnqueuedInput).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Headers;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// The codestream level an encode needs: 5, or 10 when the image is beyond level 5's limits.
/// </summary>
internal static class JxlCodestreamLevel
{
    /// <summary>
    /// Returns the level the codestream needs, as libjxl's <c>VerifyLevelSettings</c>, and throws
    /// <see cref="ImageFormatException"/> where libjxl returns -1 (no level allows the image).
    /// </summary>
    /// <remarks>
    /// libjxl's encoder starts at the auto level (-1), so it takes this level, and a codestream that needs
    /// level 10 goes in a container with a <c>jxll</c> box. This encoder has no level option.
    /// </remarks>
    /// <param name="metadata">The codestream metadata.</param>
    /// <param name="icc">The ICC profile that is embedded, or null.</param>
    /// <returns>5 or 10.</returns>
    public static int Verify(JxlCodecMetadata metadata, byte[] icc)
    {
        JxlImageMetadata m = metadata.M;
        ulong xsize = metadata.XSize;
        ulong ysize = metadata.YSize;

        // The uncompressed ICC size, if it is used.
        ulong iccSize = m.ColorEncoding.WantIcc && icc is not null ? (ulong)icc.Length : 0;

        // Level 10 checks
        if (xsize > (1UL << 30) || ysize > (1UL << 30) || xsize * ysize > (1UL << 40))
        {
            JxlThrowHelper.ThrowImageFormatException("Too large image dimensions");
        }

        if (iccSize > (1UL << 28))
        {
            JxlThrowHelper.ThrowImageFormatException("Too large ICC profile size");
        }

        if (m.NumExtraChannels > 256)
        {
            JxlThrowHelper.ThrowImageFormatException("Too many extra channels");
        }

        // Level 5 checks. Too high modular bit depth:
        if (!m.Modular16BitBufferSufficient)
        {
            return 10;
        }

        if (xsize > (1UL << 18) || ysize > (1UL << 18) || xsize * ysize > (1UL << 28))
        {
            return 10;
        }

        // JxlIccWriter refuses a profile over 2^22 bytes, so this one never decides the level here.
        if (iccSize > (1UL << 22))
        {
            return 10;
        }

        if (m.NumExtraChannels > 4)
        {
            return 10;
        }

        foreach (JxlExtraChannelInfo eci in m.ExtraChannelInfo)
        {
            // CMYK channel not allowed.
            if (eci.Type == JxlExtraChannelType.Black)
            {
                return 10;
            }
        }

        // All level 5 checks pass, so return the more compatible level 5.
        return 5;
    }
}
