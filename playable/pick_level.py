"""Pick a campaign level for the playable ad and emit its constants.

The playable's hint engine implements two of SolveTracer's techniques —
QueenScope (the three base rules) and RegionSingle (a colour down to one cell).
Most levels need more than that, so a level can only be used if:

  1. it solves end-to-end on RegionSingle alone from the chosen start row, and
  2. that start row yields three small, balanced tutorial steps.

Rule 2 is the one that bites: level 3 solves fine but its row/column step is
18 cells, which is unplayable in a 20-second ad.

    python playable/pick_level.py 4            # check level 4, print constants
    python playable/pick_level.py 4 --write    # ...and patch src/template.html
    python playable/pick_level.py --scan 200   # list every usable level

Then rebuild:  python playable/build.py
"""
import argparse
import os
import re
import sys
from itertools import permutations

ROOT = os.path.dirname(os.path.abspath(__file__))
LEVELS = os.path.normpath(os.path.join(ROOT, "..", "Assets", "Levels", "Sets", "Puzzby"))
TEMPLATE = os.path.join(ROOT, "src", "template.html")
VARIANTS = os.path.join(ROOT, "src", "levels")

# Visually distinct picks from Assets/Reskin/Resources/SORegionsColors.asset.
# The shipped per-level regionColors are not used: levels routinely pair two
# near-identical shades (level 4 ships #F8D636 and #D7C159, both yellow), which
# is fine in-game but unreadable at ad scale where the note names the colour.
PALETTE = [
    ("#F8D636", "Yellow"), ("#B185CF", "Purple"), ("#7DC9D9", "Blue"),
    ("#FF808E", "Pink"),   ("#67D2AB", "Green"),  ("#F9A55F", "Orange"),
    ("#C8D0FF", "Purple"), ("#FF93D1", "Pink"),   ("#A9D056", "Green"),
    ("#E4B2A1", "Beige"),
]

# tutorial steps outside this range make for a bad ad: too few reads as noise,
# too many is a tapping chore
MIN_STEP, MAX_STEP = 2, 5


def hexarr(s):
    return [int.from_bytes(bytes.fromhex(s[i:i + 8]), "little") for i in range(0, len(s), 8)]


def load(level):
    path = os.path.join(LEVELS, "%d.asset" % level)
    if not os.path.exists(path):
        sys.exit("no such level: %s" % path)
    text = open(path, encoding="utf-8", errors="ignore").read()
    n = int(re.search(r"\bsize:\s*(\d+)", text).group(1))
    grab = lambda k: (re.search(k + r":\s*([0-9a-fA-F]*)", text).group(1) or "")
    regions, solution = hexarr(grab("regions")), hexarr(grab("solutionColumns"))
    if len(regions) != n * n or len(solution) != n:
        sys.exit("level %d: malformed data" % level)
    return n, regions, solution


def rules_for(n, regions, queens):
    at = lambda r, c: regions[r * n + c]
    return {
        "touch":  lambda r, c: any(abs(qr - r) <= 1 and abs(qc - c) <= 1 for qr, qc in queens),
        "color":  lambda r, c: any(at(qr, qc) == at(r, c) for qr, qc in queens),
        "rowcol": lambda r, c: any(qr == r or qc == c for qr, qc in queens),
    }


def solves(n, regions, solution, start):
    """Does RegionSingle alone carry this to a full board?"""
    at = lambda r, c: regions[r * n + c]
    queens = [(start, solution[start])]
    while len(queens) < n:
        attacked = lambda r, c: any(
            qr == r or qc == c or at(qr, qc) == at(r, c) or (abs(qr - r) <= 1 and abs(qc - c) <= 1)
            for qr, qc in queens)
        free = [(r, c) for r in range(n) for c in range(n)
                if (r, c) not in queens and not attacked(r, c)]
        by_region = {}
        for cell in free:
            by_region.setdefault(at(*cell), []).append(cell)
        forced = next((v[0] for v in by_region.values() if len(v) == 1), None)
        if forced is None:
            return False, len(queens)
        queens.append(forced)
    return True, n


def tutorial(n, regions, solution, start):
    """First rule order whose three steps are all small and balanced."""
    queens = [(start, solution[start])]
    rules = rules_for(n, regions, queens)
    for order in permutations(["touch", "color", "rowcol"]):
        marked, sizes = set(), []
        for name in order:
            cells = [(r, c) for r in range(n) for c in range(n)
                     if (r, c) not in queens and (r, c) not in marked and rules[name](r, c)]
            marked.update(cells)
            sizes.append(len(cells))
        if MIN_STEP <= min(sizes) and max(sizes) <= MAX_STEP:
            return list(order), sizes
    return None, None


