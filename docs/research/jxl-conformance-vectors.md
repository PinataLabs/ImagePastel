# libjxl conformance vectors: inventory, thresholds and reference output format

Research for PinataLabs/ImageSharp#101 (child of the JPEG XL map, #100).

**Answer in one line:** the suite has 27 Main-profile vectors (plus 13 `_5` Level 5 variants with looser thresholds). The reference outputs are float32 `.npy` arrays, about 665 MB in total. Each frame passes when its worst per-channel RMSE and its peak absolute error are within the vector's limits. **No reference output is converted through an embedded ICC profile.** Non-XYB ICC images expect the raw decoded samples. XYB images with an ICC profile expect **linear sRGB** (or linear grey), which needs no CMS.

## Sources and snapshot

| Source | Version read |
|---|---|
| `https://github.com/libjxl/conformance` (shallow clone, blobs not fetched in bulk) | `b1d0f990b03e57bf6d137c365cd5dc8b470b9191`, 2026-09-06 |
| GCS bucket `jxl-conformance` object listing (`https://storage.googleapis.com/storage/v1/b/jxl-conformance/o`) | fetched 2026-10-18 |
| `D:\Data\_CodeOS\libjxl` | `ef67fde2ec16d52e644c5a0969230b9a85e3eb31`, 2026-10-06 |
| `D:\Data\_CodeOS\libjxl\testdata` submodule | `73695d303670c90e4d506ea89d9901b081385089` |

Method:

- I fetched every `.icc` blob in full and the first 256 bytes of every `.npy` blob (HTTP Range), to read the dtype and shape.
- I parsed each `input.jxl` header (SizeHeader, ImageMetadata up to ColourEncoding) with a throwaway script. It follows the field order in libjxl `lib/jxl/headers.cc`, `lib/jxl/image_metadata.cc` and `lib/jxl/color_encoding_internal.cc`.
- The parsed bit depths and extra channels match every `test.json`, which supports the parse.
- I ran no script from the conformance repo.

## 1. Inventory

### How the repo is laid out

- `testcases/<id>/` holds `input.jxl`, `test.json` and a viewable preview (`ref.png`/`.apng`/`.gif`). The comparison does not use the preview. Some directories also hold a `source` or `sourcetree` note.
- The comparison files (`reference_image.npy`, `reference.icc`, `original.icc`, `reconstructed.jpg`) are **not in git**. `.gitignore` excludes `*.npy`, `*.icc` and `*.jpg`. `test.json` lists them under `sha256sums`, and they are stored content-addressed in a public GCS bucket: `https://storage.googleapis.com/jxl-conformance/objects/<sha256>`. `scripts/download_and_symlink.sh` downloads them and symlinks them into place. The repo does not use Git LFS.
- One exception: `mul_no_extra_channels/reference_image.npy` (844 B) is checked in.
- `testcases/main_level5.txt` (24 ids) and `testcases/main_level10.txt` (27 ids) are the corpus lists. libjxl CI runs one list per level (`.github/workflows/conformance.yml`, matrix `[level5, level10]`).
- A `*_5` directory holds the Level 5 copy of a lossy vector. Its `input.jxl` and preview are symlinks to the base directory. Its `test.json` has looser thresholds. Three of them have their own reference (`animation_icos4d_5`, `patches_5`, `upsampling_5`). The others reuse the base vector's `.npy`.
- All vectors are **Main profile**: the corpus lists are named `main_level5` and `main_level10`. The streams that carry a `jxll` box declare level 10.

### Per-vector table

- Features come from the conformance `README.md`. The header facts come from my parse. Sizes are in bytes.
- Thresholds are `rms_error / peak_error` in nominal [0, 1] sample units. "L5" means the `_5` variant.
- "enum" means the colour is signalled by JPEG XL's built-in colour description. "ICC" means `want_icc` is set and an ICC profile is embedded.

