// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Brotli (https://github.com/google/brotli) at tag v1.2.0 (commit 028fb5a2):
// c/dec/decode.c, c/dec/state.h and c/dec/state.c.
// Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers;

namespace PinataLabs.ImagePastel.Compression.Brotli;

/// <summary>
/// Decodes a Brotli stream (RFC 7932) in managed code, for targets without
/// <c>System.IO.Compression.BrotliDecoder</c>.
/// </summary>
/// <remarks>
/// <para>Departures from the C decoder:</para>
/// <list type="bullet">
/// <item>
/// The input has to hold the rest of the stream. The C decoder can stop where the input ends and go on
/// with more; this one throws there. So the states that only wait for input are gone, and so is the
/// "safe" variant of each read. The decoder still stops where the output is full.
/// </item>
/// <item>
/// A stream that is corrupt or truncated throws <see cref="InvalidImageContentException"/>, where the C
/// decoder returns an error code.
/// </item>
/// <item>
/// No large window (<c>BROTLI_DECODER_PARAM_LARGE_WINDOW</c>), no attached or compound dictionary and no
/// metadata callbacks: <c>System.IO.Compression.BrotliDecoder</c> uses none of them.
/// </item>
/// </list>
/// </remarks>
internal sealed partial class ManagedBrotliDecoder
{
    private const int HuffmanTableBits = 8;

    private const int RingBufferWriteAheadSlack = 542;

    private const int NumDistanceShortCodes = 16;
    private const int MaxDistanceBits = 24;
    private const int NumLiteralSymbols = 256;
    private const int MaxContextMapSymbols = 256 + 16;

    private const int MaxAllowedDistance = 0x7FFFFFFC;

    private const int WindowGap = 16;

    private const int BlockSizeCap = 1 << 24;

    private const int MaxTableSizeOverAlphabet = 376;

    private readonly int[] distRb = { 16, 15, 11, 4 };
    private readonly int[] blockLength = new int[3];
    private readonly int[] numBlockTypes = new int[3];
    private readonly int[] blockTypeRb = new int[6];
    private readonly uint[] trivialLiteralContexts = new uint[8];
    private readonly BrotliHuffmanCode[] blockTypeTrees = new BrotliHuffmanCode[3 * BrotliHuffman.MaxSize258];
    private readonly BrotliHuffmanCode[] blockLenTrees = new BrotliHuffmanCode[3 * BrotliHuffman.MaxSize26];
    private readonly HuffmanTreeGroup literalGroup = new();
    private readonly HuffmanTreeGroup insertCopyGroup = new();
    private readonly HuffmanTreeGroup distanceGroup = new();

    private readonly BrotliHuffmanCode[] codeLengthTable = new BrotliHuffmanCode[32];
    private readonly ushort[] symbolLists = new ushort[BrotliHuffman.SymbolListsBase + BrotliPrefix.NumCommandSymbols];
    private readonly int[] nextSymbol = new int[BrotliHuffman.MaxCodeLength + 1];
    private readonly byte[] codeLengthCodeLengths = new byte[BrotliHuffman.CodeLengthCodes];
    private readonly ushort[] codeLengthHisto = new ushort[BrotliHuffman.MaxCodeLength + 1];
    private readonly BrotliHuffmanCode[] contextMapTable = new BrotliHuffmanCode[BrotliHuffman.MaxSize272];
    private readonly ushort[] simpleSymbols = new ushort[4];

    private readonly byte[] distExtraBits = new byte[544];
    private readonly int[] distOffset = new int[544];

    private State state = State.Uninited;

    private bool failed;

    private uint heldBits;
    private int heldBitCount;

    private int loopCounter;
    private int pos;
    private int maxBackwardDistance;
    private int maxDistance;
    private int ringbufferSize;
    private int ringbufferMask;
    private int newRingbufferSize;
    private int distRbIdx;
    private int metaBlockRemainingLen;
    private byte[] ringbuffer;
    private bool shouldWrapRingbuffer;

