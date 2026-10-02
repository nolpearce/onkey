#!/usr/bin/env python3
# Draws the jungle pictures for the Mac app icon, the Mac DMG window and the Windows installer, in the same
# hand-drawn style and colours as the settings panel (Mac/Sources/SettingsPanel.swift).
# The pictures are checked in, so this only needs running again to change them:
#
#   python3 -m pip install pillow
#   python3 Assets/Installer/make-art.py path/to/PatrickHand-Regular.ttf
#
# Patrick Hand is a free handwriting font from Google Fonts (fonts.google.com/specimen/Patrick+Hand).
import math, os, random, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

here = os.path.dirname(os.path.abspath(__file__))
font_path = sys.argv[1]
sprite = Image.open(os.path.join(here, "..", "Onkey.png")).convert("RGBA")

night = (28, 52, 34); canopy = (40, 74, 44); paper = (246, 237, 211); ink = (58, 40, 24)
faded = (140, 120, 94); bark = (146, 98, 54); banana = (244, 200, 66); vine = (104, 122, 52)
greens = [(34, 70, 38), (56, 104, 46), (72, 128, 56), (92, 146, 62)]
SS = 4  # drawn this many times bigger, then shrunk, for smooth edges


class Canvas:
    def __init__(self, w, h, scale):
        self.w, self.h, self.k = w, h, scale * SS
        self.img = Image.new("RGB", (int(w * self.k), int(h * self.k)), night)
        self.d = ImageDraw.Draw(self.img, "RGBA")

    def p(self, pts):
        return [(x * self.k, y * self.k) for x, y in pts]

    def gradient(self):
        H = self.img.height
        for y in range(H):
            t = y / max(1, H - 1)
            c = tuple(int(canopy[i] + (night[i] - canopy[i]) * t) for i in range(3))
            self.d.line([(0, y), (self.img.width, y)], fill=c)

    def poly(self, pts, fill=None, line=None, width=1.6):
        pts = self.p(pts)
        if fill: self.d.polygon(pts, fill=fill)
        if line:
            self.d.line(pts + [pts[0]], fill=line, width=max(1, int(width * self.k)), joint="curve")

    def stroke(self, pts, color, width):
        self.d.line(self.p(pts), fill=color, width=max(1, int(width * self.k)), joint="curve")

    def ellipse(self, cx, cy, rx, ry, fill):
        k = self.k
        self.d.ellipse([(cx - rx) * k, (cy - ry) * k, (cx + rx) * k, (cy + ry) * k], fill=fill)

    def text(self, xy, s, size, color, anchor="mm", angle=0):
        f = ImageFont.truetype(font_path, int(size * self.k))
        if angle == 0:
            self.d.text((xy[0] * self.k, xy[1] * self.k), s, font=f, fill=color, anchor=anchor)
            return
        box = self.d.textbbox((0, 0), s, font=f, anchor="lt")
        layer = Image.new("RGBA", (box[2] + 20, box[3] + 20), (0, 0, 0, 0))
        ImageDraw.Draw(layer).text((10, 10), s, font=f, fill=color, anchor="lt")
        layer = layer.rotate(angle, resample=Image.BICUBIC, expand=True)
        self.img.paste(layer, (int(xy[0] * self.k - layer.width / 2), int(xy[1] * self.k - layer.height / 2)), layer)

    def paste(self, im, cx, bottom, width):
        height = width * im.height / im.width
        scaled = im.resize((int(width * self.k), int(height * self.k)), Image.LANCZOS)
        self.img.paste(scaled, (int((cx - width / 2) * self.k), int((bottom - height) * self.k)), scaled)

    def save(self, path, mode="RGB"):
        out = self.img.resize((int(self.w * self.k / SS), int(self.h * self.k / SS)), Image.LANCZOS).convert(mode)
        out.save(path)
        print("wrote", os.path.relpath(path), out.size)


def wobble(points, rng, amount, closed=True):
    # Fills in long runs, then shakes every point a little, like a pencil line.
    out = []
    n = len(points) if closed else len(points) - 1
    for i in range(n):
        a, b = points[i], points[(i + 1) % len(points)]
        steps = max(1, int(math.dist(a, b) / 22))
        for s in range(steps):
            t = s / steps
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    if not closed: out.append(points[-1])
    return [(x + rng.uniform(-amount, amount), y + rng.uniform(-amount, amount)) for x, y in out]


