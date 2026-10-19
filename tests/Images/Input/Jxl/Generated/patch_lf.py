# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Rewrites the loop-filter bundle of a bare JPEG XL codestream's first frame header,
# so that custom Gaborish weights, custom EPF fields or out-of-range values can be tested.
# cjxl v0.12.0 never writes the custom loop-filter fields (no encoder code sets gab_custom,
# epf_sharp_custom, epf_weight_custom or epf_sigma_custom), so this is the only way to get them.
# Only the frame header bits change; the TOC and sections are copied, re-aligned after the TOC's
# permutation bit (the TOC sizes start on a byte boundary, so the sections are untouched).
#
# usage: py -3 -I patch_lf.py in.jxl out.jxl '<python dict of fields>'
#   fields: gab (bool), gab_custom (6 floats: x1 x2 y1 y2 b1 b2), epf (0..3),
#           sharp (8 floats), weight (5 floats: cs0 cs1 cs2 zf1 zf2),
#           sigma (VarDCT: quant_mul pass0 pass2 border; Modular: pass0 pass2 border),
#           sigma_modular (float, Modular only)
import ast, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lfhdr as H  # noqa: E402


def bits_of(data):
    return [(data[i >> 3] >> (i & 7)) & 1 for i in range(len(data) * 8)]


def put(out, n, v):
    out.extend((v >> i) & 1 for i in range(n))


def f16(out, x):
    put(out, 16, struct.unpack('<H', struct.pack('<e', x))[0])


def lf_bits(spec, modular):
    o = []
    put(o, 1, 0)  # all_default
    gab = spec.get('gab', False)
    put(o, 1, int(gab))
    if gab:
        gc = spec.get('gab_custom')
        put(o, 1, int(gc is not None))
        if gc is not None:
            for w in gc: f16(o, w)
    epf = spec.get('epf', 0)
    put(o, 2, epf)
    if epf > 0:
        if not modular:
            sh = spec.get('sharp')
            put(o, 1, int(sh is not None))
            if sh is not None:
                for w in sh: f16(o, w)
        wt = spec.get('weight')
        put(o, 1, int(wt is not None))
        if wt is not None:
            for w in wt: f16(o, w)
        sg = spec.get('sigma')
        put(o, 1, int(sg is not None))
        if sg is not None:
            for w in sg: f16(o, w)
        if modular:
            f16(o, spec.get('sigma_modular', 1.0))
    put(o, 2, 0)  # extensions = 0
    return o


def main(src, dst, spec):
    data = open(src, 'rb').read()
    assert data[:2] == b'\xff\x0a', 'bare codestream only'
    r = H.BR(data, 2)
    xs, ys = H.size_header(r)
    m = H.image_metadata(r)
    H.transform_data(r, m['xyb'])
    assert not m['color'].get('want_icc'), 'ICC not supported'
    r.align()
    f = H.frame_header(r, m, xs, ys)
    assert 'lf_bit' in f, 'frame header is all_default: not supported'
    assert not r.b(), 'permuted TOC not supported'
    perm_end = r.bit
    old = bits_of(data)
    new = old[:f['lf_bit']] + lf_bits(spec, f['modular']) + old[f['lf_end']:perm_end]
    new += [0] * (-len(new) % 8)
    head = bytes(sum(new[i + j] << j for j in range(8)) for i in range(0, len(new), 8))
    open(dst, 'wb').write(head + data[(perm_end + 7) // 8:])


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], ast.literal_eval(sys.argv[3]))