    private long rbRoundtrips;
    private long partialPosOut;

    private int windowBits;
    private bool isLastMetablock;
    private bool isUncompressed;
    private bool isMetadata;
    private bool uncompressedWrite;

    private int distancePostfixBits;
    private int numDirectDistanceCodes;
    private byte[] contextModes;
    private byte[] contextMap;
    private byte[] distContextMap;

    private int htreeCommand;
    private int literalHtree;
    private int contextLookup;
    private int contextMapSlice;
    private int distContextMapSlice;

    private bool trivialLiteralContext;
    private int distanceContext;
    private int distHtreeIndex;
    private int copyLength;
    private int distanceCode;

    // BrotliRunningState, without the states that only wait for input.
    private enum State
    {
        Uninited,
        MetablockBegin,
        CommandBegin,
        CommandInner,
        CommandPostDecodeLiterals,
        CommandPostWrapCopy,
        Uncompressed,
        CommandInnerWrite,
        MetablockDone,
        CommandPostWrite1,
        CommandPostWrite2,
        Done,
    }

    private static ReadOnlySpan<byte> CodeLengthCodeOrder => new byte[] { 1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    private static ReadOnlySpan<byte> CodeLengthPrefixLength => new byte[] { 2, 2, 2, 3, 2, 2, 2, 4, 2, 2, 2, 3, 2, 2, 2, 4 };

    private static ReadOnlySpan<byte> CodeLengthPrefixValue => new byte[] { 0, 4, 3, 2, 0, 4, 3, 1, 0, 4, 3, 2, 0, 4, 3, 5 };

    public OperationStatus Decompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten)
    {
        if (this.failed)
        {
            BrotliThrowHelper.ThrowCorrupt("the decoder already failed");
        }

        this.failed = true;
        BrotliBitReader br = new(source, this.heldBits, this.heldBitCount);
        int written = 0;
        OperationStatus status = this.Run(ref br, destination, ref written);
        bytesConsumed = br.Unload(out this.heldBits, out this.heldBitCount);
        bytesWritten = written;
        this.failed = false;
        return status;
    }

    private static int ReadBlockLength(BrotliHuffmanCode[] table, int offset, ref BrotliBitReader br)
    {
        int code = br.ReadSymbol(table, offset);
        return BrotliPrefix.BlockLengthOffset[code] + br.ReadBits(BrotliPrefix.BlockLengthBits[code]);
    }

    private OperationStatus Run(ref BrotliBitReader br, Span<byte> destination, ref int written)
    {
        for (; ;)
        {
            switch (this.state)
            {
                case State.Uninited:
                    this.DecodeWindowBits(ref br);

                    // RFC 7932, section 9.1: the window is (1 << WBITS) - 16 bytes.
                    this.maxBackwardDistance = (1 << this.windowBits) - WindowGap;
                    this.state = State.MetablockBegin;
                    break;

                case State.MetablockBegin:
                    this.ReadMetablockHeader(ref br);
                    break;

                case State.Uncompressed:
                    if (!this.CopyUncompressedBlockToOutput(ref br, destination, ref written))
                    {
                        return OperationStatus.DestinationTooSmall;
                    }

                    this.state = State.MetablockDone;
                    break;

                case State.CommandBegin:
                case State.CommandInner:
                case State.CommandPostDecodeLiterals:
                case State.CommandPostWrapCopy:
                    this.ProcessCommands(ref br);
                    break;

                case State.CommandInnerWrite:
                case State.CommandPostWrite1:
                case State.CommandPostWrite2:
                    if (!this.WriteRingBuffer(destination, ref written, force: false))
                    {
                        return OperationStatus.DestinationTooSmall;
                    }

                    this.WrapRingBuffer();
                    if (this.ringbufferSize == 1 << this.windowBits)
                    {
                        this.maxDistance = this.maxBackwardDistance;
                    }

                    if (this.state == State.CommandPostWrite1)
                    {
                        this.state = this.metaBlockRemainingLen == 0 ? State.MetablockDone : State.CommandBegin;
                    }
                    else if (this.state == State.CommandPostWrite2)
                    {
                        this.state = State.CommandPostWrapCopy;
                    }
                    else if (this.loopCounter == 0)
                    {
                        this.state = this.metaBlockRemainingLen == 0 ? State.MetablockDone : State.CommandPostDecodeLiterals;
                    }
                    else
                    {
                        this.state = State.CommandInner;
                    }

                    break;

                case State.MetablockDone:
                    if (this.metaBlockRemainingLen < 0)
                    {
                        BrotliThrowHelper.ThrowCorrupt("a meta-block has more data than its length");
                    }

                    if (!this.isLastMetablock)
                    {
                        this.state = State.MetablockBegin;
                        break;
                    }

                    if (!br.JumpToByteBoundary())
                    {
                        BrotliThrowHelper.ThrowCorrupt("the padding after the last meta-block is not zero");
                    }

                    this.state = State.Done;
                    break;

                default:
                    if (this.ringbuffer != null && !this.WriteRingBuffer(destination, ref written, force: true))
                    {
                        return OperationStatus.DestinationTooSmall;
                    }

                    return OperationStatus.Done;
            }
        }
    }

