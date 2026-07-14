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
        GameObject _hintQueen, _hintX;   // ghost previews of the mark a tutorial step wants here
        ECellType? _hintGhost;           // the wanted mark; the ghost shows only while State differs

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

            var hq = transform.RecursiveFindChild("$HintQueen");
            _hintQueen = hq != null ? hq.gameObject : null;
            var hx = transform.RecursiveFindChild("$HintX");
            _hintX = hx != null ? hx.gameObject : null;

            _xMark.InitOut();
            _queenMark.InitOut();
            _wrongQueenMark.InitOut();
            State = ECellType.EMPTY;
            SetHintGhost(null);
        }

        /// <summary>Ghost preview of the mark the tutorial wants here: QUEEN shows $HintQueen,
        /// X shows $HintX, anything else hides both. The ghost hides itself while the cell
        /// already holds the wanted mark and returns if the player clears it.</summary>
        public void SetHintGhost(ECellType? type) {
            _hintGhost = type;
            ApplyHintGhost();
        }

        void ApplyHintGhost() {
            bool pending = _hintGhost.HasValue && State != _hintGhost.Value;
            if (_hintQueen != null) _hintQueen.SetActive(pending && _hintGhost.Value == ECellType.QUEEN);
            if (_hintX != null) _hintX.SetActive(pending && _hintGhost.Value == ECellType.X);
        }

        /// <summary>Play a queen animation — only when this cell actually shows a queen.</summary>
        public void PlayQueen(QueenState state) {
            if (State != ECellType.QUEEN || !_queenMark.gameObject.activeSelf) return;
            _queenMark.Play(state.ToString());
        }

        /// <summary>Length of a state's clip in seconds (for timing the win/fail popups).</summary>
        public float GetStateLength(QueenState state) =>
            _queenMark != null ? _queenMark.StateLength(state.ToString()) : 0f;

        /// <summary>Board setup (restore / revealed queens, before the bloom): set the state and
        /// show the mark INSTANTLY — no pop-in, no particles, no fades. Played moves use MarkCell.</summary>
        public void StartWithMark(ECellType type) {

            if (type == State) {
                return;
            }

            State = type;

            var target = OverlayFor(type);

            if (target != null) {
                target.InitIn();
            }
            ApplyHintGhost();
        }

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
            ApplyHintGhost();
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

