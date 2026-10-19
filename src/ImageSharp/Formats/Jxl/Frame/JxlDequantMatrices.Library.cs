// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/quant_weights.cc (DequantMatricesLibraryDef and DequantMatrices::LibraryInit).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <content>
/// The library quantization tables: the encoding a table has when the frame codes it as library table 0,
/// which is the default.
/// </content>
/// <remarks>
/// libjxl writes each value as <c>V(x)</c>, a double converted to float; a product such as
/// <c>0.9 * 26629.07</c> is computed in double first, as it is here.
/// </remarks>
internal sealed partial class JxlDequantMatrices
{
    // DequantMatrices::Library()[table]: the encoding of the quant table of that index.
    private static JxlQuantEncoding LibraryEncoding(int table) => table switch
    {
        // DCT8
        0 => Dct(
            6,
            new[] { 3150.0f, 0.0f, -0.4f, -0.4f, -0.4f, -2.0f },
            new[] { 560.0f, 0.0f, -0.3f, -0.3f, -0.3f, -0.3f },
            new[] { 512.0f, -2.0f, -1.0f, 0.0f, -1.0f, -2.0f }),

        // Identity
        1 => Identity(new[] { 280.0f, 3160.0f, 3160.0f }, new[] { 60.0f, 864.0f, 864.0f }, new[] { 18.0f, 200.0f, 200.0f }),

        // DCT2
        2 => Dct2(
            new[] { 3840.0f, 2560.0f, 1280.0f, 640.0f, 480.0f, 300.0f },
            new[] { 960.0f, 640.0f, 320.0f, 180.0f, 140.0f, 120.0f },
            new[] { 640.0f, 320.0f, 128.0f, 64.0f, 32.0f, 16.0f }),

        // DCT4 (quant_kind 3)
        3 => Dct4X4(),

        // DCT16
        4 => Dct(
            7,
            new[] { 8996.8725711814115328f, -1.3000777393353804f, -0.49424529824571225f, -0.439093774457103443f, -0.6350101832695744f, -0.90177264050827612f, -1.6162099239887414f },
            new[] { 3191.48366296844234752f, -0.67424582104194355f, -0.80745813428471001f, -0.44925837484843441f, -0.35865440981033403f, -0.31322389111877305f, -0.37615025315725483f },
            new[] { 1157.50408145487200256f, -2.0531423165804414f, -1.4f, -0.50687130033378396f, -0.42708730624733904f, -1.4856834539296244f, -4.9209142884401604f }),

        // DCT32
        5 => Dct(
            8,
            new[] { 15718.40830982518931456f, -1.025f, -0.98f, -0.9012f, -0.4f, -0.48819395464f, -0.421064f, -0.27f },
            new[] { 7305.7636810695983104f, -0.8041958212306401f, -0.7633036457487539f, -0.55660379990111464f, -0.49785304658857626f, -0.43699592683512467f, -0.40180866526242109f, -0.27321683125358037f },
            new[] { 3803.53173721215041536f, -3.060733579805728f, -2.0413270132490346f, -2.0235650159727417f, -0.5495389509954993f, -0.4f, -0.4f, -0.3f }),

        // DCT16X8
        6 => Dct(
            7,
            new[] { 7240.7734393502f, -0.7f, -0.7f, -0.2f, -0.2f, -0.2f, -0.5f },
            new[] { 1448.15468787004f, -0.5f, -0.5f, -0.5f, -0.2f, -0.2f, -0.2f },
            new[] { 506.854140754517f, -1.4f, -0.2f, -0.5f, -0.5f, -1.5f, -3.6f }),

        // DCT32X8
        7 => Dct(
            8,
            new[] { 16283.2494710648897f, -1.7812845336559429f, -1.6309059012653515f, -1.0382179034313539f, -0.85f, -0.7f, -0.9f, -1.2360638576849587f },
            new[] { 5089.15750884921511936f, -0.320049391452786891f, -0.35362849922161446f, -0.30340000000000003f, -0.61f, -0.5f, -0.5f, -0.6f },
            new[] { 3397.77603275308720128f, -0.321327362693153371f, -0.34507619223117997f, -0.70340000000000003f, -0.9f, -1.0f, -1.0f, -1.1754605576265209f }),

        // DCT32X16
        8 => Dct(
            8,
            new[] { 13844.97076442300573f, -0.97113799999999995f, -0.658f, -0.42026f, -0.22712f, -0.2206f, -0.226f, -0.6f },
            new[] { 4798.964084220744293f, -0.61125308982767057f, -0.83770786552491361f, -0.79014862079498627f, -0.2692727459704829f, -0.38272769465388551f, -0.22924222653091453f, -0.20719098826199578f },
            new[] { 1807.236946760964614f, -1.2f, -1.2f, -0.7f, -0.7f, -0.7f, -0.4f, -0.5f }),

        // DCT4X8 and 8x4
        9 => Dct4X8(),

        // AFV
        10 => Afv0(),

        // DCT64
        11 => Dct(8, LargeX(0.9, 26629.073922049845), LargeY(0.9, 9311.3238710010046), LargeB(0.9, 4992.2486445538634)),

        // DCT64X32
        12 => Dct(8, LargeX(0.65, 23629.073922049845), LargeY(0.65, 8611.3238710010046), LargeB(0.65, 4492.2486445538634)),

        // DCT128X128
        13 => Dct(8, LargeX(1.8, 26629.073922049845), LargeY(1.8, 9311.3238710010046), LargeB(1.8, 4992.2486445538634)),

        // DCT128X64
        14 => Dct(8, LargeX(1.3, 23629.073922049845), LargeY(1.3, 8611.3238710010046), LargeB(1.3, 4492.2486445538634)),

        // DCT256X256
        15 => Dct(8, LargeX(3.6, 26629.073922049845), LargeY(3.6, 9311.3238710010046), LargeB(3.6, 4992.2486445538634)),

        // DCT256X128
        16 => Dct(8, LargeX(2.6, 23629.073922049845), LargeY(2.6, 8611.3238710010046), LargeB(2.6, 4492.2486445538634)),

        // Unreachable: the caller passes a table index below NumQuantTables, and libjxl indexes an array of
        // the 17 tables. A switch expression needs a default arm.
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a quantization table"),
    };

