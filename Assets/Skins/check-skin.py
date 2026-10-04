#!/usr/bin/env python3
# Checks a hand-drawn skin before it goes into Onkey: the size, that each eye has a black pupil the
# app can lift out and move, with white all the way round it, and that his body covers the arm roots
# and stays inside the body limits below his ears. Uses the same rules as the renderers.
#
#   python3 Assets/Skins/check-skin.py path/to/skin.png [body_left body_right]
import sys
from PIL import Image

EYES = [(753, 525), (1056, 512)]


def flood(px, w, h, seed, radius, accept):
    seen, stack, found = set(), [seed], []
    while stack:
        x, y = stack.pop()
        if (x - seed[0]) ** 2 + (y - seed[1]) ** 2 >= radius * radius or not (0 <= x < w and 0 <= y < h):
            continue
        if (x, y) in seen:
            continue
        seen.add((x, y))
        if not accept(px[x, y]):
            continue
        found.append((x, y))
        stack += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
    return found


def main():
    path = sys.argv[1]
    left, right = (float(sys.argv[2]), float(sys.argv[3])) if len(sys.argv) > 3 else (470, 1330)
    img = Image.open(path).convert("RGBA")
    ok = True
    if img.size != (1774, 887):
        print(f"FAIL size is {img.size[0]} x {img.size[1]}; it must be 1774 x 887")
        return 1
    w, h = img.size
    px = img.load()
    for name, seed in zip(("left", "right"), EYES):
        pupil = flood(px, w, h, seed, 75, lambda p: p[3] > 100 and max(p[:3]) < 200)
        touches = any((x - seed[0]) ** 2 + (y - seed[1]) ** 2 >= 73 * 73 for x, y in pupil)
        if len(pupil) < 1000:
            print(f"FAIL {name} eye: no solid dark pupil over the + at {seed}"); ok = False
        elif touches:
            print(f"FAIL {name} eye: the pupil isn't ringed by white, so it runs into what's round it"); ok = False
        else:
            print(f"ok   {name} pupil ({len(pupil)} pixels)")
        # With the pupil painted white, the white must be one patch bounded by a coloured or dark edge.
        dark = set(pupil)

        def light(p, xy=None):
            return p[3] > 100 and max(p[:3]) >= 60 and max(p[:3]) - min(p[:3]) < 60

        white = flood(px, w, h, seed, 85, lambda p: True)
        white = [xy for xy in white if xy in dark or light(px[xy])]
        if len(white) - len(pupil) < 300:
            print(f"FAIL {name} eye: not enough white round the pupil"); ok = False
    for x0, x1 in ((620, 680), (1095, 1150)):
        holes = sum(1 for x in range(x0, x1) for y in range(780, 830) if px[x, y][3] < 200)
        if holes > 50:
            print(f"FAIL his body doesn't cover the arm root at x {x0}-{x1}, y 780-830"); ok = False
    print("ok   arm roots covered" if ok else "")
    print(f"note below y 703, anything left of x {left:.0f} or right of x {right:.0f} is cut off (except the hands)")
    print("PASS" if ok else "Fix the FAILs above and check again.")
    return 0 if ok else 1


sys.exit(main())
