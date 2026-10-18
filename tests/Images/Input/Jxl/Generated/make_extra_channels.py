# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes three 16 x 16 8-bit PAMs into the current directory, each of one colour:
#   two_alpha_a_source.pam  RGB (200, 100, 50), alpha 128, a second alpha channel of 255
#   two_alpha_b_source.pam  RGB (40, 220, 160), alpha 64, a second alpha channel of 192
#   depth_source.pam        RGB (200, 100, 50) and a depth channel of 7
# cjxl reads each extra TUPLTYPE line after the first as one more extra channel.

SIZE = 16


def write_pam(path, tupltypes, pixel):
    header = "P7\nWIDTH {0}\nHEIGHT {0}\nDEPTH {1}\nMAXVAL 255\n".format(SIZE, len(pixel))
    header += "".join("TUPLTYPE {0}\n".format(t) for t in tupltypes) + "ENDHDR\n"
    with open(path, "wb") as f:
        f.write(header.encode("ascii") + bytes(pixel) * (SIZE * SIZE))


write_pam("two_alpha_a_source.pam", ["RGB_ALPHA", "Alpha"], [200, 100, 50, 128, 255])
write_pam("two_alpha_b_source.pam", ["RGB_ALPHA", "Alpha"], [40, 220, 160, 64, 192])
write_pam("depth_source.pam", ["RGB", "Depth"], [200, 100, 50, 7])
