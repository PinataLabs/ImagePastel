# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# A parser for JPEG XL codestream headers, used by patch_lf.py: the image headers, the first frame
# header with its loop-filter fields, the TOC, and the fixed-field starts of the DC global and AC
# global sections. Run on its own, it prints them. It follows the field order of this repo's C#
# port (Headers/*, Frame/*) and libjxl ef67fde2.
import struct, sys


class BR:
    def __init__(self, data, pos=0):
        self.d = data; self.bit = pos * 8

    def u(self, n):
        v = 0
        for i in range(n):
            byte = self.d[self.bit >> 3] if (self.bit >> 3) < len(self.d) else 0
            v |= ((byte >> (self.bit & 7)) & 1) << i
            self.bit += 1
        return v

    def b(self):
        return self.u(1) == 1

    def u32(self, *d):
        s = self.u(2)
        kind, a, c = d[s]
        if kind == 'v':
            return a
        return self.u(a) + c

    def u64(self):
        s = self.u(2)
        if s == 0: return 0
        if s == 1: return 1 + self.u(4)
        if s == 2: return 17 + self.u(8)
        v = self.u(12); shift = 12
        while self.u(1):
            if shift == 60:
                v |= self.u(4) << 60; break
            v |= self.u(8) << shift; shift += 8
        return v

    def f16(self):
        bits = self.u(16)
        return struct.unpack('<e', struct.pack('<H', bits))[0]

    def enum(self):
        return self.u32(V(0), V(1), B(4, 2), B(6, 18))

    def ext(self):
        e = self.u64()
        if e:
            total = 0
            for i in range(64):
                if e >> i & 1: total += self.u64()
            self.bit += total
        return e

    def align(self):
        self.bit = (self.bit + 7) & ~7


def V(v): return ('v', v, 0)
def B(n, off=0): return ('b', n, off)


def size_header(r):
    small = r.b()
    if small:
        ys = (r.u(5) + 1) * 8
    else:
        ys = r.u32(B(9, 1), B(13, 1), B(18, 1), B(30, 1))
    ratio = r.u(3)
    if ratio == 0:
        xs = (r.u(5) + 1) * 8 if small else r.u32(B(9, 1), B(13, 1), B(18, 1), B(30, 1))
    else:
        num, den = [(1, 1), (12, 10), (4, 3), (3, 2), (16, 9), (5, 4), (2, 1)][ratio - 1]
        xs = ys * num // den
    return xs, ys


def bit_depth(r):
    fp = r.b()
    if not fp:
        return dict(fp=False, bits=r.u32(V(8), V(10), V(12), B(6, 1)))
    bits = r.u32(V(32), V(16), V(24), B(6, 1))
    return dict(fp=True, bits=bits, exp=r.u(4) + 1)


def custom_xy(r):
    enc = (B(19), B(19, 524288), B(20, 1048576), B(21, 2097152))
    return r.u32(*enc), r.u32(*enc)


def color_encoding(r):
    if r.b():
        return dict(all_default=True, want_icc=False, cs='RGB', wp='D65', prim='sRGB', tf='sRGB', intent='Relative')
    ce = {}
    ce['want_icc'] = r.b()
    cs = r.enum(); ce['cs'] = {0: 'RGB', 1: 'Gray', 2: 'XYB', 3: 'Unknown'}.get(cs, cs)
    if not ce['want_icc']:
        if cs != 2:
            wp = r.enum(); ce['wp'] = wp
            if wp == 2: custom_xy(r)
        if cs not in (1, 2):
            p = r.enum(); ce['prim'] = p
            if p == 2: custom_xy(r); custom_xy(r); custom_xy(r)
        # transfer function
        if cs != 2:
            hg = r.b()
            if hg: ce['gamma'] = r.u(24)
            else: ce['tf'] = r.enum()
        ce['intent'] = r.enum()
    return ce