def evaluate(level):
    """Every usable start row for a level, best (smallest total tutorial) first."""
    n, regions, solution = load(level)
    out = []
    for start in range(n):
        ok, got = solves(n, regions, solution, start)
        if not ok:
            continue
        order, sizes = tutorial(n, regions, solution, start)
        if order:
            out.append({"n": n, "regions": regions, "solution": solution,
                        "start": start, "order": order, "sizes": sizes})
    out.sort(key=lambda c: sum(c["sizes"]))
    return n, regions, solution, out


def one_level(c):
    n, regions = c["n"], c["regions"]
    if n > len(PALETTE):
        sys.exit("level is %dx%d but the palette only has %d distinct colours" % (n, n, len(PALETTE)))
    used = sorted(set(regions))
    if used != list(range(len(used))):
        sys.exit("region ids are not contiguous: %s" % used)
    rows = ", ".join(",".join(str(regions[r * n + col]) for col in range(n)) for r in range(n))
    return "{n:%d,regions:[%s],colors:[%s],names:[%s],solution:[%s],start:%d,order:[%s]}" % (
        n, rows,
        ",".join('"%s"' % PALETTE[i][0] for i in range(len(used))),
        ",".join('"%s"' % PALETTE[i][1] for i in range(len(used))),
        ",".join(str(x) for x in c["solution"]),
        c["start"],
        ",".join('"%s"' % r for r in c["order"]))


def levels_block(chosen):
    return ("/* ==== LEVELS (generated by pick_level.py - do not hand-edit) ==== */\n"
            "var LEVELS=[\n%s\n];\n"
            "/* ==== END LEVELS ==== */") % ",\n".join(one_level(c) for c in chosen)


def write(block, levels):
    """Save just the level data. build.py injects it into the master at build time,
    so edits to template.html reach every variant without regenerating them."""
    os.makedirs(VARIANTS, exist_ok=True)
    path = os.path.join(VARIANTS, "%s.js" % "-".join(str(n) for n in levels))
    open(path, "w", encoding="utf-8").write(block + "\n")
    print("\n  wrote %s" % os.path.relpath(path, os.path.dirname(ROOT)))
    print("  now run: python playable/build.py")


def scan(upto):
    hits = 0
    for level in range(1, upto + 1):
        if not os.path.exists(os.path.join(LEVELS, "%d.asset" % level)):
            continue
        n, _, _, usable = evaluate(level)
        for c in usable:
            hits += 1
            print("  level %-4d %dx%d  startRow %d  order %-22s steps %s" %
                  (level, n, n, c["start"], ">".join(c["order"]), c["sizes"]))
    print("\n  %d usable configuration(s) in levels 1-%d" % (hits, upto))


def report_unusable(level, n, regions, solution):
    print("  level %d NOT USABLE. Reasons per start row:" % level)
    for start in range(n):
        ok, got = solves(n, regions, solution, start)
        if not ok:
            print("    row %d: stalls at %d/%d puppies - needs techniques the playable lacks" % (start, got, n))
        else:
            print("    row %d: solves, but no rule order gives balanced steps "
                  "(every step must be %d-%d cells)" % (start, MIN_STEP, MAX_STEP))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("levels", nargs="*", type=int,
                    help="campaign level numbers, in the order the ad should play them")
    ap.add_argument("--write", action="store_true",
                    help="write src/levels/<a>-<b>-<c>.html (master template untouched)")
    ap.add_argument("--scan", type=int, metavar="UPTO", help="list every usable level up to UPTO")
    args = ap.parse_args()

    if args.scan:
        return scan(args.scan)
    if not args.levels:
        return ap.print_help()

    chosen, failed = [], False
    for level in args.levels:
        n, regions, solution, usable = evaluate(level)
        print("level %d  (%dx%d)" % (level, n, n))
        if not usable:
            report_unusable(level, n, regions, solution)
            failed = True
            continue
        for i, c in enumerate(usable):
            print("  %s startRow %d  order %-22s steps %s  (%d taps)" %
                  ("->" if i == 0 else "  ", c["start"], ">".join(c["order"]), c["sizes"], sum(c["sizes"])))
        chosen.append(usable[0])
        print()

    if failed:
        print("  Nothing written. Run --scan 200 to find levels that work.")
        sys.exit(1)

    print("ad will play %d level(s): %s" % (len(chosen), " -> ".join(str(n) for n in args.levels)))
    print("  tutorial on level %d only; final win goes to the store\n" % args.levels[0])
    block = levels_block(chosen)
    if args.write:
        write(block, args.levels)
    else:
        print(block)
        print("\n  (dry run - pass --write to create the variant)")


if __name__ == "__main__":
    main()
