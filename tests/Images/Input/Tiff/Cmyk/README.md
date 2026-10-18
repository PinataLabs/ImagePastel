#### Separated (CMYK) TIFF test files

These files test the decoding of TIFF images with `PhotometricInterpretation = Separated`. They were made for this repository by `make_cmyk_tiffs.py` (Python 3, standard library only), and are licensed under the Apache License, Version 2.0, like the rest of the repository.

Each image is 48 x 32 pixels in strips of 8 rows. Cyan grows from left to right, magenta grows from top to bottom, yellow is a hashed pattern, and black steps in 8 x 8 blocks. Every ink reaches 0 and full coverage.

| File | Layout | Notes |
|---|---|---|
| `cmyk-contig-08.tiff` | chunky, 8-bit, little endian | Writes `InkSet = 1` (CMYK) and `NumberOfInks = 4`. |
| `cmyk-contig-16_lsb.tiff` | chunky, 16-bit, little endian | No `InkSet`: the default is CMYK. |
| `cmyk-contig-16_msb.tiff` | chunky, 16-bit, big endian | |
| `cmyk-planar-08.tiff` | planar, 8-bit, little endian | |
| `cmyk-planar-16_lsb.tiff` | planar, 16-bit, little endian | |
| `cmyk-planar-16_msb.tiff` | planar, 16-bit, big endian | |
| `cmyk-contig-08_deflate_predictor.tiff` | chunky, 8-bit, Deflate with the horizontal predictor | |
| `cmyk-contig-16_msb_deflate_predictor.tiff` | chunky, 16-bit, big endian, Deflate with the horizontal predictor | |
| `cmyk-contig-08_icc.tiff` | chunky, 8-bit, little endian | Embeds `cmyk-test-profile.icc`. |
| `cmyk-planar-16_msb_icc.tiff` | planar, 16-bit, big endian | Embeds `cmyk-test-profile.icc`. |
| `inkset-not-cmyk-08.tiff` | chunky, 8-bit | `InkSet = 2` (not CMYK). The decoder must throw `NotSupportedException`. |
| `cmyk-contig-08_jpeg.tiff` | chunky, 8-bit | `Compression = 7` (JPEG) with zero bytes for strips. The decoder must throw `NotSupportedException`. |
| `cmyk-test-profile.icc` | ICC v2.1 input profile, CMYK to Lab | See below. |

#### The test profile

`cmyk-test-profile.icc` is a small CMYK profile with no press data in it, so it can be redistributed. It has the tags `desc`, `cprt`, `wtpt` and `A2B0` (a `lut16Type` with a 5-point CLUT for each ink). Its colours come from the simple CMYK-to-RGB formula applied after a dot gain of `1 - (1 - ink)^1.6` for each ink, then from sRGB to D50 Lab. Because of the dot gain, a conversion through this profile gives darker colours than the formula alone.

ImageMagick 7.1.2 (Little CMS 2) reads the profile and converts `cmyk-contig-08_icc.tiff` through it without errors.

#### References

Decoder tests compare against Magick.NET at test time: the profiles are removed and the image is converted to sRGB, which applies the formula `R = (1 - C) * (1 - K)`, and the same for G and B.

#### How the files were made

```sh
py -3 make_cmyk_tiffs.py
```