| id | Levels | Features (README) | Header: size, bits, extra ch., XYB, colour | `input.jxl` | reference `.npy` (shape F,H,W,C) | Thresholds L10 | Thresholds L5 |
|---|---|---|---|---|---|---|---|
| alpha_nonpremultiplied | 5, 10 | Modular, alpha, 12-bit | 1024x1024, 12, Alpha12, no, sRGB enum | 30 | 16,777,298 (1,1024,1024,4) | 6.1035e-5 / 6.1035e-5 | same dir |
| alpha_premultiplied | 10 | VarDCT, premultiplied alpha, 12-bit colour, 16-bit alpha | 1024x1024, 12, Alpha16 premul, yes, sRGB enum; container `jxll`=10 | 8,772 | 16,777,298 (1,1024,1024,4) | 3.815e-6 / 3.815e-6 | not in L5 |
| alpha_triangles | 5, 10 | Modular, alpha, 9-bit | 1024x1024, 9, Alpha9, no, sRGB enum | 61 | 16,777,298 (1,1024,1024,4) | 0.001953125 / 0.001953125 | same dir |
| animation_icos4d | 10 (+`_5`) | VarDCT, alpha, animation | 128x128, 8, Alpha8, yes, sRGB enum; 48 frames | 352,478 | 12,582,993 (48,128,128,4) | 1e-4 / 0.005 | 0.02 / 0.06 (own `.npy`) |
| animation_newtons_cradle | 5, 10 | Modular, palette, animation | 480x360, 8, Alpha8, no, sRGB enum; 36 frames | 238,305 | 99,532,881 (36,360,480,4) | 9.76562e-4 / 9.76562e-4 | same dir |
| animation_spline | 10 (+`_5`) | Splines, animation | 320x320, 8, none, yes, sRGB enum; 60 frames | 9,581 | 73,728,081 (60,320,320,3) | 1e-4 / 0.004 | 0.02 / 0.06 |
| bench_oriented_brg | 10 (+`_5`) | Container, VarDCT, JPEG reconstruction, orientation, ICC | 500x606 orientation 5, 8, none, no, ICC "BRG" (2,712); `Exif`, `jbrd` boxes | 184,341 | 3,636,080 (1,500,606,3) | 1e-5 / 0.004 | 0.02 / 0.06 |
| bicycles | 5, 10 | Modular, Squeeze, XYB | 1024x631, 8, none, yes, sRGB (all-default metadata) | 73,501 | 7,753,809 (1,631,1024,3) | 9.76562e-4 / 9.76562e-4 | same dir |
| bike | 10 (+`_5`) | VarDCT | 2048x2560, 8, none, yes, enum RGB_D65_sRGB-primaries_709-TF_Perceptual | 388,268 | 62,914,642 (1,2560,2048,3) | 1e-4 / 0.007 | 0.02 / 0.06 |
| blendmodes | 10 (+`_5`) | Modular, blend modes, 12-bit | 1024x1024, 12, Alpha12, no, sRGB enum | 145 | 16,777,298 (1,1024,1024,4) | 0.004 / 0.0001 (peak < RMSE, as published) | 0.02 / 0.06 |
| cafe | 10 (+`_5`) | Container, VarDCT, JPEG reconstruction, chroma upsampling | 1280x1600, 8, none, no, ICC (lcms, 896); `jbrd` box | 381,505 | 24,576,082 (1,1600,1280,3) | 1e-5 / 0.004 | 0.02 / 0.06 |
| cmyk_layers | 10 | Modular, CMYK (kBlack), layers, large ICC | 512x512, 8, Black8 + Alpha8, no, ICC CMYK "U.S. Web Coated (SWOP) v2" (557,168); `jxll`=10 | 387,140 | 5,242,960 (1,512,512,5) | 9.76562e-4 / 9.76562e-4 | not in L5 |
| delta_palette | 5, 10 | Modular, delta palette | 555x751, 8, none, no, sRGB enum | 107,899 | 5,001,740 (1,751,555,3) | 9.76562e-4 / 9.76562e-4 | same dir |
| grayscale | 10 (+`_5`) | VarDCT, greyscale ICC | 200x200, 8, none, yes, ICC grey "Dot Gain 10%" (912) | 1,069 | 160,080 (1,200,200,1) | 1e-4 / 0.004 | 0.02 / 0.06 |
| grayscale_jpeg | 10 (+`_5`) | Container, VarDCT, JPEG reconstruction, greyscale | 200x200, 8, none, no, ICC grey "Dot Gain 30%" (912); `jbrd` box | 7,799 | 160,080 (1,200,200,1) | 1e-5 / 0.004 | 0.02 / 0.06 |
| grayscale_public_university | 5, 10 | Modular, Squeeze, greyscale | 2880x1620, 8, none, no, enum Gray_D65_sRGB-TF_Relative | 63,377 | 18,662,482 (1,1620,2880,1) | 9.76562e-4 / 9.76562e-4 | same dir |
| lossless_pfm | 10 | Modular, lossless 32-bit float | 500x500, float32 (8 exponent bits), none, no, sRGB enum; `jxll`=10 | 774,294 | 3,000,080 (1,500,500,3) | 0 / 0 (bit-exact) | not in L5 |
| lz77_flower | 5, 10 | Modular, LZ77 | 834x244, 8, none, no, enum RGB_D65_sRGB-primaries_gamma0.45455_Relative | 103,540 | 2,442,032 (1,244,834,3) | 9.76562e-4 / 9.76562e-4 | same dir |
| mul_no_extra_channels | 5, 10 | Modular, Mul blending without extra channels | 8x8, 8, none, no, sRGB enum | 32 | 844, in git (1,8,8,3) | 1e-4 / 0.004 | same dir |
| noise | 10 (+`_5`) | Noise | 500x606, 8, none, yes, sRGB (all-default) | 118,125 | 3,636,080 (1,606,500,3) | 1e-4 / 0.004 | 0.02 / 0.06 |
| opsin_inverse | 10 (+`_5`) | Custom OpsinInverseMatrix | 500x606, 8, none, yes, sRGB (all-default) | 118,148 | 3,636,080 (1,606,500,3) | 1e-4 / 0.004 | 0.02 / 0.06 |
| patches | 10 (+`_5`) | VarDCT, patches | 1600x1096, 8, Alpha8, yes, ICC Apple "Colour LCD Calibrated" (2,924); `Exif`, `xml ` boxes | 66,562 | 28,057,682 (1,1096,1600,4) | 1e-4 / 0.004 | 0.02 / 0.06 (own `.npy`) |
| patches_lossless | 5, 10 | Modular, patches | 1600x1096, 8, Alpha8, no, same Apple ICC; `Exif`, `xml ` boxes | 34,207 | 28,057,682 (1,1096,1600,4) | 9.76562e-4 / 9.76562e-4 | same dir |
| progressive | 10 (+`_5`) | VarDCT, LF frame (Modular + Squeeze), TOC permutation, HF passes | 4064x2704, 8, none, yes, ICC (lcms, 896) | 467,605 | 131,868,754 (1,2704,4064,3) | 1e-4 / 0.02 | 0.02 / 0.06 |
| spot | 5, 10 | Modular, two spot colours, two layers, 16-bit ProPhoto | 600x400, 16, Alpha16 + 2 x SpotColor16, no, ICC "ProPhoto RGB" (940); `jxll`=10 | 392,254 | 5,760,080 (1,400,600,6) | 3.815e-6 / 3.815e-6 | same dir |
| sunset_logo | 5, 10 | Modular, RCT, 10-bit, orientation, two layers | 1386x924 coded, orientation 7, 10, Alpha10, no, sRGB enum | 218 | 20,490,705 (1,1386,924,4) | 2.44141e-4 / 2.44141e-4 | same dir |
| upsampling | 10 (+`_5`) | VarDCT, alpha, upsampling | 800x600, 8, Alpha8, yes, sRGB enum | 10,567 | 7,680,080 (1,600,800,4) | 1e-4 / 0.004 | 0.02 / 0.06 (own `.npy`) |

