# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes the sources of the noise files into the current directory:
#
#   noise_source.ppm: a 540 x 280 8-bit RGB image of smooth gradients over the whole intensity range,
#   so that the noise strength comes from every entry of the noise look-up table.
#
#   noise_grey_source.pgm: a 200 x 120 8-bit grey image, the mean of noise_source.ppm's gradient, with a
#   small 17-step ripple so that the VarDCT blocks are not flat.
#
#   noise_animation_source.png: a 280 x 80 8-bit RGB APNG with four frames, two groups wide:
#     frame 0: the full canvas, shown for 100 ms;
#     frame 1: a 120 x 50 rectangle at (10, 10), with a delay of 0;
#     frame 2: a 120 x 50 rectangle at (150, 25), shown for 100 ms;
#     frame 3: the full canvas, shown for 100 ms.
#
# cjxl keeps frame 1 as a frame with no duration, which is not shown, so the decoder's non-visible frame
# counter seeds its noise. Frame 2 does not cover it, so its noise shows in the second displayed frame.

import struct
import zlib


def gradient(x, y, width, height, phase):
    r = (x * 255) // (width - 1)
    g = (y * 255) // (height - 1)
    b = ((x + y + phase) * 255 // (width + height - 2)) % 256
    return r, g, b


def write_ppm(path, width, height):
    pixels = bytearray()
    for y in range(height):
        for x in range(width):
            pixels.extend(gradient(x, y, width, height, 0))
    with open(path, "wb") as f:
        f.write(b"P6\n%d %d\n255\n" % (width, height))
        f.write(bytes(pixels))


def write_pgm(path, width, height):
    pixels = bytearray()
    for y in range(height):
        for x in range(width):
            r, g, b = gradient(x, y, width, height, 0)
            pixels.append(min(255, (r + g + b) // 3 + (x * y) % 17))
    with open(path, "wb") as f:
        f.write(b"P5\n%d %d\n255\n" % (width, height))
        f.write(bytes(pixels))


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


def write_apng(path, width, height, frames):
    out = [b"\x89PNG\r\n\x1a\n", chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))]
    out.append(chunk(b"acTL", struct.pack(">II", len(frames), 0)))
    sequence = 0
    for index, (x0, y0, w, h, delay) in enumerate(frames):
        # fcTL: sequence, size, offset, delay (numerator / denominator in seconds), dispose none, blend source.
        out.append(chunk(b"fcTL", struct.pack(">IIIIIHHBB", sequence, w, h, x0, y0, delay, 1000, 0, 0)))
        sequence += 1
        rows = b"".join(
            b"\x00" + b"".join(struct.pack(">3B", *gradient(x0 + x, y0 + y, width, height, 64 * index)) for x in range(w))
            for y in range(h))
        data = zlib.compress(rows, 9)
        if index == 0:
            out.append(chunk(b"IDAT", data))
        else:
            out.append(chunk(b"fdAT", struct.pack(">I", sequence) + data))
            sequence += 1

    out.append(chunk(b"IEND", b""))
    with open(path, "wb") as f:
        f.write(b"".join(out))


def main():
    write_ppm("noise_source.ppm", 540, 280)
    write_pgm("noise_grey_source.pgm", 200, 120)
    write_apng("noise_animation_source.png", 280, 80, [
        (0, 0, 280, 80, 100),
        (10, 10, 120, 50, 0),
        (150, 25, 120, 50, 100),
        (0, 0, 280, 80, 100),
    ])


if __name__ == "__main__":
    main()
