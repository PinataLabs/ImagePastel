# Port the JPEG XL codec closely from libjxl, with attribution

The JPEG XL codec is a pure managed C# port that closely follows libjxl (`https://github.com/libjxl/libjxl`). It keeps libjxl's algorithms, module structure and constants, rather than being a clean-room implementation of the ISO/IEC 18181 text. libjxl is BSD-3-Clause and comes with a royalty-free patent grant, so a close port is allowed. Its decoder is effectively the executable spec: the ISO text is paywalled and has errata that libjxl already reflects. Every ported file carries a header naming libjxl as its source, and `THIRD-PARTY-NOTICES.TXT` has a libjxl entry with the BSD-3 notice. This is a different case from the SixLabors/ImageSharp boundary in `CLAUDE.md`: that upstream code is under a commercial licence and must never be read, including any JPEG XL work there.

## Considered Options

- **Clean-room from ISO/IEC 18181:** rejected. The spec is paywalled and has known errata, and conformance depends on matching libjxl's behaviour anyway.
- **P/Invoke wrapper over native libjxl:** rejected. It breaks ImageSharp's no-native-dependency model.

## Consequences

- Test data copied from libjxl `testdata/` is CC-BY 4.0 and needs its own attribution, separate from the code notice.
- Divergence from libjxl's structure should be deliberate. When a ported file departs from its source, its comments should say why, so later fixes in libjxl can still be compared and carried over.
