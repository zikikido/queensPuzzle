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
        [SerializeField] Color _pctColor = new Color(1f, 0.42f, 0.61f);   // the "78%" pops in its own color

        CanvasGroup _group;
        Coroutine _running;

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
            if (attempts > 1) return;
            if (!daily && AppData.LevelIdx.Value + 1 < GameConfig.StartShowFirstTryAtLevel) return;   // opening levels stay clean

            float beatsX = WinStats.For(LevelLoader.CurrentLevelHash, LevelLoader.CurrentLevelWeight).FirstTryBeatsPct;
            if (beatsX < 0f) return;                             // no data — say nothing
            float rate = 100f - beatsX;                          // stored = % of starters who DIDN'T pass first try

            gameObject.SetActive(true);                          // may start inactive — Awake runs now

            var text = transform.RecursiveFindChild<TMPro.TMP_Text>("$Text");
            if (text == null) return;
            string pct = $"<color=#{ColorUtility.ToHtmlStringRGB(_pctColor)}>{rate:0.#}%</color>";
            text.text = rate >= 50f
                ? $"{pct} of players pass this level\non their first try!"
                : $"ONLY {pct} of players pass this level\non their first try!";

            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(ShowFlow());
        }

        IEnumerator ShowFlow() {
            _group.blocksRaycasts = true;                        // tappable only while visible
            for (float t = 0f; t < _fadeIn; t += Time.unscaledDeltaTime) {
                _group.alpha = t / _fadeIn;
                yield return null;
            }
            _group.alpha = 1f;

            yield return new WaitForSecondsRealtime(_hold);

            for (float t = 0f; t < _fadeOut; t += Time.unscaledDeltaTime) {
                _group.alpha = 1f - t / _fadeOut;
                yield return null;
            }
            _running = null;
            Hide();
        }

        /// <summary>Tap (or the flow ending) → gone immediately.</summary>
        public void Hide() {
            if (_running != null) { StopCoroutine(_running); _running = null; }
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
        }
    }
}
