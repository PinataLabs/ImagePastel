# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Gives the first frame of a bare JPEG XL codestream custom Gaborish weights:
#   py -3 set_gaborish_weights.py in.jxl out.jxl x1 x2 y1 y2 b1 b2
# cjxl v0.12.0 never writes custom Gaborish weights (gab_custom), so this is the only way to get a file
# with them. The frame must have Gaborish on with the default weights, and EPF off. The script sets
# gab_custom and inserts the six weights as float16 after it; every other bit of the frame header stays
# as it was. It reads the headers with lfhdr.py and rewrites the loop-filter bundle with patch_lf.py,
# which copies the TOC and the sections.
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lfhdr as H  # noqa: E402
import patch_lf  # noqa: E402


def main(src, dst, weights):
    data = open(src, 'rb').read()
    if data[:2] != b'\xff\x0a':
        sys.exit('only a bare codestream is handled')
    r = H.BR(data, 2)
    xs, ys = H.size_header(r)
    m = H.image_metadata(r)
    H.transform_data(r, m['xyb'])
    if m['color'].get('want_icc'):
        sys.exit('an ICC profile is not handled')
    r.align()
    f = H.frame_header(r, m, xs, ys)
    if f.get('lf_default'):
        sys.exit('the loop filter is all default')
    if not f['gab']:
        sys.exit('Gaborish is off')
    if 'gab_custom' in f:
        sys.exit('the Gaborish weights are custom already')
    if f['epf']:
        sys.exit('EPF is not handled')
    patch_lf.main(src, dst, {'gab': True, 'gab_custom': weights})


if __name__ == '__main__':
    if len(sys.argv) != 9:
        sys.exit('usage: set_gaborish_weights.py in.jxl out.jxl x1 x2 y1 y2 b1 b2')
    main(sys.argv[1], sys.argv[2], [float(a) for a in sys.argv[3:]])
