// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/enc_ma.h and lib/jxl/modular/encoding/enc_ma.cc (TreeSamples, QuantizeHistogram,
// QuantizeSamples, QuantMap).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular;

/// <summary>
/// All the data needed to build a tree: for each sampled pixel, the residual token of each predictor
/// tried and the quantised value of each property, with equal samples merged into one with a count.
/// </summary>
internal sealed class JxlTreeSamples
{
    // Mapping property value -> quantized property value.
    private const int PropertyRange = 511;

    // Table for deduplication.
    private const uint DedupEntryUnused = uint.MaxValue;

    // TODO(veluca): as the total number of properties and predictors are known before adding any
    // samples, it might be better to interleave predictors, properties and counts in a single vector
    // to improve locality. A first attempt at doing this actually results in much slower encoding,
    // possibly because of the more complex addressing.
    // Residual information: token and number of extra bits, per predictor.
    private readonly List<List<ResidualToken>> residuals = new();

    // Number of occurrences of each sample.
    private readonly List<ushort> sampleCounts = new();

    // Quantized static property values
    private readonly List<uint>[] staticProps = { new(), new() };

    private readonly ushort[][] staticPropertyMapping = new ushort[JxlModularPredict.NumStaticProperties][];

    // List of predictors to use.
    private readonly List<JxlPredictor> predictors = new();

    private int numStaticProps;

    // Property values, quantized to at most 256 distinct values.
    private List<byte>[] props = Array.Empty<List<byte>>();

    // Decompactification info for `props`.
    private int[][] compactProperties = Array.Empty<int[]>();

    // List of properties to use.
    private List<uint> propsToUse = new();

    private byte[][] propertyMapping = Array.Empty<byte[]>();

    private uint[] dedupTable = Array.Empty<uint>();

    /// <summary>Gets a value indicating whether any sample was added.</summary>
    public bool HasSamples => this.residuals.Count > 0 && this.residuals[0].Count > 0;

    /// <summary>Gets the number of distinct samples.</summary>
    public int NumDistinctSamples => this.sampleCounts.Count;

    /// <summary>Gets the number of samples added, equal ones included.</summary>
    public long NumSamples { get; private set; }

    /// <summary>Gets the number of static properties among the properties used.</summary>
    public int NumStaticProps => this.numStaticProps;

    /// <summary>Gets the number of predictors tried.</summary>
    public int NumPredictors => this.predictors.Count;

    /// <summary>Gets the number of properties used.</summary>
    public int NumProperties => this.propsToUse.Count;

    /// <summary>Returns the residual tokens of predictor <paramref name="pred"/>.</summary>
    /// <param name="pred">The predictor index.</param>
    /// <returns>The tokens.</returns>
    public List<ResidualToken> RTokens(int pred) => this.residuals[pred];

    /// <summary>Returns the residual token of sample <paramref name="i"/> under predictor <paramref name="pred"/>.</summary>
    /// <param name="pred">The predictor index.</param>
    /// <param name="i">The sample.</param>
    /// <returns>The token.</returns>
    public int Token(int pred, int i) => this.residuals[pred][i].Tok;

    /// <summary>Returns how many times sample <paramref name="i"/> occurred.</summary>
    /// <param name="i">The sample.</param>
    /// <returns>The count.</returns>
    public int Count(int i) => this.sampleCounts[i];

    /// <summary>Returns the index of <paramref name="predictor"/> among the predictors tried.</summary>
    /// <param name="predictor">The predictor.</param>
    /// <returns>The index.</returns>
    public int PredictorIndex(JxlPredictor predictor)
    {
        int index = this.predictors.IndexOf(predictor);
        DebugGuard.IsTrue(index >= 0, "The predictor is one of those tried");
        return index;
    }

    /// <summary>Returns the number of quantised values of the property at <paramref name="propertyIndex"/>.</summary>
    /// <param name="propertyIndex">The property index.</param>
    /// <returns>The number of values.</returns>
    public int NumPropertyValues(int propertyIndex) => this.compactProperties[propertyIndex].Length + 1;

