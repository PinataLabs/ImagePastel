// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Brotli (https://github.com/google/brotli) at tag v1.2.0 (commit 028fb5a2):
// c/dec/decode.c, c/dec/state.h and c/dec/state.c.
// Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace PinataLabs.ImagePastel.Compression.Brotli;

/// <content>
/// Reads the header of a meta-block: its length, and for a compressed meta-block its block types,
/// context maps and prefix codes.
/// </content>
internal sealed partial class ManagedBrotliDecoder
{
    private static int Log2Floor(int x)
    {
        int result = 0;
        while (x != 0)
        {
            x >>= 1;
            ++result;
        }

        return result;
    }

    // Decodes a number in the range [0..255], by reading 1 - 11 bits.
    private static int DecodeVarLenUint8(ref BrotliBitReader br)
    {
        if (br.ReadBits(1) == 0)
        {
            return 0;
        }

        int bits = br.ReadBits(3);
        return bits == 0 ? 1 : (1 << bits) + br.ReadBits(bits);
    }

    // The C version re-initializes only the elements that an earlier call changed (mtf_upper_bound).
    private static void InverseMoveToFrontTransform(Span<byte> v)
    {
        Span<byte> mtf = stackalloc byte[256];
        for (int i = 0; i < mtf.Length; i++)
        {
            mtf[i] = (byte)i;
        }

        for (int i = 0; i < v.Length; i++)
        {
            int index = v[i];
            byte value = mtf[index];
            v[i] = value;
            for (; index > 0; index--)
            {
                mtf[index] = mtf[index - 1];
            }

            mtf[0] = value;
        }
    }

    // BrotliDecoderStateMetablockBegin, and the meta-block header states of
    // BrotliDecoderDecompressStream up to BROTLI_STATE_BEFORE_COMPRESSED_METABLOCK_BODY.
    private void ReadMetablockHeader(ref BrotliBitReader br)
    {
        this.metaBlockRemainingLen = 0;
        this.blockLength.AsSpan().Fill(BlockSizeCap);
        this.numBlockTypes.AsSpan().Fill(1);
        for (int i = 0; i < 3; i++)
        {
            this.blockTypeRb[i * 2] = 1;
            this.blockTypeRb[(i * 2) + 1] = 0;
        }

        this.distHtreeIndex = 0;

        this.DecodeMetaBlockLength(ref br);
        if ((this.isMetadata || this.isUncompressed) && !br.JumpToByteBoundary())
        {
            BrotliThrowHelper.ThrowCorrupt("the padding before the data of a meta-block is not zero");
        }

        if (this.isMetadata)
        {
            br.SkipBytes(this.metaBlockRemainingLen);
            this.metaBlockRemainingLen = 0;
            this.state = State.MetablockDone;
            return;
        }

        if (this.metaBlockRemainingLen == 0)
        {
            this.state = State.MetablockDone;
            return;
        }

        this.CalculateRingBufferSize();
        if (this.isUncompressed)
        {
            this.EnsureRingBuffer();
            this.uncompressedWrite = false;
            this.state = State.Uncompressed;
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            // Reads 1..11 bits.
            this.numBlockTypes[i] = DecodeVarLenUint8(ref br) + 1;
            if (this.numBlockTypes[i] < 2)
            {
                continue;
            }

            int alphabetSize = this.numBlockTypes[i] + 2;
            this.ReadHuffmanCode(alphabetSize, alphabetSize, this.blockTypeTrees, i * BrotliHuffman.MaxSize258, ref br);
            this.ReadHuffmanCode(BrotliPrefix.NumBlockLengthSymbols, BrotliPrefix.NumBlockLengthSymbols, this.blockLenTrees, i * BrotliHuffman.MaxSize26, ref br);
            this.blockLength[i] = ReadBlockLength(this.blockLenTrees, i * BrotliHuffman.MaxSize26, ref br);
        }

        int bits = br.ReadBits(6);
        this.distancePostfixBits = bits & 3;
        this.numDirectDistanceCodes = (bits >> 2) << this.distancePostfixBits;

        // Reads 1..256 2-bit context modes.
        this.contextModes = new byte[this.numBlockTypes[0]];
        for (int i = 0; i < this.contextModes.Length; i++)
        {
            this.contextModes[i] = (byte)br.ReadBits(2);
        }

        this.contextMap = this.DecodeContextMap(this.numBlockTypes[0] << BrotliContext.LiteralContextBits, out int numLiteralHtrees, ref br);
        this.DetectTrivialLiteralBlockTypes();

        this.distContextMap = this.DecodeContextMap(this.numBlockTypes[2] << BrotliContext.DistanceContextBits, out int numDistHtrees, ref br);

        // BROTLI_DISTANCE_ALPHABET_SIZE
        int distanceAlphabetSize = NumDistanceShortCodes + this.numDirectDistanceCodes + (MaxDistanceBits << (this.distancePostfixBits + 1));
        this.literalGroup.Init(NumLiteralSymbols, NumLiteralSymbols, numLiteralHtrees);
        this.insertCopyGroup.Init(BrotliPrefix.NumCommandSymbols, BrotliPrefix.NumCommandSymbols, this.numBlockTypes[1]);
        this.distanceGroup.Init(distanceAlphabetSize, distanceAlphabetSize, numDistHtrees);
        this.HuffmanTreeGroupDecode(this.literalGroup, ref br);
        this.HuffmanTreeGroupDecode(this.insertCopyGroup, ref br);
        this.HuffmanTreeGroupDecode(this.distanceGroup, ref br);

        this.PrepareLiteralDecoding();
        this.distContextMapSlice = 0;
        this.htreeCommand = this.insertCopyGroup.Htrees[0];
        this.EnsureRingBuffer();
        this.CalculateDistanceLut();
        this.state = State.CommandBegin;
    }

