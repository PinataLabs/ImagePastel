#### Generated JPEG XL test files

These files cover features that no conformance vector checks. They were made for this repository, with the libjxl tools at v0.12.0 (`jxl-x64-windows-static.zip` from the [v0.12.0 release](https://github.com/libjxl/libjxl/releases/tag/v0.12.0)). They are licensed under the Apache License, Version 2.0, like the rest of the repository.

| File | Why it is here | Reference |
|---|---|---|
| `premultiplied_alpha.jxl` | Lossless Modular, 16-bit RGB with premultiplied (associated) alpha. Alpha is 0, 1/4, 1/2, 3/4 and 1 in bands of 8 columns. | `premultiplied_alpha_straight.png` (straight alpha, for `Image<TPixel>`); `External/ReferenceOutput/JxlDecoderTests/premultiplied_alpha.png` (as coded, for `DecodeToPlanes`) |
| `greyscale.jxl` | Lossless Modular, 8-bit grey, no squeeze. | `External/ReferenceOutput/JxlDecoderTests/greyscale.png` |
| `greyscale_alpha.jxl` | Lossless Modular, 16-bit grey with straight alpha, no squeeze. `cjxl` puts it in a container with a `jxll` box. | `External/ReferenceOutput/JxlDecoderTests/greyscale_alpha.png` |
| `greyscale_squeeze.jxl` | Lossless Modular, 8-bit grey with the default squeeze and no loop filters: the D3 done-check. Same pixels as `greyscale.jxl`. | `External/ReferenceOutput/JxlDecoderTests/greyscale_squeeze.png` |
| `rgba_squeeze.jxl` | Lossless Modular, 8-bit RGBA with the default squeeze, including the chroma and alpha channels. 321 x 283: odd sizes, and more than one group. | `External/ReferenceOutput/JxlDecoderTests/rgba_squeeze.png` |
| `greyscale_squeeze_wide.jxl` | Lossless Modular, 8-bit grey with the default squeeze, 2061 x 40. It is wider than one 2048-pixel DC group, so the squeezed channels with a shift of 3 or more are decoded in the DC groups. | `External/ReferenceOutput/JxlDecoderTests/greyscale_squeeze_wide.png` |
| `exif_xmp.jxl` | Lossless Modular, 16 x 16 8-bit RGB with the Exif and XMP of libjxl's `1x1_exif_xmp.jpg` in uncompressed `Exif` and `xml ` boxes. `cjxl` splits the codestream into two `jxlp` boxes with the metadata boxes between them. | `External/ReferenceOutput/JxlDecoderTests/exif_xmp.png` |
| `exif_xmp_brob.jxl` | The same image and metadata, with both metadata boxes Brotli-compressed in `brob` boxes. | `External/ReferenceOutput/JxlDecoderTests/exif_xmp.png` |
| `custom_icc.jxl` | Lossless Modular, 32 x 32 8-bit RGB with an embedded 600-byte ICC profile: made-up primaries and a tabulated tone curve, so no colour encoding describes it. | `External/ReferenceOutput/JxlDecoderTests/custom_icc.png`, whose `iCCP` chunk holds the same profile |
| `colour_srgb.jxl` | Lossless Modular, 32 x 32 8-bit, with the colour encoding `sRGB`: sRGB: the pixel profile is null. | `External/ReferenceOutput/JxlDecoderTests/colour_srgb.png`, whose `iCCP` chunk is the profile `djxl` builds |
| `colour_grey_srgb.jxl` | Lossless Modular, 32 x 32 8-bit, with the colour encoding `Gra_D65_Rel_SRG`: grey with the sRGB curve: the pixel profile is null. | `External/ReferenceOutput/JxlDecoderTests/colour_grey_srgb.png`, whose `iCCP` chunk is the profile `djxl` builds |
| `colour_rec2020.jxl` | Lossless Modular, 32 x 32 8-bit, with the colour encoding `RGB_D65_202_Rel_709`: Rec.2020 primaries, BT.709 curve. | `External/ReferenceOutput/JxlDecoderTests/colour_rec2020.png`, whose `iCCP` chunk is the profile `djxl` builds |
| `colour_display_p3.jxl` | Lossless Modular, 32 x 32 8-bit, with the colour encoding `DisplayP3`: P3 primaries, sRGB curve. | `External/ReferenceOutput/JxlDecoderTests/colour_display_p3.png`, whose `iCCP` chunk is the profile `djxl` builds |
| `colour_linear_srgb.jxl` | Lossless Modular, 32 x 32 8-bit, with the colour encoding `RGB_D65_SRG_Rel_Lin`: sRGB primaries, linear. | `External/ReferenceOutput/JxlDecoderTests/colour_linear_srgb.png`, whose `iCCP` chunk is the profile `djxl` builds |
| `colour_gamma22.jxl` | Lossless Modular, 32 x 32 8-bit, with the colour encoding `RGB_D65_SRG_Per_g0.45455`: sRGB primaries, gamma 2.2, perceptual intent. | `External/ReferenceOutput/JxlDecoderTests/colour_gamma22.png`, whose `iCCP` chunk is the profile `djxl` builds |
| `colour_grey_linear.jxl` | Lossless Modular, 32 x 32 8-bit, with the colour encoding `Gra_D65_Rel_Lin`: grey, linear. | `External/ReferenceOutput/JxlDecoderTests/colour_grey_linear.png`, whose `iCCP` chunk is the profile `djxl` builds |
| `colour_custom.jxl` | Lossless Modular, 32 x 32 8-bit, with the colour encoding `RGB_0.3457;0.3585_0.7;0.29;0.18;0.72;0.14;0.05_Abs_SRG`: custom white point and primaries, absolute intent. | `External/ReferenceOutput/JxlDecoderTests/colour_custom.png`, whose `iCCP` chunk is the profile `djxl` builds |
| `colour_dci.jxl` | Lossless Modular, 32 x 32 8-bit, with the colour encoding `RGB_DCI_DCI_Rel_DCI`: DCI white point, P3 primaries, DCI curve. | `External/ReferenceOutput/JxlDecoderTests/colour_dci.png`, whose `iCCP` chunk is the profile `djxl` builds |
| `colour_hlg.jxl` | Lossless Modular, 32 x 32 8-bit, with the colour encoding `Rec2100HLG`: HLG: no pixel profile until D11. | `External/ReferenceOutput/JxlDecoderTests/colour_hlg.png`, whose `iCCP` chunk is the profile `djxl` builds |
| `orientation_1.jxl` to `orientation_8.jxl` | Lossless Modular, 37 x 23 8-bit RGBA, with codestream orientations 1 to 8. The files differ only in the three orientation bits; every pixel differs, so a wrong flip or transpose shows. Orientations 5 to 8 decode to 23 x 37. | `External/ReferenceOutput/JxlDecoderTests/orientation_1.png` to `orientation_8.png` |
| `orientation_exif.jxl` | The same image with orientation 6, in a container with an uncompressed `Exif` box whose orientation tag is also 6. | `External/ReferenceOutput/JxlDecoderTests/orientation_exif.png`, whose Exif (a `zTXt` "Raw profile type exif" chunk) has the orientation reset to 1 |
| `two_alpha_a.jxl`, `two_alpha_b.jxl` | Lossless Modular, 16 x 16 8-bit RGB with two alpha extra channels, one colour each: (200, 100, 50) with alphas 128 and 255, and (40, 220, 160) with alphas 64 and 192. Their image headers are equal, so the layer tests stack the frame of one on the other and blend the second alpha channel with the first as its alpha. | `djxl` on each layered codestream, in `JxlLayersTests` |
| `depth.jxl` | Lossless Modular, 16 x 16 8-bit RGB with a depth extra channel. The colour is (200, 100, 50) and the depth 7. `Image<TPixel>` drops the depth. | None: the colour is known |
| `animation_from_apng.jxl` | Lossless Modular, 30 x 20 8-bit RGBA animation made from a four-frame APNG. The APNG's second frame has a delay of 0, so it becomes a frame with no duration that is blended into the next one: the animation shows three frames, of 120, 250 and 80 ticks at 1000 ticks per second. The later frames are cropped, and one blends over the canvas. | `External/ReferenceOutput/JxlDecoderTests/animation_from_apng.npy`: every displayed frame, as float32 (frames, height, width, channels) |
| `vardct_small.jxl` | Lossy VarDCT, 64 x 48 8-bit RGB with no XYB: every block DCT8, no Gaborish, no EPF, one-band parametric quant tables. One group, so the frame is a single section. | `External/ReferenceOutput/JxlDecoderTests/vardct_small.npy`, float32 |
| `vardct_groups.jxl` | The same kind of frame, 280 x 72: two groups, each in its own section, and a custom DCT8 coefficient order. | `External/ReferenceOutput/JxlDecoderTests/vardct_groups.npy` |
| `vardct_alpha.jxl` | The same kind of frame, 72 x 56 8-bit RGBA: the alpha is a Modular extra channel beside the VarDCT colour. | `External/ReferenceOutput/JxlDecoderTests/vardct_alpha.npy` |
| `vardct_grey.jxl` | The same kind of frame, 70 x 50 8-bit grey. A VarDCT frame codes three colour planes even for grey, and the output has one. Neither side is a multiple of 8. | `External/ReferenceOutput/JxlDecoderTests/vardct_grey.npy`, one channel |
| `vardct_grey_alpha.jxl` | The same kind of frame, 70 x 50 8-bit grey with alpha: the alpha is a Modular extra channel after the three VarDCT colour planes. | `External/ReferenceOutput/JxlDecoderTests/vardct_grey_alpha.npy`, two channels |
| `vardct_strategies.jxl` | The image of `vardct_small.jxl` at a slower effort, which also uses 4x8, 8x4 and 32x32 blocks with one-band parametric quant tables at their sizes (D9e). | `External/ReferenceOutput/JxlDecoderTests/vardct_strategies.npy` |
| `vardct_identity_afv.jxl` | `Png/bike-small.png`, 288 x 163 8-bit RGBA, at effort 7 with no XYB: every kind of block up to 32x32, Identity and AFV0 to AFV3 among them (D9f), an adaptive quant field and non-zero chroma-from-luma maps. Two groups. | `External/ReferenceOutput/JxlDecoderTests/vardct_identity_afv.npy` |
| `vardct_xyb.jxl` | Lossy VarDCT of the same image with XYB (`cjxl -e 2`). It decodes since D8d; the `vardct_xyb_*` files below check XYB VarDCT output. | None: it must decode |
| `jpeg_rgb.jxl` | `cjxl`'s lossless transcode of `Jpg/baseline/jpeg-rgb.jpg`, a 200 x 200 baseline JPEG coded in RGB (Adobe transform 0). An RGB JPEG transcodes to a VarDCT frame with no colour transform and no chroma subsampling, so it checks the raw quantization tables (D10d) on their own: the DCT8 table is raw, and the others come from the library. One group; the frame has the skip-adaptive-DC-smoothing flag and no loop filters. | `External/ReferenceOutput/JxlDecoderTests/jpeg_rgb.npy` |
| `twelve_bit.jxl` | Lossless Modular, 45 x 33 12-bit RGBA: ramps and noise, with alpha 0, 4095 and noise in turn. The encoder tests decode it, encode it again and check that it stays 12-bit with the same samples. | `External/ReferenceOutput/JxlDecoderTests/twelve_bit.png` (16-bit) |

#### How `premultiplied_alpha` was made

```sh
py -3 make_premultiplied_alpha.py
cjxl premultiplied_alpha_source.png premultiplied_alpha.jxl -d 0 --premultiply=1
djxl premultiplied_alpha.jxl ../../../External/ReferenceOutput/JxlDecoderTests/premultiplied_alpha.png
```

- `make_premultiplied_alpha.py` writes `premultiplied_alpha_source.png`, whose colours are already multiplied by alpha, and `premultiplied_alpha_straight.png`, the same pixels with straight alpha. The source PNG is not committed; the script makes it again.
- `--premultiply=1` only marks the alpha as premultiplied. It does not change the samples, so the file stores the source colours as they are.
- `djxl` v0.12.0 does not un-premultiply, so its PNG holds the colours as coded. It matches the source PNG pixel for pixel.

#### How the greyscale and squeeze files were made

```sh
py -3 make_greyscale_and_squeeze.py
cjxl greyscale_source.png greyscale.jxl -d 0
cjxl greyscale_alpha_source.png greyscale_alpha.jxl -d 0
cjxl greyscale_source.png greyscale_squeeze.jxl -d 0 --responsive=1
cjxl rgba_source.png rgba_squeeze.jxl -d 0 --responsive=1
cjxl greyscale_wide_source.png greyscale_squeeze_wide.jxl -d 0 --responsive=1
djxl greyscale.jxl ../../../External/ReferenceOutput/JxlDecoderTests/greyscale.png
djxl greyscale_alpha.jxl ../../../External/ReferenceOutput/JxlDecoderTests/greyscale_alpha.png
djxl greyscale_squeeze.jxl ../../../External/ReferenceOutput/JxlDecoderTests/greyscale_squeeze.png
djxl rgba_squeeze.jxl ../../../External/ReferenceOutput/JxlDecoderTests/rgba_squeeze.png
djxl greyscale_squeeze_wide.jxl ../../../External/ReferenceOutput/JxlDecoderTests/greyscale_squeeze_wide.png
```

- `make_greyscale_and_squeeze.py` writes the source PNGs. They are not committed; the script makes them again.
- `--responsive=1` makes lossless `cjxl` use the squeeze transform. Lossless encoding turns the loop filters off, and these files have one pass.
- The files are lossless, so each `djxl` PNG holds the same pixels as its source PNG.

#### How the Exif and XMP files were made

```sh
py -3 make_exif_xmp.py ../libjxl/jxl/jpeg_reconstruction/1x1_exif_xmp.jpg
cjxl exif_xmp_source.png exif_xmp.jxl -d 0 --compress_boxes=0
cjxl exif_xmp_source.png exif_xmp_brob.jxl -d 0 --compress_boxes=1
djxl exif_xmp.jxl ../../../External/ReferenceOutput/JxlDecoderTests/exif_xmp.png
```

- `make_exif_xmp.py` writes `exif_xmp_source.png`, with the JPEG's Exif in an `eXIf` chunk and its XMP in an `iTXt` chunk. It is not committed; the script makes it again. `cjxl` given the JPEG itself keeps the Exif but drops the XMP, so the PNG carries both.
- `djxl` gives the same PNG for `exif_xmp_brob.jxl`.

#### How `custom_icc` was made

```sh
py -3 make_custom_icc.py
cjxl custom_icc_source.png custom_icc.jxl -d 0
djxl custom_icc.jxl ../../../External/ReferenceOutput/JxlDecoderTests/custom_icc.png
```

- `make_custom_icc.py` writes the profile to `custom_icc.icc` and `custom_icc_source.png`, which carries it in an `iCCP` chunk. Neither is committed; the script makes them again.
- `djxl --icc_out` gives back `custom_icc.icc` byte for byte.

#### How the colour-encoding files were made

```sh
py -3 make_colour_encodings.py
cjxl colour_source.ppm colour_srgb.jxl -d 0 -x "color_space=sRGB"
cjxl colour_grey_source.pgm colour_grey_srgb.jxl -d 0 -x "color_space=Gra_D65_Rel_SRG"
cjxl colour_source.ppm colour_rec2020.jxl -d 0 -x "color_space=RGB_D65_202_Rel_709"
cjxl colour_source.ppm colour_display_p3.jxl -d 0 -x "color_space=DisplayP3"
cjxl colour_source.ppm colour_linear_srgb.jxl -d 0 -x "color_space=RGB_D65_SRG_Rel_Lin"
cjxl colour_source.ppm colour_gamma22.jxl -d 0 -x "color_space=RGB_D65_SRG_Per_g0.45455"
cjxl colour_grey_source.pgm colour_grey_linear.jxl -d 0 -x "color_space=Gra_D65_Rel_Lin"
cjxl colour_source.ppm colour_custom.jxl -d 0 -x "color_space=RGB_0.3457;0.3585_0.7;0.29;0.18;0.72;0.14;0.05_Abs_SRG"
cjxl colour_source.ppm colour_dci.jxl -d 0 -x "color_space=RGB_DCI_DCI_Rel_DCI"
cjxl colour_source.ppm colour_hlg.jxl -d 0 -x "color_space=Rec2100HLG"
djxl colour_srgb.jxl ../../../External/ReferenceOutput/JxlDecoderTests/colour_srgb.png
djxl colour_grey_srgb.jxl ../../../External/ReferenceOutput/JxlDecoderTests/colour_grey_srgb.png
djxl colour_rec2020.jxl ../../../External/ReferenceOutput/JxlDecoderTests/colour_rec2020.png
djxl colour_display_p3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/colour_display_p3.png
djxl colour_linear_srgb.jxl ../../../External/ReferenceOutput/JxlDecoderTests/colour_linear_srgb.png
djxl colour_gamma22.jxl ../../../External/ReferenceOutput/JxlDecoderTests/colour_gamma22.png
djxl colour_grey_linear.jxl ../../../External/ReferenceOutput/JxlDecoderTests/colour_grey_linear.png
djxl colour_custom.jxl ../../../External/ReferenceOutput/JxlDecoderTests/colour_custom.png
djxl colour_dci.jxl ../../../External/ReferenceOutput/JxlDecoderTests/colour_dci.png
djxl colour_hlg.jxl ../../../External/ReferenceOutput/JxlDecoderTests/colour_hlg.png
```

- `make_colour_encodings.py` writes `colour_source.ppm` and `colour_grey_source.pgm`. PPM and PGM carry no colour information, so `cjxl` takes the colour encoding from `-x color_space`. They are not committed; the script makes them again.
- Each file stores its colour encoding as enumerated fields, not as an ICC profile.
- `djxl` writes the profile it builds from the colour encoding into the PNG's `iCCP` chunk; it equals `djxl --icc_out`. For `colour_srgb` and `colour_grey_srgb` it writes an `sRGB` chunk instead.

#### How the orientation files were made

```sh
py -3 make_orientation.py
cjxl orientation_source.png orientation_base.jxl -d 0 --container=0
cjxl orientation_source.png orientation_exif.jxl -d 0 --compress_boxes=0
py -3 make_orientation.py orientation_base.jxl
djxl orientation_1.jxl ../../../External/ReferenceOutput/JxlDecoderTests/orientation_1.png
djxl orientation_2.jxl ../../../External/ReferenceOutput/JxlDecoderTests/orientation_2.png
djxl orientation_3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/orientation_3.png
djxl orientation_4.jxl ../../../External/ReferenceOutput/JxlDecoderTests/orientation_4.png
djxl orientation_5.jxl ../../../External/ReferenceOutput/JxlDecoderTests/orientation_5.png
djxl orientation_6.jxl ../../../External/ReferenceOutput/JxlDecoderTests/orientation_6.png
djxl orientation_7.jxl ../../../External/ReferenceOutput/JxlDecoderTests/orientation_7.png
djxl orientation_8.jxl ../../../External/ReferenceOutput/JxlDecoderTests/orientation_8.png
djxl orientation_exif.jxl ../../../External/ReferenceOutput/JxlDecoderTests/orientation_exif.png
```

- With no argument, `make_orientation.py` writes `orientation_source.png`, whose `eXIf` chunk sets the Exif orientation to 6. `cjxl` copies that orientation into the codestream.
- `--container=0` drops the Exif box but keeps the codestream orientation, so `orientation_base.jxl` is a bare codestream with orientation 6. Its orientation field is present, because the orientation is not 1.
- Given that file, `make_orientation.py` rewrites its three orientation bits to each orientation and writes `orientation_1.jxl` to `orientation_8.jxl`. `orientation_6.jxl` equals `orientation_base.jxl`. Neither the source PNG nor the base file is committed; the commands make them again.
- `djxl` orients its output by default, and when it writes the Exif it resets the orientation tag to 1 (`ResetExifOrientation`).

#### How `animation_from_apng` was made

```sh
py -3 make_animation.py
cjxl animation_source.png animation_from_apng.jxl -d 0
djxl animation_from_apng.jxl ../../../External/ReferenceOutput/JxlDecoderTests/animation_from_apng.npy
```

- `make_animation.py` writes `animation_source.png`, an APNG with four frames and no disposal. It is not committed; the script makes it again.
- `cjxl` keeps every APNG frame as a JPEG XL frame, with the APNG delay in milliseconds as its duration (1000 ticks per second). `jxlinfo -v` lists the zero-delay frame as a "Layer" with a duration of 0.
- Given a `.png` output, `djxl` writes an APNG, and this repository's PNG decoder reads only its first frame. Given `.npy`, it writes every displayed frame, coalesced onto the full canvas: three frames, one fewer than the APNG.

#### How the extra-channel files were made

```sh
py -3 make_extra_channels.py
cjxl two_alpha_a_source.pam two_alpha_a.jxl -d 0
cjxl two_alpha_b_source.pam two_alpha_b.jxl -d 0
cjxl depth_source.pam depth.jxl -d 0
```

- `make_extra_channels.py` writes the source PAMs. They are not committed; the script makes them again.
- In a PAM header, `cjxl` reads each `TUPLTYPE` line after the first as one more extra channel: `TUPLTYPE RGB_ALPHA` then `TUPLTYPE Alpha` gives two alpha channels, and `TUPLTYPE RGB` then `TUPLTYPE Depth` a depth channel.
- `jxlinfo -v` lists the channels as "RGB+Alpha+Alpha" and "RGB+Depth".

#### How the lossy Modular (XYB) files were made

| File | Why it is here | Reference |
|---|---|---|
| `xyb_modular_rgb.jxl` | Lossy Modular (`-m 1 -d 1`), 283 x 67 8-bit RGB, sRGB: XYB, no loop filters. Wider than one group. | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_rgb.npy` |
| `xyb_modular_grey.jxl` | The same in grey. The XYB stage gives three equal planes, and the output has one. | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_grey.npy`, one channel |
| `xyb_modular_icc.jxl` | Lossy Modular, 32 x 32 8-bit RGB with the embedded ICC profile of `custom_icc.jxl`. The XYB stage cannot output to an ICC profile, so the output is linear sRGB (ADR 0002). | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_icc.npy`, linear sRGB |
| `xyb_modular_display_p3.jxl` | Lossy Modular, 32 x 32 8-bit RGB, `DisplayP3`: the inverse opsin matrix is adapted to the P3 primaries. | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_display_p3.npy` |
| `xyb_modular_intensity_target.jxl` | Lossy Modular, 32 x 32 8-bit RGB, sRGB, with an intensity target of 1000 nits, which scales the inverse opsin matrix. | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_intensity_target.npy` |
| `xyb_modular_gamma22.jxl` | Lossy Modular, 32 x 32 8-bit RGB, `RGB_D65_SRG_Per_g0.45455`: a gamma curve, which XYB output does not support until D11. | None: decoding throws `NotSupportedException` |
| `xyb_modular_icc_animation.jxl` | Lossy Modular, 24 x 16 8-bit RGBA animation with the ICC profile of `custom_icc.jxl`. Its second frame is blended, and libjxl does not blend in the linear sRGB fallback. | None: `djxl` fails on it, and decoding throws `InvalidImageContentException` |

```sh
py -3 make_custom_icc.py
py -3 make_colour_encodings.py
py -3 make_xyb_modular.py custom_icc.icc
cjxl xyb_modular_source.ppm xyb_modular_rgb.jxl -m 1 -d 1
cjxl xyb_modular_grey_source.pgm xyb_modular_grey.jxl -m 1 -d 1
cjxl custom_icc_source.png xyb_modular_icc.jxl -m 1 -d 1
cjxl colour_source.ppm xyb_modular_display_p3.jxl -m 1 -d 1 -x "color_space=DisplayP3"
cjxl colour_source.ppm xyb_modular_intensity_target.jxl -m 1 -d 1 --intensity_target=1000
cjxl colour_source.ppm xyb_modular_gamma22.jxl -m 1 -d 1 -x "color_space=RGB_D65_SRG_Per_g0.45455"
cjxl xyb_modular_icc_animation_source.png xyb_modular_icc_animation.jxl -m 1 -d 1
djxl xyb_modular_rgb.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_rgb.npy
djxl xyb_modular_grey.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_grey.npy
djxl xyb_modular_icc.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_icc.npy
djxl xyb_modular_display_p3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_display_p3.npy
djxl xyb_modular_intensity_target.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_intensity_target.npy
```

- `make_xyb_modular.py` writes `xyb_modular_source.ppm` and `xyb_modular_grey_source.pgm`, and, given the profile that `make_custom_icc.py` writes, the APNG `xyb_modular_icc_animation_source.png`. `make_colour_encodings.py` writes `colour_source.ppm`. None of the sources is committed; the scripts make them again.
- Lossy `cjxl` encodes XYB, so the files are XYB with the colour encoding of the source as their original encoding. `-m 1` makes Modular frames rather than VarDCT, and at `-d 1` they have no Gaborish or EPF.
- `djxl` writes float32 `.npy` of (frames, height, width, channels), not clamped: some samples are below 0. The decoder cannot match it bit for bit, because libjxl uses FMA (ADR 0004), so the tests compare at the lossy thresholds (docs/jxl/spec.md, "Tolerance for lossy output").
- `djxl` gives linear sRGB for `xyb_modular_icc.jxl`, as with `--color_space=RGB_D65_SRG_Rel_Lin`.

#### How the VarDCT files were made

```sh
py -3 make_vardct.py
benchmark_xl --input=vardct_small.png --codec=jxl:thunder:d1:gab0:epf0:noperc --save_compressed --output_dir=out
benchmark_xl --input=vardct_groups.png --codec=jxl:thunder:d1:gab0:epf0:noperc --save_compressed --output_dir=out
benchmark_xl --input=vardct_alpha.png --codec=jxl:thunder:d1:gab0:epf0:noperc --save_compressed --output_dir=out
benchmark_xl --input=vardct_grey.png --codec=jxl:thunder:d1:gab0:epf0:noperc --save_compressed --output_dir=out
benchmark_xl --input=vardct_grey_alpha.png --codec=jxl:thunder:d1:gab0:epf0:noperc --save_compressed --output_dir=out
benchmark_xl --input=vardct_small.png --codec=jxl:wombat:d1:gab0:epf0:noperc --save_compressed --output_dir=out
cp ../../Png/bike-small.png .
benchmark_xl --input=bike-small.png --codec=jxl:squirrel:d1:gab0:epf0:noperc --save_compressed --output_dir=out --print_more_stats
mv out/vardct_small.png.noperc.epf0.gab0.d1.thunder.jxl vardct_small.jxl
mv out/vardct_groups.png.noperc.epf0.gab0.d1.thunder.jxl vardct_groups.jxl
mv out/vardct_alpha.png.noperc.epf0.gab0.d1.thunder.jxl vardct_alpha.jxl
mv out/vardct_grey.png.noperc.epf0.gab0.d1.thunder.jxl vardct_grey.jxl
mv out/vardct_grey_alpha.png.noperc.epf0.gab0.d1.thunder.jxl vardct_grey_alpha.jxl
mv out/vardct_small.png.noperc.epf0.gab0.d1.wombat.jxl vardct_strategies.jxl
mv out/bike-small.png.noperc.epf0.gab0.d1.squirrel.jxl vardct_identity_afv.jxl
cjxl vardct_small.png vardct_xyb.jxl -e 2 --gaborish=0 --epf=0
djxl vardct_small.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_small.npy
djxl vardct_groups.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_groups.npy
djxl vardct_alpha.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_alpha.npy
djxl vardct_grey.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_grey.npy
djxl vardct_grey_alpha.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_grey_alpha.npy
djxl vardct_strategies.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_strategies.npy
djxl vardct_identity_afv.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_identity_afv.npy
```

- `make_vardct.py` writes the source PNGs. They are not committed; the script makes them again. `bike-small.png` is copied from `Png/` because `benchmark_xl` names its output after the input path; delete the copy afterwards.
- `benchmark_xl` is from the same v0.12.0 release as `cjxl`. `noperc` (`disable_perceptual_optimizations`) turns XYB off and codes every quant table as parametric DCT with one distance band, which needs no `FastPowf`. `thunder` is effort 2, where every block is DCT8 and the quant field is constant. `gab0` and `epf0` turn the loop filters off.
- `vardct_strategies.jxl` has 9 DCT8, 5 DCT4X8, 2 DCT8X4 and 2 DCT32X32 blocks, counted from its AC metadata. `benchmark_xl --print_more_stats` prints 7 "4x8" blocks and 2 "16x32" blocks: v0.12.0 prints each count from "8x32" to "32x32" against the name of the next smaller size (see the XYB VarDCT notes below).
- `vardct_identity_afv.jxl` (squirrel is effort 7) has 389 DCT8, 19 Identity, 37 DCT2X2, 13 DCT4X8, 6 DCT8X4, 5 AFV0, 4 AFV1, 6 AFV2, 3 AFV3, 11 DCT16X8, 38 DCT8X16, 16 DCT16X16, 2 DCT32X16, 10 DCT16X32 and 1 DCT32X32 blocks, counted from its AC metadata. `benchmark_xl --print_more_stats` prints 18 AFV blocks and 56 "small" blocks (Identity, DCT2X2 and DCT4X4 together). Read from the decoder's state after its DC groups (the AC metadata and the chroma-from-luma maps), its quant field runs from 4 to 10 over the first blocks of its varblocks, and 9 of the 15 YtoX entries and 2 of the 15 YtoB entries of its chroma-from-luma maps are not 0.
- The VarDCT output cannot match `djxl` bit for bit (`docs/jxl/spec.md`, "Tolerance for lossy output"). Measured on x64 against `djxl` with AVX2, the worst channel RMSE is 8.1e-8 (`vardct_small`), 8.3e-8 (`vardct_groups`), 8.1e-8 (`vardct_alpha`) 6.4e-8 (`vardct_grey`, `vardct_grey_alpha`), 2.7e-7 (`vardct_strategies`) and 1.0e-7 (`vardct_identity_afv`), and the peak error 3.6e-7, 4.2e-7, 4.2e-7, 3.0e-7, 1.7e-6 and 1.4e-6.

#### How the XYB VarDCT files were made

| File | Why it is here | Reference |
|---|---|---|
| `vardct_xyb_64.jxl` | Lossy VarDCT with XYB (`cjxl -e 2 --gaborish=0 --epf=0 -d 1`), 64 x 64 8-bit RGB, sRGB: every block DCT8, the default quant tables, no loop filters. One group, so the frame is a single section. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_64.npy` |
| `vardct_xyb_300x200_d05.jxl` | The same kind of frame at `-d 0.5`, 300 x 200: two groups and a custom DCT8 coefficient order. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_300x200_d05.npy` |
| `vardct_xyb_600x400_d3.jxl` | The same kind of frame at `-d 3`, 600 x 400: three by two groups, a custom DCT8 coefficient order, and an X quant-matrix scale of 4. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_600x400_d3.npy` |
| `vardct_xyb_grey.jxl` | The same kind of frame, 300 x 200 8-bit grey. The XYB stage gives three equal planes, and the output has one. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_grey.npy`, one channel |
| `vardct_xyb_alpha.jxl` | The same kind of frame, 72 x 56 8-bit RGBA (the image of `vardct_alpha.jxl`): the alpha is a Modular extra channel. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_alpha.npy` |
| `vardct_xyb_icc.jxl` | The same kind of frame, 32 x 32 8-bit RGB with the embedded ICC profile of `custom_icc.jxl`. The output is linear sRGB (ADR 0002). | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_icc.npy`, linear sRGB |
| `vardct_xyb_grey_d50.jxl` | The same kind of frame, 70 x 50 8-bit grey (the image of `vardct_grey.jxl`) with a D50 white point, which the XYB stage cannot output for grey: the output is linear sRGB, and `Image<TPixel>` gets sRGB with no pixel profile (ADR 0002). | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_grey_d50.npy`, one channel, linear |
| `vardct_xyb_e7.jxl` | The 64 x 64 image at `cjxl -d 1` and the default effort (7): Gaborish, EPF with one iteration, other block sizes, and EPF sharpness 0 and 7. It needs Gaborish (D9b), EPF (D9c) and the other block sizes (D9e), so it decodes since all three are in. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_e7.npy` |
| `vardct_xyb_epf.jxl` | The 64 x 64 image at `-e 2` with EPF only (D9). `vardct_xyb_gaborish.jxl`, with Gaborish only, is in the Gaborish files below, and the other EPF files in the EPF files. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf.npy` |
| `vardct_xyb_strategies.jxl` | The 64 x 64 image at `-e 7` with no loop filters, which uses blocks other than DCT8 (D9e): DCT2X2, DCT4X8, DCT16X8, DCT8X16, DCT16X16, DCT32X16, DCT16X32 and DCT32X32. One group. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_strategies.npy` |
| `vardct_xyb_sizes_noisy.jxl` | Lossy VarDCT with XYB at `cjxl -e 7 --gaborish=0 --epf=0 -d 0.15`, 320 x 288 8-bit RGB: smooth colour with soft noise, so mostly small blocks. Two by two groups, and custom coefficient orders for DCT8, the 2x2 / 4x8 bucket and DCT32X32. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_sizes_noisy.npy` |
| `vardct_xyb_sizes_fine.jxl` | The same kind of frame at `-d 1`, with a fine, fading texture: blocks from 8x8 to 64x32, and custom coefficient orders for DCT8, DCT32X32, DCT16X8 and DCT32X16. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_sizes_fine.npy` |
| `vardct_xyb_sizes_smooth.jxl` | The same kind of frame at `-d 1`, with smooth colour only: 64x64 and 32x64 blocks in their natural coefficient order. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_sizes_smooth.npy` |
| `vardct_xyb_identity_afv.jxl` | `Png/bike-small.png`, 288 x 163 8-bit RGBA, at `-e 7` with no loop filters, which has Identity and AFV0 to AFV3 blocks (D9f), an adaptive quant field and a non-zero chroma-from-luma map. Two groups. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_identity_afv.npy` |
| `vardct_xyb_passes.jxl` | The 64 x 64 image at `-e 2 --progressive_ac`: three passes (D13). | None: decoding throws `NotSupportedException` |
| `vardct_xyb_dc_frame.jxl` | The 64 x 64 image at `-e 2 --progressive_dc=1`: a Modular DC frame comes first (D13). | None: decoding throws `NotSupportedException` |

```sh
py -3 make_vardct_xyb.py
py -3 make_vardct.py
py -3 make_custom_icc.py
cjxl vardct_xyb_64_source.ppm vardct_xyb_64.jxl -e 2 --gaborish=0 --epf=0 -d 1
cjxl vardct_xyb_300x200_source.ppm vardct_xyb_300x200_d05.jxl -e 2 --gaborish=0 --epf=0 -d 0.5
cjxl vardct_xyb_600x400_source.ppm vardct_xyb_600x400_d3.jxl -e 2 --gaborish=0 --epf=0 -d 3
cjxl vardct_xyb_grey_source.pgm vardct_xyb_grey.jxl -e 2 --gaborish=0 --epf=0 -d 1
cjxl vardct_alpha.png vardct_xyb_alpha.jxl -e 2 --gaborish=0 --epf=0 -d 1
cjxl custom_icc_source.png vardct_xyb_icc.jxl -e 2 --gaborish=0 --epf=0 -d 1
cjxl vardct_grey.png vardct_xyb_grey_d50.jxl -e 2 --gaborish=0 --epf=0 -d 1 -x color_space=Gra_D50_Rel_SRG
cjxl vardct_xyb_64_source.ppm vardct_xyb_e7.jxl -d 1
cjxl vardct_xyb_64_source.ppm vardct_xyb_gaborish.jxl -e 2 --gaborish=1 --epf=0 -d 1
cjxl vardct_xyb_64_source.ppm vardct_xyb_epf.jxl -e 2 --gaborish=0 --epf=1 -d 1
cjxl vardct_xyb_64_source.ppm vardct_xyb_strategies.jxl -e 7 --gaborish=0 --epf=0 -d 1
cjxl vardct_xyb_64_source.ppm vardct_xyb_passes.jxl -e 2 --gaborish=0 --epf=0 -d 1 --progressive_ac
cjxl vardct_xyb_64_source.ppm vardct_xyb_dc_frame.jxl -e 2 --gaborish=0 --epf=0 -d 1 --progressive_dc=1
py -3 make_vardct_sizes.py
cjxl vardct_xyb_sizes_noisy_source.ppm vardct_xyb_sizes_noisy.jxl -e 7 --gaborish=0 --epf=0 -d 0.15
cjxl vardct_xyb_sizes_fine_source.ppm vardct_xyb_sizes_fine.jxl -e 7 --gaborish=0 --epf=0 -d 1
cjxl vardct_xyb_sizes_smooth_source.ppm vardct_xyb_sizes_smooth.jxl -e 7 --gaborish=0 --epf=0 -d 1
cjxl ../../Png/bike-small.png vardct_xyb_identity_afv.jxl -e 7 --gaborish=0 --epf=0 -d 1
benchmark_xl --input=vardct_xyb_sizes_noisy_source.ppm --codec=jxl:squirrel:d0.15:gab0:epf0 --print_more_stats
benchmark_xl --input=vardct_xyb_sizes_fine_source.ppm --codec=jxl:squirrel:d1:gab0:epf0 --print_more_stats
benchmark_xl --input=vardct_xyb_sizes_smooth_source.ppm --codec=jxl:squirrel:d1:gab0:epf0 --print_more_stats
benchmark_xl --input=../../Png/bike-small.png --codec=jxl:squirrel:d1:gab0:epf0 --print_more_stats
djxl vardct_xyb_64.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_64.npy
djxl vardct_xyb_300x200_d05.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_300x200_d05.npy
djxl vardct_xyb_600x400_d3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_600x400_d3.npy
djxl vardct_xyb_grey.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_grey.npy
djxl vardct_xyb_alpha.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_alpha.npy
djxl vardct_xyb_icc.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_icc.npy
djxl vardct_xyb_grey_d50.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_grey_d50.npy
djxl vardct_xyb_strategies.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_strategies.npy
djxl vardct_xyb_sizes_noisy.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_sizes_noisy.npy
djxl vardct_xyb_sizes_fine.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_sizes_fine.npy
djxl vardct_xyb_sizes_smooth.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_sizes_smooth.npy
djxl vardct_xyb_identity_afv.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_identity_afv.npy
djxl vardct_xyb_e7.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_e7.npy
```

- `make_vardct_xyb.py` writes the PPM and PGM sources, `make_vardct.py` writes `vardct_alpha.png` and `vardct_grey.png`, `make_custom_icc.py` writes `custom_icc_source.png`, and `make_vardct_sizes.py` writes the three `vardct_xyb_sizes_*_source.ppm`. None of the sources is committed; the scripts make them again.
- The `vardct_xyb_sizes_*` files are the same bytes that `benchmark_xl --codec=jxl:squirrel:dD:gab0:epf0 --save_compressed` writes (squirrel is effort 7). Their blocks, counted from their AC metadata:
  - `vardct_xyb_sizes_noisy.jxl`: 528 DCT8, 114 DCT2X2, 17 DCT4X8, 161 DCT8X4, 1 DCT16X16, 1 DCT16X32 and 38 DCT32X32.
  - `vardct_xyb_sizes_fine.jxl`: 70 DCT8, 11 DCT16X8, 2 DCT8X16, 12 DCT16X16, 27 DCT32X16, 15 DCT16X32, 54 DCT32X32, 2 DCT64X32 and 1 DCT32X64.
  - `vardct_xyb_sizes_smooth.jxl`: 5 DCT32X64 and 20 DCT64X64.
  - `vardct_xyb_strategies.jxl`: 8 DCT8, 2 DCT2X2, 2 DCT4X8, 1 DCT16X8, 3 DCT8X16, 1 DCT16X16, 1 DCT32X16, 2 DCT16X32 and 1 DCT32X32.
  - `vardct_xyb_identity_afv.jxl`: 315 DCT8, 23 Identity, 133 DCT2X2, 40 DCT4X8, 23 DCT8X4, 11 AFV0, 14 AFV1, 10 AFV2, 11 AFV3, 3 DCT16X8, 37 DCT8X16, 10 DCT16X16, 1 DCT32X16, 4 DCT16X32 and 1 DCT32X32.
- The quant fields and the chroma-from-luma maps, read from the decoder's state after the DC groups: `vardct_xyb_strategies.jxl`'s quant field runs from 6 to 8 over the first blocks of its varblocks, but its one chroma-from-luma tile is 0. `vardct_xyb_identity_afv.jxl`'s runs from 5 to 18, and 1 of the 15 YtoX entries of its map is not 0 (its YtoB entries are 0). The non-XYB `vardct_identity_afv.jxl` has non-zero YtoX and YtoB entries too (see the VarDCT notes above). `DecodeDcGroups_QuantFieldAndChromaFromLuma` in `JxlXybVarDctTests` and `DecodeDcGroups_IdentityAfv_HasAnAdaptiveQuantFieldAndNonZeroChromaFromLuma` in `JxlVarDctTests` check these values.
- `benchmark_xl --print_more_stats` (v0.12.0) shows no AFV blocks for the `vardct_xyb_sizes_*` sources and 46 for `bike-small.png`. Its "small blocks" count adds Identity, DCT2X2 and DCT4X4, so the 114 small blocks of the noisy source were checked to be DCT2X2 by decoding them. Read its other counts with care: v0.12.0's `JxlEncoderStatsGet` (`lib/jxl/encode.cc`) returns each count from "8x32" to "32x32" for the next larger size, so its "8x32" line counts DCT16X16 blocks, "16x16" counts DCT16X32 and DCT32X16, "16x32" counts DCT32X32 and "32x32" counts DCT32X64 and DCT64X32. It has no line for DCT8X16, DCT8X32 or the sizes above 64x64; `cjxl -e 7` writes none above 64x64.
- At `-e 2`, every block is DCT8, the quant field is constant, the chroma-from-luma maps are 0, and the quant tables are the library defaults, whose DCT8 table has six distance bands and so needs `FastPowf`. Gaborish is off below effort 5; `--epf=0` is needed, because at `-d 1` cjxl turns EPF on.
- The chroma-from-luma maps are 0, but an XYB frame still adds Y to B: the default B ratio is 1.
- `djxl` writes float32 `.npy`, not clamped. The decoder cannot match it bit for bit (`docs/jxl/spec.md`, "Tolerance for lossy output"): libjxl fuses multiplies and adds on AVX2 and approximates the reciprocal in the quantisation bias, and this port does neither (ADR 0004). Measured on x64 against `djxl` with AVX2, the worst channel RMSE and the peak error are 1.1e-6 and 1.2e-5 (`vardct_xyb_64`), 1.1e-6 and 2.6e-5 (`vardct_xyb_300x200_d05`), 1.3e-6 and 7.8e-5 (`vardct_xyb_600x400_d3`), 2.6e-7 and 1.6e-6 (`vardct_xyb_grey`), 3.0e-6 and 4.1e-5 (`vardct_xyb_alpha`), 1.4e-6 and 7.7e-6 (`vardct_xyb_icc`), 1.1e-7 and 7.8e-7 (`vardct_xyb_grey_d50`), 9.8e-7 and 1.3e-5 (`vardct_xyb_strategies`), 6.7e-7 and 2.5e-5 (`vardct_xyb_sizes_noisy`), 7.2e-7 and 3.0e-5 (`vardct_xyb_sizes_fine`), 6.1e-7 and 1.0e-5 (`vardct_xyb_sizes_smooth`), and 4.1e-7 and 2.8e-5 (`vardct_xyb_identity_afv`). `Image.Load<Rgb24>` of each is within 0.50 of one 8-bit step of the clamped reference, which is the rounding.
- `djxl` gives linear sRGB for `vardct_xyb_icc.jxl`, as with `--color_space=RGB_D65_SRG_Rel_Lin`.
- `djxl` gives linear grey with sRGB's D65 white point for `vardct_xyb_grey_d50.jxl`: libjxl's `CanOutputToColorEncoding` rejects grey with any other white point.

#### How the Gaborish files were made

| File | Why it is here | Reference |
|---|---|---|
| `vardct_xyb_gaborish.jxl` | The 64 x 64 image of `vardct_xyb_64.jxl` at `cjxl -e 2 --gaborish=1 --epf=0 -d 1`: Gaborish with the default weights and no EPF. One group. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gaborish.npy` |
| `vardct_xyb_gaborish_600x400.jxl` | The same at 600 x 400: three by two groups, so the filter reads across group edges. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gaborish_600x400.npy` |
| `vardct_xyb_gaborish_grey.jxl` | The same, 300 x 200 8-bit grey: two groups. The filter runs on the three planes before the output drops to one. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gaborish_grey.npy`, one channel |
| `vardct_xyb_gaborish_custom.jxl` | `vardct_xyb_gaborish.jxl` with custom Gaborish weights in its frame header (x 0.2 and 0.05, y 0.08 and 0.03, b 0.15 and -0.02). `cjxl` never writes custom weights, so `set_gaborish_weights.py` sets them; the coefficients are unchanged. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gaborish_custom.npy` |
| `vardct_xyb_gaborish_zero_kernel.jxl` | `vardct_xyb_gaborish.jxl` with x weights of -0.125 and -0.125, whose kernel sums to 0 before it is scaled. | None: `djxl` fails on it, and decoding throws `InvalidImageContentException` |
| `xyb_modular_gaborish.jxl` | The 64 x 64 image as lossy Modular (`-m 1 -d 1 --gaborish=1 --epf=0`): Gaborish on a Modular frame. It decodes since D9d. | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_gaborish.npy` |

```sh
py -3 make_vardct_xyb.py
cjxl vardct_xyb_64_source.ppm vardct_xyb_gaborish.jxl -e 2 --gaborish=1 --epf=0 -d 1
cjxl vardct_xyb_600x400_source.ppm vardct_xyb_gaborish_600x400.jxl -e 2 --gaborish=1 --epf=0 -d 1
cjxl vardct_xyb_grey_source.pgm vardct_xyb_gaborish_grey.jxl -e 2 --gaborish=1 --epf=0 -d 1
py -3 set_gaborish_weights.py vardct_xyb_gaborish.jxl vardct_xyb_gaborish_custom.jxl 0.2 0.05 0.08 0.03 0.15 -0.02
py -3 set_gaborish_weights.py vardct_xyb_gaborish.jxl vardct_xyb_gaborish_zero_kernel.jxl -0.125 -0.125 0.1 0.05 0.1 0.05
cjxl vardct_xyb_64_source.ppm xyb_modular_gaborish.jxl -m 1 -d 1 --gaborish=1 --epf=0
djxl vardct_xyb_gaborish.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gaborish.npy
djxl vardct_xyb_gaborish_600x400.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gaborish_600x400.npy
djxl vardct_xyb_gaborish_grey.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gaborish_grey.npy
djxl vardct_xyb_gaborish_custom.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gaborish_custom.npy
djxl xyb_modular_gaborish.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_gaborish.npy
```

- `set_gaborish_weights.py` rewrites only the loop-filter bits of the frame header: it sets `gab_custom` and inserts the six weights as float16. The TOC and the sections are copied as they are. It reads the headers with `lfhdr.py` and writes the bits with `patch_lf.py` (below), and handles only a frame with Gaborish on, its default weights and EPF off, in a bare codestream with no ICC profile.
- The custom weights move `djxl`'s output away from that of the default weights by an RMSE of 0.039, so a decoder that ignored them would fail the test.
- Measured on x64 against `djxl` with AVX2, the worst channel RMSE and the peak error are 1.0e-6 and 1.7e-5 (`vardct_xyb_gaborish`), 1.1e-6 and 3.8e-5 (`vardct_xyb_gaborish_600x400`), 1.9e-7 and 9.5e-7 (`vardct_xyb_gaborish_grey`), and 1.1e-6 and 2.0e-5 (`vardct_xyb_gaborish_custom`). libjxl fuses the filter's multiplies and adds on AVX2, and this port does not (ADR 0004).

#### How the EPF files were made

| File | Why it is here | Reference |
|---|---|---|
| `vardct_xyb_epf.jxl` (above), `vardct_xyb_epf2.jxl`, `vardct_xyb_epf3.jxl` | The 64 x 64 image at `cjxl -e 2 --gaborish=0 -d 1` with `--epf=1`, `2` and `3` (D9c). At `-d 1` the image is barely filtered: with EPF off, `vardct_xyb_epf` changes by at most 2.9e-6, inside the tolerance, so it cannot show a missing EPF. Pass 2 changes the other two by 2.1e-2. | `External/ReferenceOutput/JxlDecoderTests/<name>.npy` |
| `vardct_xyb_epf1_d3.jxl`, `vardct_xyb_epf2_d3.jxl`, `vardct_xyb_epf3_d3.jxl` | The same at `-d 3`, where every pass changes the output well above the tolerance. | the same |
| `vardct_xyb_epf3_300x200_d3.jxl`, `vardct_xyb_epf3_600x400_d3.jxl` | Three iterations at `-d 3` on the 300 x 200 image (2 groups, a width that is not a multiple of 8) and the 600 x 400 image (3 x 2 groups). Every block is DCT8. | the same |
| `vardct_xyb_epf3_13x37_d3.jxl` | Three iterations at `-d 3` on a 13 x 37 image: neither side is a multiple of 8, and the frame is less than two blocks wide. | the same |
| `vardct_xyb_epf3_custom.jxl` | `vardct_xyb_epf3.jxl` with custom EPF fields patched into the frame header: sharpness LUT `[0, 0.1, 0.3, 0.5, 0.7, 0.8, 0.9, 1.2]`, channel scales `(30, 6, 4)`, zero-flush `(0.4, 0.5)`, quant_mul 0.6, pass 0 scale 1.1, pass 2 scale 5.0, border SAD multiplier 0.5. | the same |
| `vardct_xyb_epf_sharp0.jxl`, `vardct_xyb_epf_quant_mul0.jxl` | `vardct_xyb_epf.jxl` with an all-zero sharpness LUT, and with quant_mul 0. Every sigma is then clamped to -1e-4, so every block passes through unfiltered: the output equals the frame without EPF. | the same |
| `xyb_modular_epf2.jxl` | Lossy Modular with XYB, 300 x 200, `cjxl -m 1 -d 1 --gaborish=0 --epf=2`: EPF on a Modular frame, whose sigma is `epf_sigma_for_modular` (1.0 here). It decodes since D9d. | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_epf2.npy` |
| `xyb_modular_epf_sigma0.jxl`, `xyb_modular_epf_sigma_neg.jxl` | `xyb_modular_epf2.jxl` with `epf_sigma_for_modular` patched to 0 and to -1, below libjxl's bound of 1e-8. `djxl` rejects both. | None: decoding throws `InvalidImageContentException` |
| `xyb_modular_epf_sigma_tiny.jxl` | The same with `epf_sigma_for_modular` 5.96e-8, the smallest F16 subnormal, which is above the bound. `djxl` decodes it; the inverse sigma is below `kMinSigma`, so every pixel passes through. | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_epf_sigma_tiny.npy` |

```sh
py -3 make_vardct_xyb.py
cjxl vardct_xyb_64_source.ppm vardct_xyb_epf2.jxl -e 2 --gaborish=0 --epf=2 -d 1
cjxl vardct_xyb_64_source.ppm vardct_xyb_epf3.jxl -e 2 --gaborish=0 --epf=3 -d 1
cjxl vardct_xyb_64_source.ppm vardct_xyb_epf1_d3.jxl -e 2 --gaborish=0 --epf=1 -d 3
cjxl vardct_xyb_64_source.ppm vardct_xyb_epf2_d3.jxl -e 2 --gaborish=0 --epf=2 -d 3
cjxl vardct_xyb_64_source.ppm vardct_xyb_epf3_d3.jxl -e 2 --gaborish=0 --epf=3 -d 3
cjxl vardct_xyb_300x200_source.ppm vardct_xyb_epf3_300x200_d3.jxl -e 2 --gaborish=0 --epf=3 -d 3
cjxl vardct_xyb_600x400_source.ppm vardct_xyb_epf3_600x400_d3.jxl -e 2 --gaborish=0 --epf=3 -d 3
cjxl vardct_xyb_13x37_source.ppm vardct_xyb_epf3_13x37_d3.jxl -e 2 --gaborish=0 --epf=3 -d 3
cjxl vardct_xyb_300x200_source.ppm xyb_modular_epf2.jxl -m 1 -d 1 --gaborish=0 --epf=2
py -3 patch_lf.py vardct_xyb_epf3.jxl vardct_xyb_epf3_custom.jxl "{'epf': 3, 'sharp': [0, 0.1, 0.3, 0.5, 0.7, 0.8, 0.9, 1.2], 'weight': [30, 6, 4, 0.4, 0.5], 'sigma': [0.6, 1.1, 5.0, 0.5]}"
py -3 patch_lf.py vardct_xyb_epf.jxl vardct_xyb_epf_sharp0.jxl "{'epf': 1, 'sharp': [0, 0, 0, 0, 0, 0, 0, 0]}"
py -3 patch_lf.py vardct_xyb_epf.jxl vardct_xyb_epf_quant_mul0.jxl "{'epf': 1, 'sigma': [0.0, 0.9, 6.5, 0.6666666666666666]}"
py -3 patch_lf.py xyb_modular_epf2.jxl xyb_modular_epf_sigma0.jxl "{'epf': 2, 'sigma_modular': 0.0}"
py -3 patch_lf.py xyb_modular_epf2.jxl xyb_modular_epf_sigma_neg.jxl "{'epf': 2, 'sigma_modular': -1.0}"
py -3 patch_lf.py xyb_modular_epf2.jxl xyb_modular_epf_sigma_tiny.jxl "{'epf': 2, 'sigma_modular': 5.96e-08}"
djxl vardct_xyb_epf.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf.npy
djxl vardct_xyb_epf2.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf2.npy
djxl vardct_xyb_epf3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf3.npy
djxl vardct_xyb_epf1_d3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf1_d3.npy
djxl vardct_xyb_epf2_d3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf2_d3.npy
djxl vardct_xyb_epf3_d3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf3_d3.npy
djxl vardct_xyb_epf3_300x200_d3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf3_300x200_d3.npy
djxl vardct_xyb_epf3_600x400_d3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf3_600x400_d3.npy
djxl vardct_xyb_epf3_13x37_d3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf3_13x37_d3.npy
djxl vardct_xyb_epf3_custom.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf3_custom.npy
djxl vardct_xyb_epf_sharp0.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf_sharp0.npy
djxl vardct_xyb_epf_quant_mul0.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_epf_quant_mul0.npy
djxl xyb_modular_epf2.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_epf2.npy
djxl xyb_modular_epf_sigma_tiny.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_epf_sigma_tiny.npy
```

- `cjxl` v0.12.0 never writes the custom loop-filter fields, so `patch_lf.py` rewrites the loop-filter bundle of a bare codestream's frame header and copies the rest; it re-aligns the TOC. `lfhdr.py` is the header parser it uses; run on its own, it prints a file's headers, with the loop-filter fields.
- At `-e 2`, `cjxl` codes the same coefficients for every `--epf`, so the files of one image and distance differ only in their frame header. Every block is DCT8 and has EPF sharpness 4.
- The zero-flush fields are read, but libjxl's EPF never uses them.
- Each test records how far each reference is from the same file decoded by `djxl` with `epf_iters` patched to 0. Measured on x64 against `djxl` with AVX2, the worst channel RMSE is at most 1.6e-6 and the peak error at most 7.8e-5 (`vardct_xyb_epf3_600x400_d3`). `JxlEpfTests` records the error of each file.

#### How the Modular and both-filter files were made

| File | Why it is here | Reference |
|---|---|---|
| `vardct_xyb_gab_epf1.jxl` | The 64 x 64 image at `cjxl -e 2 --gaborish=1 --epf=1 -d 1`: both filters in one VarDCT frame. At `-d 1` EPF barely touches this image (turning it off changes the output by 3.2e-6), so the next file checks EPF after Gaborish. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gab_epf1.npy` |
| `vardct_xyb_gab_epf3_300x200_d3.jxl` | The 300 x 200 image at `-e 2 --gaborish=1 --epf=3 -d 3`: Gaborish and all three EPF passes, two groups, and a width that is not a multiple of 8. | `External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gab_epf3_300x200_d3.npy` |
| `xyb_modular_gab_epf3.jxl` | The 300 x 200 image as lossy Modular with XYB, `-m 1 -d 2 --gaborish=1 --epf=3`: both filters on a Modular frame, with `epf_sigma_for_modular` 2.0. | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_gab_epf3.npy` |
| `xyb_modular_gab_epf1_600x400.jxl` | The 600 x 400 image at `-m 1 -d 1 --gaborish=1 --epf=1`: both filters across several Modular groups. | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_gab_epf1_600x400.npy` |
| `xyb_modular_epf2_custom.jxl` | `xyb_modular_epf2.jxl` with custom EPF fields: channel scales `(30, 6, 4)`, zero-flush `(0.4, 0.5)`, pass 0 scale 1.1, pass 2 scale 5.0, border SAD multiplier 0.5, and `epf_sigma_for_modular` 1.5. | `External/ReferenceOutput/JxlDecoderTests/xyb_modular_epf2_custom.npy` |
| `modular_grey_gab_epf3.jxl` | The 300 x 200 grey image as lossy Modular **without** XYB (`--disable_perceptual_optimizations`), `-m 1 -d 2 --gaborish=1 --epf=3`: the layout of the conformance file `grayscale_public_university`. libjxl copies the grey channel into three channels and filters all three. The output takes the first. | `External/ReferenceOutput/JxlDecoderTests/modular_grey_gab_epf3.npy`, one channel |
| `modular_grey_gab_epf3_custom.jxl` | `modular_grey_gab_epf3.jxl` with custom Gaborish weights (x 0.2 and 0.05, y 0.08 and 0.03, b 0.15 and -0.02) and `epf_sigma_for_modular` 20, as `grayscale_public_university` has. The three channels then differ after Gaborish, and EPF sums its distances over all three, so a decoder that filtered the one grey plane alone fails. | `External/ReferenceOutput/JxlDecoderTests/modular_grey_gab_epf3_custom.npy`, one channel |

```sh
py -3 make_vardct_xyb.py
cjxl vardct_xyb_64_source.ppm vardct_xyb_gab_epf1.jxl -e 2 --gaborish=1 --epf=1 -d 1
cjxl vardct_xyb_300x200_source.ppm vardct_xyb_gab_epf3_300x200_d3.jxl -e 2 --gaborish=1 --epf=3 -d 3
cjxl vardct_xyb_300x200_source.ppm xyb_modular_gab_epf3.jxl -m 1 -d 2 --gaborish=1 --epf=3
cjxl vardct_xyb_600x400_source.ppm xyb_modular_gab_epf1_600x400.jxl -m 1 -d 1 --gaborish=1 --epf=1
cjxl vardct_xyb_grey_source.pgm modular_grey_gab_epf3.jxl -m 1 -d 2 --gaborish=1 --epf=3 --disable_perceptual_optimizations
py -3 patch_lf.py modular_grey_gab_epf3.jxl modular_grey_gab_epf3_custom.jxl "{'gab': True, 'gab_custom': [0.2, 0.05, 0.08, 0.03, 0.15, -0.02], 'epf': 3, 'sigma_modular': 20.0}"
py -3 patch_lf.py xyb_modular_epf2.jxl xyb_modular_epf2_custom.jxl "{'epf': 2, 'weight': [30, 6, 4, 0.4, 0.5], 'sigma': [1.1, 5.0, 0.5], 'sigma_modular': 1.5}"
djxl vardct_xyb_gab_epf1.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gab_epf1.npy
djxl vardct_xyb_gab_epf3_300x200_d3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_xyb_gab_epf3_300x200_d3.npy
djxl xyb_modular_gab_epf3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_gab_epf3.npy
djxl xyb_modular_gab_epf1_600x400.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_gab_epf1_600x400.npy
djxl xyb_modular_epf2_custom.jxl ../../../External/ReferenceOutput/JxlDecoderTests/xyb_modular_epf2_custom.npy
djxl modular_grey_gab_epf3.jxl ../../../External/ReferenceOutput/JxlDecoderTests/modular_grey_gab_epf3.npy
djxl modular_grey_gab_epf3_custom.jxl ../../../External/ReferenceOutput/JxlDecoderTests/modular_grey_gab_epf3_custom.npy
```

- `cjxl -m 1 --gaborish=1` forces Gaborish on a Modular frame, and `--epf=N` is used as given. So `cjxl` v0.12.0 writes lossy Modular files with either filter alone (`xyb_modular_gaborish.jxl`, `xyb_modular_epf2.jxl`) or with both. It sets `epf_sigma_for_modular` to the distance, and to at least 1.
- Without `--disable_perceptual_optimizations`, `cjxl -m` codes a grey image in XYB, with three planes.
- `JxlLoopFilterTests` records, for each file, how far its reference is from the same file decoded by `djxl` with a filter patched off by `patch_lf.py`. Each filter of each file moves the output by more than the tolerance, except EPF in `vardct_xyb_gab_epf1` (3.2e-6), EPF in `xyb_modular_gab_epf1_600x400` (peak 5.0e-2, RMSE 3.0e-4: above the RMSE tolerance only) and EPF in `modular_grey_gab_epf3` (peak 1.9e-3, RMSE 1.4e-4: above the RMSE tolerance only).
- `modular_grey_gab_epf3_custom.jxl` was checked against a decoder that keeps the three grey channels equal after each filter, as filtering one plane would: that decoder fails it by an RMSE of 6.1e-3. With the default weights the three channels stay equal, so `modular_grey_gab_epf3.jxl` cannot tell the two apart.
- Measured on x64 against `djxl` with AVX2, the worst channel RMSE is at most 1.3e-6 and the peak error at most 4.6e-5 (`vardct_xyb_gab_epf3_300x200_d3`). `JxlLoopFilterTests` records the error of each file. `Image.Load<Rgb24>` is within 0.50 of one 8-bit step of the clamped reference.

#### How the upsampling files were made

Frames coded at a lower resolution and upsampled on decode (D10b). Each reference is `External/ReferenceOutput/JxlDecoderTests/<name>.npy`. `JxlUpsamplingTests` checks them.

| File | Why it is here |
|---|---|
| `upsampling_2x_xyb.jxl`, `upsampling_4x_xyb.jxl`, `upsampling_8x_xyb.jxl` | A 67 x 45 crop of `Png/bike-small.png`, lossy VarDCT with XYB, Gaborish and EPF, upsampled 2x, 4x and 8x with the default weights. The frames are coded at 34 x 23, 17 x 12 and 9 x 6: odd sizes, which the upsampling rounds back down. |
| `upsampling_2x_xyb_nolf.jxl` | The same at 2x with no loop filters. |
| `upsampling_2x_lossless.jxl`, `upsampling_4x_lossless.jxl`, `upsampling_8x_lossless.jxl` | The same crop as lossless Modular: no XYB and no loop filters, so the upsampling stage alone makes the difference from the coded pixels. |
| `upsampling_4x_nn_lossless.jxl` | A 37 x 21 crop, lossless Modular, with custom 4x weights in the image header that pick the nearest pixel. Every 4 x 4 block of the 148 x 84 output is one coded pixel, so `upsampling_4x_nn_lossless.png` (the source) is a bit-exact reference too. |
| `upsampling_2x_custom.jxl`, `upsampling_4x_custom.jxl`, `upsampling_8x_custom.jxl` | The 37 x 21 crop, lossy VarDCT with XYB, with custom "pixel dots" weights for 2x, 4x and 8x in the image header (custom weights mask 1, 2 and 4). |
| `upsampling_2x_alpha_late.jxl` | The 67 x 45 crop with its alpha, upsampled 2x, the alpha too. Every extra channel has the frame's upsampling, so the alpha is upsampled with the colour, after the patches ("late"). |
| `upsampling_2x_alpha_4x.jxl` | The same with the alpha upsampled 4x: the alpha has its own stage, before the patches ("early"). |
| `upsampling_4x_alpha_8x.jxl` | The same at 4x, with the alpha at 8x: coded 17 x 12, alpha 9 x 6. |
| `upsampling_1x_alpha_2x.jxl` | A 288 x 40 crop with alpha: the colour is not upsampled, and the alpha is upsampled 2x. Two groups, so the alpha's group rectangles are half the colour's. |

```sh
magick ../../Png/bike-small.png -alpha off -crop 67x45+100+60 +repage upsampling_rgb_source.png
magick ../../Png/bike-small.png -crop 67x45+100+60 +repage upsampling_rgba_source.png
magick ../../Png/bike-small.png -crop 288x40+0+60 +repage upsampling_rgba_wide_source.png
magick ../../Png/bike-small.png -alpha off -crop 37x21+120+70 +repage upsampling_small_source.png
cjxl upsampling_rgb_source.png upsampling_2x_xyb.jxl -d 1 --resampling=2
cjxl upsampling_rgb_source.png upsampling_4x_xyb.jxl -d 1 --resampling=4
cjxl upsampling_rgb_source.png upsampling_8x_xyb.jxl -d 1 --resampling=8
cjxl upsampling_rgb_source.png upsampling_2x_xyb_nolf.jxl -d 1 --resampling=2 --gaborish=0 --epf=0
cjxl upsampling_rgb_source.png upsampling_2x_lossless.jxl -d 0 --resampling=2
cjxl upsampling_rgb_source.png upsampling_4x_lossless.jxl -d 0 --resampling=4
cjxl upsampling_rgb_source.png upsampling_8x_lossless.jxl -d 0 --resampling=8
cjxl upsampling_small_source.png upsampling_4x_nn_lossless.jxl -d 0 --resampling=4 --already_downsampled --upsampling_mode=0
cjxl upsampling_small_source.png upsampling_2x_custom.jxl -d 1 --resampling=2 --already_downsampled --upsampling_mode=1
cjxl upsampling_small_source.png upsampling_4x_custom.jxl -d 1 --resampling=4 --already_downsampled --upsampling_mode=1
cjxl upsampling_small_source.png upsampling_8x_custom.jxl -d 1 --resampling=8 --already_downsampled --upsampling_mode=1
cjxl upsampling_rgba_source.png upsampling_2x_alpha_late.jxl -d 1 --resampling=2 --ec_resampling=2
cjxl upsampling_rgba_source.png upsampling_2x_alpha_4x.jxl -d 1 --resampling=2 --ec_resampling=4
cjxl upsampling_rgba_source.png upsampling_4x_alpha_8x.jxl -d 1 --resampling=4 --ec_resampling=8
cjxl upsampling_rgba_wide_source.png upsampling_1x_alpha_2x.jxl -d 1 --resampling=1 --ec_resampling=2
for f in upsampling_*.jxl; do djxl $f ../../../External/ReferenceOutput/JxlDecoderTests/${f%.jxl}.npy; done
cp upsampling_small_source.png ../../../External/ReferenceOutput/JxlDecoderTests/upsampling_4x_nn_lossless.png
```

- ImageMagick 7.1.2 made the crops. Only the 37 x 21 crop is committed, as the nearest-neighbour reference; the commands make the others again.
- `cjxl` writes custom weights only with `--already_downsampled`: it then codes the input as it is and sets `--upsampling_mode` (0 nearest neighbour, 1 "pixel dots") in the image header. Without it, `cjxl` downsamples with the default weights and ignores `--upsampling_mode`.
- The upsampling stage clamps each output pixel to the smallest and largest of the 5 x 5 input pixels it is made from. A decoder without the clamp fails 13 of the 14 `djxl` comparisons, all but `upsampling_2x_custom`.
- Measured on x64 against `djxl` with AVX2, the worst channel RMSE and the peak error are 5.9e-7 and 3.5e-6 (`upsampling_2x_xyb`), 4.4e-7 and 3.1e-6 (`upsampling_4x_xyb`), 5.4e-7 and 3.3e-6 (`upsampling_8x_xyb`), 5.0e-7 and 5.3e-6 (`upsampling_2x_xyb_nolf`), 1.9e-8 and 1.2e-7 (`upsampling_2x_lossless`), 1.8e-8 and 1.2e-7 (`upsampling_4x_lossless`, `upsampling_8x_lossless`), 0 and 0 (`upsampling_4x_nn_lossless`), 5.2e-7 and 2.9e-6 (`upsampling_2x_custom`), 3.9e-7 and 2.9e-6 (`upsampling_4x_custom`), 4.2e-7 and 2.9e-6 (`upsampling_8x_custom`), 5.7e-7 and 3.6e-6 (`upsampling_2x_alpha_late`), 5.9e-7 and 3.5e-6 (`upsampling_2x_alpha_4x`), 4.4e-7 and 3.1e-6 (`upsampling_4x_alpha_8x`) and 3.7e-7 and 8.9e-6 (`upsampling_1x_alpha_2x`). libjxl fuses the stage's multiplies and adds on AVX2, and this port does not (ADR 0004). `Image.Load<Rgba32>` is within 0.50 of one 8-bit step of the clamped reference.

#### How `twelve_bit` was made

```sh
py -3 make_twelve_bit.py
cjxl twelve_bit_source.png twelve_bit.jxl -d 0 --override_bitdepth=12
djxl twelve_bit.jxl ../../../External/ReferenceOutput/JxlDecoderTests/twelve_bit.png
```

- `make_twelve_bit.py` writes `twelve_bit_source.png`, a 16-bit PNG whose samples are 12-bit values scaled to 16 bits, with an `sBIT` chunk of 12. The source PNG is not committed; the script makes it again.
- `--override_bitdepth=12` stores 12 bits per sample, for alpha too; `jxlinfo` reports "12-bit RGB+Alpha".

#### How `jpeg_rgb` was made

```sh
cjxl ../../Jpg/baseline/jpeg-rgb.jpg jpeg_rgb.jxl
djxl jpeg_rgb.jxl ../../../External/ReferenceOutput/JxlDecoderTests/jpeg_rgb.npy
```

- Given a JPEG, `cjxl` transcodes it losslessly by default (`--lossless_jpeg=1`), at effort 7. It writes a container with a `jbrd` box, which holds what is needed to rebuild the JPEG, between two `jxlp` boxes.
- The frame is VarDCT with no colour transform, because the JPEG's components are R, G and B. Its DC quantization factors are all 1/255, and its DCT8 quantization table is raw: before D10d, decoding it threw `NotSupportedException` ("RAW quantization tables") in the AC global section.
- `djxl` decodes the frame to pixels; the `jbrd` box only matters when rebuilding the JPEG, which this decoder does not do.
- The JPEG conformance vectors allow an RMSE of 1e-5, ten times less than the general lossy rule, so `JxlRawQuantTableTests` holds this file to that too. Measured on x64 against `djxl` with AVX2, the worst channel RMSE is 8.4e-8 and the peak error 2.0e-6. `Image.Load<Rgb24>` is within one 8-bit step of the clamped reference.