    /// <summary>
    /// Returns the *quantized* property value of sample <paramref name="i"/>. With
    /// <paramref name="isStatic"/>, <paramref name="propertyIndex"/> counts the static properties;
    /// otherwise it counts the others.
    /// </summary>
    /// <param name="isStatic">Whether the property is static.</param>
    /// <param name="propertyIndex">The property index.</param>
    /// <param name="i">The sample.</param>
    /// <returns>The quantised value.</returns>
    public int Property(bool isStatic, int propertyIndex, int i)
        => isStatic ? (int)this.staticProps[propertyIndex][i] : this.props[propertyIndex][i];

    /// <summary>Returns the property value that the quantised value <paramref name="quant"/> stands for.</summary>
    /// <param name="propertyIndex">The property index.</param>
    /// <param name="quant">The quantised value.</param>
    /// <returns>The property value.</returns>
    public int UnquantizeProperty(int propertyIndex, uint quant)
    {
        DebugGuard.IsTrue(quant < this.compactProperties[propertyIndex].Length, "The value is in range");
        return this.compactProperties[propertyIndex][quant];
    }

    /// <summary>Returns the predictor at <paramref name="index"/>.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The predictor.</returns>
    public JxlPredictor PredictorFromIndex(int index) => this.predictors[index];

    /// <summary>Returns the property at <paramref name="index"/>.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The property.</returns>
    public uint PropertyFromIndex(int index) => this.propsToUse[index];

    /// <summary>Sets the predictors to try. Must be called before adding any samples.</summary>
    /// <param name="predictor">The predictor option.</param>
    /// <param name="wpTreeMode">The tree mode.</param>
    public void SetPredictor(JxlPredictor predictor, JxlModularOptions.TreeMode wpTreeMode)
    {
        if (wpTreeMode == JxlModularOptions.TreeMode.WPOnly)
        {
            this.predictors.Clear();
            this.predictors.Add(JxlPredictor.Weighted);
            this.ResizeResiduals(1);
            return;
        }

        if (wpTreeMode == JxlModularOptions.TreeMode.NoWP && predictor == JxlPredictor.Weighted)
        {
            JxlThrowHelper.ThrowImageFormatException("Invalid predictor settings");
        }

        if (predictor == JxlPredictor.Variable)
        {
            for (int i = 0; i < JxlModularPredict.NumModularPredictors; i++)
            {
                this.predictors.Add((JxlPredictor)i);
            }

            (this.predictors[0], this.predictors[(int)JxlPredictor.Weighted]) = (this.predictors[(int)JxlPredictor.Weighted], this.predictors[0]);
            (this.predictors[1], this.predictors[(int)JxlPredictor.Gradient]) = (this.predictors[(int)JxlPredictor.Gradient], this.predictors[1]);
        }
        else if (predictor == JxlPredictor.Best)
        {
            this.predictors.Add(JxlPredictor.Weighted);
            this.predictors.Add(JxlPredictor.Gradient);
        }
        else
        {
            this.predictors.Add(predictor);
        }

        if (wpTreeMode == JxlModularOptions.TreeMode.NoWP)
        {
            this.predictors.RemoveAll(p => p == JxlPredictor.Weighted);
        }

        this.ResizeResiduals(this.predictors.Count);
    }

