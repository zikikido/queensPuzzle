"""Build the Pawdoku playable ad into a single self-contained HTML file.

Extracts art straight from the Unity project, downscales + quantises it,
inlines everything as base64, and writes playable/dist/pawdoku-playable.html.

    python playable/build.py
"""
import base64
import glob
import io
import json
import os
import re
import zipfile
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


# Frames pre-rendered from the game's Spine puppy (Unity menu:
# QueensPuzzle/Bake Playable Spine Frames). Re-run the bake whenever the rig changes.
SPINE = os.path.join(ROOT, "src", "spine_frames")


def spine_frames(anim):
    return sorted(glob.glob(os.path.join(SPINE, anim, "*.png")))


def spine_meta():
    return json.load(open(os.path.join(SPINE, "bake.json")))


def content_box(anim):
    """Union alpha bbox over one animation's baked frames."""
    box = None
    paths = spine_frames(anim)
    if not paths:
        sys.exit("no baked frames for %s - run Unity menu QueensPuzzle/Bake Playable Spine Frames" % anim)
    for p in paths:
        b = Image.open(p).convert("RGBA").getbbox()
        if b is None:
            continue
        box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]),
                                     max(box[2], b[2]), max(box[3], b[3]))
    return box


def framing(anims, center_on, pad=2):
    """Crop box covering every frame of `anims`, symmetric around the center of
    `center_on`'s content - the character lands dead-center of the window and wide
    effects (celebrate stars, flying tears) get equal room on both sides. The box
    may extend past the bake canvas; PIL pads that with transparency.

    Returns (box, character_box): the second is `center_on`'s own bbox, so callers
    can size the CSS window from the character rather than the effects padding."""
    boxes = {a: content_box(a) for a in anims}
    c = boxes[center_on]
    cx, cy = (c[0] + c[2]) / 2.0, (c[1] + c[3]) / 2.0
    hw = max(max(cx - b[0], b[2] - cx) for b in boxes.values()) + pad
    hh = max(max(cy - b[1], b[3] - cy) for b in boxes.values()) + pad
    return (round(cx - hw), round(cy - hh), round(cx + hw), round(cy + hh)), c


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


# The rests QueenSpineController puts on end poses: Idle's loopDelay between
# idle_thinking cycles, and Happy's nextDelay before celebrate hands back to Idle.
IDLE_REST = 4.0
CELEB_REST = 2.0


def _pack_strip(paths, width, box, colors):
    """Crop, resize and lay a frame list into one quantized PNG strip."""
    ims = [Image.open(p).convert("RGBA").crop(box) for p in paths]
    h = round(ims[0].height * width / ims[0].width)
    ims = [i.resize((width, h), Image.LANCZOS) for i in ims]
    strip = Image.new("RGBA", (width * len(ims), h))
    for n, im in enumerate(ims):
        strip.paste(im, (n * width, 0), im)
    buf = io.BytesIO()
    strip.quantize(colors=colors, method=Image.Quantize.FASTOCTREE).save(buf, "PNG", optimize=True)
    raw = buf.getvalue()
    return base64.b64encode(raw).decode(), len(raw), h


def _kf(name, base, count, n, cycle, fps, end):
    """@keyframes for one clip inside a combined strip: step-end stops at 1/fps
    intervals over `cycle` seconds, holding `end` at the 100% mark."""
    ss = "".join("%.3f%%{transform:translateX(-%.4f%%)}" % ((i / fps) / cycle * 100.0, (base + i) * 100.0 / n)
                 for i in range(count))
    ss += "100%%{transform:translateX(-%.4f%%)}" % (end * 100.0 / n)
    return "@keyframes %s{%s}" % (name, ss)


CELL_ANIMS = ["idle_thinking", "celebrate", "disappointed"]

# how much of the cell's width the dog itself spans (effects overflow around it)
DOG_FILL = 100.0


