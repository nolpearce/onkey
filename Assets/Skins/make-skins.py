#!/usr/bin/env python3
# Draws Onkey's skins in the style of Onkey.png: each skin is a full sprite the same size as Onkey.png,
# with his arms, hands and ears lifted from it so the arm rig, blinking and moving pupils all still
# fit. The pictures are checked in, so this only needs running again to change them:
#
#   python3 -m pip install pillow
#   python3 Assets/Skins/make-skins.py
#
# Each eye must keep a black pupil inside a white eye, centred on the eye seeds the renderers use
# ((753, 525) and (1056, 512)), and below y = 703 his body must stay between x = 606 and 1150,
# where the rig's arms come out (ArmSpec.LeftCut / RightCut in Rig.cs and Rig.swift).
import math, os, random
from PIL import Image, ImageDraw

here = os.path.dirname(os.path.abspath(__file__))
classic = Image.open(os.path.join(here, "..", "Onkey.png")).convert("RGBA")
W, H = classic.size
SS = 2  # drawn this many times bigger, then shrunk, for smooth edges

ink = (26, 6, 3, 255)
EYES = [(753, 525), (1056, 512)]


class Pen:
    def __init__(self, seed):
        self.img = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.rng = random.Random(seed)

    def p(self, pts):
        return [(x * SS, y * SS) for x, y in pts]

    def shape(self, pts, fill, outline=ink, width=9):
        pts = self.p(pts)
        self.d.polygon(pts, fill=fill)
        if outline:
            self.line_px(pts + [pts[0]], outline, width)

    def line(self, pts, color, width):
        self.line_px(self.p(pts), color, width)

    def line_px(self, pts, color, width):
        w = max(1, int(width * SS))
        self.d.line(pts, fill=color, width=w, joint="curve")
        r = w / 2
        for x, y in (pts[0], pts[-1]):
            self.d.ellipse([x - r, y - r, x + r, y + r], fill=color)

    def ellipse(self, cx, cy, rx, ry, fill):
        self.d.ellipse([(cx - rx) * SS, (cy - ry) * SS, (cx + rx) * SS, (cy + ry) * SS], fill=fill)

    def done(self):
        return self.img.resize((W, H), Image.LANCZOS)


def wobble(pts, rng, amount):
    # A hand's unsteadiness: a slow drift along the line, not per-point noise.
    a, b = rng.uniform(0, 6.3), rng.uniform(0, 6.3)
    out = []
    for i, (x, y) in enumerate(pts):
        t = i / max(1, len(pts) - 1)
        out.append((x + amount * math.sin(t * 9 + a), y + amount * math.sin(t * 13 + b)))
    return out


def blob(cx, cy, rx, ry, steps=64, squash=None):
    # A closed round shape; squash(angle) -> radius factor shapes it.
    pts = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        k = squash(a) if squash else 1
        pts.append((cx + math.cos(a) * rx * k, cy + math.sin(a) * ry * k))
    return pts


def without_body(sprite):
    # His ears, arms and hands, with the head and body between them cleared away.
    out = sprite.copy()
    clear = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    mask = Image.new("L", (W, H), 0)
    m = ImageDraw.Draw(mask)
    m.rectangle([605, 0, 1150, H], fill=255)
    m.rectangle([1150, 0, 1175, 703], fill=255)
    out.paste(clear, (0, 0), mask)
    return out