    private void DecodeWindowBits(ref BrotliBitReader br)
    {
        if (br.ReadBits(1) == 0)
        {
            this.windowBits = 16;
            return;
        }

        int n = br.ReadBits(3);
        if (n != 0)
        {
            this.windowBits = 17 + n;
            return;
        }

        n = br.ReadBits(3);
        if (n == 1)
        {
            // The marker of a large window stream, which needs BROTLI_DECODER_PARAM_LARGE_WINDOW.
            BrotliThrowHelper.ThrowCorrupt("the window size is not valid");
        }

        this.windowBits = n != 0 ? 8 + n : 17;
    }

    private bool DecodeBlockTypeAndLength(int treeType, ref BrotliBitReader br)
    {
        int maxBlockType = this.numBlockTypes[treeType];
        if (maxBlockType <= 1)
        {
            return false;
        }

        int blockType = br.ReadSymbol(this.blockTypeTrees, treeType * BrotliHuffman.MaxSize258);
        this.blockLength[treeType] = ReadBlockLength(this.blockLenTrees, treeType * BrotliHuffman.MaxSize26, ref br);

        Span<int> ringbuffer = this.blockTypeRb.AsSpan(treeType * 2, 2);
        if (blockType == 1)
        {
            blockType = ringbuffer[1] + 1;
        }
        else if (blockType == 0)
        {
            blockType = ringbuffer[0];
        }
        else
        {
            blockType -= 2;
        }

        if (blockType >= maxBlockType)
        {
            blockType -= maxBlockType;
        }

        ringbuffer[0] = ringbuffer[1];
        ringbuffer[1] = blockType;
        return true;
    }

    private void PrepareLiteralDecoding()
    {
        int blockType = this.blockTypeRb[1];
        this.contextMapSlice = blockType << BrotliContext.LiteralContextBits;
        this.trivialLiteralContext = ((this.trivialLiteralContexts[blockType >> 5] >> (blockType & 31)) & 1) != 0;
        this.literalHtree = this.literalGroup.Htrees[this.contextMap[this.contextMapSlice]];
        this.contextLookup = (this.contextModes[blockType] & 3) << 9;
    }