    // Decodes a meta-block length and flags by reading 2 - 31 bits.
    private void DecodeMetaBlockLength(ref BrotliBitReader br)
    {
        this.isLastMetablock = br.ReadBits(1) != 0;
        this.metaBlockRemainingLen = 0;
        this.isUncompressed = false;
        this.isMetadata = false;
        if (this.isLastMetablock && br.ReadBits(1) != 0)
        {
            // The last meta-block is empty.
            return;
        }

        int bits = br.ReadBits(2);
        if (bits == 3)
        {
            this.isMetadata = true;
            if (br.ReadBits(1) != 0)
            {
                BrotliThrowHelper.ThrowCorrupt("the reserved bit of a metadata meta-block is set");
            }

            int sizeBytes = br.ReadBits(2);
            if (sizeBytes == 0)
            {
                return;
            }

            for (int i = 0; i < sizeBytes; ++i)
            {
                bits = br.ReadBits(8);
                if (i + 1 == sizeBytes && sizeBytes > 1 && bits == 0)
                {
                    BrotliThrowHelper.ThrowCorrupt("the length of a metadata meta-block has a zero byte at its top");
                }

                this.metaBlockRemainingLen |= bits << (i * 8);
            }

            ++this.metaBlockRemainingLen;
            return;
        }

        int sizeNibbles = bits + 4;
        for (int i = 0; i < sizeNibbles; ++i)
        {
            bits = br.ReadBits(4);
            if (i + 1 == sizeNibbles && sizeNibbles > 4 && bits == 0)
            {
                BrotliThrowHelper.ThrowCorrupt("the length of a meta-block has a zero nibble at its top");
            }

            this.metaBlockRemainingLen |= bits << (i * 4);
        }

        if (!this.isLastMetablock)
        {
            this.isUncompressed = br.ReadBits(1) != 0;
        }

        ++this.metaBlockRemainingLen;
    }

