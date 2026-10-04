#!/usr/bin/env python3
# Draws the template for drawing a new skin by hand, into Assets/Skins/Template:
#
#   skin-guides.png  the guide marks on a transparent background, to put on a layer above your drawing
#   skin-start.png   his arms and hands from Onkey.png and nothing else, a starting layer to draw his head on
#   skin-preview.png the guides over a faded classic Onkey on white, for looking at
#
# The canvas is the size of Onkey.png (1774 x 887). Check a finished drawing with check-skin.py.
#
#   python3 -m pip install pillow
#   python3 Assets/Skins/make-template.py
import os
from PIL import Image, ImageDraw, ImageFont

here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, "Template")
os.makedirs(out, exist_ok=True)
classic = Image.open(os.path.join(here, "..", "Onkey.png")).convert("RGBA")
W, H = classic.size

EYES = [(753, 525), (1056, 512)]
BODY_LEFT, BODY_RIGHT = 470, 1330      # Where his body may reach below his ears (Skin.BodyLeft / BodyRight).
EAR_LINE = 703                          # Below this, the body is cut at BODY_LEFT / BODY_RIGHT.
ROOTS = [(620, 780, 680, 830), (1095, 780, 1150, 830)]
HANDS = [((0, 600, 262, 887), 250), ((1518, 600, 1774, 887), 1530)]   # Box, and the wrist line.
ARMS = [[(0, 600), (330, 600), (410, 722), (565, 722), (620, 752), (643, 778), (643, 887), (0, 887)],
        [(1137, 762), (1200, 748), (1340, 730), (1440, 620), (1774, 620), (1774, 887), (1137, 887)]]

red, blue, green, purple = (214, 48, 49, 255), (40, 110, 214, 255), (36, 150, 72, 255), (140, 70, 190, 255)


def font(size):
    for name in ("DejaVuSans-Bold.ttf", "Arial Bold.ttf", "Arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default(size=size)


def dashed(d, a, b, color, width=4, dash=18):
    (x0, y0), (x1, y1) = a, b
    length = max(1, ((x1 - x0) ** 2 + (y1 - y0) ** 2) ** 0.5)
    n = int(length // dash)
    for i in range(0, n, 2):
        t0, t1 = i / n, min(1, (i + 1) / n)
        d.line([(x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0), (x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1)], fill=color, width=width)


def guides():
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    small, big = font(22), font(30)
    d.rectangle([0, 0, W - 1, H - 1], outline=(120, 120, 120, 255), width=3)

    # Eyes: where the pupils go.
    for i, (x, y) in enumerate(EYES):
        d.ellipse([x - 50, y - 56, x + 50, y + 56], outline=blue, width=4)
        d.ellipse([x - 64, y - 70, x + 64, y + 70], outline=blue, width=2)
        d.line([(x - 12, y), (x + 12, y)], fill=blue, width=3)
        d.line([(x, y - 12), (x, y + 12)], fill=blue, width=3)
    d.text((EYES[0][0] - 190, 330), "EYES: solid black pupil over each +,", font=small, fill=blue)
    d.text((EYES[0][0] - 190, 356), "white all the way round it, then a coloured edge", font=small, fill=blue)

    # Body limits below the ears.
    dashed(d, (0, EAR_LINE), (W, EAR_LINE), green, 3)
    # The arm cuts also remove these strips beside his ears.
    for box in ((0, 600, 380, EAR_LINE), (1394, 600, W - 1, 713)):
        d.rectangle(box, outline=red, width=3)
        d.line([box[:2], box[2:]], fill=red, width=2)
    d.text((272, 640), "no drawing", font=small, fill=red)
    d.text((1400, 606), "no drawing", font=small, fill=red)
    for x in (BODY_LEFT, BODY_RIGHT):
        dashed(d, (x, EAR_LINE), (x, H), green, 4)
    d.text((BODY_LEFT + 10, EAR_LINE + 8), "below the line: body stays between the green lines", font=small, fill=green)
    d.text((BODY_LEFT + 10, EAR_LINE - 32), "above the line: draw anywhere but the crossed red boxes", font=small, fill=green)

    # Arm roots his body must cover.
    for box in ROOTS:
        d.rectangle(box, outline=red, width=4)
        d.line([box[:2], box[2:]], fill=red, width=2)
        d.line([(box[0], box[3]), (box[2], box[1])], fill=red, width=2)
    d.text((ROOTS[0][0] - 10, ROOTS[0][3] + 6), "cover these", font=small, fill=red)
    d.text((ROOTS[1][0] - 10, ROOTS[1][3] + 6), "cover these", font=small, fill=red)

    # Hands and arms.
    for (box, wrist) in HANDS:
        d.rectangle(box, outline=purple, width=3)
        dashed(d, (wrist, box[1]), (wrist, box[3]), purple, 3, 12)
    for x in (10, 1436):
        d.text((x, 546), "HANDS (optional): redraw", font=small, fill=purple)
        d.text((x, 570), "in the box, wrist at dashes", font=small, fill=purple)
    d.text((272, 776), "arms: the app", font=small, fill=purple)
    d.text((272, 800), "draws these", font=small, fill=purple)

    d.text((20, 14), "Onkey skin template  1774 x 887", font=big, fill=(90, 90, 90, 255))
    d.text((20, 52), "Draw on a layer under this one, then export only your drawing as a PNG with a transparent background.",
           font=small, fill=(90, 90, 90, 255))
    return img


def start():
    # His arms and hands from Onkey.png, beyond the body limits.
    mask = Image.new("L", (W, H), 0)
    m = ImageDraw.Draw(mask)
    for arm in ARMS:
        m.polygon(arm, fill=255)
    m.rectangle([BODY_LEFT, 0, BODY_RIGHT, H], fill=0)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    img.paste(classic, (0, 0), mask)
    return img


g = guides()
g.save(os.path.join(out, "skin-guides.png"), optimize=True)
start().save(os.path.join(out, "skin-start.png"), optimize=True)
preview = Image.new("RGBA", (W, H), (255, 255, 255, 255))
faded = classic.copy()
faded.putalpha(faded.getchannel("A").point(lambda a: a * 35 // 100))
preview.alpha_composite(faded)
preview.alpha_composite(g)
preview.convert("RGB").save(os.path.join(out, "skin-preview.png"), optimize=True)
print("Wrote", out)