    /// <summary>Sets the properties to use. Must be called before adding any samples.</summary>
    /// <param name="properties">The properties, the static ones first.</param>
    /// <param name="wpTreeMode">The tree mode.</param>
    public void SetProperties(List<uint> properties, JxlModularOptions.TreeMode wpTreeMode)
    {
        this.propsToUse = new List<uint>(properties);
        if (wpTreeMode == JxlModularOptions.TreeMode.WPOnly)
        {
            this.propsToUse = new List<uint> { (uint)JxlModularPredict.WPProp };
        }

        if (wpTreeMode == JxlModularOptions.TreeMode.GradientOnly)
        {
            this.propsToUse = new List<uint> { (uint)JxlModularPredict.GradientProp };
        }

        if (wpTreeMode == JxlModularOptions.TreeMode.NoWP)
        {
            this.propsToUse.RemoveAll(p => p == JxlModularPredict.WPProp);
        }

        if (this.propsToUse.Count == 0)
        {
            JxlThrowHelper.ThrowImageFormatException("Invalid property set configuration");
        }

        this.numStaticProps = 0;

        // Check that if static properties present, then those are at the beginning.
        for (int i = 0; i < this.propsToUse.Count; ++i)
        {
            uint prop = this.propsToUse[i];
            if (prop < JxlModularPredict.NumStaticProperties)
            {
                DebugGuard.IsTrue(i == prop, "The static properties come first");
                this.numStaticProps++;
            }
        }

        this.props = new List<byte>[this.propsToUse.Count - this.numStaticProps];
        for (int i = 0; i < this.props.Length; i++)
        {
            this.props[i] = new List<byte>();
        }
    }

    /// <summary>
    /// Preallocates data for a given number of samples, and grows the deduplication table. MUST be
    /// called before adding any sample.
    /// </summary>
    /// <param name="extraNumSamples">The number of samples about to be added.</param>
    public void PrepareForSamples(ulong extraNumSamples)
    {
        int extra = (int)Math.Min(extraNumSamples, int.MaxValue / 2);
        foreach (List<ResidualToken> res in this.residuals)
        {
            res.Capacity = Math.Max(res.Capacity, res.Count + extra);
        }

        for (int i = 0; i < this.numStaticProps; ++i)
        {
            this.staticProps[i].Capacity = Math.Max(this.staticProps[i].Capacity, this.staticProps[i].Count + extra);
        }

        foreach (List<byte> p in this.props)
        {
            p.Capacity = Math.Max(p.Capacity, p.Count + extra);
        }

        ulong totalNumSamples = extraNumSamples + (ulong)this.sampleCounts.Count;
        int nextSize = JxlBits.CeilLog2Nonzero(totalNumSamples * 3 / 2);
        this.InitTable(nextSize);
    }

    /// <summary>Adds a sample, or counts it again when an equal one was added before.</summary>
    /// <param name="pixel">The sample value.</param>
    /// <param name="properties">The properties of the sample, indexed by property.</param>
    /// <param name="predictions">The prediction of each predictor, indexed by predictor.</param>
    public void AddSample(long pixel, int[] properties, ReadOnlySpan<long> predictions)
    {
        JxlHybridUintConfig config = new(4, 1, 2);
        for (int i = 0; i < this.predictors.Count; i++)
        {
            int v = unchecked((int)(pixel - predictions[(int)this.predictors[i]]));
            config.Encode(JxlBits.PackSigned(v), out uint tok, out uint nbits, out _);
            DebugGuard.IsTrue(tok < 256 && nbits < 256, "The token fits a byte");
            this.residuals[i].Add(new ResidualToken((byte)tok, (byte)nbits));
        }

        for (int i = 0; i < this.numStaticProps; ++i)
        {
            this.staticProps[i].Add(this.QuantizeStaticProperty(i, properties[i]));
        }

        for (int i = this.numStaticProps; i < this.propsToUse.Count; i++)
        {
            this.props[i - this.numStaticProps].Add(this.QuantizeProperty(i, properties[this.propsToUse[i]]));
        }

        this.sampleCounts.Add(1);
        this.NumSamples++;
        if (this.AddToTableAndMerge(this.sampleCounts.Count - 1))
        {
            foreach (List<ResidualToken> r in this.residuals)
            {
                r.RemoveAt(r.Count - 1);
            }

            for (int i = 0; i < this.numStaticProps; ++i)
            {
                this.staticProps[i].RemoveAt(this.staticProps[i].Count - 1);
            }

            foreach (List<byte> p in this.props)
            {
                p.RemoveAt(p.Count - 1);
            }

            this.sampleCounts.RemoveAt(this.sampleCounts.Count - 1);
        }
    }

    /// <summary>Frees the deduplication table once every sample is in.</summary>
    public void AllSamplesDone() => this.dedupTable = Array.Empty<uint>();

