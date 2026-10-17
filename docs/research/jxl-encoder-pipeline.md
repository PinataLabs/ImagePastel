# libjxl encoder: minimal pipeline for lossless Modular and lossy VarDCT

Research for PinataLabs/ImageSharp#103 (map: #100).

- **Source:** `D:\Data\_CodeOS\libjxl` at commit `ef67fde2ec16d52e644c5a0969230b9a85e3eb31` (2026-10-06). BSD-3-Clause.
- **Paths:** all paths below are relative to `libjxl/`. Line numbers refer to that commit.
- **Licensing:** no SixLabors/ImageSharp code was read for this note.

## Answer in one paragraph

In libjxl, `effort = 10 - speed_tier` (`lib/jxl/encode.cc:1884-1885`). Every encoder stage is gated by comparing `cparams.speed_tier` against a `SpeedTier` value (`lib/jxl/common.h:42-71`). Lossless effort 1 is a separate, self-contained encoder (`enc_fast_lossless.cc`). Every other effort runs the general `EncodeFrame` pipeline. Lossy VarDCT itself needs the Modular encoder, because the DC image and the AC metadata are Modular streams. At low effort they use predefined MA trees, so no tree learning is needed. So the smallest useful encoder is one shared **encoder core** with five parts: bit writer, header field writer, ANS writer, Modular encoder with predefined trees, and frame/TOC assembly. Lossless and lossy are thin layers on that core. We recommend **three ImageSharp effort levels** that copy libjxl **e3, e5 and e6/e7 without patches**. We do not port the fast-lossless e1 encoder, patches and dots, butteraugli iteration, or e8 and higher.

## 1. Effort level → stage mapping (traced in code)

### 1.1 How effort becomes a speed tier

| Effort | `SpeedTier` | Value | Notes |
|---|---|---|---|
| 1 | `kLightning` | 9 | Lossless: `enc_fast_lossless` if `CanDoFastLossless` passes (`encode.cc:2530-2580`). In all other cases, `EncodeFrame` changes it to `kThunder` (`enc_frame.cc:2587-2589`). |
| 2 | `kThunder` | 8 | |
| 3 | `kFalcon` | 7 | |
| 4 | `kCheetah` | 6 | |
| 5 | `kHare` | 5 | |
| 6 | `kWombat` | 4 | |
| 7 | `kSquirrel` | 3 | default (`enc_params.h:42`, `cjxl_main.cc:581`) |
| 8 | `kKitten` | 2 | |
| 9 | `kTortoise` | 1 | |
| 10 | `kGlacier` | 0 | |
| 11 | `kTectonicPlate` | -1 | expert flag only. Lossless only; lossy falls back to Glacier (`enc_frame.cc:2581-2584`). |

Mapping: `encode.cc:1872-1886`. Lower tier value means more effort, so a `<= kHare` test means "effort ≥ 5".

`doc/encode_effort.md` has a summary table. The table below is built from the code. Where the doc and the code disagree, the code wins and §1.4 notes the difference.

### 1.2 Lossless Modular: what switches on, per effort

