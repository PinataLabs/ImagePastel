#### Generated JPEG XL test files

These files cover features that no conformance vector checks. They were made for this repository, with the libjxl tools at v0.12.0 (`jxl-x64-windows-static.zip` from the [v0.12.0 release](https://github.com/libjxl/libjxl/releases/tag/v0.12.0)). They are licensed under the Apache License, Version 2.0, like the rest of the repository.

| File | Why it is here | Reference |
|---|---|---|
| `premultiplied_alpha.jxl` | Lossless Modular, 16-bit RGB with premultiplied (associated) alpha. Alpha is 0, 1/4, 1/2, 3/4 and 1 in bands of 8 columns. | `premultiplied_alpha_straight.png` (straight alpha, for `Image<TPixel>`); `External/ReferenceOutput/JxlDecoderTests/premultiplied_alpha.png` (as coded, for `DecodeToPlanes`) |
| `greyscale.jxl` | Lossless Modular, 8-bit grey, no squeeze. | `External/ReferenceOutput/JxlDecoderTests/greyscale.png` |
| `greyscale_alpha.jxl` | Lossless Modular, 16-bit grey with straight alpha, no squeeze. `cjxl` puts it in a container with a `jxll` box. | `External/ReferenceOutput/JxlDecoderTests/greyscale_alpha.png` |
| `greyscale_squeeze.jxl` | Lossless Modular, 8-bit grey with the default squeeze and no loop filters: the D3 done-check. Same pixels as `greyscale.jxl`. | `External/ReferenceOutput/JxlDecoderTests/greyscale_squeeze.png` |
| `rgba_squeeze.jxl` | Lossless Modular, 8-bit RGBA with the default squeeze, including the chroma and alpha channels. 321 x 283: odd sizes, and more than one group. | `External/ReferenceOutput/JxlDecoderTests/rgba_squeeze.png` |

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
djxl greyscale.jxl ../../../External/ReferenceOutput/JxlDecoderTests/greyscale.png
djxl greyscale_alpha.jxl ../../../External/ReferenceOutput/JxlDecoderTests/greyscale_alpha.png
djxl greyscale_squeeze.jxl ../../../External/ReferenceOutput/JxlDecoderTests/greyscale_squeeze.png
djxl rgba_squeeze.jxl ../../../External/ReferenceOutput/JxlDecoderTests/rgba_squeeze.png
```

- `make_greyscale_and_squeeze.py` writes the source PNGs. They are not committed; the script makes them again.
- `--responsive=1` makes lossless `cjxl` use the squeeze transform. Lossless encoding turns the loop filters off, and these files have one pass.
- The files are lossless, so each `djxl` PNG holds the same pixels as its source PNG.
