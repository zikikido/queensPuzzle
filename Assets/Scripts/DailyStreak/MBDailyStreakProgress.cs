using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// The streak progress bar as a self-contained, reusable widget (lobby button + popups). Passive:
    /// it never polls or ticks — callers push a streak in and it repaints.
    ///   ApplyImmediately(streak) — snap the checks to that streak.
    ///   AnimateTo(streak)        — tween from what it currently shows to the given streak.
    ///
    /// Layout: this GameObject's direct children are the $Dot slots (in order), each holding a $V
    /// checkmark; the last slot also shows the (static) gift. The streak climbs forever; the bar shows
    /// its position within the current cycleLength-day segment (streak % cycle), one day per slot, so
    /// every multiple of the cycle lands on an empty bar toward the next gift.
    /// </summary>
    public class MBDailyStreakProgress : MonoBehaviour {

        GameObject[] _checks;        // $V of every dot, in order
        bool _cached;

        int _shownStreak;            // last streak painted — the anim start point

        /// <summary>Snap the bar to <paramref name="streak"/> — checks only, no animation.</summary>
        public void ApplyImmediately(int streak) {
            _ensureCached();
            _shownStreak = streak;

            int cycle = DailyStreakManager.CycleLength;
            // Position within the current segment — the streak climbs forever, the bar cycles every
            // cycle days; a multiple of cycle lands on 0, an empty bar toward the next gift.
            int done = cycle > 0 ? streak % cycle : 0;
            int slots = _checks != null ? _checks.Length : 0;

            // Spread the cycle's days across the slots: slot i is lit once i+1 of them are done.
            if (_checks != null)
                for (int i = 0; i < slots; i++)
                    _checks[i].SetActive(done * slots >= (i + 1) * cycle);
        }

        /// <summary>Animate the bar from what it currently shows to <paramref name="streak"/>
        /// (for the reward-reveal popup). TODO: real tween — snaps for now.</summary>
        public void AnimateTo(int streak) => ApplyImmediately(streak);

        // Direct children are the dots (in order), each with a $V. Cached lazily so it works whenever
        // the widget first paints.
        void _ensureCached() {
            if (_cached) return;
            _cached = true;

            var checks = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in transform) {
                if (child.name != "$Dot") continue;
                var v = child.RecursiveFindChild("$V");
                if (v != null) checks.Add(v.gameObject);
            }
            _checks = checks.ToArray();
        }
    }
}
