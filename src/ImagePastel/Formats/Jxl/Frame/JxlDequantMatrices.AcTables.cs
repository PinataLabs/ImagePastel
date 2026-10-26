// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/quant_weights.h and lib/jxl/quant_weights.cc (DctQuantWeightParams, QuantEncoding, QuantTable,
// kAcStrategyToQuantTableMap, DequantMatrices: Decode, EnsureComputed, Matrix, InvMatrix, required_size_x/y;
// GetQuantWeightsDCT2, GetQuantWeightsIdentity, Interpolate, Mult, GetQuantWeights, ComputeQuantTable,
// DecodeDctParams and Decode). The library tables are in JxlDequantMatrices.Library.cs; a raw table's
// Modular stream is read by JxlModularFrameDecoder.DecodeQuantTable (lib/jxl/dec_modular.cc).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.VarDct;

namespace PinataLabs.ImagePastel.Formats.Jxl.Frame;

/// <content>
/// The AC dequantization tables, which the AC global section of a VarDCT frame codes.
/// </content>
/// <remarks>
/// Departure (ADR 0001): each table and its inverse are allocated when they are first computed, rather than
/// one block for all 17, so a frame allocates only the tables it uses. A table is still checked as libjxl
/// checks it.
/// </remarks>
internal sealed partial class JxlDequantMatrices
{
    /// <summary>The number of quantization tables.</summary>
    public const int NumQuantTables = 17;

    private const int NumPredefinedTables = 1;
    private const int CeilLog2NumPredefinedTables = 0;
    private const int Log2NumQuantModes = 3;
    private const float Sqrt2 = 1.41421356237f;

    private readonly JxlQuantEncoding[] encodings = CreateDefaultEncodings();
    private readonly float[][] tables = new float[NumQuantTables][];
    private readonly float[][] invTables = new float[NumQuantTables][];
    private uint computedMask;

    /// <summary>The ways a quantization table is coded.</summary>
    public enum QuantMode
    {
        /// <summary>A table from the library.</summary>
        Library,

        /// <summary>Weights for the identity transform.</summary>
        Id,

        /// <summary>Weights for DCT2.</summary>
        Dct2,

        /// <summary>Parametric weights for DCT4, with extra multipliers.</summary>
        Dct4,

        /// <summary>Parametric weights for DCT4X8, with an extra multiplier.</summary>
        Dct4X8,

        /// <summary>Weights for the corner DCT.</summary>
        Afv,

        /// <summary>Parametric weights from distance bands.</summary>
        Dct,

        /// <summary>An explicit table, as in JPEG.</summary>
        Raw,
    }

    /// <summary>Gets the quantization table of each raw strategy.</summary>
    public static ReadOnlySpan<byte> AcStrategyToQuantTable => new byte[]
    {
        0, 1, 2, 3, 4, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 10, 10, 11, 12, 12, 13, 14, 14, 15, 16, 16,
    };

    /// <summary>Gets the number of 8x8 blocks of each table along x (libjxl's <c>required_size_x</c>).</summary>
    public static ReadOnlySpan<byte> RequiredSizeX => new byte[] { 1, 1, 1, 1, 2, 4, 1, 1, 2, 1, 1, 8, 4, 16, 8, 32, 16 };

    /// <summary>Gets the number of 8x8 blocks of each table along y (libjxl's <c>required_size_y</c>).</summary>
    public static ReadOnlySpan<byte> RequiredSizeY => new byte[] { 1, 1, 1, 1, 2, 4, 2, 4, 4, 1, 1, 8, 8, 16, 16, 32, 32 };

    /// <summary>Gets the encoding of each table.</summary>
    public ReadOnlySpan<JxlQuantEncoding> Encodings => this.encodings;

