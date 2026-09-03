using System.Collections;
using Common;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Level-start social proof: "84% of players pass this level on their first try!" —
    /// and under half it flips to a challenge: "ONLY 42% ...". Data = the first-try stat
    /// from the winstats blob (which never clears the win-popup bar in this game, so this
    /// is where it earns its keep).
    ///
    /// Lives in the gameplay scene (found by type, like MBWinPopup; may start inactive).
    /// MBGameplay calls <see cref="TryShow"/> right after the bloom. Quick fade-in, a
    /// short readable hold, quick fade-out; ANY tap on the card dismisses instantly.
    /// It never blocks the screen: raycasts are on only while visible, and only the
    /// card's own area catches them — no fullscreen blocker.
    /// </summary>
    public class MBFirstTryPopup : MonoBehaviour {

        [SerializeField] float _fadeIn = 0.15f;
        [SerializeField] float _hold = 2.5f;     // long enough to read, short enough to not nag
        [SerializeField] float _fadeOut = 0.25f;
        [SerializeField] Color _pctColor = new Color32(0xFF, 0x97, 0x43, 0xFF);   // the "78%" pops in its own color
        [SerializeField] float _pctSize = 84f;                             // TMP <size> for the number

        CanvasGroup _group;
        Coroutine _running;
        bool _boardLockHeld;   // InputLocks held while visible — the dismiss tap must not paint an X

        // The board reads MBTouches (world-space), not UI raycasts — the fullscreen
        // $CloseBtn alone can't shield it. Lock like every other overlay does.
        void _lockBoard(bool on) {
            if (on == _boardLockHeld || MBGameplay.instance == null) return;
            _boardLockHeld = on;
            MBGameplay.instance.InputLocks += on ? 1 : -1;
        }

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var btn = transform.RecursiveFindChild<Button>("$CloseBtn");   // tap → dismiss instantly
            if (btn != null) btn.onClick.AddListener(Hide);
        }

        /// <summary>Called by MBGameplay after the bloom. Shows only on the level's FIRST
        /// attempt (retries already lost the "first try" story) and only with real data.</summary>
        public void TryShow() {
            bool daily = DailyChallengeManager.InDailyRun;
            int attempts = daily ? DailyChallengeManager.State.attempts : AppData.LevelAttempts.Value;
            //if (attempts > 1) return;
            if (!daily && AppData.LevelIdx.Value + 1 < GameConfig.StartShowFirstTryAtLevel) return;   // opening levels stay clean

            float rate = WinStats.For(LevelLoader.CurrentLevelHash, LevelLoader.CurrentLevelWeight).NeverLoseOrLeavePct;
            if (rate < 0f) return;                               // no data — say nothing

            gameObject.SetActive(true);                          // may start inactive — Awake runs now

            var text = transform.RecursiveFindChild<TMPro.TMP_Text>("$Text");
            if (text == null) return;
            string pct = $"<size={_pctSize:0.#}><color=#{ColorUtility.ToHtmlStringRGB(_pctColor)}>{rate:0.##}%</color></size>";
            text.text = rate >= 50f
                ? $"{pct} of players pass\nthis level on their first try!"
                : $"Only {pct} of players pass\nthis level on their first try!";

            _lockBoard(true);
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(ShowFlow());
        }

        IEnumerator ShowFlow() {
            _group.blocksRaycasts = true;                        // tappable only while visible
            // From the CURRENT alpha at constant speed — a re-show mid-fade-out never pops.
            for (float a = _group.alpha; a < 1f; a += Time.unscaledDeltaTime / _fadeIn) {
                _group.alpha = a;
                yield return null;
            }
            _group.alpha = 1f;

            yield return new WaitForSecondsRealtime(_hold);

            _running = null;
            Hide();                                              // same animated exit as a tap
        }

        /// <summary>Tap (or the hold ending) → the board comes back FIRST (lock released,
        /// taps no longer eaten), then the card fades out. Safe to call repeatedly:
        /// the unlock is guarded and a running fade just restarts from where it is.</summary>
        public void Hide() {
            if (_running != null) { StopCoroutine(_running); _running = null; }
            _lockBoard(false);
            _group.blocksRaycasts = false;
            if (_group.alpha > 0f) _running = StartCoroutine(HideFlow());
        }

        IEnumerator HideFlow() {
            // From the CURRENT alpha (a tap mid-fade-in exits from there) at constant speed.
            for (float a = _group.alpha; a > 0f; a -= Time.unscaledDeltaTime / _fadeOut) {
                _group.alpha = a;
                yield return null;
            }
            _group.alpha = 0f;
            _running = null;
        }

        void OnDisable() => _lockBoard(false);   // never leak the lock past our lifetime
    }
}
