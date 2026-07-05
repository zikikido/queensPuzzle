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
        const float CloseDur = 0.1f;

        static MBDrapeHoles _i;
        static MBDrapeHoles I => _i != null ? _i : (_i = new GameObject("$DrapeHoles").AddComponent<MBDrapeHoles>());

        GameObject _maskPrefab;
        GameObject _drape;
        readonly List<Transform> _live = new List<Transform>();
        readonly Stack<Transform> _pool = new Stack<Transform>();
        Coroutine _autoClear;

        public static void Show(IEnumerable<MBCell> cells) => I.ShowCells(cells);
        public static void ShowFor(IEnumerable<MBCell> cells, float seconds) => I.ShowTimed(cells, seconds);
        public static void Clear() { if (_i != null) _i.CloseAll(); }

        public static void SetCurtain(GameObject curtain) {
            var i = I;
            i._drape = curtain;
        }

        void ShowCells(IEnumerable<MBCell> cells) {
            if (_autoClear != null) { StopCoroutine(_autoClear); _autoClear = null; }
            CloseAll(instant: true);
            if (_drape == null) { Debug.LogWarning("[DrapeHoles] No curtain — call MBToturial.Init() first."); return; }
            _drape.SetActive(true);
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
            foreach (var t in _live) {
                if (t == null) continue;
                if (instant) Recycle(t);
                else StartCoroutine(Shrink(t));
            }
            _live.Clear();
            if (_drape != null) _drape.SetActive(false);
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

        IEnumerator Shrink(Transform t) {
            float start = t.localScale.x;
            for (float e = 0f; e < CloseDur; e += Time.unscaledDeltaTime) {
                t.localScale = Vector3.one * Mathf.Lerp(start, 0f, e / CloseDur);
                yield return null;
            }
            Recycle(t);
        }

        static float EaseOutBack(float x) {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
