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
| Public API | WebP-style types; the decoder exposes `IgnoreMetadata` and `MaxFrames`; the encoder exposes `Distance` and `JxlEffort` only. E6 refined the encoder: `Distance` is in [0, 25] (0 is lossless, and values in (0, 0.05) are clamped to 0.05, as libjxl's `kMinButteraugliDistance`; this replaces "0.1–25"); the output is a container only when it has Exif or XMP to store or needs level 10; the Exif orientation becomes the codestream orientation, as cjxl's `InterpretExif` | [#108](https://github.com/PinataLabs/ImageSharp/issues/108), [ADR 0003](../adr/0003-jxl-encoder-takes-distance-not-quality.md), [E6 notes](#e6-done-check-notes) |
| Extra channels | Straight alpha; CMYK converted naively; spot colours rendered; other channels described in `JxlMetadata` but dropped; internal float-plane seam for conformance | [#111](https://github.com/PinataLabs/ImageSharp/issues/111) |
| Hostile input | Fixed 2^28-pixel cap per frame; libjxl's structural bounds ported verbatim; only three exception types may escape | [#112](https://github.com/PinataLabs/ImageSharp/issues/112) |
| Pipeline and memory | Serial, full-frame simple pipeline; one `Buffer2D` per channel; stages in place or ping-pong; the final stage is a swappable sink | [#113](https://github.com/PinataLabs/ImageSharp/issues/113) |
| Numerics | libjxl's fast-math approximations ported verbatim; no FMA in scalar code, so output is bit-identical on every platform | [#114](https://github.com/PinataLabs/ImageSharp/issues/114), [ADR 0004](../adr/0004-jxl-maths-matches-libjxl-bit-exact.md) |
| Encoder input colour | The pixel profile is the only colour input; lossy needs no profile or one recognised analytically, else it throws; lossless embeds the ICC verbatim | [#115](https://github.com/PinataLabs/ImageSharp/issues/115) |

### Out of scope

- JPEG bitstream reconstruction (`jbrd`) on decode, and lossless JPEG → JXL transcoding.
- Animation encoding.
- `brob` compression of the Exif and XMP boxes on encode. E6 writes them as plain boxes on both targets (see the [E6 notes](#e6-done-check-notes)).
- Encoder e1 fast-lossless, patches and dots (E5 skips both as one departure; splines are libjxl v0.12's own no-op, ported as such), butteraugli iteration, and e8+.
- Incremental or streaming partial-image decode.
- A general ICC colour-management engine.
- Matching `cjxl` on compression ratio.
- Performance targets, and a native libjxl wrapper.
- Non-XYB lossy encoding for unrecognised ICC profiles.
- Parallel decode and encode. The group loop keeps a seam for it.
- Public access to extra-channel data.

## Delivery

### Merge strategy

Merge each slice to `main` on its own. A JXL type stays `internal` until its release slice. Tests reach the internal types through the existing `InternalsVisibleTo`. Two release slices make the codec public:
- **D15** for the decoder (done; see the [D15 notes](#d15-done-check-notes)). The decoder surface is public: `JxlFormat`, `JxlDecoder`, `JxlImageFormatDetector`, `JxlMetadata`, `JxlFrameMetadata`, `JxlExtraChannel`, the colour description `JxlColorEncoding`, their enums, `JxlConfigurationModule`, and `GetJxlMetadata()` / `GetJxlFrameMetadata()`. `JxlConfigurationModule` is in `Configuration.CreateDefaultInstance()` and registers the decoder and the detector only. `IJxlDecoderOptions` and every codec type stay `internal`, as WebP's options interface does.
- **E6** for the encoder (done; see the [E6 notes](#e6-done-check-notes)). The encoder surface is public: `JxlEncoder`, with `Distance`, `Effort`, `Encode` and `EncodeAsync`, and `JxlEffort`. The source generator adds the 9 `SaveAsJxl` / `SaveAsJxlAsync` overloads. `JxlConfigurationModule` now registers the encoder too, so `image.Save("x.jxl")` writes JPEG XL with the defaults. `IJxlEncoderOptions`, `JxlSpeedTier`, the encoder's test hooks and every codec type stay `internal`.

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
  - The TOC permutation and the progressive section scheduling (about 0.15K) are added to D13, which ported them (see the [D13 notes](#d13-done-check-notes)).
  - The reference-frame bookkeeping (about 0.05K) is added to D5.
- **No slice is added for the four Modular fast paths** (about 0.35K). The general path is fast enough, and the PRD has no performance target.
- **Totals.** The rest of the decoder (D2 to D15) is about 19.0K C++ lines: about 13.6K C# and about 3.2 agent hours. The encoder track (T2, E1 to E6) is about 12.8K C++ lines: about 9.2K C# and about 2.2 agent hours.
- **Done-checks are checked against the vector headers.** Four vectors need a feature from a later slice than the one that names them:
  - `lossless_pfm` (D2) is in a container. D2 takes the minimal container read for one `jxlc` box (about 0.05K) from D4. `jxlp`, `brob` and 64-bit sizes stay in D4.
  - `grayscale_public_university` (D3) uses Gaborish and EPF. It moves to D9. D3 is checked with a generated greyscale squeeze file with no loop filters.
  - `alpha_premultiplied` is VarDCT, so it cannot check the D2 un-premultiply. D2 uses a generated lossless premultiplied file.
  - `bike` (D9) is XYB with the BT.709 transfer function, which XYB output gets in D11. It moved to D11, and the test recorded the `NotSupportedException`. D9's done-check found that with a trial BT.709 stage it matched `djxl` within a peak error of 1.2e-5, and said it needed nothing else. That was wrong: its VarDCT frame also has patches, which come with D12. D11a's BT.709 stage uncovered the patches check, so `bike` and `bike_5` moved to D12 (see the [D11 notes](#d11-done-check-notes)), which decodes them (see the [D12 notes](#d12-done-check-notes)).
- **Treat the hour estimates as a floor.** D1 passed its conformance vector on the first run, because its integer code is exact. The float slices (D8, D9, D11, D12, E3 to E5) must match references within tight thresholds, and debugging a numeric mismatch can take longer than the port. Plan about twice the estimate for them.

### Rules for every slice

- **Conformance:** the named vectors pass at their Level 10 thresholds, and at Level 5 for any `_5` copies, in the `jxl-oracle` job.
- **Bounds:** the slice ports the libjxl bounds for everything it parses ([#112](https://github.com/PinataLabs/ImageSharp/issues/112)), and tests show that broken input raises `InvalidImageContentException`.
- **Attribution:** every ported file names its libjxl source. Any deliberate departure from libjxl is commented with the reason ([ADR 0001](../adr/0001-port-jpeg-xl-from-libjxl.md)).
- **Generated test files:** for features with no conformance vector, generate small files with `cjxl` v0.12.0, make their references with `djxl`, and commit the commands next to them ([#106](https://github.com/PinataLabs/ImageSharp/issues/106)).
  - Custom upsampling weights: `cjxl` writes them only with `--resampling=N --already_downsampled --upsampling_mode=0|1`. It ignores `--upsampling_mode` without `--already_downsampled`. Mode 0 is nearest neighbour, so a lossless file (`-d 0`) gives a bit-exact upsampling test.
- **Tolerance for lossy output:** VarDCT and XYB output cannot match `djxl` bit for bit, because libjxl's IDCT, XYB and sRGB code use FMA on AVX2 and [ADR 0004](../adr/0004-jxl-maths-matches-libjxl-bit-exact.md) forbids FMA in our scalar code. Compare the planes with `djxl`'s float32 `.npy` output at the lossy conformance thresholds, 1e-4 RMSE and 4e-3 peak per channel, and record the measured error in each test. Compare `Image<TPixel>` output within one quantisation step of the clamped reference.
  - One exception: 16-bit output in PQ or HLG. Both curves are steep near black, so they magnify the FMA differences of the linear samples to more than one 16-bit step there. D11 checks those images against the decoder's own clamped planes (PQ), or within one step only above the shadows (HLG), and against `djxl` at the lossy peak; the [D11 done-check notes](#d11-done-check-notes) give the numbers. 8-bit PQ and HLG output still keeps the one-step rule.
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
- **Grey images with a YCbCr frame:** libjxl keeps three planes through the YCbCr stage and outputs R. So the drop to one grey plane comes after the colour transform. Since D14b, a grey YCbCr frame also keeps three planes through its patches and its save before the colour transform, as libjxl does. Since the D14 review, every grey frame saved before its colour transform keeps three planes in its slot (see the [D14 notes](#d14-done-check-notes)).
- **Subsampled VarDCT without the skip-adaptive-DC-smoothing flag:** djxl v0.12.0 rejects such a frame with "Non-444 chroma subsampling is not allowed when adaptive DC smoothing is enabled". libjxl commit `05f95ed1` removed that check, and `ef67fde2` includes it. So this decoder decodes the frame, as `ef67fde2` does. In that case the smoothing reads the chroma DC planes past their subsampled size. libjxl leaves that memory uninitialised, but this port's planes are allocated clean, so it reads zeros. `cjxl` never writes such a frame, and no test file checks the path.
- **Two ported bounds cannot be reached from a stream:**
  - "Frame dimensions are too big for the image." (`dec_group.cc`). The groups are counted from the frame's pixel size, which is at most 8 × `xsize_blocks`. A chroma shift only moves a group's corners towards the origin. So a group's blocks always lie inside the DC planes. The proof is in the remarks of `JxlVarDctBoundsTests`. The test decodes subsampled frames of several groups at the bound.
  - The Modular "Dimension mismatch" with shifted extra channels. Both sizes come from the frame header, so they always match. `JxlBoundsTests` tests the side at the bound.

### D11 done-check notes

D11 ([#255](https://github.com/PinataLabs/ImageSharp/issues/255)) passed its done-check ([#262](https://github.com/PinataLabs/ImageSharp/issues/262)). `pq_gradient` and the `cjxl`-made transfer-function files pass. `bike` and `bike_5` need patches on VarDCT frames, so they move to D12. No vector showed a mismatch in the D11 code. The table gives the worst channel's error, measured on x64. The generated files' references are `djxl` v0.12.0's own output, so `djxl`'s error against them is 0. For `bike`, djxl v0.12.0 (AVX2) is measured against the conformance reference.

| Vector | Threshold (RMSE / peak) | This decoder | djxl v0.12.0 |
|---|---|---|---|
| `pq_gradient` (libjxl test data; PQ, 16-bit grey, lossless) | exact | 0 / 0 | 0 / 0 (its `.npy` is the reference) |
| `vardct_xyb_hlg` (the `cjxl`-made HLG file) | 1e-4 / 4e-3 | 3.5e-6 / 1.7e-4 | reference |
| `vardct_xyb_grey_hlg` | 1e-4 / 4e-3 | 1.8e-7 / 2.5e-6 | reference |
| `vardct_xyb_pq` (10,000 nits) | 1e-4 / 4e-3 | 2.1e-5 / 7.1e-4 | reference |
| `vardct_xyb_pq_1000` (1,000 nits) | 1e-4 / 4e-3 | 3.1e-5 / 1.3e-3 | reference |
| `vardct_xyb_bt709` | 1e-4 / 4e-3 | 6.9e-7 / 6.0e-6 | reference |
| `vardct_xyb_dci` | 1e-4 / 4e-3 | 2.7e-6 / 1.1e-4 | reference |
| `xyb_modular_gamma22` | 1e-4 / 4e-3 | 5.8e-7 / 6.6e-6 | reference |
| `bike`, `bike_5` (fetched) | 1e-4 / 7e-3; 2e-2 / 6e-2 | Not decoded at D11: patches on VarDCT frames came with D12 (see the [D12 notes](#d12-done-check-notes)) | 3.0e-7 / 6.3e-6 |

`Image.Load` gives `L16` for `pq_gradient`, equal to `djxl`'s PNG. It gives `Rgb24` for the RGB transfer-function files and `L8` for `vardct_xyb_grey_hlg`. Each `Image.Load<Rgb24>` result is within one 8-bit step of the clamped reference: 0.50, which is the rounding, for every file but the PQ ones, which are steep near black and give 0.58 (`vardct_xyb_pq`) and 0.59 (`vardct_xyb_pq_1000`). `Identify` gives each file's size, channels and transfer function (or gamma, for `xyb_modular_gamma22`), and the same pixel profile as `Image.Load`.

- **`bike` and `bike_5` move to D12.** `bike` is XYB with BT.709, which D11a decodes. Before D11a, the BT.709 error hid a second one: its VarDCT frame has patches, which `JxlFrameDecoder` rejected until D12a. At D11, `JxlConformanceDecoderTests` recorded the `NotSupportedException` for both. D12 decodes them.
- **The 16-bit criterion is relaxed for PQ and HLG.** "Within one quantisation step of the clamped reference" holds at 8 bits but not at 16 bits. PQ is very steep near black (a slope of the order of 1e3 to 1e4 below 1e-5 linear), and so is HLG's square-root segment (3 / 2E, about 360 at E = 0.004). These slopes magnify the differences of about 3e-7 in the linear samples, which come from libjxl's FMA ([ADR 0004](../adr/0004-jxl-maths-matches-libjxl-bit-exact.md)). The transfer functions are not the cause: this decoder's `TF_PQ` applied to `djxl`'s own linear output gives `djxl`'s PQ output within 3.6e-7.
  - PQ: `Image.Load<Rgb48>` is within 0.50 of one 16-bit step of this decoder's own planes, clamped. Against `djxl`'s clamped reference it is within 5.2e-4 (34 steps, `vardct_xyb_pq`) and 1.3e-3 (83 steps, `vardct_xyb_pq_1000`). The test checks one step against the planes and the lossy peak (4e-3) against `djxl`.
  - HLG: `vardct_xyb_grey_hlg` is within 0.58 of one 16-bit step everywhere. `vardct_xyb_hlg` is within 0.93 of one step where the clamped reference is at least 0.1. Below that, 14 of its 12,288 samples are more than one step away, the largest 8.1 steps at 0.0041. The test checks one step at or above 0.1 and the lossy peak below it.
- **The profiles match `djxl`'s exactly.** A no-FMA build of libjxl gives the same profiles as `djxl` v0.12.0, so the tests keep a tolerance of one step but measure 0.
  - Table curves (D11d): every entry of the 64-entry curves of `pq_gradient`, `colour_grey_hlg` and `colour_custom_pq` equals `djxl`'s (0 steps). The `curv`, `wtpt`, `chad` and colorant tags are byte-identical.
  - 3D LUT (D11e, D11f): `ToneMapPixel` gives `djxl`'s Lab bytes on the 9 × 9 × 9 grid (0 bytes differ), and every `A2B0` CLUT entry of `colour_hlg`, `colour_pq` and `colour_p3_hlg` equals `djxl`'s. The tags are `djxl`'s, in `djxl`'s order, with the same total size (4324, 4324 and 4344 bytes) and offsets. The `wtpt`, `chad`, `cicp`, colorant, `A2B0` and `B2A0` data are byte-identical. The lossy XYB files get the profile of their lossless twin, byte for byte.
  - Three differences remain, none of them in the colour. The `desc` and `cprt` tags differ in size, because libjxl counts two trailing NUL bytes in each `mluc` string; this dates from D4. With custom primaries the `desc` text also differs ("0.13999999999999999" against "0.14"). These change the header's profile ID (bytes 84 to 99). In the tag table, the `A2B0` size is 3771 here and 3772 in libjxl: libjxl counts the pad byte, and ICC says that the size leaves out padding.
- **The shared `lut8` writer was fixed.** `IccDataWriter` wrote the output channel count where an `mft1` (`lut8`) tag holds its CLUT grid-point count. The lut16 writer and the reader were right. D11f's 9 × 9 × 9 `A2B0` (3 outputs, 9 grid points) needed the fix, which writes `GridPointCount[0]`. The writer is shared ICC code, so the fix also changes the bytes of every `lut8` tag written outside JPEG XL whose grid-point count is not its output count. The writer runs only for an `IccProfile` built from tag entries; a profile read from a file keeps its own bytes. Those tags were corrupt before: they did not read back as written. A tag whose two counts are equal, like the one test `lut8` before D11, is written as before.
- **The custom-primaries example changed.** The D11d ticket's PQ file with primaries `0.68;0.32;0.265;0.69;0.15;0.06` would not test a table curve: these are P3, `cjxl` stores them as the P3 enum, and libjxl builds the 3D LUT for them (libjxl `color_encoding_cms.h`). `colour_custom_pq` uses `0.7;0.29;0.18;0.72;0.14;0.05` instead, and `colour_p3_hlg` covers the P3 case.
- **Table curve or 3D LUT.** As libjxl's default build (`kEnable3DToneMapping`), a PQ or HLG encoding that `CanToneMap` accepts gets a 9 × 9 × 9 3D LUT to PCS Lab (`A2B0`, `mft1`, tone-mapped with Rec. 2408 for PQ and the HLG OOTF for HLG, then gamut-mapped), a no-op `B2A0`, and a Lab profile connection space. `CanToneMap` accepts an RGB encoding with enumerated primaries and a D65 white point, or P3 primaries with the DCI white point: the encodings that a `cicp` tag can name. Every other PQ or HLG encoding (grey, or custom primaries) gets a 64-entry table curve. The tone-mapping block of libjxl's table-curve code is not ported: libjxl's default build never reaches it.
- **No tone-mapping or to-linear stage.** libjxl adds them to the render pipeline only when the caller's desired intensity target differs from the image's. This port has no caller-chosen target, so they would never run, and they are not ported. The desired intensity target always equals the image's.
- **The XYB colour-space profile is out of scope.** libjxl's `CreateICCLutAtoBTagForXYB` builds the profile of a colour encoding whose colour space is XYB. That is not an XYB-encoded image, and `cjxl` does not write it.
- **The 1e-3 to 1e-4 peaks of PQ and DCI are not port errors.** The DCI curve is also steep near 0 (a slope of about 460 at 1e-5). Each peak is at small encoded values and below the 4e-3 threshold.

### D12 done-check notes

D12 ([#274](https://github.com/PinataLabs/ImageSharp/issues/274)) passed its done-check ([#278](https://github.com/PinataLabs/ImageSharp/issues/278), PR [#279](https://github.com/PinataLabs/ImageSharp/pull/279)). Every D12 vector passes at Level 10, and its `_5` copy at Level 5. None needs a later slice, and no vector showed a mismatch in the D12 code, so D12d fixed nothing. At D12, `progressive` and `progressive_5` stayed in the later-slice theory of `JxlConformanceDecoderTests`: they start with a DC frame, which D13 decodes (see the [D13 notes](#d13-done-check-notes)). The table gives the worst channel's error (the worst frame for an animation), measured on x64. djxl v0.12.0 (AVX2) is measured against the same reference. The references of the libjxl test data and of the generated files are `djxl` v0.12.0's own output, so `djxl`'s error against them is 0.

| Vector | Threshold (RMSE / peak) | This decoder | djxl v0.12.0 |
|---|---|---|---|
| `patches` | 1e-4 / 4e-3 | 1.5e-7 / 6.6e-6 | 9.2e-8 / 2.7e-6 |
| `patches_5` (its own reference) | 2e-2 / 6e-2 | 2.6e-4 / 2.4e-2 | 2.6e-4 / 2.4e-2 |
| `bike`, `bike_5` (fetched) | 1e-4 / 7e-3; 2e-2 / 6e-2 | 4.7e-7 / 2.5e-4 | 3.0e-7 / 6.3e-6 |
| `animation_spline`, `animation_spline_5` (60 frames) | 1e-4 / 4e-3; 2e-2 / 6e-2 | 2.8e-7 / 1.4e-5 | 2.8e-7 / 1.4e-5 |
| `noise`, `noise_5` | 1e-4 / 4e-3; 2e-2 / 6e-2 | 5.8e-7 / 2.8e-5 | 3.8e-7 / 9.2e-6 |
| `splines.jxl` (libjxl test data; 2048 × 2048 Modular, not XYB) | 1e-4 / 4e-3 | 9.0e-8 / 2.0e-6 | reference |
| `spline_on_first_frame.jxl` (libjxl test data) | 1e-4 / 4e-3 | 6.3e-7 / 3.2e-6 | reference |
| `vardct_xyb_grey_patches` (`cjxl -d 1 --patches=1`, grey) | 1e-4 / 4e-3 | 1.4e-7 / 7.2e-7 | reference |
| `vardct_xyb_patches_noise` (`cjxl -d 1 --patches=1 --photon_noise_iso=3200`) | 1e-4 / 4e-3 | 5.8e-7 / 1.4e-5 | reference |
| `noise_resampling_2x` (`--resampling=2 --photon_noise_iso=3200`) | 1e-4 / 4e-3 | 1.4e-6 / 4.2e-5 | reference |
| `noise_animation` (4 frames, 3 shown, ISO 6400) | 1e-4 / 4e-3 | 1.5e-6 / 4.1e-5 | reference |
| `vardct_grey_noise` (D12 review; `--photon_noise_iso=3200 --disable_perceptual_optimizations`, grey, not XYB) | 4e-6 RMSE, 4e-3 peak | 1.2e-7 / 1.0e-6 | reference |
| Six hand-built spline dictionaries (`JxlSplinesTests`), worst | 1e-4 / 4e-3 | 4.6e-8 / 2.4e-7 | reference |
| Two hand-built grey Modular frames with noise, one with splines and one with patches (`JxlNoiseTests`, D12 review), worst | 4e-6 RMSE, 4e-3 peak | 1.5e-8 / 1.2e-7 | reference |

`Image.Load` gives `Rgba32` for `patches` (linear sRGB planes, sRGB pixels), and `Rgb24` for `bike`, `noise`, `animation_spline` (all 60 frames), libjxl's two spline files and the generated colour files. Each result is within 0.502 of one 8-bit step of the clamped reference, so within the rounding and the float error. `Identify` gives each vector's size, bit depth, extra channels and colour encoding, and the animation header of `animation_spline` (100 ticks a second).

- **The peak of `bike` is one sample.** R at (1586, 593) is 0.081249 here and 0.081000 in the reference. The BT.709 curve jumps between two adjacent floats at linear 0.018, from 0.08100 to 0.08124, and libjxl's `TF_709` has the same switch. The FMA differences ([ADR 0004](../adr/0004-jxl-maths-matches-libjxl-bit-exact.md)) put the linear sample on the other side of it. No other sample is more than 1.6e-5 away.
- **The total spline area is rejected only above 2^30.** libjxl rejects a total estimated area above `min(8 · pixels + 2^25, 2^30)` only in fuzzing builds, and otherwise warns. #274 planned to reject that whole limit. But libjxl's own `splines.jxl` is over its per-pixel part (244,616,490 against 67,108,864 for 2048 × 2048), and `djxl` decodes it. So this port rejects only above the 2^30 cap of that limit (`JxlSplines.InitializeDrawCache`, commented as a departure). That still keeps a hostile file from drawing for a long time. `animation_spline` and `spline_on_first_frame.jxl` are within the whole limit. The other spline bounds are libjxl's: for example, the Manhattan distance of each spline and the total estimated area stay within `min(1024 · pixels + 2^32, 2^42)`.
- **One ported spline bound cannot be reached from a stream.** libjxl's "Decoded splines but got none" (`Splines::Decode`): it reads one spline more than the count it is given, so it always has at least one. The check is ported as it is (`JxlSplinesTests` remarks).
- **Two departures in the noise.**
  - A NaN sample takes the first entry of the noise look-up table (`JxlAddNoiseStage.StrengthEvalLut`). libjxl converts the NaN floor to an integer and looks it up, which depends on the SIMD target and, on the scalar target, reads outside the table. Only a broken Modular float image can give a NaN.
  - The noise planes are filled for every group after all the sections are decoded, not as each AC group is decoded (libjxl's `ProcessACGroup`). Each 256 × 256 tile has a generator of its own, seeded by the frame counters and the tile position, so the order does not change the noise.
- **A reference frame can carry noise.** `cjxl --patches=1 --photon_noise_iso` sets the noise flag on the reference-only frame that holds the patches as well as on the displayed frame. The noise of the reference frame is seeded by the non-visible frame counter and saved with it, so the patches paste it, and the displayed frame adds its own. `vardct_xyb_patches_noise` checks this.
- **`Identify` cannot give a frame count.** `IImageInfo` has none in this version of ImageSharp. `Image.Load` gives every frame of each reference (60 for `animation_spline`), and `Identify` gives the animation header.
- **A grey image that is not XYB keeps three colour planes up to the noise (D12 review).** libjxl always has three colour planes, and its add-noise stage takes the noise strength from the first two. Before the review, this port dropped such a frame to its one grey plane before the patches, and gave the noise three copies of it. That missed `djxl` by an RMSE of 2.5e-5 on `vardct_grey_noise`, whose VarDCT planes differ by their quantization, and by 5.4e-5 (peak 1.2e-3) on a grey Modular frame with splines, whose X and Y colours differ. Both are within the lossy threshold, so their tests use an RMSE of 4e-6. Now a frame with noise keeps three planes to the noise; a Modular frame gets three copies of its grey plane, as libjxl's `rgb_from_gray`. The splines add their X, Y and B colours, and the patches blend all three. A frame without noise still drops to grey before the patches: no later stage mixes the colour planes, so the output is the same.
  - A departure from libjxl, until the D14 review: grey reference slots held one plane, where libjxl keeps three. So the patches of such a frame blended each colour plane with the slot's grey plane. That is libjxl's result only when the reference frame's three planes are equal. Once D14b decoded grey YCbCr frames, a chain could reach a slot whose planes differ (see the [D14 notes](#d14-done-check-notes)). Since the review, a slot saved before the colour transform, the only kind patches read, holds three planes, as in libjxl.
- **Spline rounding does not depend on the runtime (D12 review).** libjxl rounds the span of a spline segment with `std::llround`, which gives `LONG_MIN` on x86-64 for a NaN, an infinity or a value out of range. Such a segment then adds nothing. A .NET cast of a NaN gives 0 from .NET 9 on, and `long.MinValue` before. So the port maps these values to `long.MinValue` on every runtime (`JxlSplines.LlRound`, tested in `JxlSplineMathsTests` against libjxl's scalar code). The spline bounds keep a NaN or infinite maximum distance out of a stream. A curve whose 32 colour coefficients are all 2^21 decodes to finite samples of up to about 1.2e6, each within 1e-4 of `djxl`'s, relative to its size. Larger ones, or a large quantization adjustment, fail the total-area bound in both decoders.
- **A guard on the spline draw cache.** The segments of each row are indexed with `int`, so `JxlSplines.InitializeDrawCache` throws `InvalidMemoryOperationException` if their total is above `Array.MaxLength`. libjxl has no such limit. Within the 2^30 area limit, the total stays well below it: the rows of a segment grow with sigma, the number of segments grows with the arc length, and the area estimate bounds their product. `JxlSplinesTests` checks the guard with a lower limit.
- **Not covered by a file:** a preview frame, which libjxl counts as non-visible for the noise seed. At D12 this port rejected it. D14a skips it as libjxl does, and `preview_noise.jxl` covers the noise seed (see the [D14 notes](#d14-done-check-notes)).

### D13 done-check notes

D13 ([#281](https://github.com/PinataLabs/ImageSharp/issues/281)) passed its done-check ([#285](https://github.com/PinataLabs/ImageSharp/issues/285), PR [#286](https://github.com/PinataLabs/ImageSharp/pull/286)). `progressive` passes at Level 10 and `progressive_5` at Level 5: they are the same file against the same reference. Neither needs a later slice, and no vector showed a mismatch in the D13 code, so D13d fixed nothing. The later-slice theories are gone, from `JxlConformanceDecoderTests` and from `JxlXybVarDctTests`: no conformance vector and no generated file throws `NotSupportedException`. The table gives the worst channel's error, measured on x64. djxl v0.12.0 (AVX2) is measured against the same reference. The references of the generated files are `djxl` v0.12.0's own output, so `djxl`'s error against them is 0.

| Vector | Threshold (RMSE / peak) | This decoder | djxl v0.12.0 |
|---|---|---|---|
| `progressive`, `progressive_5` (fetched) | 1e-4 / 2e-2; 2e-2 / 6e-2 | 4.4e-7 / 2.0e-5 | 2.8e-7 / 4.9e-6 |
| `group_order_centre` (`cjxl -d 1 --group_order=1`; D13a) | 1e-4 / 4e-3 | 1.0e-6 / 1.2e-5 | reference |
| `vardct_progressive_ac`, `vardct_qprogressive_ac` (3 and 2 passes; D13b) | 1e-4 / 4e-3 | 1.0e-6 / 1.2e-5 | reference |
| `vardct_xyb_passes` (the D9 file of 3 passes, 64 × 64; D13b) | 1e-4 / 4e-3 | 1.1e-6 / 1.2e-5 | reference |
| `modular_progressive_ac`, `modular_progressive` (`cjxl -d 0 --progressive_ac` and `-d 0 -p`; D13b) | exact | 0 / 0 | reference |
| `progressive_dc1` (`--progressive_dc=1`; D13c) | 1e-4 / 4e-3 | 9.7e-7 / 1.7e-5 | reference |
| `progressive_dc2` (`--progressive_dc=2`: a VarDCT DC frame that uses a DC frame; D13c) | 1e-4 / 4e-3 | 1.0e-6 / 1.2e-5 | reference |
| `vardct_xyb_dc_frame` (the D9 file with a DC frame, 64 × 64; D13c) | 1e-4 / 4e-3 | 9.1e-7 / 1.2e-5 | reference |
| `vardct_progressive` (`cjxl -d 1 -p`: a DC frame, then 3 passes, both TOCs permuted; D13d) | 1e-4 / 4e-3 | 9.7e-7 / 1.7e-5 | reference |

`Image.Load` gives `Rgb24` for `progressive`, with one frame: its reference-only frame and its DC frame are not shown. Its planes are linear sRGB and its pixels sRGB, and they are within 0.506 of one 8-bit step of the clamped reference. The generated lossy files are within 0.502, and the lossless ones are `Png/Bike.png` exactly. `Identify` gives `progressive`'s image size (4064 × 2704, not the DC frame's), 24 bits, no extra channels, XYB and its ICC profile.

- **The structure of `progressive`.** A Modular reference-only frame, a Modular DC frame at level 1 with a permuted TOC, then a VarDCT frame of two passes with patches that uses the DC frame and stores its groups centre first. `cjxl -d 1 -p` writes the same structure without the reference-only frame: a Modular DC frame, then a VarDCT frame of three passes, and both TOCs are permuted (`vardct_progressive`). A decoder that reads that file's sections in their stored order throws "Invalid AC".
- **What `cjxl` writes.** `--progressive_dc=1` and `=2` do not permute the TOC and write one pass per frame. `cjxl` codes the smallest DC frame as Modular and any larger one as VarDCT. On a lossless file, `-p` is `--progressive_ac` with the centre-first group order: `cjxl -d 0 -p --group_order=0` writes the bytes of `cjxl -d 0 --progressive_ac`.
- **Departures.**
  - libjxl's "group offset overflow" check is not ported. The offsets are summed as a `long` of at most 65,536 sizes, each below 2^31, so the sum cannot overflow (`JxlToc.ReadGroupOffsets`).
  - libjxl checks the bytes of each section as the data arrives. This decoder has the whole codestream, so it checks once that the frame holds all its sections. Every section lies inside that total, so the check is the same.
  - A frame that uses a DC frame gets a copy of the DC slot in its DC planes, instead of a reference to it. Nothing writes to the slot in that frame.
  - A grey DC frame that is not XYB keeps three colour planes up to the write to its slot, as libjxl's slot has three channels, and as noise frames do.
  - The D8 departure on coefficient width can now be reached. libjxl keeps the coefficients as `int16` when `max_num_bits + CeilLog2(num_passes) < 16`, but `max_num_bits` does not count a pass's shift (up to 3). So a crafted stream of several passes can make libjxl's `int16` sum wrap, where this port's `int` does not. `cjxl` does not write such files. Porting it means porting `UpdateMaxNumBits` and truncating in the AC loop, so it is left for D14, if bit-exact output on hostile input matters. D14 keeps the departure, by decision (see the [D14 notes](#d14-done-check-notes)).
- **The DC-frame size check is libjxl's, moved earlier.** libjxl's `dec_group.cc` checks that each AC group's blocks lie inside the DC image, and fails with "Frame dimensions are too big for the image." For a VarDCT frame that uses a DC frame, that image is the DC frame's, so a frame with a custom size larger than the image fails. This port's copy of the check (`JxlVarDctGroup`) compares with the frame's own DC planes, which are copied at the frame's block size, so it cannot fail. `JxlFrameDecoder.GetDcFrame` makes the check instead, with libjxl's message, before the sections are read. A frame that uses a DC frame has no chroma subsampling, so it rejects the same files.
- **What still throws `NotSupportedException`.** At D13, `JxlFrameDecoder.CheckSupported` rejected only the two grey YCbCr frames of the [D10 notes](#d10-done-check-notes), and frame flags that libjxl does not define, which libjxl ignores. Outside it, `JxlDecoderCore` rejected an image with a preview frame, which libjxl skips. No conformance vector has a preview. Since D14 the decoder throws no `NotSupportedException`: D14a skips the preview frame, and D14b removed `CheckSupported` (see the [D14 notes](#d14-done-check-notes)).
- **`progressive` takes about a minute in a Debug test run.** In isolation, its decode takes 4.0 s in Release and 52 s in Debug, against 1.4 s in Release for `bike`, which has 5.2 MP against its 11 MP. A `dotnet-trace` profile of the Release build puts 66 % of the decode in the edge-preserving filter, and 54 % in EPF pass 0 alone. The frame has three EPF iterations, and more of its blocks pass the sigma test than `bike`'s, whose decode spends 33 % in pass 0. Pass 0 computes 12 sums of absolute differences over 5 pixels and 3 channels for each pixel, one pixel at a time, where libjxl filters a SIMD vector of pixels. Debug is 13 times slower than Release, because nothing is inlined. The D13 code (the TOC, the passes and the DC frames) takes a negligible share. D13d changes nothing. Vectorising the EPF, bit-exact and without FMA ([ADR 0004](../adr/0004-jxl-maths-matches-libjxl-bit-exact.md)), is performance work for after D15: the PRD has no performance target.
- **Not covered by a file:** DC frames at levels 3 and 4 (`cjxl` writes at most level 2), and a VarDCT DC frame with passes. The DC-frame bounds and a hand-built level-2 chain are covered by `JxlDcFrameTests`.

### D14 done-check notes

D14 ([#288](https://github.com/PinataLabs/ImageSharp/issues/288)) passed its done-check ([#295](https://github.com/PinataLabs/ImageSharp/issues/295)). `JxlOssFuzzTests` runs libjxl's OSS-Fuzz regression set (the 44 inputs of `tools/djxl_fuzzer_test.cc`) in the normal test run, not only in `jxl-oracle`. Each input goes through `Image.Load` and `Image.Identify`. Each call must finish, or throw `InvalidImageContentException`, `InvalidMemoryOperationException` or `OperationCanceledException`, within its budget. `Image.Load` must decode the 7 inputs that `djxl` v0.12.0 decodes, and only those. The files are fed as stored, with libjxl's 4-byte fuzzer footer: the D14 probe found that stripping it changes no outcome class. `JxlVarDctBoundsTests.Decode_AnyBitFlipped_ThrowsOnlyTheAllowedExceptions` now allows only the same three exceptions.

The OSS-Fuzz outcome per class, net10.0, x64 (it is the same in Release and in Debug):

| Call | Decodes | `InvalidImageContentException` | `InvalidMemoryOperationException`, `OperationCanceledException` | Anything else |
|---|---|---|---|---|
| `Image.Identify` | 42 | 2 ("unknown jxl file format version") | 0 | 0 |
| `Image.Load` | 7, the same 7 as `djxl` | 37; `djxl` rejects the same 37 | 0 | 0 |

Before D14, 10 decodes and 8 identifies threw `NotSupportedException`: 8 inputs have a preview, and 2 set an undefined frame flag. All 8 preview inputs now identify and then fail later in the decode, as in `djxl`. `5382562858532864` (flag 0x4) now decodes, and `6569981946494976` (flag 0x40) fails with "Invalid context map", as in `djxl`.

- **The time budget.** It is 10 s per call in a Release build, as #112 asks. Every CI job runs the tests in Release (`ci.yml`: the build, net11.0 and `jxl-oracle` jobs all pass `-c Release`). A Debug build gets 16 times the budget, 160 s: Debug is 13 to 16 times slower on the slowest input, and a 10 s budget would fail every local Debug run. The budget's token is passed to the decoder, so an input over the budget stops at the next check and fails the test. The budget is wall-clock time, so `JxlOssFuzzTests` runs alone, after the parallel tests (an xUnit collection with `DisableParallelization`): run in parallel with the rest of the JPEG XL suite on a 32-thread machine, the slowest input took over 10 s in Release.
- **The slowest input** is `6748486320652288`. It has 39 full-size frames of 408 × 340. Since D14a skips its preview, it decodes many of them before a later frame fails with "Non-zero padding bits". `Image.Load` takes 2.0 s in Release and 27.5 s in Debug (a run of the whole JPEG XL suite, net10.0, x64); `djxl` fails it in 0.3 s on one thread. A `dotnet-trace` profile in Release puts 87 % of the decode in the edge-preserving filter, the scalar EPF of the [D13 notes](#d13-done-check-notes), whose vectorising is left for after D15. The next is `6277573962760192` ("Non-zero padding bits" in a later frame's TOC): 0.5 s in Release and 6.1 s in Debug. Every other call takes at most 0.1 s in Release and 0.3 s in Debug.
- **Preview frames (D14a, [#289](https://github.com/PinataLabs/ImageSharp/issues/289)).** The decoder skips the preview frame as libjxl does when the preview is not wanted. It reads the header at the preview size (`nonserialized_is_preview`), with libjxl's two preview checks (a regular frame only; replace blending, and no custom size or origin). It reads the TOC, counts the frame as not shown for the noise seed, and steps over its sections. `Identify` no longer reads past the image header. `preview_noise.jxl` (hand-built, with a `djxl` reference) checks the noise counter. One departure: sections that run past the codestream throw `InvalidImageContentException`, where libjxl asks for more input.
- **Frame flags and grey YCbCr (D14b, [#290](https://github.com/PinataLabs/ImageSharp/issues/290)).** `JxlFrameDecoder.CheckSupported` is gone. Undefined frame flags are ignored, as libjxl does. A grey YCbCr frame keeps three colour planes through its patches and its save before the colour transform. Its slot keeps Cb, Y and Cr, which patches read per channel, and from which an extra channel may blend. `grey_ycbcr_saved_before_ct` and `grey_ycbcr_alpha_blending` match `djxl` exactly; `grey_ycbcr_patches` has an RMSE of 3.6e-8 and a peak of 2.4e-7.
  - **The D12 departure is gone (D14 review).** Before, a grey frame that is not YCbCr dropped to one plane before its patches, so its slot kept one plane. With patches from a grey YCbCr slot (Cb, Y and Cr), that plane is the one patched from Cb. libjxl's `rgb_from_gray` and patches stage leave three different planes, and a later grey YCbCr frame that patches from the slot mixes them. Now a frame of a grey image that is not XYB, if it is saved before its colour transform, keeps three colour planes up to that save, as libjxl. A Modular frame gets three copies of its grey plane, as `rgb_from_gray`. The drop to grey comes after the save. `grey_ycbcr_patch_chain` (hand-built, with a `djxl` reference) checks the chain: RMSE 3.3e-8, peak 2.4e-7, where the old code missed `djxl` by an RMSE of 0.26 (peak 1.2).
  - One departure stays: a slot saved after blending holds one grey plane, where libjxl keeps three. Patches cannot read such a slot ("Patches cannot use frames saved post color transforms"). Blending, the save after it and the output each work on one colour channel at a time, so only libjxl's first channel is ever read from it. The output does not change.
- **Resource caps (D14c, [#291](https://github.com/PinataLabs/ImageSharp/issues/291)).** Each cap is a commented departure from libjxl.
  - ICC: the decoded size is at most 2^22 bytes, #112's Level 5 tightening of libjxl's 2^28. The cap also applies to the encoder: `JxlIccWriter` refuses a larger profile with `ImageFormatException`, so it never writes what this decoder will not read.
  - Exif and XMP boxes: at most 64 MiB, plain or Brotli-compressed, else `InvalidImageContentException`. A `brob` box is measured before it is decompressed, so an oversized one is never held. On netstandard2.1, which has no `BrotliDecoder`, `brob` boxes are still dropped at any size ([#165](https://github.com/PinataLabs/ImageSharp/issues/165)). With `IgnoreMetadata`, boxes are not read, so there is no cap.
  - Modular channels: at most 2^14 after the squeezes ([#161](https://github.com/PinataLabs/ImageSharp/issues/161)); libjxl has no bound. The cap is 2^14, not 2^16, for speed: each squeeze step inserts its channels into a list, so filling the cap costs about C²/2 moves. In Debug, the #161 stream throws in about 0.3 s at 2^14, against 1.09 s at 2^16. The largest channel count in the test corpus (the conformance vectors, the generated files, the libjxl files and the OSS-Fuzz inputs) is 56, and libjxl's encoder cannot pass about 6,700 under the pixel cap.
- **Sample budget (D14d, [#292](https://github.com/PinataLabs/ImageSharp/issues/292)).** A frame's pixels × (colour + extra channels) must not exceed 2^30 samples, the 2^28 pixel cap × 4. Colour planes count at the upsampled frame size. Each extra channel counts at its own size, after its `dim_shift` and upsampling. It throws `InvalidMemoryOperationException`. This is a departure: libjxl has no such cap, but #112 allows only three exceptions to escape, and 256 extra channels would otherwise let `OutOfMemoryException` escape.
  - The budget is checked per frame, before any allocation. It is also checked on the image header, before the output image is allocated, as the pixel cap is. The ticket asked only for the check per frame; without the header check, `Image.Load` would allocate the output image before the first frame failed the budget. `Identify` does not enforce the budget.
  - There is still no total budget across frames. Each displayed frame of an animation allocates a full canvas, so many small frames on a large canvas add up, and `MaxFrames` defaults to `uint.MaxValue`. libjxl's fuzzer caps the total (`djxl_fuzzer.cc`: 5 × 2^21 pixels). This is out of D14's scope and is left for a later ticket.
- **Cancellation (D14e, [#293](https://github.com/PinataLabs/ImageSharp/issues/293)).** The decoder checks the token per frame, and in `ReadHeaders`, so `Identify` honours it. It also checks per 512 ICC symbols, per Modular channel (in the global and group streams), per transform in `MetaApply` and in the undo, per squeeze step, per DC and AC group, per row in the render pipeline, and in patches and splines. libjxl has no cancellation, so these checks are additions, with no effect on the output. Tests: `JxlCancellationTests`.
- **Bounds coverage (D14f, [#294](https://github.com/PinataLabs/ImageSharp/issues/294)).** Every #112 bound and checklist item has a test; the table is in the D14 PR. The checklist fixes 87bee190 (Modular residual overflow) and 30b0d63a (noise limits) are encoder-side in libjxl and do not apply to the decoder. ef555aa6 (stack depth) is build flags only, and the decoder's one depth bound, the MA tree height, is tested.
- **The int16 coefficient width stays a departure, by decision.** libjxl keeps the VarDCT coefficients as `int16` when `max_num_bits + CeilLog2(num_passes) < 16`, and its sum of several passes can then wrap (see the [D13 notes](#d13-done-check-notes)). This port keeps `int`, so it does not wrap. `cjxl` does not write such files, and the difference only changes the pixels of a crafted, hostile stream. `UpdateMaxNumBits` is not ported.
- **What still throws `NotSupportedException`.** Nothing in the decoder. The remaining sites are in the encoder, for libjxl efforts that later slices port. The decoder's other throws of types that #112 does not allow (`ArgumentOutOfRangeException` in `JxlDct` and `JxlDequantMatrices.Library`, `InvalidOperationException` in `JxlFromLinearStage` and `JxlIccBuilder`) guard programming errors. The D14 probe found them unreachable from a stream, and neither the OSS-Fuzz theory nor the bit-flip tests reach them.

### D15 done-check notes

D15 ([#307](https://github.com/PinataLabs/ImageSharp/issues/307)) passed its done-check ([#312](https://github.com/PinataLabs/ImageSharp/issues/312), PR [#314](https://github.com/PinataLabs/ImageSharp/pull/314)): every CI leg passed at 2f267c446 (see "Per leg" below). D15 changes no decoding, and the done-check found nothing to fix in the decoder. All 27 vectors pass `DecodeToPlanes_PassesThresholds` at Level 10, and their `_5` copies at Level 5 (41 rows), with `JXL_CONFORMANCE=1` and the libjxl v0.12.0 tools, locally on net10.0 and in `jxl-oracle`. `JxlConformanceLoadTests` and `JxlMagickCrossCheckTests` now go through `Configuration.Default`, which registers the format, so the lossy `Image.Load` theory and the Magick cross-check test the path that users take.

- **The public surface.** Everything is in `SixLabors.ImageSharp.Formats.Jxl`, with XML docs in WebP's style.
  - D15a ([#308](https://github.com/PinataLabs/ImageSharp/issues/308)): `JxlMetadata.ColorEncoding` is the public, immutable `JxlColorEncoding`. It gives the colour space, white point, primaries, transfer function and rendering intent, the gamma when the transfer function is a gamma, and the custom white point and primaries as `CieXyChromaticityCoordinates`. It is null when the stream signals an ICC profile. The enums `JxlColorSpace`, `JxlWhitePoint`, `JxlPrimaries`, `JxlTransferFunction`, `JxlRenderingIntent` and `JxlExtraChannelType` moved from `Headers` and are public. The internal header bundle is now `Headers.JxlColorEncodingHeader`. One departure: `JxlTransferFunction.Gamma` (65535) is libjxl's public `JXL_TRANSFER_FUNCTION_GAMMA`. The stream's enum has no such value, so only the description uses it. For a grey or XYB colour space, which codes no primaries, `Primaries` is `Srgb`, as in libjxl's public `JxlColorEncoding`.
  - D15b ([#311](https://github.com/PinataLabs/ImageSharp/issues/311)): `JxlFormat`, `JxlDecoder`, `JxlImageFormatDetector`, `JxlMetadata`, `JxlFrameMetadata`, `JxlExtraChannel` and the new `JxlConfigurationModule` are public, with `GetJxlMetadata()` and `GetJxlFrameMetadata()`. `JxlDecoder` has `IgnoreMetadata`, `MaxFrames`, `ColorProfileConversion` and `ColorProfileConversionTarget`. `JxlConfigurationModule` is the last module in `Configuration.CreateDefaultInstance()`. It registers the decoder and the detector only, so saving to `.jxl` throws the usual no-encoder `NotSupportedException` until E6. `JxlMetadata.ColorEncoding` and `ExtraChannels` have internal setters, because `JxlColorEncoding` and `JxlExtraChannel` have no public constructor. The other `JxlMetadata` members have public setters, as WebP's metadata has. The README lists JPEG XL as decode only.
  - **Amendments to #108, from the D15 review.** `JxlMetadata.OriginalIccProfile` is renamed `EmbeddedIccProfile`, the GLOSSARY.md term ("_Avoid_: original ICC"). This amends #108's name, which predates the glossary entry. `BitsPerSample` and `ExponentBitsPerSample` are `int`, as #108's table has, on `JxlMetadata` and, to match, on `JxlExtraChannel`. The header bundle (`Headers.JxlBitDepth`) keeps `uint`, as libjxl's does. The decoder converts, and the encoder rejects a negative depth with `ImageFormatException`, as it rejects other invalid depths. The three custom primaries of `JxlColorEncoding` are set together or are all null, and their docs say so.
  - `JxlDecoderTests.OnlyTheDecoderSurfaceIsPublic` lists the 14 public types. `IJxlDecoderOptions`, the encoder (`JxlEncoder`, `IJxlEncoderOptions`, `JxlEffort`, `JxlSpeedTier`) and every codec type stay internal.
  - **The API check.** `apicompat-suppressions.xml` has two commented blocks, as #174's: "JPEG XL colour description and its enums, #308 (D15a)", with 7 CP0001, and "JPEG XL decoder, its format metadata and JxlConfigurationModule, #311 (D15b)", with 7 CP0001 and 2 CP0002 (`MetadataExtensions.GetJxlMetadata` and `GetJxlFrameMetadata`). `ci-apicompat.ps1` finds no other change.
  - **E6 must:** register `JxlEncoder` in `JxlConfigurationModule`; flip `ImageFormatManagerTests.DefaultFormatsManager_RegistersNoJxlEncoder` and `JxlDefaultConfigurationTests.Save_AsJxl_WithDefaultConfiguration_ThrowsNoEncoder`; extend `OnlyTheDecoderSurfaceIsPublic`; put the encoder sentences of `BitsPerSample` and `IntensityTarget`, now `//` comments, back into their docs; and update the README line.
- **The Magick cross-check (D15c, [#309](https://github.com/PinataLabs/ImageSharp/issues/309)).** `JxlMagickCrossCheckTests.Load_MatchesMagick` compares `Image.Load` of each of the 24 committed conformance inputs with libjxl 0.12 through Magick.NET (`MagickReferenceDecoder.HighBitDepthInstance`, Q16), frame by frame, on every CI leg. It needs no `.npy` data and no tools. The fetched `bike`, `cafe` and `progressive` are left out, because only `jxl-oracle` has them. `Cases_CoverEveryCommittedInput` fails if a committed input has no case. The five older Magick tests of `JxlDecoderTests` are folded into the theory.
  - Lossless inputs match exactly on x64, in the pixel type that `Image.Load` picks: `alpha_nonpremultiplied`, `alpha_triangles`, `animation_newtons_cradle` (36 frames), `blendmodes`, `delta_palette`, `lz77_flower`, `patches_lossless`, `spot` (both decoders render the spot colours) and `sunset_logo`. On arm64, `blendmodes`, `spot` and `sunset_logo` can differ by one 16-bit step (see "Per leg" below).
  - Lossy inputs are compared through `RgbaVector`, before this decoder rounds, in steps of Magick's depth (8 bits for images of up to 8 bits, else 16). libjxl dithers its 8-bit output with an ordered blue noise of less than 0.49219 of a step before it rounds (`MakeUnsigned` in `stage_write.cc`), so Magick can be 0.99219 of a step from the unrounded value. With that dither emulated on this decoder's samples, only 0 to 147 samples per vector differ, by one step each, from libjxl's FMA ([ADR 0004](../adr/0004-jxl-maths-matches-libjxl-bit-exact.md)). Each tolerance is the measured value plus 0.01, rounded up to 0.01:

    | Vector | Compared as | Measured (Windows x64) | Tolerance |
    |---|---|---|---|
    | `animation_icos4d` (48 frames), `animation_spline` (60), `bench_oriented_brg`, `bicycles`, `grayscale_public_university`, `noise`, `opsin_inverse`, `upsampling` | Lossy, 8-bit steps | 0.991979 to 0.992301 | 1.01 |
    | `grayscale_jpeg` | Lossy, 8-bit steps | 0.989798 | 1.00 |
    | `mul_no_extra_channels` | Lossy, 8-bit steps | 0.749012 | 0.76 |
    | `lossless_pfm` | Lossy, 16-bit steps | 0.500000 | 0.51 |
    | `grayscale`, `patches` | Linear sRGB, 8-bit steps | 0.987789, 0.992182 | 1.00, 1.01 |
    | `cmyk_layers` | Coded planes, 8-bit steps | 0.988247 | 1.00 |
    | `alpha_premultiplied` | Premultiplied, 16-bit steps | 0.627922 | 0.75 |

  - **Inputs that Magick decodes differently by design** are compared in a form that holds:
    - XYB with an ICC profile (`grayscale`, `patches`): Magick gets libjxl's default linear sRGB and attaches the embedded profile; this decoder gives sRGB ([ADR 0002](../adr/0002-jxl-xyb-icc-decodes-to-srgb.md)). The theory undoes the sRGB curve on this decoder's float colour.
    - Premultiplied alpha (`alpha_premultiplied`): both decoders un-premultiply, which magnifies the error at small alpha (57 16-bit steps on 2 % of the pixels). The theory compares colour times its own alpha, and alpha directly. The 0.628 is 0.5 of Magick's rounding plus 0.128 of float difference. The tolerance lets the float difference reach the vector's conformance peak (0.25 of a 16-bit step), because 0.01 of a 16-bit step is too small a margin for the other legs' libjxl builds.
    - CMYK (`cmyk_layers`): Magick reads it as RGBA, with the C, M and Y planes in R, G and B and the Black channel dropped, so nothing in its output can be compared with `Image.Load`'s naive conversion ([#111](https://github.com/PinataLabs/ImageSharp/issues/111)). The theory compares the `DecodeToPlanes` C, M, Y and alpha planes with it instead.
    - Orientation (`bench_oriented_brg` 5, `sunset_logo` 7): Magick leaves the image as coded and reports the orientation. The theory orients Magick's output with `AutoOrient`.
  - **Per leg.** D15c measured on Windows x64 only, with margins of 0.010 to 0.018 of a step. Two CI runs of PR #314 ran the theory on every leg, and no leg skipped it: the first at bc158c73a, and the second at 2f267c446, after the arm64 allowance below. The second run is green on every leg:

    | Leg | Magick theory at bc158c73a | Magick theory at 2f267c446 | Whole test project at 2f267c446 |
    |---|---|---|---|
    | Windows x64, net10.0 and net11.0 | All 25 pass | All 25 pass | 41,270 passed, 1,078 skipped, 0 failed (each) |
    | Linux x64 (Ubuntu), net10.0 | All 25 pass | All 25 pass | 40,568 passed, 1,780 skipped, 0 failed (the coverage leg, which skips the slow encoder round trips) |
    | Linux x64 (Ubuntu), net11.0 | All 25 pass | All 25 pass | 41,270 passed, 1,078 skipped, 0 failed |
    | macOS arm64 (macOS 26), net10.0 and net11.0 | 22 pass. `blendmodes`, `spot` and `sunset_logo` failed as exact, by one 16-bit step | All 25 pass | 41,267 passed, 1,081 skipped, 0 failed (each; 3 memory-pool tests skip on macOS) |
    | `jxl-oracle` (Ubuntu 24.04, x64) | All 25 pass | All 25 pass | The JPEG XL suite: 4371 passed, 0 skipped |

    The API check, the analysis and SonarCloud also pass at 2f267c446.

    Every lossy row passed on every leg within its x64 margin. The three that failed are lossless, but above 8 bits (12, 16 and 10), so libjxl gives Magick 16-bit samples, and their samples are blended or have spot colours rendered in float. Such samples fall between 16-bit steps. A probe found thousands of them within one float ulp of a half step: 6,374 in `blendmodes`, 1,085 in `spot` and 4,469 in `sunset_logo`. On arm64, where FMA is always available, libjxl can fuse a multiply and an add that this decoder keeps apart ([ADR 0004](../adr/0004-jxl-maths-matches-libjxl-bit-exact.md)), so a few of these samples round the other way. The two x64 legs match exactly, so the decoder is not at fault. `alpha_nonpremultiplied` and `alpha_triangles` also have samples that close to a half step (7,168 and 10,936), but they are only scaled, not blended, and they match on arm64 too. The three rows now allow one 16-bit step on arm64 only (`RuntimeInformation.ProcessArchitecture`), with this reason in the test, and stay exact on x64.
- **Parse-only coverage (D15d, [#310](https://github.com/PinataLabs/ImageSharp/issues/310)).** The features of #101 with no `.npy` reference now have tests that pin their parsing. Each test checks that the file decodes and identifies like the same file without the feature, and a `djxl` oracle checks that the streams are valid. All streams are built with the test bit writer, so no generated file is committed.
  - Timecodes (`JxlParseOnlyTests`): a 3-frame animation with `have_timecodes` loads like the same animation without them: frame count, durations, loop count, ticks per second and pixels. `Identify` matches too.
  - Extra channels that are not rendered (`JxlParseOnlyTests`): Depth, Thermal, CFA, selection mask, optional and a second alpha, with several bit depths, float types, `dim_shift` and names. Each alone, and all six together, give the pixels and pixel type of the RGBA file without them, and each is described in `ExtraChannels` in stream order. The `djxl` oracle compares all 10 planes of `DecodeToPlanes`, with the `dim_shift` upsampling.
  - Unknown (15) and reserved (7 to 14) extra-channel types throw `InvalidImageContentException` in `Identify` and `Load`. #310 asked for them to be described and dropped, but libjxl `ef67fde2` rejects them (`ExtraChannelInfo::VisitFields`), and so does `djxl` v0.12.0. Optional (16) is the type that a decoder may ignore, and it is covered.
  - Ignored boxes (`JxlContainerTests`): `jbrd`, `jxll` and an unknown type, before `jxlc`, after it, or between two `jxlp` boxes, load and identify like the bare codestream, with no Exif or XMP profile.
  - The other features of #101's list already had tests: the preview (`preview_noise`, D14a), `jxlp` (`JxlContainerTests`), `brob` (`JxlMetadataTests`, `JxlContainerTests`) and HLG (D11). Non-coalesced output is out of scope (#108).
- **Left recorded, by decision on 2026-10-23.** These stay as the D14 notes describe them:
  - There is no total sample budget across frames. libjxl has none; its fuzzer caps the total.
  - `brob` boxes are dropped on netstandard2.1, which has no `BrotliDecoder`. Decoding them there needs a managed Brotli port.
  - The scalar EPF is slow (the [D13 notes](#d13-done-check-notes)). Vectorising it is performance work for after D15.
  - The VarDCT coefficients stay `int`, where libjxl can keep them as `int16` and wrap on a crafted stream.
- **Test counts.** The JPEG XL suite has 4371 tests on net10.0. With `JXL_CONFORMANCE=1` and the tools, all of them pass and none skip, locally (Windows x64, Debug) and in `jxl-oracle` (Release). D15 adds 13 tests through `Configuration.Default` (`JxlDefaultConfigurationTests`), 15 for the colour description (`JxlColorEncodingDescriptionTests`), 25 for the Magick cross-check (5 older ones folded in), and 37 parse-only tests. It removes `DefaultConfiguration_HasNoJxlFormat`, whose opposite now holds. The D15 review fixes add 2 rows for a negative bit depth, so the suite has 4373 tests; all of them pass locally with `JXL_CONFORMANCE=1` and the tools, and none skip. The netstandard2.1 and net10.0 Release builds have 0 warnings, and `ci-apicompat.ps1` finds no breaking change on either leg.

### E6 done-check notes

E6 ([#330](https://github.com/PinataLabs/ImageSharp/issues/330)) passed its done-check ([#335](https://github.com/PinataLabs/ImageSharp/issues/335)) locally on Windows x64, net10.0 Release, with the libjxl v0.12.0 tools. The done-check found nothing to fix, so it adds tests and docs only. The CI legs run on the PR. Each check goes through an entry point: `SaveAsJxl` / `Image.Save` with `Configuration.Default`, then our decoder, `MagickImage`, and `djxl` / `jxlinfo` in `jxl-oracle`.

- **The public surface** (E6a, [#331](https://github.com/PinataLabs/ImageSharp/issues/331)).
  - `JxlDecoderTests.OnlyTheCodecSurfaceIsPublic` lists 16 public types: D15's 14, plus `JxlEncoder` and `JxlEffort`. `TheEncoderHasTwoOptions` checks that the public members of `JxlEncoder` are exactly `Distance`, `Effort`, `Encode` and `EncodeAsync`, and that `IJxlEncoderOptions`, `JxlSpeedTier` and `JxlEncoderTestHooks` stay internal. The generator snapshot has the 9 `SaveAsJxl` / `SaveAsJxlAsync` overloads after TIFF's.
  - **The API check.** `apicompat-suppressions.xml` has one commented block, "Additions: JPEG XL encoder and SaveAsJxl, #331 (E6a)", with 2 CP0001 (`JxlEncoder`, `JxlEffort`) and 9 CP0002 (the overloads). `ci-apicompat.ps1` finds no other change.
  - **Validation**, when the encode starts, with `ImageFormatException`, and nothing written. This holds through `SaveAsJxl` and `SaveAsJxlAsync`. `Distance` must be in [0, 25], and NaN is rejected. A value in (0, 0.05) is clamped to 0.05: 0.01 writes the same bytes as 0.05. An `Effort` that `JxlEffort` does not define throws, where it used to fall back to Squirrel.
- **The container rules** (E6b [#332](https://github.com/PinataLabs/ImageSharp/issues/332), E6c [#333](https://github.com/PinataLabs/ImageSharp/issues/333)). The output is a container only when libjxl's `MustUseContainer` holds: there is a metadata box, or the codestream needs level 10. Otherwise it is the bare codestream, byte for byte as before E6.
  - **Metadata.** The encoder calls `SyncProfiles()`, then writes `ExifProfile.ToByteArray()` only when it starts with a TIFF header (cjxl's `IsExif`), after a 4-byte offset of 0. It writes `XmpProfile` when it is not empty. The ICC profile stays in the codestream ([#115](https://github.com/PinataLabs/ImageSharp/issues/115)). An Exif or XMP box over 64 MiB, the decoder's cap, throws before any work is done. The layout is `JXL `, `ftyp` (version 0), [`jxll`], `jxlp` 0 (the header), `Exif`, `xml `, then `jxlp` 0x80000001 (the frame).
  - **Level 10.** `JxlCodestreamLevel.Verify` ports `VerifyLevelSettings` (`encode.cc:560-628`). It gives level 10 when the 16-bit buffer is not enough (lossless samples or alpha above 12 bits), for a side over 2^18, more than 2^28 pixels, more than 4 extra channels, or a Black channel. It throws where libjxl returns -1: for example, a side over 2^30. Level 10 alone is `JXL `, `ftyp`, `jxll` (10), `jxlc`. With metadata, `jxll` comes before `jxlp` 0.
  - **The codestream does not change.** The codestream that `JxlContainer.Read` joins equals the bare encode for every profile mix, lossless and lossy. The pin tables hash `JxlBoxList.Codestream`, so 10 pins of L16, Rgba64 and RgbaVector rows now hash the joined codestream. No pin value changed since `main` (`ac75e342b`).
- **The cjxl comparison.** cjxl v0.12.0 with `--compress_boxes=0` runs on the same image, saved as PNG, in `jxl-oracle`:
  - E6b ran `-d 0 -e 1` with Exif and XMP;
  - the done-check ran `-d 0` and `-d 1` at `-e 1`, opaque and with alpha, each with Exif, orientation 6, XMP and a Display P3 profile.

  In each case cjxl gives the same box types in the same order, the same `ftyp` and `jxlp` indexes, and the same `Exif` and `xml ` contents. For level 10, `-d 0 -e 1` on a 16-bit L16 and Rgba64 PNG gives the same box types, `ftyp` and `jxll` contents (E6c). `jxlinfo` lists our boxes with their sizes, and `djxl` decodes every E6 output.
- **The `brob` departure.** Exif and XMP are always plain boxes. netstandard2.1 has no `BrotliEncoder`, so the bytes would depend on the target, and our netstandard2.1 decoder drops `brob` ([#317](https://github.com/PinataLabs/ImageSharp/issues/317)). This departs from cjxl's default (`--compress_boxes=1`), not from libjxl, which leaves compression to the caller (`JxlEncoderAddBox`'s `compress_box`).
- **The orientation rule** (E6d, [#334](https://github.com/PinataLabs/ImageSharp/issues/334)). The encoder follows cjxl's `InterpretExif` (`exif.h:69-86`): a SHORT Exif orientation from 1 to 8 becomes the codestream orientation. The pixels and the Exif box are written as they are stored, so the two stay in sync, as `encode.h` asks. Our decoder applies the orientation and resets the Exif orientation to 1. `SyncProfiles` rewrites the tag as a SHORT, or drops it when its value does not fit. So on a malformed JPEG, cjxl (which copies the Exif blob) and this encoder can differ, but each keeps its codestream in sync with the Exif it writes.
  - **Mutation check:** with the codestream orientation left at 1, the 12 round-trip cases through our decoder, `djxl` and `jxlinfo` fail. The Magick and cjxl rows still pass, because they read the Exif.
- **The metadata round trip** (`JxlEncoderMetadataRoundTripTests`, 20 cases). It covers lossless and lossy, with and without alpha.
  - **Our decoder:** the Exif values, with the orientation 1; the XMP bytes; the ICC profile byte for byte for lossless, and for lossy the profile built for the same colour encoding (#115); and the `AutoOrient` pixels. Lossless is exact. Lossy is within the interim bound, with alpha exact.
  - **`MagickImage`:** the Exif values, with the orientation 6; the XMP bytes; the ICC profile for lossless; and `Orientation.RightTop`.
  - **`jxl-oracle`:** `djxl`'s oriented pixels, `jxlinfo`'s boxes and orientation, and cjxl's box layout.
- **Magick results per leg.** Locally on Windows x64, net10.0, every Magick row of E6 passes: 4 in the round trip, 2 in `JxlEncoderMetadataTests`, 8 in `JxlEncoderOrientationTests` and the level-10 decodes. The round-trip rows skip only where Magick.NET has no JPEG XL delegate, so they run under coverage too. The other legs (Linux, macOS, net11.0) are recorded from the PR's CI run.
- **netstandard2.1.** No test project targets netstandard2.1, so the Exif and XMP round trip ([#332](https://github.com/PinataLabs/ImageSharp/issues/332)) on that target is checked by a console harness. The harness is a net10.0 program that references the netstandard2.1 Release DLL by `HintPath`. It does these steps:
  - It stops unless the loaded assembly's `TargetFrameworkAttribute` is `.NETStandard,Version=v2.1`.
  - It saves a 37 x 23 `Rgba32` image with `SaveAsJxl` at `Distance` 0 and 1. The image has Exif (Software, orientation 6) and XMP.
  - It checks that the output is a container with `Exif` and `xml ` boxes.
  - It loads the output with `Image.Load` and checks the Exif Software value, the Exif orientation (reset to 1), the XMP bytes and the oriented size (23 x 37). For `Distance` 0, it also checks that the pixels equal the `AutoOrient` pixels exactly.

  The E6 review fixes ran it on 2026-10-24 and it printed `d0: 351 bytes, OK`, `d1: 439 bytes, OK` and `OK on .NETStandard,Version=v2.1`. To run it again, put the two files below in a folder outside the repository, so that the repository's `Directory.Build.props` does not apply, then run these commands in PowerShell from the repository root:

  ```powershell
  dotnet build src/ImageSharp -c Release -f netstandard2.1
  dotnet run --project $env:TEMP\jxl-ns21\jxl-ns21.csproj -c Release "-p:ImagePastelDll=$PWD\artifacts\bin\src\ImageSharp\Release\netstandard2.1\PinataLabs.ImagePastel.dll"
  ```

  <details><summary><code>jxl-ns21.csproj</code> and <code>Program.cs</code></summary>

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <TargetFramework>net10.0</TargetFramework>
    </PropertyGroup>
    <ItemGroup>
      <Reference Include="PinataLabs.ImagePastel" HintPath="$(ImagePastelDll)" />
    </ItemGroup>
  </Project>
  ```

  ```csharp
  using System;
  using System.IO;
  using System.Linq;
  using System.Reflection;
  using System.Runtime.Versioning;
  using System.Text;
  using SixLabors.ImageSharp;
  using SixLabors.ImageSharp.Formats.Jxl;
  using SixLabors.ImageSharp.Metadata.Profiles.Exif;
  using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
  using SixLabors.ImageSharp.PixelFormats;
  using SixLabors.ImageSharp.Processing;

  string tfm = typeof(Image).Assembly.GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName;
  Check(tfm == ".NETStandard,Version=v2.1", $"loaded {tfm}");
  byte[] xmp = Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>");
  foreach (float distance in new[] { 0f, 1f })
  {
      using Image<Rgba32> image = new(37, 23);
      for (int y = 0; y < image.Height; y++)
      {
          for (int x = 0; x < image.Width; x++)
          {
              image[x, y] = new Rgba32((byte)(x * 7), (byte)(y * 11), (byte)(x + y), 200);
          }
      }

      image.Metadata.ExifProfile = new ExifProfile();
      image.Metadata.ExifProfile.SetValue(ExifTag.Software, "ns21");
      image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
      image.Metadata.XmpProfile = new XmpProfile(xmp);

      using MemoryStream stream = new();
      image.SaveAsJxl(stream, new JxlEncoder { Distance = distance });
      byte[] encoded = stream.ToArray();
      string ascii = Encoding.ASCII.GetString(encoded);
      Check(ascii.Substring(4, 4) == "JXL " && ascii.Contains("Exif") && ascii.Contains("xml "), "container with Exif and xml boxes");

      using Image<Rgba32> decoded = Image.Load<Rgba32>(encoded);
      Check(decoded.Metadata.ExifProfile.GetValue(ExifTag.Software).Value == "ns21", "Exif Software");
      Check(decoded.Metadata.ExifProfile.GetValue(ExifTag.Orientation).Value == 1, "Exif orientation reset to 1");
      Check(decoded.Metadata.XmpProfile.ToByteArray().SequenceEqual(xmp), "XMP bytes");
      Check(decoded.Width == 23 && decoded.Height == 37, "oriented size");
      if (distance == 0)
      {
          using Image<Rgba32> oriented = image.Clone(c => c.AutoOrient());
          for (int y = 0; y < oriented.Height; y++)
          {
              for (int x = 0; x < oriented.Width; x++)
              {
                  Check(oriented[x, y] == decoded[x, y], $"pixel ({x}, {y})");
              }
          }
      }

      Console.WriteLine($"d{distance}: {encoded.Length} bytes, OK");
  }

  Console.WriteLine($"OK on {tfm}");

  static void Check(bool condition, string what)
  {
      if (!condition)
      {
          throw new InvalidOperationException($"FAILED: {what}");
      }
  }
  ```

  </details>
- **Left recorded:**
  - **No cancellation inside the encode.** The token is checked once, at the start, as in the other encoders here. A follow-up issue can add per-group checks, like D14e.
  - **No IPTC, JUMBF or gain-map boxes.** No JPEG XL box carries IPTC-IIM, and ImageSharp 2.x has no profile for JUMBF or gain maps.
  - **The decoder's 2^28-pixel cap against level-10 writes.** The encoder writes an image over 2^28 pixels as a valid level-10 file, which libjxl reads, but this library's decoder refuses it. The `JxlEncoder` docs say so.
  - **Exif without a TIFF header** gives a bare codestream here, where cjxl writes a `jxlc` container for it (E6b).
  - **Not covered by a unit test:** more than 2^28 pixels with both sides at most 2^18. The encode would read 268M pixels. The side checks cover the same branch of `Verify` (E6c).
- **Test counts.** The new E6 classes have these case counts:
  - `Formats/Jxl/ImageExtensionsTests`: 31;
  - `JxlEncoderMetadataTests`: 16;
  - `JxlEncoderLevelTests`: 30;
  - `JxlEncoderOrientationTests`: 73;
  - `JxlEncoderMetadataRoundTripTests`: 20.

  E6 also changed `JxlDecoderTests`, `JxlDefaultConfigurationTests` and `ImageFormatManagerTests`, from "no encoder" to the registered encoder. The JPEG XL suite has 5188 tests on net10.0. With the tools and without `JXL_CONFORMANCE`, 5124 pass and 64 skip; the skips are the conformance vectors that are not cached. The whole test project gives 43,082 passed, 83 skipped and 0 failed. The generator tests pass (16). The netstandard2.1 and net10.0 Release builds have 0 warnings, and `ci-apicompat.ps1` passes.

## Slices

The size column is a rough count of libjxl C++ lines, taken from the [decoder](https://github.com/PinataLabs/ImageSharp/issues/102) and [encoder](https://github.com/PinataLabs/ImageSharp/issues/103) research and corrected at the [re-estimation gate](#re-estimation-gate-outcome). The C# column is the size times 0.72, and the agent-hours column is the size times 0.17 hours per 1,000 lines, both measured on D1. Agent hours are wall-clock time for an AI agent, not a human rate.

### Test infrastructure

| # | Ships | Done-check | Size | C# (est.) | Agent hours (est.) |
|---|---|---|---|---|---|
| T1 ([#126](https://github.com/PinataLabs/ImageSharp/issues/126)) | `*.jxl` LFS pattern; conformance inputs, `test.json` files, READMEs and notices entry; `.npy` reader; sha256 fetch cache with `JXL_CONFORMANCE=1`; `jxl-oracle` CI job skeleton; `MagickReferenceDecoder` widened to 16-bit for JXL | Harness self-test: **Magick's** decode of `delta_palette` passes its thresholds | test code only | — | — |
| T2 ([#265](https://github.com/PinataLabs/ImageSharp/issues/265)) | Managed SSIMULACRA2 port; cjxl calibration table (about 6 images × d{1,2,4} × 3 levels) with its generating command | Within 0.5 of the libjxl `ssimulacra2` binary in `jxl-oracle`; table committed | ≈ 0.6K | ≈ 0.43K | ≈ 0.1 |

### Decoder

| # | Ships | Done-check | Size | C# (est.) | Agent hours (est.) |
|---|---|---|---|---|---|
| D1 ([#127](https://github.com/PinataLabs/ImageSharp/issues/127)) | **Porting spike, first real slice:** bit reader, fields, image headers, entropy (prefix, ANS, hybrid uint, context map; no LZ77), Modular general path, RCT and palette (including delta), single-frame frame header and TOC, simple pipeline with `PlanesSink` and `ImageFrameSink<TPixel>`, `Identify` | `delta_palette`; measurements recorded in this file | 7.15K ported (9.2K estimated; 1.9K deferred, see the gate) | 5.15K (actual) | 1.2 (actual) |
| D2 ([#152](https://github.com/PinataLabs/ImageSharp/issues/152), [#153](https://github.com/PinataLabs/ImageSharp/issues/153), [#154](https://github.com/PinataLabs/ImageSharp/issues/154), [#155](https://github.com/PinataLabs/ImageSharp/issues/155), [#156](https://github.com/PinataLabs/ImageSharp/issues/156)) | Alpha extra channel, 9–16-bit and float32 samples, LZ77, un-premultiply in the sink, pixel-type choice for the non-generic `Decode`; the minimal container read for one `jxlc` box | `alpha_nonpremultiplied`, `alpha_triangles`, `lossless_pfm` (bit-exact), `lz77_flower`; a `cjxl`-made lossless premultiplied file | ≈ 0.85K (0.8K + 0.05K `jxlc` read from D4) | ≈ 0.61K | ≈ 0.1 |
| D3 ([#158](https://github.com/PinataLabs/ImageSharp/issues/158), [#159](https://github.com/PinataLabs/ImageSharp/issues/159)) | Squeeze; greyscale output | A `cjxl`-made lossless greyscale file that uses squeeze and no loop filters | ≈ 0.5K | ≈ 0.36K | ≈ 0.1 |
| D4 ([#163](https://github.com/PinataLabs/ImageSharp/issues/163), [#165](https://github.com/PinataLabs/ImageSharp/issues/165), [#164](https://github.com/PinataLabs/ImageSharp/issues/164), [#166](https://github.com/PinataLabs/ImageSharp/issues/166)) | Container and box parsing (`jxlp`, `brob`, 64-bit sizes; D2 reads one `jxlc` box); ICC codec; Exif/XMP mapped to ImageSharp profiles; pixel-profile rules; enum → ICC builder; `JxlMetadata` colour fields | `EmbeddedIccProfile` bytes equal `original.icc` for every ICC vector (via `Identify`); `square-extended-size-container`; `1x1_exif_xmp` | ≈ 2.15K (2.2K − 0.05K `jxlc` read moved to D2) | ≈ 1.55K | ≈ 0.4 |
| D5 ([#189](https://github.com/PinataLabs/ImageSharp/issues/189), [#190](https://github.com/PinataLabs/ImageSharp/issues/190), [#191](https://github.com/PinataLabs/ImageSharp/issues/191)) | Multi-frame: blending, layers, reference frames, coalescing, animation, orientation, `JxlFrameMetadata`, `MaxFrames` | `sunset_logo`, `blendmodes`, `mul_no_extra_channels`, `animation_newtons_cradle`, `cropped_traffic_light` | ≈ 2.15K (2.1K + 0.05K reference bookkeeping from D1) | ≈ 1.55K | ≈ 0.4 |
| D6 ([#194](https://github.com/PinataLabs/ImageSharp/issues/194), [#195](https://github.com/PinataLabs/ImageSharp/issues/195), [#196](https://github.com/PinataLabs/ImageSharp/issues/196), [#197](https://github.com/PinataLabs/ImageSharp/issues/197)) | Spot-colour rendering, CMYK naive conversion, `JxlMetadata.ExtraChannels` | `spot`, `cmyk_layers` | ≈ 0.3K | ≈ 0.22K | ≈ 0.1 |
| D7 ([#216](https://github.com/PinataLabs/ImageSharp/issues/216), [#217](https://github.com/PinataLabs/ImageSharp/issues/217), [#218](https://github.com/PinataLabs/ImageSharp/issues/218), [#219](https://github.com/PinataLabs/ImageSharp/issues/219)) | Patches on Modular frames | `patches_lossless` | ≈ 0.5K | ≈ 0.36K | ≈ 0.1 |
| D8 ([#223](https://github.com/PinataLabs/ImageSharp/issues/223), [#224](https://github.com/PinataLabs/ImageSharp/issues/224), [#225](https://github.com/PinataLabs/ImageSharp/issues/225), [#226](https://github.com/PinataLabs/ImageSharp/issues/226), [#227](https://github.com/PinataLabs/ImageSharp/issues/227), [#228](https://github.com/PinataLabs/ImageSharp/issues/228)) | VarDCT core: DCT8, quantiser and DC, chroma-from-luma and per-block quant fields (decoded and applied), coefficient orders, VarDCT group decode, XYB → sRGB on Modular and VarDCT frames, `JxlFastMath` | `bicycles` (Modular with XYB); `benchmark_xl` VarDCT files without XYB, and `cjxl -e 2 --gaborish=0 --epf=0` files, with `djxl` references; `JxlFastMath` unit tests within libjxl's error bounds | ≈ 4.7K (4.3K + 0.45K VarDCT glue from D1 − 0.05K raw quant tables moved to D10) | ≈ 3.38K | ≈ 0.8 |
| D9 ([#239](https://github.com/PinataLabs/ImageSharp/issues/239), [#240](https://github.com/PinataLabs/ImageSharp/issues/240), [#241](https://github.com/PinataLabs/ImageSharp/issues/241), [#242](https://github.com/PinataLabs/ImageSharp/issues/242), [#243](https://github.com/PinataLabs/ImageSharp/issues/243), [#244](https://github.com/PinataLabs/ImageSharp/issues/244), [#245](https://github.com/PinataLabs/ImageSharp/issues/245)) | All transform sizes and AFV, Gaborish, EPF; tests with adaptive quant fields and non-default chroma-from-luma maps (both decoded since D8) | `opsin_inverse`, `grayscale` (linear at the seam), `alpha_premultiplied`, `animation_icos4d`, `grayscale_public_university` (Modular with Gaborish and EPF); `vardct_xyb_e7` (`cjxl -d 1` at the default effort) with a `djxl` reference. `bike` moved to D11, then to D12 | ≈ 3.0K | ≈ 2.16K | ≈ 0.5 |
| D10 ([#248](https://github.com/PinataLabs/ImageSharp/issues/248), [#249](https://github.com/PinataLabs/ImageSharp/issues/249), [#250](https://github.com/PinataLabs/ImageSharp/issues/250), [#251](https://github.com/PinataLabs/ImageSharp/issues/251), [#252](https://github.com/PinataLabs/ImageSharp/issues/252), [#253](https://github.com/PinataLabs/ImageSharp/issues/253)) | Upsampling (2×, 4×, 8×, default and custom weights; colour, and early and late extra channels), chroma upsampling (any colour channel, Y included), YCbCr on Modular and VarDCT frames, raw quant tables (only JPEG-transcoded files use them). D8 had already ported the subsampled VarDCT decode, so D10's chroma work is the pipeline stage | `upsampling`, `cafe` (fetched), `grayscale_jpeg`, `bench_oriented_brg`, and their `_5` copies; `cjxl` upsampling files and JPEG transcodes (4:4:4, 4:2:0, 4:2:2, 4:4:0, grey, luma-subsampled), and `jxl_from_tree` Modular YCbCr files, with `djxl` references | ≈ 0.65K (0.6K + 0.05K raw quant tables from D8) | ≈ 0.47K | ≈ 0.1 |
| D11 ([#256](https://github.com/PinataLabs/ImageSharp/issues/256), [#258](https://github.com/PinataLabs/ImageSharp/issues/258), [#259](https://github.com/PinataLabs/ImageSharp/issues/259), [#257](https://github.com/PinataLabs/ImageSharp/issues/257), [#260](https://github.com/PinataLabs/ImageSharp/issues/260), [#261](https://github.com/PinataLabs/ImageSharp/issues/261), [#262](https://github.com/PinataLabs/ImageSharp/issues/262)) | All transfer functions for XYB output (BT.709, DCI, gamma, PQ scaled by the intensity target, HLG with the OOTF to scene light) and the PQ and HLG pixel profiles: a 64-entry table curve, or libjxl's 9 × 9 × 9 tone-mapping 3D LUT to PCS Lab for an RGB encoding that a `cicp` tag can name. No tone-mapping or to-linear stage, and no XYB colour-space profile (see the [D11 notes](#d11-done-check-notes)) | `pq_gradient` (libjxl test data) with a `djxl` `.npy` reference, exact; `cjxl` files for each transfer function (`vardct_xyb_hlg` is the HLG file) and lossless PQ and HLG colour files, with `djxl` references and profiles. `bike` and `bike_5` moved to D12 | ≈ 1.6K | ≈ 1.15K | ≈ 0.3 |
| D12 ([#274](https://github.com/PinataLabs/ImageSharp/issues/274), [#275](https://github.com/PinataLabs/ImageSharp/issues/275), [#276](https://github.com/PinataLabs/ImageSharp/issues/276), [#277](https://github.com/PinataLabs/ImageSharp/issues/277), [#278](https://github.com/PinataLabs/ImageSharp/issues/278); PR [#279](https://github.com/PinataLabs/ImageSharp/pull/279)) | Patches on VarDCT frames; splines (the spline dictionary with libjxl's bounds, drawn after the patches); noise (libjxl's `Xorshift128Plus`, seeded by the frame counters and the tile position, added after the upsampling). DC frames threw until D13, which decodes them. The total spline area is rejected only above 2^30 (see the [D12 notes](#d12-done-check-notes)) | `patches`, `animation_spline`, `noise` and their `_5` copies; `bike` and `bike_5` (fetched; XYB with BT.709 and patches on a VarDCT frame, moved from D11); libjxl `splines.jxl` and `spline_on_first_frame.jxl` with `djxl` `.npy` references; `cjxl` files with patches on a grey image, noise with `--resampling=2`, noise on an animation, and patches with noise, with `djxl` references | ≈ 1.4K | ≈ 1.01K | ≈ 0.2 |
| D13 ([#281](https://github.com/PinataLabs/ImageSharp/issues/281), [#282](https://github.com/PinataLabs/ImageSharp/issues/282), [#283](https://github.com/PinataLabs/ImageSharp/issues/283), [#284](https://github.com/PinataLabs/ImageSharp/issues/284), [#285](https://github.com/PinataLabs/ImageSharp/issues/285); PR [#286](https://github.com/PinataLabs/ImageSharp/pull/286)) | Progressive files. The permuted TOC: the permutation is read with the Lehmer code of the coefficient orders and its ANS final-state check, and each section is found through it (libjxl's `ReadGroupOffsets`). Passes, up to 11: each VarDCT pass adds its shifted coefficients, and each Modular pass decodes its downsampling bracket. DC frames: four DC slots beside the reference slots, written after the noise and before the save before the colour transform, and never shown; a frame that uses one takes its DC from it and skips its own VarDCT DC and the adaptive DC smoothing. At D13 the frame decoder rejected only its commented departures, and an image with a preview frame threw until D14 (see the [D13 notes](#d13-done-check-notes)) | `progressive` and `progressive_5` (fetched); `cjxl` files with a centre-first TOC, `--progressive_ac`, `--qprogressive_ac`, `--progressive_dc=1` and `=2`, and `-p` on a lossy and a lossless file, with `djxl` references | ≈ 1.15K (1.0K + 0.15K TOC permutation and section scheduling from D1) | ≈ 0.83K | ≈ 0.2 |
| D14 ([#288](https://github.com/PinataLabs/ImageSharp/issues/288), [#289](https://github.com/PinataLabs/ImageSharp/issues/289), [#290](https://github.com/PinataLabs/ImageSharp/issues/290), [#291](https://github.com/PinataLabs/ImageSharp/issues/291), [#292](https://github.com/PinataLabs/ImageSharp/issues/292), [#293](https://github.com/PinataLabs/ImageSharp/issues/293), [#294](https://github.com/PinataLabs/ImageSharp/issues/294), [#295](https://github.com/PinataLabs/ImageSharp/issues/295)) | Hardening sweep. Preview frames are skipped as libjxl does (D14a). The frame decoder throws no `NotSupportedException`: undefined frame flags are ignored, and grey YCbCr frames are decoded (D14b). Resource caps: ICC ≤ 2^22 bytes, Exif and XMP boxes ≤ 64 MiB, Modular images ≤ 2^14 channels (D14c, closes #161). A sample budget of 2^30 per frame (D14d). Cancellation at every long-running site, and in `Identify` (D14e). A test for every #112 bound (D14f). The OSS-Fuzz regression theory in the normal test run (D14g). See the [D14 notes](#d14-done-check-notes) | All 44 OSS-Fuzz inputs decode or throw an allowed exception within 10 s, through `Image.Load` and `Identify`; `Image.Load` decodes the 7 inputs that `djxl` decodes; the bit-flip tests allow only the three exceptions of #112 | small | — | — |
| D15 ([#307](https://github.com/PinataLabs/ImageSharp/issues/307), [#308](https://github.com/PinataLabs/ImageSharp/issues/308), [#311](https://github.com/PinataLabs/ImageSharp/issues/311), [#309](https://github.com/PinataLabs/ImageSharp/issues/309), [#310](https://github.com/PinataLabs/ImageSharp/issues/310), [#312](https://github.com/PinataLabs/ImageSharp/issues/312); PR [#314](https://github.com/PinataLabs/ImageSharp/pull/314)) | **Decoder release.** A public, immutable colour description, `JxlColorEncoding`, with its enums in `Formats.Jxl`; the header bundle becomes `Headers.JxlColorEncodingHeader` (D15a). The decoder surface is public, and `JxlConfigurationModule`, in `Configuration.CreateDefaultInstance()`, registers the decoder and the detector but no encoder (D15b). A Magick cross-check of `Image.Load` on all 24 committed conformance inputs, on every CI leg (D15c). Parse-only tests for timecodes, the extra channels that are not rendered, and the `jbrd`, `jxll` and unknown boxes (D15d). See the [D15 notes](#d15-done-check-notes) | All 27 vectors pass in `jxl-oracle`; Magick cross-check on every leg | small | — | — |

### Encoder

| # | Ships | Done-check | Size | C# (est.) | Agent hours (est.) |
|---|---|---|---|---|---|
| E1 ([#207](https://github.com/PinataLabs/ImageSharp/issues/207), [#208](https://github.com/PinataLabs/ImageSharp/issues/208), [#209](https://github.com/PinataLabs/ImageSharp/issues/209), [#210](https://github.com/PinataLabs/ImageSharp/issues/210), [#211](https://github.com/PinataLabs/ImageSharp/issues/211), [#212](https://github.com/PinataLabs/ImageSharp/issues/212), [#213](https://github.com/PinataLabs/ImageSharp/issues/213), [#214](https://github.com/PinataLabs/ImageSharp/issues/214)) | Encoder core: bit writer, `Write` field visitor, ANS and prefix writers, context-map writer, Modular with predefined trees, frame and TOC assembly; lossless Fastest (fixed WP tree, global palette, YCoCg); ICC embedded verbatim | Bit-exact round-trip on the lossless corpus through both decoders; `djxl` accepts the output | ≈ 4.9K | ≈ 3.53K | ≈ 0.8 |
| E2 ([#231](https://github.com/PinataLabs/ImageSharp/issues/231), [#232](https://github.com/PinataLabs/ImageSharp/issues/232), [#233](https://github.com/PinataLabs/ImageSharp/issues/233), [#234](https://github.com/PinataLabs/ImageSharp/issues/234), [#235](https://github.com/PinataLabs/ImageSharp/issues/235)) | Lossless Default and Thorough: MA-tree learning, RCT search, local palette, RLE LZ77, e7 parameters and `EstimateCost` | Bit-exact at all three levels | ≈ 1.6K | ≈ 1.15K | ≈ 0.3 |
| E3 ([#263](https://github.com/PinataLabs/ImageSharp/issues/263), [#264](https://github.com/PinataLabs/ImageSharp/issues/264), [#265](https://github.com/PinataLabs/ImageSharp/issues/265), [#266](https://github.com/PinataLabs/ImageSharp/issues/266), [#267](https://github.com/PinataLabs/ImageSharp/issues/267), [#268](https://github.com/PinataLabs/ImageSharp/issues/268), [#269](https://github.com/PinataLabs/ImageSharp/issues/269), [#270](https://github.com/PinataLabs/ImageSharp/issues/270)) | Lossy Fastest: `TryFromIccProfile` and the colour rules, forward XYB, DCT8, constant quant field, custom DCT8 coefficient order (libjxl reorders at e3), AC tokens, DC/AC-metadata streams, alpha with the predefined tree | Fastest rows of the calibration table; colour-rule tests (an unrecognised ICC throws, a mismatched profile is ignored) | ≈ 1.8K | ≈ 1.3K | ≈ 0.3 |
| E4 ([#297](https://github.com/PinataLabs/ImageSharp/issues/297), [#298](https://github.com/PinataLabs/ImageSharp/issues/298), [#299](https://github.com/PinataLabs/ImageSharp/issues/299), [#300](https://github.com/PinataLabs/ImageSharp/issues/300), [#301](https://github.com/PinataLabs/ImageSharp/issues/301), [#302](https://github.com/PinataLabs/ImageSharp/issues/302), [#303](https://github.com/PinataLabs/ImageSharp/issues/303), [#304](https://github.com/PinataLabs/ImageSharp/issues/304), [#305](https://github.com/PinataLabs/ImageSharp/issues/305), [#306](https://github.com/PinataLabs/ImageSharp/issues/306)) | Lossy Default (libjxl e5, `kHare`): the adaptive quant field (`InitialQuantField` with the global scale from 0.39 / d, and `AdjustQuantField` over multi-block transforms); `AdjustQuantBlockAC`; the AC-strategy search, which at Hare tries only DCT8, DCT4X4, DCT2X2 and IDENTITY at the 8x8 level, then the aligned merges up to 64x64 (16x8, 8x16, 16x16, 32x16, 16x32, 32x32, 64x32, 32x64, 64x64); the fast least-squares chroma-from-luma map per 64x64 tile; the block context map (`FindBestBlockEntropyModel`); Gaborish above d0.5; near-lossless DC; the `kACMeta` tree for the AC metadata and a learned tree for alpha; custom coefficient orders for every order the search uses. `FindBestQuantizer` is a no-op at Hare, so there is no butteraugli | All 18 Default rows of the calibration table at the slack of 3, through our decoder, Magick.NET and `djxl` (through our decoder each row is within 0.09 of cjxl's score at 0.98x to 1.02x its size; the smallest margin is 3.23, through Magick.NET on gradient d1); the encoder's choices read back through the decoder's parse | ≈ 2.5K | ≈ 1.8K | ≈ 0.45 |
| E5 ([#318](https://github.com/PinataLabs/ImageSharp/issues/318), [#319](https://github.com/PinataLabs/ImageSharp/issues/319), [#320](https://github.com/PinataLabs/ImageSharp/issues/320), [#321](https://github.com/PinataLabs/ImageSharp/issues/321), [#322](https://github.com/PinataLabs/ImageSharp/issues/322), [#323](https://github.com/PinataLabs/ImageSharp/issues/323), [#324](https://github.com/PinataLabs/ImageSharp/issues/324), [#325](https://github.com/PinataLabs/ImageSharp/issues/325), [#326](https://github.com/PinataLabs/ImageSharp/issues/326), [#327](https://github.com/PinataLabs/ImageSharp/issues/327)) | Lossy Thorough (libjxl e7, `kSquirrel`), every stage behind libjxl's own `speed_tier` comparison: the 8x8 candidates DCT4X8, DCT8X4 and AFV0-3, with their forward transforms (AFV's 4x4 basis is libjxl's 256 encoder literals); the non-aligned 16x16 and 32x32 merges after the aligned ones (libjxl steps by `speed_tier >= kTortoise ? 2 : 1`, so 2 at Squirrel, not 1); the chroma-from-luma DCT8 pre-pass per tile before the strategy search, and the Newton solver (`fast = false`) in the fit after it; the pixel-statistics `x_qm_scale` and `b_qm_scale` (`PixelStatsForChromacityAdjustment`; cjxl -e 7's on all 18 rows); dynamic EPF sharpness (`ComputeARHeuristics`), which reconstructs the frame inside the encoder once per EPF step through the decoder's VarDCT group decode, Gaborish and EPF (`ReconstructImage`). Squirrel's alpha tree parameters were already E2's. Splines are not a departure: libjxl v0.12's `FindSplines` returns an empty set (`enc_splines.cc:103-106`), so the step is ported as that no-op. Departure (ADR 0001): no patches or dots. Measured with cjxl -e 7 `--patches=0` against `--patches=1`, only `text` changes (1.18x the size; 0.34, 1.49 and 1.18 lower at d1, d2 and d4 by the table's scorer); our `text` encodes score the same as cjxl -e 7 `--patches=0` to 4 decimals through our decoder, at 0.998x its size, so the whole `text` gap to the table is the patches | All 18 Thorough rows of the calibration table at the slack of 3, through our decoder, Magick.NET and `djxl`; alpha exact. Without `text`, every score is within 0.28 of cjxl -e 7's at 0.99x to 1.02x its size; `text` is 0.55, 1.46 and 1.17 below at 1.17x to 1.18x (the patches). The smallest margins are `text` d2 2.04 (our decoder), 2.05 (djxl) and 2.09 (Magick.NET); `line_art`, the risk row (E5a: -9.5 at d2), passes by 3.5. Bike's whole strategy map at d0.5, d1, d2 and d4 is a no-FMA libjxl harness's (the CfL pre-pass, then `ProcessRectACS`, per tile), and agrees with cjxl -e 7 on 99.86 % to 99.95 % of its blocks (Calliphora: 100 %). On the photo set Thorough is no larger than Default, or larger by as much as cjxl -e 7 is over -e 5 (at most 1.034x, bike d4, for 1.15 points more). Mutation check: an AFV basis sign fails 13 rows; the merge step 2 -> 1 fails no row but the map and 5 pins; `kFavorNoSmoothing` 0.99 -> 1.2 changes no output (the first pass of the sharpness choice feeds only a histogram whose quotients are 0) | ≈ 0.9K | ≈ 1.2K | ≈ 0.15 |
| E6 ([#330](https://github.com/PinataLabs/ImageSharp/issues/330), [#331](https://github.com/PinataLabs/ImageSharp/issues/331), [#332](https://github.com/PinataLabs/ImageSharp/issues/332), [#333](https://github.com/PinataLabs/ImageSharp/issues/333), [#334](https://github.com/PinataLabs/ImageSharp/issues/334), [#335](https://github.com/PinataLabs/ImageSharp/issues/335)) | **Encoder release:** `JxlEncoder` (`Distance`, `Effort`) and `JxlEffort` public; `JxlConfigurationModule` registers the encoder; `SaveAsJxl` / `SaveAsJxlAsync` from the source generator; an undefined `Effort` throws `ImageFormatException`. Exif and XMP in plain `Exif` and `xml ` boxes of a container, in libjxl's layout (`JXL `, `ftyp`, [`jxll`], `jxlp` 0, `Exif`, `xml `, `jxlp` last), with an Exif or XMP box over 64 MiB refused; a level-10 codestream (`VerifyLevelSettings`) in a container with a `jxll` box, alone as `JXL `, `ftyp`, `jxll`, `jxlc`; a side over 2^30 refused; the Exif orientation carried into the codestream (`InterpretExif`). Otherwise a bare codestream, byte for byte as before. Departure from cjxl's default, not from libjxl: no `brob` | Public-API tests through `Configuration.Default` (every overload, `Save("x.jxl")`, each effort at `Distance` 0 and 1, the validation errors); the metadata round trip (lossless and lossy, with and without alpha, each with Exif, orientation 6, XMP and a Display P3 profile) through our decoder, `MagickImage`, and in `jxl-oracle` `djxl` and `jxlinfo`; every box layout equals cjxl v0.12.0 `--compress_boxes=0`'s on the same input. No codestream SHA-256 pin moved, and every calibration row still passes. See the [E6 notes](#e6-done-check-notes) | ≈ 0.15K | ≈ 0.5K | ≈ 0.03 |

### Hardening

| # | Ships | Done-check | Size | C# (est.) | Agent hours (est.) |
|---|---|---|---|---|---|
| F1 | SharpFuzz harness `tests/ImageSharp.Fuzz`, seeded with the conformance and libjxl inputs; runs locally only | One hour with no new failure; each crash it finds is committed as `tests/Images/Input/Jxl/issues/fuzz/IssueNNN-<Exception>.jxl` | test code only | — | — |