    /// <summary>
    /// Returns the dequantization weights of channel <paramref name="c"/> for a strategy, whose table
    /// <see cref="EnsureComputed"/> has computed.
    /// </summary>
    /// <param name="quantKind">The strategy.</param>
    /// <param name="c">The channel.</param>
    /// <returns>The weights, one per coefficient.</returns>
    public ReadOnlySpan<float> Matrix(JxlAcStrategyType quantKind, int c)
    {
        int table = AcStrategyToQuantTable[(int)quantKind];
        int num = RequiredSizeX[table] * RequiredSizeY[table] * 64;
        return this.tables[table].AsSpan(c * num, num);
    }

    /// <summary>
    /// Returns the quantization weights of channel <paramref name="c"/> for a strategy, the inverse of
    /// <see cref="Matrix"/>, which the encoder quantises with. Its lowest-frequency entries are 0.
    /// </summary>
    /// <param name="quantKind">The strategy.</param>
    /// <param name="c">The channel.</param>
    /// <returns>The weights, one per coefficient.</returns>
    public ReadOnlySpan<float> InvMatrix(JxlAcStrategyType quantKind, int c)
    {
        int table = AcStrategyToQuantTable[(int)quantKind];
        int num = RequiredSizeX[table] * RequiredSizeY[table] * 64;
        return this.invTables[table].AsSpan(c * num, num);
    }

    /// <summary>Reads the encoding of every table from the AC global section.</summary>
    /// <param name="br">The reader.</param>
    /// <param name="modularFrameDecoder">
    /// The frame's Modular decoder, whose global tree, histograms and context map a raw table's stream uses.
    /// </param>
    public void Decode(JxlBitReader br, JxlModularFrameDecoder modularFrameDecoder)
    {
        bool allDefault = br.ReadBits(1) != 0;
        int numTables = allDefault ? 0 : NumQuantTables;
        for (int i = 0; i < NumQuantTables; i++)
        {
            this.encodings[i] = JxlQuantEncoding.Library(0);
        }

        for (int i = 0; i < numTables; i++)
        {
            this.encodings[i] = DecodeEncoding(br, RequiredSizeX[i % NumQuantTables], RequiredSizeY[i % NumQuantTables], i, modularFrameDecoder);
        }

        this.computedMask = 0;
    }

    /// <summary>Computes the tables of the strategies in <paramref name="acsMask"/> that are not computed yet.</summary>
    /// <param name="acsMask">The strategies, one bit per raw strategy.</param>
    public void EnsureComputed(uint acsMask)
    {
        uint kindMask = 0;
        for (int i = 0; i < JxlAcStrategy.NumValidStrategies; i++)
        {
            if ((acsMask & (1u << i)) != 0)
            {
                kindMask |= 1u << AcStrategyToQuantTable[i];
            }
        }

        uint computedKindMask = 0;
        for (int i = 0; i < JxlAcStrategy.NumValidStrategies; i++)
        {
            if ((this.computedMask & (1u << i)) != 0)
            {
                computedKindMask |= 1u << AcStrategyToQuantTable[i];
            }
        }

        for (int table = 0; table < NumQuantTables; table++)
        {
            if (((1u << table) & computedKindMask) != 0 || ((1u << table) & ~kindMask) != 0)
            {
                continue;
            }

            JxlQuantEncoding encoding = this.encodings[table].Mode == QuantMode.Library ? LibraryEncoding(table) : this.encodings[table];
            this.tables[table] = ComputeQuantTable(encoding, table, out this.invTables[table]);
        }

        this.computedMask |= acsMask;
    }

    private static JxlQuantEncoding[] CreateDefaultEncodings()
    {
        JxlQuantEncoding[] result = new JxlQuantEncoding[NumQuantTables];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = JxlQuantEncoding.Library(0);
        }

