// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Provides JPEG XL specific metadata information for the image. Its fields arrive with later slices.
/// </summary>
internal sealed class JxlMetadata : IDeepCloneable
{
    /// <inheritdoc/>
    public IDeepCloneable DeepClone() => new JxlMetadata();
}
