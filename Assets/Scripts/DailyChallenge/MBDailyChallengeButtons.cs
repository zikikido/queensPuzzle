using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Drives the lobby's three daily cards — pure display over <see cref="DailyChallengeManager"/>.
    /// Attach to ButtonsContainer (or any parent of the cards). Exactly one card is active:
    ///   $DailyButtonLock            Status == Lock (nothing to fill)
    ///   $DailyButtonActive          Status == Active — $Date, $TimeLeft (countdown to next UTC day)
    ///   $DailyButtonDone            Status == Done — $Date, $SolvedTime, $TopPct
    /// Ticks once per second so the countdown runs and a midnight/status flip while the lobby
    /// sits open just works. Play click starts the daily run; the Done card only logs.
    /// </summary>
    public class MBDailyChallengeButtons : MonoBehaviour {

        GameObject _lock, _active, _done;
        TMP_Text _date, _timeLeft;                       // under $DailyButtonActive
        TMP_Text _doneDate, _solvedTime, _topPct;        // under $DailyButtonDone

        EDailyChallengeStatus _shownStatus;   // what Refresh last built — Tick refreshes only on change

        void Awake() {
            var lockT = transform.RecursiveFindChild("$DailyButtonLock");
            var activeT = transform.RecursiveFindChild("$DailyButtonActive");
            var doneT = transform.RecursiveFindChild("$DailyButtonDone");
            _lock = lockT != null ? lockT.gameObject : null;
            _active = activeT != null ? activeT.gameObject : null;
            _done = doneT != null ? doneT.gameObject : null;

            if (activeT != null) {   // $Date exists on BOTH cards — searches stay scoped per card
                _date = activeT.RecursiveFindChild<TMP_Text>("$Date");
                _timeLeft = activeT.RecursiveFindChild<TMP_Text>("$TimeLeft");
                activeT.GetComponent<Button>().onClick.AddListener(OnPlayClick);
            }
            if (doneT != null) {
                _doneDate = doneT.RecursiveFindChild<TMP_Text>("$Date");
                _solvedTime = doneT.RecursiveFindChild<TMP_Text>("$SolvedTime");
                _topPct = doneT.RecursiveFindChild<TMP_Text>("$TopPct");
                doneT.GetComponent<Button>().onClick.AddListener(OnDoneClick);
            }
        }

        void OnEnable() {
            Refresh();
            InvokeRepeating(nameof(Tick), 1f, 1f);
        }

        void OnDisable() => CancelInvoke(nameof(Tick));

        // 1/sec: only the countdown string changes — a full Refresh only when the status
        // actually flips (midnight, or Done after a solve) which is a once-a-day event.
        void Tick() {
            if (DailyChallengeManager.Status != _shownStatus) { Refresh(); return; }
            if (_shownStatus == EDailyChallengeStatus.Active && _timeLeft != null)
                _timeLeft.text = DailyChallengeManager.TimeLeft.ToString(@"hh\:mm\:ss");
        }

        void Refresh() {
            var status = _shownStatus = DailyChallengeManager.Status;
            if (_lock != null) _lock.SetActive(status == EDailyChallengeStatus.Lock);
            if (_active != null) _active.SetActive(status == EDailyChallengeStatus.Active);
            if (_done != null) _done.SetActive(status == EDailyChallengeStatus.Done);

            switch (status) {
                case EDailyChallengeStatus.Active:
                    if (_date != null) _date.text = DailyChallengeManager.NiceDate;
                    if (_timeLeft != null) _timeLeft.text = DailyChallengeManager.TimeLeft.ToString(@"hh\:mm\:ss");
                    break;
                case EDailyChallengeStatus.Done:
                    if (_doneDate != null) _doneDate.text = DailyChallengeManager.NiceDate;
                    if (_solvedTime != null) _solvedTime.text = DailyChallengeManager.FormatTime(DailyChallengeManager.State.timeSec);
                    if (_topPct != null) _topPct.text = $"TOP {DailyChallengeManager.State.topPct}%";
                    break;
            }
        }

        // Play today's daily: lock the day's tier, flag the run, and enter gameplay —
        // LevelLoader serves the daily pack while InDailyRun is up.
        void OnPlayClick() {
            Debug.Log($"[DailyChallenge] play — day {DailyChallengeManager.DayIndex}, " +
                      $"tier {DailyChallengeManager.Tier} ({DailyChallengeManager.TierName}), " +
                      $"timeSec {DailyChallengeManager.State.timeSec:0}");
            DailyChallengeManager.StartDaily();
#if UNITY_EDITOR
            // the daily launches by day/tier — drop any Level Builder playtest token so it isn't reused
            UnityEditor.SessionState.EraseString(LevelLoader.PlayLevelGuidKey);
#endif
            Navigator.Go(Navigator.Gameplay);
        }

        void OnDoneClick() =>
            Debug.Log($"[DailyChallenge] done card clicked — solved in {DailyChallengeManager.FormatTime(DailyChallengeManager.State.timeSec)}, " +
                      $"TOP {DailyChallengeManager.State.topPct}%");
    }
}