        return result;
    }

    private static void DecodeDctParams(JxlBitReader br, JxlDctQuantWeightParams parameters)
    {
        parameters.NumDistanceBands = (int)br.ReadBits(JxlDctQuantWeightParams.Log2MaxDistanceBands) + 1;
        for (int c = 0; c < 3; c++)
        {
            for (int i = 0; i < parameters.NumDistanceBands; i++)
            {
                parameters.DistanceBands[c][i] = JxlFieldCoders.ReadF16OrThrow(br);
            }

            if (parameters.DistanceBands[c][0] < AlmostZero)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Distance band seed is too small");
            }

            parameters.DistanceBands[c][0] *= 64.0f;
        }
    }

    private static JxlQuantEncoding DecodeEncoding(JxlBitReader br, int requiredSizeX, int requiredSizeY, int idx, JxlModularFrameDecoder modularFrameDecoder)
    {
        int requiredSize = requiredSizeX * requiredSizeY;
        QuantMode mode = (QuantMode)br.ReadBits(Log2NumQuantModes);
        JxlQuantEncoding encoding = new(mode);
        switch (mode)
        {
            case QuantMode.Library:
            {
                encoding.Predefined = (int)br.ReadBits(CeilLog2NumPredefinedTables);
                if (encoding.Predefined >= NumPredefinedTables)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid predefined table");
                }

                break;
            }

            case QuantMode.Id:
            {
                if (requiredSize != 1)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid mode");
                }

                ReadWeights(br, encoding.IdWeights, 3, "ID Quantizer is too small");
                break;
            }

            case QuantMode.Dct2:
            {
                if (requiredSize != 1)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid mode");
                }

                ReadWeights(br, encoding.Dct2Weights, 6, "Quantizer is too small");
                break;
            }

            case QuantMode.Dct4X8:
            {
                if (requiredSize != 1)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid mode");
                }

                for (int c = 0; c < 3; c++)
                {
                    encoding.Dct4X8Multipliers[c] = JxlFieldCoders.ReadF16OrThrow(br);
                    if (MathF.Abs(encoding.Dct4X8Multipliers[c]) < AlmostZero)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException("DCT4X8 multiplier is too small");
                    }
                }

                DecodeDctParams(br, encoding.DctParams);
                break;
            }

            case QuantMode.Dct4:
            {
                if (requiredSize != 1)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid mode");
                }

                for (int c = 0; c < 3; c++)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        encoding.Dct4Multipliers[c][i] = JxlFieldCoders.ReadF16OrThrow(br);
                        if (MathF.Abs(encoding.Dct4Multipliers[c][i]) < AlmostZero)
                        {
                            JxlThrowHelper.ThrowInvalidImageContentException("DCT4 multiplier is too small");
                        }
                    }
                }

                DecodeDctParams(br, encoding.DctParams);
                break;
            }

            case QuantMode.Afv:
            {
                if (requiredSize != 1)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid mode");
                }

                for (int c = 0; c < 3; c++)
                {
                    for (int i = 0; i < 9; i++)
                    {
                        encoding.AfvWeights[c][i] = JxlFieldCoders.ReadF16OrThrow(br);
                    }

                    for (int i = 0; i < 6; i++)
                    {
                        encoding.AfvWeights[c][i] *= 64;
                    }
                }

                DecodeDctParams(br, encoding.DctParams);
                DecodeDctParams(br, encoding.DctParamsAfv4X4);
                break;
            }

            case QuantMode.Dct:
            {
                DecodeDctParams(br, encoding.DctParams);
                break;
            }

            case QuantMode.Raw:
            {
                // The table has one entry per coefficient, so it is 8 times the size in blocks each way.
                modularFrameDecoder.DecodeQuantTable(requiredSizeX * JxlFrameDimensions.BlockDim, requiredSizeY * JxlFrameDimensions.BlockDim, br, encoding, idx);
                break;
            }

            default:
            {
                // The mode has 3 bits, and all 8 modes exist.
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid quantization table encoding");
                break;
            }
        }

        return encoding;
    }

    private static void ReadWeights(JxlBitReader br, float[][] weights, int count, string tooSmall)
    {
        for (int c = 0; c < 3; c++)
        {
            for (int i = 0; i < count; i++)
            {
                weights[c][i] = JxlFieldCoders.ReadF16OrThrow(br);
                if (MathF.Abs(weights[c][i]) < AlmostZero)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException(tooSmall);
                }

                weights[c][i] *= 64;
            }
        }
    }

    // ComputeQuantTable: the weights of the table (the inverse table), then their reciprocals.
    private static float[] ComputeQuantTable(JxlQuantEncoding encoding, int quantTableIdx, out float[] invTable)
    {
        const int n = 8;
        int wrows = 8 * RequiredSizeX[quantTableIdx];
        int wcols = 8 * RequiredSizeY[quantTableIdx];
        int num = wrows * wcols;

        float[] weights = new float[3 * num];

        switch (encoding.Mode)
        {
            case QuantMode.Library:
            {
                // Library and copy quant encoding should get replaced by the actual parameters by the
                // caller.
                JxlThrowHelper.ThrowInvalidImageContentException("Library quant encoding was not resolved");
                break;
            }

            case QuantMode.Id:
            {
                EnsureBlockSize(num);
                GetQuantWeightsIdentity(encoding.IdWeights, weights);
                break;
            }

            case QuantMode.Dct2:
            {
                EnsureBlockSize(num);
                GetQuantWeightsDct2(encoding.Dct2Weights, weights);
                break;
            }

            case QuantMode.Dct4:
            {
                EnsureBlockSize(num);
                float[] weights4X4 = new float[3 * 4 * 4];

                // Always use 4x4 GetQuantWeights for DCT4 quantization tables.
                GetQuantWeights(4, 4, encoding.DctParams.DistanceBands, encoding.DctParams.NumDistanceBands, weights4X4);
                for (int c = 0; c < 3; c++)
                {
                    for (int y = 0; y < n; y++)
                    {
                        for (int x = 0; x < n; x++)
                        {
                            weights[(c * num) + (y * n) + x] = weights4X4[(c * 16) + ((y / 2) * 4) + (x / 2)];
                        }
                    }

                    weights[(c * num) + 1] /= encoding.Dct4Multipliers[c][0];
                    weights[(c * num) + n] /= encoding.Dct4Multipliers[c][0];
                    weights[(c * num) + n + 1] /= encoding.Dct4Multipliers[c][1];
                }

                break;
            }

            case QuantMode.Dct4X8:
            {
                EnsureBlockSize(num);
                float[] weights4X8 = new float[3 * 4 * 8];

                // Always use 4x8 GetQuantWeights for DCT4X8 quantization tables.
                GetQuantWeights(4, 8, encoding.DctParams.DistanceBands, encoding.DctParams.NumDistanceBands, weights4X8);
                for (int c = 0; c < 3; c++)
                {
                    for (int y = 0; y < n; y++)
                    {
                        for (int x = 0; x < n; x++)
                        {
                            weights[(c * num) + (y * n) + x] = weights4X8[(c * 32) + ((y / 2) * 8) + x];
                        }
                    }

                    weights[(c * num) + n] /= encoding.Dct4X8Multipliers[c];
                }

                break;
            }

            case QuantMode.Dct:
            {
                GetQuantWeights(wrows, wcols, encoding.DctParams.DistanceBands, encoding.DctParams.NumDistanceBands, weights);
                break;
            }

            case QuantMode.Raw:
            {
                int[] qtable = encoding.QTable;
                if (qtable == null || qtable.Length != 3 * num)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid table encoding");
                }

                for (int i = 0; i < 3 * num; i++)
                {
                    weights[i] = 1.0f / (encoding.QTableDen * qtable[i]);
                }

                break;
            }

            case QuantMode.Afv:
            {
                ComputeAfvWeights(encoding, weights, num);
                break;
            }
        }

        float[] table = new float[3 * num];
        for (int i = 0; i < num * 3; i++)
        {
            float invVal = weights[i];
            if (invVal >= 1.0f / AlmostZero || invVal < AlmostZero)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid quantization table");
            }

            table[i] = 1.0f / invVal;
        }

        // Ensure that the lowest frequencies have a 0 inverse table. This does not affect en/decoding, but
        // allows AC strategy selection to be slightly simpler. It also keeps the encoder's quantised
        // lowest frequencies at 0, so the chroma-from-luma step does not subtract from the B DC twice.
        int xs = RequiredSizeX[quantTableIdx];
        int ys = RequiredSizeY[quantTableIdx];
        JxlAcStrategy.CoefficientLayout(ref ys, ref xs);
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < ys; y++)
            {
                for (int x = 0; x < xs; x++)
                {
                    weights[(c * ys * xs * 64) + (y * 8 * xs) + x] = 0;
                }
            }
        }

        invTable = weights;
        return table;
    }

    private static void EnsureBlockSize(int num)
    {
        // libjxl's JXL_ENSURE: Decode allows these modes only for tables of one block.
        if (num != 64)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Quant mode needs a table of one 8x8 block");
        }
    }

    private static void ComputeAfvWeights(JxlQuantEncoding encoding, float[] weights, int num)
    {
        ReadOnlySpan<float> freqs = stackalloc float[]
        {
            0xBAD, 0xBAD, 0.8517778890324296f, 5.37778436506804f,
            0xBAD, 0xBAD, 4.734747904497923f, 5.449245381693219f,
            1.6598270267479331f, 4, 7.275749096817861f, 10.423227632456525f,
            2.662932286148962f, 7.630657783650829f, 8.962388608184032f, 12.97166202570235f,
        };

        float[] weights4X8 = new float[3 * 4 * 8];
        GetQuantWeights(4, 8, encoding.DctParams.DistanceBands, encoding.DctParams.NumDistanceBands, weights4X8);
        float[] weights4X4 = new float[3 * 4 * 4];
        GetQuantWeights(4, 4, encoding.DctParamsAfv4X4.DistanceBands, encoding.DctParamsAfv4X4.NumDistanceBands, weights4X4);

        const float lo = 0.8517778890324296f;
        const float hi = 12.97166202570235f - lo + 1e-6f;
        Span<float> bands = stackalloc float[4];
        for (int c = 0; c < 3; c++)
        {
            bands[0] = encoding.AfvWeights[c][5];
            if (bands[0] < AlmostZero)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid AFV bands");
            }

            for (int i = 1; i < 4; i++)
            {
                bands[i] = bands[i - 1] * Mult(encoding.AfvWeights[c][i + 5]);
                if (bands[i] < AlmostZero)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid AFV bands");
                }
            }

            int start = c * 64;
            void SetWeight(int x, int y, float val) => weights[start + (y * 8) + x] = val;

            weights[start] = 1; // Not used, but causes MSAN error otherwise.

            // Weights for (0, 1) and (1, 0).
            SetWeight(0, 1, encoding.AfvWeights[c][0]);
            SetWeight(1, 0, encoding.AfvWeights[c][1]);

            // AFV special weights for 3-pixel corner.
            SetWeight(0, 2, encoding.AfvWeights[c][2]);
            SetWeight(2, 0, encoding.AfvWeights[c][3]);
            SetWeight(2, 2, encoding.AfvWeights[c][4]);

            // All other AFV weights.
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    if (x < 2 && y < 2)
                    {
                        continue;
                    }

                    float val = Interpolate(freqs[(y * 4) + x] - lo, hi, bands);
                    SetWeight(2 * x, 2 * y, val);
                }
            }

            // Put 4x8 weights in odd rows, except (1, 0).
            for (int y = 0; y < 8 / 2; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (x == 0 && y == 0)
                    {
                        continue;
                    }

                    weights[(c * num) + (((2 * y) + 1) * 8) + x] = weights4X8[(c * 32) + (y * 8) + x];
                }
            }

            // Put 4x4 weights in even rows / odd columns, except (0, 1).
            for (int y = 0; y < 8 / 2; y++)
            {
                for (int x = 0; x < 8 / 2; x++)
                {
                    if (x == 0 && y == 0)
                    {
                        continue;
                    }

                    weights[(c * num) + (2 * y * 8) + (2 * x) + 1] = weights4X4[(c * 16) + (y * 4) + x];
                }
            }
        }
    }

    private static void GetQuantWeightsDct2(float[][] dct2Weights, float[] weights)
    {
        for (int c = 0; c < 3; c++)
        {
            int start = c * 64;
            weights[start] = 0xBAD;
            weights[start + 1] = weights[start + 8] = dct2Weights[c][0];
            weights[start + 9] = dct2Weights[c][1];
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    weights[start + (y * 8) + x + 2] = dct2Weights[c][2];
                    weights[start + ((y + 2) * 8) + x] = dct2Weights[c][2];
                }
            }

            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    weights[start + ((y + 2) * 8) + x + 2] = dct2Weights[c][3];
                }
            }

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    weights[start + (y * 8) + x + 4] = dct2Weights[c][4];
                    weights[start + ((y + 4) * 8) + x] = dct2Weights[c][4];
                }
            }

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    weights[start + ((y + 4) * 8) + x + 4] = dct2Weights[c][5];
                }
            }
        }
    }

    private static void GetQuantWeightsIdentity(float[][] idWeights, float[] weights)
    {
        for (int c = 0; c < 3; c++)
        {
            for (int i = 0; i < 64; i++)
            {
                weights[(64 * c) + i] = idWeights[c][0];
            }

            weights[(64 * c) + 1] = idWeights[c][1];
            weights[(64 * c) + 8] = idWeights[c][1];
            weights[(64 * c) + 9] = idWeights[c][2];
        }
    }

    private static float Mult(float v)
    {
        if (v > 0.0f)
        {
            return 1.0f + v;
        }

        return 1.0f / (1.0f - v);
    }

    private static float Interpolate(float pos, float max, ReadOnlySpan<float> array)
    {
        float scaledPos = pos * (array.Length - 1) / max;
        int idx = (int)scaledPos;
        if (idx + 1 >= array.Length)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Interpolation position is out of range");
        }

        float a = array[idx];
        float b = array[idx + 1];
        return a * FastPowf(b / a, scaledPos - idx);
    }

    // Computes quant weights for a COLS*ROWS-sized transform, using num_bands distance bands.
    private static void GetQuantWeights(int rows, int cols, float[][] distanceBands, int numBands, float[] output)
    {
        Span<float> bands = stackalloc float[JxlDctQuantWeightParams.MaxDistanceBands];
        for (int c = 0; c < 3; c++)
        {
            bands.Clear();
            bands[0] = distanceBands[c][0];
            if (bands[0] < AlmostZero)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid distance bands");
            }

            for (int i = 1; i < numBands; i++)
            {
                bands[i] = bands[i - 1] * Mult(distanceBands[c][i]);
                if (bands[i] < AlmostZero)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid distance bands");
                }
            }

            float scale = (numBands - 1) / (Sqrt2 + 1e-6f);
            float rcpcol = scale / (cols - 1);
            float rcprow = scale / (rows - 1);
            for (int y = 0; y < rows; y++)
            {
                float dy = y * rcprow;
                float dy2 = dy * dy;
                for (int x = 0; x < cols; x++)
                {
                    float weight;
                    if (numBands == 1)
                    {
                        weight = bands[0];
                    }
                    else
                    {
                        // libjxl's MulAdd here is a fused multiply-add on AVX2; this port does not fuse
                        // (ADR 0004). (x + l) * rcpcol is libjxl's lane arithmetic, in float.
                        float dx = (float)x * rcpcol;
                        float scaledDistance = MathF.Sqrt((dx * dx) + dy2);

                        // InterpolateVec: libjxl reads past the bands without a check, which the
                        // range of scaledDistance keeps inside them.
                        int idx = (int)scaledDistance;
                        float frac = scaledDistance - idx;
                        float a = bands[idx];
                        float b = bands[idx + 1];
                        weight = a * FastPowf(b / a, frac);
                    }

                    output[(c * cols * rows) + (y * cols) + x] = weight;
                }
            }
        }
    }

    // libjxl's FastPowf from lib/jxl/base/fast_math-inl.h, as the scalar and the vector Interpolate call it.
    private static float FastPowf(float b, float e) => JxlFastMath.FastPowf(b, e);
}

