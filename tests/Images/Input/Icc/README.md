#### ICC test profiles

Profiles for the ICC profile tests (`tests/ImageSharp.Tests/Metadata/Profiles/ICC/`). The constants are in `TestImages.Icc`. The files are copied unmodified, apart from the renames that the table shows.

| File | Source | Licence |
|---|---|---|
| `LittleCms/sRGB-v4.icc` | Written by Little CMS 2.20 (`mm2/Little-CMS` at `a57322d8`): `cmsCreate_sRGBProfile()`, profile version 4.3. | Little CMS is MIT. The profile's `cprt` tag says "No copyright, use freely". |
| `LittleCms/sRGB-v2.icc` | The same profile saved as version 2.1, so `desc` is a `textDescriptionType` and the curves are a 4096-entry table. | As above. |
| `LittleCms/Gray22-v4.icc` | `cmsCreateGrayProfile(cmsD50_xyY(), cmsBuildGamma(NULL, 2.2))`, version 4.3: the `*Gray22` profile of `transicc`. | As above. |
| `LittleCms/GrayLab18-v4.icc` | A gray profile with a gamma 1.8 `kTRC` and a **Lab** connection space (the curve gives L\*), version 4.3, written by Little CMS 2.20 with the second program below. Used by the colour engine tests. | As above. |
| `LittleCms/RgbLut8-v2.icc` | A synthetic RGB profile, version 2.1, Lab PCS, whose `A2B0` and `B2A0` tags are `lut8Type` tables (9-point CLUTs, input and output tables). Written by Little CMS 2.20 with `LittleCms/lutprofiles.c` (below). Test only: it approximates an Adobe RGB (1998)-like space. | Little CMS is MIT; the program is Apache-2.0 like this repository. The profile's `cprt` tag says "No copyright, use freely". |
| `LittleCms/RgbLut16-v2.icc` | As above, XYZ PCS, `lut16Type` tables (11-point CLUTs, 512 to 4096-entry tables, and a 3 x 3 matrix in `B2A0`). | As above. |
| `LittleCms/RgbLutAToB-v4.icc` | As above, version 4.3, XYZ PCS, `lutAToBType` and `lutBToAType` tables with every element: A, M and B curves (parametric and tabulated), a matrix with an offset, and CLUTs of 5 x 7 x 9 and 9 x 7 x 5 points. | As above. |
| `LittleCms/GrayLut-v4.icc` | A synthetic gray profile, version 4.3, Lab PCS, with `lutAToBType` (1 input, 17-point CLUT) and `lutBToAType` (3 inputs, 1 output) tables. Written by `LittleCms/lutprofiles.c`. | As above. |
| `LittleCms/FakeCmyk-v4.icc` | A synthetic CMYK output profile, version 4.3, Lab PCS, with `lutAToBType` and `lutBToAType` tables (17-point CLUTs, identity curves; `A2B1` and `B2A1` share their data). Its colors come from the simple CMYK-to-RGB formula with a 300% ink limit; no press data. Written by `LittleCms/fakecmyk.c`, a port of `CreateFakeCMYK` from Little CMS's `testbed/testcms2.c`, built against Little CMS at `5176347` (libjxl `third_party/lcms`). | Little CMS is MIT (the program keeps that licence). The profile's `cprt` tag says "No copyright, use freely". |
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
```

The colour engine tests also use `Tiff/Cmyk/cmyk-test-profile.icc` (a `lut16Type` CMYK profile; see that folder's README) and the sRGB v4 ICC preference profile embedded in `Webp/lossy_with_iccp.webp`.

Expected values in the profile tests (descriptions, colour spaces, colorants) are what Little CMS 2.18 reports for these files through Pillow's `ImageCms`. Expected pixels in the colour engine tests (`tests/ImageSharp.Tests/Processing/Processors/ColorManagement/LittleCmsReferences.cs`) come from `transicc` of Little CMS 2.20; that file gives the command.
