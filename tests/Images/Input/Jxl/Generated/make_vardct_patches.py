# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes the source of the grey VarDCT patches file into the current directory:
#   vardct_xyb_grey_patches_source.pgm   200 x 120 8-bit grey: lines of "text" on a flat background, with
#                                        a smooth gradient band below them
# The text is four 5 x 7 glyphs at twice their size, repeated along each line, so cjxl --patches=1
# stores each glyph once in a reference frame and pastes the others with patches.

WIDTH = 200
HEIGHT = 120
BACKGROUND = 235
INK = 30

GLYPHS = [
    ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    ["11110", "10001", "11110", "10001", "10001", "10001", "11110"],
    ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
    ["11111", "10000", "11110", "10000", "10000", "10000", "11111"],
]


def pixels():
    rows = [[BACKGROUND] * WIDTH for _ in range(HEIGHT)]
    # Five lines of text, 12 glyph cells (14 pixels each) a line.
    for line in range(5):
        y0 = 4 + line * 18
        for cell in range(13):
            glyph = GLYPHS[(cell + line) % len(GLYPHS)]
            x0 = 4 + cell * 14
            for gy, bits in enumerate(glyph):
                for gx, bit in enumerate(bits):
                    if bit == "1":
                        for dy in range(2):
                            for dx in range(2):
                                rows[y0 + gy * 2 + dy][x0 + gx * 2 + dx] = INK
    # A smooth gradient band below the text, so the VarDCT frame codes more than a flat background.
    for y in range(96, HEIGHT):
        for x in range(WIDTH):
            rows[y][x] = 60 + (x * 150) // (WIDTH - 1) + (y - 96)
    return rows


with open("vardct_xyb_grey_patches_source.pgm", "wb") as f:
    f.write(b"P5\n%d %d\n255\n" % (WIDTH, HEIGHT))
    f.write(bytes(v for row in pixels() for v in row))