    /// <summary>Returns the quantised value of the non-static property at <paramref name="prop"/>.</summary>
    /// <param name="prop">The property index.</param>
    /// <param name="v">The property value.</param>
    /// <returns>The quantised value.</returns>
    public byte QuantizeProperty(int prop, int v)
    {
        DebugGuard.IsTrue(prop >= this.numStaticProps, "The property is not static");
        v = Math.Clamp(v, -PropertyRange, PropertyRange) + PropertyRange;
        return this.propertyMapping[prop - this.numStaticProps][v];
    }

    /// <summary>Returns the quantised value of the static property at <paramref name="prop"/>.</summary>
    /// <param name="prop">The property index.</param>
    /// <param name="v">The property value.</param>
    /// <returns>The quantised value.</returns>
    public ushort QuantizeStaticProperty(int prop, int v)
    {
        DebugGuard.IsTrue(prop < this.numStaticProps, "The property is static");
        v = Math.Clamp(v, -PropertyRange, PropertyRange) + PropertyRange;
        return this.staticPropertyMapping[prop][v];
    }

    /// <summary>Swaps samples in position a and b. Does nothing if a == b.</summary>
    /// <param name="a">The first sample.</param>
    /// <param name="b">The second sample.</param>
    public void Swap(int a, int b)
    {
        if (a == b)
        {
            return;
        }

        foreach (List<ResidualToken> r in this.residuals)
        {
            (r[a], r[b]) = (r[b], r[a]);
        }

        for (int i = 0; i < this.numStaticProps; ++i)
        {
            List<uint> s = this.staticProps[i];
            (s[a], s[b]) = (s[b], s[a]);
        }

        foreach (List<byte> p in this.props)
        {
            (p[a], p[b]) = (p[b], p[a]);
        }

        (this.sampleCounts[a], this.sampleCounts[b]) = (this.sampleCounts[b], this.sampleCounts[a]);
    }

