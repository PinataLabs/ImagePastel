// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Little CMS (https://github.com/mm2/Little-CMS) at commit 5176347 (vendored in libjxl
// third_party/lcms): fclamp, Eval1InputFloat, BilinearInterpFloat, TrilinearInterpFloat,
// TetrahedralInterpFloat and Eval4InputsFloat in src/cmsintrp.c, and the grid strides of
// _cmsComputeInterpParamsEx in the same file.
// Copyright (c) Marti Maria Saguer. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Numerics;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// Looks values up in a color lookup table (CLUT) of one to four inputs and one to four outputs,
/// interpolating between grid points as Little CMS does: linearly for one input, bilinearly for
/// two, tetrahedrally for three (or trilinearly when asked), and for four inputs tetrahedrally in
/// the last three at the two grid planes around the first, then linearly between them.
/// </summary>
/// <remarks>
/// Inputs are clamped to [0, 1]. The outputs replace the first channels of each value; channels
/// past the last output keep their values.
/// </remarks>
internal sealed class IccClutStage : IccStage
{
    private readonly float[] table;
    private readonly int inputs;
    private readonly int outputs;
    private readonly bool trilinear;

    // The number of grid intervals in each input dimension.
    private readonly int[] domain;

    // strides[d] is the distance in the table between neighbouring grid points along input d.
    // The last input varies fastest.
    private readonly int[] strides;

    /// <summary>
    /// Initializes a new instance of the <see cref="IccClutStage"/> class.
    /// </summary>
    /// <param name="clut">The table, with one to four inputs and outputs and at least two grid points per input.</param>
    /// <param name="trilinear">Whether to interpolate a three-input table trilinearly rather than tetrahedrally.</param>
    public IccClutStage(IccClut clut, bool trilinear)
    {
        DebugGuard.MustBeBetweenOrEqualTo(clut.InputChannelCount, 1, 4, nameof(clut));
        DebugGuard.MustBeBetweenOrEqualTo(clut.OutputChannelCount, 1, 4, nameof(clut));

        this.inputs = clut.InputChannelCount;
        this.outputs = clut.OutputChannelCount;
        this.trilinear = trilinear;

        this.table = new float[clut.Values.Length * this.outputs];
        for (int i = 0; i < clut.Values.Length; i++)
        {
            clut.Values[i].AsSpan().CopyTo(this.table.AsSpan(i * this.outputs));
        }

        this.domain = new int[this.inputs];
        this.strides = new int[this.inputs];
        int stride = this.outputs;
        for (int d = this.inputs - 1; d >= 0; d--)
        {
            this.domain[d] = clut.GridPointCount[d] - 1;
            this.strides[d] = stride;
            stride *= clut.GridPointCount[d];
        }
    }

    /// <inheritdoc/>
    public override void Invoke(Span<Vector4> values)
    {
        Span<float> input = stackalloc float[4];
        Span<float> output = stackalloc float[4];
        for (int i = 0; i < values.Length; i++)
        {
            ref Vector4 value = ref values[i];
            input[0] = value.X;
            input[1] = value.Y;
            input[2] = value.Z;
            input[3] = value.W;

            switch (this.inputs)
            {
                case 1:
                    this.Linear(input[0], output);
                    break;
                case 2:
                    this.Bilinear(input, output);
                    break;
                case 3:
                    if (this.trilinear)
                    {
                        this.Trilinear(input, output, 0, 0);
                    }
                    else
                    {
                        this.Tetrahedral(input, output, 0, 0);
                    }

                    break;
                default:
                    this.FourInputs(input, output);
                    break;
            }

            value.X = output[0];
            if (this.outputs > 1)
            {
                value.Y = output[1];
            }

            if (this.outputs > 2)
            {
                value.Z = output[2];
            }

            if (this.outputs > 3)
            {
                value.W = output[3];
            }
        }
    }

    // Clamps to [0, 1]; NaN and values below 1e-9 give 0, as fclamp does.
    private static float Clamp(float v) => v < 1e-9F || float.IsNaN(v) ? 0 : (v > 1 ? 1 : v);