# Pumpkin: a carved jack-o'-lantern head with angry eyes and a jagged grin, his monkey ears
# poking out of the sides, and a curly stem where his tuft was.
def pumpkin():
    rng = random.Random(31)
    pen = Pen(31)
    skin = (236, 132, 38, 255)
    shade = (205, 102, 26, 255)
    rib = (184, 86, 22, 255)
    glow = (250, 168, 70, 255)
    carved = (122, 60, 26, 255)
    flesh = (232, 178, 112, 255)
    stem = (150, 82, 42, 255)
    stem_dark = (112, 56, 26, 255)
    cx, top, bottom = 878, 250, 866

    def half_width(y):
        # Widest across his eyes, tucking in toward the bottom so his arms come out from under him.
        if y < 480:
            t = (480 - y) / (480 - top)
            return 342 * (1 - t ** 1.9) ** 0.5
        if y < 690:
            t = (y - 480) / (690 - 480)
            return 342 - (342 - 270) * (t * t * (3 - 2 * t))
        if y < bottom - 52:
            return 270 - 3 * math.sin(math.pi * (y - 690) / (bottom - 52 - 690))
        t = (y - (bottom - 52)) / 52
        return 270 - 70 * (1 - math.sqrt(max(0, 1 - t * t)))

    left, right = [], []
    for i in range(0, 121):
        y = top + (bottom - top) * i / 120
        w = half_width(y)
        # A dip in the top where the stem grows.
        left.append((cx - w, y))
        right.append((cx + w, y))
    outline = left + right[::-1]
    dip = []
    for x, y in outline:
        d = abs(x - cx)
        if y < 320 and d < 150:
            y += 16 * (1 - d / 150) ** 2
        dip.append((x, y))
    outline = wobble(dip, rng, 2.5)

    # Body, with a darker band down each side and a highlight on the upper left.
    pen.shape(outline, skin, outline=None)
    for side in (-1, 1):
        band = []
        for i in range(0, 61):
            y = top + 40 + (bottom - top - 70) * i / 60
            w = half_width(y)
            band.append((cx + side * w * 0.99, y))
        for i in range(60, -1, -1):
            y = top + 40 + (bottom - top - 70) * i / 60
            w = half_width(y)
            band.append((cx + side * w * 0.80, y))
        pen.shape(band, shade, outline=None)
    pen.shape(blob(728, 352, 62, 32), glow, outline=None)

    # Ribs: soft curved strokes following his shape, broken here and there like pencil.
    for f in (-0.72, -0.4, -0.12, 0.16, 0.44, 0.74):
        y0, y1 = top + 34 + abs(f) * 60, bottom - 26 - abs(f) * 30
        pts = []
        for i in range(0, 41):
            y = y0 + (y1 - y0) * i / 40
            pts.append((cx + f * half_width(y) * 0.98 + 6 * math.sin(i * 0.4 + f * 5), y))
        pieces = [(0, 14), (17, 29), (32, 40)] if rng.random() < 0.6 else [(0, 22), (25, 40)]
        for a, b in pieces:
            pen.line(pts[a:b + 1], rib, 6)
    pen.line(outline + [outline[0]], ink, 9)

    # Stem, curling over to his right like the tuft it replaces.
    stem_pts = [(842, 254), (840, 200), (846, 160), (862, 128), (888, 104), (918, 96), (942, 108), (946, 128),
                (928, 132), (908, 134), (892, 150), (884, 178), (886, 214), (892, 256)]
    pen.shape(wobble(stem_pts, rng, 1.5), stem, width=9)
    pen.line([(866, 240), (862, 190), (872, 154)], stem_dark, 6)
    pen.ellipse(906, 112, 7, 5, (178, 104, 60, 255))

    # Eyes: carved angry sockets with a white eye and black pupil in each.
    for n, (ex, ey) in enumerate(EYES):
        inner = 1 if n == 0 else -1   # Toward his nose.
        sx = ex + inner * 6
        # Socket: a rounded slab whose top slopes down toward the middle.
        sock = []
        for i in range(48):
            a = 2 * math.pi * i / 48
            x, y = math.cos(a), math.sin(a)
            rx, ry = 104, 78
            px, py = sx + x * rx, ey + 4 + y * ry
            if y < 0:
                # Brow: the top flattens and tilts, lowest at the inner corner.
                py = ey - 66 + 40 * (x * inner + 1) / 2 + (1 + y) * 18
                py = max(py, ey + 4 + y * ry)
            sock.append((px, py))
        pen.shape(wobble(sock, rng, 2), carved, width=9)
        # Eye: round below, cut flat by the brow above.
        eye = []
        for i in range(48):
            a = 2 * math.pi * i / 48
            x, y = math.cos(a), math.sin(a)
            px, py = ex + x * 72, ey + 6 + y * 60
            if y < 0:
                py = max(py, ey - 54 + 22 * (x * inner + 1) / 2)
            eye.append((px, py))
        pen.shape(eye, (252, 250, 244, 255), width=7)
        pen.ellipse(ex, ey + 8, 38, 38, (8, 6, 6, 255))
        # A brow crease carved into the skin above the socket.
        pen.line([(sx - 92 * inner, ey - 82), (sx - 30 * inner, ey - 70), (sx + 50 * inner, ey - 44)], rib, 7)

    # Grin: a wide carved mouth, smiling up at the corners, with jagged teeth top and bottom.
    mx, mhalf = 900, 200

    def mouth_top(t):
        return 650 - 46 * (2 * t - 1) ** 2

    def mouth_bottom(t):
        return 794 - 150 * (2 * t - 1) ** 2

    mouth = [(mx - mhalf + 2 * mhalf * i / 40, mouth_top(i / 40)) for i in range(41)]
    mouth += [(mx - mhalf + 2 * mhalf * i / 40, mouth_bottom(i / 40)) for i in range(40, -1, -1)]
    pen.shape(wobble(mouth, rng, 2), carved, width=9)
    n, edge = 8, 0.86
    upper, lower = [], []
    for i in range(n * 2 + 1):
        t = 0.5 + (i / (n * 2) - 0.5) * edge
        x = mx - mhalf + 2 * mhalf * t
        gap = mouth_bottom(t) - mouth_top(t)
        upper.append((x, mouth_top(t) + gap * (0.08 + (0.22 if i % 2 else 0))))
        lower.append((x, mouth_bottom(t) - gap * (0.08 + (0 if i % 2 else 0.2))))
    pen.shape(upper + lower[::-1], flesh, outline=(150, 72, 30, 255), width=6)
    # Where his top teeth meet the bottom ones.
    seam = []
    for i in range(21):
        t = 0.5 + (i / 20 - 0.5) * 0.78
        seam.append((mx - mhalf + 2 * mhalf * t, (mouth_top(t) + mouth_bottom(t)) / 2 + 6))
    pen.line(seam, (170, 96, 46, 255), 5)

    out = without_body(classic)
    out.alpha_composite(pen.done())
    return out


pumpkin().save(os.path.join(here, "Pumpkin.png"), optimize=True)
print("Drew Pumpkin.png")