    /// <summary>
    /// Pre-clusters the property values: picks at most <paramref name="maxPropertyValues"/> - 1
    /// thresholds for each property, from the pixel counts of the channels and streams and from the
    /// sampled pixel values and differences, and the maps from property values to quantised values.
    /// </summary>
    /// <param name="range">The channels and streams the tree covers.</param>
    /// <param name="multiplierInfo">The multiplier boxes, whose edges become thresholds.</param>
    /// <param name="groupPixelCount">The pixel count of each stream.</param>
    /// <param name="channelPixelCount">The pixel count of each channel.</param>
    /// <param name="pixelSamples">Sampled pixel values; replaced by their absolute values.</param>
    /// <param name="diffSamples">Sampled differences to the left neighbour; replaced by their absolute values.</param>
    /// <param name="maxPropertyValues">The most quantised values per property.</param>
    public void PreQuantizeProperties(
        JxlStaticPropRange range,
        List<JxlModularMultiplierInfo> multiplierInfo,
        List<uint> groupPixelCount,
        List<uint> channelPixelCount,
        List<int> pixelSamples,
        List<int> diffSamples,
        int maxPropertyValues)
    {
        // If we have forced splits because of multipliers, choose channel and group thresholds
        // accordingly.
        List<int> groupMultiplierThresholds = new();
        List<int> channelMultiplierThresholds = new();
        foreach (JxlModularMultiplierInfo v in multiplierInfo)
        {
            if (v.Range[0, 0] != range[0, 0])
            {
                channelMultiplierThresholds.Add((int)v.Range[0, 0] - 1);
            }

            if (v.Range[0, 1] != range[0, 1])
            {
                channelMultiplierThresholds.Add((int)v.Range[0, 1] - 1);
            }

            if (v.Range[1, 0] != range[1, 0])
            {
                groupMultiplierThresholds.Add((int)v.Range[1, 0] - 1);
            }

            if (v.Range[1, 1] != range[1, 1])
            {
                groupMultiplierThresholds.Add((int)v.Range[1, 1] - 1);
            }
        }

        SortUnique(channelMultiplierThresholds);
        SortUnique(groupMultiplierThresholds);

        this.compactProperties = new int[this.propsToUse.Count][];
        int[] QuantizeChannel()
            => channelMultiplierThresholds.Count > 0 ? channelMultiplierThresholds.ToArray() : QuantizeHistogram(channelPixelCount, maxPropertyValues);

        int[] QuantizeGroupId()
            => groupMultiplierThresholds.Count > 0 ? groupMultiplierThresholds.ToArray() : QuantizeHistogram(groupPixelCount, maxPropertyValues);

        int[] QuantizeCoordinate()
        {
            int[] quantized = new int[maxPropertyValues - 1];
            for (int i = 0; i + 1 < maxPropertyValues; i++)
            {
                quantized[i] = ((i + 1) * 256 / maxPropertyValues) - 1;
            }

            return quantized;
        }

        int[] absPixelThresholds = null;
        int[] pixelThresholds = null;
        int[] QuantizePixelProperty() => pixelThresholds ??= QuantizeSamples(pixelSamples, maxPropertyValues);

        int[] QuantizeAbsPixelProperty()
        {
            if (absPixelThresholds == null)
            {
                QuantizePixelProperty(); // Compute the non-abs thresholds.
                Abs(pixelSamples);
                absPixelThresholds = QuantizeSamples(pixelSamples, maxPropertyValues);
            }

            return absPixelThresholds;
        }

        int[] absDiffThresholds = null;
        int[] diffThresholds = null;
        int[] QuantizeDiffProperty() => diffThresholds ??= QuantizeSamples(diffSamples, maxPropertyValues);

        int[] QuantizeAbsDiffProperty()
        {
            if (absDiffThresholds == null)
            {
                QuantizeDiffProperty(); // Compute the non-abs thresholds.
                Abs(diffSamples);
                absDiffThresholds = QuantizeSamples(diffSamples, maxPropertyValues);
            }

            return absDiffThresholds;
        }

        int[] QuantizeWp()
        {
            if (maxPropertyValues < 32)
            {
                return new[] { -127, -63, -31, -15, -7, -3, -1, 0, 1, 3, 7, 15, 31, 63, 127 };
            }

            if (maxPropertyValues < 64)
            {
                return new[]
                {
                    -255, -191, -127, -95, -63, -47, -31, -23, -15, -11, -7, -5, -3, -1, 0, 1,
                    3, 5, 7, 11, 15, 23, 31, 47, 63, 95, 127, 191, 255,
                };
            }

            return new[]
            {
                -255, -223, -191, -159, -127, -111, -95, -79, -63, -55, -47,
                -39, -31, -27, -23, -19, -15, -13, -11, -9, -7, -6,
                -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5,
                6, 7, 9, 11, 13, 15, 19, 23, 27, 31, 39,
                47, 55, 63, 79, 95, 111, 127, 159, 191, 223, 255,
            };
        }

        const int numNonref = JxlModularPredict.NumNonrefProperties;
        this.propertyMapping = new byte[this.propsToUse.Count - this.numStaticProps][];
        for (int i = 0; i < this.propsToUse.Count; i++)
        {
            uint p = this.propsToUse[i];
            if (p == 0)
            {
                this.compactProperties[i] = QuantizeChannel();
            }
            else if (p == 1)
            {
                this.compactProperties[i] = QuantizeGroupId();
            }
            else if (p is 2 or 3)
            {
                this.compactProperties[i] = QuantizeCoordinate();
            }
            else if (p is 6 or 7 or 8 || (p >= numNonref && (p - numNonref) % 4 == 1))
            {
                this.compactProperties[i] = QuantizePixelProperty();
            }
            else if (p is 4 or 5 || (p >= numNonref && (p - numNonref) % 4 == 0))
            {
                this.compactProperties[i] = QuantizeAbsPixelProperty();
            }
            else if (p >= numNonref && (p - numNonref) % 4 == 2)
            {
                this.compactProperties[i] = QuantizeAbsDiffProperty();
            }
            else if (p == JxlModularPredict.WPProp)
            {
                this.compactProperties[i] = QuantizeWp();
            }
            else
            {
                this.compactProperties[i] = QuantizeDiffProperty();
            }

            if (i < this.numStaticProps)
            {
                this.staticPropertyMapping[i] = Array.ConvertAll(QuantMap(this.compactProperties[i], (PropertyRange * 2) + 1, PropertyRange), v => (ushort)v);
            }
            else
            {
                this.propertyMapping[i - this.numStaticProps] = Array.ConvertAll(QuantMap(this.compactProperties[i], (PropertyRange * 2) + 1, PropertyRange), v => (byte)v);
            }
        }
    }

