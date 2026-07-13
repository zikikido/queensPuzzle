using Common;
using System.Collections;
using System.Collections.Generic;
using QueensPuzzle;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    /// <summary>
    /// The tutorial's spotlight API: every call darkens the board (the Black drape) and opens
    /// holes over the cells it names — the player's eye goes exactly where the step wants it.
    /// Compose steps from these calls and finish with Hide():
    ///
    ///   ShowCell(2, 3)          — one cell
    ///   ShowCells(indices)      — any set of cells (flat index = row * N + col)
    ///   ShowRow(2) / ShowColumn(3)
    ///   ShowRegion(4)           — a whole colour
    ///   Hide()                  — close the holes, lift the drape
    /// </summary>
    public class MBToturial : MonoBehaviour {

        public static MBToturial instance;

        GameObject _blackCurtain;   // the darkening drape; found by name when left empty
        CanvasGroup _canvas;        // the tutorial UI canvas; found in children when left empty
        Button _applyButton;        // auto-fills the hint's suggestion

        float _curtainAlpha = 1f;   // the curtain's designed alpha — "opacity 1" restores THIS
        TMPro.TMP_Text _text;       // $Text under the tutorial — the step's message
        RectTransform _textContainer;   // $TextContainer — parked above the board on Show
        RectTransform _applyRt;         // $ApplayButton — parked under the board on Show
        GameObject _hand;               // $Hand — the demo finger (its own tap animation runs on $FingerImage)
        GameObject _doubleTapText;      // $DoublieTapTex — shown only when the step wants a queen (double tap)
        Transform _finger;              // $FingerImage — rotated by code to mime the taps
        Coroutine _fingerAnim;
        Coroutine _handSweep;

        [SerializeField] float _boardMargin = 0.4f;   // gap to the board, in cell widths
        [SerializeField] Color _causeTint = new Color(0.30f, 0.75f, 1f, 0.45f);   // azure glass over the hint's cause cells

        readonly List<GameObject> _causeTints = new List<GameObject>();   // live overlays, destroyed on Hide

        bool _handEnabled = true, _applyEnabled = true;   // per-step visibility switches (sticky until changed)

        // while a spotlight is up: only these cells may be edited…
        readonly HashSet<MBCell> _allowed = new HashSet<MBCell>();
        // …and (for hints) each target cell must reach this state before the tutorial closes
        readonly Dictionary<MBCell, MBCell.ECellType> _targets = new Dictionary<MBCell, MBCell.ECellType>();

        /// <summary>True for cells the player may NOT touch while a spotlight is up.</summary>
        public bool IsLocked(MBCell cell) => _allowed.Count > 0 && !_allowed.Contains(cell);

        /// <summary>
        /// While a spotlight is up, only the suggested edit is allowed: outside the holes —
        /// nothing; inside a hint step — exactly the target mark; inside a plain spotlight — anything.
        /// </summary>
        public bool AllowsEdit(MBCell cell, MBCell.ECellType intended) {
            if (_allowed.Count == 0) return true;          // no spotlight — free play
            if (!_allowed.Contains(cell)) return false;    // outside the holes — locked
            if (_targets.Count == 0) return true;          // plain spotlight — free inside
            return _targets.TryGetValue(cell, out var t) && t == intended;
        }

        // The trick: everything stays ACTIVE but at opacity 0 for the first frames, so Unity's
        // layout pass sizes/stretches the UI properly; only after that we hide for real.
        void Awake() {

            instance = this;
            _blackCurtain = GameObject.Find("$BlackCurtain");
            _canvas = GetComponentInChildren<CanvasGroup>(true);
            _text = transform.RecursiveFindChild<TMPro.TMP_Text>("$Text");
            _textContainer = transform.RecursiveFindChild("$TextContainer") as RectTransform;
            _applyRt = transform.RecursiveFindChild("$ApplyButton") as RectTransform;
            var hand = transform.RecursiveFindChild("$Hand");
            _hand = hand != null ? hand.gameObject : null;
            var dtt = transform.RecursiveFindChild("$DoublieTapTex");
            _doubleTapText = dtt != null ? dtt.gameObject : null;
            _finger = transform.RecursiveFindChild("$FingerImage");

            var sr = _blackCurtain.GetComponent<SpriteRenderer>();

            if (sr != null) {
                _curtainAlpha = sr.color.a;
            }

            if (_applyButton == null && _applyRt != null) _applyButton = _applyRt.GetComponentInChildren<Button>(true);
            if (_applyButton != null) _applyButton.onClick.AddListener(Apply);
            else Debug.LogWarning("[MBToturial] No Apply button found ($ApplayButton).");
            SetOpacity(0f);   // invisible, but alive for the layout pass
        }

        /// <summary>When true (default) a step hides itself the moment it's done; turn off to
        /// keep the curtain up between steps and call Hide() yourself.</summary>
        public bool AutoHide = true;

        /// <summary>True when every target cell of the current step holds the right mark.</summary>
        public bool StepDone() {
            if (_targets.Count == 0) return false;   // no step (or plain spotlight — never "done")
            foreach (var kv in _targets)
                if (kv.Key.State != kv.Value) return false;
            return true;
        }

        // the step completes itself the moment every target cell holds the right mark
        void Update() {
            if (AutoHide && StepDone()) Hide();
        }

        void OnDestroy() { if (instance == this) instance = null; }

        IEnumerator Start() {
            // wait for UI to refresh our layout
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            if (_blackCurtain != null) _blackCurtain.SetActive(false);
            gameObject.SetActive(false);
            SetOpacity(1f);
            MBDrapeHoles.SetCurtain(_blackCurtain);
        }

        void SetOpacity(float a) {

            if (_canvas != null) _canvas.alpha = a;

            var sr = _blackCurtain != null ? _blackCurtain.GetComponent<SpriteRenderer>() : null;

            if (sr != null) {
                var c = sr.color;
                c.a = a * _curtainAlpha;
                sr.color = c; 
            }
        }

        /// <summary>
        /// Spotlight a hint and lock the rest of the board: the tutorial closes only when the
        /// player does what the hint says (or taps Apply, which does it for them).
        /// </summary>
        public void ShowHint(Hint hint) {
            _targets.Clear();
            var gp = MBGameplay.instance;
            if (gp == null || hint.cells == null || hint.cells.Length == 0) return;

            var lit = new List<MBCell>();
            var target = TargetFor(hint.kind);
            foreach (int idx in hint.cells) {
                var cell = gp.CellAt(idx / gp.N, idx % gp.N);
                if (cell == null) continue;
                lit.Add(cell);
                if (target.HasValue) _targets[cell] = target.Value;
            }

            // the cells the reasoning is ABOUT get a hole too, tinted, so the player sees the
            // why: the trick's own cause cells (a confined region, a one-colour line, a subset…)
            // plus — for eliminations — every placed queen attacking a target. Lit, tinted,
            // never targets, never editable.
            var cause = new List<MBCell>();
            if (hint.causeCells != null)
                foreach (int idx in hint.causeCells) {
                    var cell = gp.CellAt(idx / gp.N, idx % gp.N);
                    if (cell != null && !lit.Contains(cell) && !cause.Contains(cell)) cause.Add(cell);
                }
            if (hint.kind == HintKind.Eliminate)
                AddCauseQueens(lit, cause, gp);

            var holes = new List<MBCell>(lit);
            holes.AddRange(cause);
            Spot(holes);
            TintCause(cause);
            SetText(hint.note);
            ShowHand(hint, gp);
            if (_applyRt != null) _applyRt.gameObject.SetActive(_applyEnabled && _targets.Count > 0);
        }

        /// <summary>Show/hide the demo hand (sticky — applies to this and future steps).</summary>
        public void SetHandVisible(bool on) {
            _handEnabled = on;
            if (!on) {
                if (_handSweep != null) { StopCoroutine(_handSweep); _handSweep = null; }
                PlayFingerAnim(false);
                if (_hand != null) _hand.SetActive(false);
            }
        }

        /// <summary>Show/hide the Apply button (sticky — applies to this and future steps).</summary>
        public void SetApplyVisible(bool on) {
            _applyEnabled = on;
            if (_applyRt != null) _applyRt.gameObject.SetActive(on && _targets.Count > 0);
        }

        // The demo finger: queen steps park it on the cell with the double-tap text; X/clear
        // steps hide the text and sweep the finger along the cells to mark.
        void ShowHand(Hint hint, MBGameplay gp) {
            if (_hand == null) return;
            if (_handSweep != null) { StopCoroutine(_handSweep); _handSweep = null; }
            if (!_handEnabled) { _hand.SetActive(false); return; }

            var target = TargetFor(hint.kind);
            if (!target.HasValue) { _hand.SetActive(false); return; }

            var cells = new List<MBCell>();
            foreach (int idx in hint.cells) {
                var cell = gp.CellAt(idx / gp.N, idx % gp.N);
                if (cell != null) cells.Add(cell);
            }
            if (cells.Count == 0) { _hand.SetActive(false); return; }

            bool doubleTap = target.Value == MBCell.ECellType.QUEEN;
            _hand.SetActive(true);
            if (_doubleTapText != null) _doubleTapText.SetActive(doubleTap);
            PlayFingerAnim(doubleTap);   // tap-tap only when demonstrating a queen

            if (doubleTap || cells.Count == 1) _hand.transform.position = cells[0].transform.position;
            else _handSweep = StartCoroutine(SweepHand(cells, target.Value));
        }

        void PlayFingerAnim(bool on) {
            if (_fingerAnim != null) { StopCoroutine(_fingerAnim); _fingerAnim = null; }
            if (_finger == null) return;
            _finger.localRotation = Quaternion.identity;
            if (on) _fingerAnim = StartCoroutine(FingerDoubleTap());
        }

        // tap-tap … pause … tap-tap — a quick press-rotation and back, twice
        IEnumerator FingerDoubleTap() {
            const float angle = -27f, press = 0.09f, gap = 0.12f, pause = 0.7f;
            while (true) {
                yield return FingerTap(angle, press);
                yield return new WaitForSecondsRealtime(gap);
                yield return FingerTap(angle, press);
                yield return new WaitForSecondsRealtime(pause);
            }
        }

        IEnumerator FingerTap(float angle, float dur) {
            for (float e = 0f; e < dur; e += Time.unscaledDeltaTime) {
                _finger.localRotation = Quaternion.Euler(Mathf.Lerp(0f, angle, e / dur), 0f, 0f);
                yield return null;
            }
            for (float e = 0f; e < dur; e += Time.unscaledDeltaTime) {
                _finger.localRotation = Quaternion.Euler(Mathf.Lerp(angle, 0f, e / dur), 0f, 0f);
                yield return null;
            }
            _finger.localRotation = Quaternion.identity;
        }

        // glide the finger along the cells, looping with a small pause. Every cycle drops the
        // cells the player already marked, so the hand only shows what's still left to do.
        IEnumerator SweepHand(List<MBCell> cells, MBCell.ECellType target) {
            const float perCell = 0.35f, pause = 0.4f;
            var pending = new List<MBCell>();
            while (true) {
                pending.Clear();
                foreach (var c in cells) if (c.State != target) pending.Add(c);

                if (pending.Count == 0) { _hand.SetActive(false); yield break; }  // all done — completion closes the step

                // drag is only demonstrated on a gapless straight line — anything else
                // (scattered cells, or a line broken by the player's marks) gets a pointing
                // finger on the next cell instead, so we never teach a drag that would
                // paint over cells in between.
                if (pending.Count == 1 || !IsContiguousLine(pending)) {
                    _hand.transform.position = pending[0].transform.position;
                    yield return null;
                    continue;
                }

                _hand.transform.position = pending[0].transform.position;
                for (int i = 0; i < pending.Count - 1; i++) {
                    Vector3 a = pending[i].transform.position, b = pending[i + 1].transform.position;
                    for (float e = 0f; e < perCell; e += Time.unscaledDeltaTime) {
                        _hand.transform.position = Vector3.Lerp(a, b, e / perCell);
                        yield return null;
                    }
                }
                yield return new WaitForSecondsRealtime(pause);
            }
        }

        // same row or same column, with consecutive positions — a drag can cover it exactly
        static bool IsContiguousLine(List<MBCell> cells) {
            bool sameRow = true, sameCol = true;
            foreach (var c in cells) {
                if (c.Y != cells[0].Y) sameRow = false;
                if (c.X != cells[0].X) sameCol = false;
            }
            if (!sameRow && !sameCol) return false;

            var vals = new List<int>();
            foreach (var c in cells) vals.Add(sameRow ? c.X : c.Y);
            vals.Sort();
            for (int i = 1; i < vals.Count; i++)
                if (vals[i] != vals[i - 1] + 1) return false;
            return true;
        }

        /// <summary>The message shown with the spotlight ($Text).</summary>
        public void SetText(string message) {
            if (_text != null) _text.text = message ?? "";
        }

        // every placed queen that attacks one of the target cells — collected as a cause
        void AddCauseQueens(List<MBCell> lit, List<MBCell> cause, MBGameplay gp) {
            for (int r = 0; r < gp.N; r++)
                for (int c = 0; c < gp.N; c++) {
                    var q = gp.CellAt(r, c);
                    if (q == null || q.State != MBCell.ECellType.QUEEN || lit.Contains(q) || cause.Contains(q)) continue;
                    foreach (var t in lit)
                        if (Attacks(q, t, gp.Level)) { cause.Add(q); break; }
                }
        }

        // Azure glass over each cause cell: a copy of the cell's own sprite, tinted, drawn
        // above the cell's art inside its sorting group. Cleared on Hide().
        void TintCause(List<MBCell> cells) {
            ClearCauseTints();
            foreach (var cell in cells) {
                var src = cell.transform.RecursiveFindChild<SpriteRenderer>("$CellSprite");
                if (src == null) src = cell.GetComponentInChildren<SpriteRenderer>();
                if (src == null) continue;
                var go = new GameObject("$CauseTint");
                go.transform.SetParent(src.transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = src.sprite;
                sr.drawMode = src.drawMode;
                if (src.drawMode != SpriteDrawMode.Simple) sr.size = src.size;
                sr.color = _causeTint;
                sr.sortingLayerID = src.sortingLayerID;
                sr.sortingOrder = src.sortingOrder + 40;
                _causeTints.Add(go);
            }
        }

        void ClearCauseTints() {
            foreach (var go in _causeTints) if (go != null) Destroy(go);
            _causeTints.Clear();
        }

        static bool Attacks(MBCell q, MBCell t, LevelPack.Level level) {
            if (q.Y == t.Y || q.X == t.X) return true;                                  // row / column
            if (Mathf.Abs(q.Y - t.Y) <= 1 && Mathf.Abs(q.X - t.X) <= 1) return true;    // touch
            return level != null && level.RegionAt(q.Y, q.X) == level.RegionAt(t.Y, t.X); // region
        }

        // what state completes each hint kind; null = no single right edit (Guess = just look)
        static MBCell.ECellType? TargetFor(HintKind kind) {
            switch (kind) {
                case HintKind.PlaceQueen: return MBCell.ECellType.QUEEN;
                case HintKind.Eliminate:  return MBCell.ECellType.X;
                case HintKind.WrongX:     return MBCell.ECellType.EMPTY;
                case HintKind.WrongQueen: return MBCell.ECellType.EMPTY;
                default:                  return null;
            }
        }

        /// <summary>Auto-fill the hint's suggestion (the Apply button).</summary>
        public void Apply() {
            var gp = MBGameplay.instance; if (gp == null) return;
            if (_targets.Count == 0) { Debug.LogWarning("[MBToturial] Apply clicked but this step has no targets."); return; }
            foreach (var kv in _targets) gp.ApplyTutorialMark(kv.Key, kv.Value);
            // Update sees every target met and closes the tutorial
        }

        public void ShowCell(int row, int col) => Spot(Collect(c => c.Y == row && c.X == col));

        public void ShowCells(IEnumerable<int> cellIndices) {
            var gp = MBGameplay.instance; if (gp == null) return;
            var lit = new List<MBCell>();
            foreach (int idx in cellIndices) {
                var cell = gp.CellAt(idx / gp.N, idx % gp.N);
                if (cell != null) lit.Add(cell);
            }
            Spot(lit);
        }

        public void ShowRow(int row) => Spot(Collect(c => c.Y == row));

        public void ShowColumn(int col) => Spot(Collect(c => c.X == col));

        public void ShowRegion(int region) {
            var gp = MBGameplay.instance; if (gp == null || gp.Level == null) return;
            Spot(Collect(c => gp.Level.RegionAt(c.Y, c.X) == region));
        }

        public void Hide() {
            _allowed.Clear();
            _targets.Clear();
            if (_handSweep != null) { StopCoroutine(_handSweep); _handSweep = null; }
            PlayFingerAnim(false);
            if (_hand != null) _hand.SetActive(false);
            ClearCauseTints();
            MBDrapeHoles.Clear();
            gameObject.SetActive(false);   // back to sleep until the next Show*
        }

        // ---- helpers ------------------------------------------------------------------

        List<MBCell> Collect(System.Predicate<MBCell> pick) {
            var lit = new List<MBCell>();
            var gp = MBGameplay.instance; if (gp == null) return lit;
            for (int r = 0; r < gp.N; r++)
                for (int c = 0; c < gp.N; c++) {
                    var cell = gp.CellAt(r, c);
                    if (cell != null && pick(cell)) lit.Add(cell);
                }
            return lit;
        }

        void Spot(List<MBCell> cells) {
            if (cells.Count == 0) return;
            _allowed.Clear();
            foreach (var c in cells) _allowed.Add(c);   // everything else is locked
            SetText("");                  // stale text never survives into a new spotlight
            gameObject.SetActive(true);   // the tutorial put itself to sleep after layout — wake it
            PlaceAroundBoard();
            MBDrapeHoles.Show(cells);
        }

        // Park $TextContainer above the board and $ApplayButton below it, with a margin.
        void PlaceAroundBoard() {
            var gp = MBGameplay.instance;
            if (gp == null) return;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, half = 0f;
            for (int r = 0; r < gp.N; r++)
                for (int c = 0; c < gp.N; c++) {
                    var cell = gp.CellAt(r, c);
                    if (cell == null) continue;
                    var p = cell.transform.position;
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                    half = cell.GetSize().x * cell.transform.lossyScale.x * 0.5f;
                }
            if (half <= 0f) return;

            float centerX = (minX + maxX) * 0.5f;
            float margin = half * 2f * _boardMargin;

            if (_textContainer != null) {
                float h = _textContainer.rect.height * _textContainer.lossyScale.y * 0.5f;
                _textContainer.position = new Vector3(centerX, maxY + half + margin + h, _textContainer.position.z);
            }
            if (_applyRt != null) {
                float h = _applyRt.rect.height * _applyRt.lossyScale.y * 0.5f;
                _applyRt.position = new Vector3(centerX, minY - half - margin - h, _applyRt.position.z);
            }
        }
    }
}
