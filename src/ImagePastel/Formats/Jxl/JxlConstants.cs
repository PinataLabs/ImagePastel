// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Constants of the JPEG XL format.
/// </summary>
internal static class JxlConstants
{
    /// <summary>
    /// The list of file extensions that equate to JPEG XL.
    /// </summary>
    public static readonly IEnumerable<string> FileExtensions = new[] { "jxl" };

    /// <summary>
    /// The list of mimetypes that equate to JPEG XL.
    /// </summary>
    public static readonly IEnumerable<string> MimeTypes = new[] { "image/jxl" };

    /// <summary>
    /// The signature of a bare codestream.
    /// </summary>
    public static readonly byte[] CodestreamSignature = { 0xFF, 0x0A };

    /// <summary>
    /// The signature of the ISO BMFF-style container: a 12-byte <c>JXL </c> box.
    /// </summary>
    public static readonly byte[] ContainerSignature = { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A };
}