| Effort | Stage that switches on | Code |
|---|---|---|
| 1 | Separate fast-lossless encoder: fixed YCoCg, ClampedGradient predictor, simple palette detection, prefix (Huffman) codes, RLE. It writes its own frame header, TOC and bit writer. It is used only for ≤16-bit, ≤1 extra channel (alpha), no animation, no crop, and no frame name. | `encode.cc:2530-2590`, `2660-2684`, `enc_fast_lossless.cc` |
| 2 | General path. One fixed tree for all streams: `PredefinedTree(kGradientFixedDC)` with the Gradient predictor. Global YCoCg. Global palette and channel palette are tried at every effort. ANS with fast clustering and "approximate" histograms. No LZ77, default hybrid-uint. | tree `enc_modular.cc:1225-1246`; predictor `:642-650`; YCoCg `:905-911`; palette `:890-902`; histograms `enc_ans.cc:1350-1366` |
| 3 | Fixed tree `kWPFixedDC` with the Weighted predictor | `enc_modular.cc:645-647`, `:1238-1240`, `:681-684` |
| 4 | **MA tree learning** (`LearnTree`), predictor `Best` (Gradient and Weighted both tried), 3 tree properties, `nb_repeats × 0.3` | `enc_modular.cc:642-644`, `:1173-1224`, `:589-595` |
| 5 | Local (per-group) palette; **patches** (`FindBestPatchDictionary` at `< kCheetah`); RCT search over 4 candidates; **RLE LZ77**; hybrid-uint "fast" search; 4 properties, 48 values | `enc_modular.cc:1429-1436`, `:710-718`, `:1440-1455`; `enc_ans.cc:1356-1365` |
| 6 | 5 RCTs; 5 properties, 64 values | `enc_modular.cc:568-572`, `:1457` |
| 7 | 7 RCTs; 7 properties, 96 values. Palette and RCT choices are checked by `EstimateCost` instead of a fixed bpp guess. | `enc_modular.cc:574-578`, `:346-350`, `:406-410`, `:314` |
| 8 | 9 RCTs; 10 properties; 2 WP modes tried; LZ77 `kLZ77b3w3f`; best clustering, best hybrid-uint, precise ANS histograms | `enc_modular.cc:579-584`, `:1528-1532`; `enc_ans.cc:1370-1371`, `enc_ans_params.h:73-87` |
| 9 | 19 RCTs; all properties, including all previous-channel ones; 5 WP modes; optimal LZ77 (`kOptc256`); "group" property kept | `enc_modular.cc:585-604`, `:1466-1469`, `:1528-1530`, `:551` |
| 10 | Predictor `Variable` (all predictors); one global MA tree (no chunked/local trees) | `enc_modular.cc:628-634`; `enc_frame.cc:1699-1708`, `:1818` |
| 11 | Brute force over several e10 parameter sets | `enc_frame.cc:2590-2657` |

### 1.3 Lossy VarDCT: what switches on, per effort

Some stages depend on distance, not effort, and run at every effort:

- **EPF iterations** go up by one at each threshold d ≥ 0.7, 1.5, 4.0 (`enc_frame.cc:325-339`). At e ≤ 5 the encoder only writes a constant sharpness map of 4 (`enc_heuristics.cc:904-909`). The filter itself is decoder work.
- **2× resampling** applies at d ≥ 10 (`enc_frame.cc:104-117`).
- **x/b QM scale** comes from distance (`enc_frame.cc:680-686`).

| Effort | Stage that switches on | Code |
|---|---|---|
| 1-2 | DCT8 only (`FillDCT8`); constant quant field `q = 0.79/d`; default CfL map; default dequant matrices; default coefficient orders. DC and AC metadata use predefined Modular trees (`kWPFixedDC`, `kFalconACMeta`). AC uses prefix codes, because clustering `kFastest` forces Huffman (`enc_ans.cc:1169-1172`). | `enc_ac_strategy.cc:1142-1146`, `:1076-1078`; `enc_heuristics.cc:1098-1115`; `enc_coeff_order.cc:42`; `enc_modular.cc:1595-1597`, `:1752-1754` |
| 3 | Fast histogram clustering, so AC is ANS-coded (`HistogramParams(tier)` at `> kFalcon` vs `> kTortoise`) | `enc_ans_params.h:72-79` |
| 4 | **Coefficient reordering** (`ComputeUsedOrders`). **Near-lossless DC**: DC quantised with WP prediction and one extra bit of precision. Block-context-map search (`FindBestBlockEntropyModel`). AC-metadata tree `kACMeta`. | `enc_coeff_order.cc:42`; `enc_frame.cc:1180-1189`, `:232-236`; `enc_modular.cc:1645-1679`; `enc_heuristics.cc:1220-1223`, `:89-91`; `enc_modular.cc:1755-1757` |
| 5 | **Adaptive quantisation** (`InitialQuantField`, `q = 0.39/d`). **AC-strategy search**: 8×8 variants DCT, DCT4×4, DCT2×2, IDENTITY, plus aligned merges up to 64×64. **Gaborish** (inverse applied on encode, if d > 0.5). **Chroma-from-luma** (fast). `AdjustQuantBlockAC`. All dequant matrices computed. | `enc_heuristics.cc:1098-1129`, `:1132-1142`, `:1191-1195`; `enc_ac_strategy.cc:855`, `:525-576`, `:1030-1032`, `:1076-1087`; `enc_frame.cc:316-321`; `enc_group.cc:337` |
| 6 | Full AC-strategy search: non-aligned 16×8/16×16/32×32, plus DCT4×8, DCT8×4 and AFV0-3. **Dynamic EPF sharpness** (`ComputeARHeuristics`). CfL still "fast". | `enc_ac_strategy.cc:1030-1058`, `:525-576` (limit 4); `enc_heuristics.cc:904-909`, `:1194` |
| 7 | **Patches**, and **dots** if d ≥ 3. CfL computed before ACS as well as after. Full CfL. Pixel-stats chroma adjustment. Spline search (a stub, see below). "Error diffusion" flag (unused, see below). | `enc_heuristics.cc:1049-1066`, `:1170-1177`; `enc_patch_dictionary.cc:631-643`; `enc_frame.cc:691-693`; `enc_group.cc:419` |
| 8 | **Butteraugli iteration**: `FindBestQuantization`, 2 iterations, each a full decoder round trip (`RoundtripImage`). It needs the linear image (`enc_frame.cc:1608-1613`). | `enc_adaptive_quantization.cc:1282-1286`, `:926-984`, `:840` |
| 9 | 4 butteraugli iterations; ACS 32×32 step 2 → 1 | `enc_adaptive_quantization.cc:982-984`; `enc_ac_strategy.cc:1046` |
| 10 | Iterative downsampling (only matters at d ≥ 10); no chunked encoding | `enc_frame.cc:759-765`; `enc_frame.cc:1818` |

