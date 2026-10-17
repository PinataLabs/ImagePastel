# Reference JPEG XL tools for tests and CI

Research for [#104](https://github.com/PinataLabs/ImageSharp/issues/104), a child of the
JPEG XL codec map [#100](https://github.com/PinataLabs/ImageSharp/issues/100).
Researched 2026-10-18 against `main` at `eece8f70a`.

## Question

How can this repo's tests and CI get reference JPEG XL tools to check our encoder? The tools
must decode with a reference decoder and measure SSIMULACRA2 / butteraugli on Windows and
Linux runners.

## Short answer

- **Reference decoder: use Magick.NET, which is already referenced.** Magick.NET 15.0.0
  bundles libjxl 0.12.0 (the latest libjxl release) and reads and writes JXL in-process. This
  was verified by running it on Windows x64 and Linux x64. It needs no CI change and runs on all
  three CI operating systems.
- **Perceptual metric: port SSIMULACRA2 to managed C# as a test utility.** No usable managed
  implementation exists. The libjxl source is 537 lines (BSD-3-Clause). Its main dependency is
  the XYB transform, which the encoder must port anyway.
- **Native libjxl tools are an oracle, not a dependency.** The libjxl v0.12.0 GitHub release
  ships static `cjxl`, `djxl`, `ssimulacra2` and `butteraugli_main` for Windows x64/x86 and
  Linux x86_64. They were verified to run on Windows and on stock Ubuntu 24.04. They are good
  for an opt-in CI job that checks the managed SSIMULACRA2 port and cross-checks the decoder
  with `djxl`. There is no macOS or arm64 prebuilt; Homebrew covers macOS.

## 1. Magick.NET as a JXL reference codec

### What the repo has now

- `Directory.Packages.props` pins `Magick.NET-Q16-AnyCPU` **15.0.0**, and
  `tests/ImageSharp.Tests/ImageSharp.Tests.csproj` references it.
- `tests/ImageSharp.Tests/TestUtilities/ReferenceCodecs/MagickReferenceDecoder.cs` decodes any
  Magick-readable stream into `Image<TPixel>`. It handles frame depths 1-12 (as 8-bit RGBA)
  and 14/16 (as 16-bit RGBA), and throws for other depths.
- `TestUtilities/ImageComparison/ImageComparingUtils.cs` (`CompareWithReferenceDecoder`,
  `DecodeWithMagick`) compares an ImageSharp decode against Magick, exactly or with a tolerance.
- `TestUtilities/TestEnvironment.Formats.cs` registers Magick as the reference decoder for PNG
  (and BMP on non-Windows).

### Does it read and write JXL, and where?

Package contents (`magick.net-q16-anycpu/15.0.0`):

- The native runtimes are `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`,
  `linux-musl-x64` and `osx-arm64`. **There is no `osx-x64`.** This is not a problem, because
  `macos-latest` is arm64.
- `Notice.txt` lists `[ libjxl 0.12.0 (2026-07-01) ]`, plus brotli 1.2.0 and highway.
- Every native binary embeds the ImageMagick string `ImageMagick 7.1.2-33 Q16` and the coder
  description `JPEG XL (ISO/IEC 18181)`. Every one also contains libjxl's `lib/jxl/encode.cc`
  assertion paths. The `osx-arm64` dylib also exports the libjxl API symbol
  `JxlEncoderSetFrameDistance`.

A probe program (`net10.0`, `Magick.NET-Q16-AnyCPU 15.0.0`) printed these results:

| | Windows 11 x64 | Ubuntu 24.04.3 x64 (WSL, self-contained publish) |
|---|---|---|
| `MagickNET.Delegates` contains `jxl` | yes | yes |
| `MagickFormat.Jxl` read / write / multi-frame | True / True / True | True / True / True |
| 16-bit RGB, `Quality = 100`, `jxl:effort=7`, then read back | depth 16, AE = 0 (bit-exact) | depth 16, AE = 0 (bit-exact) |
| Output container | ISOBMFF container (not a bare `FF 0A` codestream) | same |

macOS was **not executed**. The `osx-arm64` binary has the libjxl API symbols and the JXL coder
string, so support is very likely. The first CI run of a JXL test on `macos-latest` confirms it.

### Limits

- **Encoding controls are coarse.** Magick.NET exposes only `JxlWriteDefines.Effort` (3-9).
  Quality maps to distance, and `Quality = 100` gives lossless. There is no Modular/VarDCT switch
  and no direct distance setting. This matters little, because the encoder tests need a
  reference *decoder*. Fixture creation can use `cjxl`.
- **No SSIMULACRA2 or butteraugli metric.** ImageMagick's compare metrics are AE, DSSIM, FUZZ,
  MAE, MEPP, MSE, NCC, PAE, PHASH, PSNR, RMSE and SSIM. The `ssimulacra2` string in the native
  binary is libaom's `AOM_TUNE_SSIMULACRA2` tuning flag, and `butteraugli` is libjxl's internal
  encoder comparator. Neither is callable.
- **`MagickReferenceDecoder` needs a change for 10/12-bit JXL.** It narrows depths 10 and 12 to
  8-bit bytes, so a bit-exact lossless test of 10/12-bit images would compare quantised
  values. For JXL, read through `ToShortArray` (16-bit) for every depth above 8.
- **Q16 is not HDRI.** Float and >16-bit JXL samples are quantised to 16 bits. That is enough
  for 8/16-bit encoder round-trips, but not for float lossless checks.

### Other in-process options checked

- **NetVips.Native 8.18.7** (referenced by Benchmarks and ProfilingSandbox, not by the tests):
  `versions.json` lists no libjxl. It is the libvips "web" build. It is not an option.
- **SkiaSharp 4.153.1:** has no JXL codec. It is not an option.

## 2. libjxl command-line tools

### Which tools exist

`tools/CMakeLists.txt` in libjxl (local checkout `v0.12-snapshot-22-gef67fde2`):

- The default tools are `cjxl`, `djxl`, `jxlinfo` and `jxltran`.
- `ssimulacra2`, `butteraugli_main`, `benchmark_xl` and others are built only with
  `JPEGXL_ENABLE_DEVTOOLS=ON` (lines 175-221). They are then installed with the others
  (`install(TARGETS ${TOOL_BINARIES} ...)`, line 345).

### Prebuilt GitHub release binaries

Source: `gh release view -R libjxl/libjxl` (latest is **v0.12.0, published 2026-07-01**). The
earlier maintained lines are v0.11.2, v0.10.5, v0.9.5, v0.8.5 and v0.7.3, all published
2026-02-10.

| Asset | Size | Contents (checked) |
|---|---|---|
| `jxl-x64-windows-static.zip` | 40.5 MB | `bin/` with `cjxl`, `djxl`, `jxlinfo`, `jxltran`, `ssimulacra2`, `butteraugli_main`, `benchmark_xl`, `ssimulacra_main` and other devtools (.exe) |
| `jxl-x64-windows-static.7z` | 8.1 MB | same, smaller archive |
| `jxl-x86-windows-static.{zip,7z}` | 34.0 / 6.8 MB | x86 build |
| `jxl-x64-windows.{zip,7z}` | 23.6 / 4.8 MB | DLL build |
| `jxl-linux-x86_64-static.tar.lz` | 4.0 MB | `tools/cjxl`, `djxl`, `jxlinfo`, `benchmark_xl`, `butteraugli_main`, `ssimulacra2` + licences |
| `jxl-debs-amd64-{ubuntu-20.04,22.04,24.04,debian-*}.tar` | 39-58 MB | .deb packages |

There is **no macOS asset and no arm64 asset.**

Verified on this machine:

- Windows: `cjxl v0.12.0 4128790 [_AVX2_,SSE4,SSE2] {Clang 22.1.3}`.
- Linux, in stock Ubuntu 24.04 with nothing installed: `cjxl v0.12.0 4128790 ... {Clang 18.1.3}`.
  `ldd` reports "not a dynamic executable", so the binaries are fully static.
- The tools ran end to end: `cjxl -d 1 -e 7` on `testdata/jxl/flower/flower_alpha.png`, then
  `djxl`, then `ssimulacra2` and `butteraugli_main`. `djxl` returns exit code 1 on a corrupt
  codestream and 0 with `--disable_output` on a valid one, so it can serve as a validity check.

Two CI gotchas:

- **The Windows `.zip` uses Deflate64.** bsdtar (`tar.exe`) fails with "Unsupported ZIP
  compression method (9: deflation-64-bit)". PowerShell 7 `Expand-Archive` (.NET) extracts it.
  Use the 8 MB `.7z` with 7-Zip, which the runners have preinstalled.
- **The Linux archive is lzip.** The `ubuntu-24.04` runner image (20261012) does not list lzip,
  and its p7zip 16.02 does not read lzip. Run `sudo apt-get install -y lzip` first.

### Cross-platform variance

This is important for test thresholds. The same `d1.jxl` was decoded by the Windows and the
Linux `djxl`:

- The decoded PNGs are **not byte-identical**.
- `butteraugli_main` gave the same result on both: 1.4081653357, 3-norm 0.492045.
- `ssimulacra2` gave 88.965 (Windows tool, Windows decode), 89.017 (Windows tool, Linux decode),
  89.079 (Linux tool, Windows decode) and 89.132 (Linux tool, Linux decode).
- Both the tool build and the decode build cause the spread of about 0.17. Lossy thresholds
  need a margin of about 0.5, not an exact score. Lossless round-trips scored exactly 100.

### Package managers

| Manager | Package / version | Has `ssimulacra2` + `butteraugli_main`? | Source |
|---|---|---|---|
| winget | `libjxl.libjxl` 0.12.0, portable | yes. The installer URL is the release `jxl-x64-windows-static.zip`, with the same SHA-256 `3025d7e3...` that was downloaded here | `winget show libjxl.libjxl` |
| apt, Ubuntu 24.04 (noble) | `libjxl-tools` / `libjxl-devtools` 0.7.0-10.2ubuntu6.1 | **no `ssimulacra2`**. devtools has `butteraugli_main` and the old `ssimulacra_main` (v1) | packages.ubuntu.com noble filelist; `apt-cache policy` |
| apt, Ubuntu 26.04 (resolute) | 0.11.1-6ubuntu4.2 | yes (`libjxl-devtools`), and `cjxl`/`djxl` in `libjxl-tools` | packages.ubuntu.com resolute filelist; launchpad |
| apt, Debian trixie | `libjxl-tools` (cjxl/djxl/jxlinfo) + `libjxl-devtools` | yes | packages.debian.org filelists |
| Homebrew | `jpeg-xl` 0.12.0. Bottles for macOS arm64 (sequoia, tahoe, golden_gate) and Linux x86_64/arm64 | yes. The formula sets `-DJPEGXL_ENABLE_DEVTOOLS=ON`, and the CMake rule above installs the devtools. The keg contents were not listed | formulae.brew.sh API; homebrew-core `Formula/j/jpeg-xl.rb` |
| vcpkg | `libjxl` 0.12.0, feature `tools` = "cjxl and djxl" only | **no** (no devtools feature). It also builds from source | `ports/libjxl/vcpkg.json` |
| NuGet | none that ships the libjxl tools | n/a | nuget.org search |

Runner note: GitHub moves `ubuntu-latest` from 24.04 to 26.04 gradually between **2026-10-19
and 2026-11-19** (github.blog changelog, 2026-09-17). After that, `apt-get install
libjxl-tools libjxl-devtools` gives 0.11.1 with all four tools. On 24.04 it does not.

## 3. Managed SSIMULACRA2 / butteraugli

- **No pure managed implementation was found.** The only .NET package is
  `YellowDogMan.ssimulacra2.NET` 0.1.1 (one release, 2025-07-17, MIT, net9.0, 311 downloads). It
  wraps a native fork. Its NuGet page says Linux needs `libhwy-dev` and `liblcms2-dev`, and it
  does not say that it bundles natives. It is not a good fit.
- Other ports exist (Rust `rust-av/ssimulacra2`, an Elixir binding over a Rust crate, and an
  AviSynth plugin). Binding to any of them would bring back a native test dependency for no
  gain over the libjxl binary.
- **Porting libjxl's SSIMULACRA2 is cheap.** `tools/ssimulacra2.cc` is 537 lines, plus
  `ssimulacra2.h` and `tools/gauss_blur.*` (a recursive Gaussian). It uses
  `lib/jxl/enc_xyb.h` (linear sRGB to XYB). The JXL encoder ports XYB anyway, so a test-side
  port can share or duplicate it. The CMS step (any colour space to linear sRGB) can be limited
  to sRGB/linear inputs, which is all the encoder tests produce. Licence: BSD-3-Clause with the
  libjxl patent grant, the same terms as the rest of the codec port.
- **Butteraugli is a larger port.** `lib/jxl/butteraugli/butteraugli.cc` is 84 KB. Defer it,
  and use the native `butteraugli_main` in the opt-in job if a second metric is wanted.
- Check the port against the libjxl `ssimulacra2` binary on a fixed set of image pairs, with a
  tolerance of about 0.5 (see the variance figures above).

## 4. CI changes for each option

Today `.github/workflows/ci.yml` runs `build` on `[ubuntu-latest, windows-latest,
macos-latest]` with bash, then runs `dotnet test tests/ImageSharp.Tests/...`. Coverage runs on
Ubuntu. `sonarqube.yml` also runs the tests on `ubuntu-latest`. The tests use xunit.v3
(`xunit.v3.mtp-off` 4.0.2), which already uses `[Fact(Skip=..., SkipUnless=...)]`. A test that
needs a native tool can skip cleanly when the tool is absent.

| Option | Platforms | Version | CI change needed |
|---|---|---|---|
| A. Magick.NET (in-process decode + encode) | win x64/x86/arm64, linux x64/arm64/musl, osx-arm64 | libjxl 0.12.0 (Magick.NET 15.0.0) | **None.** Register Magick as the `.jxl` reference decoder in `TestEnvironment.Formats.cs`, and widen `MagickReferenceDecoder` to 16-bit for depths above 8 |
| B. Managed SSIMULACRA2 port (test utility) | all | tracks libjxl `tools/ssimulacra2.cc` | **None** |
| C. libjxl release static binaries | Windows x64/x86, Linux x86_64 (no macOS, no arm64) | v0.12.0 | Add a step: `actions/cache` keyed on the version → download (`gh release download v0.12.0 -R libjxl/libjxl -p <asset>`) → extract (Linux: `sudo apt-get install -y lzip; tar --lzip -xf`; Windows: `7z x *.7z`) → export `JXL_TOOLS_DIR`. Tests read the variable and skip when it is unset. Skip the step on macOS, or use D |
| D. Homebrew `jpeg-xl` | macOS arm64 (+ Linux) | 0.12.0 | `brew install jpeg-xl` on the macOS leg; the tools go on PATH |
| E. apt `libjxl-tools` + `libjxl-devtools` | Ubuntu | 0.7.0 on 24.04 (no `ssimulacra2`); 0.11.1 on 26.04 | `sudo apt-get install -y libjxl-tools libjxl-devtools`. This works only after `ubuntu-latest` becomes 26.04, or with `runs-on: ubuntu-26.04` |
| F. winget `libjxl.libjxl` | Windows x64 | 0.12.0 (same zip as C) | `winget install libjxl.libjxl`. Hosted runners do not reliably have winget, so C is simpler. This option is good for local development |
| G. vcpkg `libjxl[tools]` | all | 0.12.0 | Not recommended: a source build, and no `ssimulacra2`/`butteraugli_main` |
| H. Check the tools into `tests/Images/External/tools` | Windows (+ Linux) | pinned | None. There is precedent: `tools/jpeg/dump-jpeg-coeffs.exe` is checked in and Windows-only (`LibJpegTools`). It adds about 18 MB of binaries to the repo, so C is preferred |

## Recommendation

1. **Default test path (every CI leg, no CI change):** use Magick.NET for the reference decode
   and for validity ("libjxl accepts our bitstream"). Lossless round-trips must be bit-exact,
   compared through a 16-bit path. Measure lossy quality with the managed SSIMULACRA2 port.
2. **Opt-in oracle job (Linux + Windows):** use option C (libjxl v0.12.0 static tools, cached).
   It runs the tests that need `djxl`, `ssimulacra2` and `butteraugli_main`. Those tests check the
   managed SSIMULACRA2 against the reference, check our encoder output with a second decoder
   (`djxl` exit code / `--disable_output`), and can use `cjxl` to make decoder fixtures. The
   tests skip when `JXL_TOOLS_DIR` is not set, so local runs and the macOS leg still pass.
3. When `ubuntu-latest` is on 26.04, option E (apt) can replace the download on Linux. Its tools
   are 0.11.1, not 0.12.0, so pin the release download if the version must match on both
   operating systems.

## Sources

- This repo: `Directory.Packages.props`; `tests/ImageSharp.Tests/ImageSharp.Tests.csproj`;
  `tests/ImageSharp.Tests/TestUtilities/ReferenceCodecs/MagickReferenceDecoder.cs`;
  `TestUtilities/ImageComparison/ImageComparingUtils.cs`; `TestUtilities/TestEnvironment.Formats.cs`;
  `TestUtilities/TestEnvironment.cs` (`ToolsDirectoryRelativePath`);
  `Formats/Jpg/Utils/LibJpegTools.cs`; `.github/workflows/ci.yml`, `sonarqube.yml`.
- Magick.NET 15.0.0 NuGet package: `magick.net-q16-anycpu.nuspec`, `Notice.txt`, `runtimes/*`
  natives, and `lib/net10.0/Magick.NET-Q16-AnyCPU.xml` (`JxlWriteDefines`). A probe program was
  run on Windows and Linux.
- NetVips.Native.win-x64 8.18.7 `versions.json`.
- libjxl: https://github.com/libjxl/libjxl/releases/tag/v0.12.0 (assets were downloaded and run);
  local checkout `D:\Data\_CodeOS\libjxl` (`tools/CMakeLists.txt`, `tools/ssimulacra2.cc`,
  `lib/jxl/butteraugli/butteraugli.cc`).
- winget: `winget show libjxl.libjxl`.
- Ubuntu: https://packages.ubuntu.com/noble/amd64/libjxl-devtools/filelist,
  https://packages.ubuntu.com/resolute/amd64/libjxl-devtools/filelist,
  https://launchpad.net/ubuntu/+source/jpeg-xl
- Debian: https://packages.debian.org/trixie/amd64/libjxl-tools/filelist,
  https://packages.debian.org/trixie/amd64/libjxl-devtools/filelist
- Homebrew: https://formulae.brew.sh/api/formula/jpeg-xl.json,
  https://github.com/Homebrew/homebrew-core/blob/main/Formula/j/jpeg-xl.rb
- vcpkg: https://github.com/microsoft/vcpkg/blob/master/ports/libjxl/vcpkg.json
- Runner images: https://github.com/actions/runner-images (Ubuntu2404 and macos-15-arm64
  READMEs); https://github.blog/changelog/2026-09-17-ubuntu-26-generally-available-and-latest-migration
- https://www.nuget.org/packages/YellowDogMan.ssimulacra2.NET