    // The bands of the tables of 64x32 and larger share everything but their first band, which is a
    // product: V(scale * seed).
    private static float[] LargeX(double scale, double seed)
        => new[] { (float)(scale * seed), -1.025f, -0.78f, -0.65012f, -0.19041574084286472f, -0.20819395464f, -0.421064f, -0.32733845535848671f };

    private static float[] LargeY(double scale, double seed)
        => new[] { (float)(scale * seed), -0.3041958212306401f, -0.3633036457487539f, -0.35660379990111464f, -0.3443074455424403f, -0.33699592683512467f, -0.30180866526242109f, -0.27321683125358037f };

    private static float[] LargeB(double scale, double seed)
        => new[] { (float)(scale * seed), -1.2f, -1.2f, -0.8f, -0.7f, -0.7f, -0.4f, -0.5f };

    // The distance bands of the DCT4X4 table, which AFV shares.
    private static void SetDct4X4Params(JxlDctQuantWeightParams p)
        => SetBands(p, 4, new[] { 2200.0f, 0.0f, 0.0f, 0.0f }, new[] { 392.0f, 0.0f, 0.0f, 0.0f }, new[] { 112.0f, -0.25f, -0.25f, -0.5f });

    // The distance bands of the DCT4X8 table, which AFV shares.
    private static void SetDct4X8Params(JxlDctQuantWeightParams p)
        => SetBands(
            p,
            4,
            new[] { 2198.050556016380522f, -0.96269623020744692f, -0.76194253026666783f, -0.6551140670773547f },
            new[] { 764.3655248643528689f, -0.92630200888366945f, -0.9675229603596517f, -0.27845290869168118f },
            new[] { 527.107573587542228f, -1.4594385811273854f, -1.450082094097871593f, -1.5843722511996204f });

    private static JxlQuantEncoding Dct(int numBands, float[] x, float[] y, float[] b)
    {
        JxlQuantEncoding dct = new(QuantMode.Dct);
        SetBands(dct.DctParams, numBands, x, y, b);
        return dct;
    }

    private static JxlQuantEncoding Identity(float[] x, float[] y, float[] b)
    {
        JxlQuantEncoding id = new(QuantMode.Id);
        x.CopyTo(id.IdWeights[0], 0);
        y.CopyTo(id.IdWeights[1], 0);
        b.CopyTo(id.IdWeights[2], 0);
        return id;
    }

    private static JxlQuantEncoding Dct2(float[] x, float[] y, float[] b)
    {
        JxlQuantEncoding dct2 = new(QuantMode.Dct2);
        x.CopyTo(dct2.Dct2Weights[0], 0);
        y.CopyTo(dct2.Dct2Weights[1], 0);
        b.CopyTo(dct2.Dct2Weights[2], 0);
        return dct2;
    }

    private static JxlQuantEncoding Dct4X4()
    {
        JxlQuantEncoding dct4 = new(QuantMode.Dct4);
        SetDct4X4Params(dct4.DctParams);

        // kMul
        for (int c = 0; c < 3; c++)
        {
            dct4.Dct4Multipliers[c][0] = 1.0f;
            dct4.Dct4Multipliers[c][1] = 1.0f;
        }

        return dct4;
    }

    private static JxlQuantEncoding Dct4X8()
    {
        JxlQuantEncoding dct4X8 = new(QuantMode.Dct4X8);
        SetDct4X8Params(dct4X8.DctParams);

        // kMuls
        for (int c = 0; c < 3; c++)
        {
            dct4X8.Dct4X8Multipliers[c] = 1.0f;
        }

        return dct4X8;
    }

    private static JxlQuantEncoding Afv0()
    {
        JxlQuantEncoding afv = new(QuantMode.Afv);
        SetDct4X8Params(afv.DctParams);
        SetDct4X4Params(afv.DctParamsAfv4X4);

        // Per channel: the 4x4/4x8 DC tendency (2), the AFV corner (3) and the AFV high frequencies (4).
        float[][] weights =
        {
            new[] { 3072.0f, 3072.0f, 256.0f, 256.0f, 256.0f, 414.0f, 0.0f, 0.0f, 0.0f },
            new[] { 1024.0f, 1024.0f, 50.0f, 50.0f, 50.0f, 58.0f, 0.0f, 0.0f, 0.0f },
            new[] { 384.0f, 384.0f, 12.0f, 12.0f, 12.0f, 22.0f, -0.25f, -0.25f, -0.25f },
        };
        for (int c = 0; c < 3; c++)
        {
            weights[c].CopyTo(afv.AfvWeights[c], 0);
        }

        return afv;
    }

    private static void SetBands(JxlDctQuantWeightParams p, int numBands, float[] x, float[] y, float[] b)
    {
        p.NumDistanceBands = numBands;
        x.CopyTo(p.DistanceBands[0], 0);
        y.CopyTo(p.DistanceBands[1], 0);
        b.CopyTo(p.DistanceBands[2], 0);
    }

    private static JxlQuantEncoding ThrowInvalidTable(int table)
    {
        JxlThrowHelper.ThrowInvalidImageContentException($"Invalid quantization table {table}");
        return null;
    }
}
