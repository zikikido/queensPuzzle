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
    /// stage: six checkmark dots ($V) then the gift slot. Milestones come from the manager (cycle 21 →
    /// stages 0→7 small, 7→14 medium, 14→21 large — one day per slot). On reaching a milestone the
    /// bar restarts empty toward the next one.
    /// Ticks slowly so a status flip (server time syncing) while the lobby sits open just works.
    /// </summary>
    public class MBDailyStreakBtn : MonoBehaviour {

        GameObject _active, _offline;

        TMP_Text _currentDay;                                // the 🔥 streak number
        GameObject[] _checks;                                // $V of the six numbered dots, in order
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

        // Collect the dots in sibling order: the leading ones carry a $V, the last is the gift slot.
        void CacheProgressBar() {
            var prog = transform.RecursiveFindChild("DailyStreakProgress");
            if (prog == null) return;

            var checks = new System.Collections.Generic.List<GameObject>();
            Transform gift = null;
            foreach (Transform child in prog) {
                if (child.name != "$Dot") continue;
                var v = child.RecursiveFindChild("$V");
                if (v != null) checks.Add(v.gameObject);
                else gift = child;                          // the gift dot has no $V
            }
            _checks = checks.ToArray();

            if (gift != null) {
                _giftSmall = gift.RecursiveFindChild("$GiftBoxSmall")?.gameObject;
                _giftMedium = gift.RecursiveFindChild("$GiftBoxMeduim")?.gameObject;   // (spelling as authored)
                _giftLarge = gift.RecursiveFindChild("$GiftBoxLarge")?.gameObject;
            }
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

            // Current stage: the first milestone the streak hasn't reached yet (strict — a reached
            // milestone starts the next stage empty). Past the last one, sit full on the last stage.
            int tier = milestones.Length - 1, prev = 0, next = milestones[milestones.Length - 1];
            for (int i = 0; i < milestones.Length; i++) {
                if (streak < milestones[i]) { tier = i; next = milestones[i]; prev = i == 0 ? 0 : milestones[i - 1]; break; }
                prev = milestones[i];
            }

            int stageLen = next - prev;                                  // 7, 7 or 14
            int done = Mathf.Clamp(streak - prev, 0, stageLen);
            int slots = _checks != null ? _checks.Length : 0;           // six checkmark dots

            // Spread the stage's days over the slots: slot i (1-based) is done once the stage has
            // progressed i/(slots+1) of the way — the gift is the final (slots+1) step.
            if (_checks != null)
                for (int i = 0; i < slots; i++)
                    _checks[i].SetActive(done * (slots + 1) >= (i + 1) * stageLen);

            SetGift(tier);
        }

        void SetGift(int tier) {
            if (_giftSmall != null) _giftSmall.SetActive(tier == 0);
            if (_giftMedium != null) _giftMedium.SetActive(tier == 1);
            if (_giftLarge != null) _giftLarge.SetActive(tier == 2);
        }
    }
}
