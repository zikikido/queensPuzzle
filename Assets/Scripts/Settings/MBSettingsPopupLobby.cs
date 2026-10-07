using Common;
using TMPro;
using UnityEngine;

namespace qp {
    /// <summary>
    /// The lobby settings popup: the base popup plus the name other players see on the
    /// tournament leaderboard ($ChangeName).
    ///
    /// Validation is LIVE — every keystroke runs the same <see cref="ProfileManager.Validate"/>
    /// the save will, so the player is told while they are still typing instead of after they
    /// commit. Too long cannot be reported at all, because the field refuses the character:
    /// a limit is better kept than explained.
    ///
    /// Saving happens on close, whichever way the popup was dismissed, and only if the name is
    /// both changed and valid. An invalid edit is dropped rather than blocking the way out —
    /// the error has been on screen the whole time, and a popup that will not close over a
    /// cosmetic field is a worse bargain than a discarded edit. The old name always survives,
    /// so the player is never left without one.
    /// </summary>
    public class MBSettingsPopupLobby : MBSettingsPopup {

        TMP_InputField _name;
        TMP_Text _error;

        protected override void Awake() {
            base.Awake();

            _name = transform.RecursiveFindChild("$ChangeName").GetComponentInChildren<TMP_InputField>(true);
            _error = transform.RecursiveFindChild("$Error").GetComponent<TMP_Text>();

            var cfg = ProfileConfig.Instance;
            if (cfg != null) _name.characterLimit = cfg.maxNameLength;

            _name.onValueChanged.AddListener(_ => _show(ProfileManager.Validate(_name.text)));
        }

        protected override void OnEnable() {
            base.OnEnable();

            // Open on what is actually in use, not on whatever was typed and dropped last time.
            _name.SetTextWithoutNotify(ProfileManager.Name);
            _show(ENameError.Ok);
        }

        public override void Close() {
            _save();
            base.Close();
        }

        /// <summary>Keep the edit if it is one, and if it is allowed. <see cref="ProfileManager"/>
        /// re-checks it either way — this side of the glass can always be lied to.</summary>
        void _save() {
            if (_name == null) return;

            string typed = (_name.text ?? "").Trim();
            if (typed == ProfileManager.Name) return;        // untouched, or typed back to itself

            ProfileManager.SetName(typed);                   // refuses anything Validate refuses
        }

        /// <summary>
        /// One short line, or nothing at all. Short because it sits under a single field and
        /// should read as a correction rather than announce itself; the lengths come from the
        /// config so the text cannot drift from the rule it describes.
        /// </summary>
        void _show(ENameError error) {
            var cfg = ProfileConfig.Instance;
            switch (error) {
                case ENameError.Ok:            _error.text = ""; break;
                case ENameError.TooShort:      _error.text = $"At least {cfg?.minNameLength ?? 4} characters"; break;
                case ENameError.TooLong:       _error.text = $"Up to {cfg?.maxNameLength ?? 14} characters"; break;
                case ENameError.BadCharacters: _error.text = "Letters and numbers only"; break;
                case ENameError.NotAllowed:    _error.text = "Pick another name"; break;
            }
        }
    }
}