The JPEG-reconstruction vectors also have a byte-exact `reconstructed.jpg`: bench_oriented_brg 218,481 B, cafe 436,641 B and grayscale_jpeg 9,218 B. The map puts `jbrd` out of scope, so only their pixel check applies to us.

Level notes:

- Three vectors are Level 10 only: `alpha_premultiplied`, `cmyk_layers` and `lossless_pfm`.
- `spot` is listed in `main_level5.txt` although its container has `jxll` level 10. This looks like an upstream inconsistency; it does not change what we test.
- Level 5 forbids the kBlack channel and more than 16 bits per colour sample (libjxl `lib/include/jxl/encode.h:1394-1411`). That explains why `cmyk_layers` and `lossless_pfm` are Level 10 only. `alpha_premultiplied` declares `jxll` level 10, but its header shows no Level 10-only feature. The reason may sit in coding-tool limits that the header does not show.
- The `_5` thresholds (RMSE 0.02, peak 0.06) are much looser than Level 10. My guess is that this tolerance lets Level 5 decoders use lower internal precision. The conformance repo does not state the reason; ISO/IEC 18181-3 is the authority, and I did not read it.

### Download size

| What | Bytes |
|---|---|
| Unique `input.jxl` bitstreams (in git) | 4,289,823 (4.3 MB) |
| Git checkout of `testcases/` (inputs, `test.json`, PNG/APNG/GIF previews) | 44,766,683 (44.8 MB) |
| Blobs referenced by `test.json` (48 objects) | **665,247,084 (665 MB)**: `.npy` 664.0 MB, `.icc` 0.57 MB, `.jpg` 0.66 MB |
| Level 5 list only | 591,348,823 |
| Level 10 list only | 616,926,329 |
| Whole bucket, which `download_and_symlink.sh` mirrors (85 objects, including stale ones) | 1,230,536,089 (1.23 GB) |

