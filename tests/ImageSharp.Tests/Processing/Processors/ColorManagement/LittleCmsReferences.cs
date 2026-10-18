// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace SixLabors.ImageSharp.Tests.Processing.Processors.ColorManagement;

/// <summary>
/// Colour conversions computed by Little CMS 2.20 (<c>mm2/Little-CMS</c> at <c>a57322d8</c>), the
/// independent reference for the ICC colour engine. Each table was printed by
/// <code>transicc -n -s -c0 -t1 -i&lt;source&gt; -o&lt;target&gt;</code>
/// (float pipeline, relative colorimetric, output clipped to the gamut) with inputs and outputs in
/// 8-bit code values (0 to 255) and four decimals. A <see langword="null"/> profile stands for the
/// <c>transicc</c> built-in <c>*sRGB</c>. For these matrix/TRC profiles <c>-t0</c> (perceptual)
/// gives the same numbers. CMYK values are in percent of ink (0 to 100), as <c>transicc</c> takes
/// and prints them. The comments on the LUT-based entries say where the command differs.
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

        // LUT-based profiles (#175). The test profiles have only intent-0 tables (or intent-1 tables
        // that share their data), so with -t1 Little CMS uses the same tables as the engine.
        // lut8 tables, Lab PCS.
        [(TestImages.Icc.LittleCmsRgbLut8V2, null)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 255.0000F, 255.0000F, 255.0000F],
            [255, 0, 0, 255.0000F, 7.4164F, 0.8703F],
            [0, 255, 0, 0.0000F, 254.5143F, 0.0000F],
            [0, 0, 255, 0.0000F, 1.7705F, 255.0000F],
            [128, 64, 32, 147.3387F, 59.8096F, 11.3177F],
            [51, 102, 153, 0.0000F, 100.5178F, 156.1584F],
            [200, 180, 20, 208.5167F, 180.9307F, 0.0000F],
            [10, 20, 30, 3.9466F, 9.2468F, 16.8709F],
            [240, 15, 200, 255.0000F, 15.6385F, 205.5790F],
            [128, 128, 128, 128.5844F, 128.5844F, 128.5844F],
            [1, 2, 3, 0.0000F, 0.0000F, 0.0000F],
        ]),
        [(null, TestImages.Icc.LittleCmsRgbLut8V2)] = Rgb(
        [
            [0, 0, 0, 1.6342F, 0.0000F, 0.0000F],
            [255, 255, 255, 254.0623F, 255.0000F, 254.2140F],
            [255, 0, 0, 219.5331F, 49.2685F, 28.3969F],
            [0, 255, 0, 144.2918F, 251.5019F, 68.8560F],
            [0, 0, 255, 54.9494F, 31.1751F, 239.4319F],
            [128, 64, 32, 115.1245F, 67.4008F, 39.6537F],
            [51, 102, 153, 73.8755F, 102.7821F, 151.1556F],
            [200, 180, 20, 194.0000F, 178.9611F, 58.5681F],
            [10, 20, 30, 23.6148F, 27.5370F, 41.4591F],
            [240, 15, 200, 207.0000F, 49.4241F, 197.0000F],
            [128, 128, 128, 127.9222F, 127.8794F, 128.5914F],
            [1, 2, 3, 4.2490F, 3.3502F, 3.3502F],
        ]),

        // lut16 tables, XYZ PCS; the B2A0 table has a matrix.
        [(TestImages.Icc.LittleCmsRgbLut16V2, null)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 255.0000F, 254.9997F, 254.9985F],
            [255, 0, 0, 255.0000F, 0.0000F, 0.0349F],
            [0, 255, 0, 0.0000F, 255.0000F, 0.0000F],
            [0, 0, 255, 0.0000F, 0.0000F, 255.0000F],
            [128, 64, 32, 146.0533F, 62.1976F, 24.7985F],
            [51, 102, 153, 0.0000F, 102.2760F, 156.2286F],
            [200, 180, 20, 208.5170F, 181.5126F, 0.0000F],
            [10, 20, 30, 0.0000F, 13.7383F, 26.1275F],
            [240, 15, 200, 255.0000F, 6.4567F, 205.1165F],
            [128, 128, 128, 129.0882F, 129.0645F, 129.0839F],
            [1, 2, 3, 0.0000F, 0.1960F, 0.1183F],
        ]),
        [(null, TestImages.Icc.LittleCmsRgbLut16V2)] = Rgb(
        [
            [0, 0, 0, 0.0000F, 0.0000F, 0.0000F],
            [255, 255, 255, 251.9261F, 250.8949F, 246.2879F],
            [255, 0, 0, 219.1206F, 59.4163F, 53.9494F],
            [0, 255, 0, 144.3852F, 245.0817F, 68.5175F],
            [0, 0, 255, 57.1323F, 61.5292F, 246.9261F],
            [128, 64, 32, 115.2101F, 75.5486F, 58.1634F],
            [51, 102, 153, 79.9338F, 103.6420F, 150.4786F],
            [200, 180, 20, 193.7510F, 178.9767F, 49.2568F],
            [10, 20, 30, 35.1362F, 37.6576F, 45.8872F],
            [240, 15, 200, 206.2101F, 59.6420F, 195.4202F],
            [128, 128, 128, 126.9883F, 127.2918F, 127.7821F],
            [1, 2, 3, 12.2802F, 13.4280F, 15.6342F],
        ]),

        // lutAToBType and lutBToAType tables with every element, XYZ PCS, CLUT grids that differ per dimension.
        [(TestImages.Icc.LittleCmsRgbLutAToBV4, null)] = Rgb(
        [
            [0, 0, 0, 0.1581F, 0.0000F, 0.0036F],
            [255, 255, 255, 255.0000F, 251.8374F, 255.0000F],
            [255, 0, 0, 255.0000F, 0.0000F, 6.3701F],
            [0, 255, 0, 0.0000F, 251.6718F, 0.0000F],
            [0, 0, 255, 0.3427F, 0.0000F, 255.0000F],
            [128, 64, 32, 147.7662F, 63.7398F, 19.5603F],
            [51, 102, 153, 22.2987F, 102.2005F, 156.1702F],
            [200, 180, 20, 209.1671F, 181.4009F, 0.0000F],
            [10, 20, 30, 1.8965F, 18.8680F, 21.5589F],
            [240, 15, 200, 255.0000F, 14.6811F, 205.0687F],
            [128, 128, 128, 131.2996F, 128.8130F, 129.0397F],
            [1, 2, 3, 0.0000F, 1.6500F, 0.0630F],
        ]),
        [(null, TestImages.Icc.LittleCmsRgbLutAToBV4)] = Rgb(
        [
            [0, 0, 0, 17.5607F, 4.9767F, 4.8288F],
            [255, 255, 255, 253.8508F, 250.1089F, 254.8327F],
            [255, 0, 0, 218.9448F, 39.4942F, 8.5447F],
            [0, 255, 0, 144.2193F, 249.8483F, 58.2607F],
            [0, 0, 255, 14.9644F, 26.5214F, 250.1945F],
            [128, 64, 32, 113.7442F, 63.8132F, 34.5992F],
            [51, 102, 153, 73.3160F, 100.2685F, 150.0350F],
            [200, 180, 20, 193.2402F, 178.3696F, 46.0350F],
            [10, 20, 30, 27.4945F, 22.6654F, 31.1051F],
            [240, 15, 200, 205.8539F, 43.0389F, 195.0117F],
            [128, 128, 128, 127.0120F, 126.5058F, 126.9339F],
            [1, 2, 3, 18.7815F, 8.8444F, 7.5914F],
        ]),

        // A gray profile with lutAToBType and lutBToAType tables, Lab PCS.
        [(TestImages.Icc.LittleCmsGrayLutV4, null)] = GrayToRgb(
        [
            [0, 0.0000F, 0.0000F, 0.0000F],
            [1, 0.2004F, 0.2003F, 0.2003F],
            [5, 2.2318F, 2.2317F, 2.2317F],
            [32, 27.2651F, 27.2651F, 27.2651F],
            [64, 61.7457F, 61.7457F, 61.7457F],
            [128, 128.8806F, 128.8806F, 128.8806F],
            [200, 201.2817F, 201.2817F, 201.2816F],
            [255, 255.0000F, 255.0000F, 255.0000F],
        ]),
        [(null, TestImages.Icc.LittleCmsGrayLutV4)] = RgbToGray(
        [
            [0, 0, 0, 0.0000F],
            [255, 255, 255, 255.0000F],
            [255, 0, 0, 128.8289F],
            [0, 255, 0, 219.2344F],
            [0, 0, 255, 71.3942F],
            [128, 64, 32, 83.7384F],
            [51, 102, 153, 97.9923F],
            [200, 180, 20, 178.6351F],
            [10, 20, 30, 16.5609F],
            [240, 15, 200, 131.4441F],
            [128, 128, 128, 127.0254F],
            [1, 2, 3, 1.4636F],
        ]),

        // The sRGB v4 ICC preference profile embedded in the WebP file (lutAToBType, Lab PCS), with
        // -t0: its perceptual table. The version 2 target keeps Little CMS from forcing black-point
        // compensation, which it does for version 4 targets with the perceptual intent.
        [(TestImages.Webp.Lossy.WithIccp, TestImages.Icc.LittleCmsSrgbV2)] = Rgb(
        [
            [0, 0, 0, 11.3035F, 11.3035F, 11.3035F],
            [255, 255, 255, 255.0000F, 255.0000F, 255.0000F],
            [255, 0, 0, 241.7665F, 0.0000F, 0.0000F],
            [0, 255, 0, 0.0000F, 198.1128F, 0.0000F],
            [0, 0, 255, 0.0000F, 19.6148F, 195.8171F],
            [128, 64, 32, 126.6770F, 64.0156F, 32.7237F],
            [51, 102, 153, 22.5370F, 93.5175F, 150.9105F],
            [200, 180, 20, 207.0078F, 167.5875F, 0.0000F],
            [10, 20, 30, 18.1206F, 25.4475F, 35.5953F],
            [240, 15, 200, 225.2918F, 0.0000F, 154.2957F],
            [128, 128, 128, 128.1790F, 128.1790F, 128.1829F],
            [1, 2, 3, 12.0039F, 13.0117F, 14.5175F],
        ]),

        // CMYK inputs in percent of ink.
        [(TestImages.Icc.LittleCmsFakeCmykV4, null)] = CmykToRgb(
        [
            [0, 0, 0, 0, 255.0000F, 255.0000F, 255.0000F],
            [100, 0, 0, 0, 0.0535F, 254.9983F, 254.9992F],
            [0, 100, 0, 0, 254.9994F, 0.0326F, 255.0000F],
            [0, 0, 100, 0, 255.0000F, 254.9995F, 0.0000F],
            [0, 0, 0, 100, 0.0000F, 0.0000F, 0.0000F],
            [20, 40, 60, 80, 40.7375F, 30.6451F, 20.6803F],
            [50, 50, 50, 0, 127.4982F, 127.4982F, 127.4982F],
            [10, 20, 30, 40, 137.7355F, 122.4894F, 107.1450F],
            [100, 100, 100, 100, 0.0000F, 0.0000F, 0.0000F],
            [75, 25, 60, 10, 57.4963F, 172.1019F, 92.0605F],
            [33, 66, 99, 5, 162.3515F, 82.6582F, 2.1151F],
            [3, 7, 12, 1, 244.9579F, 234.8763F, 222.1667F],
        ]),
        [(TestImages.Icc.TiffCmykTestProfile, null)] = CmykToRgb(
        [
            [0, 0, 0, 0, 254.9920F, 254.9986F, 255.0000F],
            [100, 0, 0, 0, 0.0000F, 254.9970F, 255.0000F],
            [0, 100, 0, 0, 255.0000F, 0.0000F, 255.0000F],
            [0, 0, 100, 0, 254.9928F, 255.0000F, 0.2402F],
            [0, 0, 0, 100, 0.0000F, 0.0000F, 0.0000F],
            [20, 40, 60, 80, 16.9033F, 11.3014F, 6.0978F],
            [50, 50, 50, 0, 84.1106F, 84.1176F, 84.1358F],
            [10, 20, 30, 40, 98.7123F, 83.5208F, 66.8018F],
            [100, 100, 100, 100, 0.0000F, 0.0000F, 0.0000F],
            [75, 25, 60, 10, 25.2178F, 136.5863F, 58.3005F],
            [33, 66, 99, 5, 126.2129F, 50.7620F, 3.5371F],
            [3, 7, 12, 1, 244.0002F, 226.5234F, 207.0688F],
        ]),

        // CMYK outputs in percent of ink, printed without -s, which would clip them to 1.
        [(null, TestImages.Icc.LittleCmsFakeCmykV4)] = RgbToCmyk(
        [
            [0, 0, 0, 66.8849F, 67.1305F, 66.9139F, 99.0692F],
            [255, 255, 255, 0.4974F, 0.1328F, 0.3708F, 0.0000F],
            [255, 0, 0, 1.8036F, 92.5292F, 94.7829F, 1.8036F],
            [0, 255, 0, 95.8511F, 0.3601F, 92.8405F, 0.3601F],
            [0, 0, 255, 91.6686F, 94.4121F, 1.4572F, 1.4572F],
            [128, 64, 32, 49.8543F, 75.0942F, 87.4800F, 49.8543F],
            [51, 102, 153, 81.7792F, 60.0397F, 39.9771F, 39.9771F],
            [200, 180, 20, 21.6800F, 29.4347F, 90.4524F, 21.0147F],
            [10, 20, 30, 73.6568F, 70.6065F, 67.4907F, 88.2445F],
            [240, 15, 200, 6.3203F, 89.5476F, 21.5396F, 6.3203F],
            [128, 128, 128, 49.8283F, 49.7993F, 49.7902F, 49.0791F],
            [1, 2, 3, 67.1504F, 67.1839F, 66.9657F, 98.6999F],
        ]),
    };

    // sRGB -> CMYK profile -> sRGB, each step a transicc table: first
    // transicc -n -c0 -t1 -i*sRGB -o<profile> (CMYK in percent, not clipped), then
    // transicc -n -s -c0 -t1 -i<profile> -o*sRGB on those CMYK values. The 8-bit tables round the
    // CMYK to 8-bit code values (percent * 255 / 100) between the two steps, as Rgba32 stores them.
    // Rows are the sRGB input and the sRGB result, in 8-bit code values.
    private static readonly Dictionary<(string Profile, bool EightBit), LittleCmsReference> RoundTrips = new()
    {
        [(TestImages.Icc.LittleCmsFakeCmykV4, false)] = Rgb(
        [
            [0, 0, 0, 0.7885F, 0.8906F, 0.8953F],
            [255, 255, 255, 253.7706F, 254.6651F, 254.0551F],
            [255, 0, 0, 245.8520F, 19.5557F, 13.3333F],
            [0, 255, 0, 11.2846F, 253.2003F, 18.8431F],
            [0, 0, 255, 22.0624F, 14.2181F, 247.7351F],
            [128, 64, 32, 64.1165F, 31.8595F, 16.0217F],
            [51, 102, 153, 28.1711F, 61.2643F, 92.0315F],
            [200, 180, 20, 157.8191F, 142.2258F, 19.8545F],
            [10, 20, 30, 7.8924F, 8.8952F, 9.7947F],
            [240, 15, 200, 223.8282F, 25.7180F, 187.4042F],
            [128, 128, 128, 65.1482F, 65.1977F, 65.2106F],
            [1, 2, 3, 1.0849F, 1.2428F, 1.2434F],
        ]),

        // CMYK in 8-bit code values: 171 171 171 253, 1 0 1 0, 5 236 242 5, 244 1 237 1, 234 241 4 4,
        // 127 191 223 127, 209 153 102 102, 55 75 231 54, 188 180 172 225, 16 228 55 16,
        // 127 127 127 125, 171 171 171 252.
        [(TestImages.Icc.LittleCmsFakeCmykV4, true)] = Rgb(
        [
            [0, 0, 0, 0.6527F, 0.7513F, 0.7466F],
            [255, 255, 255, 254.0303F, 255.0000F, 253.9987F],
            [255, 0, 0, 245.0613F, 19.4675F, 13.0224F],
            [0, 255, 0, 11.6222F, 253.0446F, 18.5326F],
            [0, 0, 255, 21.7797F, 13.9764F, 247.1795F],
            [128, 64, 32, 64.2435F, 32.1482F, 16.0867F],
            [51, 102, 153, 27.8951F, 61.3067F, 91.9660F],
            [200, 180, 20, 157.7121F, 141.9851F, 19.5296F],
            [10, 20, 30, 7.8847F, 8.9041F, 9.8096F],
            [240, 15, 200, 224.0522F, 26.0213F, 187.4320F],
            [128, 128, 128, 65.2596F, 65.2655F, 65.2576F],
            [1, 2, 3, 0.9935F, 1.1253F, 1.1325F],
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

    /// <summary>
    /// Gets the reference round trip from sRGB to a CMYK test profile and back.
    /// </summary>
    /// <param name="profile">The CMYK profile file.</param>
    /// <param name="eightBit">Whether the CMYK values are rounded to 8 bits between the two conversions.</param>
    /// <returns>The reference, sRGB in and sRGB out.</returns>
    public static LittleCmsReference GetRoundTrip(string profile, bool eightBit)
        => RoundTrips.TryGetValue((profile, eightBit), out LittleCmsReference reference)
            ? reference
            : throw new ArgumentException($"No Little CMS round trip for {profile}.");

    private static LittleCmsReference Rgb(float[][] rows) => new(3, 3, rows);

    private static LittleCmsReference GrayToRgb(float[][] rows) => new(1, 3, rows);

    private static LittleCmsReference RgbToGray(float[][] rows) => new(3, 1, rows);

    private static LittleCmsReference CmykToRgb(float[][] rows) => new(4, 3, rows);

    private static LittleCmsReference RgbToCmyk(float[][] rows) => new(3, 4, rows);
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

    /// <summary>
    /// Gets the input of a row as the engine takes it: each channel scaled to [0, 1] from 8-bit code
    /// values, or from percent for CMYK, in X, Y, Z and W. A gray input is repeated in X, Y and Z,
    /// and W is <paramref name="alpha"/> when the input has fewer than four channels.
    /// </summary>
    public Vector4 ScaledInput(int row, float alpha)
        => this.Scale(this.rows[row].AsSpan(0, this.InputChannels), alpha);

    /// <summary>
    /// Gets the output of a row scaled to [0, 1], as <see cref="ScaledInput(int, float)"/> does.
    /// </summary>
    public Vector4 ScaledOutput(int row, float alpha)
        => this.Scale(this.rows[row].AsSpan(this.InputChannels), alpha);

    private Vector4 Scale(ReadOnlySpan<float> values, float alpha)
    {
        if (values.Length == 4)
        {
            return new Vector4(values[0], values[1], values[2], values[3]) / 100;
        }

        return values.Length == 1
            ? new Vector4(values[0] / 255, values[0] / 255, values[0] / 255, alpha)
            : new Vector4(values[0] / 255, values[1] / 255, values[2] / 255, alpha);
    }
}