/// <summary>The distance bands of a parametric quantization table.</summary>
internal sealed class JxlDctQuantWeightParams
{
    /// <summary>Log2 of the largest number of distance bands, less one.</summary>
    public const int Log2MaxDistanceBands = 4;

    /// <summary>The largest number of distance bands.</summary>
    public const int MaxDistanceBands = 1 + (1 << Log2MaxDistanceBands);

    /// <summary>Gets or sets the number of distance bands.</summary>
    public int NumDistanceBands { get; set; }

    /// <summary>Gets the distance bands of each channel.</summary>
    public float[][] DistanceBands { get; } = { new float[MaxDistanceBands], new float[MaxDistanceBands], new float[MaxDistanceBands] };
}

/// <summary>How one quantization table is coded, as libjxl's <c>QuantEncoding</c>.</summary>
/// <remarks>libjxl keeps the per-mode weights in a union; here each has its own array.</remarks>
internal sealed class JxlQuantEncoding
{
    public JxlQuantEncoding(JxlDequantMatrices.QuantMode mode) => this.Mode = mode;

    /// <summary>Gets the mode.</summary>
    public JxlDequantMatrices.QuantMode Mode { get; }

    /// <summary>Gets the weights for DCT4 and larger tables.</summary>
    public JxlDctQuantWeightParams DctParams { get; } = new();

