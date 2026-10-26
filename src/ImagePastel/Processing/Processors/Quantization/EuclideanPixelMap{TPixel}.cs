// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Processing.Processors.Quantization;

/// <summary>
/// Gets the closest color to the supplied color based upon the Euclidean distance.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
/// <para>
/// This class is not threadsafe and should not be accessed in parallel.
/// Doing so will result in non-idempotent results.
/// </para>
internal sealed class EuclideanPixelMap<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private Rgba32[] rgbaPalette;

    // The index of the first entry of each distinct color, in palette order. A later entry with the same
    // color can never be the nearest, as equal distances resolve to the lowest index, so searches skip it.
    // Quantizers pad unused palette entries with one color, which would otherwise crowd out the others.
    private int[] distinctIndices;

    // The palette sorted by the channel that varies most across it, so a search can start
    // near the color it is matching and stop early. See SearchSortedPalette.
    private Rgba32[] sortedPalette;
    private byte[] sortedKeys;
    private int[] sortedIndices;
    private readonly int[] keyStarts = new int[256];
    private int sortChannel;

    // Do not make this readonly! Struct value would be always copied on non-readonly method calls.
    private ColorDistanceCache cache;
    private readonly Configuration configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="EuclideanPixelMap{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="palette">The color palette to map from. Must not be empty.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="palette"/> is empty.</exception>
    public EuclideanPixelMap(Configuration configuration, ReadOnlyMemory<TPixel> palette)
    {
        this.configuration = configuration;

        // Check the palette before allocating the cache, so a rejected palette does not leak it.
        this.SetPalette(palette);
        this.cache = new ColorDistanceCache(configuration.MemoryAllocator);
    }

    /// <summary>
    /// Gets the color palette of this <see cref="EuclideanPixelMap{TPixel}"/>.
    /// The palette memory is owned by the palette source that created it.
    /// </summary>
    public ReadOnlyMemory<TPixel> Palette
    {
        [MethodImpl(InliningOptions.ShortMethod)]
        get;

        [MethodImpl(InliningOptions.ShortMethod)]
        private set;
    }

    /// <summary>
    /// Returns the closest color in the palette and the index of that pixel.
    /// The palette contents must match the one used in the constructor.
    /// </summary>
    /// <param name="color">The color to match.</param>
    /// <param name="match">The matched color.</param>
    /// <returns>The <see cref="int"/> index.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public int GetClosestColor(TPixel color, out TPixel match)
    {
        ref TPixel paletteRef = ref MemoryMarshal.GetReference(this.Palette.Span);
        Unsafe.SkipInit(out Rgba32 rgba);
        color.ToRgba32(ref rgba);

        // Compare against the palette entries that can be nearest to any color in this color's cell.
        // Cells with too many such entries, and palettes too large for the cache, fall back to a search.
        int index;
        if (this.cache.TryGetCandidates(rgba, this.rgbaPalette, this.distinctIndices, out ReadOnlySpan<byte> candidates))
        {
            index = candidates[0];
            int leastDistance = DistanceSquared(rgba, this.rgbaPalette[index]);
            for (int i = 1; i < candidates.Length; i++)
            {
                int candidate = candidates[i];
                int distance = DistanceSquared(rgba, this.rgbaPalette[candidate]);

                // Candidates are in palette order, so equal distances keep the lowest index.
                if (distance < leastDistance)
                {
                    index = candidate;
                    leastDistance = distance;
                }
            }
        }
        else
        {
            index = this.SearchSortedPalette(rgba);
        }

        match = Unsafe.Add(ref paletteRef, index);
        return index;
    }

    /// <summary>
    /// Clears the map, resetting it to use the given palette.
    /// </summary>
    /// <param name="palette">The color palette to map from. Must not be empty.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="palette"/> is empty.</exception>
    public void Clear(ReadOnlyMemory<TPixel> palette)
    {
        this.SetPalette(palette);
        this.cache.Clear();
    }

    private void SetPalette(ReadOnlyMemory<TPixel> palette)
    {
        // Every lookup returns a palette entry, so an empty palette has nothing to return.
        Guard.MustBeGreaterThan(palette.Length, 0, nameof(palette));

        this.Palette = palette;
        this.rgbaPalette = new Rgba32[palette.Length];
        PixelOperations<TPixel>.Instance.ToRgba32(this.configuration, palette.Span, this.rgbaPalette);

        // Sort on the channel with the largest spread, as it lets the search rule out the most entries.
        long bestSpread = -1;
        for (int channel = 0; channel < 4; channel++)
        {
            long sum = 0;
            long sumOfSquares = 0;
            foreach (Rgba32 color in this.rgbaPalette)
            {
                int value = GetChannel(color, channel);
                sum += value;
                sumOfSquares += value * value;
            }

            long spread = (sumOfSquares * this.rgbaPalette.Length) - (sum * sum);
            if (spread > bestSpread)
            {
                bestSpread = spread;
                this.sortChannel = channel;
            }
        }

        var seen = new HashSet<Rgba32>();
        var distinct = new List<int>();
        for (int i = 0; i < this.rgbaPalette.Length; i++)
        {
            if (seen.Add(this.rgbaPalette[i]))
            {
                distinct.Add(i);
            }
        }

        this.distinctIndices = distinct.ToArray();

        int length = this.distinctIndices.Length;
        this.sortedKeys = new byte[length];
        this.sortedIndices = new int[length];
        for (int i = 0; i < length; i++)
        {
            int index = this.distinctIndices[i];
            this.sortedKeys[i] = GetChannel(this.rgbaPalette[index], this.sortChannel);
            this.sortedIndices[i] = index;
        }

        Array.Sort(this.sortedKeys, this.sortedIndices);
        this.sortedPalette = new Rgba32[length];
        for (int i = 0; i < length; i++)
        {
            this.sortedPalette[i] = this.rgbaPalette[this.sortedIndices[i]];
        }

        // keyStarts[v] is the first sorted position whose key is at least v.
        int position = 0;
        for (int value = 0; value < this.keyStarts.Length; value++)
        {
            while (position < length && this.sortedKeys[position] < value)
            {
                position++;
            }

            this.keyStarts[value] = position;
        }
    }

    private int SearchSortedPalette(Rgba32 rgba)
    {
        Rgba32[] colors = this.sortedPalette;
        byte[] keys = this.sortedKeys;
        int[] indices = this.sortedIndices;
        int key = GetChannel(rgba, this.sortChannel);

        // Walk outwards in both directions from the entries whose sort channel is nearest the color's.
        // The difference in that one channel is a lower bound on the distance, so once it exceeds the
        // best distance found, no entry further out on that side can be nearer.
        // Equal distances resolve to the lowest palette index, matching a scan of the palette in order.
        int index = 0;
        int leastDistance = int.MaxValue;
        int high = this.keyStarts[key];
        int low = high - 1;
        while (high < colors.Length || low >= 0)
        {
            if (high < colors.Length)
            {
                int gap = keys[high] - key;
                if (gap * gap > leastDistance)
                {
                    high = colors.Length;
                }
                else
                {
                    Consider(rgba, colors[high], indices[high], ref index, ref leastDistance);
                    high++;
                }
            }

            if (low >= 0)
            {
                int gap = key - keys[low];
                if (gap * gap > leastDistance)
                {
                    low = -1;
                }
                else
                {
                    Consider(rgba, colors[low], indices[low], ref index, ref leastDistance);
                    low--;
                }
            }
        }

        return index;
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static void Consider(Rgba32 rgba, Rgba32 candidate, int candidateIndex, ref int index, ref int leastDistance)
    {
        int distance = DistanceSquared(rgba, candidate);
        if (distance < leastDistance || (distance == leastDistance && candidateIndex < index))
        {
            index = candidateIndex;
            leastDistance = distance;
        }
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static byte GetChannel(Rgba32 color, int channel)
        => channel switch
        {
            0 => color.R,
            1 => color.G,
            2 => color.B,
            _ => color.A
        };

    /// <summary>
    /// Returns the Euclidean distance squared between two specified points.
    /// </summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>The distance squared.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    private static int DistanceSquared(Rgba32 a, Rgba32 b)
    {
        int deltaR = a.R - b.R;
        int deltaG = a.G - b.G;
        int deltaB = a.B - b.B;
        int deltaA = a.A - b.A;
        return (deltaR * deltaR) + (deltaG * deltaG) + (deltaB * deltaB) + (deltaA * deltaA);
    }

    public void Dispose() => this.cache.Dispose();

    /// <summary>
    /// A cache of the palette entries that can be nearest to the colors in each cell of the color space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The color space is split into cells of 16 levels per channel. The first lookup in a cell keeps every
    /// palette entry whose distance to the nearest point of the cell is no more than the smallest distance
    /// from any entry to the furthest point of the cell. The nearest entry of any color in the cell must
    /// pass that test, so comparing the color against the kept entries gives the same answer as comparing
    /// it against the whole palette.
    /// A cell keeps at most 31 entries; cells needing more, and palettes over 256 entries, are not cached.
    /// Entry count is fixed at 65536 cells of 32 bytes (2097152 bytes, 2MB).
    /// </para>
    /// </remarks>
    private unsafe struct ColorDistanceCache : IDisposable
    {
        private const int CellBits = 4;
        private const int CellShift = 8 - CellBits;
        private const int CellCount = 1 << (CellBits * 4);
        private const int CellSize = 1 << CellShift;
        private const int RowLength = 32;
        private const int MaxCandidates = RowLength - 1;

        // Testing every entry against only a few rivals keeps filling a cell cheap. Testing against
        // more rivals removed almost no further entries on photographic palettes.
        private const int MaxRivals = 8;
        private const byte Unknown = 0;
        private const byte Overflow = byte.MaxValue;
        private MemoryHandle tableHandle;
        private readonly IMemoryOwner<byte> table;
        private readonly byte* tablePointer;

        public ColorDistanceCache(MemoryAllocator allocator)
        {
            // Each row holds the candidate count, or Unknown or Overflow, followed by the candidate indices.
            this.table = allocator.Allocate<byte>(CellCount * RowLength, AllocationOptions.Clean);
            this.tableHandle = this.table.Memory.Pin();
            this.tablePointer = (byte*)this.tableHandle.Pointer;
        }

        [MethodImpl(InliningOptions.ShortMethod)]
        public bool TryGetCandidates(Rgba32 rgba, Rgba32[] palette, int[] distinctIndices, out ReadOnlySpan<byte> candidates)
        {
            if (palette.Length is 0 or > 256)
            {
                candidates = default;
                return false;
            }

            int cell = ((rgba.R >> CellShift) << (CellBits * 3))
                | ((rgba.G >> CellShift) << (CellBits * 2))
                | ((rgba.B >> CellShift) << CellBits)
                | (rgba.A >> CellShift);

            byte* row = this.tablePointer + (cell * RowLength);
            byte count = row[0];
            if (count == Unknown)
            {
                count = FillRow(row, rgba, palette, distinctIndices);
            }

            if (count == Overflow)
            {
                candidates = default;
                return false;
            }

            candidates = new ReadOnlySpan<byte>(row + 1, count);
            return true;
        }

        /// <summary>
        /// Clears the cache resetting each entry to empty.
        /// </summary>
        [MethodImpl(InliningOptions.ShortMethod)]
        public void Clear() => this.table.GetSpan().Clear();

        private static byte FillRow(byte* row, Rgba32 rgba, Rgba32[] palette, int[] distinctIndices)
        {
            // The darkest, most transparent corner of the cell.
            const int mask = ~(CellSize - 1);
            var cellMin = new Rgba32((byte)(rgba.R & mask), (byte)(rgba.G & mask), (byte)(rgba.B & mask), (byte)(rgba.A & mask));

            Span<int> nearest = stackalloc int[palette.Length];
            Span<int> furthest = stackalloc int[palette.Length];
            int threshold = int.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                Rgba32 entry = palette[i];
                int far = 0;
                nearest[i] = Bounds(entry.R, cellMin.R, ref far)
                    + Bounds(entry.G, cellMin.G, ref far)
                    + Bounds(entry.B, cellMin.B, ref far)
                    + Bounds(entry.A, cellMin.A, ref far);

                furthest[i] = far;
                threshold = Math.Min(threshold, far);
            }

            Span<int> kept = stackalloc int[palette.Length];
            int keptCount = 0;
            foreach (int i in distinctIndices)
            {
                if (nearest[i] <= threshold)
                {
                    kept[keptCount++] = i;
                }
            }

            kept = kept.Slice(0, keptCount);

            // The entries whose furthest point of the cell is nearest are the most likely to be
            // closer than another entry everywhere in the cell, which rules that entry out.
            Span<int> rivals = stackalloc int[keptCount];
            kept.CopyTo(rivals);
            int rivalCount = Math.Min(keptCount, MaxRivals);
            for (int i = 0; i < rivalCount; i++)
            {
                int best = i;
                for (int j = i + 1; j < keptCount; j++)
                {
                    if (furthest[rivals[j]] < furthest[rivals[best]])
                    {
                        best = j;
                    }
                }

                (rivals[i], rivals[best]) = (rivals[best], rivals[i]);
            }

            rivals = rivals.Slice(0, rivalCount);

            int count = 0;
            foreach (int i in kept)
            {
                if (IsBeaten(palette, i, rivals, cellMin))
                {
                    continue;
                }

                if (count == MaxCandidates)
                {
                    row[0] = Overflow;
                    return Overflow;
                }

                row[++count] = (byte)i;
            }

            row[0] = (byte)count;
            return (byte)count;
        }

        // Returns whether one of the rivals is strictly closer than the entry to every color in the cell.
        // The difference of two squared distances is linear in the color, so it is smallest at a corner
        // of the cell, and each channel can pick its own worst corner.
        private static bool IsBeaten(Rgba32[] palette, int index, ReadOnlySpan<int> rivals, Rgba32 cellMin)
        {
            Rgba32 entry = palette[index];
            foreach (int rival in rivals)
            {
                if (rival == index)
                {
                    continue;
                }

                Rgba32 other = palette[rival];
                int margin = Margin(entry.R, other.R, cellMin.R)
                    + Margin(entry.G, other.G, cellMin.G)
                    + Margin(entry.B, other.B, cellMin.B)
                    + Margin(entry.A, other.A, cellMin.A);

                if (margin > 0)
                {
                    return true;
                }
            }

            return false;
        }

        // Returns the smallest value over the cell's levels along one channel of
        // (level - entry)^2 - (level - rival)^2, which is (rival - entry) * (2 * level - entry - rival).
        [MethodImpl(InliningOptions.ShortMethod)]
        private static int Margin(int entry, int rival, int min)
        {
            int slope = rival - entry;
            int atMin = slope * ((2 * min) - entry - rival);
            int atMax = slope * ((2 * (min + CellSize - 1)) - entry - rival);
            return Math.Min(atMin, atMax);
        }

        // Returns the squared distance along one channel from a value to the nearest level of the cell
        // starting at min, and adds the squared distance to the furthest level of the cell.
        [MethodImpl(InliningOptions.ShortMethod)]
        private static int Bounds(int value, int min, ref int furthest)
        {
            int max = min + CellSize - 1;
            int far = Math.Max(value - min, max - value);
            furthest += far * far;

            int near = value < min ? min - value : value > max ? value - max : 0;
            return near * near;
        }

        public void Dispose()
        {
            if (this.table != null)
            {
                this.tableHandle.Dispose();
                this.table.Dispose();
            }
        }
    }
}
