#### cjxl calibration table

`calibration.json` records how libjxl's own encoder does on six small images, so that the lossy encoder tests can judge quality on every CI leg without the native tools (#107). For each image, effort (3, 5 and 7: the encoder's Fastest, Default and Thorough) and distance (1, 2 and 4), it holds the size of `cjxl` v0.12.0's output and the score that the `ssimulacra2` tool gives djxl's 16-bit PNG of it: 54 rows. The tests read it through `JxlCalibrationTable`, and `JxlQualityComparer` applies #107's rule to an encode against a row.

| Name | File | Kind | Size | Source and licence |
|---|---|---|---|---|
| `bike` | `Png/Bike.png` | photo | 582 x 453, RGB | existing test image of this repository |
| `calliphora` | `Png/CalliphoraPartial.png` | photo (macro) | 378 x 349, RGB | existing test image of this repository |
| `bike_alpha` | `Png/bike-small.png` | photo with alpha | 288 x 163, RGBA | existing test image of this repository |
| `gradient` | `Jxl/Calibration/gradient.png` | synthetic gradient | 256 x 256, RGB | made for this repository by `make_calibration.py`; Apache License, Version 2.0 |
| `text` | `Jxl/Calibration/text.png` | text | 320 x 200, RGB | made for this repository by `make_calibration.py` with its own 5 x 7 bitmap font; Apache License, Version 2.0 |
| `line_art` | `Jxl/Calibration/line_art.png` | line art | 256 x 256, RGB | made for this repository by `make_calibration.py`; Apache License, Version 2.0 |

Every image is 8-bit sRGB with no colour chunks, so every tool and the managed SSIMULACRA2 read it the same way. Each is at most 6 groups of 256 x 256, below the 8 groups above which `cjxl` streams a frame.

#### How the table was made

With the Windows x64 tools of the [v0.12.0 release](https://github.com/libjxl/libjxl/releases/tag/v0.12.0) (`jxl-x64-windows-static.zip`, AVX2):

```sh
py -3 make_calibration.py images   # gradient.png, text.png and line_art.png
JXL_TOOLS_DIR=<folder with cjxl, djxl and ssimulacra2> py -3 make_calibration.py table
```

For each row the table step runs:

```sh
cjxl <image> <out.jxl> -d <distance> -e <effort>
djxl <out.jxl> <out.png> --bits_per_sample=16
ssimulacra2 <image> <out.png>
```

The arguments are stored in the table (`cjxl_arguments`, `djxl_arguments`). The `jxl-oracle` job runs them again in `JxlCalibrationOracleTests.Table_IsReproduced`.

- **Why djxl's PNG is scored, not `out.jxl`:** `ssimulacra2 <image> <out.jxl>` scores the decoder's float samples before clamping, and lossy output rings past 0 and 1 at sharp edges. An `Image<TPixel>` decode clamps, as djxl's integer output does. On `line_art` at effort 7, distance 4, the two differ by 9 points (55.2 against 64.5); on photos by up to 0.7.
- **Reproduction:** two runs on one machine give the same table. The Linux x64 tools of the same release (Clang 18, where Windows is Clang 22) differ on the same AVX2 machine: 7 of the 54 files are up to 11 bytes (0.05 %) away, and scores up to 0.074. The oracle test allows 0.25 % on size and 0.15 on score.