    // Decodes the Huffman tables.
    // There are 2 scenarios:
    //  A) The code has only few symbols (1..4). Those symbols are read directly; their code lengths are
    //     defined by the number of symbols.
    //  B) 2-phase decoding:
    //  B.1) A small table is decoded; it is specified with code lengths encoded with a predefined code.
    //  B.2) That table is used to decode the code lengths of the symbols of the resulting table.
    // Returns the size of the table.
    private int ReadHuffmanCode(int alphabetSizeMax, int alphabetSizeLimit, BrotliHuffmanCode[] table, int offset, ref BrotliBitReader br)
    {
        // 1 for a simple code; 0 for no skipping, 2 skips 2 code lengths, 3 skips 3 code lengths.
        int skip = br.ReadBits(2);
        if (skip == 1)
        {
            // Read symbols, codes and code lengths directly.
            int numSymbols = br.ReadBits(2);
            Span<ushort> symbols = this.simpleSymbols;
            this.ReadSimpleHuffmanSymbols(alphabetSizeMax, alphabetSizeLimit, numSymbols, symbols, ref br);
            if (numSymbols == 3)
            {
                numSymbols += br.ReadBits(1);
            }

            return BrotliHuffman.BuildSimpleHuffmanTable(table, offset, HuffmanTableBits, symbols, numSymbols);
        }

        // Decode Huffman-coded code lengths.
        Span<ushort> histo = this.codeLengthHisto;
        histo.Clear();
        this.ReadCodeLengthCodeLengths(skip, ref br);
        BrotliHuffman.BuildCodeLengthsHuffmanTable(this.codeLengthTable, this.codeLengthCodeLengths, histo);
        histo.Clear();
        for (int i = 0; i <= BrotliHuffman.MaxCodeLength; ++i)
        {
            this.nextSymbol[i] = i - (BrotliHuffman.MaxCodeLength + 1);
            this.symbolLists[BrotliHuffman.SymbolListsBase + this.nextSymbol[i]] = 0xFFFF;
        }

        this.ReadSymbolCodeLengths(alphabetSizeLimit, ref br);
        return BrotliHuffman.BuildHuffmanTable(table, offset, HuffmanTableBits, this.symbolLists, histo);
    }

    // Reads numSymbols + 1 symbols: 1..4 symbols, 1..11 bits each. They must not contain duplicates.
    private void ReadSimpleHuffmanSymbols(int alphabetSizeMax, int alphabetSizeLimit, int numSymbols, Span<ushort> symbols, ref BrotliBitReader br)
    {
        int maxBits = Log2Floor(alphabetSizeMax - 1);
        for (int i = 0; i <= numSymbols; ++i)
        {
            int v = br.ReadBits(maxBits);
            if (v >= alphabetSizeLimit)
            {
                BrotliThrowHelper.ThrowCorrupt("a symbol of a simple prefix code is outside the alphabet");
            }

            symbols[i] = (ushort)v;
        }

        for (int i = 0; i < numSymbols; ++i)
        {
            for (int k = i + 1; k <= numSymbols; ++k)
            {
                if (symbols[i] == symbols[k])
                {
                    BrotliThrowHelper.ThrowCorrupt("a simple prefix code has the same symbol twice");
                }
            }
        }
    }

    // Reads and decodes 15..18 codes using a static prefix code. Each code is 2..4 bits long.
    private void ReadCodeLengthCodeLengths(int skip, ref BrotliBitReader br)
    {
        Span<byte> codeLengths = this.codeLengthCodeLengths;
        codeLengths.Clear();
        int numCodes = 0;
        int space = 32;
        for (int i = skip; i < BrotliHuffman.CodeLengthCodes; ++i)
        {
            int ix = br.PeekBits(4);
            int v = CodeLengthPrefixValue[ix];
            br.DropBits(CodeLengthPrefixLength[ix]);
            codeLengths[CodeLengthCodeOrder[i]] = (byte)v;
            if (v != 0)
            {
                space -= 32 >> v;
                ++numCodes;
                ++this.codeLengthHisto[v];
                if (space <= 0)
                {
                    break;
                }
            }
        }

        if (!(numCodes == 1 || space == 0))
        {
            BrotliThrowHelper.ThrowCorrupt("the code of the code lengths is not complete");
        }
    }

