// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// PatchDictionary in lib/jxl/dec_patch_dictionary.h and lib/jxl/dec_patch_dictionary.cc (Decode,
// ComputePatchTree, GetPatchesForRow, AddOneRow), and the contexts in lib/jxl/patch_dictionary_internal.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// The patches of a frame: rectangles of frames kept in reference slots, each placed at one or more
/// positions in the frame and blended there.
/// </summary>
/// <remarks>
/// A deliberate departure from libjxl: libjxl keeps one dictionary across frames and clears it for a
/// frame without patches. Here each frame with the patches flag decodes its own, and a frame without
/// the flag has none.
/// </remarks>
internal sealed class JxlPatchDictionary
{
    // Context numbers as specified in Section C.4.5, Listing C.2.
    private const int NumRefPatchContext = 0;
    private const int ReferenceFrameContext = 1;
    private const int PatchSizeContext = 2;
    private const int PatchReferencePositionContext = 3;
    private const int PatchPositionContext = 4;
    private const int PatchBlendModeContext = 5;
    private const int PatchOffsetContext = 6;
    private const int PatchCountContext = 7;
    private const int PatchAlphaChannelContext = 8;
    private const int PatchClampContext = 9;
    private const int NumPatchDictionaryContexts = 10;

    private const int NumPatchBlendModes = (int)JxlPatchBlendMode.AlphaWeightedAddBelow + 1;

    // Decode checks the cancellation token once every 4096 positions of a reference patch.
    private const long CancellationCheckMask = 4095;

    private static readonly IComparer<PatchInterval> ByY0 = Comparer<PatchInterval>.Create(static (i0, i1) => i0.Y0.CompareTo(i1.Y0));
    private static readonly IComparer<PatchInterval> ByY1 = Comparer<PatchInterval>.Create(static (i0, i1) => i0.Y1.CompareTo(i1.Y1));

    private readonly JxlReferenceFrames referenceFrames;
    private readonly List<PatchPosition> positions = new();
    private readonly List<PatchReferencePosition> refPositions = new();

    // An array once decoded, so AddOneRow can hand out a span of each patch's extra channel blendings.
    private JxlPatchBlending[] blendings = Array.Empty<JxlPatchBlending>();
    private int blendingsStride;

    // Interval tree on the y coordinates of the patches.
    private readonly List<PatchTreeNode> patchTree = new();

    // Number of patches for each row.
    private readonly List<int> numPatches = new();
    private readonly List<(long Y, int Index)> sortedPatchesY0 = new();
    private readonly List<(long Y, int Index)> sortedPatchesY1 = new();

    // Scratch for AddOneRow.
    private readonly List<int> rowPatches = new();
    private JxlBlendingRows fgRows;
    private JxlReferenceFrame fgReference;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlPatchDictionary"/> class.
    /// </summary>
    /// <param name="referenceFrames">The reference slots the patches are taken from.</param>
    public JxlPatchDictionary(JxlReferenceFrames referenceFrames) => this.referenceFrames = referenceFrames;

    /// <summary>
    /// Reads the dictionary from the frame's DC global section. Throws
    /// <see cref="InvalidImageContentException"/> for invalid data.
    /// </summary>
    /// <param name="br">The reader.</param>
    /// <param name="xsize">The padded width of the frame.</param>
    /// <param name="ysize">The padded height of the frame.</param>
    /// <param name="numExtraChannels">The number of extra channels of the image.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public void Decode(JxlBitReader br, long xsize, long ysize, int numExtraChannels, CancellationToken cancellationToken)
    {
        this.positions.Clear();
        this.refPositions.Clear();
        this.blendings = Array.Empty<JxlPatchBlending>();
        List<JxlPatchBlending> blendings = new();
        this.blendingsStride = numExtraChannels + 1;
        JxlAnsCode code = new();
        byte[] contextMap = JxlEntropyDecoder.DecodeHistograms(br, NumPatchDictionaryContexts, code);
        JxlAnsSymbolReader decoder = new(code, br);

        long ReadNum(int context) => decoder.ReadHybridUint(context, br, contextMap);

        long numRefPatch = ReadNum(NumRefPatchContext);

        // Limit max memory usage of patches to about 66 bytes per pixel (assuming 8 bytes per size_t).
        // These are libjxl's limits, kept as they are so that every file libjxl accepts decodes. They
        // bound the memory, not the time: a dictionary of a few bytes can place about one patch per pixel,
        // each as large as the frame, so the patches pass may blend patches x pixels samples. The work is
        // proportional to that and stops when the decode is cancelled; a time budget is D14's.
        long numPixels = xsize * ysize;
        long maxRefPatches = 1024 + (numPixels / 4);
        long maxPatches = maxRefPatches * 4;
        long maxBlendingInfos = maxPatches * 4;
        if (numRefPatch > maxRefPatches)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Too many patches in dictionary");
        }

