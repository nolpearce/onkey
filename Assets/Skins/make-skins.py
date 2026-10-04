#!/usr/bin/env python3
# Draws Onkey's skins: each skin is a full sprite the same size as Onkey.png, with his arms, hands
# and ears lifted from it so the arm rig, blinking and moving pupils all still fit. The pictures are
# checked in, so this only needs running again to change them:
#
#   python3 -m pip install pillow
#   python3 Assets/Skins/make-skins.py
#
# Each eye must keep a black pupil fully inside a white eye, centred on the eye seeds the renderers
# use ((753, 525) and (1056, 512)), with a coloured (not grey) shape around the white. Below y = 703
# his body is cut away outside the skin's bodyCut x values (Skin.cs / Skin.swift), where the rig's
# arms come out from under him, and it must cover the arm roots near (640, 805) and (1134, 805).
import math, os, random
from PIL import Image, ImageDraw

here = os.path.dirname(os.path.abspath(__file__))
classic = Image.open(os.path.join(here, "..", "Onkey.png")).convert("RGBA")
W, H = classic.size
SS = 2  # drawn this many times bigger, then shrunk, for smooth edges
EYES = [(753, 525), (1056, 512)]


class Pen:
    def __init__(self):
        self.img = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)

    def p(self, pts):
        return [(x * SS, y * SS) for x, y in pts]

    def fill(self, pts, color):
        self.d.polygon(self.p(pts), fill=color)

    def line(self, pts, color, width, closed=False):
        pts = self.p(pts)
        if closed:
            pts = pts + [pts[0], pts[1]]
        w = max(1, int(width * SS))
        self.d.line(pts, fill=color, width=w, joint="curve")
        r = w / 2
        ends = [] if closed else [pts[0], pts[-1]]
        for x, y in ends:
            self.d.ellipse([x - r, y - r, x + r, y + r], fill=color)

    def shape(self, pts, color, outline, width):
        self.fill(pts, color)
        self.line(pts, outline, width, closed=True)

    def done(self):
        return self.img.resize((W, H), Image.LANCZOS)


def wobble(pts, seed, amount):
    # A hand's unsteadiness: a slow drift along the line, not per-point noise.
    rng = random.Random(seed)
    a, b = rng.uniform(0, 6.3), rng.uniform(0, 6.3)
    n = len(pts)
    return [(x + amount * math.sin(2 * math.pi * 3 * i / n + a), y + amount * math.sin(2 * math.pi * 4 * i / n + b))
            for i, (x, y) in enumerate(pts)]


def bezier(points, steps=24):
    # Points along a chain of cubic curves: p0, c1, c2, p1, c1, c2, p2, ...
    out = []
    for k in range(0, len(points) - 1, 3):
        p0, c1, c2, p1 = points[k:k + 4]
        for i in range(steps):
            t = i / steps
            u = 1 - t
            out.append((u ** 3 * p0[0] + 3 * u * u * t * c1[0] + 3 * u * t * t * c2[0] + t ** 3 * p1[0],
                        u ** 3 * p0[1] + 3 * u * u * t * c1[1] + 3 * u * t * t * c2[1] + t ** 3 * p1[1]))
    out.append(points[-1])
    return out


