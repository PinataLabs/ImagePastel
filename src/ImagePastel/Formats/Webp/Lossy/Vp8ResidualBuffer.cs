// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Webp.Lossy;

/// <summary>
/// The quantized levels of every block decided in one pass over the image, in coding order.
/// They are held until the token probabilities are final, because the coefficient partition
/// can only be written with the probabilities that the frame header announces.
/// </summary>
internal sealed class Vp8ResidualBuffer : IDisposable
{
    public const int BlocksPerI16Macroblock = 25;

    public const int BlocksPerI4Macroblock = 24;

    private const int CountBits = 5;

    private const int CountMask = (1 << CountBits) - 1;

    private const int MaxShortsPerBlock = 1 + 16;

    private const int MaxShortsPerMacroblock = BlocksPerI16Macroblock + 16 + 256 + 128;

    private const int MaxPageLength = 1 << 14;

    private readonly MemoryAllocator memoryAllocator;

    private readonly int pageLength;

    private readonly List<IMemoryOwner<short>> pages = new();

    private readonly List<int> pageUsed = new();

    private int currentPage = -1;

    public Vp8ResidualBuffer(MemoryAllocator memoryAllocator, int macroblockCount)
    {
        this.memoryAllocator = memoryAllocator;
        this.pageLength = (int)Math.Min(MaxPageLength, (long)macroblockCount * MaxShortsPerMacroblock);
    }

    public void Clear() => this.currentPage = -1;

    public void AppendBlock(int ctx, ReadOnlySpan<short> levels)
    {
        if (this.currentPage < 0 || this.pageLength - this.pageUsed[this.currentPage] < MaxShortsPerBlock)
        {
            this.NextPage();
        }

        int used = this.pageUsed[this.currentPage];
        Span<short> page = this.pages[this.currentPage].GetSpan();
        page[used++] = (short)((ctx << CountBits) | levels.Length);
        levels.CopyTo(page.Slice(used));
        this.pageUsed[this.currentPage] = used + levels.Length;
    }

    public Reader GetReader() => new(this);

    public void Dispose()
    {
        foreach (IMemoryOwner<short> page in this.pages)
        {
            page.Dispose();
        }

        this.pages.Clear();
        this.pageUsed.Clear();
        this.currentPage = -1;
    }

    private void NextPage()
    {
        int next = this.currentPage + 1;
        if (next == this.pages.Count)
        {
            this.pages.Add(this.memoryAllocator.Allocate<short>(this.pageLength));
            this.pageUsed.Add(0);
        }

        this.pageUsed[next] = 0;
        this.currentPage = next;
    }

    public ref struct Reader
    {
        private readonly Vp8ResidualBuffer buffer;

        private ReadOnlySpan<short> page;

        private int nextPage;

        private int position;

        public Reader(Vp8ResidualBuffer buffer)
        {
            this.buffer = buffer;
            this.page = default;
            this.nextPage = 0;
            this.position = 0;
        }

        public void ReadBlock(out int ctx, out ReadOnlySpan<short> levels)
        {
            int header = this.ReadHeader();
            int count = header & CountMask;
            ctx = header >> CountBits;
            levels = this.page.Slice(this.position, count);
            this.position += count;
        }

        public void SkipBlocks(int blockCount)
        {
            for (int i = 0; i < blockCount; i++)
            {
                int count = this.ReadHeader() & CountMask;
                this.position += count;
            }
        }

        private int ReadHeader()
        {
            if (this.position == this.page.Length)
            {
                DebugGuard.MustBeLessThanOrEqualTo(this.nextPage, this.buffer.currentPage, nameof(this.nextPage));
                this.page = this.buffer.pages[this.nextPage].GetSpan().Slice(0, this.buffer.pageUsed[this.nextPage]);
                this.nextPage++;
                this.position = 0;
            }

            return this.page[this.position++];
        }
    }
}
