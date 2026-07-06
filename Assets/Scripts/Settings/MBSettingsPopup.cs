using Common;
using Puzzby;
using System;
using System.Collections;
using System.Collections.Generic;
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

        /// <summary>Fired after the out animation finished and the popup deactivated itself.</summary>
        public event Action Closed;

        MBSwitch _sound, _vibration;
        Button _bg;              // $BG — fades
        Transform _popup;        // $Popup — pops
        CanvasGroup _popupGroup; // fades with the pop, so no scaling from zero
        Vector3 _popupScale;     // design-time scale, animations run relative to it
        Coroutine _anim;
        bool _initialized;       // layout pass done
        bool _openRequested;     // Open() was called — this activation is a real open

        /// <summary>The one way to open the popup (the top bar) — plays the in animation.</summary>
        public void Open() {
            _openRequested = true;
            gameObject.SetActive(true);
            // Start() runs only once — if the layout pass never completed, finish it here
            if (!_initialized) StartCoroutine(LateFirstOpen());
        }

        IEnumerator LateFirstOpen() {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _initialized = true;
            if (_openRequested) { _openRequested = false; PlayIn(); }
        }

        // Closed by default: stay active-but-invisible for the first frames so the UI lays out
        // at real size, then self-hide until Open().
        IEnumerator Start() {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _initialized = true;
            if (_openRequested) { _openRequested = false; PlayIn(); }
            else if (_anim == null) gameObject.SetActive(false);
        }

        void PlayIn() {
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(In());
        }

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

            // invisible during the layout pass — Start() hides the object right after
            (_bg.targetGraphic as Image).SetAlpha(0f);
            _popupGroup.alpha = 0f;

            
        }

        void OnEnable() {
            _sound?.Init(MBSFX.Instance == null || !MBSFX.Instance.Mute, on => {
                RegisterCodeTap(1);
                if (MBSFX.Instance != null) MBSFX.Instance.Mute = !on;
            });

            _vibration?.Init(AppData.Haptics.Value, on => {
                RegisterCodeTap(2);
                AppData.Haptics.Value = on;
                Haptics.Enabled = on;
                if (on) Haptics.Play(GameHaptic.Tap);   // instant proof it's back on
            });

            if (_initialized && _openRequested) { _openRequested = false; PlayIn(); }
        }

        // ---- debug-mode codes: Sound = 1, Vibration = 2 (tapped in this popup) --------------
        // Not in debug mode: the long code (max 2s between taps) turns AppData.DebugMode on.
        // In debug mode: the short code within 2s total opens the debug window.
        static readonly int[] EnterCode = { 1, 1, 2, 2, 1, 2, 1, 2, 1, 1, 2, 2 };
        static readonly int[] OpenCode = { 1, 1, 2, 2 };
        const float CodeTapGap = 2f;
        readonly List<(int id, float time)> _codeTaps = new List<(int, float)>();

        void RegisterCodeTap(int id) {
            _codeTaps.Add((id, Time.unscaledTime));
            if (_codeTaps.Count > EnterCode.Length) _codeTaps.RemoveAt(0);

            if (!AppData.DebugMode.Value) {
                if (TailMatches(EnterCode) && GapsWithin(EnterCode.Length, CodeTapGap)) {
                    AppData.DebugMode.Value = true;
                    Haptics.Play(GameHaptic.Happy);   // you're in — the OnGUI badge shows it too
                }
            }
            else if (TailMatches(OpenCode) && TotalWithin(OpenCode.Length, CodeTapGap)) {
                MBDebugWin.Open();
            }
        }

        bool TailMatches(int[] code) {
            if (_codeTaps.Count < code.Length) return false;
            for (int i = 0; i < code.Length; i++)
                if (_codeTaps[_codeTaps.Count - code.Length + i].id != code[i]) return false;
            return true;
        }

        bool GapsWithin(int count, float maxGap) {
            for (int i = _codeTaps.Count - count + 1; i < _codeTaps.Count; i++)
                if (_codeTaps[i].time - _codeTaps[i - 1].time > maxGap) return false;
            return true;
        }

        bool TotalWithin(int count, float window) =>
            _codeTaps[_codeTaps.Count - 1].time - _codeTaps[_codeTaps.Count - count].time <= window;

        // "you are in debug mode" badge while the popup is up (IMGUI: no prefab change needed)
        void OnGUI() {
            if (!AppData.DebugMode.Value) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * Mathf.Max(1f, Screen.dpi / 96f)), alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.red;
            GUI.Label(new Rect(0, 10, Screen.width, style.fontSize * 2), "DEBUG MODE", style);
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
            Closed?.Invoke();
        }

        static float EaseOutBack(float x) {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