The four largest references are `progressive` (132 MB), `animation_newtons_cradle` (99.5 MB), `animation_spline` (73.7 MB) and `bike` (62.9 MB). The `.npy` files are uncompressed float32, so they should compress well; I did not measure this. Fetching by sha256 from the bucket works anonymously over HTTPS, and Range requests work.

## 2. Reference output format and how thresholds are checked

### What is stored

Each `test.json` holds `frames[]` (one entry per displayed frame: `name`, `duration` for animations, `rms_error`, `peak_error`), metadata (`intensity_target`, `min_nits`, `relative_to_max_display`, `linear_below`, `extra_channel_type`, `bits_per_sample`, `exp_bits_per_sample`), optional `original_icc` / `reconstructed_jpeg` / `preview` keys, and `sha256sums`. No vector has a `preview` key.

The references were made by `djxl input.jxl reference_image.npy --icc_out reference.icc --metadata_out test.json` (libjxl `tools/conformance/generator.py`). The thresholds were added afterwards. `djxl` decodes with libjxl defaults (`lib/extras/dec/jxl.h:39-66`, `tools/djxl_main.cc:398-408`). So the reference pixels are:

- **Type:** `.npy` v1.0, `{'descr': '<f4', 'fortran_order': False, 'shape': (frames, ysize, xsize, channels)}`: float32, little-endian, interleaved (`lib/extras/enc/npy.cc:235-295`). Integer samples are scaled to nominal [0, 1]. Values are **not clamped**: the `grayscale` reference ranges from -0.00017 to 1.0017, with 445 of 40,000 samples outside [0, 1].
- **Channels:** the colour channels (1 or 3), then **every** extra channel in header order: alpha, Black, spot colours. For example, `cmyk_layers` has 5 channels (C, M, Y, K, A) and `spot` has 6 (R, G, B, A, spot, spot).
- **Spot colours are not rendered:** the runner passes `--norender_spotcolors`.
- **Alpha stays as coded:** `unpremultiply_alpha = false`, so `alpha_premultiplied` expects premultiplied colour.
- **Orientation is applied:** `keep_orientation = false`. `bench_oriented_brg` (orientation 5) is coded as 500x606 but stored as H=500, W=606. `sunset_logo` (orientation 7) is coded as 1386x924 but stored as H=1386, W=924.
- **Frames are coalesced:** `coalescing = true`. Each `.npy` frame is a composited display frame, and zero-duration layers are merged. `cmyk_layers`, `spot` and `sunset_logo` each have one frame.
- **CMYK stays CMYK:** the NumPy encoder accepts CMYK (`npy.cc:327`), so `djxl` does not convert to sRGB (`djxl_main.cc:408`).

