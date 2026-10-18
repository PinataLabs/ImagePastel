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
