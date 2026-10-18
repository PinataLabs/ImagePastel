#### ICC test profiles

Profiles for the ICC profile tests (`tests/ImageSharp.Tests/Metadata/Profiles/ICC/`). The constants are in `TestImages.Icc`. The files are copied unmodified, apart from the renames that the table shows.

| File | Source | Licence |
|---|---|---|
| `LittleCms/sRGB-v4.icc` | Written by Little CMS 2.20 (`mm2/Little-CMS` at `a57322d8`): `cmsCreate_sRGBProfile()`, profile version 4.3. | Little CMS is MIT. The profile's `cprt` tag says "No copyright, use freely". |
| `LittleCms/sRGB-v2.icc` | The same profile saved as version 2.1, so `desc` is a `textDescriptionType` and the curves are a 4096-entry table. | As above. |
| `LittleCms/Gray22-v4.icc` | `cmsCreateGrayProfile(cmsD50_xyY(), cmsBuildGamma(NULL, 2.2))`, version 4.3: the `*Gray22` profile of `transicc`. | As above. |
| `LittleCms/GrayLab18-v4.icc` | A gray profile with a gamma 1.8 `kTRC` and a **Lab** connection space (the curve gives L\*), version 4.3, written by Little CMS 2.20 with the second program below. Used by the colour engine tests. | As above. |
| `LittleCms/LinearSrgb-v4.icc`, `LittleCms/DisplayP3-v4.icc`, `LittleCms/Rec2020-v4.icc`, `LittleCms/AdobeRgb-v4.icc`, `LittleCms/ProPhoto18-v4.icc`, `LittleCms/ProPhotoMixed-v4.icc`, `LittleCms/GraySrgb-v4.icc`, `LittleCms/GrayLinear-v4.icc`, `LittleCms/GrayGamma22-v4.icc` | Written by Little CMS 2.20 with `cmsCreateRGBProfile` and `cmsCreateGrayProfile` (version 4.4), with the third program below. They are the references for the profiles `IccProfileFactory` builds (`tests/ImageSharp.Tests/ColorManagement/IccProfileFactoryTests.cs`). | As above. |
| `LittleCms/RgbLut8-v2.icc` | A synthetic RGB profile, version 2.1, Lab PCS, whose `A2B0` and `B2A0` tags are `lut8Type` tables (9-point CLUTs, input and output tables). Written by Little CMS 2.20 with `LittleCms/lutprofiles.c` (below). Test only: it approximates an Adobe RGB (1998)-like space. | Little CMS is MIT; the program is Apache-2.0 like this repository. The profile's `cprt` tag says "No copyright, use freely". |
| `LittleCms/RgbLut16-v2.icc` | As above, XYZ PCS, `lut16Type` tables (11-point CLUTs, 512 to 4096-entry tables, and a 3 x 3 matrix in `B2A0`). | As above. |
| `LittleCms/RgbLutAToB-v4.icc` | As above, version 4.3, XYZ PCS, `lutAToBType` and `lutBToAType` tables with every element: A, M and B curves (parametric and tabulated), a matrix with an offset, and CLUTs of 5 x 7 x 9 and 9 x 7 x 5 points. | As above. |
| `LittleCms/GrayLut-v4.icc` | A synthetic gray profile, version 4.3, Lab PCS, with `lutAToBType` (1 input, 17-point CLUT) and `lutBToAType` (3 inputs, 1 output) tables. Written by `LittleCms/lutprofiles.c`. | As above. |
| `LittleCms/FakeCmyk-v4.icc` | A synthetic CMYK output profile, version 4.3, Lab PCS, with `lutAToBType` and `lutBToAType` tables (17-point CLUTs, identity curves; `A2B1` and `B2A1` share their data). Its colors come from the simple CMYK-to-RGB formula with a 300% ink limit; no press data. Written by `LittleCms/fakecmyk.c`, a port of `CreateFakeCMYK` from Little CMS's `testbed/testcms2.c`, built against Little CMS at `5176347` (libjxl `third_party/lcms`). | Little CMS is MIT (the program keeps that licence). The profile's `cprt` tag says "No copyright, use freely". |
| `LittleCms/RgbIntents-v2.icc` | A synthetic RGB output profile, version 2.1, Lab PCS, with different `lut16Type` tables for each intent (`A2B0`/`B2A0` perceptual, `A2B1`/`B2A1` relative colorimetric, `A2B2`/`B2A2` saturation: each its own tone curve, lifted black and chroma scale of an Adobe RGB (1998)-like space) and a paper media white point (X 0.90, Y 0.93, Z 0.73). Written by Little CMS 2.20 with `LittleCms/intentprofiles.c` (below). Used by the rendering intent and black-point compensation tests. | Little CMS is MIT; the program is Apache-2.0 like this repository. The profile's `cprt` tag says "No copyright, use freely". |
| `LittleCms/RgbIntents-v4.icc` | The same perceptual and relative colorimetric tables as `lutAToBType`/`lutBToAType`, version 4.3, without saturation tables (so saturation falls back to the perceptual ones). Written by `LittleCms/intentprofiles.c`. | As above. |
| `LittleCms/RgbIntentsMatrix-v2.icc` | A synthetic RGB display profile, version 2.1: matrix/TRC tags whose curves lift black (`Y = (aX + b)^2.2`, black Y = 0.01), plus only `A2B1`/`B2A1` tables, and a `wtpt` tag that Little CMS ignores for version 2 display profiles. Written by `LittleCms/intentprofiles.c`. | As above. |
| `LittleCms/Rec709Gamma22-v4.icc`, `LittleCms/P3Gamma182224-v4.icc` | Written by Little CMS 2.20 with `cmsCreateRGBProfile` (version 4.4), with the fourth program below. They are the references for the profiles the BMP decoder builds from `LCS_CALIBRATED_RGB` headers (`tests/ImageSharp.Tests/Formats/Bmp/BmpMetadataTests.cs`). | As above. |
| `qcms/sRGB_lcms.icc`, `qcms/ITU-709.icc`, `qcms/ITU-2020.icc` | [qcms](https://github.com/FirefoxGraphics/qcms) at `2ee13006ac1d17d10a53fb5b89ecd6513211c1c3`, from the repository root. | MIT, see `qcms/COPYING`. |
| `CompactIcc/DisplayP3-v4.icc`, `CompactIcc/AdobeCompat-v2.icc`, `CompactIcc/sGrey-v4.icc`, `CompactIcc/sRGB-v2-micro.icc`, `CompactIcc/ProPhoto-v4.icc`, `CompactIcc/scRGB-v2.icc` (linear sRGB) | [Compact-ICC-Profiles](https://github.com/saucecontrol/Compact-ICC-Profiles) at `bdd8466`, as vendored in libjxl `testdata/external`. | CC0 1.0, see `CompactIcc/LICENSE`. |

The Little CMS profiles were written with this program, built against the Little CMS sources:

```c
#include "lcms2.h"

static void save(cmsHPROFILE h, double version, const char* path)
{
    cmsSetProfileVersion(h, version);
    cmsSaveProfileToFile(h, path);
    cmsCloseProfile(h);
}

int main(void)
{
    save(cmsCreate_sRGBProfile(), 4.3, "sRGB-v4.icc");
    save(cmsCreate_sRGBProfile(), 2.1, "sRGB-v2.icc");
    cmsToneCurve* gamma = cmsBuildGamma(NULL, 2.2);
    save(cmsCreateGrayProfile(cmsD50_xyY(), gamma), 4.3, "Gray22-v4.icc");
    cmsFreeToneCurve(gamma);
    return 0;
}
```

`GrayLab18-v4.icc` was written with this program, against the same sources:

```c
#include "lcms2.h"

int main(void)
{
    cmsToneCurve* gamma = cmsBuildGamma(NULL, 1.8);
    cmsHPROFILE h = cmsCreateGrayProfile(cmsD50_xyY(), gamma);
    cmsSetPCS(h, cmsSigLabData);
    cmsMLU* mlu = cmsMLUalloc(NULL, 1);
    cmsMLUsetASCII(mlu, "en", "US", "Gray 1.8, Lab PCS");
    cmsWriteTag(h, cmsSigProfileDescriptionTag, mlu);
    cmsMLUfree(mlu);
    cmsSetProfileVersion(h, 4.3);
    cmsSaveProfileToFile(h, "GrayLab18-v4.icc");
    cmsCloseProfile(h);
    cmsFreeToneCurve(gamma);
    return 0;
}
```

The LUT-based profiles were written with the two C programs in `LittleCms/`. Build each against the Little CMS sources and run it in the output folder:

```sh
gcc -O2 -I<lcms>/include lutprofiles.c liblcms2.a -lm -o lutprofiles && ./lutprofiles   # RgbLut8-v2, RgbLut16-v2, RgbLutAToB-v4, GrayLut-v4
gcc -O2 -I<lcms>/include fakecmyk.c liblcms2.a -lm -o fakecmyk && ./fakecmyk FakeCmyk-v4.icc
gcc -O2 -I<lcms>/include intentprofiles.c liblcms2.a -lm -o intentprofiles && ./intentprofiles   # RgbIntents-v2, RgbIntents-v4, RgbIntentsMatrix-v2 (lcms 2.20, a57322d8)
```

The colour engine tests also use `Tiff/Cmyk/cmyk-test-profile.icc` (a `lut16Type` CMYK profile; see that folder's README) and the sRGB v4 ICC preference profile embedded in `Webp/lossy_with_iccp.webp`.

Expected values in the profile tests (descriptions, colour spaces, colorants) are what Little CMS 2.18 reports for these files through Pillow's `ImageCms`. Expected pixels in the colour engine tests (`tests/ImageSharp.Tests/Processing/Processors/ColorManagement/LittleCmsReferences.cs`) come from `transicc` of Little CMS 2.20; that file gives the command.

The `IccProfileFactory` references were written with this program, against the same sources:

```c
#include "lcms2.h"

static void save(cmsHPROFILE h, const char* description, const char* path)
{
    cmsMLU* mlu = cmsMLUalloc(NULL, 1);
    cmsMLUsetASCII(mlu, "en", "US", description);
    cmsWriteTag(h, cmsSigProfileDescriptionTag, mlu);
    cmsMLUfree(mlu);
    cmsSaveProfileToFile(h, path);
    cmsCloseProfile(h);
}

static cmsHPROFILE rgb(const cmsCIExyY* white, const cmsCIExyYTRIPLE* primaries, cmsToneCurve* r, cmsToneCurve* g, cmsToneCurve* b)
{
    cmsToneCurve* curves[3] = { r, g, b };
    return cmsCreateRGBProfile(white, primaries, curves);
}

int main(void)
{
    cmsCIExyY d65 = { 0.3127, 0.3290, 1.0 };
    cmsCIExyY d50 = { 0.3457, 0.3585, 1.0 };
    cmsCIExyYTRIPLE rec709 = { { 0.64, 0.33, 1.0 }, { 0.30, 0.60, 1.0 }, { 0.15, 0.06, 1.0 } };
    cmsCIExyYTRIPLE p3 = { { 0.680, 0.320, 1.0 }, { 0.265, 0.690, 1.0 }, { 0.150, 0.060, 1.0 } };
    cmsCIExyYTRIPLE rec2020 = { { 0.708, 0.292, 1.0 }, { 0.170, 0.797, 1.0 }, { 0.131, 0.046, 1.0 } };
    cmsCIExyYTRIPLE adobe = { { 0.64, 0.33, 1.0 }, { 0.21, 0.71, 1.0 }, { 0.15, 0.06, 1.0 } };
    cmsCIExyYTRIPLE prophoto = { { 0.7347, 0.2653, 1.0 }, { 0.1596, 0.8404, 1.0 }, { 0.0366, 0.0001, 1.0 } };

    // The sRGB curve as Little CMS builds it for cmsCreate_sRGBProfile (Build_sRGBGamma).
    cmsFloat64Number srgbParams[5] = { 2.4, 1.0 / 1.055, 0.055 / 1.055, 1.0 / 12.92, 0.04045 };
    // ITU-R BT.709 (and BT.2020) inverse OETF.
    cmsFloat64Number bt709Params[5] = { 1.0 / 0.45, 1.0 / 1.099, 0.099 / 1.099, 1.0 / 4.5, 0.081 };
    cmsToneCurve* srgb = cmsBuildParametricToneCurve(NULL, 4, srgbParams);
    cmsToneCurve* bt709 = cmsBuildParametricToneCurve(NULL, 4, bt709Params);
    cmsToneCurve* linear = cmsBuildGamma(NULL, 1.0);
    cmsToneCurve* adobeGamma = cmsBuildGamma(NULL, 563.0 / 256.0);
    cmsToneCurve* gamma18 = cmsBuildGamma(NULL, 1.8);
    cmsToneCurve* gamma22 = cmsBuildGamma(NULL, 2.2);

    save(rgb(&d65, &rec709, linear, linear, linear), "Linear sRGB", "LinearSrgb-v4.icc");
    save(rgb(&d65, &p3, srgb, srgb, srgb), "Display P3", "DisplayP3-v4.icc");
    save(rgb(&d65, &rec2020, bt709, bt709, bt709), "Rec. 2020", "Rec2020-v4.icc");
    save(rgb(&d65, &adobe, adobeGamma, adobeGamma, adobeGamma), "Adobe RGB (1998) compatible", "AdobeRgb-v4.icc");

    cmsHPROFILE h = rgb(&d50, &prophoto, gamma18, gamma18, gamma18);
    cmsSetHeaderRenderingIntent(h, INTENT_RELATIVE_COLORIMETRIC);
    save(h, "ProPhoto primaries, gamma 1.8", "ProPhoto18-v4.icc");

    h = rgb(&d50, &prophoto, gamma18, gamma22, srgb);
    cmsSetHeaderRenderingIntent(h, INTENT_SATURATION);
    save(h, "ProPhoto primaries, mixed curves", "ProPhotoMixed-v4.icc");

    save(cmsCreateGrayProfile(&d65, srgb), "Gray, sRGB curve", "GraySrgb-v4.icc");
    save(cmsCreateGrayProfile(&d65, linear), "Gray, linear", "GrayLinear-v4.icc");
    save(cmsCreateGrayProfile(&d65, gamma22), "Gray, gamma 2.2", "GrayGamma22-v4.icc");

    cmsFreeToneCurve(srgb);
    cmsFreeToneCurve(bt709);
    cmsFreeToneCurve(linear);
    cmsFreeToneCurve(adobeGamma);
    cmsFreeToneCurve(gamma18);
    cmsFreeToneCurve(gamma22);
    return 0;
}
```

The BMP calibrated RGB references were written with this program, against the same sources:

```c
#include "lcms2.h"

static void save(cmsHPROFILE h, const char* description, const char* path)
{
    cmsMLU* mlu = cmsMLUalloc(NULL, 1);
    cmsMLUsetASCII(mlu, "en", "US", description);
    cmsWriteTag(h, cmsSigProfileDescriptionTag, mlu);
    cmsMLUfree(mlu);
    cmsSaveProfileToFile(h, path);
    cmsCloseProfile(h);
}

int main(void)
{
    cmsCIExyY d65 = { 0.3127, 0.3290, 1.0 };
    cmsCIExyYTRIPLE rec709 = { { 0.64, 0.33, 1.0 }, { 0.30, 0.60, 1.0 }, { 0.15, 0.06, 1.0 } };
    cmsCIExyYTRIPLE p3 = { { 0.680, 0.320, 1.0 }, { 0.265, 0.690, 1.0 }, { 0.150, 0.060, 1.0 } };
    cmsToneCurve* gamma18 = cmsBuildGamma(NULL, 1.8);
    cmsToneCurve* gamma22 = cmsBuildGamma(NULL, 2.2);
    cmsToneCurve* gamma24 = cmsBuildGamma(NULL, 2.4);

    cmsToneCurve* rec709Curves[3] = { gamma22, gamma22, gamma22 };
    save(cmsCreateRGBProfile(&d65, &rec709, rec709Curves), "BT.709 primaries, gamma 2.2", "Rec709Gamma22-v4.icc");

    cmsToneCurve* p3Curves[3] = { gamma18, gamma22, gamma24 };
    save(cmsCreateRGBProfile(&d65, &p3, p3Curves), "P3 primaries, gamma 1.8/2.2/2.4", "P3Gamma182224-v4.icc");

    cmsFreeToneCurve(gamma18);
    cmsFreeToneCurve(gamma22);
    cmsFreeToneCurve(gamma24);
    return 0;
}
```
