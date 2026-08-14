using System;
using System.Collections;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The in-game streak REWARD popup — shown after a win that reached a milestone (a reward was
    /// granted), before the win popup. (The plain variant is <see cref="MBDailyStreakInGamePopup"/>.)
    /// Caller does <c>Show(streak, reward)</c> then waits on <see cref="IsShowing"/>; $ContinueButton
    /// closes it. No anims — just fill the streak, reward icon/amount, and the tier's gift box.
    /// </summary>
    public class MBDailyStreakInGameRewordedPopup : MonoBehaviour {

        CanvasGroup _group;
        bool _showing;              // a real show is on screen (guards the layout pass from hiding it)

        TMP_Text _streakText;                            // $Streak
        Image _icon;                                     // $Icon — the reward boost sprite
        TMP_Text _amountText;                            // $AmountText — "x2"
        GameObject _boxSmall, _boxMedium, _boxLarge;     // $GiftBoxIcon* — the big box, by tier
        MBDailyStreakProgress _progress;                 // the bar widget in DailyStreakProgressContainer

        /// <summary>True while the popup is open — the win flow waits on this before continuing.</summary>
        public bool IsShowing => _showing;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass

            _streakText = transform.RecursiveFindChild<TMP_Text>("$Streak");
            _icon = transform.RecursiveFindChild<Image>("$Icon");
            _amountText = transform.RecursiveFindChild<TMP_Text>("$AmountText");
            _boxSmall = transform.RecursiveFindChild("$GiftBoxIconSmall")?.gameObject;
            _boxMedium = transform.RecursiveFindChild("$GiftBoxIconMeduim")?.gameObject;   // (spelling as authored)
            _boxLarge = transform.RecursiveFindChild("$GiftBoxIconLarge")?.gameObject;
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

        public void Show(int streak, Reward reward) {
            _showing = true;
            _group.alpha = 1f;
            gameObject.SetActive(true);

            if (_streakText != null) _streakText.text = streak.ToString();
            if (_progress != null) _progress.ApplyImmediately(streak);

            if (reward != null) {
                if (_icon != null) _icon.sprite = DailyStreakManager.SpriteFor(reward.type);
                if (_amountText != null) _amountText.text = reward.Label;
            }

            // The big gift box, sized to which milestone this is (tier = index of the matching day).
            int tier = Array.IndexOf(DailyStreakManager.Milestones, streak);
            if (_boxSmall != null) _boxSmall.SetActive(tier == 0);
            if (_boxMedium != null) _boxMedium.SetActive(tier == 1);
            if (_boxLarge != null) _boxLarge.SetActive(tier == 2);
        }

        void _close() {
            _showing = false;
            gameObject.SetActive(false);
        }
    }
}