    // A category with one block type has one block, as long as the largest meta-block (2^24), so a
    // stream that ends it has more literals or commands than a meta-block can hold: 2^24 commands fit
    // only if they have no output (a dictionary word that a transform cuts to nothing). The C source
    // reads nothing there and tests the block length again, with no error code for it; this throws, so
    // that the decoder cannot go on without reading. BrotliDecoder also rejects such a stream
    // (InvalidData, seen on Windows x64).
    private void DecodeLiteralBlockSwitch(ref BrotliBitReader br)
    {
        if (!this.DecodeBlockTypeAndLength(0, ref br))
        {
            BrotliThrowHelper.ThrowCorrupt("a meta-block has more literals than its length");
        }

        this.PrepareLiteralDecoding();
    }

    private void DecodeCommandBlockSwitch(ref BrotliBitReader br)
    {
        if (!this.DecodeBlockTypeAndLength(1, ref br))
        {
            BrotliThrowHelper.ThrowCorrupt("a meta-block has more commands than its length");
        }

        this.htreeCommand = this.insertCopyGroup.Htrees[this.blockTypeRb[3]];
    }

    // With one block type the block length goes below zero and is not looked at again, as in the C code,
    // where it wraps.
    private void DecodeDistanceBlockSwitch(ref BrotliBitReader br)
    {
        if (!this.DecodeBlockTypeAndLength(2, ref br))
        {
            return;
        }

        this.distContextMapSlice = this.blockTypeRb[5] << BrotliContext.DistanceContextBits;
        this.distHtreeIndex = this.distContextMap[this.distContextMapSlice + this.distanceContext];
    }

    private long UnwrittenBytes()
    {
        int end = this.pos > this.ringbufferSize ? this.ringbufferSize : this.pos;
        return (this.rbRoundtrips * this.ringbufferSize) + end - this.partialPosOut;
    }

    private bool WriteRingBuffer(Span<byte> destination, ref int written, bool force)
    {
        int start = (int)(this.partialPosOut & this.ringbufferMask);
        long toWrite = this.UnwrittenBytes();
        int numWritten = (int)Math.Min(destination.Length - written, toWrite);
        if (this.metaBlockRemainingLen < 0)
        {
            BrotliThrowHelper.ThrowCorrupt("a meta-block has more data than its length");
        }

        this.ringbuffer.AsSpan(start, numWritten).CopyTo(destination.Slice(written));
        written += numWritten;
        this.partialPosOut += numWritten;
        if (numWritten < toWrite)
        {
            return !(this.ringbufferSize == 1 << this.windowBits || force);
        }

        if (this.ringbufferSize == 1 << this.windowBits && this.pos >= this.ringbufferSize)
        {
            this.pos -= this.ringbufferSize;
            this.rbRoundtrips++;
            this.shouldWrapRingbuffer = this.pos != 0;
        }

        return true;
    }

    private void WrapRingBuffer()
    {
        if (this.shouldWrapRingbuffer)
        {
            this.ringbuffer.AsSpan(this.ringbufferSize, this.pos).CopyTo(this.ringbuffer);
            this.shouldWrapRingbuffer = false;
        }
    }

    // A new array is zeroed, so its last two bytes, which give the context of the first two
    // positions, are zero. The C code sets them.
    private void EnsureRingBuffer()
    {
        if (this.ringbufferSize == this.newRingbufferSize)
        {
            return;
        }

        byte[] oldRingbuffer = this.ringbuffer;
        this.ringbuffer = new byte[this.newRingbufferSize + RingBufferWriteAheadSlack];
        if (oldRingbuffer != null)
        {
            oldRingbuffer.AsSpan(0, this.pos).CopyTo(this.ringbuffer);
        }

        this.ringbufferSize = this.newRingbufferSize;
        this.ringbufferMask = this.newRingbufferSize - 1;
    }

    private void CalculateRingBufferSize()
    {
        int windowSize = 1 << this.windowBits;
        int size = windowSize;

        int minSize = this.ringbufferSize != 0 ? this.ringbufferSize : 1024;

        if (this.ringbufferSize == windowSize)
        {
            return;
        }

        int outputSize = (this.ringbuffer == null ? 0 : this.pos) + this.metaBlockRemainingLen;
        minSize = minSize < outputSize ? outputSize : minSize;
        while ((size >> 1) >= minSize)
        {
            size >>= 1;
        }

        this.newRingbufferSize = size;
    }

