// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Runtime.CompilerServices;

namespace PinataLabs.ImagePastel.Formats.Jpeg.Components.Decoder;

/// <summary>
/// Represents a jpeg file marker.
/// </summary>
internal readonly struct JpegFileMarker
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JpegFileMarker"/> struct.
    /// </summary>
    /// <param name="marker">The marker</param>
    /// <param name="position">The position within the stream</param>
    public JpegFileMarker(byte marker, long position)
    {
        this.Marker = marker;
        this.Position = position;
    }

    /// <summary>
    /// Gets the position of the marker within a stream
    /// </summary>
    public byte Marker
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    /// <summary>
    /// Gets the position of the marker within a stream
    /// </summary>
    public long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return this.Marker.ToString("X");
    }
}