    private void ResizeResiduals(int count)
    {
        while (this.residuals.Count < count)
        {
            this.residuals.Add(new List<ResidualToken>());
        }

        if (this.residuals.Count > count)
        {
            this.residuals.RemoveRange(count, this.residuals.Count - count);
        }
    }

    private void InitTable(int logSize)
    {
        int size = 1 << logSize;
        if (this.dedupTable.Length == size)
        {
            return;
        }

        // std::vector::resize keeps the entries it has and fills the new ones.
        int oldSize = this.dedupTable.Length;
        Array.Resize(ref this.dedupTable, size);
        if (size > oldSize)
        {
            Array.Fill(this.dedupTable, DedupEntryUnused, oldSize, size - oldSize);
        }

        for (int i = 0; i < this.NumDistinctSamples; i++)
        {
            if (this.sampleCounts[i] != ushort.MaxValue)
            {
                this.AddToTable(i);
            }
        }
    }

    // Returns true if `a` was already present in the table.
    private bool AddToTableAndMerge(int a)
    {
        int pos1 = this.Hash1(a);
        int pos2 = this.Hash2(a);
        if (this.dedupTable[pos1] != DedupEntryUnused && this.IsSameSample(a, (int)this.dedupTable[pos1]))
        {
            this.MergeInto(pos1);
            return true;
        }

        if (this.dedupTable[pos2] != DedupEntryUnused && this.IsSameSample(a, (int)this.dedupTable[pos2]))
        {
            this.MergeInto(pos2);
            return true;
        }

        this.AddToTable(a);
        return false;
    }

    private void MergeInto(int pos)
    {
        int entry = (int)this.dedupTable[pos];
        this.sampleCounts[entry]++;

        // Remove from hash table samples that are saturated.
        if (this.sampleCounts[entry] == ushort.MaxValue)
        {
            this.dedupTable[pos] = DedupEntryUnused;
        }
    }

    private void AddToTable(int a)
    {
        int pos1 = this.Hash1(a);
        int pos2 = this.Hash2(a);
        if (this.dedupTable[pos1] == DedupEntryUnused)
        {
            this.dedupTable[pos1] = (uint)a;
        }
        else if (this.dedupTable[pos2] == DedupEntryUnused)
        {
            this.dedupTable[pos2] = (uint)a;
        }
    }

    private int Hash1(int a)
    {
        const ulong constant = 0x1e35a7bd;
        ulong h = constant;
        unchecked
        {
            foreach (List<ResidualToken> r in this.residuals)
            {
                h = (h * constant) + r[a].Tok;
                h = (h * constant) + r[a].NBits;
            }

            for (int i = 0; i < this.numStaticProps; ++i)
            {
                h = (h * constant) + this.staticProps[i][a];
            }

            foreach (List<byte> p in this.props)
            {
                h = (h * constant) + p[a];
            }
        }

        return (int)((h >> 16) & (ulong)(this.dedupTable.Length - 1));
    }

    private int Hash2(int a)
    {
        const ulong constant = 0x1e35a7bd1e35a7bd;
        ulong h = constant;
        unchecked
        {
            for (int i = 0; i < this.numStaticProps; ++i)
            {
                h = (h * constant) ^ this.staticProps[i][a];
            }

            foreach (List<byte> p in this.props)
            {
                h = (h * constant) ^ p[a];
            }

            foreach (List<ResidualToken> r in this.residuals)
            {
                h = (h * constant) ^ r[a].Tok;
                h = (h * constant) ^ r[a].NBits;
            }
        }

        return (int)((h >> 16) & (ulong)(this.dedupTable.Length - 1));
    }