### 1.4 Where the code and the docs disagree

These are decision-relevant: they are things we should *not* port.

- **Error diffusion is a no-op.** `encode_effort.md` and `common.h:55` both say e6 turns on error diffusion. In the code, the flag is set at e ≥ 7 (`enc_group.cc:419`). `QuantizeBlockAC` takes the parameter but never reads it (`enc_group.cc:58-102`).
- **Spline detection is a stub.** `FindSplines` returns an empty set (`enc_splines.cc:103-106`). Splines are a decoder feature only.
- **Noise is always off by default.** `kMinButteraugliForNoise = 99.0f` (`enc_params.h:200-201`). Noise is set only by `--photon_noise_iso` or manual noise.
- **e1 lossless is conditional.** The doc lists it as fast-lossless. In code it falls back to e2 behaviour when the fast path does not apply.
- **e4 VarDCT does more than the doc says.** The doc lists only "coefficient reordering". The code also switches on near-lossless DC, the block-context-map search and the `kACMeta` tree (§1.3).
- **VarDCT with alpha learns a tree at every effort.** Only `stream_options_[0]` gets a fixed tree at e2/e3 (`enc_modular.cc:681-684`). The group streams that hold the extra channel keep the default `kLearn` (`options.h:112`, `enc_modular.cc:678`). So libjxl runs `LearnTree` for alpha in lossy files. At e4 and higher this always happens. At e2/e3 it happens when the image is larger than one group, because the alpha then sits in the group streams and not in stream 0. Our encoder may use a predefined tree there instead: the bitstream allows any tree.

## 2. What the encoder shares with the decoder

libjxl keeps one copy of several pieces and uses it in both directions. A C# port should do the same, so each piece is written once.

