using System.Collections;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The in-game streak REWARD popup — shown after a win that reached a milestone (a reward was
    /// granted), before the win popup. (The plain variant is <see cref="MBDailyStreakInfoPopup"/>.)
    /// Caller does <c>Show(streak, reward)</c> then waits on <see cref="IsShowing"/>; $%ContinueButton
    /// closes it. No anims yet — just fills the streak, reward icon/amount, and the bar.
    /// </summary>
    public class MBDailyStreakInGameRewordedPopup : MonoBehaviour {

        // The streak number container starts scrolled up by this (old in view) and slides to 0 (new).
        const float ScrollFrom = -158f;

        CanvasGroup _group;
        bool _showing;              // a real show is on screen (guards the layout pass from hiding it)

        TMP_Text _streakOld, _streakNew;                 // $StreakOld / $StreakNew
        RectTransform _streakScroll;                     // $StreakContainer — their shared parent
        Image _icon;                                     // $Icon — the reward boost sprite
        TMP_Text _amountText;                            // $AmountText — "+2"
        MBDailyStreakProgress _progress;                 // the bar widget in %DailyStreakProgressContainer

        MBDailyStreakInGameRewordedPopupAnim _anim;      // reskin animator driver (Show + Reward states)
        CanvasGroup _amount1, _amount;                   // $%1 / $%Amount — faded in by the reward clip
        Transform _poof;                                 // $CFXR Magic Poof — played by code at phase 3

        /// <summary>True while the popup is open — the win flow waits on this before continuing.</summary>
        public bool IsShowing => _showing;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass

            _streakScroll = transform.RecursiveFindChild<RectTransform>("$StreakContainer");
            _streakOld = _streakScroll.RecursiveFindChild<TMP_Text>("$StreakOld");
            _streakNew = _streakScroll.RecursiveFindChild<TMP_Text>("$StreakNew");
            _icon = transform.RecursiveFindChild<Image>("$Icon");
            _amountText = transform.RecursiveFindChild<TMP_Text>("$AmountText");
            _progress = GetComponentInChildren<MBDailyStreakProgress>(true);

            _anim = GetComponent<MBDailyStreakInGameRewordedPopupAnim>();
            _amount1 = transform.RecursiveFindChild<CanvasGroup>("$%1");
            _amount = transform.RecursiveFindChild<CanvasGroup>("$%Amount");
            _poof = transform.RecursiveFindChild("$CFXR Magic Poof");

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

        public void Show(int oldStreak, int newStreak, Reward reward) {
            _showing = true;
            _group.alpha = 1f;
            gameObject.SetActive(true);

            _streakOld.text = oldStreak.ToString();
            _streakNew.text = newStreak.ToString();
            // start with the old number in view + the old streak's checks already on the bar
            _streakScroll.anchoredPosition = new Vector2(_streakScroll.anchoredPosition.x, ScrollFrom);
            if (_progress != null) _progress.ApplyImmediately(oldStreak);

            if (reward != null) {
                if (_icon != null) _icon.sprite = DailyStreakManager.SpriteFor(reward.type);
                if (_amountText != null) _amountText.text = reward.Label;
            }
            // the reward stays hidden until phase 3 fades it in
            if (_amount1 != null) _amount1.alpha = 0f;
            if (_amount != null) _amount.alpha = 0f;

            StartCoroutine(_showFlow(newStreak));
        }

        // Promise-style sequence: entrance → stamp the completing check → scroll old→new → reward reveal.
        IEnumerator _showFlow(int newStreak) {
            _anim?.PlayIn();
            if (_anim != null) yield return new WaitUntil(() => _anim.IsFinished());

            if (_progress != null) yield return _progress.PlayStamp(newStreak);
            yield return _scrollY(_streakScroll, ScrollFrom, 0f, 1f);

            // Phase 3: the reward pops from the gift — poof (code), then the reveal clip opens the
            // gift and fades in $%1 / $%Amount.
            _playPoof();
            _anim?.PlayReward();
        }

        void _playPoof() {
            var sfx = GPSFX.Instance.RewordOpen;
            if (sfx != null) CommonSFX.Play(sfx);

            if (_poof == null) return;
            var uip = _poof.GetComponent<Coffee.UIExtensions.UIParticle>();
            if (uip != null) { uip.Play(); return; }
            var ps = _poof.GetComponent<ParticleSystem>();
            if (ps != null) ps.Play();
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
