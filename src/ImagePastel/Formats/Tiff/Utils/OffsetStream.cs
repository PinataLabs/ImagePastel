// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace PinataLabs.ImagePastel.Formats.Tiff.Utils;

/// <summary>
/// A view of a seekable stream that starts at the position the stream had when the view was created.
/// TIFF offsets count from the first byte of the TIFF data, so a TIFF that does not start at
/// position 0 of its stream is read and written through this view.
/// </summary>
internal sealed class OffsetStream : Stream
{
    private readonly Stream stream;

    private readonly long start;

    /// <summary>
    /// Initializes a new instance of the <see cref="OffsetStream"/> class.
    /// </summary>
    /// <param name="stream">The seekable stream, at the position where the TIFF data starts.</param>
    public OffsetStream(Stream stream)
    {
        this.stream = stream;
        this.start = stream.Position;
    }

    /// <inheritdoc/>
    public override bool CanRead => this.stream.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => true;

    /// <inheritdoc/>
    public override bool CanWrite => this.stream.CanWrite;

    /// <inheritdoc/>
    public override long Length => this.stream.Length - this.start;

    /// <inheritdoc/>
    public override long Position
    {
        get => this.stream.Position - this.start;
        set => this.stream.Position = value + this.start;
    }

    /// <summary>
    /// Returns a view of the stream that starts at its current position, or the stream itself when that position is 0
    /// or the stream cannot seek.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <returns>A stream whose position 0 is the current position of <paramref name="stream"/>.</returns>
    public static Stream FromCurrentPosition(Stream stream)
        => stream.CanSeek && stream.Position != 0 ? new OffsetStream(stream) : stream;

    /// <inheritdoc/>
    public override void Flush() => this.stream.Flush();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => this.stream.Read(buffer, offset, count);

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer) => this.stream.Read(buffer);

    /// <inheritdoc/>
    public override int ReadByte() => this.stream.ReadByte();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
        => this.stream.Seek(origin == SeekOrigin.Begin ? offset + this.start : offset, origin) - this.start;

    /// <inheritdoc/>
    public override void SetLength(long value) => this.stream.SetLength(value + this.start);

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => this.stream.Write(buffer, offset, count);

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer) => this.stream.Write(buffer);

    /// <inheritdoc/>
    public override void WriteByte(byte value) => this.stream.WriteByte(value);
}