def image_metadata(r):
    m = dict(xyb=True, num_ec=0, ec=[], bit_depth=dict(fp=False, bits=8), have_preview=False, have_anim=False)
    if r.b():
        m['all_default'] = True
        m['color'] = color_encoding_default()
        return m
    extra = r.b()
    if extra:
        m['orientation'] = r.u(3) + 1
        if r.b(): size_header(r)
        m['have_preview'] = r.b()
        if m['have_preview']: raise SystemExit('preview not handled')
        m['have_anim'] = r.b()
        if m['have_anim']:
            r.u32(V(100), V(1000), B(10, 1), B(30, 1)); r.u32(V(1), V(1001), B(8, 1), B(10, 1))
            r.u32(V(0), B(3), B(16), B(32)); m['timecodes'] = r.b()
    m['bit_depth'] = bit_depth(r)
    m['mod16'] = r.b()
    n = r.u32(V(0), V(1), B(4, 2), B(12, 1)); m['num_ec'] = n
    for _ in range(n):
        if r.b():
            m['ec'].append('alpha(default)'); continue
        t = r.enum(); bd = bit_depth(r); r.u32(V(0), V(3), V(4), B(3, 1))
        nl = r.u32(V(0), B(4), B(5, 16), B(10, 48)); r.u(8 * nl)
        if t == 0: r.b()
        if t == 2: [r.f16() for _ in range(4)]
        if t == 5: r.u32(V(1), B(2), B(4, 3), B(8, 19))
        m['ec'].append(t)
    m['xyb'] = r.b()
    m['color'] = color_encoding(r)
    if extra:
        if not r.b():
            m['tone'] = (r.f16(), r.f16(), r.b(), r.f16())
    m['ext'] = r.ext()
    return m


def color_encoding_default():
    return dict(all_default=True, want_icc=False, cs='RGB')


def transform_data(r, xyb):
    t = {}
    if r.b():
        t['all_default'] = True; return t
    if xyb:
        if not r.b():
            t['custom_opsin'] = True
            [r.f16() for _ in range(9 + 3 + 4)]
    mask = r.u(3); t['custom_weights_mask'] = mask
    for bit, n in ((1, 15), (2, 55), (4, 210)):
        if mask & bit: [r.f16() for _ in range(n)]
    return t


