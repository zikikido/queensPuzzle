using System.Collections;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The in-game streak PROGRESS popup — shown after a win that advanced the streak without hitting
    /// a milestone, before the win popup. (The reward variant is <see cref="MBDailyStreakInGameRewordedPopup"/>.)
    /// Caller does <c>Show(oldStreak, newStreak)</c> then waits on <see cref="IsShowing"/>;
    /// $ContinueButton closes it. Show runs a coroutine: entrance cascade → stamp the new check →
    /// scroll the streak number old→new.
    /// </summary>
    public class MBDailyStreakInfoPopup : MonoBehaviour {

        // The streak number container starts scrolled up by this (old in view) and slides to 0 (new).
        const float ScrollFrom = -158f;

        CanvasGroup _group;
        bool _showing;              // a real show is on screen (guards the layout pass from hiding it)

        TMP_Text _streakOld, _streakNew;        // $StreakOld / $StreakNew
        RectTransform _streakScroll;            // their shared parent (Container) — scrolled old→new
        MBDailyStreakProgress _progress;        // the bar widget in DailyStreakProgressContainer

        /// <summary>True while the popup is open — the win flow waits on this before continuing.</summary>
        public bool IsShowing => _showing;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass

            _streakScroll = transform.RecursiveFindChild<RectTransform>("$StreakContainer");
            _streakOld = _streakScroll.RecursiveFindChild<TMP_Text>("$StreakOld");
            _streakNew = _streakScroll.RecursiveFindChild<TMP_Text>("$StreakNew");
            
            _progress = GetComponentInChildren<MBDailyStreakProgress>(true);

            // Continue button carries both markers: $ (found from code) and % (used by the animator).
            var cont = transform.RecursiveFindChild<Button>("$%ContinueButton");
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

        /// <summary>Info view (e.g. from the lobby streak button): the current streak with the
        /// entrance animation only — checks applied up front, no stamp and no old→new scroll.</summary>
        public void Show(int streak) {
            _showing = true;
            _group.alpha = 1f;
            gameObject.SetActive(true);

            _streakOld.text = _streakNew.text = streak.ToString();
            _streakScroll.anchoredPosition = new Vector2(_streakScroll.anchoredPosition.x, 0f);   // new in view
            if (_progress != null) _progress.ApplyImmediately(streak);

            GetComponent<IPopupAnim>()?.PlayIn();
        }

        public void Show(int oldStreak, int newStreak) {
            _showing = true;
            _group.alpha = 1f;
            gameObject.SetActive(true);

            _streakOld.text = oldStreak.ToString();
            _streakNew.text = newStreak.ToString();
            // start with the old number in view + the old streak's checks already on the bar
            _streakScroll.anchoredPosition = new Vector2(_streakScroll.anchoredPosition.x, ScrollFrom);
            if (_progress != null) _progress.ApplyImmediately(oldStreak);

            StartCoroutine(_showFlow(newStreak));
        }

        // Promise-style sequence: play the entrance, then stamp the new check, then scroll old→new.
        IEnumerator _showFlow(int newStreak) {
            var anim = GetComponent<IPopupAnim>();
            anim?.PlayIn();
            if (anim != null) yield return new WaitUntil(() => anim.IsFinished());

            yield return _progress.PlayStamp(newStreak);   // stamp only the newly-earned check
            yield return _scrollY(_streakScroll, ScrollFrom, 0f, 1f);
        }

        // Slide the container's anchored Y from -> to (ease-out), revealing $StreakNew.
        IEnumerator _scrollY(RectTransform rt, float fromY, float toY, float dur) {
            float x = rt.anchoredPosition.x;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime) {
                float p = Mathf.Clamp01(t / dur);
                float e = 1f - (1f - p) * (1f - p);   // ease-out
                rt.anchoredPosition = new Vector2(x, Mathf.Lerp(fromY, toY, e));
                yield return null;
            }
            rt.anchoredPosition = new Vector2(x, toY);
        }

        void _close() {
            _showing = false;
            gameObject.SetActive(false);
        }
    }
}
