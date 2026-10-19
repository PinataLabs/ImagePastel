# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Clears the skip-adaptive-DC-smoothing flag of the first frame of a JPEG XL file, and writes a bare
# codestream:
#   py -3 clear_skip_smoothing.py in.jxl out.jxl
# cjxl v0.12.0 sets the flag (kSkipAdaptiveDCSmoothing, 128) on every JPEG transcode, 4:4:4 included, so
# this is the only way to get a YCbCr frame that runs adaptive DC smoothing. The flags must be exactly
# 128. They are a U64 in the frame header: 128 is selector 2 and 8 bits, and 0 is selector 0 alone. Every
# other bit of the frame header stays as it was. The TOC sizes start on a byte boundary after the TOC's
# permutation bit, so the TOC and the sections are copied as they are. A container is unboxed first; the
# JPEG reconstruction box is dropped, as only the pixels are decoded.
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lfhdr as H  # noqa: E402
import patch_lf  # noqa: E402


def main(src, dst):
    data, _ = H.unbox(open(src, 'rb').read())
    r = H.BR(data, 2)
    xs, ys = H.size_header(r)
    m = H.image_metadata(r)
    H.transform_data(r, m['xyb'])
    if m['color'].get('want_icc'):
        sys.exit('an ICC profile is not handled')
    r.align()
    frame_start = r.bit
    if r.b():
        sys.exit('the frame header is all default')
    r.u32(H.V(0), H.V(1), H.V(2), H.V(3))  # frame_type
    r.b()  # encoding
    flags_start = r.bit
    flags = r.u64()
    flags_end = r.bit
    if flags != 128:
        sys.exit(f'the flags are {flags}, not 128')
    r.bit = frame_start
    H.frame_header(r, m, xs, ys)
    if r.b():
        sys.exit('a permuted TOC is not handled')
    perm_end = r.bit
    old = patch_lf.bits_of(data)
    new = old[:flags_start] + [0, 0] + old[flags_end:perm_end]
    new += [0] * (-len(new) % 8)
    head = bytes(sum(new[i + j] << j for j in range(8)) for i in range(0, len(new), 8))
    open(dst, 'wb').write(head + data[(perm_end + 7) // 8:])


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
