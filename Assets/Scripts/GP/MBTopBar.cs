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

        private void Awake() {
            transform.RecursiveFindChild("$QueensProgressText", out _queensProgressText);
            _foundHex = ColorUtility.ToHtmlStringRGB(_queenFoundCLR);

            _canvasGroup = GetComponent<CanvasGroup>();

            _settingsBtn = transform.RecursiveFindChild<Button>("$SettingsBtn");
            _settingsBtn.onClick.AddListener(OpenSettings);

            transform.RecursiveFindChild<Button>("$BackBtn").onClick.AddListener(() => { Ads.HideBanner(); Navigator.Go(Navigator.Lobby); });

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
                if (child.name.StartsWith("Bone")) bones.Add(child);
            bones.Sort((a, b) => b.localPosition.x.CompareTo(a.localPosition.x));
            _bones = bones.ConvertAll(t => t.gameObject).ToArray();
        }

        void OpenSettings() {
            if (_settings == null) {
                _settings = FindAnyObjectByType<MBSettingsPopup>(FindObjectsInactive.Include);
                _settings.Closed += () => MBGameplay.instance.InputLocks--;   // once, with the find
            }

            MBGameplay.instance.InputLocks++;   // board untouchable while the popup is up
            _settings.Open();   // plays the in animation (plain SetActive would skip it)
        }

        // Set the level's queen target and reset the display to 0/total.
        public void Init(int queensInBoard) {
            _queensInBoard = queensInBoard;
            _setQueensProgress(0);

            transform.RecursiveFindChild<TMPro.TMP_Text>("$LevelText").text = (AppData.LevelIdx + 1).ToString();
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

