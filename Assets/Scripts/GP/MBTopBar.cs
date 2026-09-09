using Common;
using System;
using System.Collections;
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
        private GameObject[][] _boneRows;    // lives, one row per bottom layout — [row][0] is the first to disappear
        private Transform[] _boneRoots;      // the rows' containers, same order as _boneRows

        // How many wrong moves end the level (one bone each).
        public int MaxWrongMoves => _boneRows[0].Length;

        private GameObject _withoutTime, _withTime;   // bottom row: campaign layout / daily layout with the timer
        private TMPro.TMP_Text _timeProgress;
        private MBLevelCenterizer _levelCenterizer;   // keeps "Level <n>" centered as the digit count changes

        private GameObject _levelStrech, _dateStrech;   // top row: "Level <n>" (campaign) / the date (daily)
        private TMPro.TMP_Text _levelText, _dateText;

        // The three rule cards' backgrounds ($RuleTouch / $RuleRowCol / $RuleColor → their BG image)
        private Dictionary<QueensPuzzle.Rule, Image> _ruleBGs;
        private Coroutine _ruleBlink;
        private Image _blinkingBG;
        private Color _blinkingBGColor;   // the BG's own color, restored after the blink

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

            var levelStrech = transform.RecursiveFindChild("%LevelStrech");
            var dateStrech = transform.RecursiveFindChild("%DateStrech");
            _levelStrech = levelStrech.gameObject;
            _dateStrech = dateStrech.gameObject;
            _levelText = levelStrech.RecursiveFindChild<TMPro.TMP_Text>("$LevelText");
            _dateText = dateStrech.RecursiveFindChild<TMPro.TMP_Text>("$Date");

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

            // Bones = lives. Each bottom layout carries its own bone row — collect both so a
            // wrong move hides a bone in whichever layout is visible. Rightmost lost first.
            _boneRoots = new[] { withoutTime.RecursiveFindChild("$Bones"), withTime.RecursiveFindChild("Bones") };
            _boneRows = Array.ConvertAll(_boneRoots, _collectBones);

            _ruleBGs = new Dictionary<QueensPuzzle.Rule, Image> {
                { QueensPuzzle.Rule.Touch,  transform.RecursiveFindChild("$RuleTouch").RecursiveFindChild<Image>("$RuleBG") },
                { QueensPuzzle.Rule.RowCol, transform.RecursiveFindChild("$RuleRowCol").RecursiveFindChild<Image>("$RuleBG") },
                { QueensPuzzle.Rule.Color,  transform.RecursiveFindChild("$RuleColor").RecursiveFindChild<Image>("$RuleBG") },
            };
        }

        // Red flicker on the card of the rule the player just broke — three quick pulses.
        public void BlinkRule(QueensPuzzle.Rule rule) {
            if (!_ruleBGs.TryGetValue(rule, out var bg) || bg == null) return;
            if (_ruleBlink != null) {
                StopCoroutine(_ruleBlink);
                _blinkingBG.color = _blinkingBGColor;   // a new blink never inherits a half-red card
            }
            _blinkingBG = bg;
            _blinkingBGColor = bg.color;
            _ruleBlink = StartCoroutine(_blinkRule(bg, _blinkingBGColor));
        }

        IEnumerator _blinkRule(Image bg, Color baseColor) {
            const int pulses = 3;
            const float pulseDur = 0.3f;
            var red = new Color(1f, 0.25f, 0.25f, baseColor.a);
            for (float e = 0f; e < pulses * pulseDur; e += Time.unscaledDeltaTime) {
                float k = Mathf.Abs(Mathf.Sin(e / pulseDur * Mathf.PI));   // 0 → 1 → 0 per pulse
                bg.color = Color.Lerp(baseColor, red, k);
                yield return null;
            }
            bg.color = baseColor;
            _ruleBlink = null;
        }

        private static GameObject[] _collectBones(Transform bonesRoot) {
            var bones = new List<Transform>();
            foreach (Transform child in bonesRoot)
                if (child.name.StartsWith("$Bone")) bones.Add(child);
            bones.Sort((a, b) => b.localPosition.x.CompareTo(a.localPosition.x));
            return bones.ConvertAll(t => t.gameObject).ToArray();
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

            _levelStrech.SetActive(!showTimeProgress);
            _dateStrech.SetActive(showTimeProgress);

            if (showTimeProgress) {
                _dateText.text = DailyChallengeManager.NiceDate;
            } else {
                var levelText = (AppData.LevelIdx + 1).ToString();
                _levelText.text = levelText;
                _levelCenterizer.Recenter(levelText);
            }

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
            foreach (var row in _boneRows)
                for (int i = 0; i < row.Length; i++)
                    row[i].SetActive(i >= wrong);
        }


        private void _setQueensProgress(int done) {
            _queensProgressText.text = $"<color=#{_foundHex}>{done}</color>/{_queensInBoard}";
        }

        internal void SetInteractable(bool on) {
            _canvasGroup.interactable = on;
        }

        public Transform GetBonesTransform() {
            return _withTime.activeSelf ? _boneRoots[1] : _boneRoots[0];   // the visible row
        }
    }
}