    private static float Lerp(float fraction, float low, float high) => low + ((high - low) * fraction);

    private void Linear(float input, Span<float> output)
    {
        float v = Clamp(input);
        float[] t = this.table;
        if (v >= 1 || this.domain[0] == 0)
        {
            int start = this.domain[0] * this.strides[0];
            for (int o = 0; o < this.outputs; o++)
            {
                output[o] = t[start + o];
            }

            return;
        }

        v *= this.domain[0];
        int cell0 = (int)MathF.Floor(v);
        int cell1 = (int)MathF.Ceiling(v);
        float rest = v - cell0;
        cell0 *= this.strides[0];
        cell1 *= this.strides[0];
        for (int o = 0; o < this.outputs; o++)
        {
            output[o] = Lerp(rest, t[cell0 + o], t[cell1 + o]);
        }
    }

    private void Bilinear(ReadOnlySpan<float> input, Span<float> output)
    {
        float[] t = this.table;
        float cx = Clamp(input[0]);
        float cy = Clamp(input[1]);
        float px = cx * this.domain[0];
        float py = cy * this.domain[1];

        int x0 = (int)MathF.Floor(px);
        float fx = px - x0;
        int y0 = (int)MathF.Floor(py);
        float fy = py - y0;

        int xa = this.strides[0] * x0;
        int xb = xa + (cx >= 1 ? 0 : this.strides[0]);
        int ya = this.strides[1] * y0;
        int yb = ya + (cy >= 1 ? 0 : this.strides[1]);

        for (int o = 0; o < this.outputs; o++)
        {
            float d00 = t[xa + ya + o];
            float d01 = t[xa + yb + o];
            float d10 = t[xb + ya + o];
            float d11 = t[xb + yb + o];

            float dx0 = Lerp(fx, d00, d10);
            float dx1 = Lerp(fx, d01, d11);
            output[o] = Lerp(fy, dx0, dx1);
        }
    }

    // Interpolates the three inputs input[first..first + 2], which index dimensions first..first + 2,
    // in the part of the table that starts at offset.
    private void Trilinear(ReadOnlySpan<float> input, Span<float> output, int first, int offset)
    {
        float[] t = this.table;
        float cx = Clamp(input[first]);
        float cy = Clamp(input[first + 1]);
        float cz = Clamp(input[first + 2]);
        float px = cx * this.domain[first];
        float py = cy * this.domain[first + 1];
        float pz = cz * this.domain[first + 2];

        int x0 = (int)MathF.Floor(px);
        float fx = px - x0;
        int y0 = (int)MathF.Floor(py);
        float fy = py - y0;
        int z0 = (int)MathF.Floor(pz);
        float fz = pz - z0;

        int xa = offset + (this.strides[first] * x0);
        int xb = xa + (cx >= 1 ? 0 : this.strides[first]);
        int ya = this.strides[first + 1] * y0;
        int yb = ya + (cy >= 1 ? 0 : this.strides[first + 1]);
        int za = this.strides[first + 2] * z0;
        int zb = za + (cz >= 1 ? 0 : this.strides[first + 2]);

        for (int o = 0; o < this.outputs; o++)
        {
            float d000 = t[xa + ya + za + o];
            float d001 = t[xa + ya + zb + o];
            float d010 = t[xa + yb + za + o];
            float d011 = t[xa + yb + zb + o];
            float d100 = t[xb + ya + za + o];
            float d101 = t[xb + ya + zb + o];
            float d110 = t[xb + yb + za + o];
            float d111 = t[xb + yb + zb + o];

            float dx00 = Lerp(fx, d000, d100);
            float dx01 = Lerp(fx, d001, d101);
            float dx10 = Lerp(fx, d010, d110);
            float dx11 = Lerp(fx, d011, d111);

            float dxy0 = Lerp(fy, dx00, dx10);
            float dxy1 = Lerp(fy, dx01, dx11);

            output[o] = Lerp(fz, dxy0, dxy1);
        }
    }