def encode_cell_anim(width, colors=64):
    """One strip carrying the three in-cell clips, each addressed by its own
    keyframes: idle (loop + the game's 4s rest), celeb (place a puppy / win),
    disap (a wrong puppy in fail mode). Framed symmetric around the idle dog, so
    the dog sits dead-center; PUP_WIDTH scales the CSS window so the DOG - not
    the box with its effects padding - fills DOG_FILL of the cell."""
    fps = float(spine_meta()["fps"])
    box, dog = framing(CELL_ANIMS, "idle_thinking")
    seqs = [spine_frames(a) for a in CELL_ANIMS]
    counts = [len(s) for s in seqs]
    n = sum(counts)
    b64, raw, h = _pack_strip(sum(seqs, []), width, box, colors)

    idle_cycle = counts[0] / fps + IDLE_REST
    celeb_dur = counts[1] / fps
    disap_dur = counts[2] / fps
    css = (_kf("idle", 0, counts[0], n, idle_cycle, fps, end=0) +
           _kf("celeb", counts[0], counts[1], n, celeb_dur, fps, end=counts[0] + counts[1] - 1) +
           _kf("disap", counts[0] + counts[1], counts[2], n, disap_dur, fps, end=n - 1))

    pup_w = DOG_FILL * (box[2] - box[0]) / (dog[2] - dog[0])
    return {
        "b64": b64, "raw": raw, "box": box,
        "pup_width": "%.1f%%" % pup_w,
        # both percentages resolve against the (square) cell, so width-relative is fine
        "pup_height": "%.1f%%" % (pup_w * h / width),
        "css": css, "frames": n,
        "idle_duration": "%.3fs" % idle_cycle,
        "celeb_duration": "%.3fs" % celeb_dur,
        # celebrate holds its end pose CELEB_REST before the idle loop takes over
        "celeb_total": "%.3fs" % (celeb_dur + CELEB_REST),
        "disap_duration": "%.3fs" % disap_dur,
    }


