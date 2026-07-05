using Common;
using System;
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

        private void Awake() {
            transform.RecursiveFindChild("$QueensProgressText", out _queensProgressText);
            _foundHex = ColorUtility.ToHtmlStringRGB(_queenFoundCLR);

            _canvasGroup = GetComponent<CanvasGroup>();

            _settingsBtn = transform.RecursiveFindChild<Button>("$SettingsBtn");
            _settingsBtn.onClick.AddListener(OpenSettings);

            transform.RecursiveFindChild<Button>("$BackBtn").onClick.AddListener(() => SceneManager.LoadScene("Lobby"));
        }

        void OpenSettings() {
            if (_settings == null) {
                _settings = FindAnyObjectByType<MBSettingsPopup>(FindObjectsInactive.Include);
            }

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


        private void _setQueensProgress(int done) {
            _queensProgressText.text = $"<color=#{_foundHex}>{done}</color>/{_queensInBoard}";
        }

        internal void SetInteractable(bool on) {
            _canvasGroup.interactable = on;
        }
    }
}