def arms_and_ears(left_in, right_in, ear_shift):
    # His arms and hands from Onkey.png, beyond left_in / right_in (under his new body they're never
    # seen), and his ears moved out by ear_shift so a wider head doesn't swallow them.
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    arms = Image.new("L", (W, H), 0)
    m = ImageDraw.Draw(arms)
    # The arm outlines the renderers cut his arms out with (Renderer.cs / Renderer.swift).
    m.polygon([(0, 600), (330, 600), (410, 722), (565, 722), (620, 752), (643, 778), (643, 887), (0, 887)], fill=255)
    m.polygon([(1137, 762), (1200, 748), (1340, 730), (1440, 620), (1774, 620), (1774, 887), (1137, 887)], fill=255)
    m.rectangle([left_in, 0, right_in, H], fill=0)
    out.paste(classic, (0, 0), arms)
    for box, dx in (((410, 430, 606, 716), -ear_shift), ((1168, 430, 1366, 716), ear_shift)):
        ear = classic.crop(box)
        mask = Image.new("L", ear.size, 0)
        # Leave out the arm below each ear.
        ImageDraw.Draw(mask).rectangle([0, 0, ear.size[0], ear.size[1]], fill=255)
        cut = Image.new("L", (W, H), 0)
        ImageDraw.Draw(cut).polygon([(330, 600), (410, 722), (565, 722), (620, 752), (643, 778), (643, 887), (0, 887), (0, 600)], fill=255)
        ImageDraw.Draw(cut).polygon([(1137, 762), (1200, 748), (1340, 730), (1440, 620), (1774, 620), (1774, 887), (1137, 887)], fill=255)
        mask.paste(0, (0, 0), cut.crop(box))
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        layer.paste(ear, (box[0] + dx, box[1]), mask)
        out.alpha_composite(layer)
    return out


# Pumpkin: a carved jack-o'-lantern head, round and orange, with angry carved eyes, a jagged grin,
# a curly stem and his monkey ears poking out of the sides. Matches the cartoon it came from: flat
# colours, brown outlines, a few thin ribs.
PUMPKIN_CUT = (470, 1330)   # Skin.Pumpkin's bodyCut.