    // Tetrahedral interpolation (Sakamoto) of input[first..first + 2] in the part of the table that
    // starts at offset.
    private void Tetrahedral(ReadOnlySpan<float> input, Span<float> output, int first, int offset)
    {
        float[] t = this.table;
        float cx = Clamp(input[first]);
        float cy = Clamp(input[first + 1]);
        float cz = Clamp(input[first + 2]);
        float px = cx * this.domain[first];
        float py = cy * this.domain[first + 1];
        float pz = cz * this.domain[first + 2];

        int x0 = (int)MathF.Floor(px);
        float rx = px - x0;
        int y0 = (int)MathF.Floor(py);
        float ry = py - y0;
        int z0 = (int)MathF.Floor(pz);
        float rz = pz - z0;

        int xa = offset + (this.strides[first] * x0);
        int xb = xa + (cx >= 1 ? 0 : this.strides[first]);
        int ya = this.strides[first + 1] * y0;
        int yb = ya + (cy >= 1 ? 0 : this.strides[first + 1]);
        int za = this.strides[first + 2] * z0;
        int zb = za + (cz >= 1 ? 0 : this.strides[first + 2]);

        for (int o = 0; o < this.outputs; o++)
        {
            float c0 = t[xa + ya + za + o];
            float c1;
            float c2;
            float c3;

            if (rx >= ry && ry >= rz)
            {
                c1 = t[xb + ya + za + o] - c0;
                c2 = t[xb + yb + za + o] - t[xb + ya + za + o];
                c3 = t[xb + yb + zb + o] - t[xb + yb + za + o];
            }
            else if (rx >= rz && rz >= ry)
            {
                c1 = t[xb + ya + za + o] - c0;
                c2 = t[xb + yb + zb + o] - t[xb + ya + zb + o];
                c3 = t[xb + ya + zb + o] - t[xb + ya + za + o];
            }
            else if (rz >= rx && rx >= ry)
            {
                c1 = t[xb + ya + zb + o] - t[xa + ya + zb + o];
                c2 = t[xb + yb + zb + o] - t[xb + ya + zb + o];
                c3 = t[xa + ya + zb + o] - c0;
            }
            else if (ry >= rx && rx >= rz)
            {
                c1 = t[xb + yb + za + o] - t[xa + yb + za + o];
                c2 = t[xa + yb + za + o] - c0;
                c3 = t[xb + yb + zb + o] - t[xb + yb + za + o];
            }
            else if (ry >= rz && rz >= rx)
            {
                c1 = t[xb + yb + zb + o] - t[xa + yb + zb + o];
                c2 = t[xa + yb + za + o] - c0;
                c3 = t[xa + yb + zb + o] - t[xa + yb + za + o];
            }
            else if (rz >= ry && ry >= rx)
            {
                c1 = t[xb + yb + zb + o] - t[xa + yb + zb + o];
                c2 = t[xa + yb + zb + o] - t[xa + ya + zb + o];
                c3 = t[xa + ya + zb + o] - c0;
            }
            else
            {
                c1 = c2 = c3 = 0;
            }

            output[o] = c0 + (c1 * rx) + (c2 * ry) + (c3 * rz);
        }
    }

    // Four inputs: tetrahedral interpolation of the last three at the two grid planes around the
    // first, then linear interpolation between the two results.
    private void FourInputs(ReadOnlySpan<float> input, Span<float> output)
    {
        float ck = Clamp(input[0]);
        float pk = ck * this.domain[0];
        int k0 = (int)MathF.Floor(pk);
        float rest = pk - k0;

        int low = this.strides[0] * k0;
        int high = low + (ck >= 1 ? 0 : this.strides[0]);

        Span<float> lowOutput = stackalloc float[4];
        Span<float> highOutput = stackalloc float[4];
        this.Tetrahedral(input, lowOutput, 1, low);
        this.Tetrahedral(input, highOutput, 1, high);

        for (int o = 0; o < this.outputs; o++)
        {
            output[o] = Lerp(rest, lowOutput[o], highOutput[o]);
        }
    }
}
