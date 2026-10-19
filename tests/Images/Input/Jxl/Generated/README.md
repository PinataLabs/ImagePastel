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
| `vardct_strategies.jxl` | The image of `vardct_small.jxl` at a slower effort, which also uses 4x8 and 16x32 blocks: the decoder rejects every strategy but DCT8 until D9. | None: it must throw `NotSupportedException` |
| `vardct_xyb.jxl` | Lossy VarDCT of the same image with XYB (`cjxl -e 2`): the decoder rejects XYB VarDCT until D8d. | None: it must throw `NotSupportedException` |

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
benchmark_xl --input=vardct_small.png --codec=jxl:wombat:d1:gab0:epf0:noperc --save_compressed --output_dir=out
mv out/vardct_small.png.noperc.epf0.gab0.d1.thunder.jxl vardct_small.jxl
mv out/vardct_groups.png.noperc.epf0.gab0.d1.thunder.jxl vardct_groups.jxl
mv out/vardct_alpha.png.noperc.epf0.gab0.d1.thunder.jxl vardct_alpha.jxl
mv out/vardct_small.png.noperc.epf0.gab0.d1.wombat.jxl vardct_strategies.jxl
cjxl vardct_small.png vardct_xyb.jxl -e 2 --gaborish=0 --epf=0
djxl vardct_small.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_small.npy
djxl vardct_groups.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_groups.npy
djxl vardct_alpha.jxl ../../../External/ReferenceOutput/JxlDecoderTests/vardct_alpha.npy
```

- `make_vardct.py` writes the source PNGs. They are not committed; the script makes them again.
- `benchmark_xl` is from the same v0.12.0 release as `cjxl`. `noperc` (`disable_perceptual_optimizations`) turns XYB off and codes every quant table as parametric DCT with one distance band, which needs no `FastPowf`. `thunder` is effort 2, where every block is DCT8 and the quant field is constant. `gab0` and `epf0` turn the loop filters off.
- `benchmark_xl --print_more_stats` counts 7 4x8 blocks and 2 16x32 blocks in `vardct_strategies.jxl`.
- The VarDCT output cannot match `djxl` bit for bit (`docs/jxl/spec.md`, "Tolerance for lossy output"). Measured on x64 against `djxl` with AVX2, the worst channel RMSE is 8.1e-8 (`vardct_small`), 8.3e-8 (`vardct_groups`) and 8.1e-8 (`vardct_alpha`), and the peak error 3.6e-7, 4.2e-7 and 4.2e-7.
