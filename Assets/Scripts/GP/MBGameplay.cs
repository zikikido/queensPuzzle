using Common;
using Core;
using Puzzby;
using QueensPuzzle;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    public class MBGameplay : MonoBehaviour, IMBTouchesListener {

        static public MBGameplay instance;

        [Header("Board layout (fractions of a cell)")]
        [SerializeField] float _spacing = 0.08f;   // gap between cells
        [SerializeField] float _margin = 0.25f;    // border between the grid and the board edge

        [Header("Start animation")]
        [SerializeField] float _bloomPop = 0.35f;       // per-cell pop duration
        [SerializeField] float _bloomStagger = 0.05f;   // extra delay per ring out from the centre

        enum DragMode { None, PaintX, Erase }

        MBCell[,] _cells;            // [row, col]
        LevelData _level;            // the level currently on the board (for hints)
        RectTransform _board;
        int _n;
        float _step, _cellSize;
        DragMode _drag;
        Vector3 _lastDragWorld;      // previous drag sample — fill cells skipped between frames
        MBTouches _touches;
        bool _ready;                 // input gated until the bloom reveal finishes
        MBWinPopup _winPopup;        // found by type (it lives elsewhere in the scene, inactive)
        MBTopBar _topBar;            // "queens placed / total" HUD
        Coroutine _shake;            // board shake on a wrong queen
        float _lastTick;             // throttle so drag-paint haptics are distinct ticks, not a buzz
        const float TickInterval = 0.12f;   // gap must be well over the pulse length or ticks merge
        MBCell _lastTapCell, _prevTapCell;  // last two tapped cells — a double-click must be the same cell

        // Undo: each stroke (one tap or one drag of X edits) is a group of cell changes we can revert.
        // Queens / wrong-queens are never recorded, so they can't be undone.
        struct CellEdit { public int idx; public MBCell.ECellType from; public CellEdit(int i, MBCell.ECellType f) { idx = i; from = f; } }
        readonly List<List<CellEdit>> _undo = new List<List<CellEdit>>();
        List<CellEdit> _stroke;             // the stroke being recorded now (null between strokes)

        // read-only board access for the tutorial (spotlights need real cells)
        public bool Ready => _ready;
        public int N => _n;
        public LevelData Level => _level;
        public MBCell CellAt(int row, int col) =>
            _cells != null && row >= 0 && row < _n && col >= 0 && col < _n ? _cells[row, col] : null;

        void Awake() {
            instance = this;

            // The tutorial object may be left disabled in the scene; wake it so its Awake runs and
            // registers MBToturial.instance (it puts itself back to sleep after its layout pass).
            var tut = FindAnyObjectByType<MBToturial>(FindObjectsInactive.Include);
            if (tut != null && !tut.gameObject.activeSelf) tut.gameObject.SetActive(true);

            // Same for the win popup: wake it so its Awake wires the button, then hide it again
            // (unlike the tutorial it doesn't self-sleep — it only shows on win).
            _winPopup = FindAnyObjectByType<MBWinPopup>(FindObjectsInactive.Include);
            if (_winPopup != null && !_winPopup.gameObject.activeSelf) {
                _winPopup.gameObject.SetActive(true);
                _winPopup.gameObject.SetActive(false);
            }
        }

        void OnDestroy() {
            instance = null;
        }


        IEnumerator Start() {
            WireBoostButtons();
            yield return BuildBoard();
        }

        // Collect every boost button under us and route its click to the boost for its type.
        void WireBoostButtons() {
            foreach (var boost in GetComponentsInChildren<MBBoostButton>(true)) {
                var btn = boost.GetButton();
                if (btn == null) continue;
                var type = boost.BoostType;
                btn.onClick.AddListener(() => RunBoost(type));
            }
        }

        void RunBoost(EBoostType type) {
            switch (type) {
                case EBoostType.QUEEN: OpenQueen(); break;
                case EBoostType.HINT:
                    MBToturial.instance?.SetHandVisible(false);   // boost hint: Apply button, no hand
                    MBToturial.instance?.SetApplyVisible(true);
                    OpenHint();
                    break;
                case EBoostType.UNDO:  Undo();      break;
            }
        }

        // Queen boost: place the most trivial forced queen; if none is strictly forced (the board
        // would need a guess) still reveal a correct, unplaced solution queen.
        void OpenQueen() {
            if (!_ready || _cells == null || _level == null) return;
            GatherBoard(out var queens, out var xs);

            if (SolveTracer.TryQueenBoost(_n, _level.regions, _level.solutionColumns, queens, xs, out int target)) {
                PlaceBoostQueen(target);
                return;
            }

            int fallback = FirstUnplacedSolutionQueen();
            if (fallback >= 0) PlaceBoostQueen(fallback);
            else Debug.Log("[MBGameplay] Queen boost: nothing to place.");
        }

        // Place a correct queen from a boost and advance the game (as a manual correct placement).
        void PlaceBoostQueen(int idx) {
            int r = idx / _n, c = idx % _n;
            var cell = _cells[r, c];
            if (cell.State == MBCell.ECellType.QUEEN) return;
            cell.MarkCell(MBCell.ECellType.QUEEN);
            cell.Pulse();

            int placed = CountQueens();
            _topBar?.SetProgress(placed);
            if (placed == _n) Win();
            else Haptics.Play(GameHaptic.Happy);
        }

        // A solution queen not yet on the board (row-major), or -1 when they're all placed.
        int FirstUnplacedSolutionQueen() {
            foreach (var cell in _cells)
                if (cell.IsSolutionQueen && cell.State != MBCell.ECellType.QUEEN)
                    return cell.Y * _n + cell.X;
            return -1;
        }

        // Read the board for the solver:
        //   QUEEN                        → a placed queen
        //   WRONG_QUEEN                  → a non-solution cell, so a real X
        //   X not on a solution cell     → a real X
        //   X on a solution cell         → a "wrong X" (unsure): treated as EMPTY, so it
        //                                  doesn't corrupt the deduction or spoil the queen.
        void GatherBoard(out List<int> queens, out List<int> xs) {
            queens = new List<int>();
            xs = new List<int>();
            foreach (var cell in _cells) {
                int idx = cell.Y * _n + cell.X;
                if (cell.State == MBCell.ECellType.QUEEN) queens.Add(idx);
                else if (cell.State == MBCell.ECellType.WRONG_QUEEN) xs.Add(idx);
                else if (cell.State == MBCell.ECellType.X && !cell.IsSolutionQueen) xs.Add(idx);
            }
        }

        // Undo boost: revert the most recent stroke of X edits (a whole drag counts as one).
        // Never touches a queen / wrong-queen cell.
        void Undo() {
            if (!_ready || _undo.Count == 0) return;

            var stroke = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            bool any = false;
            foreach (var e in stroke) {
                var cell = _cells[e.idx / _n, e.idx % _n];
                if (cell.State == MBCell.ECellType.QUEEN || cell.State == MBCell.ECellType.WRONG_QUEEN) continue;
                cell.MarkCell(e.from);
                any = true;
            }
            if (any) Haptics.Play(GameHaptic.Tap);
        }

        // Hint: fix a mistake first (cheapest + necessary), else the next deduction from the board.
        // Public: the first-play tutorial drives the same flow, step by step.
        public void OpenHint() {
            if (!_ready || _cells == null || _level == null) return;
            GatherBoard(out var queens, out var xs);

            // 1) the next real deduction
            if (SolveTracer.TryHint(_n, _level.regions, _level.solutionColumns, queens, xs, out var hint, RegionRichName, PieceName, PieceNamePlural)) {
                PresentHint(hint);
                return;
            }

            // 2) last resort — a wrong X is hiding a forced queen (strong spoiler, only when stuck)
            foreach (var cell in _cells)
                if (cell.State == MBCell.ECellType.X && cell.IsSolutionQueen) {
                    PresentHint(new Hint { kind = HintKind.WrongX, cells = new[] { cell.Y * _n + cell.X },
                                           note = $"a {PieceName} belongs here — clear this X" });
                    return;
                }

            Debug.Log("[MBGameplay] Hint: no simple next step (would need a guess).");
        }

        // What hint texts call the piece — this game's queens are puppies (another skin: cats).
        const string PieceName = "puppy";
        const string PieceNamePlural = "puppies";

        // How hint texts name a region for the PLAYER: the colour's name, tinted in that colour.
        static string RegionRichName(int g) {
            string name = SORegionsColors.NameAt(g);
            if (string.IsNullOrEmpty(name)) name = ((char)('A' + g)).ToString();
            return $"<color=#{UnityEngine.ColorUtility.ToHtmlStringRGB(SORegionsColors.ColorAt(g))}>{name}</color>";
        }

        void PresentHint(Hint hint) {
            Debug.Log($"[MBGameplay] Hint ({hint.kind}): {hint.note}");
            if (hint.cells == null) return;
            foreach (int idx in hint.cells) {
                int r = idx / _n, c = idx % _n;
                if (r >= 0 && r < _n && c >= 0 && c < _n) _cells[r, c].Pulse();
            }
            MBToturial.instance?.ShowHint(hint); // the tutorial owns spotlight, lock and completion
        }

        // Re-run while already in play mode (called by the Level Builder): clears the current
        // board and rebuilds it from the current level.
        public void Replay() {
            StopAllCoroutines();
            StartCoroutine(BuildBoard());
        }

        IEnumerator BuildBoard() {

            LevelData level = LevelLoader.LoadLevel();
            if (level == null) {
                Debug.LogError("[MBGameplay] No level to load.");
                yield break;
            }

            _ready = false;   // no input while we (re)build and bloom
            _level = level;   // keep for hints

            // fresh start — clear any leftover state from the previous level so it can't bleed in:
            //   win popup (hide it) and the tutorial (Hide() also clears its cell-locks/targets,
            //   otherwise the new board's cells would all read as locked).
            if (_winPopup != null) _winPopup.gameObject.SetActive(false);
            MBToturial.instance?.Hide();

            // attempts counter: fresh level → 1; same level again (retry / app restart) → +1
            if (AppData.AttemptsLevelIdx.Value != AppData.LevelIdx.Value) {
                AppData.AttemptsLevelIdx.Value = AppData.LevelIdx.Value;
                AppData.LevelAttempts.Value = 1;
            } else {
                AppData.LevelAttempts.Value++;
            }

            var board = transform.RecursiveFindChild("$Board") as RectTransform;
            var cellPrefab = MBCell.LoadFromResource();

            // clear cells from any previous build
            for (int i = board.childCount - 1; i >= 0; i--)
                Destroy(board.GetChild(i).gameObject);

            int n = level.size;

            // one cell = the sprite's own world size; cells keep that size and spread by _spacing
            float cellSize = cellPrefab.GetSize().x;
            float step = cellSize * (1f + _spacing);   // center-to-center distance

            // remember geometry for touch hit-testing
            _board = board; _n = n; _cellSize = cellSize; _step = step;
            _cells = new MBCell[n, n];
            _undo.Clear(); _stroke = null;   // fresh board, nothing to undo

            // top-bar progress starts at 0 / n queens
            if (_topBar == null) _topBar = FindAnyObjectByType<MBTopBar>(FindObjectsInactive.Include);
            _topBar?.Init(_n);

            // load the cells into $Board — n x n grid, centered on the board, row 0 on top
            for (int r = 0; r < n; r++) {
                for (int c = 0; c < n; c++) {
                    var cell = Instantiate(cellPrefab, board, false);
                    cell.name = $"Cell_{r}_{c}";
                    float x = (c - (n - 1) * 0.5f) * step;
                    float y = ((n - 1) * 0.5f - r) * step;
                    cell.transform.localPosition = new Vector3(x, y, 0f);
                    cell.Init(level.RegionAt(r, c), c, r, level.IsSolutionQueen(r, c));
                    _cells[r, c] = cell;
                }
            }

            // board size = grid + gaps + margin border on every side
            float boardSize = cellSize * (n + (n - 1) * _spacing + 2f * _margin);
            transform.RecursiveFindChild<RectTransform>("$BoardScaler").sizeDelta = board.sizeDelta = new Vector2(boardSize, boardSize);

            // force repost
            transform.GetComponent<RTResizeInvoker>().ReporteSize();

            // start listening for touches (needs an MBTouches on a scene root)
            if (_touches == null) {
                _touches = this.RegisterToRoot();
                if (_touches != null) _touches.DoubleClickDelteTime = 0.3f;   // default 1.5s is far too long
            }

            // wait for UI to refresh our layout
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            // "Bloom" the cells in from the centre, then let the player interact
            yield return BloomReveal();
            Haptics.Prepare();   // warm the engine so the first tap fires without latency
            _ready = true;

            MBFirstPlayToturial.TryBegin(this);   // first level ever → guided tutorial
        }

        void OnDisable() {
            if (_touches != null) { this.UnRegister(_touches); _touches = null; }
        }

        // ---- touch input: paint X / erase on drag, double-click to place a queen ----------

        public void TouchDown(MBTouches.TouchData touch, bool firstTime) {
            if (!_ready || !firstTime) return;
            var cell = HitTest(touch.WorldPoint);
            _lastDragWorld = touch.WorldPoint;   // start of the drag path
            _prevTapCell = _lastTapCell;   // remember the last two taps so we can require same-cell double-clicks
            _lastTapCell = cell;
            if (cell == null) { _drag = DragMode.None; return; }

            switch (cell.State) {
                case MBCell.ECellType.EMPTY:
                    if (!TutorialAllows(cell, MBCell.ECellType.X)) { _drag = DragMode.None; break; }
                    _drag = DragMode.PaintX;
                    _stroke = new List<CellEdit>();   // begin recording this stroke
                    PaintCell(cell, MBCell.ECellType.X);
                    Tick();
                    break;
                case MBCell.ECellType.X:
                    if (!TutorialAllows(cell, MBCell.ECellType.EMPTY)) { _drag = DragMode.None; break; }
                    _drag = DragMode.Erase;
                    _stroke = new List<CellEdit>();
                    PaintCell(cell, MBCell.ECellType.EMPTY);
                    Tick();
                    break;
                default:
                    _drag = DragMode.None;   // don't paint over queens
                    break;
            }
        }

        // While a tutorial spotlight is up, only the SUGGESTED edit on its own cells is allowed.
        static bool TutorialAllows(MBCell cell, MBCell.ECellType intended) =>
            MBToturial.instance == null || MBToturial.instance.AllowsEdit(cell, intended);

        // Apply a tutorial suggestion to a cell the proper way (queens advance progress/win).
        public void ApplyTutorialMark(MBCell cell, MBCell.ECellType target) {
            if (cell == null || cell.State == target) return;
            if (target == MBCell.ECellType.QUEEN) PlaceBoostQueen(cell.Y * _n + cell.X);
            else cell.MarkCell(target);
        }

        // Change a cell's X/empty state as part of the current stroke, remembering its prior state.
        void PaintCell(MBCell cell, MBCell.ECellType to) {
            _stroke?.Add(new CellEdit(cell.Y * _n + cell.X, cell.State));
            cell.MarkCell(to);
        }

        public void TouchDrag(MBTouches.TouchData touch, bool samePoint) {
            if (!_ready || samePoint || _drag == DragMode.None) return;

            // Walk from the previous sample to this one and paint every cell along the way, so a
            // fast drag (finger moving several cells per frame) doesn't leave gaps in the row.
            Vector3 from = _lastDragWorld, to = touch.WorldPoint;
            float localDist = Vector2.Distance(_board.InverseTransformPoint(from), _board.InverseTransformPoint(to));
            int steps = Mathf.Max(1, Mathf.CeilToInt(localDist / (_cellSize * 0.5f)));   // ≤ half a cell = no skips
            for (int i = 1; i <= steps; i++)
                PaintDrag(HitTest(Vector3.Lerp(from, to, (float)i / steps)));

            _lastDragWorld = to;
        }

        // Apply the active drag mode to one cell. Idempotent — the state guard means re-visiting a
        // cell (from overlapping steps) does nothing and won't re-fire the haptic.
        void PaintDrag(MBCell cell) {
            if (cell == null) return;
            if (_drag == DragMode.PaintX && cell.State == MBCell.ECellType.EMPTY && TutorialAllows(cell, MBCell.ECellType.X)) {
                PaintCell(cell, MBCell.ECellType.X);
                DragTick();
            }
            else if (_drag == DragMode.Erase && cell.State == MBCell.ECellType.X && TutorialAllows(cell, MBCell.ECellType.EMPTY)) {
                PaintCell(cell, MBCell.ECellType.EMPTY);
                DragTick();
            }
        }

        public void TouchUp(MBTouches.TouchData touch, bool clicked, bool doubleClick) {
            if (!_ready) return;
            // a real double-click means both taps landed on the SAME cell — otherwise it's just two quick taps
            if (doubleClick && _lastTapCell != null && _lastTapCell == _prevTapCell && TutorialAllows(_lastTapCell, MBCell.ECellType.QUEEN)) {
                _prevTapCell = null;   // consume it so a third quick tap can't re-trigger
                var cell = _lastTapCell;

                // This gesture is a queen, not X edits — drop the two taps' X toggles from undo.
                _stroke = null;
                int qidx = cell.Y * _n + cell.X;
                if (_undo.Count > 0 && _undo[_undo.Count - 1].Count == 1 && _undo[_undo.Count - 1][0].idx == qidx)
                    _undo.RemoveAt(_undo.Count - 1);

                bool correct = cell.IsSolutionQueen;
                cell.MarkCell(correct ? MBCell.ECellType.QUEEN : MBCell.ECellType.WRONG_QUEEN);
                if (correct) {
                    int placed = CountQueens();
                    _topBar?.SetProgress(placed);
                    if (placed == _n) Win();
                    else Haptics.Play(GameHaptic.Happy);
                } else {
                    Haptics.Play(GameHaptic.Wrong);
                    if (_shake != null) StopCoroutine(_shake);
                    _shake = StartCoroutine(ShakeBoard());
                }
            }
            else if (_stroke != null) {
                // a tap or a whole drag of X edits = one undo entry
                if (_stroke.Count > 0) _undo.Add(_stroke);
                _stroke = null;
            }

            _drag = DragMode.None;
        }

        // Queens correctly placed (they only ever land on solution cells).
        int CountQueens() {
            int placed = 0;
            foreach (var cell in _cells)
                if (cell.State == MBCell.ECellType.QUEEN) placed++;
            return placed;
        }

        // Solved when every solution queen is on the board.
        bool IsSolved() => CountQueens() == _n;

        void Win() {
            _ready = false;              // stop input
            AppData.LevelIdx.Value++;    // advance progress (persisted)
            if (_winPopup == null)
                _winPopup = FindAnyObjectByType<MBWinPopup>(FindObjectsInactive.Include);
            if (_winPopup != null) _winPopup.gameObject.SetActive(true);
            Haptics.Play(GameHaptic.Win); // last, so nothing here can block the popup
        }

        // Quick decaying horizontal shake of the board — feedback for a wrong queen.
        IEnumerator ShakeBoard() {
            if (_board == null) yield break;
            const float dur = 0.3f;
            float mag = _cellSize * 0.1f;
            for (float e = 0f; e < dur; e += Time.unscaledDeltaTime) {
                float x = Mathf.Sin(e * 60f) * mag * (1f - e / dur);
                _board.localPosition = new Vector3(x, 0f, 0f);
                yield return null;
            }
            _board.localPosition = Vector3.zero;
        }

        void Tick() { _lastTick = Time.unscaledTime; Haptics.Play(GameHaptic.Tap); }
        void DragTick() { if (Time.unscaledTime - _lastTick >= TickInterval) Tick(); }

        // World point -> cell, by mapping into $Board local space and rounding to the grid.
        // Returns null for taps that land in the gaps/margin or off the board.
        MBCell HitTest(Vector3 world) {
            if (_cells == null || _board == null) return null;

            Vector3 local = _board.InverseTransformPoint(world);
            float half = (_n - 1) * 0.5f;
            int c = Mathf.RoundToInt(local.x / _step + half);
            int r = Mathf.RoundToInt(half - local.y / _step);
            if (r < 0 || r >= _n || c < 0 || c >= _n) return null;

            float cx = (c - half) * _step;
            float cy = (half - r) * _step;
            float tol = _cellSize * 0.5f;   // only the cell's own area counts, not the gap
            if (Mathf.Abs(local.x - cx) > tol || Mathf.Abs(local.y - cy) > tol) return null;

            return _cells[r, c];
        }

        // ---- start animation: cells pop in 0 -> overshoot -> 1, rippling out from the centre ----

        IEnumerator BloomReveal() {
            float cc = (_n - 1) * 0.5f;
            var delay = new float[_n, _n];
            float maxDelay = 0f;

            for (int r = 0; r < _n; r++) {
                for (int c = 0; c < _n; c++) {
                    float d = Mathf.Sqrt((r - cc) * (r - cc) + (c - cc) * (c - cc)); // rings from centre
                    delay[r, c] = d * _bloomStagger;
                    if (delay[r, c] > maxDelay) maxDelay = delay[r, c];
                    _cells[r, c].transform.localScale = Vector3.zero;
                    _cells[r, c].SetAlpha(0f);
                }
            }

            float total = maxDelay + _bloomPop;
            for (float t = 0f; t < total; t += Time.unscaledDeltaTime) {
                for (int r = 0; r < _n; r++)
                    for (int c = 0; c < _n; c++) {
                        float lt = Mathf.Clamp01((t - delay[r, c]) / _bloomPop);
                        _cells[r, c].transform.localScale = Vector3.one * EaseOutBack(lt);
                        _cells[r, c].SetAlpha(lt);
                    }
                yield return null;
            }

            for (int r = 0; r < _n; r++)
                for (int c = 0; c < _n; c++) {
                    _cells[r, c].transform.localScale = Vector3.one;
                    _cells[r, c].SetAlpha(1f);
                }
        }

        // overshoot ease: rises past 1 then settles back to 1 — the satisfying "pop"
        static float EaseOutBack(float x) {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