def smooth(points, closed=True, per=8):
    # Catmull-Rom through the points.
    out, n = [], len(points)
    rng = range(n) if closed else range(n - 1)
    for i in rng:
        p0 = points[(i - 1) % n] if closed or i > 0 else points[0]
        p1, p2 = points[i], points[(i + 1) % n]
        p3 = points[(i + 2) % n] if closed or i + 2 < n else points[-1]
        for s in range(per):
            t = s / per
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * (2 * p1[j] + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2
                                    + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3) for j in range(2)))
    if not closed: out.append(points[-1])
    return out


def sketch_box(c, x, y, w, h, r, rng, fill, line=ink, width=2):
    pts = []
    for cx, cy, start in [(x + w - r, y + r, -90), (x + w - r, y + h - r, 0), (x + r, y + h - r, 90), (x + r, y + r, 180)]:
        for i in range(4):
            a = math.radians(start + i * 30)
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    outline = smooth(wobble(pts, rng, 1.6))
    c.poly(outline, fill=fill + (255,))
    c.poly(outline, line=ink + (230,), width=width)
    c.poly([(px + 0.8, py - 0.6) for px, py in outline], line=ink + (80,), width=width * 0.6)


def leaf(c, base, angle, length, width, color):
    def bez(p0, p1, p2, p3, n=14):
        return [tuple((1 - t) ** 3 * p0[j] + 3 * (1 - t) ** 2 * t * p1[j] + 3 * (1 - t) * t * t * p2[j] + t ** 3 * p3[j]
                      for j in range(2)) for t in (i / n for i in range(n + 1))]
    shape = bez((0, 0), (length * 0.3, -width), (length * 0.75, -width * 0.8), (length, 0)) + \
        bez((length, 0), (length * 0.75, width * 0.8), (length * 0.3, width), (0, 0))[1:]
    a = math.radians(angle)
    rot = lambda p: (base[0] + p[0] * math.cos(a) - p[1] * math.sin(a), base[1] + p[0] * math.sin(a) + p[1] * math.cos(a))
    c.poly([rot(p) for p in shape], fill=color + (255,))
    c.poly([rot(p) for p in shape], line=ink + (150,), width=1.4)
    c.stroke([rot((0, 0)), rot((length * 0.9, 0))], ink + (110,), 1.2)
    for i in range(1, 4):
        x = length * i / 4.5
        for side in (-1, 1):
            c.stroke([rot((x, 0)), rot((x + length * 0.12, side * width * 0.45))], ink + (110,), 1.2)


def backdrop(c, seed, leaves_per_layer, leaf_size, vines, keep_clear=None):
    rng = random.Random(seed)
    c.gradient()
    w, h = c.w, c.h
    for _ in range(int(w * h / 9000) + 6):
        s = 20 + rng.random() * 60
        c.ellipse(rng.random() * w, rng.random() * h, s / 2, s * 0.35, (200, 230, 140, int(255 * (0.06 + rng.random() * 0.05))))
    for layer, green in enumerate(greens):
        for _ in range(leaves_per_layer):
            side, along = rng.randrange(4), rng.random()
            at = [(along * w, -6), (w + 6, along * h), (along * w, h + 6), (-6, along * h)][side]
            if keep_clear and side == keep_clear: continue
            toward = math.degrees(math.atan2(h / 2 - at[1], w / 2 - at[0])) + rng.uniform(-35, 35)
            length = leaf_size + rng.random() * leaf_size - layer * 8
            leaf(c, at, toward, length, length * 0.42, green)
    for i in range(vines):
        x = 40 + i * (w - 80) / max(1, vines - 1) + rng.random() * 30 - 15
        length = 40 + rng.random() * 50
        end = (x + rng.uniform(-8, 8), length)
        c.stroke(smooth(wobble([(x, -4), end], rng, 3, closed=False), closed=False), vine + (255,), 3)
        leaf(c, end, 70 + rng.random() * 40, 22, 11, greens[3])
        leaf(c, (x + 1, length * 0.5), 200 + rng.random() * 30, 18, 9, greens[3])


