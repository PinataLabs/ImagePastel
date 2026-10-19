# JPEG XL codec: handoff spec

This is the destination of [Wayfinder map: JPEG XL codec (decode + encode)](https://github.com/PinataLabs/ImageSharp/issues/100). It holds a PRD and the ordered implementation slices. Raise one implementation issue per slice from the [slice table](#slices), and link the issue to its row.

Each decision below names the ticket that holds its detail. The ticket is the source of truth; this file only summarises it. Terms such as *XYB-encoded*, *colour encoding*, *pixel profile*, *extra channel*, *conformance vector*, *distance* and *effort* are defined in [`GLOSSARY.md`](../../GLOSSARY.md).

## PRD

### Goal

A pure managed JPEG XL (ISO/IEC 18181) codec in `src/ImageSharp/Formats/Jxl`, for `net10.0` and `netstandard2.1`, with no native dependency in the shipped library.

- **Decoder:** the full Main profile (Level 5 and Level 10). It must pass all 27 libjxl conformance vectors within their thresholds.
- **Encoder:** lossless Modular and lossy VarDCT at three effort levels. It is verified by reference-decoder validity, bit-exact lossless round-trips and an SSIMULACRA2 floor for lossy output.

Correctness comes first. There is no performance target.

### Source and licensing

- Port closely from libjxl (BSD-3-Clause, royalty-free patent grant), with attribution in file headers and `THIRD-PARTY-NOTICES.TXT`. See [ADR 0001](../adr/0001-port-jpeg-xl-from-libjxl.md).
- Never read SixLabors/ImageSharp upstream code (`CLAUDE.md`).
- Write new xUnit tests in this repo's style. Copy libjxl test *data* with attribution; never translate its gtest files.

### Decisions

| Area | Decision | Detail |
|---|---|---|
| Conformance | 27 Main-profile vectors; a frame passes on per-channel RMSE and peak error against float32 `.npy` references | [#101](https://github.com/PinataLabs/ImageSharp/issues/101) |
| Architecture | 29 libjxl components in one forced chain; Modular before VarDCT | [#102](https://github.com/PinataLabs/ImageSharp/issues/102) |
| Encoder scope | Shared core plus thin lossless and lossy layers; Fastest ≈ e3, Default ≈ e5, Thorough ≈ e7, all without patches | [#103](https://github.com/PinataLabs/ImageSharp/issues/103) |
| Reference tools | Magick.NET (libjxl 0.12) in-process on every leg; libjxl v0.12.0 static tools in one opt-in job | [#104](https://github.com/PinataLabs/ImageSharp/issues/104) |
| Decoded colour | XYB + ICC decodes to gamma-encoded sRGB; the pixel profile only ever describes the pixels; enum spaces other than sRGB get a profile built through `IccWriter` | [#105](https://github.com/PinataLabs/ImageSharp/issues/105), [ADR 0002](../adr/0002-jxl-xyb-icc-decodes-to-srgb.md) |
| Test data | Inputs and `test.json` committed under LFS; `.npy` references fetched by sha256 into a gitignored cache; attribution in a README per directory plus a notices entry | [#106](https://github.com/PinataLabs/ImageSharp/issues/106) |
| Encoder verification | Magick plus our own decoder for validity; bit-exact lossless; managed SSIMULACRA2 against a committed cjxl calibration table | [#107](https://github.com/PinataLabs/ImageSharp/issues/107) |
| Public API | WebP-style types; the decoder exposes `IgnoreMetadata` and `MaxFrames`; the encoder exposes `Distance` and `JxlEffort` only | [#108](https://github.com/PinataLabs/ImageSharp/issues/108), [ADR 0003](../adr/0003-jxl-encoder-takes-distance-not-quality.md) |
| Extra channels | Straight alpha; CMYK converted naively; spot colours rendered; other channels described in `JxlMetadata` but dropped; internal float-plane seam for conformance | [#111](https://github.com/PinataLabs/ImageSharp/issues/111) |
| Hostile input | Fixed 2^28-pixel cap per frame; libjxl's structural bounds ported verbatim; only three exception types may escape | [#112](https://github.com/PinataLabs/ImageSharp/issues/112) |
| Pipeline and memory | Serial, full-frame simple pipeline; one `Buffer2D` per channel; stages in place or ping-pong; the final stage is a swappable sink | [#113](https://github.com/PinataLabs/ImageSharp/issues/113) |
| Numerics | libjxl's fast-math approximations ported verbatim; no FMA in scalar code, so output is bit-identical on every platform | [#114](https://github.com/PinataLabs/ImageSharp/issues/114), [ADR 0004](../adr/0004-jxl-maths-matches-libjxl-bit-exact.md) |
| Encoder input colour | The pixel profile is the only colour input; lossy needs no profile or one recognised analytically, else it throws; lossless embeds the ICC verbatim | [#115](https://github.com/PinataLabs/ImageSharp/issues/115) |

### Out of scope

- JPEG bitstream reconstruction (`jbrd`) on decode, and lossless JPEG → JXL transcoding.
- Animation encoding.
- Encoder e1 fast-lossless, patches and dots, butteraugli iteration, and e8+.
- Incremental or streaming partial-image decode.
- A general ICC colour-management engine.
- Matching `cjxl` on compression ratio.
- Performance targets, and a native libjxl wrapper.
- Non-XYB lossy encoding for unrecognised ICC profiles.
- Parallel decode and encode. The group loop keeps a seam for it.
- Public access to extra-channel data.

## Delivery

### Merge strategy

Merge each slice to `main` on its own. Every JXL type stays `internal` until its release slice. Tests reach the types through the existing `InternalsVisibleTo`. Two release slices make the codec public:
- **D15** for the decoder: it makes the types public and adds `JxlConfigurationModule` to `Configuration.CreateDefaultInstance()`.
- **E6** for the encoder: it makes the encoder public and adds `SaveAsJxl` / `SaveAsJxlAsync`.

### Order

1. **T1 → D1**, then a **re-estimation gate.** D1 records three measurements: the C#:C++ line ratio, the hours per 1,000 lines, and whether the general MA-tree path is fast enough without libjxl's fast paths. Use them to update the size column below before raising issues for later slices. Done: see the [outcome](#re-estimation-gate-outcome).
2. **D2 to D15** in order.
3. **The encoder track** (T2, E1 to E6) can start any time after D4, in parallel with the decoder. It needs the shared core, headers and colour types. F1 comes last.

### Re-estimation gate: D1 measurements

Measured on D1 ([#127](https://github.com/PinataLabs/ImageSharp/issues/127)), ported from libjxl `ef67fde2`. Lines are counted as in the decoder research: non-blank lines that are not only a comment (C++ `//`; C# `//` and `///`).

| Measurement | Result |
|---|---|
| C#:C++ line ratio | **0.72**: about 5,150 C# lines from about 7,150 C++ lines actually ported. Against the whole libjxl files that D1 touches (9,081 lines, close to the slice table's 9.2K estimate), the ratio is 0.61. Another 357 C# lines are ImageSharp plumbing with no libjxl source (format, detector, decoder, sinks). |
| Hours per 1,000 libjxl lines | **About 0.17 hours.** The port, its tests and these measurements took about 1.2 hours of wall-clock time, done by an AI agent (Claude Code). This is not a human rate, so scale it before using it to plan work by people. |
| General MA-tree path speed | **Acceptable.** `delta_palette` (555 × 751, 8-bit RGB with a delta palette) decodes in a median of 70 ms (68 ms minimum, over 30 runs after 3 warm-ups) through `JxlDecoder.Decode<Rgb24>`: Release build, net10.0, AMD Ryzen 9 5950X, Windows 11. Magick.NET 15.0.0, which calls native libjxl 0.12 with its fast paths, takes a median of 41 ms on the same bytes. So the general path costs about 1.7 times as much. Correctness work does not need the four fast paths; add them when performance matters. |

The 7,150 ported lines leave out these parts of the touched files: the writer-side field visitors, LZ77 and checkpoints in the symbol reader, the four Modular fast paths and their lookup tables, encoder-only predictor helpers, the VarDCT and JPEG branches of the frame and Modular glue, ICC synthesis, and debug strings.

### Re-estimation gate: outcome

Applied to the slice tables below after D1.

- **The C++ sizes hold.** The libjxl files that D1 touches total 9,081 lines against the estimate of 9.2K, so the research counts are kept, with the corrections below.
- **Work that D1 deferred moves to the slice that needs it:**
  - LZ77 (about 0.15K) is already in D2.
  - The ICC codec and the container are already in D4.
  - The VarDCT branches of the Modular and frame glue (about 0.45K: VarDCT DC, AC metadata, raw quant tables, AC global) are added to D8.
  - The TOC permutation and the progressive section scheduling (about 0.15K) are added to D13.
  - The reference-frame bookkeeping (about 0.05K) is added to D5.
- **No slice is added for the four Modular fast paths** (about 0.35K). The general path is fast enough, and the PRD has no performance target.
- **Totals.** The rest of the decoder (D2 to D15) is about 19.0K C++ lines: about 13.6K C# and about 3.2 agent hours. The encoder track (T2, E1 to E6) is about 12.8K C++ lines: about 9.2K C# and about 2.2 agent hours.
- **Done-checks are checked against the vector headers.** Four vectors need a feature from a later slice than the one that names them:
  - `lossless_pfm` (D2) is in a container. D2 takes the minimal container read for one `jxlc` box (about 0.05K) from D4. `jxlp`, `brob` and 64-bit sizes stay in D4.
  - `grayscale_public_university` (D3) uses Gaborish and EPF. It moves to D9. D3 is checked with a generated greyscale squeeze file with no loop filters.
  - `alpha_premultiplied` is VarDCT, so it cannot check the D2 un-premultiply. D2 uses a generated lossless premultiplied file.
  - `bike` (D9) is XYB with the BT.709 transfer function, which XYB output gets in D11. It moves to D11, and the test records the `NotSupportedException`. Found at D9's done-check: with a trial BT.709 stage, it matched `djxl` within a peak error of 1.2e-5, so it needs nothing else.
- **Treat the hour estimates as a floor.** D1 passed its conformance vector on the first run, because its integer code is exact. The float slices (D8, D9, D11, D12, E3 to E5) must match references within tight thresholds, and debugging a numeric mismatch can take longer than the port. Plan about twice the estimate for them.

### Rules for every slice

- **Conformance:** the named vectors pass at their Level 10 thresholds, and at Level 5 for any `_5` copies, in the `jxl-oracle` job.
- **Bounds:** the slice ports the libjxl bounds for everything it parses ([#112](https://github.com/PinataLabs/ImageSharp/issues/112)), and tests show that broken input raises `InvalidImageContentException`.
- **Attribution:** every ported file names its libjxl source. Any deliberate departure from libjxl is commented with the reason ([ADR 0001](../adr/0001-port-jpeg-xl-from-libjxl.md)).
- **Generated test files:** for features with no conformance vector, generate small files with `cjxl` v0.12.0, make their references with `djxl`, and commit the commands next to them ([#106](https://github.com/PinataLabs/ImageSharp/issues/106)).
  - Custom upsampling weights: `cjxl` writes them only with `--resampling=N --already_downsampled --upsampling_mode=0|1`. It ignores `--upsampling_mode` without `--already_downsampled`. Mode 0 is nearest neighbour, so a lossless file (`-d 0`) gives a bit-exact upsampling test.
- **Tolerance for lossy output:** VarDCT and XYB output cannot match `djxl` bit for bit, because libjxl's IDCT, XYB and sRGB code use FMA on AVX2 and [ADR 0004](../adr/0004-jxl-maths-matches-libjxl-bit-exact.md) forbids FMA in our scalar code. Compare the planes with `djxl`'s float32 `.npy` output at the lossy conformance thresholds, 1e-4 RMSE and 4e-3 peak per channel, and record the measured error in each test. Compare `Image<TPixel>` output within one quantisation step of the clamped reference.
  - JPEG transcodes are 10 times tighter. The `test.json` of `cafe`, `grayscale_jpeg` and `bench_oriented_brg` allows 1e-5 RMSE and 4e-3 peak. The generated JPEG transcodes use the same limits (`JxlLossyComparer.JpegRmsError`).

### D10 done-check notes

D10 ([#247](https://github.com/PinataLabs/ImageSharp/issues/247)) passed its done-check ([#253](https://github.com/PinataLabs/ImageSharp/issues/253)). Every vector passes at Level 10, and its `_5` copy passes at Level 5. None needs a later slice. The table gives the worst channel's error against the reference, measured on x64. djxl v0.12.0 (AVX2) is measured against the same reference.

| Vector | Threshold (RMSE / peak) | This decoder | djxl v0.12.0 |
|---|---|---|---|
| `upsampling` | 1e-4 / 4e-3 | 1.3e-6 / 4.7e-5 | 9.2e-7 / 4.2e-5 |
| `upsampling_5` (its own reference) | 2e-2 / 6e-2 | 2.6e-4 / 1.6e-2 | 2.6e-4 / 1.6e-2 |
| `cafe`, `cafe_5` (fetched) | 1e-5 / 4e-3 | 4.6e-7 / 5.0e-6 | 2.2e-8 / 1.2e-7 |
| `grayscale_jpeg`, `grayscale_jpeg_5` | 1e-5 / 4e-3 | 1.3e-7 / 1.8e-6 | 0 / 0 |
| `bench_oriented_brg`, `bench_oriented_brg_5` | 1e-5 / 4e-3 | 2.7e-7 / 1.9e-6 | 2.3e-8 / 1.2e-7 |

The `_5` copies of the JPEG vectors use the Level 10 reference with the Level 5 limits (2e-2 / 6e-2). `Image.Load` gives `Rgba32`, `Rgb24`, `L8` and `Rgb24`. Each result is within 0.51 of one 8-bit step of the clamped reference.

- **D8 had already ported most of chroma subsampling:** the subsampled DC, the AC-metadata bound and the subsampled group decode. D10's chroma work was the chroma-upsampling stage in the pipeline, and tests of the D8 path.
- **Grey images with a YCbCr frame:** libjxl keeps three planes through the YCbCr stage and outputs R. So the drop to one grey plane comes after the colour transform. Grey reference slots hold one plane. So a grey YCbCr frame that is saved before its colour transform, or that has patches, throws `NotSupportedException`. This is a commented departure from libjxl. JPEG transcodes never write such a frame.
- **Subsampled VarDCT without the skip-adaptive-DC-smoothing flag:** djxl v0.12.0 rejects such a frame with "Non-444 chroma subsampling is not allowed when adaptive DC smoothing is enabled". libjxl commit `05f95ed1` removed that check, and `ef67fde2` includes it. So this decoder decodes the frame, as `ef67fde2` does. In that case the smoothing reads the chroma DC planes past their subsampled size. libjxl leaves that memory uninitialised, but this port's planes are allocated clean, so it reads zeros. `cjxl` never writes such a frame, and no test file checks the path.
- **Two ported bounds cannot be reached from a stream:**
  - "Frame dimensions are too big for the image." (`dec_group.cc`). The groups are counted from the frame's pixel size, which is at most 8 × `xsize_blocks`. A chroma shift only moves a group's corners towards the origin. So a group's blocks always lie inside the DC planes. The proof is in the remarks of `JxlVarDctBoundsTests`. The test decodes subsampled frames of several groups at the bound.
  - The Modular "Dimension mismatch" with shifted extra channels. Both sizes come from the frame header, so they always match. `JxlBoundsTests` tests the side at the bound.

## Slices

The size column is a rough count of libjxl C++ lines, taken from the [decoder](https://github.com/PinataLabs/ImageSharp/issues/102) and [encoder](https://github.com/PinataLabs/ImageSharp/issues/103) research and corrected at the [re-estimation gate](#re-estimation-gate-outcome). The C# column is the size times 0.72, and the agent-hours column is the size times 0.17 hours per 1,000 lines, both measured on D1. Agent hours are wall-clock time for an AI agent, not a human rate.

### Test infrastructure

| # | Ships | Done-check | Size | C# (est.) | Agent hours (est.) |
|---|---|---|---|---|---|
| T1 ([#126](https://github.com/PinataLabs/ImageSharp/issues/126)) | `*.jxl` LFS pattern; conformance inputs, `test.json` files, READMEs and notices entry; `.npy` reader; sha256 fetch cache with `JXL_CONFORMANCE=1`; `jxl-oracle` CI job skeleton; `MagickReferenceDecoder` widened to 16-bit for JXL | Harness self-test: **Magick's** decode of `delta_palette` passes its thresholds | test code only | — | — |
| T2 | Managed SSIMULACRA2 port; cjxl calibration table (about 6 images × d{1,2,4} × 3 levels) with its generating command | Within 0.5 of the libjxl `ssimulacra2` binary in `jxl-oracle`; table committed | ≈ 0.6K | ≈ 0.43K | ≈ 0.1 |

### Decoder

| # | Ships | Done-check | Size | C# (est.) | Agent hours (est.) |
|---|---|---|---|---|---|
| D1 ([#127](https://github.com/PinataLabs/ImageSharp/issues/127)) | **Porting spike, first real slice:** bit reader, fields, image headers, entropy (prefix, ANS, hybrid uint, context map; no LZ77), Modular general path, RCT and palette (including delta), single-frame frame header and TOC, simple pipeline with `PlanesSink` and `ImageFrameSink<TPixel>`, `Identify` | `delta_palette`; measurements recorded in this file | 7.15K ported (9.2K estimated; 1.9K deferred, see the gate) | 5.15K (actual) | 1.2 (actual) |
| D2 ([#152](https://github.com/PinataLabs/ImageSharp/issues/152), [#153](https://github.com/PinataLabs/ImageSharp/issues/153), [#154](https://github.com/PinataLabs/ImageSharp/issues/154), [#155](https://github.com/PinataLabs/ImageSharp/issues/155), [#156](https://github.com/PinataLabs/ImageSharp/issues/156)) | Alpha extra channel, 9–16-bit and float32 samples, LZ77, un-premultiply in the sink, pixel-type choice for the non-generic `Decode`; the minimal container read for one `jxlc` box | `alpha_nonpremultiplied`, `alpha_triangles`, `lossless_pfm` (bit-exact), `lz77_flower`; a `cjxl`-made lossless premultiplied file | ≈ 0.85K (0.8K + 0.05K `jxlc` read from D4) | ≈ 0.61K | ≈ 0.1 |
| D3 ([#158](https://github.com/PinataLabs/ImageSharp/issues/158), [#159](https://github.com/PinataLabs/ImageSharp/issues/159)) | Squeeze; greyscale output | A `cjxl`-made lossless greyscale file that uses squeeze and no loop filters | ≈ 0.5K | ≈ 0.36K | ≈ 0.1 |
| D4 ([#163](https://github.com/PinataLabs/ImageSharp/issues/163), [#165](https://github.com/PinataLabs/ImageSharp/issues/165), [#164](https://github.com/PinataLabs/ImageSharp/issues/164), [#166](https://github.com/PinataLabs/ImageSharp/issues/166)) | Container and box parsing (`jxlp`, `brob`, 64-bit sizes; D2 reads one `jxlc` box); ICC codec; Exif/XMP mapped to ImageSharp profiles; pixel-profile rules; enum → ICC builder; `JxlMetadata` colour fields | `OriginalIccProfile` bytes equal `original.icc` for every ICC vector (via `Identify`); `square-extended-size-container`; `1x1_exif_xmp` | ≈ 2.15K (2.2K − 0.05K `jxlc` read moved to D2) | ≈ 1.55K | ≈ 0.4 |
| D5 ([#189](https://github.com/PinataLabs/ImageSharp/issues/189), [#190](https://github.com/PinataLabs/ImageSharp/issues/190), [#191](https://github.com/PinataLabs/ImageSharp/issues/191)) | Multi-frame: blending, layers, reference frames, coalescing, animation, orientation, `JxlFrameMetadata`, `MaxFrames` | `sunset_logo`, `blendmodes`, `mul_no_extra_channels`, `animation_newtons_cradle`, `cropped_traffic_light` | ≈ 2.15K (2.1K + 0.05K reference bookkeeping from D1) | ≈ 1.55K | ≈ 0.4 |
| D6 ([#194](https://github.com/PinataLabs/ImageSharp/issues/194), [#195](https://github.com/PinataLabs/ImageSharp/issues/195), [#196](https://github.com/PinataLabs/ImageSharp/issues/196), [#197](https://github.com/PinataLabs/ImageSharp/issues/197)) | Spot-colour rendering, CMYK naive conversion, `JxlMetadata.ExtraChannels` | `spot`, `cmyk_layers` | ≈ 0.3K | ≈ 0.22K | ≈ 0.1 |
| D7 ([#216](https://github.com/PinataLabs/ImageSharp/issues/216), [#217](https://github.com/PinataLabs/ImageSharp/issues/217), [#218](https://github.com/PinataLabs/ImageSharp/issues/218), [#219](https://github.com/PinataLabs/ImageSharp/issues/219)) | Patches on Modular frames | `patches_lossless` | ≈ 0.5K | ≈ 0.36K | ≈ 0.1 |
| D8 ([#223](https://github.com/PinataLabs/ImageSharp/issues/223), [#224](https://github.com/PinataLabs/ImageSharp/issues/224), [#225](https://github.com/PinataLabs/ImageSharp/issues/225), [#226](https://github.com/PinataLabs/ImageSharp/issues/226), [#227](https://github.com/PinataLabs/ImageSharp/issues/227), [#228](https://github.com/PinataLabs/ImageSharp/issues/228)) | VarDCT core: DCT8, quantiser and DC, chroma-from-luma and per-block quant fields (decoded and applied), coefficient orders, VarDCT group decode, XYB → sRGB on Modular and VarDCT frames, `JxlFastMath` | `bicycles` (Modular with XYB); `benchmark_xl` VarDCT files without XYB, and `cjxl -e 2 --gaborish=0 --epf=0` files, with `djxl` references; `JxlFastMath` unit tests within libjxl's error bounds | ≈ 4.7K (4.3K + 0.45K VarDCT glue from D1 − 0.05K raw quant tables moved to D10) | ≈ 3.38K | ≈ 0.8 |
| D9 ([#239](https://github.com/PinataLabs/ImageSharp/issues/239), [#240](https://github.com/PinataLabs/ImageSharp/issues/240), [#241](https://github.com/PinataLabs/ImageSharp/issues/241), [#242](https://github.com/PinataLabs/ImageSharp/issues/242), [#243](https://github.com/PinataLabs/ImageSharp/issues/243), [#244](https://github.com/PinataLabs/ImageSharp/issues/244), [#245](https://github.com/PinataLabs/ImageSharp/issues/245)) | All transform sizes and AFV, Gaborish, EPF; tests with adaptive quant fields and non-default chroma-from-luma maps (both decoded since D8) | `opsin_inverse`, `grayscale` (linear at the seam), `alpha_premultiplied`, `animation_icos4d`, `grayscale_public_university` (Modular with Gaborish and EPF); `vardct_xyb_e7` (`cjxl -d 1` at the default effort) with a `djxl` reference. `bike` moved to D11 | ≈ 3.0K | ≈ 2.16K | ≈ 0.5 |
| D10 ([#248](https://github.com/PinataLabs/ImageSharp/issues/248), [#249](https://github.com/PinataLabs/ImageSharp/issues/249), [#250](https://github.com/PinataLabs/ImageSharp/issues/250), [#251](https://github.com/PinataLabs/ImageSharp/issues/251), [#252](https://github.com/PinataLabs/ImageSharp/issues/252), [#253](https://github.com/PinataLabs/ImageSharp/issues/253)) | Upsampling (2×, 4×, 8×, default and custom weights; colour, and early and late extra channels), chroma upsampling (any colour channel, Y included), YCbCr on Modular and VarDCT frames, raw quant tables (only JPEG-transcoded files use them). D8 had already ported the subsampled VarDCT decode, so D10's chroma work is the pipeline stage. A grey YCbCr frame that is saved before its colour transform, or that has patches, throws (see the [D10 notes](#d10-done-check-notes)) | `upsampling`, `cafe` (fetched), `grayscale_jpeg`, `bench_oriented_brg`, and their `_5` copies; `cjxl` upsampling files and JPEG transcodes (4:4:4, 4:2:0, 4:2:2, 4:4:0, grey, luma-subsampled), and `jxl_from_tree` Modular YCbCr files, with `djxl` references | ≈ 0.65K (0.6K + 0.05K raw quant tables from D8) | ≈ 0.47K | ≈ 0.1 |
| D11 | All transfer functions (709, DCI, gamma, PQ, HLG) and their ICC tables | `pq_gradient`; `bike` (fetched; XYB with BT.709, moved from D9); a `cjxl`-made HLG file | ≈ 1.6K | ≈ 1.15K | ≈ 0.3 |
| D12 | Patches on VarDCT frames, splines, noise | `patches`, `animation_spline`, `noise`, libjxl `splines.jxl` and `spline_on_first_frame.jxl` | ≈ 1.4K | ≈ 1.01K | ≈ 0.2 |
| D13 | Progressive: passes, LF/DC frames, TOC permutation | `progressive` (fetched) | ≈ 1.15K (1.0K + 0.15K TOC permutation and section scheduling from D1) | ≈ 0.83K | ≈ 0.2 |
| D14 | Hardening sweep: OSS-Fuzz regression theory, bounds audit, pixel-cap and cancellation tests | All 44 OSS-Fuzz inputs decode or throw an allowed exception within 10 s | small | — | — |
| D15 | **Decoder release:** types public, default configuration, parse-only tests for features with no pixel reference | All 27 vectors pass in `jxl-oracle`; Magick cross-check on every leg | small | — | — |

### Encoder

| # | Ships | Done-check | Size | C# (est.) | Agent hours (est.) |
|---|---|---|---|---|---|
| E1 ([#207](https://github.com/PinataLabs/ImageSharp/issues/207), [#208](https://github.com/PinataLabs/ImageSharp/issues/208), [#209](https://github.com/PinataLabs/ImageSharp/issues/209), [#210](https://github.com/PinataLabs/ImageSharp/issues/210), [#211](https://github.com/PinataLabs/ImageSharp/issues/211), [#212](https://github.com/PinataLabs/ImageSharp/issues/212), [#213](https://github.com/PinataLabs/ImageSharp/issues/213), [#214](https://github.com/PinataLabs/ImageSharp/issues/214)) | Encoder core: bit writer, `Write` field visitor, ANS and prefix writers, context-map writer, Modular with predefined trees, frame and TOC assembly; lossless Fastest (fixed WP tree, global palette, YCoCg); ICC embedded verbatim | Bit-exact round-trip on the lossless corpus through both decoders; `djxl` accepts the output | ≈ 4.9K | ≈ 3.53K | ≈ 0.8 |
| E2 ([#231](https://github.com/PinataLabs/ImageSharp/issues/231), [#232](https://github.com/PinataLabs/ImageSharp/issues/232), [#233](https://github.com/PinataLabs/ImageSharp/issues/233), [#234](https://github.com/PinataLabs/ImageSharp/issues/234), [#235](https://github.com/PinataLabs/ImageSharp/issues/235)) | Lossless Default and Thorough: MA-tree learning, RCT search, local palette, RLE LZ77, e7 parameters and `EstimateCost` | Bit-exact at all three levels | ≈ 1.6K | ≈ 1.15K | ≈ 0.3 |
| E3 | Lossy Fastest: `TryFromIccProfile` and the colour rules, forward XYB, DCT8, constant quant field, AC tokens, DC/AC-metadata streams, alpha with the predefined tree | Fastest rows of the calibration table; colour-rule tests (an unrecognised ICC throws, a mismatched profile is ignored) | ≈ 1.8K | ≈ 1.3K | ≈ 0.3 |
| E4 | Lossy Default: adaptive quantisation, AC-strategy search with all transforms, chroma-from-luma, Gaborish, coefficient reordering, near-lossless DC | Default rows of the calibration table | ≈ 3.5K | ≈ 2.52K | ≈ 0.6 |
| E5 | Lossy Thorough: full AC-strategy search, dynamic EPF sharpness | Thorough rows of the calibration table | ≈ 0.4K | ≈ 0.29K | ≈ 0.1 |
| E6 | **Encoder release:** `JxlEncoder` public, `SaveAsJxl` / `SaveAsJxlAsync`, Exif/XMP boxes in a container | Public-API tests; metadata round-trip through our decoder and Magick | small | — | — |

### Hardening

| # | Ships | Done-check | Size | C# (est.) | Agent hours (est.) |
|---|---|---|---|---|---|
| F1 | SharpFuzz harness `tests/ImageSharp.Fuzz`, seeded with the conformance and libjxl inputs; runs locally only | One hour with no new failure; each crash it finds is committed as `tests/Images/Input/Jxl/issues/fuzz/IssueNNN-<Exception>.jxl` | test code only | — | — |
