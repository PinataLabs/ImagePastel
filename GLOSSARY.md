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

### JPEG XL testing

**Conformance vector**:
One test case from the JPEG XL conformance suite: a codestream, its reference decoded output, and the error thresholds a decoder must meet against that output.
_Avoid_: test vector, conformance image