def encode_cry_anim(width, colors=64):
    """The lose card's cry: cry_in plays once, then cry_loop loops - the same
    Cry -> _ceyIdle chain QueenSpineController plays in game. One strip holds both
    clips; two keyframe sets index into their halves. CRY_WIDTH blows the CSS
    window up so the dog itself keeps the old still's on-card size even though
    the flying tears widen the box."""
    fps = float(spine_meta()["fps"])
    box, dog = framing(["cry_in", "cry_loop"], "cry_in")
    pin, ploop = spine_frames("cry_in"), spine_frames("cry_loop")
    nin, nloop = len(pin), len(ploop)
    n = nin + nloop
    b64, raw, h = _pack_strip(pin + ploop, width, box, colors)

    css = (_kf("cryin", 0, nin, n, nin / fps, fps, end=nin - 1) +
           _kf("cryloop", nin, nloop, n, nloop / fps, fps, end=nin))
    scale = (box[2] - box[0]) / float(dog[2] - dog[0])
    return {
        "b64": b64, "raw": raw,
        "aspect": "%d/%d" % (width, h),
        # the old still was width:clamp(84px,15vh,124px) - same dog size, wider box
        "width": "width:clamp(%dpx,%.1fvh,%dpx)" % (round(84 * scale), 15 * scale, round(124 * scale)),
        "css": css, "frames": n,
        "in_duration": "%.3fs" % (nin / fps),
        "loop_duration": "%.3fs" % (nloop / fps),
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


# the win card's still: celebrate's winking grin (the lose card animates instead)
STILLS = {"HAPPY": ("celebrate", 12, 96)}


# key -> (source path, target width, palette size)
def sources():
    return {
        # the real wordmark lives outside the reskin folder
        "LOGO": (os.path.join(ROOT, "..", "Assets", "Pictures", "Lobby", "Logo.png"), 248, 64),
        "ICON": (os.path.join(RESKIN, "Icons", "Icon.png"), 96, 96),   # the real store icon, beside the CTA
        "PAW": (pic("PawIcon.png"), 72, 96),
        "XMARK": (pic("X-mark.png"), 64, 16),
        "XMARK_RED": (pic("X-mark.png"), 64, 16, (1.0, 0.16, 0.14)),   # the game's $RedX
        "BONE": (pic("Bone.png"), 56, 32),
        "BONE_EMPTY": (pic("BoneEmpty.png"), 56, 32),
        # the tutorial hand lives outside the reskin folder
        "FINGER": (os.path.join(ROOT, "..", "Assets", "Pictures", "GP", "Finger.png"), 96, 48),
    }


def encode(path, width, colors, tint=None, crop=None):
    im = Image.open(path).convert("RGBA")
    if crop:
        im = im.crop(crop)
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


def static_board(block):
    """A coloured grid for the first level, baked into #board as plain HTML.

    Google's image review can screenshot the ad before its JS builds the board,
    catching a blank frame and disapproving it for 'not filling the space'. Baking
    the grid in means the very first paint is already a full, colourful board; JS
    clears these .cell nodes and rebuilds live on start, so nothing is duplicated.
    """
    first = block[block.index("{n:"):]
    n = int(re.search(r"\bn:(\d+)", first).group(1))
    regions = [int(x) for x in re.search(r"regions:\[([\d,\s]+)\]", first).group(1).split(",")]
    colors = re.findall(r'"(#[0-9A-Fa-f]{6})"', re.search(r"colors:\[([^\]]+)\]", first).group(1))
    cells = "".join(
        '<div class="cell" style="background:%s"></div>' % colors[regions[i]]
        for i in range(n * n))
    # inline grid-columns + a viewport-based width so it fills even with JS off
    return ('<div id="board" style="grid-template-columns:repeat(%d,1fr);'
            'width:min(92vw,62vh);--gap:%dpx">%s</div>') % (n, max(2, 3), cells)


def render(master, levels_js, art, out):
    html = master
    board_html = None
    if levels_js:
        # variants carry only their level data; everything else comes from the master
        block = open(levels_js, encoding="utf-8").read().strip()
        if not LEVELS_BLOCK.search(html):
            sys.exit("master template has no LEVELS block - was it hand-edited?")
        html = LEVELS_BLOCK.sub(lambda _: block, html, count=1)
        board_html = static_board(block)
    else:
        m = LEVELS_BLOCK.search(html)
        if m:
            board_html = static_board(m.group(0))
    if board_html:
        if '<div id="board"></div>' not in html:
            sys.exit('master template has no empty <div id="board"></div> to prefill')
        html = html.replace('<div id="board"></div>', board_html, 1)

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

    # 160/frame keeps the now-bigger cell dog crisp on high-DPI screens
    cell = encode_cell_anim(160)
    art["IDLE_SHEET"] = cell["b64"]
    art["IDLE_KEYFRAMES"] = cell["css"]
    art["IDLE_DURATION"] = cell["idle_duration"]
    art["PUP_WIDTH"] = cell["pup_width"]
    art["PUP_HEIGHT"] = cell["pup_height"]
    art["CELEB_DURATION"] = cell["celeb_duration"]
    art["CELEB_TOTAL"] = cell["celeb_total"]
    art["DISAP_DURATION"] = cell["disap_duration"]
    print("  %-12s %d frames (pup %s)  %6.1f KB png -> %6.1f KB base64" %
          ("cell_anim", cell["frames"], cell["pup_width"],
           cell["raw"] / 1024, len(cell["b64"]) / 1024))

    # 432/frame, not the cell strip's 104: the card window shows the dog at ~200
    # CSS px and the tears eat 40% of the frame width, so anything less pixelates
    cry = encode_cry_anim(432, colors=128)
    art["CRY_SHEET"] = cry["b64"]
    art["CRY_KEYFRAMES"] = cry["css"]
    art["CRY_ASPECT"] = cry["aspect"]
    art["CRY_WIDTH"] = cry["width"]
    art["CRY_IN_DURATION"] = cry["in_duration"]
    art["CRY_LOOP_DURATION"] = cry["loop_duration"]
    print("  %-12s %d frames  %6.1f KB png -> %6.1f KB base64" %
          ("cry_anim", cry["frames"], cry["raw"] / 1024, len(cry["b64"]) / 1024))

    for key, (anim, frame, colors) in STILLS.items():
        # 256 wide for the same reason as the cry strip: the win card shows it big
        b64, raw = encode(spine_frames(anim)[frame], 256, colors, crop=cell["box"])
        art[key] = b64
        print("  %-12s %s[%d]  %6.1f KB png -> %6.1f KB base64" %
              (key.lower(), anim, frame, raw / 1024, len(b64) / 1024))

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

    # Google App campaigns want an HTML5 playable as a ZIP whose entry point is
    # index.html; a bare .html gets treated as an image. Unity takes the .html directly.
    # So: .html in dist/ for Unity, matching .zip in dist/google/ for Google.
    zipdir = os.path.join(DISTDIR, "google")
    os.makedirs(zipdir, exist_ok=True)
    print("\n  google zips (index.html inside) -> %s\n" % zipdir)
    for _, out in jobs:
        base = os.path.splitext(os.path.basename(out))[0]
        zpath = os.path.join(zipdir, base + ".zip")
        with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
            z.write(out, "index.html")   # single entry, named index.html at the ZIP root
        print("  %-38s %6.1f KB" % (os.path.basename(zpath), os.path.getsize(zpath) / 1024))


if __name__ == "__main__":
    main()
