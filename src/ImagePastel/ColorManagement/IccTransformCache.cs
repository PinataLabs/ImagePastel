// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace SixLabors.ImageSharp.ColorManagement;

/// <summary>
/// Keeps the transforms built during one decode or one processor run, by the bytes of their source
/// profile, so frames whose profiles are equal but separate instances (TIFF) share one transform.
/// </summary>
internal sealed class IccTransformCache
{
    // An image has few distinct profiles, so a list is enough.
    private readonly List<(byte[] Profile, IccColorTransform Transform)> entries = [];

    /// <summary>
    /// Gets the transform built for a profile with the same bytes, or builds and keeps a new one.
    /// </summary>
    /// <param name="profile">The bytes of the source profile, or <see langword="null"/> for sRGB.</param>
    /// <param name="create">Builds the transform; it may return <see langword="null"/>, which is kept too.</param>
    /// <returns>The transform.</returns>
    public IccColorTransform GetOrCreate(byte[] profile, Func<IccColorTransform> create)
    {
        foreach ((byte[] key, IccColorTransform transform) in this.entries)
        {
            if (key is null ? profile is null : profile is not null && key.AsSpan().SequenceEqual(profile))
            {
                return transform;
            }
        }

        IccColorTransform created = create();
        this.entries.Add((profile, created));
        return created;
    }
}