| Shared component | libjxl files (the decoder must have these anyway) | Phys. lines | How the encoder uses it |
|---|---|---|---|
| **Header bundles** (SizeHeader, ImageMetadata, ColorEncoding, FrameHeader, LoopFilter, Quantizer params, etc.) | `fields.{cc,h}`, `field_encodings.h`, `headers.*`, `image_metadata.*`, `frame_header.*`, `color_encoding_internal.*`, `loop_filter.*` | ≈4,000 | Each bundle has one `VisitFields`. Read, write, size and `AllDefault` are visitors over it. The encoder side is only `enc_fields.cc` (the `Write` visitor and the U32/U64/F16 coders, 294 lines). **Design implication:** model headers as one visitor per bundle in C#. |
| **Entropy-code parameters** | `dec_ans.h` (`HybridUintConfig`), `ans_common.*`, `ans_params.h`, `lehmer_code.h`, `pack_signed.h` | ≈900 | Token → (symbol, extra bits) split; ANS table size; flat histograms; packing of signed values. The ANS *writer*, histogram normalisation and clustering are encoder-only (§3). |
| **Modular predictors and properties** | `modular/encoding/context_predict.h` (Gradient, Weighted predictor state, property computation) | 712 | The encoder must compute exactly the same predictions and properties to make residuals. |
| **MA tree model** | `modular/encoding/dec_ma.*`, `ma_common.h`, `modular/options.h` | ≈400 | Tree node type. The encoder checks its tokenised tree by decoding it back (`enc_modular.cc:1250-1254`). |
| **Modular image and transforms** | `modular/modular_image.*`, `modular/transform/{transform,rct,palette,squeeze}.*` | ≈1,500 | Transform metadata bundles; inverse transforms, used when a trial transform is undone (`enc_modular.cc:308-331`). |
| **VarDCT tables and geometry** | `ac_strategy.*`, `ac_context.h`, `coeff_order.*`, `frame_dimensions.h`, `passes_state.*`, `toc.*` | ≈1,200 | Block types; zig-zag/natural orders; block-context map; group and DC-group layout. |
| **Dequant matrices and quantizer** | `quant_weights.*`, `quantizer.*`, `quantizer-inl.h` | ≈2,100 | The encoder needs the same matrices as the decoder, plus their inverses (`InvDequantMatrix`, `enc_group.cc:63`). |
| **DCT** | `dct-inl.h`, `dct_block-inl.h`, `dct_scales.*`, `dec_transforms-inl.h` | ≈1,900 | `dct-inl.h` is the forward DCT. It is *also* used by the decoder (`dec_transforms-inl.h:22`) to rebuild the lowest frequencies from DC. The ACS and CfL heuristics use the inverse transforms to estimate cost (`enc_ac_strategy.cc:33`, `enc_chroma_from_luma.cc:36`). |
| **DC dequantisation** | `compressed_dc.cc` (`DequantDC`) | 324 | After it quantises DC, the encoder runs the decoder's DC dequantisation to get the reconstructed DC (`enc_modular.cc:1730-1734`). |
| **Chroma-from-luma, XYB constants, transfer functions** | `chroma_from_luma.*`, `cms/opsin_params.h`, `opsin_params.cc`, `cms/transfer_functions*.h` | ≈900 | CfL bundle and factors; XYB matrix and bias; sRGB/PQ/HLG curves for linearisation before XYB. |
| **Decoder as a library** (only for e8+) | `dec_group.cc`, `dec_frame.cc` | — | Butteraugli iteration (`enc_adaptive_quantization.cc:840`), patch search and progressive DC run the decoder. We do not need any of these. |

Counted shared total: **≈16,500 physical / ≈12,600 SLOC** of decoder-side files that the encoder reuses unchanged.

**Not shared (mirror pairs that each side writes):**

- ANS/prefix *writer* vs reader
- context-map writer (`enc_context_map.cc`) vs reader (`dec_context_map.cc`)
- tree tokenise vs tree decode
- forward vs inverse RCT and palette
- Gaborish inverse (5×5 symmetric convolution, `convolve_symmetric5.cc`) vs the decoder's 3×3 Gaborish stage

## 3. Line counts per component

### 3.1 How we counted

- **Phys.** = physical lines in the libjxl file, including licence header, includes and comments.
- **SLOC** = non-blank lines that are not only a `//` or `/* */` comment. A small script counted them. Test (`*_test.cc`) and benchmark (`*_gbench.cc`) files are left out.
- **Needed (est.)** = an estimate of the physical lines in the functions on that effort path. It is the sum of the function line ranges from an outline of each file, minus branches the path never takes: JPEG transcoding, streaming/chunked encoding, debug/aux output, lossy palette, Squeeze, SIMD-only duplicates. It is a C++ estimate. Treat it as ±25%. A C# port without Highway boilerplate should land about the same, or a little smaller.
- **Fast lossless scalar count:** we removed all `FJXL_AVX512`, `FJXL_AVX2`, `FJXL_NEON`, `FJXL_GENERIC_SIMD`, `FJXL_STANDALONE` and x86 CPU-detection branches with a preprocessor-aware script, and kept their `#else` (scalar) branches.
- **Whole encoder for scale:** all `enc_*` files, `modular/*/enc_*`, `encode.cc` and `encode_internal.h` total **35,082 physical / 28,869 SLOC**.

