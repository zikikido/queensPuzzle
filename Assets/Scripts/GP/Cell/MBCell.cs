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

        MBQueenMark _queenMark;
        MBABSMarkAnim _xMark, _wrongQueenMark;
        SpriteRenderer _cellSprite;
        Coroutine _pulseAnim;

        public Vector2 GetSize() {
            return (transform as RectTransform).sizeDelta;
        }

        public void Init(int clrindx, int xindex, int yindex, bool hasQueen) {
            X = xindex;
            Y = yindex;
            IsSolutionQueen = hasQueen;
            _cellSprite = transform.RecursiveFindChild<SpriteRenderer>("$CellSprite");
            _cellSprite.color = SORegionsColors.ColorAt(clrindx);
            _xMark = transform.RecursiveFindChild<MBABSMarkAnim>("$X");
            _queenMark = transform.RecursiveFindChild<MBQueenMark>("$Queen");
            _wrongQueenMark = transform.RecursiveFindChild<MBABSMarkAnim>("$WrongQueen");

            _xMark.InitOut();
            _queenMark.InitOut();
            _wrongQueenMark.InitOut();
            State = ECellType.EMPTY;
        }

        /// <summary>Play a queen animation — only when this cell actually shows a queen.</summary>
        public void PlayQueen(QueenState state) {
            if (State != ECellType.QUEEN || !_queenMark.gameObject.activeSelf) return;
            _queenMark.Play(state.ToString());
        }

        /// <summary>Length of a state's clip in seconds (for timing the win/fail popups).</summary>
        public float GetStateLength(QueenState state) =>
            _queenMark != null ? _queenMark.StateLength(state.ToString()) : 0f;

        public void MarkCell(ECellType type) {

            if (type == State) {
                return;
            }

            var prev = State;
            State = type;

            var showing = OverlayFor(prev);
            if (showing != null) {
                showing.ActOut();
            }

            var target = OverlayFor(type);

            if (target != null) {
                target.ActIn();
            }
        }

        MBABSMarkAnim OverlayFor(ECellType type) {
            switch (type) {
                case ECellType.X:           return _xMark;
                case ECellType.QUEEN:       return _queenMark;
                case ECellType.WRONG_QUEEN: return _wrongQueenMark;
                default:                    return null;   // EMPTY
            }
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

