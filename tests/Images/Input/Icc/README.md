#### ICC test profiles

Profiles for the ICC profile tests (`tests/ImageSharp.Tests/Metadata/Profiles/ICC/`). The constants are in `TestImages.Icc`. The files are copied unmodified, apart from the renames that the table shows.

| File | Source | Licence |
|---|---|---|
| `LittleCms/sRGB-v4.icc` | Written by Little CMS 2.20 (`mm2/Little-CMS` at `a57322d8`): `cmsCreate_sRGBProfile()`, profile version 4.3. | Little CMS is MIT. The profile's `cprt` tag says "No copyright, use freely". |
| `LittleCms/sRGB-v2.icc` | The same profile saved as version 2.1, so `desc` is a `textDescriptionType` and the curves are a 4096-entry table. | As above. |
| `LittleCms/Gray22-v4.icc` | `cmsCreateGrayProfile(cmsD50_xyY(), cmsBuildGamma(NULL, 2.2))`, version 4.3: the `*Gray22` profile of `transicc`. | As above. |
| `LittleCms/GrayLab18-v4.icc` | A gray profile with a gamma 1.8 `kTRC` and a **Lab** connection space (the curve gives L\*), version 4.3, written by Little CMS 2.20 with the second program below. Used by the colour engine tests. | As above. |
| `LittleCms/LinearSrgb-v4.icc`, `LittleCms/DisplayP3-v4.icc`, `LittleCms/Rec2020-v4.icc`, `LittleCms/AdobeRgb-v4.icc`, `LittleCms/ProPhoto18-v4.icc`, `LittleCms/ProPhotoMixed-v4.icc`, `LittleCms/GraySrgb-v4.icc`, `LittleCms/GrayLinear-v4.icc`, `LittleCms/GrayGamma22-v4.icc` | Written by Little CMS 2.20 with `cmsCreateRGBProfile` and `cmsCreateGrayProfile` (version 4.4), with the third program below. They are the references for the profiles `IccProfileFactory` builds (`tests/ImageSharp.Tests/ColorManagement/IccProfileFactoryTests.cs`). | As above. |
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