### 3.2 Shared encoder core (needed by both lossless and lossy)

| Component | libjxl source | Phys / SLOC (whole files) | Needed (est.) |
|---|---|---|---|
| Bit writer | `enc_bit_writer.{cc,h}` | 360 / 251 | 250 |
| Header field writer and codestream headers | `enc_fields.{cc,h}` | 294 / 216 | 250 |
| ANS writer, histogram normalisation, hybrid-uint choice | `enc_ans.{cc,h}`, `enc_ans_params.h` | 1,747 / 1,402 | 1,000 |
| Histogram clustering (fast) | `enc_cluster.{cc,h}` | 432 / 347 | 200 |
| Context-map writer | `enc_context_map.{cc,h}` | 211 / 170 | 200 |
| Modular encoder driver (Init, ComputeEncodingData, fixed-tree branch, tokens, global info, streams) | `enc_modular.{cc,h}` | 1,996 / 1,724 | 900 |
| Modular channel encoding and predefined trees | `modular/encoding/enc_encoding.{cc,h}` | 859 / 725 | 550 |
| Tree tokenisation (`TokenizeTree` only) | `modular/encoding/enc_ma.cc:1032-1070` | — | 40 |
| Frame header, group encoding, TOC, frame assembly | `enc_frame.{cc,h}`, `enc_toc.{cc,h}` | 2,975 / 2,539 | 800 |
| **Core subtotal** | | | **≈4,200** |

Two items are left out of the core:

- **Huffman/prefix writer** (`enc_huffman*`, 638 / 476). It is optional: ANS is valid for every stream. libjxl picks prefix codes only for tiny streams or `kFastest` clustering (`enc_ans.cc:1169-1183`).
- **ICC writer** (`enc_icc_codec.*`, 514 / 400). It is not needed while we signal enum colour spaces only.

### 3.3 Low-effort lossless path (≈ libjxl e2/e3)

The core, plus:

| Component | libjxl source | Phys / SLOC | Needed (est.) |
|---|---|---|---|
| Forward RCT (YCoCg) | `modular/transform/enc_rct.*` | 239 / 197 | 200 |
| Forward palette and channel compaction (no lossy palette) | `modular/transform/enc_palette.*` | 691 / 600 | 400 |
| Transform dispatch | `modular/transform/enc_transform.*` | 78 / 55 | 78 |
| **Lossless-only subtotal** | | | **≈680** |

**Total low-effort lossless: about 4,900 lines** (core and lossless-only parts together). The smallest valid encoder skips palette: about 4,500.

**Moving lossless to e4/e5 adds:**

| Component | Source | Needed (est.) |
|---|---|---|
| MA tree learning | `enc_ma.cc` (1,073 / 951, nearly all) and `GatherTreeData`/`LearnTree` in `enc_encoding.cc:105-236`, `:570-650` | 1,250 |
| RLE LZ77 (e5) | `enc_lz77.cc:97-170`, `:779-817` | 120 |
| RCT search and local palette (e5) | `enc_modular.cc:1429-1527` | 120 |
| Hybrid-uint "fast" search (e5) | inside `enc_ans.cc:712-912` | 100 |
| **Delta** | | **≈1,600** |

The full LZ77 family (e8+) is 818 lines. We do not need it.

**Alternative for e1, the fast-lossless encoder:** 4,437 physical in total, of which **2,179 physical / 1,839 SLOC are scalar**. It is self-contained: own bit writer, hand-built frame header and TOC, prefix codes, palette, YCoCg. It shares almost nothing with the core above, and all its value is speed. We have no performance target, so it would be about 2,200 lines of a second encoder for no density gain.

### 3.4 Low/mid-effort lossy path (≈ libjxl e3, then e5)

**e3 lossy is the core, plus:**

