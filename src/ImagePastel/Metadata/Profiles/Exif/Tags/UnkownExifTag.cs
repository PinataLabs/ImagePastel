// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace PinataLabs.ImagePastel.Metadata.Profiles.Exif;

internal sealed class UnkownExifTag : ExifTag
{
    internal UnkownExifTag(ExifTagValue value)
        : base((ushort)value)
    {
    }
}