def pumpkin():
    pen = Pen()
    orange = (226, 131, 48, 255)
    rib = (196, 100, 34, 255)
    line = (96, 44, 20, 255)
    carved = (146, 86, 50, 255)
    carved_dark = (118, 64, 34, 255)
    flesh = (224, 172, 108, 255)
    flesh_line = (120, 66, 34, 255)
    white = (252, 250, 246, 255)
    black = (10, 8, 8, 255)
    stem = (138, 86, 56, 255)
    cx, cy, rx = 900, 532, 392
    top, bottom = 196, 870

    def edge(a):
        # Round on top, fuller and flatter underneath like a pumpkin sitting down.
        x, y = math.cos(a), math.sin(a)
        if y < 0:
            n, ry = 2.1, cy - top
        else:
            n, ry = 3.0, bottom - cy
        k = (abs(x) ** n + abs(y) ** n) ** (-1 / n)
        px, py = cx + x * k * rx, cy + y * k * ry
        # A little dip where the stem grows.
        if y < 0 and abs(px - cx) < 120:
            py += 14 * (1 - abs(px - cx) / 120) ** 2
        return px, py

    outline = wobble([edge(2 * math.pi * i / 160) for i in range(160)], 3, 2.0)
    pen.fill(outline, orange)

    # Ribs: thin curved strokes, broken like pencil, following his roundness.
    rng = random.Random(7)
    for f in (-0.8, -0.52, -0.2, 0.18, 0.5, 0.8):
        pts = []
        for i in range(31):
            t = i / 30
            y = top + 40 + (bottom - top - 80) * t
            yy = (y - cy) / ((cy - top) if y < cy else (bottom - cy))
            w = rx * max(0, 1 - abs(yy) ** 2.4) ** 0.45
            pts.append((cx + f * w, y))
        cuts = sorted(rng.sample(range(4, 27), 2))
        for a, b in ((0, cuts[0]), (cuts[0] + 3, cuts[1]), (cuts[1] + 3, 30)):
            if b - a >= 3:
                pen.line(pts[a:b + 1], rib, 5)
    pen.line(outline, line, 9, closed=True)

    # Stem, leaning over to his right.
    stem_pts = bezier([(868, 214), (864, 168), (866, 126), (890, 96), (902, 82), (930, 76), (944, 90),
                       (954, 100), (946, 118), (930, 118), (914, 120), (904, 140), (908, 176),
                       (910, 196), (914, 210), (916, 222)], 10)
    pen.shape(wobble(stem_pts, 5, 1.2), stem, line, 8)
    pen.line([(882, 200), (880, 160), (890, 128)], (112, 66, 40, 255), 5)

    # Eyes (over the grin's corners): carved angry sockets, each with a white eye and a big black pupil sitting high in it.
    # Grin: wide and carved, jagged teeth top and bottom, the top row meeting the bottom along a dark line.
    mx, half = 902, 232

    def mtop(t):
        return 628 - 40 * (2 * t - 1) ** 2 + 6 * math.sin(t * 9)

    def mbot(t):
        return 836 - 130 * (2 * t - 1) ** 2

    mouth = [(mx - half + 2 * half * i / 40, mtop(i / 40)) for i in range(41)]
    mouth += [(mx - half + 2 * half * i / 40, mbot(i / 40)) for i in range(40, -1, -1)]
    mouth = wobble(mouth, 21, 2.0)
    pen.shape(mouth, carved, line, 9)
    upper, lower = [], []
    teeth = 7
    for i in range(teeth * 2 + 1):
        t = 0.06 + 0.88 * i / (teeth * 2)
        x = mx - half + 2 * half * t
        gap = mbot(t) - mtop(t)
        upper.append((x, mtop(t) + gap * (0.07 if i % 2 == 0 else 0.28)))
    for i in range(teeth * 2 - 1):
        t = 0.1 + 0.8 * i / (teeth * 2 - 2)
        x = mx - half + 2 * half * t
        gap = mbot(t) - mtop(t)
        lower.append((x, mbot(t) - gap * (0.07 if i % 2 == 0 else 0.26)))
    tan = upper + [(mx + half * 0.86, mbot(0.93) - (mbot(0.93) - mtop(0.93)) * 0.3)] + lower[::-1] + \
        [(mx - half * 0.86, mbot(0.07) - (mbot(0.07) - mtop(0.07)) * 0.3)]
    pen.shape(tan, flesh, flesh_line, 6)
    seam = []
    for i in range(31):
        t = 0.1 + 0.8 * i / 30
        seam.append((mx - half + 2 * half * t, mtop(t) + (mbot(t) - mtop(t)) * 0.42 + 4))
    pen.line(seam, (70, 38, 22, 255), 6)

    for n, (ex, ey) in enumerate(EYES):
        s = -1 if n == 0 else 1   # Outward.
        prx, pry = 50, 56         # The pupil, centred on the eye seed.

        def brow(x, lift, slope):
            # Slopes down toward his nose; the eye's own brow stays clear of the top of the pupil.
            d = (x - ex) * s
            return ey - pry - lift - slope * d

        def eye_shape(grow_x, grow_y, lift, slope=0.45):
            pts = []
            for i in range(96):
                a = 2 * math.pi * i / 96
                x = ex + 8 * s + math.cos(a) * (68 + grow_x)
                y = ey + 8 + math.sin(a) * (74 + grow_y)
                pts.append((x, max(y, brow(x, lift, slope))))
            return pts

        # Socket: the eye's shape carved bigger, its angry top edge higher.
        sock = wobble(eye_shape(54, 34, 36, 0.8), 11 + n, 1.5)
        pen.shape(sock, carved, line, 9)
        pen.line(wobble(eye_shape(38, 22, 26, 0.68), 13 + n, 1.0), carved_dark, 5, closed=True)
        pen.shape(eye_shape(0, 0, 16), white, (40, 20, 12, 255), 6)
        pen.fill([(ex + math.cos(a) * prx, ey + math.sin(a) * pry) for a in [2 * math.pi * i / 72 for i in range(72)]], black)

    out = arms_and_ears(*PUMPKIN_CUT, 40)
    out.alpha_composite(pen.done())
    return out


pumpkin().save(os.path.join(here, "Pumpkin.png"), optimize=True)
print("Drew Pumpkin.png")