| Component | libjxl source | Phys / SLOC | Needed (est.) |
|---|---|---|---|
| Forward XYB (linear sRGB → XYB; no YCbCr, no premultiplied alpha) | `enc_xyb.{cc,h}` | 460 / 363 | 200 |
| Frame heuristics subset: constant QF, `FillDCT8`, default CfL, default matrices, `AdjustQuantField`, `InitialQuantDC` | `enc_heuristics.cc:1023-1225` (part), `:208-232`; `enc_adaptive_quantization.cc:1198-1262` | — | 170 |
| Forward DCT8 and DC extraction | `enc_transforms-inl.h` (807 / 750) | — | 150 |
| AC quantisation and coefficient computation | `enc_group.{cc,h}` (603 / 523): `QuantizeBlockAC`, `QuantizeRoundtripYBlockAC`, `ComputeCoefficients`, `EncodeGroupTokenizedCoefficients` | — | 270 |
| AC tokenisation (non-zero counts, block contexts) | `enc_entropy_coder.{cc,h}` | 313 / 225 | 250 |
| Coefficient orders: write "default" | `enc_coeff_order.cc` | — | 40 |
| DC and AC-metadata Modular streams (plain DC quantisation) | `enc_modular.cc:1581-1806` (part) | — | 150 |
| CfL DC and dequant-matrix bundles (defaults) | `enc_chroma_from_luma.cc:389-418`; `enc_quant_weights.cc:116-165` | — | 80 |
| VarDCT frame plumbing: global DC/AC info, histograms, group encode | `enc_frame.cc:1144-1335` (part), `:312-357` | — | 300 |
| Encoder state set-up | `enc_cache.{cc,h}` (343 / 266) | — | 150 |
| **Lossy-only subtotal at e3** | | | **≈1,750** |

**Total e3 lossy: about 5,950 lines** (core and lossy-only parts together). Once lossless exists, adding e3 lossy costs about 1,750 lines.

**Moving lossy to e5 (and e4 on the way) adds:**

| Component | Source | Needed (est.) |
|---|---|---|
| Adaptive quantisation: `InitialQuantField` / `AdaptiveQuantizationMap`, masking, fuzzy erosion | `enc_adaptive_quantization.cc:88-720`, `:1264-1271` | 650 |
| AC-strategy search (no debug dump/colour code) | `enc_ac_strategy.cc` (1,200 / 980) | 850 |
| Forward transforms for all block sizes, including AFV | rest of `enc_transforms-inl.h` | 600 |
| Chroma-from-luma heuristics | `enc_chroma_from_luma.cc:127-388` | 330 |
| Gaborish inverse and 5×5 symmetric convolution | `enc_gaborish.cc` (74), `convolve_symmetric5.cc` (196), `convolve*.h` | 300 |
| `AdjustQuantBlockAC` | `enc_group.cc:104-327` | 225 |
| Coefficient reordering (e4) | `enc_coeff_order.cc` (rest) | 300 |
| Near-lossless DC with WP quantisation (e4) | `enc_modular.cc:1549-1580`, `:1645-1679` | 80 |
| Block-context-map search (e4) | `enc_heuristics.cc:69-205` | 140 |
| **Delta to e5** | | **≈3,500** |

**Moving lossy to e6/e7 without patches adds:**

| Component | Source | Needed (est.) |
|---|---|---|
| Dynamic EPF sharpness | `enc_heuristics.cc:857-1021` | 170 |
| CfL before ACS | parameter change | — |
| Full ACS: non-aligned merges, DCT4×8, AFV (code already in the e5 ACS port) | parameter change | — |
| Chroma adjustment pixel stats | `enc_frame.cc:597-699` | 100 |
| **Delta** | | **≈300** |

**Out of scope (sizes for reference):**

| Component | Source | Phys. lines |
|---|---|---|
| Patches and dots | `enc_patch_dictionary.*`, `enc_detect_dots.*`, `enc_dot_dictionary.*`; they also encode and decode a separate reference frame | 1,800 |
| Butteraugli iteration | `butteraugli/butteraugli.*` (2,414) plus `FindBestQuantization`/`RoundtripImage` (≈340); it needs the full decoder in the loop | ≈2,750 |
| Noise estimation | `enc_noise.*` | 417 |
| JPEG transcoding | `enc_frame.cc:798-1142` and `lib/jxl/jpeg/` | — |
| Streaming/chunked encoding | `enc_frame.cc:1813-1925` (`CanDoStreamingEncoding`, streaming permutation), `:2059-2320` (`EncodeFrameStreaming`) | ≈420 |
| e11 search | `enc_frame.cc:2378-2572` | ≈200 |

## 4. Recommendation: three effort levels

Each ImageSharp level copies one libjxl tier for each mode. We use libjxl's own constants, so the output is libjxl-shaped and can be checked against `cjxl -e N` for size sanity.

