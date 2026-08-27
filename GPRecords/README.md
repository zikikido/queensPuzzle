# GPRecords — scripted UA ad videos

Each folder here is one ad video for the puppy queens-puzzle game, replayed inside Unity by
the GP Recorder window (QueensPuzzle → GP Recorder). An AI can author a complete ad by
writing the JSON files only — no Unity access needed.

## Folder layout

```
GPRecords/<ad_name>/
  record.json      the timeline: level + all keys + session flags
  voices1.json     voice set (texts + ElevenLabs params); voices2.json = alternative voice
  Voices/1/*.mp3   generated audio (made from the window, not by hand)
```

## The game in one paragraph

An N×N board of colored regions. Rules: one puppy (queen) per color region, one per
row/column, and puppies can't touch (8 neighbors). Tap toggles an X mark; double-tap places
a puppy — right ones celebrate, wrong ones flash red and shake. Solving all N wins.

## record.json

```jsonc
{
  "level": {                    // the embedded puzzle (row-major arrays)
    "size": 6,
    "weight": 8,
    "regions": [ ... size*size region ids ... ],
    "solutionColumns": [ ... per row, the column of its puppy ... ],
    "revealedRows": [0],        // rows whose puppy starts on the board
    "regionColors": [ ... palette index per region id ... ]
  },

  "actions": [                  // BOARD track — what happens in the game
    { "time": 6.0, "x": 1, "y": 5, "to": 1 }
    // to: 0=UNMARK(clear cell) 1=PUPPY(double-tap; right/wrong derived from the solution)
    //     2=X mark             3=WRONG puppy (same as 1 on a non-solution cell)
    // x = column, y = row.
  ],

  "handKeys": [                 // HAND track — the demo finger (visual only)
    { "time": 5.58, "x": 1, "y": 5, "kind": 2 }
    // kind: 0=POINT  first POINT = finger appears & presses; next POINTs = drag corners
    //       1=END    finger lifts and hand vanishes (closes a POINT... drag)
    //       2=DOUBLE_CLICK  tap-tap at the cell
  ],

  "spotKeys": [                 // SPOTLIGHT track — tutorial-style dark curtain with holes
    { "time": 4.5, "x": 1, "y": 5, "xs": [1], "ys": [5], "kind": 0 },
    { "time": 7.5, "x": 0, "y": 0, "xs": [], "ys": [], "kind": 1 }
    // kind 0=SHOW (holes over the xs/ys cell list, accumulate) 1=CLEAR (curtain drops)
  ],

  "voiceKeys": [                // VOICE track — plays a line from the active voice set
    { "time": 4.5, "name": "color" }
  ],
  "voicesFile": "voices1.json", // which voice set this record uses

  "endTime": 50.0,              // replay runs at least to here (hold the ending)

  // session flags
  "noFail": true,  "noWin": true,          // noWin: win sound but no popups (turn off for final capture)
  "hideTop": false, "hideRules": false, "hideBoosters": false, "hideCounters": false,

  // subtitles overlay (types each voice line letter-by-letter with its audio)
  "showAdText": true,
  "adTextBg":    { "r": 1, "g": 1, "b": 1, "a": 1 },
  "adTextColor": { "r": 0.18, "g": 0.494, "b": 0.486, "a": 1 },
  "adTextHeight": 0.12,         // fraction of screen height
  "adTextPos": 0.08             // 0 = bottom … 1 = top
}
```

All key lists must be sorted by time. Times are seconds from replay start; the board's
bloom-in takes ~1.5s, so nothing should happen before ~2s.

## voicesN.json

```jsonc
{
  "voiceId": "FGY2WhTYpPnrIDTdsKH5",   // ElevenLabs voice for this set
  "lines": [
    { "name": "color", "text": "Every color needs one puppy.",
      "speed": 1.0, "stability": 0.5, "style": 0.25, "path": "" }
    // path is filled by the window when the audio is generated
  ]
}
```

Voice keys reference lines by `name`. Same line names across voices1/voices2 = swappable
voice sets (A/B testing) without touching the timeline.

## Authoring rules (what makes a GOOD ad)

1. **45–59 seconds total.** Structure: quiet intro → teach the 3 rules → a wrong move →
   fast logical solving → tension pause → final puppy → win (endTime ~4s after it).
2. **Every mark must be justified** — teach on puppies already on the board:
   - "one per color": spotlight a single-cell region → puppy there (it's forced).
   - "row/column": X-sweep an existing puppy's row and column.
   - "can't touch": X-ring around an existing puppy.
   - Late game: place a puppy only when its row has exactly one free cell left.
3. **Beat shape**: voice key → spotlight SHOW → hand action → board keys → spotlight CLEAR.
   Leave ~2s of calm before each voice line's action answers it.
4. **Hand sync offsets** (so the press lands exactly on the mark):
   - DOUBLE_CLICK hand key = puppy action time − 0.42
   - first POINT of a drag = first X time − 0.12; corners/END exactly on their X times.
   - The hand is optional — use it on the taught actions, skip it in fast sections.
5. **Wrong-move beat**: puppy action `to:3` on a non-solution cell (ideally touching an
   existing puppy), the "oops" voice ~0.3s AFTER it, then `to:0` on that cell ~2s later.
6. **X marks only on non-solution cells** (check solutionColumns). A puppy action on a cell
   that currently holds an X is fine.
7. The final puppy triggers the real win; keep ~4s via endTime for the popup/confetti.

## Level data

Reuse the level block from an existing record, or ask for a level export — regions,
solutionColumns and regionColors must describe a real solvable level or the replay breaks.

## What the AI can NOT do from here

Generating the audio files and capturing the video happen in the Unity window (♪ Generate
All, ▶ Play + screen capture). The AI's job ends at correct, well-timed JSON.
