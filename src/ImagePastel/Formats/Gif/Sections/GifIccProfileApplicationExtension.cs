// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

#nullable enable

using System;
using System.IO;

namespace PinataLabs.ImagePastel.Formats.Gif;

/// <summary>
/// The application extension that embeds an ICC profile in a GIF file.
/// The ICC specification's annex on embedding profiles defines it: the application identifier is
/// <c>ICCRGBG1</c>, the authentication code is <c>012</c>, and the profile follows in data sub-blocks.
/// </summary>
internal readonly struct GifIccProfileApplicationExtension : IGifExtension
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GifIccProfileApplicationExtension"/> struct.
    /// </summary>
    /// <param name="data">The profile bytes.</param>
    public GifIccProfileApplicationExtension(byte[] data) => this.Data = data;

    /// <inheritdoc/>
    public byte Label => GifConstants.ApplicationExtensionLabel;

    /// <summary>
    /// Gets the length of the extension without its introducer, label and block terminator:
    /// the block size, the identifier and authentication code, and each data sub-block with its size byte.
    /// </summary>
    public int ContentLength => this.Data.Length > 0
        ? 1 + GifConstants.ApplicationBlockSize + this.Data.Length + GetSubBlockCount(this.Data.Length)
        : 0;

    /// <summary>
    /// Gets the profile bytes.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// Reads the profile bytes from the data sub-blocks that follow the application identifier,
    /// up to and including the block terminator.
    /// </summary>
    /// <param name="stream">The stream positioned at the first data sub-block.</param>
    /// <returns>The profile bytes, or null if the stream ends before the block terminator.</returns>
    public static byte[]? Read(Stream stream)
    {
        using MemoryStream data = new();
        byte[] subBlock = new byte[byte.MaxValue];
        int length;
        while ((length = stream.ReadByte()) > 0)
        {
            int bytesRead = 0;
            while (bytesRead < length)
            {
                int read = stream.Read(subBlock, bytesRead, length - bytesRead);
                if (read <= 0)
                {
                    return null;
                }

                bytesRead += read;
            }

            data.Write(subBlock, 0, length);
        }

        // A missing block terminator means the extension is truncated.
        return length < 0 ? null : data.ToArray();
    }

    /// <inheritdoc/>
    public int WriteTo(Span<byte> buffer)
    {
        int bytesWritten = 0;
        buffer[bytesWritten++] = GifConstants.ApplicationBlockSize;

        ReadOnlySpan<byte> idBytes = GifConstants.IccApplicationIdentificationBytes;
        idBytes.CopyTo(buffer.Slice(bytesWritten));
        bytesWritten += idBytes.Length;

        ReadOnlySpan<byte> data = this.Data;
        while (data.Length > 0)
        {
            int length = Math.Min(data.Length, byte.MaxValue);
            buffer[bytesWritten++] = (byte)length;
            data.Slice(0, length).CopyTo(buffer.Slice(bytesWritten));
            bytesWritten += length;
            data = data.Slice(length);
        }

        return bytesWritten;
    }

    private static int GetSubBlockCount(int dataLength) => (dataLength + byte.MaxValue - 1) / byte.MaxValue;
}
