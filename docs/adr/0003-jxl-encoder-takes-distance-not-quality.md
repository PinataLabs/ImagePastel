# The JXL encoder takes Distance, not Quality

`JxlEncoder` exposes `float Distance` (default 1.0, 0 = lossless) as its only lossy-versus-lossless control, unlike the `int Quality` of `WebpEncoder` and `JpegEncoder`. Distance is JPEG XL's native error target. libjxl and cjxl work in it, our verification thresholds are calibrated per distance, and a 0–100 quality would only be a lossy remapping of it (cjxl's `-q` mapping), with a second meaning to document and test. Distance 0 means lossless, so there is no separate lossless switch.

## Considered Options

- **`int Quality` 0–100, as in WebP and JPEG:** rejected. It's consistent across ImageSharp but hides the native unit, and its mapping to distance is cjxl's convention, not part of the format.
- **Both `Quality` and `Distance`:** rejected. Two settings for one thing need precedence rules and double the test matrix.

## Consequences

- Do not add `Quality` "for consistency". If callers ask for it, add a documented static helper that converts quality to distance rather than a second encoder property.