**Colour space.** Without `--color_space`, `djxl` sets no CMS and asks for the colour profile of the pixel data (`JXL_COLOR_PROFILE_TARGET_DATA`), then writes that profile to `reference.icc` (`lib/extras/dec/jxl.cc:404-433`; the CMS is set only on the `--color_space` path at line 406). libjxl picks the output encoding in `OutputEncodingInfo::SetFromMetadata` (`lib/jxl/dec_xyb.cc:160-164`):

- **Non-XYB image:** the output is the coded samples, in the original colour space, whether that is an enum or an embedded ICC. No conversion happens.
- **XYB image with an enum colour space:** the output is in that enum space. This is an analytic inverse-XYB transform plus a transfer function.
- **XYB image with an embedded ICC:** the output is **linear sRGB**, or linear grey for a grey image.

The `reference.icc` descriptions confirm this (table below).

### How `tools/conformance/conformance.py` checks a vector

These are the steps in `D:\Data\_CodeOS\libjxl\tools\conformance\conformance.py`:

1. **Run the decoder.** It calls `decoder input.jxl decoded_image.npy --metadata_out meta.json --icc_out decoded.icc --norender_spotcolors`, plus `--orig_icc_out` when `original_icc` is present and `--preview_out` when `preview` is present. If `reconstructed_jpeg` is present, it runs the decoder a second time to write a `.jpg`.
2. **Compare exact files.** `original.icc` and `reconstructed.jpg` must be byte-identical (`CompareBinaries`).
3. **Check the metadata.** `CheckMeta` compares the decoder's `meta.json` with `test.json`, ignoring the threshold keys. Floats must be within 1e-4. But the function **returns after the first key of each dict or list** (`return CheckMeta(vv, v)` inside the loop, lines 95 and 100). In practice it checks only `frames[0].name`.
4. **Compare pixels per frame** (`CompareNPY`, lines 32-63):
   - The shapes must be identical.
   - If `reference.icc` and `decoded.icc` differ in their bytes and `peak_error > 0`, the decoded RGB is first converted from the decoded ICC to the reference ICC with lcms2 (perceptual intent, float64; `lcms2.py`). This step needs at least 3 channels.
   - `error = |ref - dec|` over all channels, extra channels included.
   - It computes the RMSE of each channel over the frame, and **fails if the worst channel's RMSE is above `rms_error`**.
   - It **fails if the maximum absolute error over all samples is above `peak_error`**.
   - **PSNR is not used.** `peak_error = 0` (only `lossless_pfm`) means a bit-exact float match, and the colour conversion is skipped.

The copy of `scripts/conformance.py` in the conformance repo is newer and differs in four ways:

- `--lax` mode skips `original.icc` and `reconstructed.jpg` and clamps both images to [0, 1].
- It allows an RGBA output against an RGB reference if the alpha is all 1.
- It converts greyscale through lcms2 as well.
- It **skips the colour conversion when `original.icc` does not match byte for byte** (`try_color_transform`).

### How the thresholds relate to bit depth

The tight lossless limits are a fraction of one quantisation step at the coded depth:

| Coded depth | Limit | One step at that depth | Ratio |
|---|---|---|---|
| 8-bit | 2^-10 = 9.77e-4 | 1/255 | about 1/4 step |
| 9-bit | 2^-9 = 1.95e-3 | 1/511 | about 1 step |
| 10-bit | 2^-12 = 2.44e-4 | 1/1023 | about 1/4 step |
| 12-bit | 2^-14 = 6.1e-5 | 1/4095 | about 1/4 step |
| 16-bit | 2^-18 = 3.8e-6 | 1/65535 | about 1/4 step |

Lossy Level 10 vectors use an RMSE of 1e-4 (1e-5 for the JPEG-recompressed ones) and a peak of 0.004 to 0.02, which is about one to five 8-bit steps.

## 3. ICC-only vectors, and whether the references go through the profile

Nine vectors describe their colour only through an embedded ICC profile (`want_icc` set):

