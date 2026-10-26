// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_context_map.h, lib/jxl/dec_context_map.cc and lib/jxl/inverse_mtf-inl.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>Reads the map from contexts to histograms (clusters).</summary>
internal static class JxlContextMap
{
    /// <summary>The largest number of clusters a context map may use.</summary>
    public const int MaxClusters = 256;

    /// <summary>
    /// Fills <paramref name="contextMap"/>, whose length is the number of contexts. Throws
    /// <see cref="InvalidImageContentException"/> for an invalid map.
    /// </summary>
    /// <param name="contextMap">The map to fill.</param>
    /// <param name="input">The reader.</param>
    /// <returns>The number of histograms the map uses.</returns>
    public static int DecodeContextMap(byte[] contextMap, JxlBitReader input)
    {
        bool isSimple = input.ReadBits(1) != 0;
        if (isSimple)
        {
            int bitsPerEntry = (int)input.ReadBits(2);
            if (bitsPerEntry != 0)
            {
                for (int i = 0; i < contextMap.Length; i++)
                {
                    contextMap[i] = (byte)input.ReadBits(bitsPerEntry);
                }
            }
            else
            {
                Array.Clear(contextMap, 0, contextMap.Length);
            }
        }
        else
        {
            bool useMtf = input.ReadBits(1) != 0;
            JxlAnsCode code = new();
            byte[] sinkCtxMap = JxlEntropyDecoder.DecodeHistograms(input, 1, code, disallowLz77: contextMap.Length <= 2);
            JxlAnsSymbolReader reader = new(code, input);
            uint maxSym = 0;
            for (int i = 0; i < contextMap.Length; i++)
            {
                uint sym = reader.ReadHybridUint(0, input, sinkCtxMap);
                maxSym = sym > maxSym ? sym : maxSym;
                contextMap[i] = (byte)sym;
            }

            if (maxSym >= MaxClusters)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid cluster ID");
            }

            if (!reader.CheckAnsFinalState)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid context map");
            }

            if (useMtf)
            {
                InverseMoveToFrontTransform(contextMap);
            }
        }

        int numHtrees = 0;
        foreach (byte htree in contextMap)
        {
            numHtrees = Math.Max(numHtrees, htree + 1);
        }

        VerifyContextMap(contextMap, numHtrees);
        return numHtrees;
    }

    // Every histogram index below num_htrees must be used.
    private static void VerifyContextMap(byte[] contextMap, int numHtrees)
    {
        Span<bool> haveHtree = stackalloc bool[MaxClusters];
        haveHtree.Clear();
        int numFound = 0;
        foreach (byte htree in contextMap)
        {
            if (htree >= numHtrees)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid histogram index in context map.");
            }

            if (!haveHtree[htree])
            {
                haveHtree[htree] = true;
                ++numFound;
            }
        }

        if (numFound != numHtrees)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Incomplete context map.");
        }
    }

    // libjxl's move-to-front uses Highway vectors; the scalar version gives the same result.
    private static void InverseMoveToFrontTransform(byte[] v)
    {
        Span<byte> mtf = stackalloc byte[256];
        for (int i = 0; i < 256; ++i)
        {
            mtf[i] = (byte)i;
        }

        for (int i = 0; i < v.Length; ++i)
        {
            byte index = v[i];
            v[i] = mtf[index];
            if (index != 0)
            {
                MoveToFront(mtf, index);
            }
        }
    }

    private static void MoveToFront(Span<byte> v, byte index)
    {
        byte value = v[index];
        for (int i = index; i != 0; --i)
        {
            v[i] = v[i - 1];
        }

        v[0] = value;
    }
}
