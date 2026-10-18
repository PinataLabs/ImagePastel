# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes animation_source.png into the current directory: a 30 x 20 8-bit RGBA APNG with four frames.
#
#   frame 0: the full canvas, shown for 120 ms;
#   frame 1: a 12 x 8 rectangle at (3, 2), replacing what is under it, with a delay of 0;
#   frame 2: a 14 x 10 rectangle at (13, 7), blended over the canvas, shown for 250 ms;
#   frame 3: a 9 x 15 rectangle at (19, 4), replacing what is under it, shown for 80 ms.
#
# cjxl keeps frame 1 as a frame with no duration, which a decoder blends into frame 2 and does not show,
# so the animation has three displayed frames. No frame is disposed, so each one builds on the last.

import struct
import zlib

WIDTH = 30
HEIGHT = 20

# (x0, y0, width, height, delay in ms, blend over).
FRAMES = [
    (0, 0, WIDTH, HEIGHT, 120, False),
    (3, 2, 12, 8, 0, False),
    (13, 7, 14, 10, 250, True),
    (19, 4, 9, 15, 80, False),
]


def rgba8(frame, x, y):
    # Each frame has its own pattern, and some alpha below 255, so a wrong blend shows.
    if frame == 0:
        return (x * 255) // (WIDTH - 1), (y * 255) // (HEIGHT - 1), 90, 255 - 4 * ((x + y) % 16)
    if frame == 1:
        return 230, (x * 37) & 0xFF, (y * 53) & 0xFF, 200
    if frame == 2:
        return (x * 19) & 0xFF, 40, 200, 64 + 12 * ((x * 3 + y) % 16)
    return 20, 180, (x * 29 + y * 11) & 0xFF, 255


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


def main():
    out = [b"\x89PNG\r\n\x1a\n", chunk(b"IHDR", struct.pack(">IIBBBBB", WIDTH, HEIGHT, 8, 6, 0, 0, 0))]
    out.append(chunk(b"acTL", struct.pack(">II", len(FRAMES), 0)))
    sequence = 0
    for index, (x0, y0, width, height, delay, blend) in enumerate(FRAMES):
        # fcTL: sequence, size, offset, delay (numerator / denominator in seconds), dispose none, blend op.
        out.append(chunk(b"fcTL", struct.pack(">IIIIIHHBB", sequence, width, height, x0, y0, delay, 1000, 0, 1 if blend else 0)))
        sequence += 1
        rows = b"".join(b"\x00" + b"".join(struct.pack(">4B", *rgba8(index, x, y)) for x in range(width)) for y in range(height))
        data = zlib.compress(rows, 9)
        if index == 0:
            out.append(chunk(b"IDAT", data))
        else:
            out.append(chunk(b"fdAT", struct.pack(">I", sequence) + data))
            sequence += 1

    out.append(chunk(b"IEND", b""))
    with open("animation_source.png", "wb") as f:
        f.write(b"".join(out))


if __name__ == "__main__":
    main()
