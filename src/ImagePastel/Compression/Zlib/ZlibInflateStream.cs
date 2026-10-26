// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.IO.Compression;
using SixLabors.ImageSharp.IO;

namespace SixLabors.ImageSharp.Compression.Zlib;

/// <summary>
/// Provides methods and properties for deframing streams from PNGs.
/// </summary>
internal sealed class ZlibInflateStream : Stream
{
    /// <summary>
    /// A default delegate to get more data from the inner stream.
    /// </summary>
    private static readonly Func<int> GetDataNoOp = () => 0;

    /// <summary>
    /// The inner raw memory stream.
    /// </summary>
    private readonly BufferedReadStream innerStream;

    /// <summary>
    /// A value indicating whether this instance of the given entity has been disposed.
    /// </summary>
    /// <value><see langword="true"/> if this instance has been disposed; otherwise, <see langword="false"/>.</value>
    /// <remarks>
    /// If the entity is disposed, it must not be disposed a second
    /// time. The isDisposed field is set the first time the entity
    /// is disposed. If the isDisposed field is true, then the Dispose()
    /// method will not dispose again. This help not to prolong the entity's
    /// life in the Garbage Collector.
    /// </remarks>
    private bool isDisposed;

    /// <summary>
    /// The current data remaining to be read.
    /// </summary>
    private int currentDataRemaining;

    /// <summary>
    /// Delegate to get more data once we've exhausted the current data remaining.
    /// </summary>
    private readonly Func<int> getData;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZlibInflateStream"/> class.
    /// </summary>
    /// <param name="innerStream">The inner raw stream.</param>
    public ZlibInflateStream(BufferedReadStream innerStream)
        : this(innerStream, GetDataNoOp)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZlibInflateStream"/> class.
    /// </summary>
    /// <param name="innerStream">The inner raw stream.</param>
    /// <param name="getData">A delegate to get more data from the inner stream.</param>
    public ZlibInflateStream(BufferedReadStream innerStream, Func<int> getData)
    {
        this.innerStream = innerStream;
        this.getData = getData;
    }

    /// <inheritdoc/>
    public override bool CanRead => this.innerStream.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>
    /// Gets the compressed stream over the deframed inner stream.
    /// </summary>
    public DeflateStream CompressedStream { get; private set; }

    /// <summary>
    /// Adds new bytes from a frame found in the original stream.
    /// </summary>
    /// <param name="bytes">The current remaining data according to the chunk length.</param>
    /// <param name="isCriticalChunk">Whether the chunk to be inflated is a critical chunk.</param>
    /// <returns>The <see cref="bool"/>.</returns>
    public bool AllocateNewBytes(int bytes, bool isCriticalChunk)
    {
        this.currentDataRemaining = bytes;
        if (this.CompressedStream is null)
        {
            return this.InitializeInflateStream(isCriticalChunk);
        }

        return true;
    }

    /// <summary>
    /// Skips the compressed data that has not been read yet, in the current frame and in the frames after it.
    /// </summary>
    public void SkipRemainingData()
    {
        while (this.TryGetDataRemaining())
        {
            this.innerStream.Skip(this.currentDataRemaining);
            this.currentDataRemaining = 0;
        }
    }

    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int ReadByte()
    {
        if (!this.TryGetDataRemaining())
        {
            return -1;
        }

        this.currentDataRemaining--;
        return this.innerStream.ReadByte();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        // Keep reading data until we've reached the end of the stream or filled the buffer.
        int totalBytesRead = 0;
        while (totalBytesRead < count && this.TryGetDataRemaining())
        {
            int bytesToRead = Math.Min(count - totalBytesRead, this.currentDataRemaining);
            int bytesRead = this.innerStream.Read(buffer, offset + totalBytesRead, bytesToRead);
            if (bytesRead <= 0)
            {
                break;
            }

            this.currentDataRemaining -= bytesRead;
            totalBytesRead += bytesRead;
        }

        return totalBytesRead;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (this.isDisposed)
        {
            return;
        }

        if (disposing)
        {
            // Dispose managed resources.
            if (this.CompressedStream != null)
            {
                this.CompressedStream.Dispose();
                this.CompressedStream = null;
            }
        }

        base.Dispose(disposing);

        // Call the appropriate methods to clean up
        // unmanaged resources here.
        // Note disposing is done.
        this.isDisposed = true;
    }

    private bool InitializeInflateStream(bool isCriticalChunk)
    {
        // Read the zlib header : http://tools.ietf.org/html/rfc1950
        // CMF(Compression Method and flags)
        // This byte is divided into a 4 - bit compression method and a
        // 4-bit information field depending on the compression method.
        // bits 0 to 3  CM Compression method
        // bits 4 to 7  CINFO Compression info
        //
        //   0   1
        // +---+---+
        // |CMF|FLG|
        // +---+---+
        //
        // The header may be split across frames, so read it through this stream rather than the inner one.
        int cmf = this.ReadByte();
        int flag = this.ReadByte();
        if (cmf == -1 || flag == -1)
        {
            if (isCriticalChunk)
            {
                throw new ImageFormatException("Unexpected end of data in ZLIB header.");
            }

            return false;
        }

        if ((cmf & 0x0F) == 8)
        {
            // CINFO is the base-2 logarithm of the LZ77 window size, minus eight.
            int cinfo = (cmf & 0xF0) >> 4;

            if (cinfo > 7)
            {
                if (isCriticalChunk)
                {
                    // Values of CINFO above 7 are not allowed in RFC1950.
                    // CINFO is not defined in this specification for CM not equal to 8.
                    throw new ImageFormatException($"Invalid window size for ZLIB header: cinfo={cinfo}");
                }

                return false;
            }
        }
        else if (isCriticalChunk)
        {
            throw new ImageFormatException($"Bad method for ZLIB header: cmf={cmf}");
        }
        else
        {
            return false;
        }

        // The preset dictionary.
        bool fdict = (flag & 32) != 0;
        if (fdict)
        {
            // We don't need this for inflate so simply skip by the next four bytes.
            // https://tools.ietf.org/html/rfc1950#page-6
            for (int i = 0; i < 4; i++)
            {
                if (this.ReadByte() == -1)
                {
                    if (isCriticalChunk)
                    {
                        throw new ImageFormatException("Unexpected end of data in ZLIB header.");
                    }

                    return false;
                }
            }
        }

        // Initialize the deflate BufferedReadStream.
        this.CompressedStream = new DeflateStream(this, CompressionMode.Decompress, true);

        return true;
    }

    /// <summary>
    /// Ensures there is data left to read, moving on to the next frame when the current one is exhausted.
    /// </summary>
    /// <returns><see langword="true"/> if there is data left to read; otherwise, <see langword="false"/>.</returns>
    private bool TryGetDataRemaining()
    {
        if (this.currentDataRemaining <= 0)
        {
            this.currentDataRemaining = Math.Max(this.getData(), 0);
        }

        return this.currentDataRemaining > 0;
    }
}