| id | XYB? | Embedded ICC (`original.icc`) | `reference.icc` (output space) | What the reference pixels are |
|---|---|---|---|---|
| bench_oriented_brg | no | "BRG" (DCAL, 2,712 B) | same bytes | raw decoded samples, no conversion |
| cafe | no | lcms "RGB_D65_SRG_Per_709" (896 B) | same bytes | raw decoded samples, no conversion |
| cmyk_layers | no | Adobe "U.S. Web Coated (SWOP) v2", CMYK (557,168 B) | same bytes | raw C, M, Y + K + A, no conversion |
| grayscale_jpeg | no | Adobe "Dot Gain 30%", grey (912 B) | same bytes | raw decoded samples, no conversion |
| patches_lossless | no | Apple "Colour LCD Calibrated" (2,924 B) | same bytes | raw decoded samples, no conversion |
| spot | no | Kodak "ProPhoto RGB" (940 B) | same bytes | raw decoded samples, no conversion |
| grayscale | **yes** | Adobe "Dot Gain 10%", grey (912 B) | libjxl-made "Gra_D65_Rel_Lin" | **linear grey** (analytic inverse XYB) |
| patches | **yes** | Apple "Colour LCD Calibrated" (2,924 B) | libjxl-made "RGB_D65_SRG_Rel_Lin" | **linear sRGB** (analytic inverse XYB) |
| progressive | **yes** | lcms "RGB_D65_SRG_Per_709" (896 B) | libjxl-made "RGB_D65_SRG_Rel_Lin" | **linear sRGB** (analytic inverse XYB) |

**Conclusion:**

- **No reference output is converted through its embedded ICC profile.** A decoder without a CMS can match all nine.
- The map's no-CMS stance holds for the pass criterion, with one refinement. For **XYB + ICC** images the decoder must output linear sRGB (or linear grey), and those pixels are *not* in the embedded profile's space.
- So "attach the embedded ICC unconverted" is correct for non-XYB images only. Attaching the ICC to linear-sRGB pixels would mislabel them. This affects the colour design (see "Follow-ups" below) and answers #105: none of its three options is needed.

There is one more trap, which affects only the strict exact-file check of `original.icc` on enum-coded images:

- When the colour is an enum, `original.icc` is a profile that libjxl *synthesises*, not one stored in the file. This happens for `bike`, `grayscale_public_university` and `lz77_flower`.
- Its bytes depend on the libjxl version. `bike` and `lz77_flower` have `original.icc` from a newer generator (ICC v4.4 with a `cicp` tag) and `reference.icc` from an older one (v4.3), with the same description.
- A managed decoder would need byte-identical ICC synthesis to pass the strict check. Our harness should instead compare `original.icc` byte for byte only when the stream embeds one, and compare the decoded colour encoding for enum images. The upstream `--lax` mode skips this check entirely.
- Embedded profiles come from decoding the ICC stream in the codestream, so matching them byte for byte is a real decoder requirement.

## 4. Licence

- **The repo:** `LICENSE` is BSD-3-Clause ("Copyright (c) the JPEG XL Project Authors"). GitHub's licence API reports `BSD-3-Clause` for `libjxl/conformance`.
- **Bitstreams and reference outputs:** `testcases/README.md` says *"All test bitstreams and reference decoded images can be reproduced freely for testing purposes."* It then lists a licence for each **source image** the bitstreams were made from:

| Source licence | Vectors |
|---|---|
| CC0 | alpha_*, animation_icos4D (Wikimedia CC0), animation_spline, bench_oriented_BRG, bicycles, blendmodes, cmyk_layers, delta_palette, grayscale, grayscale_jpeg, lossless_pfm, lz77_flower, mul_no_extra_channels, noise, opsin_inverse, patches, patches_lossless, spot, sunset_logo |
| CC BY-SA 3.0 (Wikimedia) | animation_newtons_cradle, upsampling |
| CC BY 3.0, Arnold and Richter Cine Technik GmbH | grayscale_public_university |
| ITU-T T.24 test-image licence (`itu-t.24_ReadMe.txt`) | bike, cafe |
| Microsoft custom licence (`progressive/source`) | progressive |

Implications for #106. This is my reading, not legal advice.

