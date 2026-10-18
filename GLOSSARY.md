# ImageSharp

A managed library that decodes, processes and encodes images. These terms come from its image formats and colour handling.

## Language

### JPEG XL colour

**XYB-encoded**:
A JPEG XL image whose codestream stores colour in the XYB perceptual space instead of in the image's own colour space. The decoder converts it back to RGB or grey.
_Avoid_: XYB image, opsin image

**Colour encoding**:
A JPEG XL header field that names the image's colour space with enumerated values (colour space, white point, primaries, transfer function, rendering intent) instead of an ICC profile.
_Avoid_: enum colour space, CICP

**Embedded ICC profile**:
The ICC profile that a file carries for its image, exactly as the file stores it. It describes the original image, which is not always the decoded pixels.
_Avoid_: original ICC, source profile

**Pixel profile**:
The ICC profile that describes a decoded image's pixels. When an image has none, its pixels are gamma-encoded sRGB.
_Avoid_: output profile, image profile

**Extra channel**:
A per-pixel channel of a JPEG XL image beyond its colour channels, such as alpha, black, a spot colour or depth. The image header describes each one by type.
_Avoid_: auxiliary channel, additional plane

### Colour management

**Profile connection space** (PCS):
The device-independent colour space through which an ICC conversion passes: the source profile maps device values to it and the target profile maps it to device values. It is CIE XYZ or CIE Lab, both relative to a D50 white.
_Avoid_: working space, intermediate space

**Rendering intent**:
The rule an ICC conversion follows for colours that the target cannot show: perceptual, relative colorimetric, saturation or absolute colorimetric. A profile can hold a different table for each one.
_Avoid_: conversion mode, gamut mapping mode

**Matrix/TRC profile**:
An RGB ICC profile that describes its colours with three tone curves (TRCs) and a 3x3 matrix of colorants, instead of lookup tables. Gray profiles are the one-channel case: a single tone curve.
_Avoid_: shaper profile, simple profile

### JPEG XL testing

**Conformance vector**:
One test case from the JPEG XL conformance suite: a codestream, its reference decoded output, and the error thresholds a decoder must meet against that output.
_Avoid_: test vector, conformance image

### JPEG XL encoding

**Distance**:
The JPEG XL encoder's target for lossy error, in butteraugli units: 0 means lossless, about 1 is visually lossless, and larger values allow more visible loss.
_Avoid_: quality, compression level

**Effort**:
How much search the JPEG XL encoder spends to reach a given distance. It changes encode time and file size, not the target error.
_Avoid_: speed, encoder level
