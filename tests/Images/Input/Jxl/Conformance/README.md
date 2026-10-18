#### JPEG XL conformance vectors

These are the JPEG XL decoder conformance test cases from [libjxl/conformance](https://github.com/libjxl/conformance) at commit `b1d0f990b03e57bf6d137c365cd5dc8b470b9191` (see `source.json`).

The repository is licensed under the BSD 3-Clause License, Copyright (c) the JPEG XL Project Authors (see `LICENSE`). Its README says: *"All test bitstreams and reference decoded images can be reproduced freely for testing purposes."*

#### Layout

- `<id>/input.jxl` and `<id>/test.json` are copied unchanged. `test.json` holds the error thresholds and the sha256 of each reference file.
- `<id>_5/test.json` holds the looser Level 5 thresholds. It reuses the input of `<id>`, which upstream links with a symlink.
- `main_level5.txt` and `main_level10.txt` list the vectors for each level.
- `mul_no_extra_channels/reference_image.npy` is the only reference stored in upstream git. All the other references are downloaded by sha256 from `https://storage.googleapis.com/jxl-conformance/objects/<sha256>` (665 MB in total) into the gitignored cache `tests/Images/External/Jxl/.cache`.
- The `bike`, `cafe` and `progressive` inputs are **not committed**, because of their source-image licences. The tests download them from the pinned commit and check them against the sha256 values in `source.json`.

The conformance tests skip when the data they need is not in the cache. Set `JXL_CONFORMANCE=1` to download missing data and to fail instead of skipping.

#### Rendered references

The conformance references are the planes as coded: `djxl` writes them with `--norender_spotcolors`. `Image.Load` renders spot colours over the colour, so its test compares with a reference made by `djxl` v0.12.0 with spot rendering on, its default:

```sh
djxl spot/input.jxl ../../../External/ReferenceOutput/JxlDecoderTests/spot.png
```

The PNG is 16-bit RGBA, with the vector's ICC profile in its `iCCP` chunk.

#### Source images

| Vector | Input committed | Source licence | Author or source |
|---|---|---|---|
| alpha_nonpremultiplied | yes | CC0 | |
| alpha_premultiplied | yes | CC0 | |
| alpha_triangles | yes | CC0 | Jon Sneyers |
| animation_icos4d | yes | CC0 | Jason Hise, <https://commons.wikimedia.org/wiki/File:24-cell-orig.gif> |
| animation_newtons_cradle | yes | CC BY-SA 3.0 | Dominique Toussaint, <https://commons.wikimedia.org/wiki/File:Newtons_cradle_animation_book_2.gif> |
| animation_spline | yes | CC0 | |
| bench_oriented_brg | yes | CC0 | |
| bicycles | yes | CC0 | |
| bike | no (downloaded) | ITU-T T.24 test-image licence | ITU-T |
| blendmodes | yes | CC0 | |
| cafe | no (downloaded) | ITU-T T.24 test-image licence | ITU-T |
| cmyk_layers | yes | CC0 | |
| delta_palette | yes | CC0 | |
| grayscale | yes | CC0 | |
| grayscale_jpeg | yes | CC0 | |
| grayscale_public_university | yes | CC BY 3.0 | Arnold and Richter Cine Technik GmbH (converted to greyscale and compressed lossily) |
| lossless_pfm | yes | CC0 | |
| lz77_flower | yes | CC0 | |
| mul_no_extra_channels | yes | CC0 | |
| noise | yes | CC0 | |
| opsin_inverse | yes | CC0 | |
| patches | yes | CC0 | |
| patches_lossless | yes | CC0 | |
| progressive | no (downloaded) | Microsoft custom licence | Microsoft |
| spot | yes | CC0 | |
| sunset_logo | yes | CC0 | Jon Sneyers |
| upsampling | yes | CC BY-SA 3.0 | <https://commons.wikimedia.org/wiki/File:PNG_transparency_demonstration_1.png> |

The CC BY-SA 3.0 and CC BY 3.0 files are redistributed unmodified, with the attribution above.
