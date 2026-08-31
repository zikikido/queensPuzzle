using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Spotlight holes in the black curtain: one pooled CellMask (SpriteMask) per highlighted
    /// cell, positioned and scaled to fit it. The curtain's SpriteRenderer is set to "visible
    /// outside mask", so wherever a mask sits the curtain vanishes — a hole. The cells live in
    /// their own SortingGroup, so the masks can't touch them — only the curtain reacts.
    ///
    /// The curtain is handed in once via SetCurtain (MBToturial.Init does it) and stays hidden;
    /// Show() lifts it and opens holes, Clear() closes them and hides it again.
    /// ShowFor() is the fire-and-forget flavour for hints.
    /// </summary>
    public class MBDrapeHoles : MonoBehaviour {
        const string MaskResource = "GP/CellMask";
        const float HoleMargin = 1.08f;   // hole slightly bigger than the cell
        const float PopDur = 0.2f;
        const float DrapeFadeIn = 0.25f;               // curtain fades, holes pop in (and fade out with it)
        public const float CurtainFadeOut = 0.2f;      // MBToturial delays its sleep by this

        static MBDrapeHoles _i;
        static MBDrapeHoles I => _i != null ? _i : (_i = new GameObject("$DrapeHoles").AddComponent<MBDrapeHoles>());

        GameObject _maskPrefab;
        GameObject _drape;
        SpriteRenderer _drapeSr;
        float _drapeAlpha = 1f;   // the curtain's designed alpha — fades run 0 ↔ this
        Coroutine _drapeFade;
        readonly List<Transform> _live = new List<Transform>();
        readonly Stack<Transform> _pool = new Stack<Transform>();
        Coroutine _autoClear;

        public static void Show(IEnumerable<MBCell> cells) => I.ShowCells(cells);
        public static void ShowFor(IEnumerable<MBCell> cells, float seconds) => I.ShowTimed(cells, seconds);
        public static void Clear() { if (_i != null) _i.CloseAll(); }

        public static void SetCurtain(GameObject curtain) {
            var i = I;
            i._drape = curtain;
            i._drapeSr = curtain != null ? curtain.GetComponent<SpriteRenderer>() : null;
            // capture the designed alpha (skip when handed over mid-fade or blanked for layout)
            if (i._drapeSr != null && i._drapeSr.color.a > 0.01f) i._drapeAlpha = i._drapeSr.color.a;
        }

        void ShowCells(IEnumerable<MBCell> cells) {
            if (_autoClear != null) { StopCoroutine(_autoClear); _autoClear = null; }
            bool wasOn = _drape != null && _drape.activeSelf;
            CloseAll(instant: true);
            if (_drape == null) { Debug.LogWarning("[DrapeHoles] No curtain — call MBToturial.Init() first."); return; }
            _drape.SetActive(true);
            if (!wasOn) SetDrapeAlpha(0f);   // fresh show fades in; growing an open set doesn't blink
            if (_drapeFade != null) StopCoroutine(_drapeFade);
            _drapeFade = StartCoroutine(FadeDrape(_drapeAlpha, DrapeFadeIn, deactivate: false));
            foreach (var cell in cells) OpenHole(cell);
        }

        void ShowTimed(IEnumerable<MBCell> cells, float seconds) {
            ShowCells(cells);
            _autoClear = StartCoroutine(AutoClear(seconds));
        }

        IEnumerator AutoClear(float seconds) {
            yield return new WaitForSecondsRealtime(seconds);
            _autoClear = null;
            CloseAll();
        }

        void OpenHole(MBCell cell) {
            if (_maskPrefab == null) _maskPrefab = Resources.Load<GameObject>(MaskResource);
            if (_maskPrefab == null) { Debug.LogError($"[DrapeHoles] Missing Resources/{MaskResource}"); return; }

            var t = _pool.Count > 0 ? _pool.Pop() : Instantiate(_maskPrefab).transform;
            t.gameObject.SetActive(true);
            t.position = cell.transform.position;

            // scale so the mask sprite covers the cell (in world units) plus a margin
            var sm = t.GetComponent<SpriteMask>();
            float spriteW = sm != null && sm.sprite != null ? sm.sprite.bounds.size.x : 1f;
            float targetW = cell.GetSize().x * cell.transform.lossyScale.x * HoleMargin;
            float scale = targetW / Mathf.Max(spriteW, 0.0001f);

            _live.Add(t);
            StartCoroutine(Pop(t, scale));
        }

        void CloseAll(bool instant = false) {
            if (instant || _drape == null || !_drape.activeSelf) {
                foreach (var t in _live) if (t != null) Recycle(t);
                _live.Clear();
                if (_drape != null) {
                    if (_drapeFade != null) { StopCoroutine(_drapeFade); _drapeFade = null; }
                    _drape.SetActive(false);   // ShowCells re-activates in the same frame
                }
                return;
            }
            // fading close: the holes stay OPEN while the curtain fades away as one piece —
            // shrinking them under a half-faded curtain reads as squares scaling over the cells.
            // FadeDrape recycles the masks once the curtain is gone.
            if (_drapeFade != null) StopCoroutine(_drapeFade);
            _drapeFade = StartCoroutine(FadeDrape(0f, CurtainFadeOut, deactivate: true));
        }

        void SetDrapeAlpha(float a) {
            if (_drapeSr == null) return;
            var c = _drapeSr.color;
            c.a = a;
            _drapeSr.color = c;
        }

        IEnumerator FadeDrape(float to, float dur, bool deactivate) {
            float from = _drapeSr != null ? _drapeSr.color.a : to;
            for (float e = 0f; e < dur; e += Time.unscaledDeltaTime) {
                SetDrapeAlpha(Mathf.Lerp(from, to, e / dur));
                yield return null;
            }
            SetDrapeAlpha(to);
            if (deactivate) {
                if (_drape != null) _drape.SetActive(false);
                foreach (var t in _live) if (t != null) Recycle(t);   // holes held open through the fade
                _live.Clear();
            }
            _drapeFade = null;
        }

        void Recycle(Transform t) {
            t.gameObject.SetActive(false);
            _pool.Push(t);
        }

        IEnumerator Pop(Transform t, float target) {
            for (float e = 0f; e < PopDur; e += Time.unscaledDeltaTime) {
                t.localScale = Vector3.one * (target * EaseOutBack(e / PopDur));
                yield return null;
            }
            t.localScale = Vector3.one * target;
        }

        static float EaseOutBack(float x) {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