- The CC0 vectors only need the "reproduce freely for testing" note and the BSD notice.
- CC BY and CC BY-SA need attribution. CC BY-SA also needs ShareAlike for the data files.
- **ITU-T T.24** gives a non-exclusive right "to freely reproduce the information ... for intended purposes only". The notice must be included in every copy, and any "financial profit from making available" is excluded. **Microsoft** grants use "for any purpose", provided a copyright notice is shown.
- `bike`, `cafe` and `progressive` are the ones to handle with care. Fetching them at test time is safer than committing them.

## 5. libjxl `testdata/` `.jxl` files that conformance does not cover

`testdata/` is CC BY 4.0 (`testdata/LICENSE`). It has only six `.jxl` files, plus 44 minimised OSS-Fuzz inputs.

| File | Size | What it adds over conformance | Reference available | Worth adding? |
|---|---|---|---|---|
| `jxl/blending/cropped_traffic_light.jxl` | 793 | Cropped frames with blending, 4 animation frames, 50x80, alpha. libjxl checks each frame for an exact match against `cropped_traffic_light_frame-{0..3}.png` (`lib/jxl/blending_test.cc`). | yes, 4 PNGs | **Yes**: small, exact, covers frame crop and blend |
| `jxl/splines.jxl` | 81 | A spline-only 2048x2048 Modular image from `splines.tree`. Golden `splines.png`, relative error 1e-2 / 1e-1 after clamping (`lib/jxl/splines_test.cc:291`). | yes, PNG | **Yes**: splines without animation or XYB |
| `jxl/spline_on_first_frame.jxl` | 76 | Splines must be cleared on every frame. Golden PNG compared after conversion to sRGB (`splines_test.cc:316`). | yes, PNG | **Yes**: catches a state-reset bug |
| `jxl/boxes/square-extended-size-container.jxl` | 104 | A container box with a 64-bit "extended" size (`size == 1`) (`lib/jxl/decode_test.cc:5147`). | no pixels; checks box parsing | **Yes**: the container parser edge case no conformance file has |
| `jxl/pq_gradient.jxl` | 137 | PQ transfer function, 16-bit grey, 1088x64. No conformance vector uses PQ or HLG. | none in testdata; make one with `djxl` at test time | Yes, if HDR enum output is in scope (it is, through the analytic enum spaces) |
| `jxl/jpeg_reconstruction/1x1_exif_xmp.jxl` | 4,104 | Exif + XMP boxes alongside `jbrd`. The reference is the original `.jpg`. | `.jpg` only | Only for Exif/XMP box mapping; `jbrd` is out of scope |
| `oss-fuzz/clusterfuzz-testcase-minimized-djxl_fuzzer-*` (44 files, 437 KB) | small | Crash regressions. libjxl only checks that they do not crash (`tools/djxl_fuzzer_test.cc`). | no | **Yes**, for the hardening work: they must fail cleanly |

Features that neither conformance nor `testdata/` covers with a pixel reference:

- preview images (no vector has `preview`)
- split codestreams in `jxlp` boxes (every container vector uses one `jxlc`)
- Brotli-compressed `brob` metadata boxes
- animation timecodes
- non-coalesced (layer) output
- the Depth, Thermal and CFA extra channels
- HLG
- `intrinsic_size`

Encoder round-trips or `djxl`-generated references would have to cover these.

## Follow-ups

- **Correct the colour stance for XYB + ICC images.** The decoder outputs linear sRGB (or linear grey). Should the embedded ICC then be exposed as "original profile" metadata rather than as the pixels' profile? This belongs in the colour or metadata ticket. The map's "attach unconverted" wording is right only for non-XYB images.
- **The test seam needs more than `Image<TPixel>`.** References carry all extra channels (Black, spot colours), keep alpha premultiplied, and hold unclamped float values. A conformance harness therefore needs an internal decoded-frame view: float planes for every channel, before unpremultiplying and clamping. A plain `Image<RgbaVector>` loses data.
- **How CMYK and spot colours map onto ImageSharp's pixel model** (`cmyk_layers`, `spot`). This may need its own ticket.
- **Reference storage for #106.** The 665 MB of `.npy` is too big to commit. Options: fetch by sha256 at test time with a local cache; commit a compressed or cropped subset; or regenerate with `djxl` at test time (the map allows native tools in tests).
- **Write our own metadata check.** Don't copy upstream's `CheckMeta`; it returns early and checks almost nothing.