def frame_header(r, m, xs, ys):
    f = dict(type=0, modular=False, flags=0, ct='XYB' if m['xyb'] else 'None', upsampling=1, gab=True,
             epf=2, passes=1, x_qm=3, b_qm=2, is_last=True, group_shift=1)
    if r.b():
        f['all_default'] = True
        return f
    f['type'] = r.u32(V(0), V(1), V(2), V(3))
    f['modular'] = r.b()
    f['flags'] = r.u64()
    if not m['xyb']:
        f['ct'] = 'YCbCr' if r.b() else 'None'
    if f['ct'] == 'YCbCr' and not (f['flags'] & 32):
        [r.u(2) for _ in range(3)]
    if not (f['flags'] & 32):
        f['upsampling'] = r.u32(V(1), V(2), V(4), V(8))
        for _ in range(m['num_ec']):
            r.u32(V(1), V(2), V(4), V(8))
    if f['modular']:
        f['group_shift'] = r.u(2)
    if not f['modular'] and f['ct'] == 'XYB':
        f['x_qm'] = r.u(3); f['b_qm'] = r.u(3)
    if f['type'] != 2:
        np_ = r.u32(V(1), V(2), V(3), B(3, 4)); f['passes'] = np_
        if np_ != 1:
            nd = r.u32(V(0), V(1), V(2), B(1, 3))
            [r.u(2) for _ in range(np_ - 1)]
            [r.u32(V(1), V(2), V(4), V(8)) for _ in range(nd)]
            [r.u32(V(0), V(1), V(2), B(3)) for _ in range(nd)]
    if f['type'] == 1:
        f['dc_level'] = r.u32(V(1), V(2), V(3), V(4))
    partial = False
    x0 = y0 = 0; fw, fh = xs, ys
    unpack = lambda u: (u >> 1) ^ -(u & 1)
    if f['type'] != 1:
        custom = r.b(); f['custom_size'] = custom
        if custom:
            crop = (B(8), B(11, 256), B(14, 2304), B(30, 18688))
            if f['type'] in (0, 3):
                x0 = unpack(r.u32(*crop)); y0 = unpack(r.u32(*crop))
            fw = r.u32(*crop); fh = r.u32(*crop)
            if f['type'] in (0, 3):
                partial = x0 > 0 or y0 > 0 or fw + x0 < xs or fh + y0 < ys
    main_mode = 0; duration = 0
    if f['type'] in (0, 3):
        for i in range(1 + m['num_ec']):
            mode = r.u32(V(0), V(1), V(2), B(2, 3))
            if i == 0: main_mode = mode
            uses_alpha = m['num_ec'] > 0 and mode in (2, 3)
            if uses_alpha:
                r.u32(V(0), V(1), V(2), B(3, 3))
            if uses_alpha or mode == 4:
                r.b()
            if mode != 0 or partial:
                r.u32(V(0), V(1), V(2), V(3))
        if m['have_anim']:
            duration = r.u32(V(0), V(1), B(8), B(32))
            if m.get('timecodes'): r.u(32)
        f['is_last'] = r.b()
    else:
        f['is_last'] = False
    f['save_as_ref'] = 0
    if f['type'] != 1 and not f['is_last']:
        f['save_as_ref'] = r.u32(V(0), V(1), V(2), V(3))
    can_ref = not f['is_last'] and f['type'] != 1 and (duration == 0 or f['save_as_ref'] != 0)
    if f['type'] != 1:
        if can_ref and main_mode == 0 and not partial and f['type'] in (0, 3):
            r.b()
        elif f['type'] == 2:
            r.b()
    nl = r.u32(V(0), B(4), B(5, 16), B(10, 48)); r.u(8 * nl)
    # loop filter
    f['lf_bit'] = r.bit
    f['lf_bits'] = ''.join(str(r.d[(r.bit + i) >> 3] >> ((r.bit + i) & 7) & 1) for i in range(12))
    if r.b():
        f['gab'] = True; f['epf'] = 2; f['lf_default'] = True  # LoopFilter default epf_iters is 2 (loop_filter.cc:58); fixed in D9 research
    else:
        f['gab'] = r.b()
        if f['gab'] and r.b():
            f['gab_custom'] = [r.f16() for _ in range(6)]
        f['epf'] = r.u(2)
        if f['epf'] > 0:
            if not f['modular'] and r.b(): f['epf_sharp_lut'] = [r.f16() for _ in range(8)]
            if r.b(): f['epf_weight_custom'] = [r.f16() for _ in range(5)]
            if r.b():
                q = r.f16() if not f['modular'] else None
                f['epf_sigma_custom'] = [q, r.f16(), r.f16(), r.f16()]
            if f['modular']: f['epf_sigma_modular'] = r.f16()
        r.ext()
    f['lf_end'] = r.bit
    f['ext'] = r.ext()
    return f


def unbox(data):
    if data[:2] == b'\xff\x0a':
        return data, 'bare codestream'
    pos = 0; cs = b''; boxes = []
    while pos < len(data):
        size, typ = struct.unpack('>I4s', data[pos:pos + 8]); hdr = 8
        if size == 1:
            size = struct.unpack('>Q', data[pos + 8:pos + 16])[0]; hdr = 16
        if size == 0: size = len(data) - pos
        body = data[pos + hdr:pos + size]
        boxes.append(typ.decode('latin1'))
        if typ == b'jxlc': cs += body
        if typ == b'jxlp': cs += body[4:]
        pos += size
    return cs, 'container: ' + ','.join(boxes)


