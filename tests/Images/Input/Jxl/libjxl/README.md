#### libjxl test data

These files come from [libjxl/testdata](https://github.com/libjxl/testdata) at commit `73695d303670c90e4d506ea89d9901b081385089`. They are licensed under the Creative Commons Attribution 4.0 International License (see `LICENSE`), Copyright (c) the JPEG XL Project Authors. The paths below match the paths in that repository. The files are copied unmodified.

| File | Why it is here | Reference |
|---|---|---|
| `jxl/blending/cropped_traffic_light.jxl` | Four cropped and blended animation frames | `cropped_traffic_light_frame-{0..3}.png`, exact |
| `jxl/splines.jxl` | Splines without animation or XYB | `jxl/splines.png` |
| `jxl/spline_on_first_frame.jxl` | Splines must be cleared on every frame | `jxl/spline_on_first_frame.png` |
| `jxl/boxes/square-extended-size-container.jxl` | Container box with a 64-bit size | none; box parsing only |
| `jxl/pq_gradient.jxl` | PQ transfer function, 1088 x 64 16-bit grey, lossless Modular, not XYB: a pixel profile with libjxl's 64-entry PQ table curve (D11d). A D11 done-check vector | `External/ReferenceOutput/JxlDecoderTests/pq_gradient.npy`, the float32 planes, made with `djxl jxl/pq_gradient.jxl ../../../External/ReferenceOutput/JxlDecoderTests/pq_gradient.npy` (v0.12.0): the planes match exactly. `External/ReferenceOutput/JxlDecoderTests/pq_gradient.png`, made with `djxl jxl/pq_gradient.jxl ../../../External/ReferenceOutput/JxlDecoderTests/pq_gradient.png` (v0.12.0): `Image.Load` gives `L16`, equal to it, and its `iCCP` chunk is the profile `djxl` builds, whose table curve the built one equals entry by entry (largest difference 0 steps) |
| `jxl/jpeg_reconstruction/1x1_exif_xmp.jxl` | Exif and XMP boxes | none; metadata only |
| `jxl/jpeg_reconstruction/1x1_exif_xmp.jpg` | The JPEG that `1x1_exif_xmp.jxl` was made from | its Exif and XMP are the profiles the `.jxl` must give |
| `oss-fuzz/*` (44 files) | Minimised OSS-Fuzz crash inputs | none; each must decode or fail cleanly |
