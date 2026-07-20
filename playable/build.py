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
import subprocess
import sys
import tempfile


from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))
RESKIN = os.path.normpath(os.path.join(ROOT, "..", "Assets", "Reskin"))
TEMPLATE = os.path.join(ROOT, "src", "template.html")
VARIANTS = os.path.join(ROOT, "src", "levels")
DISTDIR = os.path.join(ROOT, "dist")

# Unity Ads rejects playables over 5 MB.
LIMIT = 5 * 1024 * 1024


def frames(state):
    return sorted(glob.glob(os.path.join(RESKIN, "Animations", "Queens", state, "Images", "*.png")))


def pic(name):
    return os.path.join(RESKIN, "Pictures", "GP", name)


def snd(name):
    return os.path.join(RESKIN, "Audio", name)


# key -> (source wav, mono mp3 bitrate). No background music on purpose: audio can't
# start until the first touch, and a large share of ad impressions run muted.
def audio_sources():
    return {
        "SFX_TAP":      (snd("GP/pop_2.wav"), 48),
        "SFX_PUPPY":    (snd("GP/Happy.wav"), 48),
        "SFX_WRONG":    (snd("GP/15902 dog tiny whimper 1.wav"), 56),
        "SFX_LEVELWIN": (snd("GP/Success Notification .wav"), 64),
        "SFX_WIN":      (snd("GP/Game Win.wav"), 64),
    }


def encode_audio(path, bitrate):
    """Downmix to 22kHz mono MP3 - inaudible quality loss on short SFX, ~20x smaller."""
    out = os.path.join(tempfile.gettempdir(), "pawdoku_sfx_%d.mp3" % abs(hash(path)))
    try:
        subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", path,
                        "-ac", "1", "-ar", "22050", "-b:a", "%dk" % bitrate, out],
                       check=True)
    except FileNotFoundError:
        sys.exit("ffmpeg not found - needed to compress the SFX")
    except subprocess.CalledProcessError:
        sys.exit("ffmpeg failed on %s" % path)
    raw = open(out, "rb").read()
    os.remove(out)
    return base64.b64encode(raw).decode(), len(raw)


# key -> (source path, target width, palette size)
def sources():
    idle = frames("Idle")
    happy = frames("Happy")
    if not idle or not happy:
        sys.exit("no puppy frames found under %s" % RESKIN)
    return {
        "PUPPY": (idle[5], 104, 96),
        "HAPPY": (happy[4], 104, 96),
        # the real wordmark lives outside the reskin folder
        "LOGO": (os.path.join(ROOT, "..", "Assets", "Pictures", "Lobby", "Logo.png"), 248, 64),
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


LEVELS_BLOCK = re.compile(r"/\* ==== LEVELS.*?==== END LEVELS ==== \*/", re.S)


def render(master, levels_js, art, out):
    html = master
    if levels_js:
        # variants carry only their level data; everything else comes from the master
        block = open(levels_js, encoding="utf-8").read().strip()
        if not LEVELS_BLOCK.search(html):
            sys.exit("master template has no LEVELS block - was it hand-edited?")
        html = LEVELS_BLOCK.sub(lambda _: block, html, count=1)

    for key, b64 in art.items():
        token = "__%s__" % key
        if token not in html:
            sys.exit("master template has no %s placeholder" % token)
        html = html.replace(token, b64)

    left = re.findall(r"__[A-Z_]+__", html)
    if left:
        sys.exit("unfilled placeholders: %s" % ", ".join(sorted(set(left))))
    for bad in ("http://", "https://cdn", "src=\"//"):
        if bad in html.replace("https://play.google.com", ""):
            sys.exit("external reference (%s) - playable must be self-contained" % bad)

    count = html.count("{n:")   # one entry per level in the LEVELS array
    open(out, "w", encoding="utf-8").write(html)
    size = os.path.getsize(out)
    print("  %-34s %2d level(s)  %6.1f KB  (%.1f%% of 5 MB)" %
          (os.path.basename(out), count, size / 1024, size / LIMIT * 100))
    return size


def main():
    art = {}
    for key, (path, width, colors) in sources().items():
        if not os.path.exists(path):
            sys.exit("missing art: %s" % path)
        b64, raw = encode(path, width, colors)
        art[key] = b64
        print("  %-12s %6.1f KB png -> %6.1f KB base64" % (key.lower(), raw / 1024, len(b64) / 1024))

    for key, (path, bitrate) in audio_sources().items():
        if not os.path.exists(path):
            sys.exit("missing audio: %s" % path)
        b64, raw = encode_audio(path, bitrate)
        art[key] = b64
        print("  %-12s %6.1f KB mp3 -> %6.1f KB base64" % (key.lower(), raw / 1024, len(b64) / 1024))

    master = open(TEMPLATE, encoding="utf-8").read()

    # every variant in src/levels/, or the master's own default levels if there are none
    variants = sorted(glob.glob(os.path.join(VARIANTS, "*.js")))
    jobs = ([(v, os.path.join(DISTDIR, "pawdoku-%s.html" % os.path.splitext(os.path.basename(v))[0]))
             for v in variants]
            or [(None, os.path.join(DISTDIR, "pawdoku-playable.html"))])

    os.makedirs(DISTDIR, exist_ok=True)
    print("\n  building %d file(s) into %s\n" % (len(jobs), DISTDIR))
    over = [out for lv, out in jobs if render(master, lv, art, out) > LIMIT]
    if over:
        sys.exit("\nover the 5 MB limit: %s" % ", ".join(os.path.basename(o) for o in over))


if __name__ == "__main__":
    main()
