using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Drives the lobby's daily-streak button — pure display over <see cref="DailyStreakManager"/>.
    /// Exactly one child root is active for the current status:
    ///   $DailyButtonActive     Status == Active  — streak count + progress
    ///   $DailyButtonOffline    Status == Offline — no trusted server time
    ///
    /// The bar itself is <see cref="MBDailyStreakProgress"/> (a reusable widget); this only picks the
    /// status root, sets $CurrentDay, and pushes the streak into the widget. It ticks slowly so a
    /// status flip (server time syncing / net drop) while the lobby sits open just works.
    /// </summary>
    public class MBDailyStreakBtn : MonoBehaviour {

        GameObject _active, _offline;
        TMP_Text _currentDay;                 // the 🔥 streak number
        MBDailyStreakProgress _progress;      // the bar widget under $DailyButtonActive
        MBDailyStreakInfoPopup _infoPopup;    // opened on tap (found lazily)

        // What the UI currently shows. Sentinels so the first tick always paints.
        EStreakStatus _shownStatus = (EStreakStatus)(-1);
        int _shownStreak = -1;

        void Awake() {
            var activeT = transform.RecursiveFindChild("$DailyButtonActive");
            var offlineT = transform.RecursiveFindChild("$DailyButtonOffline");
            _active = activeT != null ? activeT.gameObject : null;
            _offline = offlineT != null ? offlineT.gameObject : null;

            _currentDay = transform.RecursiveFindChild<TMP_Text>("$CurrentDay");
            _progress = GetComponentInChildren<MBDailyStreakProgress>(true);

            // Tapping the active button opens the streak info popup (current streak, entrance only).
            var btn = activeT != null ? activeT.GetComponent<Button>() : null;
            if (btn != null) btn.onClick.AddListener(_openInfo);
        }

        void _openInfo() {
            if (_infoPopup == null)
                _infoPopup = FindAnyObjectByType<MBDailyStreakInfoPopup>(FindObjectsInactive.Include);
            if (_infoPopup != null) _infoPopup.Show(DailyStreakManager.Streak);
        }

        void OnEnable() {
            _apply();
            InvokeRepeating(nameof(_apply), 1f, 1f);   // status can flip while the lobby sits open (net drop/return)
        }

        void OnDisable() => CancelInvoke(nameof(_apply));

        // Runs each second. SyncDayChange breaks the streak if server-midnight passed while the lobby
        // sat open; then we repaint — but only when the status or streak actually changed, not every tick.
        void _apply() {
            DailyStreakManager.SyncDayChange();

            var status = DailyStreakManager.Status;
            int streak = DailyStreakManager.Streak;
            if (status == _shownStatus && streak == _shownStreak) return;
            _shownStatus = status;
            _shownStreak = streak;

            if (_active != null) _active.SetActive(status == EStreakStatus.Active);
            if (_offline != null) _offline.SetActive(status == EStreakStatus.Offline);

            if (status == EStreakStatus.Active) {
                if (_currentDay != null) _currentDay.text = streak.ToString();
                if (_progress != null) _progress.ApplyImmediately(streak);
            }
        }
    }
}
