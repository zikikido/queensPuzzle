using Common;
using System.Collections;
using UnityEngine;

namespace qp {
    public class MBCell : MonoBehaviour {

        // Controller state names on QueenController — Play matches them case-insensitively.
        public enum QueenState {
            IDLE,
            HAPPY,
            CRY,
            DISAPPOINTED
        }

        public enum ECellType {
            EMPTY,
            QUEEN,
            X,
            WRONG_QUEEN
        }

        const string ResourcePath = "GP/Cell";

        public static MBCell LoadFromResource() {
            return Resources.Load<MBCell>(ResourcePath);
        }

        public int X { get; private set; }            // column
        public int Y { get; private set; }            // row
        public ECellType State { get; private set; }
        public bool IsSolutionQueen { get; private set; }   // a queen belongs here in the solution

        MBSpriteFlipbook _queen;
        GameObject _xGo, _wrongQueenGo;
        SpriteRenderer _cellSprite;
        Coroutine _markAnim;
        Coroutine _pulseAnim;

        const float PopDur = 0.15f;     // mark "stamp" in
        const float ShrinkDur = 0.1f;   // mark shrink out (erase)

        public Vector2 GetSize() {
            return (transform as RectTransform).sizeDelta;
        }

        public void Init(int clrindx, int xindex, int yindex, bool hasQueen) {
            X = xindex;
            Y = yindex;
            IsSolutionQueen = hasQueen;
            _cellSprite = transform.RecursiveFindChild<SpriteRenderer>("$CellSprite");
            _cellSprite.color = SORegionsColors.ColorAt(clrindx);
            _xGo = transform.RecursiveFindChild("$X").gameObject;
            _queen = transform.RecursiveFindChild<MBSpriteFlipbook>("$Queen");
            _wrongQueenGo = transform.RecursiveFindChild("$WrongQueen").gameObject;

            _xGo.SetActive(false);
            _queen.gameObject.SetActive(false);
            _wrongQueenGo.SetActive(false);
            State = ECellType.EMPTY;
        }

        /// <summary>Play a queen animation — only when this cell actually shows a queen.</summary>
        public void PlayQueen(QueenState state) {
            if (State != ECellType.QUEEN || !_queen.gameObject.activeSelf) return;
            _queen.Play(state.ToString());
        }

        /// <summary>Length of a state's clip in seconds (for timing the win/fail popups).</summary>
        public float GetStateLength(QueenState state) =>
            _queen != null ? _queen.StateLength(state.ToString()) : 0f;

        public void MarkCell(ECellType type) {
            var prev = State;
            State = type;

            if (_markAnim != null) { StopCoroutine(_markAnim); _markAnim = null; }

            var target = OverlayFor(type);
            if (target == null) {
                // -> EMPTY: shrink out whatever mark is showing
                var showing = OverlayFor(prev);
                if (showing != null) _markAnim = StartCoroutine(ShrinkOut(showing));
            } else {
                // show the new mark (hide the others) and stamp it in
                _xGo.SetActive(target == _xGo);
                _queen.gameObject.SetActive(target == _queen.gameObject);
                _wrongQueenGo.SetActive(target == _wrongQueenGo);
                _markAnim = StartCoroutine(PopIn(target.transform));
            }
        }

        GameObject OverlayFor(ECellType type) {
            switch (type) {
                case ECellType.X:           return _xGo;
                case ECellType.QUEEN:       return _queen.gameObject;
                case ECellType.WRONG_QUEEN: return _wrongQueenGo;
                default:                    return null;   // EMPTY
            }
        }

        IEnumerator PopIn(Transform t) {
            t.localScale = Vector3.zero;
            for (float e = 0f; e < PopDur; e += Time.unscaledDeltaTime) {
                t.localScale = Vector3.one * EaseOutBack(e / PopDur);
                yield return null;
            }
            t.localScale = Vector3.one;
            _markAnim = null;
        }

        IEnumerator ShrinkOut(GameObject go) {
            var t = go.transform;
            float start = Mathf.Max(t.localScale.x, 0.0001f);
            for (float e = 0f; e < ShrinkDur; e += Time.unscaledDeltaTime) {
                t.localScale = Vector3.one * Mathf.Lerp(start, 0f, e / ShrinkDur);
                yield return null;
            }
            go.SetActive(false);
            t.localScale = Vector3.one;   // reset for the next time it's shown
            _markAnim = null;
        }

        // overshoot ease (matches the board bloom): 0 -> past 1 -> settles to 1
        static float EaseOutBack(float x) {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }

        public void SetAlpha(float a) {
            var c = _cellSprite.color;
            c.a = a;
            _cellSprite.color = c;
        }

        // Draw the eye to this cell (used by the hint boost): a couple of decaying scale bumps.
        public void Pulse() {
            if (_pulseAnim != null) StopCoroutine(_pulseAnim);
            _pulseAnim = StartCoroutine(PulseRoutine());
        }

        IEnumerator PulseRoutine() {
            const float dur = 0.6f;
            var t = transform;
            for (float e = 0f; e < dur; e += Time.unscaledDeltaTime) {
                float k = e / dur;
                float s = 1f + 0.2f * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 2f)) * (1f - k);
                t.localScale = Vector3.one * s;
                yield return null;
            }
            t.localScale = Vector3.one;
            _pulseAnim = null;
        }
    }
}

