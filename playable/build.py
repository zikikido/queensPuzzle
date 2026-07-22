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


from PIL import Image, ImageSequence

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


def encode_gif_anim(state, width, colors=64):
    """Rebuild a GIF's animation exactly: dedupe to unique frames, lay them in one
    strip, and emit CSS keyframes carrying the GIF's own per-frame durations.

    The timing matters. Idle is 15 steps over 2.77s but two of them are 1s holds
    with 30ms flicks between - a flat steps() loop turns a puppy that sits still
    and blinks into one that morphs continuously.
    """
    path = os.path.join(RESKIN, "Animations", "Queens", state, "%s.gif" % state)
    if not os.path.exists(path):
        sys.exit("missing animation gif: %s" % path)

    gif = Image.open(path)
    uniq, seq, durs = [], [], []
    for frame in ImageSequence.Iterator(gif):
        rgba = frame.convert("RGBA")
        key = rgba.tobytes()
        idx = next((i for i, u in enumerate(uniq) if u[0] == key), None)
        if idx is None:
            uniq.append((key, rgba.copy()))
            idx = len(uniq) - 1
        seq.append(idx)
        durs.append(frame.info.get("duration", 100))

    ims = [im for _, im in uniq]
    h = round(ims[0].height * width / ims[0].width)
    ims = [i.resize((width, h), Image.LANCZOS) for i in ims]
    strip = Image.new("RGBA", (width * len(ims), h))
    for n, im in enumerate(ims):
        strip.paste(im, (n * width, 0), im)
    buf = io.BytesIO()
    strip.quantize(colors=colors, method=Image.Quantize.FASTOCTREE).save(buf, "PNG", optimize=True)
    raw = buf.getvalue()

    # step-end holds each value until the next stop, so one stop per sequence entry
    total = sum(durs)
    stops, t = [], 0
    for frame_idx, d in zip(seq, durs):
        pct = t / total * 100.0
        stops.append("%.3f%%{transform:translateX(-%.4f%%)}" % (pct, frame_idx * 100.0 / len(ims)))
        t += d
    stops.append("100%%{transform:translateX(-%.4f%%)}" % (seq[0] * 100.0 / len(ims)))
    css = "@keyframes %s{%s}" % (state.lower(), "".join(stops))

    return {
        "b64": base64.b64encode(raw).decode(), "raw": len(raw),
        "css": css, "aspect": "%d/%d" % (width, h),
        "duration": "%.3fs" % (total / 1000.0),
        "unique": len(ims), "steps": len(seq),
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
        "HAPPY": (happy[4], 104, 96),   # still frame for the win card
        "CRY": (frames("Cry")[4], 104, 96),   # ...and for the lose card
        # the real wordmark lives outside the reskin folder
        "LOGO": (os.path.join(ROOT, "..", "Assets", "Pictures", "Lobby", "Logo.png"), 248, 64),
        "PAW": (pic("PawIcon.png"), 72, 96),
        "XMARK": (pic("X-mark.png"), 64, 16),
        "XMARK_RED": (pic("X-mark.png"), 64, 16, (1.0, 0.16, 0.14)),   # the game's $RedX
        "BONE": (pic("Bone.png"), 56, 32),
        "BONE_EMPTY": (pic("BoneEmpty.png"), 56, 32),
        # the tutorial hand lives outside the reskin folder
        "FINGER": (os.path.join(ROOT, "..", "Assets", "Pictures", "GP", "Finger.png"), 96, 48),
    }


def encode(path, width, colors, tint=None):
    im = Image.open(path).convert("RGBA")
    if tint:
        # the game tints $RedX by multiplying the white X sprite; do the same here
        # rather than fight CSS filters, which can't multiply
        px = im.load()
        for y in range(im.height):
            for x in range(im.width):
                r, g, b, a = px[x, y]
                px[x, y] = (int(r * tint[0]), int(g * tint[1]), int(b * tint[2]), a)
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
    for key, spec in sources().items():
        path, width, colors = spec[0], spec[1], spec[2]
        tint = spec[3] if len(spec) > 3 else None
        if not os.path.exists(path):
            sys.exit("missing art: %s" % path)
        b64, raw = encode(path, width, colors, tint)
        art[key] = b64
        print("  %-12s %6.1f KB png -> %6.1f KB base64" % (key.lower(), raw / 1024, len(b64) / 1024))

    idle = encode_gif_anim("Idle", 104)
    art["IDLE_SHEET"] = idle["b64"]
    art["IDLE_KEYFRAMES"] = idle["css"]
    art["IDLE_ASPECT"] = idle["aspect"]
    art["IDLE_DURATION"] = idle["duration"]
    print("  %-12s %d frames / %d steps over %s  %6.1f KB png -> %6.1f KB base64" %
          ("idle_anim", idle["unique"], idle["steps"], idle["duration"],
           idle["raw"] / 1024, len(idle["b64"]) / 1024))

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
