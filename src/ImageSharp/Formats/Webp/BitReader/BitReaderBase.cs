// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.IO;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Webp.BitReader;

/// <summary>
/// Base class for VP8 and VP8L bitreader.
/// </summary>
internal abstract class BitReaderBase : IDisposable
{
    private bool isDisposed;

    /// <summary>
    /// Gets or sets the raw encoded image data.
    /// </summary>
    public IMemoryOwner<byte> Data { get; set; }

    /// <summary>
    /// Copies the raw encoded image data from the stream into a byte array.
    /// The buffer only holds the data present in the stream, so a chunk size larger than the stream does not
    /// allocate its declared size. The bit readers read the missing end of the chunk as zeros.
    /// </summary>
    /// <param name="input">The input stream.</param>
    /// <param name="bytesToRead">Number of bytes to read as indicated from the chunk size.</param>
    /// <param name="memoryAllocator">Used for allocating memory during reading data from the stream.</param>
    protected void ReadImageDataFromStream(Stream input, int bytesToRead, MemoryAllocator memoryAllocator)
    {
        int bufferSize = (int)Math.Clamp(input.Length - input.Position, 0, bytesToRead);
        this.Data = memoryAllocator.Allocate<byte>(bufferSize);
        Span<byte> dataSpan = this.Data.Memory.Span;
        int totalBytesRead = 0;
        while (totalBytesRead < bufferSize)
        {
            int bytesRead = input.Read(dataSpan.Slice(totalBytesRead));
            if (bytesRead == 0)
            {
                break;
            }

            totalBytesRead += bytesRead;
        }

        // A stream that ends early leaves the end of the buffer unread. Clear it, so the missing data is read as zeros.
        dataSpan.Slice(totalBytesRead).Clear();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (this.isDisposed)
        {
            return;
        }

        if (disposing)
        {
            this.Data?.Dispose();
        }

        this.isDisposed = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