    private bool CopyUncompressedBlockToOutput(ref BrotliBitReader br, Span<byte> destination, ref int written)
    {
        for (; ;)
        {
            if (!this.uncompressedWrite)
            {
                int count = Math.Min(this.metaBlockRemainingLen, this.ringbufferSize - this.pos);
                br.CopyBytes(this.ringbuffer.AsSpan(this.pos, count));
                this.pos += count;
                this.metaBlockRemainingLen -= count;
                if (this.pos < 1 << this.windowBits)
                {
                    if (this.metaBlockRemainingLen != 0)
                    {
                        // A ring buffer below the window size holds the whole meta-block
                        // (CalculateRingBufferSize), so this is not reached. The C code asks for more input here.
                        BrotliThrowHelper.ThrowCorrupt("an uncompressed meta-block does not fit the ring buffer");
                    }

                    return true;
                }

                this.uncompressedWrite = true;
            }

            if (!this.WriteRingBuffer(destination, ref written, force: false))
            {
                return false;
            }

            if (this.ringbufferSize == 1 << this.windowBits)
            {
                this.maxDistance = this.maxBackwardDistance;
            }

            this.uncompressedWrite = false;
        }
    }

    private void TakeDistanceFromRingBuffer()
    {
        int offset = this.distanceCode - 3;
        if (this.distanceCode <= 3)
        {
            this.distanceContext = 1 >> this.distanceCode;
            this.distanceCode = this.distRb[(this.distRbIdx - offset) & 3];
            this.distRbIdx -= this.distanceContext;
        }
        else
        {
            int indexDelta = 3;
            int @base = this.distanceCode - 10;
            if (this.distanceCode < 10)
            {
                @base = this.distanceCode - 4;
            }
            else
            {
                indexDelta = 2;
            }

            int delta = ((0x605142 >> (4 * @base)) & 0xF) - 3;
            this.distanceCode = this.distRb[(this.distRbIdx + indexDelta) & 0x3] + delta;
            if (this.distanceCode <= 0)
            {
                this.distanceCode = 0x7FFFFFFF;
            }
        }
    }

    private void ReadDistance(ref BrotliBitReader br)
    {
        int code = br.ReadSymbol(this.distanceGroup.Codes, this.distanceGroup.Htrees[this.distHtreeIndex]);
        --this.blockLength[2];

        this.distanceContext = 0;
        if ((code & ~0xF) == 0)
        {
            this.distanceCode = code;
            this.TakeDistanceFromRingBuffer();
            return;
        }

        int bits = br.ReadBits(this.distExtraBits[code]);
        this.distanceCode = this.distOffset[code] + (bits << this.distancePostfixBits);
    }

