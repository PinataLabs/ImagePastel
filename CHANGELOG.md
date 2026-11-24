# Changelog

## Contents

- [Unreleased](#unreleased)
- [v2.1.14](#v2114---2026-10-17)

## Unreleased

### ⚠️ Breaking Changes

- **The NuGet package and the assembly are now `PinataLabs.ImagePastel`.** `PinataLabs.ImageSharp` gets no further releases. Replace the package reference, then update the namespaces as the next entry describes. The README section "Move from PinataLabs.ImageSharp 2.1.14" lists the steps ([#328](https://github.com/PinataLabs/ImagePastel/pull/328)).

- **Every public type moves from the `SixLabors.ImageSharp` namespace to `PinataLabs.ImagePastel`.** The type names and members do not change, so porting is a find-and-replace on `using` directives and fully qualified names. For example, `SixLabors.ImageSharp.Formats.Png` becomes `PinataLabs.ImagePastel.Formats.Png` ([#346](https://github.com/PinataLabs/ImagePastel/pull/346), [#347](https://github.com/PinataLabs/ImagePastel/pull/347)).

### 🔒 Security

- **The five advisories that Six Labors published against the 2.x line on 2026-10-07 are fixed.** A BigTIFF file that declares billions of entries no longer loops without end. A histogram over a pixel with infinite luminance no longer ends the process with an access violation. The CCITT encoder no longer overruns its buffer on a narrow image. The ICC CLUT bounds fix gets a regression test (GHSA-wmxv-xphr-5c9g, GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w, GHSA-gwg2-r3hj-4w44; [#343](https://github.com/PinataLabs/ImagePastel/pull/343)).

- **Malformed input no longer crashes the decoders.** GIF files with invalid LZW codes or frame geometry, TIFF files with bad or very large tags, WebP files with odd containers, and JPEG files with missing scans or junk between markers now load or throw `InvalidImageContentException`. Before, some threw `IndexOutOfRangeException`, `ArgumentOutOfRangeException` or `NullReferenceException`, or allocated far more memory than the file could fill. A BMP RLE bitmap is now bounded by its data length before the buffer is allocated ([#46](https://github.com/PinataLabs/ImagePastel/pull/46), [#48](https://github.com/PinataLabs/ImagePastel/pull/48), [#49](https://github.com/PinataLabs/ImagePastel/pull/49), [#50](https://github.com/PinataLabs/ImagePastel/pull/50), [#139](https://github.com/PinataLabs/ImagePastel/pull/139)).

### ✨ New Features

- **JPEG XL decoding.** `Image.Load` reads `.jxl` files with `Configuration.Default`. The decoder supports lossless Modular and lossy VarDCT frames, every transform size, patches, splines, noise, progressive files, animation, layers and blending, spot colours, CMYK, and the 709, DCI, gamma, PQ and HLG transfer functions. Exif, XMP and ICC profiles are read from the container. `JxlDecoder` exposes `MaxFrames`, and every stage honours a cancellation token. Untrusted files are bounded by a pixel cap and a total sample budget, and the sample budget covers all frames of an animation. Format details are in `JxlMetadata` and `JxlFrameMetadata` ([#149](https://github.com/PinataLabs/ImagePastel/pull/149), [#157](https://github.com/PinataLabs/ImagePastel/pull/157), [#162](https://github.com/PinataLabs/ImagePastel/pull/162), [#169](https://github.com/PinataLabs/ImagePastel/pull/169), [#193](https://github.com/PinataLabs/ImagePastel/pull/193), [#206](https://github.com/PinataLabs/ImagePastel/pull/206), [#221](https://github.com/PinataLabs/ImagePastel/pull/221), [#230](https://github.com/PinataLabs/ImagePastel/pull/230), [#246](https://github.com/PinataLabs/ImagePastel/pull/246), [#254](https://github.com/PinataLabs/ImagePastel/pull/254), [#273](https://github.com/PinataLabs/ImagePastel/pull/273), [#279](https://github.com/PinataLabs/ImagePastel/pull/279), [#286](https://github.com/PinataLabs/ImagePastel/pull/286), [#296](https://github.com/PinataLabs/ImagePastel/pull/296), [#314](https://github.com/PinataLabs/ImagePastel/pull/314), [#348](https://github.com/PinataLabs/ImagePastel/pull/348)).

- **JPEG XL encoding.** `image.SaveAsJxl()`, `SaveAsJxlAsync()` and `image.Save("x.jxl")` write JPEG XL. `JxlEncoder.Distance` picks lossless (0) or lossy, and `JxlEncoder.Effort` picks `Fastest`, `Default` or `Thorough`. Exif and XMP go into container boxes, and an Exif orientation becomes the codestream orientation. A cancelled `SaveAsJxlAsync` now stops within one group or tile instead of running to the end ([#222](https://github.com/PinataLabs/ImagePastel/pull/222), [#237](https://github.com/PinataLabs/ImagePastel/pull/237), [#280](https://github.com/PinataLabs/ImagePastel/pull/280), [#313](https://github.com/PinataLabs/ImagePastel/pull/313), [#329](https://github.com/PinataLabs/ImagePastel/pull/329), [#336](https://github.com/PinataLabs/ImagePastel/pull/336), [#372](https://github.com/PinataLabs/ImagePastel/pull/372)).

- **Brotli-compressed JPEG XL metadata loads on `netstandard2.1`.** That target has no Brotli decoder, so `brob` Exif and XMP boxes were dropped. A managed port of google/brotli v1.2.0 now reads them. It compiles only into the `netstandard2.1` build and adds no package dependency ([#371](https://github.com/PinataLabs/ImagePastel/pull/371)).

- **ICC colour management.** `IccColorTransform` converts pixels between two ICC profiles. `IccProfileFactory` builds sRGB, linear sRGB, Display P3, Rec.2020, Adobe RGB-compatible and grey profiles. `image.Mutate(x => x.ConvertColorProfile(target))` and `ConvertColorProfileToSrgb()` apply them. Each decoder takes a `ColorProfileConversion` option to convert to sRGB or a target profile while it loads. JPEG and TIFF CMYK images decode through their embedded profile ([#188](https://github.com/PinataLabs/ImagePastel/pull/188)).

- **More formats keep their ICC profile.** PNG reads and writes the `iCCP` chunk, lossless WebP writes the profile it loaded, and TIFF `Identify` returns the ICC and IPTC profiles ([#35](https://github.com/PinataLabs/ImagePastel/pull/35), [#137](https://github.com/PinataLabs/ImagePastel/pull/137), [#141](https://github.com/PinataLabs/ImagePastel/pull/141)).

- **A `net11.0` build.** The package now ships `net10.0`, `net11.0` and `netstandard2.1` ([#353](https://github.com/PinataLabs/ImagePastel/pull/353)).

### ⚡ Performance

- **Lossy WebP encoding is about 30% faster, with byte-identical output.** Analysis runs macroblock rows in parallel, the mode search transforms each block once, and new AVX2 paths handle intra-4, intra-16 and chroma blocks. RGB-to-YUV conversion is vectorized, and methods 3 to 6 decide each macroblock once ([#350](https://github.com/PinataLabs/ImagePastel/pull/350), [#357](https://github.com/PinataLabs/ImagePastel/pull/357), [#360](https://github.com/PinataLabs/ImagePastel/pull/360)).

- **Lossy WebP with alpha allocates under 3 times the image size, down from 17 times.** The alpha-plane encoder stores its backward references in pooled memory instead of one heap object per reference ([#148](https://github.com/PinataLabs/ImagePastel/pull/148)).

- **The JPEG XL edge-preserving filter is vectorized.** Its output is bit-exact with the scalar path, with or without FMA ([#349](https://github.com/PinataLabs/ImagePastel/pull/349)).

### 🐛 Bug Fixes

- **JPEG decodes to the same pixels, and encodes the same bytes, on every instruction set.** The SIMD paths gave results that differed from the scalar path by one level in some pixels. The decoder also resyncs correctly after `JPEGTables`, junk bytes and reserved markers ([#99](https://github.com/PinataLabs/ImagePastel/pull/99), [#367](https://github.com/PinataLabs/ImagePastel/pull/367), [#370](https://github.com/PinataLabs/ImagePastel/pull/370)).

- **Lossy WebP output no longer depends on the instruction set, and several encoder and decoder errors are fixed.** The SIMD paths now match the scalar paths. `QuantEnc.IsFlat` reads each AC level. The dimension limit, the AVX2 residual cost and bit-writer growth are fixed. Odd-sized `ALPH` and `VP8L` chunks are read without their padding byte. `Vp8LEncoder` can be disposed before its transform buffers exist ([#37](https://github.com/PinataLabs/ImagePastel/pull/37), [#120](https://github.com/PinataLabs/ImagePastel/pull/120), [#146](https://github.com/PinataLabs/ImagePastel/pull/146), [#147](https://github.com/PinataLabs/ImagePastel/pull/147), [#358](https://github.com/PinataLabs/ImagePastel/pull/358), [#364](https://github.com/PinataLabs/ImagePastel/pull/364)).

- **TIFF files round-trip.** The encoder writes the metadata of every frame, writes only the tags that describe the strip data it wrote, counts offsets from the start of the TIFF data, keeps the quantizer's dither settings for palette output, writes the correct CCITT bit depth and `SamplesPerPixel` type, and saves to a stream that cannot seek. The decoder applies the `BitsPerSample` default, rejects a `ColorMap` shorter than the palette, and keeps ICC profiles through a load and save ([#78](https://github.com/PinataLabs/ImagePastel/pull/78), [#89](https://github.com/PinataLabs/ImagePastel/pull/89), [#124](https://github.com/PinataLabs/ImagePastel/pull/124), [#131](https://github.com/PinataLabs/ImagePastel/pull/131), [#132](https://github.com/PinataLabs/ImagePastel/pull/132), [#133](https://github.com/PinataLabs/ImagePastel/pull/133), [#134](https://github.com/PinataLabs/ImagePastel/pull/134), [#136](https://github.com/PinataLabs/ImagePastel/pull/136)).

- **A palette PNG whose `tRNS` chunk marks every entry opaque no longer reports `HasTransparency = true`.** PNG also splits data chunks correctly, recovers from malformed chunks, and encodes and decodes Adam7 passes that have no columns ([#47](https://github.com/PinataLabs/ImagePastel/pull/47), [#142](https://github.com/PinataLabs/ImagePastel/pull/142), [#345](https://github.com/PinataLabs/ImagePastel/pull/345)).

- **BMP indexed output, palettes and bitfields are fixed.** Missing, short and 2-bit palettes decode. Bitfield masks of every width scale to 8 bits. Malformed headers are rejected ([#79](https://github.com/PinataLabs/ImagePastel/pull/79), [#138](https://github.com/PinataLabs/ImagePastel/pull/138), [#140](https://github.com/PinataLabs/ImagePastel/pull/140)).

- **Palette quantization is more accurate.** The Octree quantizer includes alpha in its palette. Bounds that do not start at the top row are quantized on every row. An empty palette is rejected when the pixel map is built ([#94](https://github.com/PinataLabs/ImagePastel/pull/94), [#143](https://github.com/PinataLabs/ImagePastel/pull/143), [#144](https://github.com/PinataLabs/ImagePastel/pull/144), [#145](https://github.com/PinataLabs/ImagePastel/pull/145)).

- **EXIF, IPTC and ICC profiles read and write correctly, and the WebP RIFF size is right for lossless images with both EXIF and XMP** ([#35](https://github.com/PinataLabs/ImagePastel/pull/35), [#38](https://github.com/PinataLabs/ImagePastel/pull/38)).

- **Netpbm P4 rows are padded and `maxval` is scaled correctly** ([#52](https://github.com/PinataLabs/ImagePastel/pull/52), [#124](https://github.com/PinataLabs/ImagePastel/pull/124)).

- **Image loading, format detection and processing operations handle their edge cases** ([#33](https://github.com/PinataLabs/ImagePastel/pull/33), [#34](https://github.com/PinataLabs/ImagePastel/pull/34)).

### 🔧 Build

- **The generated code comes from a Roslyn source generator instead of T4 templates.** The generated output is no longer committed ([#204](https://github.com/PinataLabs/ImagePastel/pull/204)).

- **The build uses the .NET 11 SDK and C# `latest`.** Building from source needs the .NET 11 SDK and the .NET 10 runtime ([#353](https://github.com/PinataLabs/ImagePastel/pull/353)).

## v2.1.14 - 2026-10-17

- **First release as `PinataLabs.ImageSharp`**, a fork of the Six Labors ImageSharp 2.1 line under the Apache 2.0 licence. It targets `net10.0` and `netstandard2.1`, and keeps the `SixLabors.ImageSharp` namespaces.
