using System.Collections;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The in-game streak PROGRESS popup — shown after a win that advanced the streak without hitting
    /// a milestone, before the win popup. (The reward variant is <see cref="MBDailyStreakInGameRewordedPopup"/>.)
    /// Caller does <c>Show(streak)</c> then waits on <see cref="IsShowing"/>; $ContinueButton closes it.
    /// No anims — just fill the streak number + progress bar and continue.
    /// </summary>
    public class MBDailyStreakInGamePopup : MonoBehaviour {

        CanvasGroup _group;
        bool _showing;              // a real show is on screen (guards the layout pass from hiding it)

        TMP_Text _streakText;                   // $Streak
        MBDailyStreakProgress _progress;        // the bar widget in DailyStreakProgressContainer

        /// <summary>True while the popup is open — the win flow waits on this before continuing.</summary>
        public bool IsShowing => _showing;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass

            _streakText = transform.RecursiveFindChild<TMP_Text>("$Streak");
            _progress = GetComponentInChildren<MBDailyStreakProgress>(true);

            var cont = transform.RecursiveFindChild<Button>("$ContinueButton");
            if (cont != null) cont.onClick.AddListener(_close);
        }

        // Same trick as the win popup: stay ACTIVE but invisible for the first frames so the UI lays
        // out at real size, then hide until Show().
        IEnumerator Start() {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _group.alpha = 1f;
            if (!_showing) gameObject.SetActive(false);
        }

        public void Show(int streak) {
            _showing = true;
            _group.alpha = 1f;
            gameObject.SetActive(true);

            if (_streakText != null) _streakText.text = streak.ToString();
            if (_progress != null) _progress.ApplyImmediately(streak);
        }

        void _close() {
            _showing = false;
            gameObject.SetActive(false);
        }
    }
}
