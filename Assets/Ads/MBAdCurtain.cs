using System;
using System.Collections;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Fullscreen black curtain drawn around fullscreen ads. It fades IN before the ad is shown and
    /// only fades OUT a couple of frames after the ad callback did its work (scene load, popup swap).
    /// MAX's hidden-callback can arrive noticeably late, so without it the player sees the pre-ad
    /// screen again for a moment before the new one appears.
    ///
    /// Lives in Assets/Ads/Resources/$AdCurtain.prefab — instantiated once by Ads.Init() and made
    /// DontDestroyOnLoad, so it stays on screen across the SceneManager.LoadScene the callback
    /// usually triggers. Only Ads.cs uses it.
    /// </summary>
    public class MBAdCurtain : MonoBehaviour {

        const float FadeInSec = 0.2f;
        const float FadeOutSec = 0.25f;

        // Failsafe for a show() that silently does nothing: no displayed, no failed, no hidden — and
        // the screen would stay black forever. Armed on fade-in, disarmed the moment the ad is
        // actually on screen (AdDisplayed), so a long rewarded video never trips it.
        const float PreDisplayTimeoutSec = 8f;

        static MBAdCurtain _instance;

        [SerializeField] CanvasGroup _group;   // on the prefab root: alpha 0, blocksRaycasts off
        Coroutine _fade, _safety;

        public static MBAdCurtain Instance {
            get {
                if (_instance == null) _instance = _create();
                return _instance;
            }
        }

        /// <summary>Cover the screen, then run <paramref name="onCovered"/> (where the ad is shown).</summary>
        public void FadeIn(Action onCovered) {
            _stopFade();
            _group.blocksRaycasts = true;   // eat taps while the curtain is up
            _fade = StartCoroutine(_fadeTo(1f, FadeInSec, onCovered));

            _stopSafety();
            _safety = StartCoroutine(_preDisplayGuard());
        }

        /// <summary>Reveal whatever is behind the curtain by now.</summary>
        public void FadeOut() {
            _stopSafety();
            _stopFade();
            _fade = StartCoroutine(_fadeTo(0f, FadeOutSec, () => _group.blocksRaycasts = false));
        }

        /// <summary>The ad is really on screen — the pre-display failsafe is no longer needed.</summary>
        public void AdDisplayed() => _stopSafety();

        static MBAdCurtain _create() {
            var go = Instantiate(Resources.Load<GameObject>("$AdCurtain"));
            go.name = "$AdCurtain";
            DontDestroyOnLoad(go);
            return go.GetComponent<MBAdCurtain>();
        }

        // Unscaled: ads pause the game, and the curtain must animate either way.
        IEnumerator _fadeTo(float target, float dur, Action onDone) {
            float from = _group.alpha;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime) {
                _group.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(t / dur));
                yield return null;
            }
            _group.alpha = target;
            _fade = null;
            onDone?.Invoke();
        }

        IEnumerator _preDisplayGuard() {
            yield return new WaitForSecondsRealtime(PreDisplayTimeoutSec);
            _safety = null;
            Debug.LogWarning("[Ads] curtain: no ad displayed in time — dropping it");
            FadeOut();
        }

        void _stopFade() { if (_fade != null) { StopCoroutine(_fade); _fade = null; } }
        void _stopSafety() { if (_safety != null) { StopCoroutine(_safety); _safety = null; } }
    }
}