def main(path):
    data, kind = unbox(open(path, 'rb').read())
    r = BR(data, 2)
    xs, ys = size_header(r)
    m = image_metadata(r)
    t = transform_data(r, m['xyb'])
    out = [f"{path}: {kind}; {xs}x{ys}; xyb_encoded={m['xyb']}; bit_depth={m['bit_depth']}; extra_channels={m['num_ec']} {m['ec']}",
           f"  colour={m['color']}; transform_data={t}"]
    if m['color'].get('want_icc'):
        out.append('  ICC present: stop (entropy-coded ICC not parsed)')
        print('\n'.join(out)); return
    r.align()
    f = frame_header(r, m, xs, ys)
    flag_names = {1: 'noise', 2: 'patches', 16: 'splines', 32: 'use_dc_frame', 128: 'skip_adaptive_dc_smoothing'}
    flags = [n for b_, n in flag_names.items() if f['flags'] & b_]
    out.append(f"  frame: type={f['type']} encoding={'Modular' if f['modular'] else 'VarDCT'} ct={f['ct']} flags={flags or 'none'} "
               f"upsampling={f['upsampling']} passes={f['passes']} gab={f['gab']} epf_iters={f['epf']} x_qm_scale={f['x_qm']} b_qm_scale={f['b_qm']} "
               f"group_shift={f['group_shift']} is_last={f['is_last']}")
    lf = {k: f[k] for k in ('lf_bit', 'lf_bits', 'lf_default', 'gab_custom', 'epf_sharp_lut', 'epf_weight_custom', 'epf_sigma_custom', 'epf_sigma_modular') if k in f}
    out.append(f"  loop filter: gab={f['gab']} epf_iters={f['epf']} {lf}")
    # TOC
    gdim = 128 << f['group_shift']
    ng = ((xs + gdim - 1) // gdim) * ((ys + gdim - 1) // gdim)
    ndc = ((xs + gdim * 8 - 1) // (gdim * 8)) * ((ys + gdim * 8 - 1) // (gdim * 8))
    nt = 1 if ng == 1 and f['passes'] == 1 else 2 + ndc + ng * f['passes']
    perm = r.b()
    if perm:
        out.append('  TOC permuted: stop'); print('\n'.join(out)); return
    r.align()
    sizes = [r.u32(B(10), B(14, 1024), B(22, 17408), B(30, 4211712)) for _ in range(nt)]
    r.align()
    base = r.bit // 8
    out.append(f"  groups={ng} dc_groups={ndc} toc_entries={nt} section_bytes={sizes[:6]}{'...' if nt > 6 else ''}")
    # DC global
    s = BR(data, base)
    if f['flags'] & 0x13:
        out.append('  DC global starts with patches/splines/noise: not parsed further'); print('\n'.join(out)); return
    dc_default = s.b(); dcq = None
    if not dc_default: dcq = [s.f16() / 128 for _ in range(3)]
    line = f"  DC global: dequant DC all_default={dc_default}{'' if dc_default else ' ' + str(dcq)}"
    if not f['modular']:
        gs = s.u32(B(11, 1), B(11, 2049), B(12, 4097), B(16, 8193))
        qdc = s.u32(V(16), B(5, 1), B(8, 1), B(16, 1))
        bcm_default = s.b()
        line += f"; quantizer global_scale={gs} quant_dc={qdc}; block_ctx_map default={bcm_default}"
        if bcm_default:
            cfl_default = s.b()
            line += f"; CfL DC all_default={cfl_default}"
            if not cfl_default:
                cf = s.u32(V(84), V(256), B(8, 2), B(16, 258)); bx = s.f16(); bb = s.f16()
                line += f" (color_factor={cf} base_x={bx} base_b={bb} ytox_dc={s.u(8) - 128} ytob_dc={s.u(8) - 128})"
        else:
            dct = []
            for j in range(3):
                n = s.u(4)
                dct.append([s.u32(B(4), B(8, 16), B(16, 272), B(32, 65808)) for _ in range(n)])
            nq = s.u(4)
            qft = [s.u32(B(2), B(3, 4), B(5, 12), B(8, 44)) + 1 for _ in range(nq)]
            line += f' (custom block context map: dc_thresholds={dct} qf_thresholds={qft}; ctx map and CfL not parsed)'
    out.append(line)
    if not f['modular'] and nt > 1:
        a = BR(data, base + sum(sizes[:1 + ndc]))
        qm_default = a.b()
        line = f"  AC global: dequant matrices all_default={qm_default}"
        if qm_default:
            nhb = (ng - 1).bit_length() if ng > 1 else 0
            nh = 1 + a.u(nhb)
            uo = a.u32(V(0x5F), V(0x13), V(0), B(13))
            line += f"; num_histograms={nh}; pass0 used_orders=0x{uo:x} (bit 0 = DCT8 order customised)"
        out.append(line)
    print('\n'.join(out))


if __name__ == '__main__':
    for p in sys.argv[1:]:
        try:
            main(p)
        except SystemExit as e:
            print(p, 'stopped:', e)
