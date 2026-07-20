using Common;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace qp {
    public class MBTopBar : MonoBehaviour {

        TMPro.TMP_Text _queensProgressText;

        [SerializeField]
        Color _queenFoundCLR ;
        private int _queensInBoard;
        private string _foundHex;
        private CanvasGroup _canvasGroup;
        private Button _settingsBtn;
        private MBSettingsPopup _settings;   // inactive in the scene by default (its OUT state)
        private GameObject[] _bones;         // lives — ordered so [0] is the first to disappear

        // How many wrong moves end the level (one bone each).
        public int MaxWrongMoves => _bones.Length;

        private GameObject _withoutTime, _withTime;   // bottom row: campaign layout / daily layout with the timer
        private TMPro.TMP_Text _timeProgress;
        private MBLevelCenterizer _levelCenterizer;   // keeps "Level <n>" centered as the digit count changes

        private void Awake() {
            transform.RecursiveFindChild("$QueensProgressText", out _queensProgressText);
            _foundHex = ColorUtility.ToHtmlStringRGB(_queenFoundCLR);

            _canvasGroup = GetComponent<CanvasGroup>();

            var withoutTime = transform.RecursiveFindChild("$WithoutTime");
            var withTime = transform.RecursiveFindChild("$WithTime");
            _withoutTime = withoutTime.gameObject;
            _withTime = withTime.gameObject;
            _timeProgress = withTime.transform.RecursiveFindChild<TMPro.TMP_Text>("$TimeProgress");

            _levelCenterizer = GetComponentInChildren<MBLevelCenterizer>(true);

            _settingsBtn = transform.RecursiveFindChild<Button>("$SettingsBtn");
            _settingsBtn.onClick.AddListener(OpenSettings);

            transform.RecursiveFindChild<Button>("$BackBtn").onClick.AddListener(() => {
                DailyChallengeManager.ExitDaily();   // leaving GP ends the daily run (the day's save stays)
                Ads.HideBanner();
                Navigator.Go(Navigator.Lobby);
            });

            // interactable=false (CanvasGroup) only blocks clicks — no gray-out on our buttons
            foreach (var btn in GetComponentsInChildren<Button>(true)) {
                var colors = btn.colors;
                colors.disabledColor = colors.normalColor;
                btn.colors = colors;
            }

            // Bones = lives. Collect them under the "Bones" container, rightmost lost first.
            var bonesRoot = transform.RecursiveFindChild("Bones");
            var bones = new List<Transform>();
            foreach (Transform child in bonesRoot)
                if (child.name.StartsWith("$Bone")) bones.Add(child);
            bones.Sort((a, b) => b.localPosition.x.CompareTo(a.localPosition.x));
            _bones = bones.ConvertAll(t => t.gameObject).ToArray();
        }

        bool _settingsLockHeld;   // pairs the ++/-- exactly once per open, whatever the popup does

        void OpenSettings() {
            if (_settings == null) {
                _settings = FindAnyObjectByType<MBSettingsPopup>(FindObjectsInactive.Include);
                _settings.Closed += () => {   // once, with the find
                    if (_settingsLockHeld) { _settingsLockHeld = false; MBGameplay.instance.InputLocks--; }
                };
            }

            // board untouchable while the popup is up — the held flag makes a double Open (or a
            // double Closed) unable to leak the lock or drive it negative
            if (!_settingsLockHeld) { _settingsLockHeld = true; MBGameplay.instance.InputLocks++; }
            _settings.Open();   // plays the in animation (plain SetActive would skip it)
        }

        // Set the level's queen target and reset the display to 0/total. A daily run shows the
        // date instead of a level number and swaps the bottom row to the timer layout.
        public void Init(int queensInBoard, bool showTimeProgress) {
            _queensInBoard = queensInBoard;
            _setQueensProgress(0);

            var levelText = showTimeProgress ? DailyChallengeManager.NiceDate : (AppData.LevelIdx + 1).ToString();
            transform.RecursiveFindChild<TMPro.TMP_Text>("$LevelText").text = levelText;
            _levelCenterizer.Recenter(levelText);

            _withoutTime.SetActive(!showTimeProgress);
            _withTime.SetActive(showTimeProgress);
            if (showTimeProgress) SetTimeProgress(DailyChallengeManager.State.timeSec);
        }

        // The daily timer readout — MBGameplay ticks it once per second while the board plays.
        public void SetTimeProgress(float seconds) {
            if (_timeProgress != null) _timeProgress.text = DailyChallengeManager.FormatTime(seconds);
        }

        // How many queens are correctly placed right now.
        public void SetProgress(int done) => _setQueensProgress(done);

        // One bone gone per wrong move — called on every wrong move AND on level load/restore.
        public void SetWrongMoves(int wrong) {
            for (int i = 0; i < _bones.Length; i++)
                _bones[i].SetActive(i >= wrong);
        }


        private void _setQueensProgress(int done) {
            _queensProgressText.text = $"<color=#{_foundHex}>{done}</color>/{_queensInBoard}";
        }

        internal void SetInteractable(bool on) {
            _canvasGroup.interactable = on;
        }
    }
}