    // ProcessCommandsInternal.
    private void ProcessCommands(ref BrotliBitReader br)
    {
        int pos = this.pos;
        int i = this.loopCounter;
        byte[] ringbuffer = this.ringbuffer;
        int ringbufferSize = this.ringbufferSize;
        int ringbufferMask = this.ringbufferMask;

        switch (this.state)
        {
            case State.CommandInner:
                goto CommandInner;
            case State.CommandPostDecodeLiterals:
                goto CommandPostDecodeLiterals;
            case State.CommandPostWrapCopy:
                goto CommandPostWrapCopy;
        }

    CommandBegin:
        if (this.blockLength[1] == 0)
        {
            this.DecodeCommandBlockSwitch(ref br);
            goto CommandBegin;
        }

        // Read the insert and copy length of the command (ReadCommandInternal).
        {
            BrotliCommand v = BrotliPrefix.Commands[br.ReadSymbol(this.insertCopyGroup.Codes, this.htreeCommand)];
            this.distanceCode = v.DistanceCode;
            this.distanceContext = v.Context;
            this.distHtreeIndex = this.distContextMap[this.distContextMapSlice + this.distanceContext];
            i = v.InsertLenOffset + br.ReadBits(v.InsertLenExtraBits);
            this.copyLength = v.CopyLenOffset + br.ReadBits(v.CopyLenExtraBits);
            --this.blockLength[1];
        }

        if (i == 0)
        {
            goto CommandPostDecodeLiterals;
        }

        this.metaBlockRemainingLen -= i;

    CommandInner:

        // The C code has a faster loop for the literals that need no check; here every literal takes the
        // checked path.
        if (this.trivialLiteralContext)
        {
            BrotliHuffmanCode[] codes = this.literalGroup.Codes;
            int htree = this.literalHtree;
            do
            {
                if (this.blockLength[0] == 0)
                {
                    this.DecodeLiteralBlockSwitch(ref br);
                    goto CommandInner;
                }

                ringbuffer[pos] = (byte)br.ReadSymbol(codes, htree);
                --this.blockLength[0];
                ++pos;
                if (pos == ringbufferSize)
                {
                    this.state = State.CommandInnerWrite;
                    --i;
                    goto SaveStateAndReturn;
                }
            }
            while (--i != 0);
        }
        else
        {
            BrotliHuffmanCode[] codes = this.literalGroup.Codes;
            int[] htrees = this.literalGroup.Htrees;
            ReadOnlySpan<byte> lookup = BrotliContext.Lookup.Slice(this.contextLookup, 512);
            byte p1 = ringbuffer[(pos - 1) & ringbufferMask];
            byte p2 = ringbuffer[(pos - 2) & ringbufferMask];
            do
            {
                if (this.blockLength[0] == 0)
                {
                    this.DecodeLiteralBlockSwitch(ref br);
                    goto CommandInner;
                }

                int context = lookup[p1] | lookup[256 + p2];
                int htree = htrees[this.contextMap[this.contextMapSlice + context]];
                p2 = p1;
                p1 = (byte)br.ReadSymbol(codes, htree);
                ringbuffer[pos] = p1;
                --this.blockLength[0];
                ++pos;
                if (pos == ringbufferSize)
                {
                    this.state = State.CommandInnerWrite;
                    --i;
                    goto SaveStateAndReturn;
                }
            }
            while (--i != 0);
        }

        if (this.metaBlockRemainingLen <= 0)
        {
            this.state = State.MetablockDone;
            goto SaveStateAndReturn;
        }

    CommandPostDecodeLiterals:
        if (this.distanceCode >= 0)
        {
            this.distanceContext = this.distanceCode != 0 ? 0 : 1;
            --this.distRbIdx;
            this.distanceCode = this.distRb[this.distRbIdx & 3];
        }
        else
        {
            if (this.blockLength[2] == 0)
            {
                this.DecodeDistanceBlockSwitch(ref br);
            }

            this.ReadDistance(ref br);
        }

        if (this.maxDistance != this.maxBackwardDistance)
        {
            this.maxDistance = pos < this.maxBackwardDistance ? pos : this.maxBackwardDistance;
        }

        i = this.copyLength;

        // RFC 7932: a distance beyond the largest backward distance refers to the static dictionary (section 8).
        if (this.distanceCode > this.maxDistance)
        {
            if (this.distanceCode > MaxAllowedDistance)
            {
                BrotliThrowHelper.ThrowCorrupt("a distance is too large");
            }

            if (i < BrotliDictionary.MinWordLength || i > BrotliDictionary.MaxWordLength)
            {
                BrotliThrowHelper.ThrowCorrupt("the dictionary has no word of that length");
            }

            int shift = BrotliDictionary.SizeBitsByLength[i];
            int address = this.distanceCode - this.maxDistance - 1;
            int wordIdx = address & ((1 << shift) - 1);
            int transformIdx = address >> shift;

            this.distRbIdx += this.distanceContext;
            if (transformIdx >= BrotliTransform.NumTransforms)
            {
                BrotliThrowHelper.ThrowCorrupt("the dictionary has no such transform");
            }

            ReadOnlySpan<byte> word = BrotliDictionary.Data.Slice(BrotliDictionary.OffsetsByLength[i] + (wordIdx * i), i);
            int len = i;
            if (transformIdx == BrotliTransform.Identity)
            {
                word.CopyTo(ringbuffer.AsSpan(pos));
            }
            else
            {
                len = BrotliTransform.TransformDictionaryWord(ringbuffer.AsSpan(pos), word, transformIdx);
                if (len == 0 && this.distanceCode <= 120)
                {
                    BrotliThrowHelper.ThrowCorrupt("a transform leaves nothing of a dictionary word");
                }
            }

            pos += len;
            this.metaBlockRemainingLen -= len;
            if (pos >= ringbufferSize)
            {
                this.state = State.CommandPostWrite1;
                goto SaveStateAndReturn;
            }
        }
        else
        {
            int srcStart = (pos - this.distanceCode) & ringbufferMask;
            int dstEnd = pos + i;
            int srcEnd = srcStart + i;

            this.distRb[this.distRbIdx & 3] = this.distanceCode;
            ++this.distRbIdx;
            this.metaBlockRemainingLen -= i;

            // The C code copies 16 bytes here before it looks at the regions, and more after. One copy of
            // the whole length gives the same bytes.
            if ((srcEnd > pos && dstEnd > srcStart) || dstEnd >= ringbufferSize || srcEnd >= ringbufferSize)
            {
                goto CommandPostWrapCopy;
            }

            ringbuffer.AsSpan(srcStart, i).CopyTo(ringbuffer.AsSpan(pos));
            pos += i;
        }

        if (this.metaBlockRemainingLen <= 0)
        {
            this.state = State.MetablockDone;
            goto SaveStateAndReturn;
        }

        goto CommandBegin;

    CommandPostWrapCopy:
        {
            int wrapGuard = ringbufferSize - pos;
            int distance = this.distanceCode;
            while (--i >= 0)
            {
                ringbuffer[pos] = ringbuffer[(pos - distance) & ringbufferMask];
                ++pos;
                if (--wrapGuard == 0)
                {
                    this.state = State.CommandPostWrite2;
                    goto SaveStateAndReturn;
                }
            }
        }

        if (this.metaBlockRemainingLen <= 0)
        {
            this.state = State.MetablockDone;
            goto SaveStateAndReturn;
        }

        goto CommandBegin;

    SaveStateAndReturn:
        this.pos = pos;
        this.loopCounter = i;
    }

    private sealed class HuffmanTreeGroup
    {
        public BrotliHuffmanCode[] Codes { get; private set; } = Array.Empty<BrotliHuffmanCode>();

        public int[] Htrees { get; private set; } = Array.Empty<int>();

        public int AlphabetSizeMax { get; private set; }

        public int AlphabetSizeLimit { get; private set; }

        public int NumHtrees { get; private set; }

        // BrotliDecoderHuffmanTreeGroupInit. The C code allocates for each meta-block; this keeps the
        // arrays of the meta-block before when they are large enough, as every entry that is read is
        // written first.
        public void Init(int alphabetSizeMax, int alphabetSizeLimit, int ntrees)
        {
            int codeSize = ntrees * (alphabetSizeLimit + MaxTableSizeOverAlphabet);
            if (this.Codes.Length < codeSize)
            {
                this.Codes = new BrotliHuffmanCode[codeSize];
            }

            if (this.Htrees.Length < ntrees)
            {
                this.Htrees = new int[ntrees];
            }

            this.AlphabetSizeMax = alphabetSizeMax;
            this.AlphabetSizeLimit = alphabetSizeLimit;
            this.NumHtrees = ntrees;
        }
    }
}
