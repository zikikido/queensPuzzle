using Common;
using Puzzby;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    /// <summary>
    /// The settings popup: two MBSwitches wired to the engines (Sounds → MBSFX mute,
    /// Vibration → Haptics.Enabled + AppData.Haptics), with in/out animations on $BG (fade)
    /// and $Popup (scale pop). Default is OUT — keep the object inactive in the scene;
    /// SetActive(true) plays the in animation, Close() plays out and deactivates again.
    /// Tapping $BG also closes.
    /// </summary>
    public class MBSettingsPopup : MonoBehaviour {

        const float InDur = 0.3f, OutDur = 0.15f, FadeDur = 0.2f;
        const float BgAlpha = 0.8f;   // the dark cover's resting alpha

        MBSwitch _sound, _vibration;
        Button _bg;              // $BG — fades
        Transform _popup;        // $Popup — pops
        CanvasGroup _popupGroup; // fades with the pop, so no scaling from zero
        Vector3 _popupScale;     // design-time scale, animations run relative to it
        Coroutine _anim;

        void Awake() {
            _sound = transform.RecursiveFindChild("$Sounds").GetComponentInChildren<MBSwitch>();
            _vibration = transform.RecursiveFindChild("$Vibration").GetComponentInChildren<MBSwitch>();

            _bg = transform.RecursiveFindChild<Button>("$BG");
            transform.RecursiveFindChild<Button>("$XButton").onClick.AddListener(Close);
            _bg.onClick.AddListener(Close);

            _popup = transform.RecursiveFindChild("$Popup");
            _popupScale = _popup.localScale;
            _popupGroup = _popup.GetComponent<CanvasGroup>();
            if (_popupGroup == null) _popupGroup = _popup.gameObject.AddComponent<CanvasGroup>();

            
        }

        void OnEnable() {
            _sound?.Init(MBSFX.Instance == null || !MBSFX.Instance.Mute, on => {
                if (MBSFX.Instance != null) MBSFX.Instance.Mute = !on;
            });

            _vibration?.Init(AppData.Haptics.Value, on => {
                AppData.Haptics.Value = on;
                Haptics.Enabled = on;
                if (on) Haptics.Play(GameHaptic.Tap);   // instant proof it's back on
            });

            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(In());
        }

        /// <summary>Play the out animation, then deactivate the popup.</summary>
        public void Close() {
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(Out());
        }

        IEnumerator In() {
            var bgImage = _bg.targetGraphic as Image;
            bgImage.SetAlpha(0f);
            _popup.localScale = _popupScale * 0.7f;   // gentle pop, never from zero
            _popupGroup.alpha = 0f;

            for (float e = 0f; e < InDur; e += Time.unscaledDeltaTime) {
                float k = e / InDur;
                bgImage.SetAlpha(Mathf.Clamp01(e / FadeDur) * BgAlpha);   // ramp straight to 0.8, no snap
                _popup.localScale = _popupScale * Mathf.LerpUnclamped(0.7f, 1f, EaseOutBack(k));
                _popupGroup.alpha = Mathf.Clamp01(e / FadeDur);
                yield return null;
            }

            bgImage.SetAlpha(BgAlpha);
            _popup.localScale = _popupScale;
            _popupGroup.alpha = 1f;
            _anim = null;
        }

        IEnumerator Out() {
            var bgImage = _bg.targetGraphic as Image;
            float startAlpha = bgImage.color.a;
            Vector3 startScale = _popup.localScale;

            for (float e = 0f; e < OutDur; e += Time.unscaledDeltaTime) {
                float k = e / OutDur;
                bgImage.SetAlpha(Mathf.Lerp(startAlpha, 0f, k));
                _popup.localScale = Vector3.Lerp(startScale, _popupScale * 0.85f, k); // slight shrink…
                _popupGroup.alpha = 1f - k;                                           // …the fade does the exit
                yield return null;
            }
            _popup.localScale = _popupScale;    // reset for the next open
            _popupGroup.alpha = 1f;
            bgImage.SetAlpha(BgAlpha);
            _anim = null;
            gameObject.SetActive(false);
        }

        static float EaseOutBack(float x) {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
