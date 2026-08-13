using Common;
using TMPro;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Drives the lobby's daily-streak button — pure display over <see cref="DailyStreakManager"/>.
    /// Exactly one child root is active for the current status:
    ///   $DailyButtonActive     Status == Active  — streak count + progress
    ///   $DailyButtonOffline    Status == Offline — no trusted server time
    ///
    /// The Active progress bar (DailyStreakProgress) is a fixed 7-slot view of the CURRENT milestone
    /// stage: seven $V checkmark dots, the last one also holding the gift. Milestones come from the
    /// manager (cycle 21 → stages 0→7 small, 7→14 medium, 14→21 large — one day per slot). Reaching a
    /// milestone empties the bar toward the next one; the final milestone loops back to the first, so
    /// every milestone day looks the same — an empty bar.
    /// Ticks slowly so a status flip (server time syncing) while the lobby sits open just works.
    /// </summary>
    public class MBDailyStreakBtn : MonoBehaviour {

        GameObject _active, _offline;

        TMP_Text _currentDay;                                // the 🔥 streak number
        GameObject[] _checks;                                // $V of every dot, in order (last = gift dot)
        GameObject _giftSmall, _giftMedium, _giftLarge;      // the three sizes on the gift dot

        // What the UI currently shows. Sentinels so the first tick always paints.
        EStreakStatus _shownStatus = (EStreakStatus)(-1);
        int _shownStreak = -1;

        void Awake() {
            var activeT = transform.RecursiveFindChild("$DailyButtonActive");
            var offlineT = transform.RecursiveFindChild("$DailyButtonOffline");
            _active = activeT != null ? activeT.gameObject : null;
            _offline = offlineT != null ? offlineT.gameObject : null;

            _currentDay = transform.RecursiveFindChild<TMP_Text>("$CurrentDay");
            CacheProgressBar();
        }

        // Every dot carries a $V; the gift dot additionally holds the three gift boxes. Collect the
        // checks in sibling order and grab the gift boxes off whichever dot has them.
        void CacheProgressBar() {
            var prog = transform.RecursiveFindChild("DailyStreakProgress");
            if (prog == null) return;

            var checks = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in prog) {
                if (child.name != "$Dot") continue;
                var v = child.RecursiveFindChild("$V");
                if (v != null) checks.Add(v.gameObject);

                var small = child.RecursiveFindChild("$GiftBoxSmall");
                if (small != null) {                        // this is the gift dot
                    _giftSmall = small.gameObject;
                    _giftMedium = child.RecursiveFindChild("$GiftBoxMeduim")?.gameObject;   // (spelling as authored)
                    _giftLarge = child.RecursiveFindChild("$GiftBoxLarge")?.gameObject;
                }
            }
            _checks = checks.ToArray();
        }

        void OnEnable() {
            Apply();
            InvokeRepeating(nameof(Apply), 1f, 1f);   // status can flip while the lobby sits open (net drop/return)
        }

        void OnDisable() => CancelInvoke(nameof(Apply));

        // Runs each second. SyncDayChange breaks the streak if server-midnight passed while the lobby
        // sat open; then we repaint — but only when the status or streak actually changed, not every tick.
        void Apply() {
            DailyStreakManager.SyncDayChange();

            var status = DailyStreakManager.Status;
            int streak = DailyStreakManager.Streak;
            if (status == _shownStatus && streak == _shownStreak) return;
            _shownStatus = status;
            _shownStreak = streak;

            if (_active != null) _active.SetActive(status == EStreakStatus.Active);
            if (_offline != null) _offline.SetActive(status == EStreakStatus.Offline);

            if (status == EStreakStatus.Active) FillProgress(streak);
        }

        // Light the checkmarks for the completed part of the current stage and show the stage's gift.
        void FillProgress(int streak) {
            if (_currentDay != null) _currentDay.text = streak.ToString();

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

            int stageLen = next - prev;                                  // one day per slot (7)
            int done = Mathf.Clamp(pos - prev, 0, stageLen);
            int slots = _checks != null ? _checks.Length : 0;

            // Spread the stage's days across the slots: slot i is lit once i+1 of them are done.
            if (_checks != null)
                for (int i = 0; i < slots; i++)
                    _checks[i].SetActive(done * slots >= (i + 1) * stageLen);

            SetGift(tier);
        }

        void SetGift(int tier) {
            if (_giftSmall != null) _giftSmall.SetActive(tier == 0);
            if (_giftMedium != null) _giftMedium.SetActive(tier == 1);
            if (_giftLarge != null) _giftLarge.SetActive(tier == 2);
        }
    }
}