    private bool IsSameSample(int a, int b)
    {
        foreach (List<ResidualToken> r in this.residuals)
        {
            if (r[a].Tok != r[b].Tok || r[a].NBits != r[b].NBits)
            {
                return false;
            }
        }

        for (int i = 0; i < this.numStaticProps; ++i)
        {
            if (this.staticProps[i][a] != this.staticProps[i][b])
            {
                return false;
            }
        }

        foreach (List<byte> p in this.props)
        {
            if (p[a] != p[b])
            {
                return false;
            }
        }

        return true;
    }

    private static void SortUnique(List<int> values)
    {
        values.Sort();
        int n = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (n == 0 || values[i] != values[n - 1])
            {
                values[n++] = values[i];
            }
        }

        values.RemoveRange(n, values.Count - n);
    }

    private static void Abs(List<int> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            values[i] = Math.Abs(values[i]);
        }
    }

    private static int[] QuantizeHistogram(List<uint> histogram, int numChunks)
    {
        if (histogram.Count == 0 || numChunks == 0)
        {
            return Array.Empty<int>();
        }

        ulong sum = 0;
        foreach (uint h in histogram)
        {
            sum += h;
        }

        if (sum == 0)
        {
            return Array.Empty<int>();
        }

        // TODO(veluca): selecting distinct quantiles is likely not the best way to go about this.
        List<int> thresholds = new();
        ulong cumsum = 0;
        ulong threshold = 1;
        for (int i = 0; i < histogram.Count; i++)
        {
            cumsum += histogram[i];
            if (cumsum * (ulong)numChunks >= threshold * sum)
            {
                thresholds.Add(i);
                while (cumsum * (ulong)numChunks >= threshold * sum)
                {
                    threshold++;
                }
            }
        }

        DebugGuard.IsTrue(thresholds.Count <= numChunks, "At most one threshold per chunk");

        // last value collects all histogram and is not really a threshold
        thresholds.RemoveAt(thresholds.Count - 1);
        return thresholds.ToArray();
    }

    private static int[] QuantizeSamples(List<int> samples, int numChunks)
    {
        if (samples.Count == 0)
        {
            return Array.Empty<int>();
        }

        const int range = 512;
        int min = int.MaxValue;
        foreach (int s in samples)
        {
            min = Math.Min(min, s);
        }

        min = Math.Clamp(min, -range, range);
        List<uint> counts = new(new uint[(2 * range) + 1]);
        foreach (int s in samples)
        {
            int sampleOffset = Math.Clamp(s, -range, range) - min;
            counts[sampleOffset]++;
        }

        int[] thresholds = QuantizeHistogram(counts, numChunks);
        for (int i = 0; i < thresholds.Length; i++)
        {
            thresholds[i] += min;
        }

        return thresholds;
    }

    // `to[i]` is assigned value `v` conforming `from[v] <= i && from[v-1] > i`. This is because the
    // decision node in the tree splits on (property) > i, hence everything that is not > of a
    // threshold should be clustered together.
    // libjxl's template writes uint16_t or uint8_t entries; the callers narrow these.
    private static int[] QuantMap(int[] from, int numPegs, int bias)
    {
        int[] to = new int[numPegs];
        int mapped = 0;
        for (int i = 0; i < numPegs; i++)
        {
            while (mapped < from.Length && i - bias > from[mapped])
            {
                mapped++;
            }

            to[i] = mapped;
        }

        return to;
    }

    /// <summary>The residual of one sample under one predictor: its hybrid uint token and extra bit count.</summary>
    internal readonly struct ResidualToken
    {
        public ResidualToken(byte tok, byte nbits)
        {
            this.Tok = tok;
            this.NBits = nbits;
        }

        public byte Tok { get; }

        public byte NBits { get; }
    }
}