def arrow(c, start, end, rng, color):
    lift = (start[0] + end[0]) / 2, min(start[1], end[1]) - 34
    curve = [tuple((1 - t) ** 2 * start[j] + 2 * (1 - t) * t * lift[j] + t * t * end[j] for j in range(2))
             for t in (i / 24 for i in range(25))]
    curve = [(x + rng.uniform(-0.6, 0.6), y + rng.uniform(-0.6, 0.6)) for x, y in curve[:-1]] + [end]
    c.stroke(curve, color, 6)
    angle = math.atan2(end[1] - lift[1], end[0] - lift[0])
    for turn in (2.6, -2.6):
        c.stroke([(end[0] + math.cos(angle + turn) * 24, end[1] + math.sin(angle + turn) * 24), end], color, 6)


# The DMG window. Finder puts Onkey.app at (170, 222) and Applications at (470, 222); see
# Mac/make-dmg.sh, which must agree with these spots.
def dmg(scale):
    c = Canvas(640, 420, scale)
    backdrop(c, seed=7, leaves_per_layer=7, leaf_size=60, vines=6)
    rng = random.Random(3)
    sketch_box(c, 50, 132, 540, 222, 22, rng, paper)
    c.paste(sprite, 320, 142, 210)
    arrow(c, (252, 226), (390, 226), rng, bark + (255,))
    c.text((320, 330), "Drag Onkey into Applications", 24, ink + (255,))
    return c


def wizard_side(scale):
    c = Canvas(164, 314, scale)
    backdrop(c, seed=11, leaves_per_layer=4, leaf_size=40, vines=3)
    rng = random.Random(5)
    sketch_box(c, 14, 156, 136, 64, 14, rng, paper)
    c.paste(sprite, 82, 166, 132)
    c.text((82, 192), "Onkey", 34, ink + (255,))
    c.text((82, 246), "a little jungle", 15, paper + (235,))
    c.text((82, 264), "friend for your", 15, paper + (235,))
    c.text((82, 282), "desktop", 15, paper + (235,))
    return c


def wizard_small(scale):
    c = Canvas(58, 58, scale)
    c.img.paste(paper, [0, 0, c.img.width, c.img.height])
    head = sprite.crop((380, 0, 1394, 887))
    c.paste(head, 29, 52, 54)
    return c


# The Mac app icon (Assets/AppIcon.png): Onkey peeking over a paper ledge in the jungle,
# on Apple's icon grid (an 824-point rounded square in a 1024 canvas, with a soft shadow).
def app_icon():
    size, body, inset = 1024, 824, 100
    c = Canvas(body, body, 1)
    backdrop(c, seed=21, leaves_per_layer=3, leaf_size=150, vines=4)
    rng = random.Random(9)
    sketch_box(c, -40, 640, body + 80, 260, 40, rng, paper, width=9)
    c.paste(sprite, body / 2, 686, 980)
    art = c.img.resize((body, body), Image.LANCZOS).convert("RGBA")
    mask = Image.new("L", (body * SS, body * SS), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, body * SS - 1, body * SS - 1], radius=185 * SS, fill=255)
    art.putalpha(mask.resize((body, body), Image.LANCZOS))
    icon = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 90), (inset, inset + 12), art.getchannel("A"))
    icon.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(14)))
    icon.alpha_composite(art, (inset, inset))
    path = os.path.join(here, "..", "AppIcon.png")
    icon.save(path)
    print("wrote", os.path.relpath(path), icon.size)


out = here
app_icon()
dmg(1).save(os.path.join(out, "dmg-background.png"))
dmg(2).save(os.path.join(out, "dmg-background@2x.png"))
wizard_side(1).save(os.path.join(out, "wizard.bmp"))
wizard_side(2).save(os.path.join(out, "wizard@2x.bmp"))
wizard_small(1).save(os.path.join(out, "wizard-small.bmp"))
wizard_small(2).save(os.path.join(out, "wizard-small@2x.bmp"))
