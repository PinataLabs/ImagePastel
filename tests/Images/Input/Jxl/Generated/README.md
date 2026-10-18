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
