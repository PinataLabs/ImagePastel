# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes the sources of the colour-encoding test files into the current directory: colour_source.ppm,
# 32 x 32 8-bit RGB, and colour_grey_source.pgm, 32 x 32 8-bit grey. PPM and PGM carry no colour
# information, so cjxl takes the colour encoding from its -x color_space hint.

SIZE = 32

with open("colour_source.ppm", "wb") as f:
    f.write(b"P6\n%d %d\n255\n" % (SIZE, SIZE))
    f.write(bytes(v for y in range(SIZE) for x in range(SIZE) for v in ((x * 8) & 0xFF, (y * 8) & 0xFF, ((x + y) * 4) & 0xFF)))

with open("colour_grey_source.pgm", "wb") as f:
    f.write(b"P5\n%d %d\n255\n" % (SIZE, SIZE))
    f.write(bytes(((x * 8) ^ (y * 4)) & 0xFF for y in range(SIZE) for x in range(SIZE)))
