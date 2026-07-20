"""Build the Pawdoku playable ad into a single self-contained HTML file.

Extracts art straight from the Unity project, downscales + quantises it,
inlines everything as base64, and writes playable/dist/pawdoku-playable.html.

    python playable/build.py
"""
import base64
import glob
import io
import os
import re
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))
RESKIN = os.path.normpath(os.path.join(ROOT, "..", "Assets", "Reskin"))
TEMPLATE = os.path.join(ROOT, "src", "template.html")
DIST = os.path.join(ROOT, "dist", "pawdoku-playable.html")

# Unity Ads rejects playables over 5 MB.
LIMIT = 5 * 1024 * 1024


def frames(state):
    return sorted(glob.glob(os.path.join(RESKIN, "Animations", "Queens", state, "Images", "*.png")))


def pic(name):
    return os.path.join(RESKIN, "Pictures", "GP", name)


# key -> (source path, target width, palette size)
def sources():
    idle = frames("Idle")
    happy = frames("Happy")
    if not idle or not happy:
        sys.exit("no puppy frames found under %s" % RESKIN)
    return {
        "PUPPY": (idle[5], 104, 96),
        "HAPPY": (happy[4], 104, 96),
        "LOGO": (pic("DogFace.png"), 160, 48),
        "PAW": (pic("PawIcon.png"), 72, 96),
        "XMARK": (pic("X-mark.png"), 64, 16),
        # the tutorial hand lives outside the reskin folder
        "FINGER": (os.path.join(ROOT, "..", "Assets", "Pictures", "GP", "Finger.png"), 96, 48),
    }


def encode(path, width, colors):
    im = Image.open(path).convert("RGBA")
    im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    # FASTOCTREE is the only PIL quantiser that keeps the alpha channel.
    q = im.quantize(colors=colors, method=Image.Quantize.FASTOCTREE)
    buf = io.BytesIO()
    q.save(buf, "PNG", optimize=True)
    return base64.b64encode(buf.getvalue()).decode(), len(buf.getvalue())


def main():
    with open(TEMPLATE, encoding="utf-8") as f:
        html = f.read()

    for key, (path, width, colors) in sources().items():
        if not os.path.exists(path):
            sys.exit("missing art: %s" % path)
        b64, raw = encode(path, width, colors)
        token = "__%s__" % key
        if token not in html:
            sys.exit("template has no %s placeholder" % token)
        html = html.replace(token, b64)
        print("  %-6s %6.1f KB png -> %6.1f KB base64" % (key.lower(), raw / 1024, len(b64) / 1024))

    left = re.findall(r"__[A-Z]+__", html)
    if left:
        sys.exit("unfilled placeholders: %s" % ", ".join(sorted(set(left))))
    for bad in ("http://", "https://cdn", "src=\"//"):
        if bad in html.replace("https://play.google.com", ""):
            sys.exit("external reference found (%s) — playable must be self-contained" % bad)

    os.makedirs(os.path.dirname(DIST), exist_ok=True)
    with open(DIST, "w", encoding="utf-8") as f:
        f.write(html)

    size = os.path.getsize(DIST)
    print("\n  %s" % DIST)
    print("  %.1f KB  (%.1f%% of the 5 MB Unity Ads limit)" % (size / 1024, size / LIMIT * 100))
    if size > LIMIT:
        sys.exit("over the 5 MB limit")


if __name__ == "__main__":
    main()