    /// <summary>Gets the weights for the identity transform.</summary>
    public float[][] IdWeights { get; } = { new float[3], new float[3], new float[3] };

    /// <summary>Gets the weights for DCT2.</summary>
    public float[][] Dct2Weights { get; } = { new float[6], new float[6], new float[6] };

    /// <summary>Gets the extra multipliers for coefficients 01/10 and 11 of DCT4.</summary>
    public float[][] Dct4Multipliers { get; } = { new float[2], new float[2], new float[2] };

    /// <summary>
    /// Gets the AFV weights: {0, 1} for coefficients (0, 1) and (1, 0); {2, 3, 4} for the corner DC,
    /// (1,0) - (0,1) and (0, 1) + (1, 0) - (0, 0) inside the AFV block; 5 to 8 are interpolated.
    /// </summary>
    public float[][] AfvWeights { get; } = { new float[9], new float[9], new float[9] };

    /// <summary>Gets the extra multipliers for coefficients 01 or 10 of DCT4X8 and DCT8X4.</summary>
    public float[] Dct4X8Multipliers { get; } = new float[3];

    /// <summary>Gets the weights for the 4x4 sub-block of AFV.</summary>
    public JxlDctQuantWeightParams DctParamsAfv4X4 { get; } = new();

    /// <summary>Gets or sets the predefined table to use, for <see cref="JxlDequantMatrices.QuantMode.Library"/>.</summary>
    public int Predefined { get; set; }

    /// <summary>
    /// Gets or sets the denominator of a <see cref="JxlDequantMatrices.QuantMode.Raw"/> table: each weight is
    /// 1 / (denominator x entry).
    /// </summary>
    public float QTableDen { get; set; }

    /// <summary>
    /// Gets or sets the entries of a <see cref="JxlDequantMatrices.QuantMode.Raw"/> table, channel by channel,
    /// each channel row by row; every entry is positive.
    /// </summary>
    public int[] QTable { get; set; }

    /// <summary>Returns a library encoding.</summary>
    /// <param name="predefined">The predefined table.</param>
    /// <returns>The encoding.</returns>
    public static JxlQuantEncoding Library(int predefined) => new(JxlDequantMatrices.QuantMode.Library) { Predefined = predefined };
}
