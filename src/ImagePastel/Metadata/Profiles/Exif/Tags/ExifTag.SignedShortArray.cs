// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Metadata.Profiles.Exif;

/// <content/>
public abstract partial class ExifTag
{
    /// <summary>
    /// Gets the TimeZoneOffset exif tag: the offset from UTC in hours of <see cref="DateTime"/> and,
    /// as an optional second value, of <see cref="DateTimeOriginal"/>.
    /// </summary>
    public static ExifTag<short[]> TimeZoneOffset { get; } = new ExifTag<short[]>(ExifTagValue.TimeZoneOffset);
}
