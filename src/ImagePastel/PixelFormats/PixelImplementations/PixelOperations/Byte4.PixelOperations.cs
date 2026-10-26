// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Formats;

namespace PinataLabs.ImagePastel.PixelFormats;

/// <content>
/// Provides optimized overrides for bulk operations.
/// </content>
public partial struct Byte4
{
    /// <summary>
    /// Provides optimized overrides for bulk operations.
    /// </summary>
    internal class PixelOperations : PixelOperations<Byte4>
    {
        private static readonly Lazy<PixelTypeInfo> LazyInfo =
            new Lazy<PixelTypeInfo>(() => PixelTypeInfo.Create<Byte4>(PixelAlphaRepresentation.Unassociated), true);

        /// <inheritdoc />
        public override PixelTypeInfo GetPixelTypeInfo() => LazyInfo.Value;
    }
}
