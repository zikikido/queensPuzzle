using System;
using System.Security.Cryptography;
using System.Text;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Zero-parse reader for the baked rivals blob (Resources/rivals.bytes) — recorded 48-hour
    /// play histories of real players, packed by pawdoku-winstats-server (see its
    /// tournament/pack.ts for the byte layout; QRIV v1).
    ///
    /// Like <see cref="WinStats"/>, the bytes ARE the data structure. The index is sorted by the
    /// score each recording ends on, so picking an opponent is a binary search over ~30 KB and
    /// then one seek — the 245 KB of wins is never touched for the thousands of recordings that
    /// were not chosen.
    ///
    /// Every stride comes from the HEADER, not from a constant here, so the server can grow a row
    /// (skins are coming) without a version bump: this reader strides by the header's numbers and
    /// reads only the fields it knows.
    ///
    /// A missing or corrupt blob is not an error worth breaking anything over — the tournament
    /// simply reports no rivals. The deep walk (<see cref="Validate"/>) is editor/download-time.
    /// </summary>
    public static class RivalsBlob {

        public const byte Version = 1;

        /// <summary>The copy shipped in the build — the floor, used until a download replaces it.</summary>
        const string BakedResource = "rivals";

        // Header: magic(4) version(1) headerSize(1) indexEntrySize(1) playSize(1) recordCount(4),
        // and after those the fields that arrived later — each read only if headerSize reaches it.
        const int MinHeaderSize = 4 + 1 + 1 + 1 + 1 + 4;
        const int MedianScoreAt = MinHeaderSize;          // uint16
        const int MedianScoreHeaderSize = MedianScoreAt + 2;

        // ---- index entry schema — the single source of truth for field order --------------
        // Offsets are derived from these; nothing else may assume the layout.
        const int TotalSize = 2;    // uint16, the score this recording ends on
        const int OwnerSize = 4;    // uint32, truncated hash of the player it came from
        const int OffsetSize = 4;   // uint32, from the start of the play region
        const int KnownEntrySize = TotalSize + OwnerSize + OffsetSize;

        const int TotalAtEntry = 0;
        const int OwnerAtEntry = TotalSize;
        const int OffsetAtEntry = TotalSize + OwnerSize;

        // A play is packed into a uint16: the minute in the high bits, points - 1 in the low two.
        const int PointsBits = 2;
        const int PointsMask = (1 << PointsBits) - 1;
        public const int MaxMinute = 2879;   // 48 hours; a win cannot sit outside its window

        /// <summary>A recording with no owner — from a build before the app sent an id.</summary>
        public const uint NoOwner = 0;

        static byte[] _blob;
        static int _count;
        static int _medianScore;
        static int _entrySize;     // from the header; may exceed KnownEntrySize
        static int _playSize;      // from the header
        static int _indexPos;
        static int _playsPos;
        static bool _loadTried;

        /// <summary>How many recordings are available; 0 when nothing is loaded.</summary>
        public static int Count => Load() ? _count : 0;

        /// <summary>
        /// What a typical player scores in 48 hours, measured over everyone — or 0 when the blob
        /// is missing or predates the field.
        ///
        /// It has to be told to us. The recordings are drawn to a fixed quota per score bucket, so
        /// their own middle is a property of the quota and not of the players; only the server has
        /// ever seen the real distribution. The game needs it to size a first tournament, before
        /// this player has finished one of their own.
        /// </summary>
        public static int MedianScore => Load() ? _medianScore : 0;

        // ---- loading ---------------------------------------------------------------------

        /// <summary>
        /// The downloaded copy, and the only file anyone writes. <see cref="RivalsDownload"/>
        /// replaces it; nothing else touches it, and nothing hands this class bytes.
        ///
        /// MAIN THREAD ONLY — <see cref="Application.persistentDataPath"/> is a Unity API. Callers
        /// off the player loop read it once at boot and keep it.
        /// </summary>
        public static string FilePath => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        const string FileName = "rivals.bytes";

        /// <summary>
        /// Load the file, if it is not already loaded.
        ///
        /// ON DEMAND, and released again by <see cref="Unload"/> the moment the caller is done:
        /// this is ~280 KB that a tournament needs for the few milliseconds it takes to pick
        /// nineteen opponents, once every 48 hours. Keeping it resident for a whole session would
        /// be paying for all of it to save none of it. There is no work to do at startup at all.
        ///
        /// TWO sources in ONE order, decided here and nowhere else: the downloaded file if there
        /// is one, the copy baked into the build otherwise. Bytes used to arrive from three
        /// directions and whichever landed last won, which is not something anyone can reason
        /// about later. <see cref="RivalsDownload"/> only ever replaces the file; it hands this
        /// class nothing.
        ///
        /// MAIN THREAD, because <see cref="Resources.Load"/> is. That holds today: the only
        /// caller is a group build, which runs from the sync runner — a MonoBehaviour — so the
        /// await before it returns to the player loop. Moving the sync off the main thread would
        /// have to deal with this.
        /// </summary>
        static bool Load() {
            if (_loadTried) return _blob != null;
            _loadTried = true;

            return _adopt(_fromFile()) || _adopt(_fromResources());
        }

        static byte[] _fromResources() {
            var baked = Resources.Load<TextAsset>(BakedResource);
            if (baked == null) return null;          // nothing baked either — no rivals, no crash
            byte[] bytes = baked.bytes;
            Resources.UnloadAsset(baked);
            return bytes;
        }

        /// <summary>
        /// Let the bytes go. Safe to call at any time — the next read loads them again, and a
        /// tournament already underway holds its own copy of the nineteen recordings it chose.
        /// </summary>
        public static void Unload() {
            _blob = null;
            _loadTried = false;
            _count = 0;
        }

        static byte[] _fromFile() {
            try {
                string path = FilePath;
                return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
            } catch (Exception e) {
                Debug.LogWarning("[RivalsBlob] downloaded blob unreadable: " + e.Message);
                return null;
            }
        }

        static bool _adopt(byte[] bytes) {
            if (bytes == null) return false;
            string err = CheckHeader(bytes);
            if (err != null) {
                CDebug.LogError("[RivalsBlob] blob rejected: " + err);
                return false;
            }
            _blob = bytes;
            _entrySize = bytes[6];
            _playSize = bytes[7];
            _count = ReadInt(bytes, 8);
            _indexPos = bytes[5];                    // headerSize

            // Only if the header is long enough to hold it — a blob from before this field just
            // reports 0, and the caller falls back to its own figure.
            _medianScore = _indexPos >= MedianScoreHeaderSize ? ReadUShort(bytes, MedianScoreAt) : 0;

            _playsPos = _indexPos + _count * _entrySize;
            _loadTried = true;
            return true;
        }

        // Cheap sanity only — the deep walk ran at download time (Validate).
        static string CheckHeader(byte[] b) {
            if (b == null || b.Length < MinHeaderSize) return "too short";
            if (b[0] != 'Q' || b[1] != 'R' || b[2] != 'I' || b[3] != 'V') return "bad magic";
            if (b[4] != Version) return $"version {b[4]}, reader understands {Version}";

            int headerSize = b[5], entrySize = b[6], playSize = b[7];
            if (headerSize < MinHeaderSize) return $"headerSize {headerSize} is below the {MinHeaderSize} this reader needs";
            if (entrySize < KnownEntrySize) return $"indexEntrySize {entrySize} is below the {KnownEntrySize} this reader needs";
            if (playSize < 2) return $"playSize {playSize} is too small";
            if (b.Length < headerSize + 4) return "truncated header";

            int count = ReadInt(b, 8);
            if (count <= 0) return $"recordCount {count}";
            long indexEnd = (long)headerSize + (long)count * entrySize;
            if (indexEnd > b.Length) return $"index of {count} entries runs past the end";
            return null;
        }

        // ---- the index -------------------------------------------------------------------

        static int EntryPos(int i) => _indexPos + i * _entrySize;

        /// <summary>The score recording <paramref name="i"/> ends its 48 hours on.</summary>
        public static int TotalAt(int i) => ReadUShort(_blob, EntryPos(i) + TotalAtEntry);

        /// <summary>Which player it came from, hashed — see <see cref="OwnerOf"/>.</summary>
        public static uint OwnerAt(int i) => (uint)ReadInt(_blob, EntryPos(i) + OwnerAtEntry);

        /// <summary>
        /// The first recording scoring at least <paramref name="total"/>, or <see cref="Count"/>
        /// when none does. The index is sorted, so a span of candidates for "ends near 48" is
        /// this and a scan forward.
        /// </summary>
        public static int LowerBound(int total) {
            if (!Load()) return 0;
            int lo = 0, hi = _count;
            while (lo < hi) {
                int mid = (lo + hi) >> 1;
                if (TotalAt(mid) < total) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        // ---- the wins --------------------------------------------------------------------

        /// <summary>
        /// Recording <paramref name="i"/>'s wins, still packed — one ushort each, in time order.
        /// Read once when a rival is chosen and kept by the caller, so the blob can be replaced
        /// underneath a running tournament without its opponents changing.
        /// </summary>
        public static ushort[] PlaysAt(int i) {
            if (!Load()) return Array.Empty<ushort>();
            int at = _playsPos + ReadInt(_blob, EntryPos(i) + OffsetAtEntry);
            int n = ReadUShort(_blob, at);
            at += 2;

            var plays = new ushort[n];
            for (int k = 0; k < n; k++, at += _playSize) plays[k] = ReadUShort(_blob, at);
            return plays;
        }

        /// <summary>Minutes from the start of the window, 0..<see cref="MaxMinute"/>.</summary>
        public static int MinuteOf(ushort play) => play >> PointsBits;

        /// <summary>Bones still standing when that level was won, 1..3.</summary>
        public static int PointsOf(ushort play) => (play & PointsMask) + 1;

        // ---- owners ----------------------------------------------------------------------

        /// <summary>
        /// A player id as the blob stores it: the first four bytes of its SHA-256, which is what
        /// the server wrote (the first eight hex characters of the same digest).
        ///
        /// It exists so a player can recognise their OWN recordings and skip them — meeting your
        /// own habits, and later your own skins, worn by a stranger is the one thing this data
        /// could get wrong. It is truncated and one-way because every player downloads the blob:
        /// it answers "is this me?" for the one id the asker already has, and nothing else.
        /// </summary>
        public static uint OwnerOf(string userId) {
            if (string.IsNullOrEmpty(userId)) return NoOwner;
            using (var sha = SHA256.Create()) {
                byte[] d = sha.ComputeHash(Encoding.UTF8.GetBytes(userId));
                return (uint)((d[0] << 24) | (d[1] << 16) | (d[2] << 8) | d[3]);
            }
        }

        // ---- validation (editor / download time) ------------------------------------------

        /// <summary>
        /// Walk the whole blob the way the game will read it: every index entry, every offset,
        /// every win. Returns null when it is sound, or why it is not.
        ///
        /// What gets baked is therefore guaranteed readable by exactly the code that will read
        /// it — the same deal <see cref="WinStats.ValidateBlob"/> makes.
        /// </summary>
        public static string Validate(byte[] b) {
            string err = CheckHeader(b);
            if (err != null) return err;

            int headerSize = b[5], entrySize = b[6], playSize = b[7];
            int count = ReadInt(b, 8);
            int playsPos = headerSize + count * entrySize;

            int lastTotal = -1;
            for (int i = 0; i < count; i++) {
                int e = headerSize + i * entrySize;
                int total = ReadUShort(b, e + TotalAtEntry);
                if (total < lastTotal) return $"entry {i}: index is not sorted by total ({total} after {lastTotal})";
                lastTotal = total;

                long at = (long)playsPos + (uint)ReadInt(b, e + OffsetAtEntry);
                if (at + 2 > b.Length) return $"entry {i}: offset runs past the end";

                int n = ReadUShort(b, (int)at);
                at += 2;
                if (at + (long)n * playSize > b.Length) return $"entry {i}: {n} wins run past the end";

                int sum = 0;
                int lastMinute = -1;
                for (int k = 0; k < n; k++, at += playSize) {
                    ushort play = ReadUShort(b, (int)at);
                    int minute = MinuteOf(play), points = PointsOf(play);
                    if (minute > MaxMinute) return $"entry {i}, win {k}: minute {minute} is outside the window";
                    if (minute < lastMinute) return $"entry {i}, win {k}: wins are out of order";
                    lastMinute = minute;
                    sum += points;
                }
                if (sum != total) return $"entry {i}: wins sum to {sum}, the index says {total}";
            }
            return null;
        }

        // ---- little-endian reads ----------------------------------------------------------

        static int ReadInt(byte[] b, int at) =>
            b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24);

        static ushort ReadUShort(byte[] b, int at) => (ushort)(b[at] | (b[at + 1] << 8));
    }
}
