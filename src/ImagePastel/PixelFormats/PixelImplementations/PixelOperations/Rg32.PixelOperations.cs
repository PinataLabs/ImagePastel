// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Formats;

namespace PinataLabs.ImagePastel.PixelFormats;

/// <content>
/// Provides optimized overrides for bulk operations.
/// </content>
public partial struct Rg32
{
    /// <summary>
    /// Provides optimized overrides for bulk operations.
    /// </summary>
    internal class PixelOperations : PixelOperations<Rg32>
    {
        private static readonly Lazy<PixelTypeInfo> LazyInfo =
            new Lazy<PixelTypeInfo>(() => PixelTypeInfo.Create<Rg32>(PixelAlphaRepresentation.None), true);

        /// <inheritdoc />
        public override PixelTypeInfo GetPixelTypeInfo() => LazyInfo.Value;
    }
}
