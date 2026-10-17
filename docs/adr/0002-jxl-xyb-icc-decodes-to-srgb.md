# XYB images with an embedded ICC decode to gamma-encoded sRGB, not linear

A JPEG XL image that stores colour as XYB and embeds an ICC profile cannot be decoded into the profile's space without a colour-management engine, and ImageSharp has none. libjxl, when it has no CMS, outputs such images as linear sRGB (or linear grey), and the conformance references expect that. We decode them into `Image<TPixel>` as gamma-encoded sRGB (or grey with the sRGB curve) instead. Every other part of ImageSharp treats pixels without a profile as gamma-encoded sRGB, and linear values held in 8-bit pixels band badly in the shadows. The conversion from XYB is analytic in both cases, so this costs no accuracy. The embedded profile goes in `JxlMetadata`, never in `ImageMetadata.IccProfile`, because it does not describe the decoded pixels. The conformance harness uses an internal seam that returns linear float planes.

## Considered Options

- **Linear sRGB in `Image<TPixel>`, as libjxl does:** rejected. It mislabels the pixels for every processor and encoder, and it loses precision at 8 bits.
- **Attach the embedded ICC as the pixels' profile:** rejected. The pixels are not in that space, and the JPEG, TIFF and WebP encoders would write the wrong profile into every re-save.

## Consequences

- Decoded pixels for these images differ from `djxl`'s no-CMS output by the sRGB transfer curve. Tests that compare against libjxl or Magick.NET output must compare in the same space.
- If a caller-chosen output colour space is added later, this stays the default.
