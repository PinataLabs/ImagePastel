#### ICC test profiles

Profiles for the ICC profile tests (`tests/ImageSharp.Tests/Metadata/Profiles/ICC/`). The constants are in `TestImages.Icc`. The files are copied unmodified, apart from the renames that the table shows.

| File | Source | Licence |
|---|---|---|
| `LittleCms/sRGB-v4.icc` | Written by Little CMS 2.20 (`mm2/Little-CMS` at `a57322d8`): `cmsCreate_sRGBProfile()`, profile version 4.3. | Little CMS is MIT. The profile's `cprt` tag says "No copyright, use freely". |
| `LittleCms/sRGB-v2.icc` | The same profile saved as version 2.1, so `desc` is a `textDescriptionType` and the curves are a 4096-entry table. | As above. |
| `LittleCms/Gray22-v4.icc` | `cmsCreateGrayProfile(cmsD50_xyY(), cmsBuildGamma(NULL, 2.2))`, version 4.3: the `*Gray22` profile of `transicc`. | As above. |
| `LittleCms/GrayLab18-v4.icc` | A gray profile with a gamma 1.8 `kTRC` and a **Lab** connection space (the curve gives L\*), version 4.3, written by Little CMS 2.20 with the second program below. Used by the colour engine tests. | As above. |
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