        long totalPatches = 0;
        long nextSize = 1;

        for (long id = 0; id < numRefPatch; id++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PatchReferencePosition refPos;
            refPos.Ref = ReadNum(ReferenceFrameContext);
            if (refPos.Ref >= JxlReferenceFrames.MaxNumReferenceFrames || this.referenceFrames[(uint)refPos.Ref] == null)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid reference frame ID");
            }

            JxlReferenceFrame ib = this.referenceFrames[(uint)refPos.Ref];
            if (!ib.IsInXyb)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Patches cannot use frames saved post color transforms");
            }

            refPos.X0 = ReadNum(PatchReferencePositionContext);
            refPos.Y0 = ReadNum(PatchReferencePositionContext);
            refPos.XSize = ReadNum(PatchSizeContext) + 1;
            refPos.YSize = ReadNum(PatchSizeContext) + 1;
            if (refPos.X0 + refPos.XSize > ib.Width)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid position specified in reference frame");
            }

            if (refPos.Y0 + refPos.YSize > ib.Height)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid position specified in reference frame");
            }

            long idCount = ReadNum(PatchCountContext);
            if (idCount > maxPatches)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Too many patches in dictionary");
            }

            idCount++;
            totalPatches += idCount;
            if (totalPatches > maxPatches)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Too many patches in dictionary");
            }

            if (nextSize < totalPatches)
            {
                nextSize *= 2;
                nextSize = Math.Min(nextSize, maxPatches);
            }

            if (nextSize * this.blendingsStride > maxBlendingInfos)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Too many patches in dictionary");
            }

            Reserve(this.positions, (int)nextSize);
            Reserve(blendings, (int)(nextSize * this.blendingsStride));
            bool chooseAlpha = numExtraChannels > 1;
            for (long i = 0; i < idCount; i++)
            {
                if ((i & CancellationCheckMask) == CancellationCheckMask)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                PatchPosition pos;
                pos.RefPosIdx = this.refPositions.Count;
                if (i == 0)
                {
                    pos.X = ReadNum(PatchPositionContext);
                    pos.Y = ReadNum(PatchPositionContext);
                }
                else
                {
                    PatchPosition prev = this.positions[^1];
                    long deltax = JxlBits.UnpackSigned((uint)ReadNum(PatchOffsetContext));
                    if (deltax < 0 && -deltax > prev.X)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException($"Invalid patch: negative x coordinate ({prev.X} base x {deltax} delta x)");
                    }

                    pos.X = prev.X + deltax;
                    long deltay = JxlBits.UnpackSigned((uint)ReadNum(PatchOffsetContext));
                    if (deltay < 0 && -deltay > prev.Y)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException($"Invalid patch: negative y coordinate ({prev.Y} base y {deltay} delta y)");
                    }

                    pos.Y = prev.Y + deltay;
                }

                if (pos.X + refPos.XSize > xsize)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"Invalid patch x: at {pos.X} + {refPos.XSize} > {xsize}");
                }

                if (pos.Y + refPos.YSize > ysize)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"Invalid patch y: at {pos.Y} + {refPos.YSize} > {ysize}");
                }

                for (int j = 0; j < this.blendingsStride; j++)
                {
                    long blendMode = ReadNum(PatchBlendModeContext);
                    if (blendMode >= NumPatchBlendModes)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException($"Invalid patch blend mode: {blendMode}");
                    }

                    JxlPatchBlendMode mode = (JxlPatchBlendMode)blendMode;
                    uint alphaChannel;
                    if (UsesAlpha(mode) && chooseAlpha)
                    {
                        alphaChannel = (uint)ReadNum(PatchAlphaChannelContext);
                        if (alphaChannel >= numExtraChannels)
                        {
                            JxlThrowHelper.ThrowInvalidImageContentException($"Invalid alpha channel for blending: {alphaChannel} out of {numExtraChannels}");
                        }
                    }
                    else
                    {
                        alphaChannel = 0;
                    }

                    bool clamp = UsesClamp(mode) && ReadNum(PatchClampContext) != 0;
                    blendings.Add(new JxlPatchBlending(mode, alphaChannel, clamp));
                }

                this.positions.Add(pos);
            }

            this.refPositions.Add(refPos);
        }

        this.positions.TrimExcess();
        this.blendings = blendings.ToArray();

        if (!decoder.CheckAnsFinalState)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("ANS checksum failure.");
        }

        this.ComputePatchTree(cancellationToken);
    }

    /// <summary>
    /// Returns the patches that cover row <paramref name="y"/>, in stream order.
    /// </summary>
    /// <param name="y">The row.</param>
    /// <param name="result">The list to fill; it is cleared first.</param>
    public void GetPatchesForRow(long y, List<int> result)
    {
        result.Clear();
        if (y < this.numPatches.Count && this.numPatches[(int)y] > 0)
        {
            Reserve(result, this.numPatches[(int)y]);
            for (int treeIdx = 0; treeIdx != -1;)
            {
                PatchTreeNode node = this.patchTree[treeIdx];
                if (y <= node.YCenter)
                {
                    for (int i = 0; i < node.Num; ++i)
                    {
                        (long Y, int Index) p = this.sortedPatchesY0[node.Start + i];
                        if (y < p.Y)
                        {
                            break;
                        }

                        result.Add(p.Index);
                    }

                    treeIdx = y < node.YCenter ? node.LeftChild : -1;
                }
                else
                {
                    for (int i = 0; i < node.Num; ++i)
                    {
                        (long Y, int Index) p = this.sortedPatchesY1[node.Start + i];
                        if (y >= p.Y)
                        {
                            break;
                        }

                        result.Add(p.Index);
                    }

                    treeIdx = node.RightChild;
                }
            }

            // Ensure that the relative order of patches that affect the same pixels is preserved. This
            // is important for patches that have a blend mode different from kAdd.
            result.Sort();
        }
    }

    /// <summary>
    /// Adds the patches to a segment of <paramref name="xsize"/> pixels of row <paramref name="y"/>,
    /// starting at column <paramref name="x0"/> of the frame, in place.
    /// </summary>
    /// <param name="inout">The rows of the frame planes, both the background and the output.</param>
    /// <param name="y">The row.</param>
    /// <param name="x0">The first column of the segment.</param>
    /// <param name="xsize">The number of pixels of the segment.</param>
    /// <param name="numColorPlanes">
    /// The number of colour planes of <paramref name="inout"/>: 1 or 3, the same on every call. It may be 1
    /// where the reference slots hold 3, for a grey image that is not XYB.
    /// </param>
    /// <param name="extraChannelInfo">The extra channels of the image.</param>
    /// <param name="hasAlpha">Whether any extra channel is an alpha channel: <see cref="JxlBlending.HasAlpha"/>.</param>
    /// <param name="tmp">Scratch rows for the blending: one per channel, each at least <paramref name="xsize"/> long.</param>
    public void AddOneRow(JxlBlendingRows inout, long y, long x0, long xsize, int numColorPlanes, List<JxlExtraChannelInfo> extraChannelInfo, bool hasAlpha, Buffer2D<float> tmp)
    {
        int numEc = extraChannelInfo.Count;
        DebugGuard.MustBeLessThanOrEqualTo(numEc + 1, this.blendingsStride, nameof(extraChannelInfo));
        int numChannels = numColorPlanes + numEc;
        this.fgRows ??= new JxlBlendingRows(new Buffer2D<float>[numChannels], Memory<float>.Empty);
        this.GetPatchesForRow(y, this.rowPatches);
        foreach (int posIdx in this.rowPatches)
        {
            int blendingIdx = posIdx * this.blendingsStride;
            PatchPosition pos = this.positions[posIdx];
            PatchReferencePosition refPos = this.refPositions[pos.RefPosIdx];
            long by = pos.Y;
            long bx = pos.X;
            long patchXSize = refPos.XSize;
            long iy = y - by;
            if (bx >= x0 + xsize)
            {
                continue;
            }

            if (bx + patchXSize < x0)
            {
                continue;
            }

            long patchX0 = Math.Max(bx, x0);
            long patchX1 = Math.Min(bx + patchXSize, x0 + xsize);

            // The slots do not change while the frame decodes, so the planes are re-pointed only when
            // the patch takes another slot.
            JxlReferenceFrame reference = this.referenceFrames[(uint)refPos.Ref];
            if (reference != this.fgReference)
            {
                // Patches read only slots saved before the colour transform (Decode), and every such slot
                // holds three colour planes, as libjxl's: a frame of a grey image that is not XYB keeps
                // three up to that save (JxlRenderPipeline.Finish). A frame with three colour planes takes
                // the slot's plane of each channel. A grey frame with one takes the slot's first plane, as
                // libjxl's output takes colour channel 0.
                const int refColorPlanes = 3;
                DebugGuard.IsTrue(reference.Planes.Length == refColorPlanes + numEc, "A slot saved before the colour transform holds three colour planes");
                for (int c = 0; c < numChannels; c++)
                {
                    int refC = c < numColorPlanes ? c : refColorPlanes + (c - numColorPlanes);
                    this.fgRows.SetPlane(c, reference.Planes[refC]);
                }

                this.fgReference = reference;
            }

            int n = (int)(patchX1 - patchX0);
            this.fgRows.PointAt((int)(refPos.X0 + patchX0 - bx), (int)(refPos.Y0 + iy), n);
            inout.PointAt((int)patchX0, (int)y, n);
            ReadOnlySpan<JxlPatchBlending> ecBlendings = this.blendings.AsSpan(blendingIdx + 1, numEc);
            JxlBlending.PerformBlending(inout, this.fgRows, inout, numColorPlanes, this.blendings[blendingIdx], ecBlendings, extraChannelInfo, hasAlpha, tmp, n);
        }
    }

    private static bool UsesAlpha(JxlPatchBlendMode mode)
        => mode is JxlPatchBlendMode.BlendAbove or JxlPatchBlendMode.BlendBelow or JxlPatchBlendMode.AlphaWeightedAddAbove or JxlPatchBlendMode.AlphaWeightedAddBelow;

    private static bool UsesClamp(JxlPatchBlendMode mode) => UsesAlpha(mode) || mode == JxlPatchBlendMode.Mul;

    // std::vector::reserve. List.EnsureCapacity is not in netstandard2.1.
    private static void Reserve<T>(List<T> list, int capacity)
    {
        if (list.Capacity < capacity)
        {
            list.Capacity = capacity;
        }
    }

    private void ComputePatchTree(CancellationToken cancellationToken)
    {
        this.patchTree.Clear();
        this.numPatches.Clear();
        this.sortedPatchesY0.Clear();
        this.sortedPatchesY1.Clear();
        if (this.positions.Count == 0)
        {
            return;
        }

        // Create a y-interval for each patch.
        PatchInterval[] intervals = new PatchInterval[this.positions.Count];
        for (int i = 0; i < this.positions.Count; ++i)
        {
            PatchPosition pos = this.positions[i];
            intervals[i].Idx = i;
            intervals[i].Y0 = pos.Y;
            intervals[i].Y1 = pos.Y + this.refPositions[pos.RefPosIdx].YSize;
        }

        void SortByY0(int start, int end) => Array.Sort(intervals, start, end - start, ByY0);
        void SortByY1(int start, int end) => Array.Sort(intervals, start, end - start, ByY1);

        // Count the number of patches for each row. libjxl adds one to every row of every patch, which
        // takes the sum of the patch heights; here each patch adds one at its first row and takes one off
        // after its last, and a running sum gives the same counts in the number of patches plus rows.
        long rows = 0;
        foreach (PatchInterval iv in intervals)
        {
            rows = Math.Max(rows, iv.Y1);
        }

        Reserve(this.numPatches, (int)rows);
        for (int y = 0; y < rows; y++)
        {
            this.numPatches.Add(0);
        }

        foreach (PatchInterval iv in intervals)
        {
            this.numPatches[(int)iv.Y0]++;
            if (iv.Y1 < rows)
            {
                this.numPatches[(int)iv.Y1]--;
            }
        }

        for (int y = 1; y < rows; y++)
        {
            this.numPatches[y] += this.numPatches[y - 1];
        }

        this.patchTree.Add(new PatchTreeNode { Start = 0, Num = intervals.Length });
        int next = 0;
        while (next < this.patchTree.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PatchTreeNode node = this.patchTree[next];
            int start = node.Start;
            int end = node.Start + node.Num;

            // Choose the y_center for this node to be the median of interval starts.
            SortByY0(start, end);
            int middleIdx = start + (node.Num / 2);
            node.YCenter = intervals[middleIdx].Y0;

            // Divide the intervals in [start, end) into three groups:
            //   * those completely to the right of y_center: [right_start, end)
            //   * those overlapping y_center: [left_end, right_start)
            //   * those completely to the left of y_center: [start, left_end)
            int rightStart = middleIdx;
            while (rightStart < end && intervals[rightStart].Y0 == node.YCenter)
            {
                ++rightStart;
            }

            SortByY1(start, rightStart);
            int leftEnd = rightStart;
            while (leftEnd > start && intervals[leftEnd - 1].Y1 > node.YCenter)
            {
                --leftEnd;
            }

            // Fill in sorted_patches_y0_ and sorted_patches_y1_ for the current node.
            node.Num = rightStart - leftEnd;
            node.Start = this.sortedPatchesY0.Count;
            for (int i = rightStart - 1; i >= leftEnd; --i)
            {
                this.sortedPatchesY1.Add((intervals[i].Y1, intervals[i].Idx));
            }

            SortByY0(leftEnd, rightStart);
            for (int i = leftEnd; i < rightStart; ++i)
            {
                this.sortedPatchesY0.Add((intervals[i].Y0, intervals[i].Idx));
            }

            // Create the left and right nodes (if not empty).
            node.LeftChild = node.RightChild = -1;
            if (leftEnd > start)
            {
                node.LeftChild = this.patchTree.Count;
                this.patchTree.Add(new PatchTreeNode { Start = start, Num = leftEnd - start });
            }

            if (rightStart < end)
            {
                node.RightChild = this.patchTree.Count;
                this.patchTree.Add(new PatchTreeNode { Start = rightStart, Num = end - rightStart });
            }

            this.patchTree[next] = node;
            ++next;
        }
    }

    // Position and size of the patch in the reference frame.
    private struct PatchReferencePosition
    {
        public long Ref;
        public long X0;
        public long Y0;
        public long XSize;
        public long YSize;
    }

    private struct PatchPosition
    {
        // Position of top-left corner of the patch in the image.
        public long X;
        public long Y;
        public int RefPosIdx;
    }

    private struct PatchTreeNode
    {
        public int LeftChild;
        public int RightChild;
        public long YCenter;

        // Range of patches in sortedPatchesY0 and sortedPatchesY1 that contain the row YCenter.
        public int Start;
        public int Num;
    }

    private struct PatchInterval
    {
        public int Idx;
        public long Y0;
        public long Y1;
    }
}
