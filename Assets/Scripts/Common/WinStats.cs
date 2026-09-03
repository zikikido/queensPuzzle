using Common;
using System;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Zero-parse reader for the baked winstats blob (Resources/winstats.bytes) — per-stage
    /// "better than X%" data, packed by pawdoku-winstats-server (see its winstats.ts for the
    /// byte layout; QWST v1, 25-byte records, count-prefixed hash buckets).
    ///
    /// The blob bytes ARE the data structure: loaded once, kept as byte[], every lookup is
    /// one mask + one offset read + 1-3 record compares. A stage without a record falls back
    /// to the difficulty-band GLOBAL matching its weight (hard stages compare against hard
    /// stages); a missing/corrupt blob just reports nothing (Found=false) — gameplay never
    /// breaks over stats.
    ///
    /// The deep integrity walk (<see cref="ValidateBlob"/>) is EDITOR/download-time only —
    /// the runtime Load does just the cheap header checks.
    /// </summary>
    public static class WinStats {

        public const byte Version = 1;
        const int HeaderSize = 4 + 1 + 4 + 4 + 1 + 1 + 1; // magic, version, entryCount, B, recordSize, anchorCount, bandCount
        const int BandTagSize = 2;                        // each band record is prefixed by maxWeight (uint16)

        // ---- record schema — THE single source of truth for field order and sizes ------
        // A record is: [hash][anchor x anchorCount][discrete pcts]. Every offset in this file
        // is DERIVED from these constants; nothing else may assume the layout. The header's
        // recordSize/anchorCount let the record GROW (trailing fields, more anchors) without
        // a version bump — we stride by the header's numbers and read only what we know.

        const int HashSize = 4;                                  // level hash, int32 LE
        const int AnchorPctSize = 1;                             // anchor percent, byte
        const int AnchorSecSize = 2;                             // anchor seconds, uint16 LE
        const int AnchorStride = AnchorPctSize + AnchorSecSize;

        // The discrete percents, in their exact order after the anchors (1 byte each).
        const int NoBonesField = 0;
        const int FirstTryField = 1;
        const int ReviveField = 2;
        const int KnownDiscretes = 3;

        static int AnchorsOffset => HashSize;                                    // anchors start
        static int DiscretesOffset => HashSize + _anchors * AnchorStride;        // first discrete

        static byte[] _blob;
        static int _b;              // bucket count (power of two)
        static int _recordSize;     // from the header — may exceed what this reader knows
        static int _anchors;        // from the header
        static int _bandCount;      // difficulty-band globals (fallback records)
        static int _bandsPos;       // bands region position
        static int _offsetsPos;     // offsets table position
        static int _dataPos;        // bucket-data region position
        static bool _loadTried;

        // ---- loading -----------------------------------------------------------------

        static bool Load() {
            if (_loadTried) return _blob != null;
            _loadTried = true;

            var ta = Resources.Load<TextAsset>("winstats");
            if (ta == null) return false;                  // no blob shipped — stats just hide
            byte[] bytes = ta.bytes;
            Resources.UnloadAsset(ta);

            string err = CheckHeader(bytes);
            if (err == null) {
                _blob = bytes;
                _b = ReadInt(bytes, 9);
                _recordSize = bytes[13];
                _anchors = bytes[14];
                _bandCount = bytes[15];
                _bandsPos = HeaderSize;                    // header, then the band globals
                _offsetsPos = _bandsPos + _bandCount * (BandTagSize + _recordSize);
                _dataPos = _offsetsPos + _b * 4;
            } else {
                CDebug.LogError("[WinStats] baked blob rejected: " + err);
            }
            return _blob != null;
        }

        // Cheap sanity only — the deep walk ran at download time (ValidateBlob).
        static string CheckHeader(byte[] b) {
            if (b == null || b.Length < HeaderSize) return "too short";
            if (b[0] != 'Q' || b[1] != 'W' || b[2] != 'S' || b[3] != 'T') return "bad magic";
            if (b[4] != Version) return $"version {b[4]}, reader understands {Version}";
            int entryCount = ReadInt(b, 5);
            int buckets = ReadInt(b, 9);
            int recordSize = b[13];
            int anchors = b[14];
            int bandCount = b[15];
            if (entryCount < 0) return "negative entryCount";
            if (buckets < 8 || (buckets & (buckets - 1)) != 0) return $"B={buckets} not a power of two";
            if (anchors < 1) return "no anchors";
            if (bandCount < 1) return "no difficulty bands";
            if (recordSize < HashSize + anchors * AnchorStride + KnownDiscretes)
                return $"recordSize {recordSize} too small for {anchors} anchors + {KnownDiscretes} pcts";
            long min = HeaderSize + (long)bandCount * (BandTagSize + recordSize) + (long)buckets * 4
                     + (entryCount > 0 ? 1 + (long)entryCount * recordSize : 0);
            if (b.Length < min) return "shorter than header promises";
            return null;
        }

        static int ReadInt(byte[] b, int pos) =>
            b[pos] | (b[pos + 1] << 8) | (b[pos + 2] << 16) | (b[pos + 3] << 24);

        // ---- lookup ------------------------------------------------------------------

        /// <summary>Stats for one stage. Hit → its record; miss → the difficulty-band GLOBAL
        /// matching the stage's `weight` (LevelLoader.CurrentLevelWeight), so a hard stage is
        /// compared against similarly-hard stages; no/corrupt blob → <see cref="Found"/> is
        /// false and every accessor returns -1.</summary>
        public static Stats For(int levelHash, int weight) {
            if (!Load()) return default;

            int bucket = (int)((uint)levelHash & (uint)(_b - 1));
            int off = ReadInt(_blob, _offsetsPos + bucket * 4);
            if (off >= 0) {
                int pos = _dataPos + off;
                int count = _blob[pos++];
                for (int i = 0; i < count; i++, pos += _recordSize)
                    if (ReadInt(_blob, pos) == levelHash)
                        return new Stats(_blob, pos, global: false);
            }
            return new Stats(_blob, BandRecordPos(weight), global: true);
        }

        // First band whose maxWeight covers this stage; the last band's tag is 65535,
        // so the walk always lands somewhere.
        static int BandRecordPos(int weight) {
            for (int k = 0; k < _bandCount - 1; k++) {
                int entry = _bandsPos + k * (BandTagSize + _recordSize);
                int maxWeight = _blob[entry] | (_blob[entry + 1] << 8);
                if (weight <= maxWeight) return entry + BandTagSize;
            }
            return _bandsPos + (_bandCount - 1) * (BandTagSize + _recordSize) + BandTagSize;
        }

        /// <summary>One 25-byte record, read in place. All percents are "share of players you
        /// beat": -1 = no data / not worth showing (below <see cref="MinShowPct"/>).</summary>
        public readonly struct Stats {
            readonly byte[] _b;
            readonly int _pos;

            internal Stats(byte[] blob, int pos, bool global) { _b = blob; _pos = pos; IsGlobal = global; }

            public bool Found => _b != null;

            /// <summary>True when this stage had no record of its own — values come from the
            /// difficulty-band GLOBAL matching the stage's weight.</summary>
            public bool IsGlobal { get; }

            int AnchorPos(int i) => _pos + AnchorsOffset + i * AnchorStride;
            int AnchorPct(int i) => _b[AnchorPos(i)];
            int AnchorSec(int i) => _b[AnchorPos(i) + AnchorPctSize] | (_b[AnchorPos(i) + AnchorPctSize + 1] << 8);

            int Discrete(int field) {
                if (_b == null) return -1;                 // no blob — Found=false contract
                int v = _b[_pos + DiscretesOffset + field];
                return v == 255 ? -1 : v;
            }

            /// <summary>⏱️ "Faster than X%" for a solve of `timeSec` — or -1 (no data).
            /// Continuous: linear interpolation between anchors ("Faster than 87.3%"); past the
            /// top anchor an asymptotic curve toward (never reaching) 100, capped at 99.9;
            /// past the slowest anchor a decay toward 0 — so STRICTLY slower than the anchor is
            /// strictly below its pct, and display floors like the win popup's 60% (caller
            /// policy) keep hiding it. The UI picks how many decimals to show.
            /// No difficulty math here: a stage record's distribution already IS its difficulty,
            /// and the fallback record was picked by the stage's weight band.</summary>
            public float FasterThanPct(float timeSec) {
                if (_b == null) return -1f;

                if (timeSec > AnchorSec(0))                // slower than our data reaches — decay to 0
                    return AnchorPct(0) * (AnchorSec(0) / timeSec);

                // top = last distinct anchor (small stages duplicate the 95 pair in slot 6)
                int top = _anchors - 1;
                while (top > 0 && AnchorSec(top) == AnchorSec(top - 1) && AnchorPct(top) == AnchorPct(top - 1))
                    top--;

                if (timeSec <= AnchorSec(top)) {           // beyond our data — extrapolate
                    if (timeSec <= 0f) return AnchorPct(top);
                    float ratio = AnchorSec(top) / timeSec;
                    return Mathf.Min(99.9f, 100f - (100f - AnchorPct(top)) / ratio);
                }

                for (int i = 0; i < top; i++) {
                    int slow = AnchorSec(i), fast = AnchorSec(i + 1);
                    if (timeSec <= slow && timeSec >= fast) {
                        if (slow == fast) return AnchorPct(i + 1);
                        float t = (slow - timeSec) / (slow - fast);
                        return Mathf.Lerp(AnchorPct(i), AnchorPct(i + 1), t);
                    }
                }
                return AnchorPct(0);                       // unreachable, but never throw
            }

            // Stored as whole-percent bytes, exposed as float for one uniform API
            // (if the server ever ships finer resolution, no call site changes).
            // PURE DATA — display policy (e.g. "show only above 60%") lives in the callers
            // (WinAchievement.Pick, the lose screen). -1 = no data.

            /// <summary>🦴 Share of winners who lost at least one life (what a clean run beats).</summary>
            public float NoBonesBeatsPct => Discrete(NoBonesField);

            /// <summary>🎯 Share of winners who needed more than one attempt (what a first-try beats).</summary>
            public float FirstTryBeatsPct => Discrete(FirstTryField);

            /// <summary>Share of players who used a revive on this stage.</summary>
            public float RevivePct => Discrete(ReviveField);
        }

        // ---- download-time validation (editor) ----------------------------------------

        /// <summary>
        /// FULL structural walk — every bucket, every record, every value — plus a lookup
        /// round-trip through the real reader for every stored hash. Called by the exporter
        /// on the freshly downloaded bytes BEFORE saving; never at runtime (boot does the
        /// cheap header check only). Returns null when the blob is sound.
        /// </summary>
        public static string ValidateBlob(byte[] b) {
            string err = CheckHeader(b);
            if (err != null) return err;

            int entryCount = ReadInt(b, 5);
            int buckets = ReadInt(b, 9);
            int recordSize = b[13];
            int anchors = b[14];
            int bandCount = b[15];
            int offsetsPos = HeaderSize + bandCount * (BandTagSize + recordSize);
            int dataPos = offsetsPos + buckets * 4;
            int dataSize = b.Length - dataPos;

            int prevMax = -1;
            for (int k = 0; k < bandCount; k++) {
                int entry = HeaderSize + k * (BandTagSize + recordSize);
                int maxWeight = b[entry] | (b[entry + 1] << 8);
                if (maxWeight < prevMax) return $"band {k}: maxWeight not ascending";
                if (k == bandCount - 1 && maxWeight != 65535) return "last band must cover all weights (65535)";
                prevMax = maxWeight;

                string bandErr = CheckRecord(b, entry + BandTagSize, anchors, $"band {k}");
                if (bandErr != null) return bandErr;
                if (ReadInt(b, entry + BandTagSize) != 0) return $"band {k}: record hash must be 0";
            }

            int found = 0;
            for (int i = 0; i < buckets; i++) {
                int off = ReadInt(b, offsetsPos + i * 4);
                if (off == -1) continue;
                if (off < 0 || off >= dataSize) return $"bucket {i}: offset {off} out of range";

                int pos = dataPos + off;
                int count = b[pos++];
                if (count < 1) return $"bucket {i}: empty but not marked -1";
                if (pos + count * recordSize > b.Length) return $"bucket {i}: records overflow the file";

                for (int r = 0; r < count; r++, pos += recordSize) {
                    int hash = ReadInt(b, pos);
                    if ((int)((uint)hash & (uint)(buckets - 1)) != i)
                        return $"bucket {i}: hash {hash} belongs elsewhere";
                    string recordErr = CheckRecord(b, pos, anchors, $"hash {hash}");
                    if (recordErr != null) return recordErr;
                    found++;
                }
            }
            if (found != entryCount) return $"header promises {entryCount} records, found {found}";

            return null;
        }

        static string CheckRecord(byte[] b, int pos, int anchors, string who) {
            int prevPct = -1, prevSec = int.MaxValue;
            for (int i = 0; i < anchors; i++) {
                int at = pos + AnchorsOffset + i * AnchorStride;
                int pct = b[at];
                int sec = b[at + AnchorPctSize] | (b[at + AnchorPctSize + 1] << 8);
                if (pct > 100) return $"{who}: anchor {i} pct {pct} > 100";
                if (pct < prevPct) return $"{who}: anchor pcts not ascending";
                if (sec > prevSec) return $"{who}: anchor secs not descending";
                prevPct = pct; prevSec = sec;
            }
            for (int i = 0; i < KnownDiscretes; i++) {
                int v = b[pos + HashSize + anchors * AnchorStride + i];
                if (v > 100 && v != 255) return $"{who}: discrete pct {v} invalid";
            }
            return null;
        }
    }
}
