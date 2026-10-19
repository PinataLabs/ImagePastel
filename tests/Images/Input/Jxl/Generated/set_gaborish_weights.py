# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Gives the first frame of a bare JPEG XL codestream custom Gaborish weights:
#   py -3 set_gaborish_weights.py in.jxl out.jxl x1 x2 y1 y2 b1 b2
# cjxl v0.12.0 never writes custom Gaborish weights (gab_custom), so this is the only way to get a file
# with them. The frame must have Gaborish on with the default weights. The script sets gab_custom and
# inserts the six weights as float16 after it; every other bit of the frame header stays as it was.
# The TOC sizes start on a byte boundary after the TOC's permutation bit, so the sections are copied
# unchanged once the header bits are padded to a byte again.
#
# The parser reads only what comes before the loop-filter fields, in the order of this repo's port of
# the headers (Headers/*, Frame/JxlFrameHeader.cs; libjxl ef67fde2 headers.cc, image_metadata.cc,
# frame_header.cc and loop_filter.cc). It handles the files cjxl writes for these tests: no preview,
# no animation, no ICC profile, a single frame.

import struct
import sys


class Reader:
    def __init__(self, data, pos):
        self.data = data
        self.bit = pos * 8

    def u(self, n):
        v = 0
        for i in range(n):
            v |= ((self.data[self.bit >> 3] >> (self.bit & 7)) & 1) << i
            self.bit += 1
        return v

    def b(self):
        return self.u(1) == 1

    def u32(self, *dist):
        kind, n, offset = dist[self.u(2)]
        return n if kind == 'v' else self.u(n) + offset

    def u64(self):
        s = self.u(2)
        if s == 0:
            return 0
        if s == 1:
            return 1 + self.u(4)
        if s == 2:
            return 17 + self.u(8)
        v = self.u(12)
        shift = 12
        while self.u(1):
            if shift == 60:
                return v | (self.u(4) << 60)
            v |= self.u(8) << shift
            shift += 8
        return v

    def f16(self):
        return self.u(16)

    def enum(self):
        return self.u32(V(0), V(1), B(4, 2), B(6, 18))

    def extensions(self):
        e = self.u64()
        self.bit += sum(self.u64() for i in range(64) if e >> i & 1)


def V(v):
    return ('v', v, 0)


def B(n, offset=0):
    return ('b', n, offset)


def size_header(r):
    small = r.b()
    ys = (r.u(5) + 1) * 8 if small else r.u32(B(9, 1), B(13, 1), B(18, 1), B(30, 1))
    ratio = r.u(3)
    if ratio == 0:
        r.u(5) if small else r.u32(B(9, 1), B(13, 1), B(18, 1), B(30, 1))


def bit_depth(r):
    if r.b():
        r.u32(V(32), V(16), V(24), B(6, 1))
        r.u(4)
    else:
        r.u32(V(8), V(10), V(12), B(6, 1))


def colour_encoding(r):
    if r.b():
        return
    if r.b():
        sys.exit('an ICC profile is not handled')
    cs = r.enum()
    if cs != 2:
        if r.enum() == 2:
            sys.exit('a custom white point is not handled')
    if cs not in (1, 2):
        if r.enum() == 2:
            sys.exit('custom primaries are not handled')
    if cs != 2:
        if r.b():
            r.u(24)
        else:
            r.enum()
    r.enum()


def image_metadata(r):
    """Returns (xyb_encoded, number of extra channels)."""
    if r.b():
        return True, 0
    extra = r.b()
    if extra:
        r.u(3)
        if r.b():
            sys.exit('intrinsic size is not handled')
        if r.b():
            sys.exit('a preview is not handled')
        if r.b():
            sys.exit('animation is not handled')
    bit_depth(r)
    r.b()
    num_ec = r.u32(V(0), V(1), B(4, 2), B(12, 1))
    if num_ec:
        sys.exit('extra channels are not handled')
    xyb = r.b()
    colour_encoding(r)
    if extra and not r.b():
        r.u(16 * 3 + 1)  # tone mapping
    r.extensions()
    return xyb, num_ec


def transform_data(r, xyb):
    if r.b():
        return
    if xyb and not r.b():
        r.u(16 * 16)
    mask = r.u(3)
    for bit, n in ((1, 15), (2, 55), (4, 210)):
        if mask & bit:
            r.u(16 * n)


def gab_custom_bit(r, xyb):
    """Reads the frame header up to the loop filter's gab_custom bit and returns its position."""
    if r.b():
        sys.exit('the frame header is all default')
    frame_type = r.u32(V(0), V(1), V(2), V(3))
    if frame_type != 0:
        sys.exit('only a regular frame is handled')
    modular = r.b()
    flags = r.u64()
    ycbcr = False if xyb else r.b()
    if ycbcr:
        sys.exit('YCbCr is not handled')
    if not flags & 32:
        r.u32(V(1), V(2), V(4), V(8))
    if modular:
        r.u(2)
    elif xyb:
        r.u(3)
        r.u(3)
    if r.u32(V(1), V(2), V(3), B(3, 4)) != 1:
        sys.exit('passes are not handled')
    if r.b():
        sys.exit('a custom frame size is not handled')
    if r.u32(V(0), V(1), V(2), B(2, 3)) != 0:
        sys.exit('only the replace blending mode is handled')
    if not r.b():
        sys.exit('only a last frame is handled')
    r.u(8 * r.u32(V(0), B(4), B(5, 16), B(10, 48)))  # name
    if r.b():
        sys.exit('the loop filter is all default')
    if not r.b():
        sys.exit('Gaborish is off')
    return r.bit


def main(src, dst, weights):
    data = open(src, 'rb').read()
    if data[:2] != b'\xff\x0a':
        sys.exit('only a bare codestream is handled')
    r = Reader(data, 2)
    size_header(r)
    xyb, _ = image_metadata(r)
    transform_data(r, xyb)
    r.bit = (r.bit + 7) & ~7
    at = gab_custom_bit(r, xyb)
    if r.b():
        sys.exit('the Gaborish weights are custom already')

    # Skip the rest of the frame header: EPF, the loop-filter and frame extensions, and the TOC's
    # permutation bit.
    epf_iters = r.u(2)
    if epf_iters:
        sys.exit('EPF is not handled')
    r.extensions()
    r.extensions()
    if r.b():
        sys.exit('a permuted TOC is not handled')
    end = r.bit

    bits = [(data[i >> 3] >> (i & 7)) & 1 for i in range(end)]
    custom = [1]
    for w in weights:
        h = struct.unpack('<H', struct.pack('<e', w))[0]
        custom += [(h >> i) & 1 for i in range(16)]
    bits = bits[:at] + custom + bits[at + 1:]
    bits += [0] * (-len(bits) % 8)
    head = bytes(sum(bits[i + j] << j for j in range(8)) for i in range(0, len(bits), 8))
    open(dst, 'wb').write(head + data[(end + 7) // 8:])


if __name__ == '__main__':
    if len(sys.argv) != 9:
        sys.exit('usage: set_gaborish_weights.py in.jxl out.jxl x1 x2 y1 y2 b1 b2')
    main(sys.argv[1], sys.argv[2], [float(a) for a in sys.argv[3:]])
