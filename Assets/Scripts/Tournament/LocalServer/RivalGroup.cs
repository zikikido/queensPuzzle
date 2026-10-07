using System;
using System.Collections.Generic;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Builds the nineteen opponents a tournament is run against.
    ///
    /// THE WHOLE IDEA: we choose what each rival ENDS ON. The recording decides everything else —
    /// when they play, how long for, whether they vanish for a day. So the competition is designed
    /// and the behaviour is not simulated, because it was never simulated: it was recorded.
    ///
    /// Targets are multiples of what THIS player usually scores in 48 hours, so the climb costs a
    /// weak player and a strong one the same effort. A set of nineteen multipliers is a whole
    /// tournament's difficulty in one line:
    ///
    ///     normal:  1.8  1.5  1.25 │ 1.15  1.08  1.03 │ (player = 1.0) │ 0.96 …
    ///
    /// Four rivals above them, so playing as usual finishes around 7th and third place — the last
    /// prize — costs about 25% more. Every win moves them a row, because a win is worth ~2.4 points
    /// and the rivals just above are a point or two apart.
    ///
    /// Which set is drawn VARIES, and that matters more than the sets do. A player who is always
    /// 7th, always 25% from a prize, is looking at a pattern; a player who comes 3rd without trying
    /// one week and cannot crack 8th the next is looking at luck. So the prize is deliberately not
    /// available every time.
    /// </summary>
    public static class RivalGroup {

        /// <summary>Rivals per group — the player makes twenty.</summary>
        public const int Size = 19;

        // ---- the three difficulty sets ---------------------------------------------------
        // Each is nineteen multipliers of the player's usual 48-hour score, biggest first. How
        // many sit above 1.0 is the whole design: that is where the player lands by doing nothing
        // differently. The last slot is not a multiplier — see DeadAccountScore.

        /// <summary>Four above: finishes ~5th by default, a prize within reach of a normal week.</summary>
        static readonly float[] Easy = {
            1.20f, 1.10f,
            0.95f, 0.89f, 0.83f, 0.77f, 0.71f, 0.65f, 0.59f, 0.53f,
            0.47f, 0.41f, 0.35f, 0.29f, 0.23f, 0.17f, 0.11f, 0.06f, 0.03f,
        };

        /// <summary>Six above: finishes ~7th, third place costs about 25% more play.</summary>
        static readonly float[] Normal = {
            1.80f, 1.50f, 1.25f, 1.15f, 1.08f, 1.03f,
            0.96f, 0.90f, 0.84f, 0.77f, 0.69f, 0.60f, 0.50f,
            0.41f, 0.32f, 0.24f, 0.16f, 0.09f, 0.04f,
        };

        /// <summary>Ten above, one of them far out of reach. This is the week the prize is gone —
        /// and the reason winning one means something.</summary>
        static readonly float[] Hard = {
            5.50f, 3.40f, 2.50f, 2.10f, 1.80f, 1.60f, 1.45f, 1.30f, 1.20f, 1.10f,
            0.92f, 0.84f, 0.75f, 0.65f, 0.55f, 0.44f, 0.33f, 0.22f, 0.11f,
        };

        const float EasyChance = 0.25f;
        const float HardChance = 0.15f;   // the rest are Normal

        /// <summary>Each multiplier is nudged by up to this much, so two groups of the same kind
        /// are never the same group.</summary>
        const float Jitter = 0.08f;

        /// <summary>The last slot is an absolute score, not a multiple: someone who opened the game
        /// once. As a multiplier it would scale with the player, and a strong player's "dead
        /// account" would be busier than most people's whole week.</summary>
        const int DeadAccountScore = 2;

        /// <summary>
        /// What a player scores in 48 hours when we have never seen them finish a tournament: the
        /// median of everyone past the unlock level. Wrong for any particular player and right for
        /// most, and only used once — the next group is built on what they actually did.
        ///
        /// Taken from the blob, so it tracks the game as it grows and arrives with every refresh
        /// of the recordings rather than with a release.
        /// </summary>
        public static int TypicalScore =>
            RivalsBlob.MedianScore > 0 ? RivalsBlob.MedianScore : FallbackMedianScore;

        /// <summary>For a blob that is missing or predates the field. Measured over the Firebase
        /// export when this was written: 16 wins at ~2.36 points.</summary>
        public const int FallbackMedianScore = 38;

        /// <summary>How close a recording has to land to its target before we take it. Widened
        /// when nothing is near enough; the bank holds dozens of candidates per target at the
        /// scores most players live at.</summary>
        static readonly float[] Tolerances = { 0.05f, 0.10f, 0.20f, 0.40f };

        // ---- building --------------------------------------------------------------------

        /// <summary>
        /// Nineteen rivals for a player who usually scores <paramref name="myScore"/>.
        /// <paramref name="myOwner"/> keeps the player's own recordings out of their group, and
        /// <paramref name="myName"/> keeps a rival from arriving under their name.
        /// </summary>
        public static List<LocalRival> Build(int myScore, uint myOwner, string myName) {
            try { return _build(myScore, myOwner, myName); }
            // The recordings are read here and nowhere else, so this is where they stop being
            // worth holding: everything chosen is copied out below, and the ~280 KB behind it is
            // not wanted again for another 48 hours.
            finally { RivalsBlob.Unload(); }
        }

        static List<LocalRival> _build(int myScore, uint myOwner, string myName) {
            var rivals = new List<LocalRival>(Size);
            if (RivalsBlob.Count == 0) {
                Debug.LogWarning("[RivalGroup] no recordings available — the tournament has no rivals");
                return rivals;
            }

            var rnd = new System.Random(Guid.NewGuid().GetHashCode());
            float[] set = _pickSet(rnd);
            var usedRecordings = new HashSet<int>();
            var usedNames = new HashSet<string> { myName ?? "" };

            for (int i = 0; i < set.Length; i++) {
                bool isLast = i == set.Length - 1;
                int target = isLast
                    ? DeadAccountScore
                    : Mathf.Max(1, Mathf.RoundToInt(myScore * set[i] * _jitter(rnd)));

                int pick = _pickRecording(target, myOwner, usedRecordings, rnd);
                if (pick < 0) continue;                 // bank too small to fill the group
                usedRecordings.Add(pick);

                rivals.Add(new LocalRival {
                    name = _pickName(usedNames),
                    total = RivalsBlob.TotalAt(pick),
                    plays = _unpack(RivalsBlob.PlaysAt(pick)),
                });
            }
            return rivals;
        }

        static float[] _pickSet(System.Random rnd) {
            double roll = rnd.NextDouble();
            if (roll < EasyChance) return Easy;
            if (roll < EasyChance + HardChance) return Hard;
            return Normal;
        }

        static float _jitter(System.Random rnd) => 1f + (float)(rnd.NextDouble() * 2 - 1) * Jitter;

        /// <summary>
        /// A recording ending near <paramref name="target"/> — at random among those close enough,
        /// never one of this player's own, never one already in this group.
        ///
        /// Random and not nearest: nearest would hand the same player the same opponents every
        /// time, since their usual score barely moves between tournaments.
        /// </summary>
        static int _pickRecording(int target, uint myOwner, HashSet<int> used, System.Random rnd) {
            foreach (float tolerance in Tolerances) {
                var candidates = new List<int>();
                int span = Mathf.Max(1, Mathf.RoundToInt(target * tolerance));
                int from = RivalsBlob.LowerBound(target - span);
                for (int i = from; i < RivalsBlob.Count && RivalsBlob.TotalAt(i) <= target + span; i++) {
                    if (used.Contains(i) || RivalsBlob.OwnerAt(i) == myOwner) continue;
                    candidates.Add(i);
                }
                if (candidates.Count > 0) return candidates[rnd.Next(candidates.Count)];
            }

            // Past every tolerance, this player has outgrown the recordings: their target is
            // above anything anyone in the bank actually scored. The closest one it is, which
            // compresses the top of their table and makes the tournament EASIER than designed.
            //
            // That is deliberate. We could stitch two recordings into one to reach any target,
            // and chose not to: it is the only place we would be inventing behaviour instead of
            // replaying it, and the point of all of this is that it was never invented. It also
            // fixes itself from two directions — the bank is resampled daily, so its ceiling
            // rises as the game grows, and a player this strong deserves real opponents, which
            // is what a real server will bring.
            //
            // Today this is under 1% of players (the ceiling bites above ~470 points in 48
            // hours). When that stops being true, "how many players score above the bank's top"
            // is one BigQuery query away — no instrumentation needed here.
            int best = -1, bestGap = int.MaxValue;
            for (int i = 0; i < RivalsBlob.Count; i++) {
                if (used.Contains(i) || RivalsBlob.OwnerAt(i) == myOwner) continue;
                int gap = Mathf.Abs(RivalsBlob.TotalAt(i) - target);
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return best;
        }

        /// <summary>The same generator the player's own name came from, so no row on the table
        /// looks like a different kind of thing from the others.</summary>
        static string _pickName(HashSet<string> used) {
            for (int attempt = 0; attempt < 20; attempt++) {
                string name = ProfileManager.GenerateName();
                if (used.Add(name)) return name;
            }
            // Every draw collided — vanishingly unlikely with 35x35 pairs, and a duplicate name
            // is better than no rival.
            return ProfileManager.GenerateName();
        }

        static int[] _unpack(ushort[] plays) {
            var copy = new int[plays.Length];
            for (int i = 0; i < plays.Length; i++) copy[i] = plays[i];
            return copy;
        }
    }
}
