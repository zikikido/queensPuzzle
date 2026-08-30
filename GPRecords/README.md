# GPRecords — scripted UA ad videos

Each folder is one ad, replayed in Unity by the GP Recorder window (QueensPuzzle → GP Recorder).
An ad is authored entirely as JSON — study an existing folder (e.g. `ad_3_rules_1_fail_win/`)
as the reference example.

**Learn the game first.** Read the sources — they are the truth, not this file:
- `Assets/Scripts/GP/MBGameplay.cs`, `MBCell.cs` — rules, input, win/fail
- `Assets/Scripts/GP/MBToturial.cs` — how the game itself teaches (spotlight, hand)
- `Assets/Scripts/GPRecorder/` — how records are replayed (GPRecord.cs is the schema)

## Folder

```
GPRecords/<ad_name>/
  record.json                 the timelines
  <voice>.voices.json         a voice set: lines (name, text, TTS params, mp3 path), voiceId
  Voices/<voice>/*.mp3        generated audio
```

## record.json — four timelines + settings

All times are seconds from replay start (board bloom ≈ first 1.5s). All lists time-sorted.

| Track | Key | Meaning |
|-------|-----|---------|
| `actions` | `{time, x, y, to}` | the game: to 0=unmark 1=puppy(double-tap) 2=X 3=wrong puppy. x=col, y=row |
| `handKeys` | `{time, x, y, kind}` | demo finger: kind 0=point(press/corner) 1=end(lift) 2=double-tap |
| `spotKeys` | `{time, xs[], ys[], kind}` | tutorial curtain: kind 0=show holes over the cell list 1=clear |
| `voiceKeys` | `{time, name}` | play the line `name` from the active voice set; subtitles type with it |

Settings: `level` (embedded puzzle — copy from an existing record), `voicesFile`, `endTime`
(replay runs at least to here), `failMode`, `noWin`, `hideTop/Rules/Boosters/Counters`,
`showAdText` + `adTextBg/Color/Height/Pos` (subtitles overlay),
`adImages` (image overlays: `{name, height, pos, bg}`, sprites from the AdsImagePortrait scene).

Audio generation and screen capture happen in the Unity window — the JSON is the whole job.