    // Reads and decodes the code lengths of the symbols. The symbols of each code length are linked into
    // a chain in symbolLists, and counted in codeLengthHisto.
    private void ReadSymbolCodeLengths(int alphabetSize, ref BrotliBitReader br)
    {
        const int repeatPreviousCodeLength = 16;
        const int initialRepeatedCodeLength = 8;

        int symbol = 0;
        int prevCodeLen = initialRepeatedCodeLength;
        int repeat = 0;
        int repeatCodeLen = 0;

        // The part of the code space that is free, in units of a code of 15 bits.
        int space = 32768;
        Span<ushort> lists = this.symbolLists;
        Span<ushort> histo = this.codeLengthHisto;
        Span<int> next = this.nextSymbol;
        while (symbol < alphabetSize && space > 0)
        {
            BrotliHuffmanCode p = this.codeLengthTable[br.PeekBits(BrotliHuffman.MaxCodeLengthCodeLength)];
            br.DropBits(p.Bits);
            int codeLen = p.Value;
            if (codeLen < repeatPreviousCodeLength)
            {
                // ProcessSingleCodeLength
                repeat = 0;
                if (codeLen != 0)
                {
                    lists[BrotliHuffman.SymbolListsBase + next[codeLen]] = (ushort)symbol;
                    next[codeLen] = symbol;
                    prevCodeLen = codeLen;
                    space -= 32768 >> codeLen;
                    histo[codeLen]++;
                }

                symbol++;
                continue;
            }

            // ProcessRepeatedCodeLength: 16 repeats the previous length, 17 repeats a zero length.
            int extraBits = codeLen - 14;
            int newLen = codeLen == repeatPreviousCodeLength ? prevCodeLen : 0;
            int repeatDelta = br.ReadBits(extraBits);
            if (repeatCodeLen != newLen)
            {
                repeat = 0;
                repeatCodeLen = newLen;
            }

            int oldRepeat = repeat;
            if (repeat > 0)
            {
                repeat -= 2;
                repeat <<= extraBits;
            }

            repeat += repeatDelta + 3;
            repeatDelta = repeat - oldRepeat;
            if (symbol + repeatDelta > alphabetSize)
            {
                BrotliThrowHelper.ThrowCorrupt("a repeated code length runs past the alphabet");
            }

            if (repeatCodeLen != 0)
            {
                int last = symbol + repeatDelta;
                int nextIndex = next[repeatCodeLen];
                do
                {
                    lists[BrotliHuffman.SymbolListsBase + nextIndex] = (ushort)symbol;
                    nextIndex = symbol;
                }
                while (++symbol != last);

                next[repeatCodeLen] = nextIndex;
                space -= repeatDelta << (15 - repeatCodeLen);
                histo[repeatCodeLen] = (ushort)(histo[repeatCodeLen] + repeatDelta);
            }
            else
            {
                symbol += repeatDelta;
            }
        }

        // The C code keeps the space unsigned, so a code that takes more than the space wraps it to a
        // large value, which this check then finds. Here the space goes negative.
        if (space != 0)
        {
            BrotliThrowHelper.ThrowCorrupt("a prefix code is not complete");
        }
    }

    // Decodes a series of Huffman tables.
    private void HuffmanTreeGroupDecode(HuffmanTreeGroup group, ref BrotliBitReader br)
    {
        int next = 0;
        for (int i = 0; i < group.NumHtrees; i++)
        {
            group.Htrees[i] = next;
            next += this.ReadHuffmanCode(group.AlphabetSizeMax, group.AlphabetSizeLimit, group.Codes, next, ref br);
        }
    }

