# JXL maths matches libjxl's approximations, and scalar output is bit-exact on every platform

The JPEG XL codec ports libjxl's fast-math approximations (`FastPowf`, `FastLog2f`, `FastCosf`, `FastErff`, `FastLinearToSRGB` and the rational polynomials) verbatim. It calls them exactly where libjxl does: the transfer-function and tone-mapping stages, splines, and quant-weight interpolation, which the encoder shares. Where libjxl uses `std::` maths, the port uses `MathF`/`Math`. The scalar code never fuses a multiply and an add, so its output is identical on every OS and CPU. That scalar output is the reference, and any later SIMD path must match it bit for bit.

We chose this over exact `MathF` for three reasons:
- The conformance references come from libjxl's approximated build, so matching it leaves the most threshold headroom for real bugs.
- The approximations are plain arithmetic, so they are deterministic. `MathF.Pow`/`Exp` call each platform's C runtime and can differ by an ulp (unit in the last place).
- The approximations vectorise later, and `MathF.Pow` doesn't.

## Considered Options

- **Exact `MathF` everywhere:** rejected. It drifts from the references, and its results depend on the platform.
- **Allow FMA, with tests comparing within a tolerance:** rejected. Goldens would need a copy per platform, or a tolerance that could hide real regressions. libjxl itself is not bit-identical across CPUs, because Highway's `MulAdd` is an FMA on AVX2 but not on SSE4.

## Consequences

- Do not replace a `JxlFastMath` call with the "more accurate" `MathF` equivalent. Accuracy is set by libjxl's choice, not by ours.
- A SIMD effort that wants FMA must reopen this ADR, not work around it.
- Lossless decoding is unaffected, because no approximation is on the Modular integer path.
