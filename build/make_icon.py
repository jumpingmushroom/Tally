"""Thunderstore icon: 256x256 PNG. A Tally window in miniature: a dark plate with four bars
of falling length, each in a different hue. Written without PIL, which the build box lacks."""
import math
import struct
import zlib

S = 256
SS = 3                       # supersample
W = S * SS

px = bytearray(W * W * 4)

def blend(x, y, r, g, b, a):
    if x < 0 or y < 0 or x >= W or y >= W:
        return
    i = (y * W + x) * 4
    ia = 1.0 - a
    px[i] = int(r * a + px[i] * ia)
    px[i + 1] = int(g * a + px[i + 1] * ia)
    px[i + 2] = int(b * a + px[i + 2] * ia)
    px[i + 3] = int(min(255, 255 * a + px[i + 3] * ia))

def rounded_rect(x0, y0, x1, y1, rad, col, alpha=1.0):
    for y in range(int(y0), int(y1)):
        for x in range(int(x0), int(x1)):
            dx = max(x0 + rad - x, 0, x - (x1 - 1 - rad))
            dy = max(y0 + rad - y, 0, y - (y1 - 1 - rad))
            if dx * dx + dy * dy <= rad * rad:
                blend(x, y, *col, alpha)

def rect(x0, y0, x1, y1, col, alpha=1.0):
    for y in range(int(y0), int(y1)):
        for x in range(int(x0), int(x1)):
            blend(x, y, *col, alpha)

WOOD_DARK = (34, 25, 18)
PLATE = (10, 8, 6)
BORDER = (158, 133, 92)
ORANGE = (255, 161, 60)
BARS = [(217, 120, 70), (92, 171, 199), (134, 190, 96), (190, 140, 210)]

# ground
rounded_rect(0, 0, W, W, 44 * SS, WOOD_DARK)

# the window plate, with a thin border
m = 26 * SS
rounded_rect(m, m + 14 * SS, W - m, W - m, 6 * SS, BORDER)
rounded_rect(m + 2 * SS, m + 16 * SS, W - m - 2 * SS, W - m - 2 * SS, 5 * SS, PLATE)

# title bar strip
rect(m + 2 * SS, m + 16 * SS, W - m - 2 * SS, m + 44 * SS, (40, 30, 20), 0.9)
rect(m + 12 * SS, m + 26 * SS, m + 86 * SS, m + 34 * SS, ORANGE)          # "Damage Done"
for i in range(3):                                                         # buttons
    x = W - m - 14 * SS - i * 16 * SS
    rect(x - 10 * SS, m + 25 * SS, x, m + 35 * SS, (110, 95, 70), 0.9)

# bars, descending
top = m + 56 * SS
h = 28 * SS
gap = 8 * SS
inner_w = W - 2 * m - 24 * SS
fracs = [1.0, 0.72, 0.47, 0.26]
for i, f in enumerate(fracs):
    y0 = top + i * (h + gap)
    rect(m + 12 * SS, y0, m + 12 * SS + inner_w, y0 + h, (255, 255, 255), 0.05)   # track
    rect(m + 12 * SS, y0, m + 12 * SS + inner_w * f, y0 + h, BARS[i], 0.92)
    rect(m + 20 * SS, y0 + 10 * SS, m + 20 * SS + 34 * SS, y0 + 18 * SS, (250, 240, 220), 0.85)  # name

# downsample
out = bytearray()
for y in range(S):
    row = bytearray([0])
    for x in range(S):
        r = g = b = a = 0
        for sy in range(SS):
            for sx in range(SS):
                i = ((y * SS + sy) * W + (x * SS + sx)) * 4
                r += px[i]; g += px[i + 1]; b += px[i + 2]; a += px[i + 3]
        n = SS * SS
        row += bytes((r // n, g // n, b // n, a // n))
    out += row

def chunk(tag, data):
    c = tag + data
    return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xffffffff)

png = b"\x89PNG\r\n\x1a\n"
png += chunk(b"IHDR", struct.pack(">IIBBBBB", S, S, 8, 6, 0, 0, 0))
png += chunk(b"IDAT", zlib.compress(bytes(out), 9))
png += chunk(b"IEND", b"")
open("thunderstore/icon.png", "wb").write(png)
print("wrote thunderstore/icon.png", S, "x", S, len(png), "bytes")