    // Decodes a context map.
    //  1) Read auxiliary information (6..16 bits). A trivial context map is finished here.
    //  2) Decode the Huffman table that the context map items are read with.
    //  3) Read the context map items; "0" values can be run-length encoded.
    //  4) Optionally, apply the inverse move-to-front transform to the resulting map.
    private byte[] DecodeContextMap(int contextMapSize, out int numHtrees, ref BrotliBitReader br)
    {
        numHtrees = DecodeVarLenUint8(ref br) + 1;
        byte[] contextMap = new byte[contextMapSize];
        if (numHtrees <= 1)
        {
            return contextMap;
        }

        int maxRunLengthPrefix = 0;
        int bits = br.PeekBits(5);
        if ((bits & 1) != 0)
        {
            // Use RLE for zeros.
            maxRunLengthPrefix = (bits >> 1) + 1;
            br.DropBits(5);
        }
        else
        {
            br.DropBits(1);
        }

        int alphabetSize = numHtrees + maxRunLengthPrefix;
        this.ReadHuffmanCode(alphabetSize, alphabetSize, this.contextMapTable, 0, ref br);

        int contextIndex = 0;
        while (contextIndex < contextMapSize)
        {
            int code = br.ReadSymbol(this.contextMapTable, 0);
            if (code == 0)
            {
                contextMap[contextIndex++] = 0;
                continue;
            }

            if (code > maxRunLengthPrefix)
            {
                contextMap[contextIndex++] = (byte)(code - maxRunLengthPrefix);
                continue;
            }

            // RLE sub-stage. The array is new, so its zeros are already there.
            int reps = br.ReadBits(code) + (1 << code);
            if (contextIndex + reps > contextMapSize)
            {
                BrotliThrowHelper.ThrowCorrupt("a run of zeros runs past the context map");
            }

            contextIndex += reps;
        }

        if (br.ReadBits(1) != 0)
        {
            InverseMoveToFrontTransform(contextMap);
        }

        return contextMap;
    }

    private void DetectTrivialLiteralBlockTypes()
    {
        this.trivialLiteralContexts.AsSpan().Clear();
        for (int i = 0; i < this.numBlockTypes[0]; i++)
        {
            ReadOnlySpan<byte> contexts = this.contextMap.AsSpan(i << BrotliContext.LiteralContextBits, 1 << BrotliContext.LiteralContextBits);
            int error = 0;
            foreach (byte context in contexts)
            {
                error |= context ^ contexts[0];
            }

            if (error == 0)
            {
                this.trivialLiteralContexts[i >> 5] |= 1u << (i & 31);
            }
        }
    }

    // Calculates the lookup table of the distance codes: the number of extra bits of each, and the
    // distance that its extra bits add to.
    private void CalculateDistanceLut()
    {
        int npostfix = this.distancePostfixBits;
        int ndirect = this.numDirectDistanceCodes;
        int alphabetSizeLimit = this.distanceGroup.AlphabetSizeLimit;
        int postfix = 1 << npostfix;
        int bits = 1;
        int half = 0;

        // Skip short codes.
        int i = NumDistanceShortCodes;

        // Fill direct codes.
        for (int j = 0; j < ndirect; ++j)
        {
            this.distExtraBits[i] = 0;
            this.distOffset[i] = j + 1;
            ++i;
        }

        // Fill regular distance codes.
        while (i < alphabetSizeLimit)
        {
            int @base = ndirect + ((((2 + half) << bits) - 4) << npostfix) + 1;

            // Always fill the complete group.
            for (int j = 0; j < postfix; ++j)
            {
                this.distExtraBits[i] = (byte)bits;
                this.distOffset[i] = @base + j;
                ++i;
            }

            bits += half;
            half ^= 1;
        }
    }
}
