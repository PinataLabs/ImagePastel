# libjxl decoder architecture: modules, dependency order and size

Research for [PinataLabs/ImageSharp#102](https://github.com/PinataLabs/ImageSharp/issues/102), a child of the map [#100](https://github.com/PinataLabs/ImageSharp/issues/100).

- **Source studied:** libjxl at `ef67fde2ec16d52e644c5a0969230b9a85e3eb31` (2026-10-06), local checkout `D:\Data\_CodeOS\libjxl`. BSD-3-Clause.
- **Feature list for the conformance vectors:** [libjxl/conformance](https://github.com/libjxl/conformance) README and `testcases/*/test.json`, at `4bf053529c7cefd2951be453475bb3dccc7e7be8`. libjxl's `.github/workflows/conformance.yml` pins this commit.
- **Not used:** any SixLabors/ImageSharp code (see `CLAUDE.md`).

All `file:line` references are to the libjxl checkout above. Paths are relative to `lib/jxl/` unless they say otherwise.

## Answer in brief

The decoder is about **32,500 lines of C++ code** (42,500 physical lines) in 29 components. JPEG reconstruction and the lcms/skcms glue are excluded. The components form one strict dependency chain: bit reader, then fields, then headers and the entropy coder, then Modular, then VarDCT, then the frame decoder, then the render pipeline, then the container.

The most important ordering fact is that **VarDCT depends on Modular**. The VarDCT DC image, its AC metadata (block strategy, quant field, chroma-from-luma map, EPF sharpness) and any raw quant tables are all Modular sub-streams (`dec_modular.cc:427-560`, `795+`). So Modular is the first codec to build, not an optional second one.

The **smallest end-to-end lossless Modular decoder** needs about **9,000 of those lines (about 28%)**. It has no render pipeline, no VarDCT, no squeeze and no ICC. The `delta_palette` conformance vector is the natural first target. With the ICC codec added (+614 lines), `lz77_flower` should decode too.

## How the lines were counted

- **File set:** the build's own decoder lists in `lib/jxl_lists.cmake`: `JPEGXL_INTERNAL_DEC_SOURCES`, `JPEGXL_INTERNAL_BASE_SOURCES`, `JPEGXL_INTERNAL_CMS_SOURCES` and `JPEGXL_INTERNAL_DEC_BOX_SOURCES`. The script checks that every listed file maps to exactly one component, and that no mapped file is missing from the lists.
- **Excluded:** `*_test.cc`, `*_gbench.cc`, `enc_*`, `*testonly*` and `test_*` files. Excluded components are listed under [Out of scope](#out-of-scope-code-in-the-decoder-lists) below.
- **"Code"** means non-blank lines that are not only a `//` or `/* */` comment. **"Raw"** means physical lines. Headers count with their component.
- **Script:** a Python script that globs each component's files and counts their lines. It also counts the `hwy::`/`HWY_`/`hn::` tokens, `template <` and `#define` in each component, as a rough measure of porting friction.
- **Caveats:** a few decoder files also carry code that a still-image decoder for ImageSharp does not need. `decode.cc` carries the C API's buffer and event handling and the `JPEGXL_ENABLE_TRANSCODE_JPEG` branches. `dec_group.cc` and `dec_frame.cc` carry about 30 `jpeg_data`/`IsJPEG` sites. These lines are counted, so the totals slightly overstate what needs a port.

## Dependency layers

```
L0  base utilities ............................................. (replaced by .NET mostly)
L1  bit reader
L2  fields (bundle visitor: U32/U64/F16/Enum/extensions)
L3  image headers + metadata    colour-encoding fields    entropy (prefix, ANS, hybrid uint, LZ77) + context map
L4  ICC codec (entropy-coded)   frame header + loop-filter fields + TOC (needs Lehmer permutation from coeff_order.cc)
L5  Modular core: channels, MA tree, predictors (incl. weighted), channel decode
L6  Modular transforms: RCT, palette (uses predictors), squeeze
L7  Modular frame glue: global info / groups / int->float; also VarDCT DC + AC metadata + raw quant tables
L8  VarDCT: AC strategy + block ctx map + coeff orders | quant weights + quantizer + DC smoothing | CfL | IDCT | group decode
L9  frame decoder: section scheduling, DC/AC groups, passes, reference + DC frames
L10 render pipeline: chroma upsampling > Gaborish > EPF > (EC upsampling) > patches > splines > upsampling > noise
                     > YCbCr | XYB > blending > spot colours > to-linear/tone-map/from-linear > write
L11 public API + container/box parsing (signature, ftyp, jxlc, jxlp incl. out-of-order, jxll, Exif/xml/brob)
```

The stage order in L10 comes from `PassesDecoderState::PreparePipeline` (`dec_cache.cc:120-372`).

## Component table (dependency order)

"Code (raw)" gives the C++ code lines first and the physical lines in brackets. "Deps" lists row numbers from this table. Conformance names are the `testcases/` directory names.

| # | Component | libjxl files | Code (raw) | Deps | Conformance features that need it | Hard-to-port idioms |
|---|---|---|---|---|---|---|
| 1 | Base utilities | `base/*`, `common.h`, `image.*`, `image_ops.*`, `memory_manager_internal.*`, `padded_bytes.h`, `simd_util*`, `pack_signed.h` | 3,497 (4,938) | none | all | `Status`/`StatusOr` with `JXL_RETURN_IF_ERROR`/`JXL_ASSIGN_OR_RETURN` macros (169 `#define`s in this group); aligned, padded `Plane<T>`; `fast_math-inl.h` and `rational_polynomial-inl.h` approximations. Most of this becomes `Buffer2D<T>`, `Span<T>` and exceptions. |
| 2 | Bit reader | `dec_bit_reader.*` | 215 (364) | 1 | all | 64-bit refill with unaligned loads. Reads past the end return zeros, and the error surfaces only later through `AllReadsWithinBounds()`/`Close()` (`dec_bit_reader.h:225-260`). It ports well with `BinaryPrimitives`. |
| 3 | Fields (bundle framework) | `fields.*`, `field_encodings.h` | 710 (1,098) | 2 | all headers | One `VisitFields(Visitor*)` per bundle drives reading, writing, defaults, `AllDefault` and extensions. Port only the read and default visitors. `F16` needs a manual half-float decode (no `System.Half` in netstandard2.1). |
| 4 | Image headers + metadata | `headers.*`, `image_metadata.*` | 818 (1,217) | 3 | all. Orientation (`bench_oriented_brg`, `sunset_logo`); 9/10/12/16-bit (`alpha_*`, `sunset_logo`, `spot`); 32-bit float (`lossless_pfm`); extra channels: alpha, spot (`spot`), kBlack (`cmyk_layers`); `opsin_inverse`; animation header; preview | Same as row 3. `CustomTransformData` also carries the upsampling weights. |
| 5 | Colour encoding + ICC synthesis | `color_encoding_internal.*`, `cms/color_encoding_cms.h`, `cms/jxl_cms_internal.h` | 1,872 (2,403) | 4 | all. Grey (`grayscale*`), ProPhoto (`spot`), PQ/HLG enums. The conformance harness also compares the decoder's reported ICC (`tools/conformance/conformance.py:136-197`). | Double-precision CIE maths (`PrimariesToXYZ`, `AdaptToXYZD50`); ICC writer with its own MD5 (`jxl_cms_internal.h:263`); `MaybeCreateProfile` (`:970-1160`). The decoder needs all of it; only `cms/jxl_cms.cc` is CMS glue. |
| 6 | Entropy: prefix, ANS, hybrid uint, LZ77 | `dec_ans.*`, `ans_common.*`, `ans_params.h`, `dec_huffman.*`, `huffman_table.*` | 1,324 (1,744) | 2, 3 | all; `lz77_flower` for LZ77 | Hot loops templated on `<bool uses_lz77>`. Alias tables with packed bit tricks. A 4 MiB LZ77 window per reader (`dec_ans.h:122`, `dec_ans.cc:386`). Checkpoints for progressive decoding. A hybrid uint with more than 29 bits is clamped, not rejected (`dec_ans.h:233-240`). |
| 7 | Context map + inverse MTF | `dec_context_map.*`, `inverse_mtf-inl.h` | 161 (219) | 6 | all | A Highway MTF; port it as scalar. |
| 8 | ICC profile codec | `icc_codec.*`, `icc_codec_common.*` | 614 (758) | 3, 6 | every vector with `original_icc`: `lz77_flower`, `grayscale_public_university`, `patches_lossless`, `spot`, `cmyk_layers` (large ICC), `grayscale`, `bench_oriented_brg` | Byte-wise predictive model. Straightforward. |
| 9 | Frame header, loop-filter fields, TOC, frame dimensions | `frame_header.*`, `loop_filter.*`, `toc.*`, `frame_dimensions.h` | 1,074 (1,488) | 3, 4, 6 (+ `DecodePermutation` from row 13) | all; `progressive` (TOC permutation, passes); `upsampling`; `blendmodes`; animation | Visitor pattern again. The TOC permutation uses the Lehmer-code decoder that lives in `coeff_order.cc` (`toc.cc` includes `coeff_order.h`). |
| 10 | Modular core: image, MA tree, predictors, channel decode | `modular/modular_image.*`, `modular/options.h`, `modular/encoding/*` | 1,849 (2,252) | 6, 7, 3 | every Modular vector, and every VarDCT vector through row 12 | Four fast paths besides the general one (`encoding.cc:212-330`): single-leaf, fjxl gradient RLE, gradient-only LUT and WP-only LUT. Only the general path is needed for correctness. Weighted-predictor state is templated on `<bool compute_properties>`. 32-bit samples with 64-bit predictions. A deferred overflow flag (`image.error`, commit 87bee190). Row pointers with negative offsets. |
| 11 | Modular transforms: RCT, palette, squeeze | `modular/transform/*` | 1,127 (1,435) | 10 | RCT: `sunset_logo`. Palette: `delta_palette`, `animation_newtons_cradle`. Squeeze: `bicycles`, `grayscale_public_university`, `progressive` (LF frame) | Highway in `rct.cc` and `squeeze.cc`; row loops inside `RunOnPool`. Palette calls the predictors for delta entries. Squeeze shift is limited to 30 or less (`squeeze.cc:488`). |
| 12 | Modular frame glue | `dec_modular.*` | 885 (998) | 9, 10, 11 | all Modular; VarDCT DC, AC metadata and quant tables | Highway int-to-float conversion. Custom float bit depths (`lossless_pfm`). Chooses between a full-frame image and per-group images (`dec_modular.cc:299-329`). |
| 13 | VarDCT: AC strategy, block context map, coefficient orders | `ac_strategy.*`, `ac_context.h`, `coeff_order*`, `lehmer_code.h`, `entropy_coder.*` | 715 (1,030) | 6 | VarDCT: `bike`, `cafe`, `alpha_premultiplied`, `animation_icos4d`, `grayscale`, `patches`, `progressive`, `upsampling`, `opsin_inverse` | `constexpr` lookup tables with `static_assert`. 27 strategies (`ac_strategy.h:35-79`). |
| 14 | VarDCT: quant weights, quantizer, DC dequant + adaptive smoothing | `quant_weights.*`, `quantizer*`, `compressed_dc.*` | 2,032 (2,483) | 12, 13, 16 | VarDCT vectors | Large constant parameter tables. Matrices are computed lazily, only for the strategies in use (`EnsureComputed(used_acs)`). Highway in `quant_weights.cc` and `compressed_dc.cc`. |
| 15 | Chroma-from-luma | `chroma_from_luma.*` | 142 (208) | 12 | VarDCT vectors; also splines and noise | Trivial. |
| 16 | IDCT for every block size | `dct-inl.h`, `dct_block-inl.h`, `dct_scales.*`, `dct_util.h`, `dec_transforms-inl.h`, `transpose-inl.h` | 1,763 (2,053) | 1 | VarDCT vectors; splines (32-point DCT) | Compile-time recursion `DCT1DImpl<N, SZ>` over Highway vectors (63 `template <`). SIMD transposes. AFV corner transforms and `ReinterpretingDCT`. Sizes run from 2×2 to 256×256, including rectangles, 4×8, AFV and identity. This is the hardest numeric module, but a scalar recursive port is compact. |
| 17 | VarDCT group decode | `dec_group.*` | 748 (891) | 13-16, 6 | VarDCT vectors; `progressive` (multiple passes) | Coefficient buffers templated on `int16_t` or `int32_t` (`dec_frame.cc:413-423`). Highway dequantisation loop. |
| 18 | Frame decoder, sections, decoder cache | `dec_frame.*`, `dec_cache.*`, `passes_state.*`, `dec_group_border.*`, `image_bundle.*` | 2,004 (2,667) | 9, 12, 17, 19 | all; `progressive` (passes, LF frame); `patches`/`blendmodes`/animation (reference slots); preview | `RunOnPool` with a per-thread storage callback (`dec_frame.cc:649`, `:720`, `:779`). Large mutable state. |
| 19 | Render pipeline core + output | `render_pipeline/render_pipeline*`, `simple_render_pipeline.*`, `low_memory_render_pipeline.*`, `stage_write.*`, `dec_external_image.*` | 2,758 (3,460) | 1 | all | Two implementations. Virtual stages over `std::vector<std::vector<float*>>` rows, with 32 floats of left padding, or 24 on ARM (`kRenderPipelineXOffset`, `render_pipeline_stage.h:26-30`). Highway in `stage_write` (float to u8/u16/f16, un-premultiply, orientation). |
| 20 | XYB / YCbCr / opsin | `dec_xyb*`, `opsin_params.*`, `cms/opsin_params.h`, `stage_xyb.*`, `stage_ycbcr.*` | 879 (1,263) | 5, 19 | `bicycles` (XYB), `opsin_inverse`, all lossy VarDCT; YCbCr for `cafe`, `grayscale_jpeg`, `bench_oriented_brg` | Highway; `cbrtf` (`MathF.Cbrt` exists in netstandard2.1). The XYB-to-target primaries matrix is folded in analytically (`dec_xyb.cc:181-250`). |
| 21 | Transfer functions (sRGB, 709, gamma, DCI, PQ, HLG) + tone mapping | `cms/transfer_functions*`, `cms/tone_mapping*`, `stage_to_linear.*`, `stage_from_linear.*`, `stage_tone_mapping.*`, `stage_cms.*` | 1,275 (1,692) | 5, 19 | any XYB vector whose output is not linear; HDR (PQ/HLG); `spot` (ProPhoto gamma) | The heaviest Highway use (166 tokens). Rational-polynomial and fast-pow approximations. `TF_PQ`/`TF_HLG` live in `cms/transfer_functions-inl.h:53,135`; `Rec2408ToneMapper` and `HlgOOTF` in `cms/tone_mapping-inl.h:33,102`. `stage_cms` only matters when a CMS is attached. |
| 22 | Gaborish | `stage_gaborish.*` | 113 (154) | 19 | most lossy VarDCT vectors (encoder default) | A Highway 3×3 convolution. Easy as scalar. |
| 23 | EPF (edge-preserving filter) | `epf.*`, `stage_epf.*` | 564 (749) | 19, 14 | lossy VarDCT; lossy Modular (`epf_sigma_for_modular`) | Highway. Three passes with different borders. Sigma comes from the quant field and sharpness. |
| 24 | Upsampling (2×/4×/8×) + chroma upsampling | `stage_upsampling.*`, `stage_chroma_upsampling.*` | 402 (502) | 19, 4 | `upsampling`; `cafe` (4:2:0) | Highway. Weights come from `CustomTransformData`. A recent OOB fix landed here (045d83ae). |
| 25 | Patches | `dec_patch_dictionary.*`, `patch_dictionary_internal.h`, `stage_patches.*` | 500 (645) | 6, 18, 28 | `patches`, `patches_lossless` | Reads saved reference frames. Count limits at `dec_patch_dictionary.cc:56-103`. |
| 26 | Splines | `splines.*`, `stage_splines.*` | 854 (1,060) | 6, 15, 16 | `animation_spline` | Fast-math approximations (`FastErff`, `FastCosf`). Area and count limits (`splines.cc:221-694`). Precision sensitive. |
| 27 | Noise | `dec_noise.*`, `noise.h`, `xorshift128plus-inl.h`, `stage_noise.*` | 555 (739) | 6, 15 | `noise` | The RNG is 8 fixed xorshift128+ lanes (`xorshift128plus-inl.h:34`), so it is portable as scalar. It needs a little-endian u64-to-u32 reinterpretation (`dec_noise.cc:80`). |
| 28 | Blending, alpha, spot colours | `blending.*`, `alpha.*`, `stage_blending.*`, `stage_spot.*` | 619 (778) | 19 | `blendmodes`, `sunset_logo`/`spot`/`cmyk_layers` (layers), animations, `alpha_premultiplied`, `spot` | Mostly plain loops. |
| 29 | Public API + container/box parsing | `decode.cc`, `box_content_decoder.*` | 2,477 (3,177) | all | container: `bench_oriented_brg`, `cafe`, `grayscale_jpeg`; animation sequencing and coalescing; Exif/XMP | A large event-driven C state machine. ImageSharp reads from a `Stream` and returns an image, so only box parsing (`HandleBoxes`, `decode.cc:1639-2163`), the frame loop and coalescing carry over; I estimate a quarter of the file. It handles out-of-order `jxlp` (`decode.cc:547-585`). `brob` boxes need Brotli; `System.IO.Compression.BrotliDecoder` is in the netstandard2.1 surface (checked in `NETStandard.Library.Ref/2.1.0/.../netstandard.xml`). |
|  | **In-scope total** |  | **32,546 (42,465)** |  |  |  |

### Out of scope code in the decoder lists

| Code | Code (raw) | Why it is excluded |
|---|---|---|
| JPEG reconstruction: `decode_to_jpeg.*`, `jpeg/dec_jpeg*`, `jpeg/jpeg_data.*` | 2,084 (2,588) | `jbrd` is out of scope on the map. |
| `cms/jxl_cms.cc` (lcms2/skcms glue) | 1,115 (1,397) | The map rules out a general CMS. |
| `convolve*` | 396 (526) | It is in `DEC_SOURCES`, but only `butteraugli` and the encoder include it. |
| `luminance.*` | 21 (49) | Only `encode.cc` uses it. |

## Smallest subset that decodes a lossless Modular image end-to-end

**Target:** a single-frame, lossless Modular codestream or container with enum colour (no ICC), `color_transform = kNone`, no XYB, no squeeze, no filters, no upsampling, and no patches, splines or noise.

| Part | Source | C++ code lines needed |
|---|---|---|
| Bit reader | row 2 | 215 |
| Fields: U32/U64/F16/Enum, extensions | row 3 | 710 |
| SizeHeader, ImageMetadata, extra-channel info, bit depth | row 4 | 818 |
| ColorEncoding fields only (no ICC synthesis) | `color_encoding_internal.*` + part of `cms/color_encoding_cms.h` | ~600 |
| Prefix codes, ANS, hybrid uint, LZ77 | row 6 | 1,324 |
| Context map + MTF | row 7 | 161 |
| Lehmer permutation (TOC may be permuted) | `DecodePermutation` in `coeff_order.cc` + `lehmer_code.h` | ~70 |
| Frame header, loop-filter fields, TOC, frame dimensions | row 9 | 1,074 |
| DC quant-matrix header (`DequantMatrices::DecodeDC`; read in every DC-global section, `dec_frame.cc:289`) | part of `quant_weights.cc` | ~25 |
| Modular core, general decode path only | row 10 | 1,849 |
| RCT + palette (with delta entries) + transform dispatch; no squeeze | row 11 minus `squeeze*` | ~650 |
| Modular glue: `DecodeGlobalInfo`, `DecodeGroup`, `FinalizeDecoding`, int-to-sample | part of `dec_modular.cc` | ~450 |
| Section scheduling for Modular only (DC global, LF groups, AC global, groups) | part of `dec_frame.cc`, `dec_cache.*`, `passes_state.*` | ~500 |
| Output: integer samples straight into `Image<TPixel>`, with no render pipeline | replaces rows 19-28 | ~250 |
| Container: signature, `ftyp`, `jxlc`, in-order `jxlp`, skip other boxes | part of `decode.cc` | ~250 |
| Base helpers actually used (bit maths, `UnpackSigned`, F16) | part of row 1 | ~200 |
| **Total** |  | **~9,150, about 28% of the decoder** |

The subset should **reject cleanly** (throw), never decode badly, any of these: VarDCT; XYB or YCbCr; squeeze; `gab` or `epf_iters > 0`; `upsampling != 1`; chroma subsampling; patches, splines or noise flags; non-replace blending or cropped frames; more than one pass; DC frames; ICC (until row 8 is added).

**Expected first passes**, based on the conformance README's "what it tests" column and each `test.json`. Confirm them in the spike, because the README calls its table a rough overview.

| Vector | Why it should pass |
|---|---|
| `delta_palette` | 8-bit, no ICC, no extra channels |
| `alpha_nonpremultiplied`, `alpha_triangles` | add alpha as an extra channel; 12-bit and 9-bit |
| `lossless_pfm` | 32-bit float samples through the `int_to_float` path |
| `lz77_flower` | needs the ICC codec too (+614 lines) |

`sunset_logo` and `blendmodes` add two layers and blending, so they belong in the next slice.

## Idioms that will port awkwardly to C#

| Idiom | Where | Suggested C# approach |
|---|---|---|
| **Highway SIMD**: `foreach_target.h` re-inclusion, `HWY_EXPORT`, `HWY_DYNAMIC_DISPATCH`, `-inl.h` headers compiled once per target | 25 decoder `.cc` files plus 11 `-inl.h` headers; worst in rows 19-21, 25 and 27 | Write scalar code first. Leave a seam for `Vector128`/`Vector256` on net10.0 behind `SUPPORTS_RUNTIME_INTRINSICS`; the repo already uses this in 41 files. netstandard2.1 has `System.Numerics.Vector<T>` but no `System.Runtime.Intrinsics` (checked in the reference XML). |
| **Compile-time specialisation**: `DCT1DImpl<N, SZ>`, `template <bool uses_lz77>`, `<bool compute_properties>`, `ACImageT<int16_t/int32_t>`, `Plane<T>`, `TreeLut<...>` | rows 6, 10, 16, 17 | Use generic struct "flag" parameters, which the JIT specialises on both target frameworks. For the DCT, use runtime size with the same recursion and cached twiddles. |
| **Status macros**: `JXL_RETURN_IF_ERROR`, `JXL_ASSIGN_OR_RETURN`, `JXL_FAILURE`, `JXL_ENSURE`, `StatusOr` | everywhere | Throw `InvalidImageContentException`. Keep hot loops free of throws, and copy libjxl's deferred checks: `image.error`, `AllReadsWithinBounds`, `CheckANSFinalState`. |
| **Fields visitor**: one `VisitFields` for every operation | rows 3, 4, 5, 9 | Port the visitor with only the read and default visitors, so defaults are written once. |
| **Raw row pointers**: negative offsets (`r - 1`, `rtop = r - 1`), padded borders, `JXL_RESTRICT` | rows 10, 11, 19, 22-24 | Use explicit padding with `Span<T>` slices or `ref T` + `Unsafe.Add`. Read borders on purpose, not by over-reading. |
| **Intentional over-read**: the bit reader returns zeros past the end; `PaddedBytes`; vector-padded planes | rows 1, 2 | Handle the tail explicitly and keep the "check later" semantics. |
| **`RunOnPool(begin, end, init(threads), fn(i, thread))`** with per-thread scratch | rows 11, 12, 14, 18, 19 | `Parallel.For` with thread-local state, honouring `Configuration.MaxDegreeOfParallelism`, or a plain loop. See the parallelism section below. |
| **Numerics**: float/u32 bit casts, half floats, `cbrtf`, signed-overflow detection builtins, CLZ/CTZ | rows 1, 3, 10, 20 | `BitConverter.Int32BitsToSingle` and `MathF.Cbrt` exist in netstandard2.1. `System.Half` and `BitOperations` do not, so a hand-written F16 decode and CLZ are needed. C# `unchecked` wraparound replaces C++ undefined behaviour, but `SubOverflow` needs an explicit check. |
| **Fast-math approximations**: `FastPowf`, `FastLog2f`, `FastCosf`, `FastErff`, rational polynomials | rows 21, 26 | Port the same approximations for parity with libjxl, or use exact `MathF` and accept a tiny drift within conformance tolerance. Decide once, per module. |
| **Large constant tables**: quant-weight defaults, DCT scales, 120 LZ77 special distances, AC strategy lookup tables, upsampling kernels | rows 6, 13, 14, 16, 24 | `static ReadOnlySpan<byte>` for byte data; `static readonly` arrays otherwise. netstandard2.1 cannot embed non-byte spans. |
| **Endianness assumptions**: `memcpy` u64 to u32 in noise, `byte_order.h` loads | rows 1, 27 | `BinaryPrimitives` and `MemoryMarshal.Cast` (little-endian). |

## Parallelism and memory model in libjxl

These notes are input to the map's "Parallelism and memory model" fog.

**Order of work in a frame** (`dec_frame.cc:565-727`):

1. DC global: patches, splines, noise, DC quant, the global MA tree and histograms, the global Modular image header.
2. DC groups (2048×2048 at default sizes), **in parallel**.
3. `FinalizeDC`: adaptive DC smoothing, which needs every DC group.
4. AC global: quant matrices, coefficient orders and histograms for each pass.
5. AC groups (256×256 by default), **in parallel**, each with per-thread scratch.
6. Render-pipeline input for each group.
7. `FinalizeDecoding`: undo the global Modular transforms. This step is serial over groups, with row loops in parallel inside it.

The TOC gives byte offsets for every section, so sections are independent once the globals are decoded.

**Memory held during a decode:**

- **Modular** keeps a full-frame `int32` image (4 bytes per sample per channel) when transforms stay global, such as a global palette or squeeze, or when a channel is larger than a group. Otherwise it decodes into per-group images and drops the full image (`dec_modular.cc:299-329`). Only a lone RCT moves to group level.
- **VarDCT** keeps the DC image at 1/64 size, per-block metadata, and AC coefficients. Coefficients are per-thread scratch for a single-pass image. With more than one pass they are a full-frame store of `int16` or `int32` (`dec_frame.cc:413-426`).
- **Reference and DC frames:** up to 4 reference slots (`common.h:40`) and up to 4 DC-level frames, all full-frame float images. Patches, blending and animation use them.
- **LZ77:** a 4 MiB window per active symbol reader, so per thread.
- **Render pipeline:** `LowMemoryRenderPipeline` (776 code lines) processes each group with border rows; `dec_group_border.cc` lets neighbours finish the filters. `SimpleRenderPipeline` (258 lines) holds a full-frame float plane per channel and allocates new planes for each in/out stage. It is selected with `use_slow_render_pipeline`, the reference path used by tests.

**Implication for ImageSharp.** At the Level 5 maximum of 2^28 pixels, one float RGB frame is 3 GiB. That is beyond this fork's default allocator limits: a 1 GiB single buffer and a 4 GiB memory group on 64-bit (`src/ImageSharp/Memory/Allocators/MemoryAllocator.cs:28-30`). A full-frame, serial "simple pipeline" design is the right correctness-first start. It costs memory, not correctness, and the allocator turns oversized images into a clean `InvalidMemoryOperationException`. Keep the group loop shaped so `Parallel.For` and a row-ring pipeline can come later. The existing managed codecs in `src/ImageSharp/Formats` decode on a single thread; none uses `Parallel.For`.

## Decoder hardening bounds found in libjxl

These notes are input to the map's "Decoder hardening" fog. libjxl has **no pixel-count cap of its own**. `CheckSizeLimit` only guards against overflow (`decode.cc:725-733`), and `SizeHeader` caps each dimension at `uint32` (`headers.cc:68`). Callers set the caps: the `djxl` fuzzer allows 2^21 pixels per frame and 5×2^21 in total (`tools/djxl_fuzzer.cc:588-589`).

| Bound | Value | Where |
|---|---|---|
| Level 5 | each side ≤ 2^18, pixels ≤ 2^28, ICC ≤ 2^22 bytes, ≤ 4 extra channels, no kBlack, 16-bit Modular buffers | `encode.cc:587-612` (encoder-side check; the decoder does not enforce it) |
| Level 10 | each side ≤ 2^30, pixels ≤ 2^40, ICC ≤ 2^28 bytes, ≤ 256 extra channels | `encode.cc:571-585` |
| TOC entries | ≤ 65,536 | `toc.cc:33` |
| MA tree nodes | ≤ min(2^22, 1024 + xsize·ysize·channels/16) | `dec_modular.cc:226-229`, `modular/encoding/ma_common.h:24`; property-range validation at `dec_ma.cc:39` |
| Context-map clusters | ≤ 256 | `dec_context_map.cc:25` |
| ANS alphabet | ≤ 256 (`log_alpha_size` 5-8) | `ans_params.h:24`, `dec_ans.cc:367` |
| Prefix alphabet | ≤ 2^15 | `ans_params.h:27`, `dec_ans.cc:202-208` |
| LZ77 window | 2^20 symbols (4 MiB) per reader; length-overflow fix in 577d83f0 | `dec_ans.h:122`, `dec_ans.cc:386` |
| Passes / reference frames | ≤ 11 / 4 | `common.h:37,40` |
| Squeeze shift | ≤ 30 | `modular/transform/squeeze.cc:488` |
| Patches | reference patches ≤ 1024 + pixels/4; positions ≤ 4× that; blend infos ≤ 4× positions | `dec_patch_dictionary.cc:56-103` |
| Splines | count and control points ≤ min(2^20, pixels/2); total area limit | `splines.cc:234-235, 446-546, 606-613, 694` |
| ICC | decoded ≤ 2^28 bytes; encoded-size sanity check; varint ≤ 10 bytes | `icc_codec.cc:82-113, 346` |
| Exif/XMP (JPEG-reconstruction path) | ≤ 64 MiB | `decode.cc:1716-1733` (ed2d529d) |
| Extra-channel upsampling, `dim_shift` | range checks | `frame_header.cc:285`, `image_metadata.cc:236` |

Recent decoder-side fixes worth reading as a hardening checklist (`git log` since 2025-01):

- 22ad80af: padding offset on ARM, upsampling offsets, consistent upsampling with patches.
- 577d83f0: LZ77 length overflow.
- 87bee190: Modular residual overflow.
- 045d83ae: upsampling temp buffer OOB.
- 30b0d63a: noise parameter limits.
- ef555aa6: stack limits.
- 381a0a41: box-storage memory tracking.
- f7c50c40: progressive lossless with multiple threads.

A managed port avoids the memory-safety class of these bugs. It still inherits the denial-of-service class (huge allocations, quadratic work), so the bounds above should be ported as explicit checks.

## Colour notes found along the way

- **XYB to any enum colour space needs no CMS.** libjxl folds a primaries matrix, with Bradford adaptation to D50, into the inverse opsin matrix (`dec_xyb.cc:194-226`). It applies the transfer function afterwards. Supported transfer functions are sRGB, linear, gamma, PQ, HLG, DCI and 709 (`CanOutputToColorEncoding`, `dec_xyb.cc:127-142`). This confirms the map's colour decision for enum spaces.
- **XYB with an ICC colour encoding decodes to linear sRGB when no CMS is set** (`dec_xyb.cc:160-164`). Attaching the embedded ICC unconverted to those pixels would mislabel them. This is input for #105 ("ICC-only images").
- **The conformance harness compares against `reference.icc` and converts with lcms2 when the ICCs differ** (`tools/conformance/conformance.py:32-62`). It also checks `original.icc` byte for byte. So the decoder must report an ICC for its output encoding, which needs the enum-to-ICC writer `MaybeCreateProfile` in `cms/jxl_cms_internal.h`. This is input for #101 and #105.

## Comparable managed codec in this repo

`src/ImageSharp/Formats/Webp` is laid out by concern: `BitReader/`, `Lossless/`, `Lossy/`, and top-level `WebpDecoderCore`/`WebpEncoderCore`, options, metadata and constants. It is about 27,600 C# lines for decode and encode together. A JPEG XL tree could mirror that layout:

- `Jxl/BitReader`
- `Jxl/Fields`
- `Jxl/Entropy`
- `Jxl/Modular` (with `Transforms/`)
- `Jxl/VarDct`
- `Jxl/Features` (patches, splines, noise)
- `Jxl/Render` (stages)
- `Jxl/Color`
- `Jxl/Container`
- `JxlDecoderCore`

## Recommendations

1. **The porting spike is warranted. Make it the first real slice, not throwaway code.** Every part of the minimal Modular subset is needed later anyway. Time-box it, and use it to measure three things before trusting the slice sizes:
   - the C#-to-C++ line ratio;
   - the time spent per 1,000 lines;
   - whether the general MA-tree decode path is fast enough without libjxl's four fast paths.

   Target `delta_palette`, then the `alpha_*` vectors and `lossless_pfm`.
2. **Build in dependency order**:
   1. bit reader, fields and headers
   2. entropy
   3. Modular, then transforms (RCT and palette, then squeeze)
   4. frame and TOC, then container
   5. ICC codec
   6. blending and animation
   7. VarDCT: strategies and orders, quant weights and DC, IDCT, group decode
   8. Gaborish, EPF and upsampling
   9. XYB, transfer functions and tone mapping
   10. patches, splines and noise
   11. progressive passes and DC frames
3. **Start serial, with a full-frame simple pipeline**, and keep the group loop shaped for `Parallel.For` later.
4. **Port the hardening bounds table as explicit checks.** Decide a default pixel cap, because libjxl leaves that to the caller.
