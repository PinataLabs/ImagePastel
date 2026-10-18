// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace SixLabors.ImageSharp.Tests.Processing.Processors.ColorManagement;

/// <summary>
/// Colour conversions computed by Little CMS 2.20 (<c>mm2/Little-CMS</c> at <c>a57322d8</c>), the
/// independent reference for the ICC colour engine. Each table was printed by
/// <code>transicc -n -s -c0 -t1 -i&lt;source&gt; -o&lt;target&gt;</code>
/// (float pipeline, relative colorimetric, output clipped to the gamut) with inputs and outputs in
/// 8-bit code values (0 to 255) and four decimals. A <see langword="null"/> profile stands for the
/// <c>transicc</c> built-in <c>*sRGB</c>. For these matrix/TRC profiles <c>-t0</c> (perceptual)
/// gives the same numbers.
/// </summary>
internal static class LittleCmsReferences
{
    private static readonly Dictionary<(string Source, string Target), LittleCmsReference> References = new()
    {
        [(TestImages.Icc.CompactAdobeRgbV2, null)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 255.0000F, 254.9997F, 255.0000F],
            [255, 0, 0, 255.0000F, 0.0000F, 0.0233F],
            [0, 255, 0, 0.0000F, 254.9987F, 0.0000F],
            [0, 0, 255, 0.0000F, 0.0361F, 255.0000F],
            [128, 64, 32, 146.1484F, 61.7724F, 23.4316F],
            [51, 102, 153, 0.0000F, 102.1591F, 156.2031F],
            [200, 180, 20, 208.5308F, 181.4887F, 0.0000F],
            [10, 20, 30, 0.0000F, 12.0828F, 24.2271F],
            [240, 15, 200, 255.0000F, 6.4974F, 205.1010F],
            [128, 128, 128, 129.0307F, 129.0302F, 129.0308F],
            [1, 2, 3, 0.0000F, 0.0771F, 0.1930F],
        ]),
        [(TestImages.Icc.CompactProPhotoV4, null)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 255.0000F, 254.9997F, 255.0000F],
            [255, 0, 0, 255.0000F, 0.0000F, 0.0000F],
            [0, 255, 0, 0.0000F, 255.0000F, 0.0000F],
            [0, 0, 255, 0.0000F, 0.0000F, 255.0000F],
            [128, 64, 32, 190.9341F, 53.3552F, 29.3194F],
            [51, 102, 153, 0.0000F, 129.9165F, 175.8482F],
            [200, 180, 20, 246.0348F, 189.2237F, 0.0000F],
            [10, 20, 30, 0.0000F, 28.3888F, 41.9231F],
            [240, 15, 200, 255.0000F, 0.0000F, 223.5030F],
            [128, 128, 128, 146.4088F, 146.4082F, 146.4089F],
            [1, 2, 3, 0.0000F, 1.7974F, 2.5601F],
        ]),
        [(TestImages.Icc.CompactDisplayP3V4, null)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 255.0000F, 254.9997F, 255.0000F],
            [255, 0, 0, 255.0000F, 0.0000F, 0.0000F],
            [0, 255, 0, 0.0000F, 255.0000F, 0.0000F],
            [0, 0, 255, 0.0504F, 0.0000F, 255.0000F],
            [128, 64, 32, 137.6826F, 59.4254F, 21.1811F],
            [51, 102, 153, 26.5406F, 103.5107F, 157.3354F],
            [200, 180, 20, 204.1507F, 179.0952F, 0.0000F],
            [10, 20, 30, 7.0644F, 20.3354F, 30.9279F],
            [240, 15, 200, 255.0000F, 0.0000F, 205.9470F],
            [128, 128, 128, 127.9998F, 127.9992F, 127.9999F],
            [1, 2, 3, 0.7750F, 2.0418F, 3.1177F],
        ]),
        [(TestImages.Icc.QcmsRec2020, null)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 254.9990F, 254.9980F, 254.9987F],
            [255, 0, 0, 255.0000F, 0.0000F, 0.0000F],
            [0, 255, 0, 0.0000F, 255.0000F, 0.0000F],
            [0, 0, 255, 0.0000F, 0.0000F, 255.0000F],
            [128, 64, 32, 166.8849F, 67.0918F, 38.8390F],
            [51, 102, 153, 0.0000F, 119.4615F, 167.8633F],
            [200, 180, 20, 221.4598F, 185.8652F, 0.0000F],
            [10, 20, 30, 7.4289F, 36.9241F, 46.9722F],
            [240, 15, 200, 255.0000F, 0.0000F, 213.8057F],
            [128, 128, 128, 139.8116F, 139.8110F, 139.8114F],
            [1, 2, 3, 0.7656F, 6.0759F, 9.0065F],
        ]),
        [(TestImages.Icc.CompactLinearSrgbV2, null)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 255.0000F, 254.9997F, 255.0000F],
            [255, 0, 0, 255.0000F, 0.0000F, 0.0000F],
            [0, 255, 0, 0.0000F, 255.0000F, 0.0178F],
            [0, 0, 255, 0.1190F, 0.0161F, 255.0000F],
            [128, 64, 32, 187.8469F, 137.2016F, 99.2687F],
            [51, 102, 153, 123.5544F, 169.6251F, 203.4248F],
            [200, 180, 20, 229.0985F, 218.6564F, 79.1165F],
            [10, 20, 30, 55.7553F, 79.1223F, 96.2655F],
            [240, 15, 200, 248.2985F, 68.5621F, 229.1002F],
            [128, 128, 128, 187.8455F, 187.8448F, 187.8456F],
            [1, 2, 3, 12.7091F, 21.6615F, 28.2293F],
        ]),
        [(null, TestImages.Icc.CompactDisplayP3V4)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 254.9995F, 255.0000F, 254.9992F],
            [255, 0, 0, 233.9594F, 51.0671F, 35.3360F],
            [0, 255, 0, 116.8945F, 251.2421F, 76.0619F],
            [0, 0, 255, 0.0000F, 0.0454F, 244.6947F],
            [128, 64, 32, 119.6487F, 67.3618F, 39.2844F],
            [51, 102, 153, 63.7110F, 100.7881F, 148.9289F],
            [200, 180, 20, 196.6425F, 180.7101F, 62.7224F],
            [10, 20, 30, 12.1806F, 19.7319F, 29.1352F],
            [240, 15, 200, 220.2474F, 51.3583F, 194.2655F],
            [128, 128, 128, 128.0003F, 128.0008F, 128.0002F],
            [1, 2, 3, 1.1776F, 1.9670F, 2.8937F],
        ]),
        [(TestImages.Icc.LittleCmsSrgbV4, TestImages.Icc.CompactDisplayP3V4)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 255.0000F, 255.0000F, 254.9978F],
            [255, 0, 0, 233.9573F, 51.0769F, 35.3288F],
            [0, 255, 0, 116.8984F, 251.2414F, 76.0580F],
            [0, 0, 255, 0.0205F, 0.0160F, 244.6949F],
            [128, 64, 32, 119.6490F, 67.3646F, 39.2842F],
            [51, 102, 153, 63.7162F, 100.7882F, 148.9296F],
            [200, 180, 20, 196.6424F, 180.7115F, 62.7187F],
            [10, 20, 30, 12.1811F, 19.7334F, 29.1369F],
            [240, 15, 200, 220.2467F, 51.3632F, 194.2652F],
            [128, 128, 128, 128.0021F, 128.0017F, 128.0006F],
            [1, 2, 3, 1.1776F, 1.9668F, 2.8934F],
        ]),

        // The target's tone curves are 4096-entry tables, so this inverts tables numerically.
        [(TestImages.Icc.CompactDisplayP3V4, TestImages.Icc.LittleCmsSrgbV2)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 255.0000F, 255.0000F, 255.0000F],
            [255, 0, 0, 255.0000F, 0.0000F, 0.0000F],
            [0, 255, 0, 0.0000F, 255.0000F, 0.0000F],
            [0, 0, 255, 0.0000F, 0.0000F, 255.0000F],
            [128, 64, 32, 137.6848F, 59.4202F, 21.1984F],
            [51, 102, 153, 26.5370F, 103.5097F, 157.3346F],
            [200, 180, 20, 204.1518F, 179.0973F, 0.0000F],
            [10, 20, 30, 7.0233F, 20.3230F, 30.9300F],
            [240, 15, 200, 255.0000F, 0.0000F, 205.9455F],
            [128, 128, 128, 128.0000F, 128.0000F, 128.0039F],
            [1, 2, 3, 0.7588F, 2.0739F, 3.1362F],
        ]),
        [(TestImages.Icc.CompactGrayV4, null)] = GrayToRgb(
        [
            [0, 0.0000F, 0.0000F, 0.0000F],
            [1, 0.9999F, 0.9999F, 0.9999F],
            [5, 4.9996F, 4.9996F, 4.9996F],
            [32, 32.0002F, 32.0002F, 32.0002F],
            [64, 63.9997F, 63.9997F, 63.9997F],
            [128, 127.9994F, 127.9994F, 127.9994F],
            [200, 199.9996F, 199.9996F, 199.9996F],
            [255, 255.0000F, 255.0000F, 255.0000F],
        ]),
        [(TestImages.Icc.LittleCmsGray22V4, null)] = GrayToRgb(
        [
            [0, 0.0000F, 0.0000F, 0.0000F],
            [1, 0.0167F, 0.0167F, 0.0167F],
            [5, 0.5770F, 0.5770F, 0.5770F],
            [32, 26.1097F, 26.1096F, 26.1096F],
            [64, 61.7391F, 61.7391F, 61.7391F],
            [128, 128.9984F, 128.9984F, 128.9984F],
            [200, 201.2904F, 201.2904F, 201.2904F],
            [255, 255.0000F, 255.0000F, 255.0000F],
        ]),

        // A gray profile with a Lab connection space: the gray curve gives L*.
        [(TestImages.Icc.LittleCmsGrayLab18V4, null)] = GrayToRgb(
        [
            [0, 0.0000F, 0.0000F, 0.0000F],
            [1, 0.0170F, 0.0170F, 0.0170F],
            [5, 0.3079F, 0.3078F, 0.3078F],
            [32, 8.6990F, 8.6990F, 8.6989F],
            [64, 24.1118F, 24.1118F, 24.1118F],
            [128, 68.1567F, 68.1567F, 68.1567F],
            [200, 156.5784F, 156.5783F, 156.5783F],
            [255, 255.0000F, 255.0000F, 255.0000F],
        ]),
        [(null, TestImages.Icc.LittleCmsGrayLab18V4)] = RgbToGray(
        [
            [0, 0, 0, 0.0000F],
            [255, 255, 255, 255.0000F],
            [255, 0, 0, 181.6189F],
            [0, 255, 0, 237.2475F],
            [0, 0, 255, 129.5794F],
            [128, 64, 32, 142.6160F],
            [51, 102, 153, 156.4823F],
            [200, 180, 20, 214.5741F],
            [10, 20, 30, 52.6822F],
            [240, 15, 200, 183.5041F],
            [128, 128, 128, 180.3055F],
            [1, 2, 3, 13.4927F],
        ]),
        [(null, TestImages.Icc.LittleCmsGray22V4)] = RgbToGray(
        [
            [0, 0, 0, 0.0000F],
            [255, 255, 255, 255.0000F],
            [255, 0, 0, 128.7828F],
            [0, 255, 0, 219.2001F],
            [0, 0, 255, 71.3102F],
            [128, 64, 32, 83.4508F],
            [51, 102, 153, 97.9783F],
            [200, 180, 20, 178.4762F],
            [10, 20, 30, 25.8057F],
            [240, 15, 200, 131.3344F],
            [128, 128, 128, 127.0256F],
            [1, 2, 3, 8.4666F],
        ]),
    };

    /// <summary>
    /// Gets the reference conversion between two test profiles.
    /// </summary>
    /// <param name="source">The source profile file, or <see langword="null"/> for sRGB.</param>
    /// <param name="target">The target profile file, or <see langword="null"/> for sRGB.</param>
    /// <returns>The reference.</returns>
    public static LittleCmsReference Get(string source, string target)
        => References.TryGetValue((source, target), out LittleCmsReference reference)
            ? reference
            : throw new ArgumentException($"No Little CMS reference for {source ?? "sRGB"} -> {target ?? "sRGB"}.");

    private static LittleCmsReference Rgb(float[][] rows) => new(3, 3, rows);

    private static LittleCmsReference GrayToRgb(float[][] rows) => new(1, 3, rows);

    private static LittleCmsReference RgbToGray(float[][] rows) => new(3, 1, rows);
}

/// <summary>
/// One Little CMS reference table: each row holds the input code values, then the output code values.
/// </summary>
internal sealed class LittleCmsReference
{
    private readonly float[][] rows;

    public LittleCmsReference(int inputChannels, int outputChannels, float[][] rows)
    {
        this.InputChannels = inputChannels;
        this.OutputChannels = outputChannels;
        this.rows = rows;
    }

    public int InputChannels { get; }

    public int OutputChannels { get; }

    public int Count => this.rows.Length;

    /// <summary>
    /// Gets the input of a row as an 8-bit RGB triple (a gray input is repeated).
    /// </summary>
    public (float R, float G, float B) Input(int row)
    {
        float[] values = this.rows[row];
        return this.InputChannels == 1 ? (values[0], values[0], values[0]) : (values[0], values[1], values[2]);
    }

    /// <summary>
    /// Gets the output of a row as an 8-bit RGB triple (a gray output is repeated).
    /// </summary>
    public (float R, float G, float B) Output(int row)
    {
        float[] values = this.rows[row];
        int i = this.InputChannels;
        return this.OutputChannels == 1 ? (values[i], values[i], values[i]) : (values[i], values[i + 1], values[i + 2]);
    }
}
