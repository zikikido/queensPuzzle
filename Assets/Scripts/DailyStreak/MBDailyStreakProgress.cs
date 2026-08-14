using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// The streak progress bar as a self-contained, reusable widget (lobby button today, popups
    /// later). Passive: it never polls or ticks — callers push a streak in and it repaints.
    ///   ApplyImmediately(streak) — snap the checks + gift to that streak.
    ///   AnimateTo(streak)        — tween from what it currently shows to the given streak.
    ///
    /// Layout: this GameObject's direct children are the $Dot slots (in order); each holds a $V
    /// checkmark and the last one also the three $GiftBox* sizes. Milestones/cycle come from
    /// <see cref="DailyStreakManager"/>, so at streak == cycle the bar shows the fresh next cycle.
    /// </summary>
    public class MBDailyStreakProgress : MonoBehaviour {

        GameObject[] _checks;                                // $V of every dot, in order (last = gift dot)
        GameObject _giftSmall, _giftMedium, _giftLarge;      // the three sizes on the gift dot
        bool _cached;

        int _shownStreak;                                    // last streak painted — the anim start point

        /// <summary>Snap the bar to <paramref name="streak"/> — checks + gift, no animation.</summary>
        public void ApplyImmediately(int streak) {
            _ensureCached();
            _shownStreak = streak;

            var milestones = DailyStreakManager.Milestones;

            // At the top of the cycle the streak sits on the last milestone — show it like every other
            // milestone: an empty bar toward the next (the new cycle's first milestone).
            int pos = streak >= DailyStreakManager.CycleLength ? 0 : streak;

            // Current stage: the first milestone pos hasn't reached yet (strict — a reached milestone
            // starts the next stage empty).
            int tier = 0, prev = 0, next = milestones[0];
            for (int i = 0; i < milestones.Length; i++) {
                if (pos < milestones[i]) { tier = i; next = milestones[i]; prev = i == 0 ? 0 : milestones[i - 1]; break; }
                prev = milestones[i];
            }

            int stageLen = next - prev;                       // one day per slot (7)
            int done = Mathf.Clamp(pos - prev, 0, stageLen);
            int slots = _checks != null ? _checks.Length : 0;

            // Spread the stage's days across the slots: slot i is lit once i+1 of them are done.
            if (_checks != null)
                for (int i = 0; i < slots; i++)
                    _checks[i].SetActive(done * slots >= (i + 1) * stageLen);

            _setGift(tier);
        }

        /// <summary>Animate the bar from what it currently shows to <paramref name="streak"/>
        /// (for the reward-reveal popup). TODO: real tween — snaps for now.</summary>
        public void AnimateTo(int streak) => ApplyImmediately(streak);

        // Direct children are the dots (in order). Every dot carries a $V; the gift dot additionally
        // holds the three gift boxes. Cached lazily so it works whenever the widget first paints.
        void _ensureCached() {
            if (_cached) return;
            _cached = true;

            var checks = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in transform) {
                if (child.name != "$Dot") continue;
                var v = child.RecursiveFindChild("$V");
                if (v != null) checks.Add(v.gameObject);

                var small = child.RecursiveFindChild("$GiftBoxSmall");
                if (small != null) {                          // this is the gift dot
                    _giftSmall = small.gameObject;
                    _giftMedium = child.RecursiveFindChild("$GiftBoxMeduim")?.gameObject;   // (spelling as authored)
                    _giftLarge = child.RecursiveFindChild("$GiftBoxLarge")?.gameObject;
                }
            }
            _checks = checks.ToArray();
        }

        void _setGift(int tier) {
            if (_giftSmall != null) _giftSmall.SetActive(tier == 0);
            if (_giftMedium != null) _giftMedium.SetActive(tier == 1);
            if (_giftLarge != null) _giftLarge.SetActive(tier == 2);
        }
    }
}
