// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Formats;

namespace PinataLabs.ImagePastel.PixelFormats;

/// <content>
/// Provides optimized overrides for bulk operations.
/// </content>
public partial struct A8
{
    /// <summary>
    /// Provides optimized overrides for bulk operations.
    /// </summary>
    internal class PixelOperations : PixelOperations<A8>
    {
        private static readonly Lazy<PixelTypeInfo> LazyInfo =
            new Lazy<PixelTypeInfo>(() => PixelTypeInfo.Create<A8>(PixelAlphaRepresentation.Unassociated), true);

        /// <inheritdoc />
        public override PixelTypeInfo GetPixelTypeInfo() => LazyInfo.Value;
    }
}