| ImageSharp level | Lossless (Modular) | Lossy (VarDCT) | Marginal code |
|---|---|---|---|
| **Fastest** (≈ libjxl e3; e2 is a one-line predictor/tree switch) | Global palette and channel palette, global YCoCg, predefined `kWPFixedDC` tree with Weighted predictor (or `kGradientFixedDC` with Gradient for e2), ANS with fast clustering, no LZ77 | DCT8 only, constant quant field `0.79/d`, default CfL/orders/matrices, predefined DC/AC-metadata trees, ANS with fast clustering, EPF chosen by distance (header only), no Gaborish | Core ≈4,200, plus lossless ≈680, plus lossy ≈1,750 → **≈6,600** |
| **Default** (≈ libjxl e5 without patches) | Learned MA tree with `Best` predictor; RCT search (4); local palette; RLE LZ77; hybrid-uint fast search | Adaptive quantisation, ACS search (DCT/4×4/2×2/IDENTITY and aligned merges), CfL (fast), Gaborish, `AdjustQuantBlockAC`, coefficient reordering, near-lossless DC, block-context map | lossless ≈1,600, plus lossy ≈3,500 → **≈5,100** |
| **Thorough** (≈ libjxl e6/e7 without patches) | e6/e7 parameter tables: 5-7 RCTs; 5-7 tree properties at 64/96 values; palette/RCT accepted by `EstimateCost` | Full ACS (non-aligned, 4×8, AFV), dynamic EPF sharpness, CfL before ACS, chroma pixel stats | **≈400** (almost all parameter tables) |

**Total about 12,000 lines of encoder-only C++ to port**, plus the ≈16,500 lines shared with the decoder. This is about a third of libjxl's encoder.

**Why these three levels:**

- **Fastest is the smallest valid encoder.** You cannot write a VarDCT file without the Modular encoder (DC and AC metadata are Modular streams). Once that core exists, lossless at e2/e3 is only the palette and RCT transforms on top.
- **Default holds the large quality wins.** For lossless, MA tree learning matters most on photos. For lossy, adaptive quantisation, variable block sizes and Gaborish account for most of the gap between libjxl e3 and e7 output. e5 is where libjxl switches all of them on.
- **Thorough is nearly free.** Most of it is libjxl's per-tier parameter tables applied to code that Default already has.
- **Leave out the rest:**
  - fast-lossless e1: a second encoder, speed only
  - patches and dots: need nested reference-frame encoding; help mainly on screenshots and text
  - butteraugli iteration (e8+): ≈2,750 lines and the decoder in the loop
  - the "error diffusion" and "spline" steps: they do nothing in libjxl
  - e10/e11 searches
  - chunked/streaming output

**Seams to keep for later:** the patch-dictionary hook point (`enc_heuristics.cc:1059-1066`, `enc_modular.cc:710-718`); a pluggable "quantizer refinement" step where butteraugli iteration would go (`FindBestQuantizer`); and prefix-code output (for very small streams).

## 5. Facts that affect implementation slices

1. **Write the shared core before either path.** Bit writer, header field writer (visitor), ANS writer with fast clustering, context map, Modular encoder with predefined trees, frame/TOC. Done-check: a 1-group lossless gray image round-trips through `djxl`.
2. **Lossless Fastest then needs only** forward YCoCg and palette. Done-check: bit-exact round-trip of the conformance PNGs through `djxl` and through our decoder.
3. **Lossy Fastest needs only** XYB, DCT8, uniform quantisation, AC tokenisation, and DC/AC-metadata streams. EPF quality comes free from the decoder; the encoder only writes `epf_iters`.
4. **Our encoder may pick a predefined tree for the extra channels in VarDCT**, where libjxl learns one. That keeps MA tree learning out of the lossy path until the Default level.
5. **The input colour path needs no CMS for sRGB/linear/enum inputs.** `ToXYB` goes through `JxlCmsInterface` (`enc_frame.cc:1616`). For enum colour spaces the analytic transfer functions (shared with the decoder) are enough. This matches the map's colour decision.
6. **Prefix codes are optional.** libjxl uses them for AC at e1/e2 and for streams of fewer than 100 tokens. ANS everywhere is valid and leaves out ≈640 lines.
