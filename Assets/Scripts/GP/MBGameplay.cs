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
        int _levelHash;              // _level.ContentHash() — stamps saves, gates restores
        RectTransform _board;
        int _n;
        float _step, _cellSize;
        DragMode _drag;
        Vector3 _lastDragWorld;      // previous drag sample — fill cells skipped between frames
        MBTouches _touches;
        bool _ready;                 // input gated until the bloom reveal finishes
        MBWinPopup _winPopup;        // found by type (it lives elsewhere in the scene, inactive)
        MBFailPopup _failPopup;      // same pattern as the win popup — shows when the bones run out
        MBTopBar _topBar;            // "queens placed / total" HUD
        Coroutine _shake;            // board shake on a wrong queen
        public PSPool QueenMarkPS;         // burst on every placed queen ($QueenMarkPS under the anims object)
        public PSPool WrongQueenMarkPS;    // burst on every wrong queen ($WorngQueenMarkPS — scene name has the typo)
        float _lastTick;             // throttle so drag-paint haptics are distinct ticks, not a buzz
        const float TickInterval = 0.12f;   // gap must be well over the pulse length or ticks merge
        MBCell _lastTapCell, _prevTapCell;  // last two tapped cells — a double-click must be the same cell

        // Undo: each stroke (one tap or one drag of X edits) is a group of cell changes we can revert.
        // Queens / wrong-queens are never recorded, so they can't be undone.
        struct CellEdit { public int idx; public MBCell.ECellType from; public CellEdit(int i, MBCell.ECellType f) { idx = i; from = f; } }
        readonly List<List<CellEdit>> _undo = new List<List<CellEdit>>();
        List<CellEdit> _stroke;             // the stroke being recorded now (null between strokes)

        // Board input lock counter — overlays (settings popup, ...) do InputLocks++ on open and
        // InputLocks-- on close; any value > 0 blocks all board touches. Int so locks can nest.
        // Locking mid-drag also cancels the gesture: the TouchUp that arrives while locked is
        // swallowed, so without this the half-open stroke would leak (lost undo, stale drag).
        int _inputLocks;
        public int InputLocks {
            get => _inputLocks;
            set {
                _inputLocks = value;
                if (_inputLocks > 0) CancelGesture();
            }
        }

        // Close the in-progress gesture the same way TouchUp would: commit the stroke to undo,
        // stop painting. Safe to call when nothing is in progress.
        void CancelGesture() {
            if (_stroke != null && _stroke.Count > 0) _undo.Add(_stroke);
            _stroke = null;
            _drag = DragMode.None;
        }

        // read-only board access for the tutorial (spotlights need real cells)
        public bool Ready => _ready;
        public int N => _n;
        public int Remaining => _cells == null ? 0 : _n - CountQueens();   // puppies still to place
        public LevelData Level => _level;
        public MBCell CellAt(int row, int col) =>
            _cells != null && row >= 0 && row < _n && col >= 0 && col < _n ? _cells[row, col] : null;

        void Awake() {
            instance = this;
            GPSFX.Load();   // pull the GP clips in now, not on the first sound

            // The tutorial object may be left disabled in the scene; wake it so its Awake runs and
            // registers MBToturial.instance (it puts itself back to sleep after its layout pass).
            var tut = FindAnyObjectByType<MBToturial>(FindObjectsInactive.Include);
            if (tut != null && !tut.gameObject.activeSelf) tut.gameObject.SetActive(true);

            // Same for the win popup: wake it so its Awake wires the button, then hide it again
            // (unlike the tutorial it doesn't self-sleep — it only shows on win).
            _winPopup = FindAnyObjectByType<MBWinPopup>(FindObjectsInactive.Include);
            if (_winPopup != null && !_winPopup.gameObject.activeSelf) {
                _winPopup.gameObject.SetActive(true);
            }

            // Fail popup: same wake-for-layout trick as the win popup (it hides itself after).
            _failPopup = FindAnyObjectByType<MBFailPopup>(FindObjectsInactive.Include);
            if (_failPopup != null && !_failPopup.gameObject.activeSelf) {
                _failPopup.gameObject.SetActive(true);
            }

            _topBar = FindAnyObjectByType<MBTopBar>(FindObjectsInactive.Include);

            // queen-mark bursts: the scene holds one template PS per effect; the pools clone
            // more when placements overlap faster than one effect lasts
            QueenMarkPS = MakePSPool("$QueenMarkPS");
            WrongQueenMarkPS = MakePSPool("$WorngQueenMarkPS", "$WrongQueenMarkPS");
        }

        // A pooled scene PS template found by name — extra names cover a later rename (typo fix).
        // The template lives under the anims container, wherever that sits in the scene —
        // search every root so moving it never breaks the lookup.
        PSPool MakePSPool(params string[] names) {
            foreach (var root in gameObject.scene.GetRootGameObjects()) {
                foreach (var name in names) {
                    var t = root.transform.name == name
                        ? root.transform : root.transform.RecursiveFindChild(name);
                    var ps = t != null ? t.GetComponentInChildren<ParticleSystem>(true) : null;
                    if (ps != null) return new PSPool(ps);
                }
            }
            Debug.LogError($"[MBGameplay] PS template '{names[0]}' not found in scene.");
            return null;
        }

        void OnDestroy() {
            instance = null;
            GPSFX.Release();   // next scene load frees the GP clips; reloads lazily if needed
        }


        IEnumerator Start() {
            WireBoostButtons();
            yield return BuildBoard();
        }

        // Give each boost button its effect. The button owns the click: it spends a boost (and
        // decrements) when there's one, or watches a rewarded ad to earn one when there isn't.
        void WireBoostButtons() {
            foreach (var boost in GetComponentsInChildren<MBBoostButton>(true)) {
                var type = boost.BoostType;
                boost.UseAction = () => UseBoost(type);
            }
        }

        // Run a boost's effect; true when it actually did something (so the button spends one).
        bool UseBoost(EBoostType type) {
            switch (type) {
                case EBoostType.QUEEN: return OpenQueen();
                case EBoostType.HINT:
                    MBToturial.instance?.SetHandVisible(false);   // boost hint: Apply button, no hand
                    MBToturial.instance?.SetApplyVisible(true);
                    // count + sound only here (player's boost) — the tutorial calls OpenHint directly
                    if (OpenHint()) {
                        CommonSFX.Play(GPSFX.Instance.Hint);
                        AppData.LastPlayData.hintsUsed++;
                        AppData.LastPlayData.Save();
                        Analytics.BoostUsed("hint", AppData.LevelIdx.Value, AppData.LevelAttempts.Value);
                        return true;
                    }
                    return false;
                case EBoostType.UNDO: return Undo();
            }
            return false;
        }

        // Queen boost: place the most trivial forced queen; if none is strictly forced (the board
        // would need a guess) still reveal a correct, unplaced solution queen.
        bool OpenQueen() {
            if (!_ready || _cells == null || _level == null) return false;
            GatherBoard(out var queens, out var xs);

            if (SolveTracer.TryQueenBoost(_n, _level.regions, _level.solutionColumns, queens, xs, out int target)) {
                CountQueenBoost();   // before the place — its SaveBoard persists it, and the event precedes a possible game_win
                PlaceBoostQueen(target);
                return true;
            }

            int fallback = FirstUnplacedSolutionQueen();
            if (fallback >= 0) { CountQueenBoost(); PlaceBoostQueen(fallback); return true; }
            Debug.Log("[MBGameplay] Queen boost: nothing to place.");
            return false;
        }

        void CountQueenBoost() {
            AppData.LastPlayData.queenBoostsUsed++;
            Analytics.BoostUsed("queen", AppData.LevelIdx.Value, AppData.LevelAttempts.Value);
        }

        // Place a correct queen from a boost and advance the game (as a manual correct placement).
        void PlaceBoostQueen(int idx) {
            int r = idx / _n, c = idx % _n;
            var cell = _cells[r, c];
            if (cell.State == MBCell.ECellType.QUEEN) return;
            cell.MarkCell(MBCell.ECellType.QUEEN);
            PlayQueens(MBCell.QueenState.HAPPY);   // every opened queen (incl. this one) celebrates
            SaveBoard();
            cell.Pulse();

            int placed = CountQueens();
            _topBar.SetProgress(placed);
            MaybePrepareReview(placed);
            if (placed == _n) Win();
            else {
                Haptics.Play(GameHaptic.Happy);
                CommonSFX.Play(GPSFX.Instance.PlaceQueen);
            }
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
        bool Undo() {
            if (!_ready || _undo.Count == 0) return false;

            var stroke = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            bool any = false;
            foreach (var e in stroke) {
                var cell = _cells[e.idx / _n, e.idx % _n];
                if (cell.State == MBCell.ECellType.QUEEN || cell.State == MBCell.ECellType.WRONG_QUEEN) continue;
                cell.MarkCell(e.from);
                any = true;
            }
            if (any) {
                AppData.LastPlayData.undosUsed++;   // before SaveBoard — it persists the counter too
                SaveBoard();
                Analytics.BoostUsed("undo", AppData.LevelIdx.Value, AppData.LevelAttempts.Value);
                Haptics.Play(GameHaptic.Tap);
            }
            return any;
        }

        // Hint: fix a mistake first (cheapest + necessary), else the next deduction from the board.
        // Public: the first-play tutorial drives the same flow, step by step.
        // Returns true when a hint was actually presented (the boost counts only those).
        public bool OpenHint() {
            if (!_ready || _cells == null || _level == null) return false;
            GatherBoard(out var queens, out var xs);

            // 1) the next real deduction
            if (SolveTracer.TryHint(_n, _level.regions, _level.solutionColumns, queens, xs, out var hint, RegionRichName, PieceName, PieceNamePlural)) {
                PresentHint(hint);
                return true;
            }

            // 2) last resort — a wrong X is hiding a forced queen (strong spoiler, only when stuck)
            foreach (var cell in _cells)
                if (cell.State == MBCell.ECellType.X && cell.IsSolutionQueen) {
                    PresentHint(new Hint { kind = HintKind.WrongX, cells = new[] { cell.Y * _n + cell.X },
                                           note = $"a {PieceName} belongs here — clear this X" });
                    return true;
                }

            Debug.Log("[MBGameplay] Hint: no simple next step (would need a guess).");
            return false;
        }

        // What hint texts call the piece — this game's queens are puppies (another skin: cats).
        const string PieceName = "puppy";
        const string PieceNamePlural = "puppies";

        // How hint texts name a region for the PLAYER: the colour's name, tinted in that colour.
        // Painted levels can remap region → colour, so resolve through the level's mapping.
        string RegionRichName(int g) {
            int clr = _level != null ? _level.ColorOf(g) : g;
            string name = SORegionsColors.NameAt(clr);
            if (string.IsNullOrEmpty(name)) name = ((char)('A' + g)).ToString();
            return $"<color=#{UnityEngine.ColorUtility.ToHtmlStringRGB(SORegionsColors.ColorAt(clr))}>{name}</color>";
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

        // Re-run while already in play mode (win → next level, or Play hit again from the
        // Level Builder): rebuild the board from the current level. The win/fail popups close
        // themselves when their buttons are pressed — Replay never touches them.
        public void Replay() {
            StopAllCoroutines();
            MBToturial.instance?.Hide();
            StartCoroutine(BuildBoard());
        }

        IEnumerator BuildBoard() {

            LevelData level = LevelLoader.LoadLevel();
            if (level == null) {
                Debug.LogError("[MBGameplay] No level to load.");
                yield break;
            }

            _ready = false;   // no input while we (re)build and bloom
            _saveQueued = false;   // a write queued on the OLD board must not land on this one
            SetChromeInteractable(false);   // top/bottom bars locked until the bloom finishes
            _level = level;   // keep for hints
            _levelHash = level.ContentHash();   // saves are stamped with it; restore requires a match


            _reviewPrepareStarted = false;   // each board build may pre-fetch the review flow once

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

            _topBar.Init(_n);

            // load the cells into $Board — n x n grid, centered on the board, row 0 on top
            for (int r = 0; r < n; r++) {
                for (int c = 0; c < n; c++) {
                    var cell = Instantiate(cellPrefab, board, false);
                    cell.name = $"Cell_{r}_{c}";
                    float x = (c - (n - 1) * 0.5f) * step;
                    float y = ((n - 1) * 0.5f - r) * step;
                    cell.transform.localPosition = new Vector3(x, y, 0f);
                    cell.Init(level.ColorOf(level.RegionAt(r, c)), c, r, level.IsSolutionQueen(r, c));
                    _cells[r, c] = cell;
                }
            }

            // same level reopened → back to the exact last state (wrong queens, boost counters);
            // a fresh board starts a fresh attempt (all counters zeroed, all bones back)
            if (!RestoreBoard()) {
                // attempts counter: fresh level → 1; a new attempt on the same level (retry) → +1.
                // A restored board is the SAME attempt — resuming the app never counts.
                if (AppData.AttemptsLevelIdx.Value != AppData.LevelIdx.Value) {
                    AppData.AttemptsLevelIdx.Value = AppData.LevelIdx.Value;
                    AppData.LevelAttempts.Value = 1;
                } else {
                    AppData.LevelAttempts.Value++;
                }
                AppData.LastPlayData = LastPlayData.StartFresh(AppData.LevelIdx.Value);
                RevealQueens(level);

                Analytics.GameStart(AppData.LevelIdx.Value, AppData.LevelAttempts.Value);
            }

            _topBar.SetWrongMoves(AppData.LastPlayData.bonesLost);

            // board size = grid + gaps + margin border on every side
            float boardSize = cellSize * (n + (n - 1) * _spacing + 2f * _margin);
            transform.RecursiveFindChild<RectTransform>("$BoardScaler").sizeDelta = board.sizeDelta = new Vector2(boardSize, boardSize);

            // force repost
            transform.GetComponent<RTResizeInvoker>().ReporteSize();

            // start listening for touches (needs an MBTouches on a scene root)
            if (_touches == null) {
                _touches = this.RegisterToRoot();
                if (_touches != null) {
                    _touches.DoubleClickDelteTime = 0.3f;   // default 1.5s is far too long
                    _touches.TrackFingerId = true;   // a second finger must not hijack a paint drag
                    // default ~1cm rejects sloppy same-cell double-taps; TouchUp's same-cell rule
                    // does the real gating, so the screen-distance gate can be generous (~3cm)
                    _touches.DoubleClickTreshold = _touches.ClickTreshold * 6f;
                }
            }

            // wait for UI to refresh our layout
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            // "Bloom" the cells in from the centre, then let the player interact
            yield return BloomReveal();
            Haptics.Prepare();   // warm the engine so the first tap fires without latency
            _ready = true;
            SetChromeInteractable(true);   // bloom done — bars usable again

            // Safety net — should be IMPOSSIBLE: Win()/Fail() call LastPlayData.Invalidate() (and
            // clear the queued save) before anything else can persist, so a finished board should
            // never exist in a save. But if one ever slips through (crash on the exact win/fail
            // frame, a future code path), restoring it would soft-lock forever: Win/Fail only run
            // on a queen placement, and a finished board has none left to make. Re-check once.
            if (CountQueens() == _n) { Win(); yield break; }
            if (AppData.LastPlayData.bonesLost >= _topBar.MaxWrongMoves) { Fail(); yield break; }

            // banner shows from GameConfig.StartShowBannerAtLevel onward (the strip is always reserved)
            if (AppData.LevelIdx.Value + 1 >= GameConfig.StartShowBannerAtLevel) {
                Ads.ShowBanner();
            }

            MBFirstPlayToturial.TryBegin(this);   // first level ever → guided tutorial

            // restored/revealed queens hold their base pose through the bloom; a beat later the
            // whole board starts idling together
            yield return new WaitForSecondsRealtime(3f);
            PlayQueens(MBCell.QueenState.IDLE);
        }

        // Play a queen animation on every OPENED queen (placed cells only) — all in the same
        // frame, so the flipbooks stay in sync.
        void PlayQueens(MBCell.QueenState state) {
            if (_cells == null) return;
            foreach (var cell in _cells)
                if (cell != null && cell.State == MBCell.ECellType.QUEEN) cell.PlayQueen(state);
        }

        // Lock/unlock the whole chrome (top bar + boost buttons): visible but untouchable
        // while the board builds and blooms — on game enter and after every win/replay.
        void SetChromeInteractable(bool on) {

            _topBar.SetInteractable(on);
            foreach (var boost in GetComponentsInChildren<MBBoostButton>(true))
                boost.SetInteractable(on);
        }


        void OnDisable() {
            if (_saveQueued) FlushBoardSave();   // leaving the scene — don't lose the last edits
            if (_touches != null) { this.UnRegister(_touches); _touches = null; }
        }

        // ---- touch input: paint X / erase on drag, double-click to place a queen ----------

        public void TouchDown(MBTouches.TouchData touch, bool firstTime) {
            if (!_ready || InputLocks > 0 || !firstTime) return;
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
            else { cell.MarkCell(target); SaveBoard(); }
        }

        // Change a cell's X/empty state as part of the current stroke, remembering its prior state.
        void PaintCell(MBCell cell, MBCell.ECellType to) {
            _stroke?.Add(new CellEdit(cell.Y * _n + cell.X, cell.State));
            cell.MarkCell(to);
            SaveBoard();
        }

        // ---- board persistence: every move is saved; reopening the same level restores it ----

        // A drag can paint several cells in ONE frame and each PlayerPrefs.Save() is a synchronous
        // disk flush — so SaveBoard only queues, and LateUpdate writes once per frame.
        bool _saveQueued;

        void SaveBoard() {
            if (_cells != null) _saveQueued = true;
        }

        void LateUpdate() {
            if (_saveQueued) FlushBoardSave();
        }

        void FlushBoardSave() {
            _saveQueued = false;
            if (_cells == null) return;
            var sb = new System.Text.StringBuilder(_n * _n);
            foreach (var cell in _cells) sb.Append(StateChar(cell.State));
            var data = AppData.LastPlayData;
            data.forLevelIdx = AppData.LevelIdx.Value;
            data.levelHash = _levelHash;
            data.board = sb.ToString();
            data.Save();   // one write: board + the attempt's counters
        }

        // Early-level help: the level can ship with some solution queens already on the board.
        // Fresh starts only — a restored board already contains them (SaveBoard saves everything).
        void RevealQueens(LevelData level) {
            if (level.revealedRows == null || level.revealedRows.Length == 0) return;
            foreach (int r in level.revealedRows) {
                var cell = CellAt(r, level.solutionColumns[r]);
                if (cell != null && cell.State != MBCell.ECellType.QUEEN) cell.MarkCell(MBCell.ECellType.QUEEN);
            }
            SaveBoard();
            _topBar.SetProgress(CountQueens());
        }

        // Re-apply the saved marks (wrong queens included) when the same level reopens.
        bool RestoreBoard() {
            if (AppData.LastPlayData.forLevelIdx != AppData.LevelIdx.Value) return false;
            // an app update can redesign a level at the same index and size — the save is for a
            // DIFFERENT puzzle then (stale queens → false win / stuck board), so reject it
            if (AppData.LastPlayData.levelHash != _levelHash) return false;
            string s = AppData.LastPlayData.board;
            if (string.IsNullOrEmpty(s) || s.Length != _n * _n) return false;

            int i = 0;
            foreach (var cell in _cells) {
                var state = CharState(s[i++]);
                if (state != MBCell.ECellType.EMPTY) cell.MarkCell(state);
            }
            _topBar.SetProgress(CountQueens());
            MaybePrepareReview(CountQueens());   // resuming a board that's already 2-from-win
            return true;
        }

        static char StateChar(MBCell.ECellType t) {
            switch (t) {
                case MBCell.ECellType.QUEEN: return 'Q';
                case MBCell.ECellType.X: return 'X';
                case MBCell.ECellType.WRONG_QUEEN: return 'W';
                default: return '0';
            }
        }

        static MBCell.ECellType CharState(char c) {
            switch (c) {
                case 'Q': return MBCell.ECellType.QUEEN;
                case 'X': return MBCell.ECellType.X;
                case 'W': return MBCell.ECellType.WRONG_QUEEN;
                default: return MBCell.ECellType.EMPTY;
            }
        }

        public void TouchDrag(MBTouches.TouchData touch, bool samePoint) {
            if (!_ready || InputLocks > 0 || samePoint || _drag == DragMode.None) return;

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
            if (!_ready || InputLocks > 0) return;
            // a real double-click means both taps landed on the SAME cell — otherwise it's just two quick taps
            if (doubleClick && _lastTapCell != null && _lastTapCell == _prevTapCell && TutorialAllows(_lastTapCell, MBCell.ECellType.QUEEN)) {
                _prevTapCell = null;   // consume it so a third quick tap can't re-trigger
                var cell = _lastTapCell;

                // already holds a queen (right or wrong) — re-tapping must not cost another bone
                if (cell.State == MBCell.ECellType.QUEEN || cell.State == MBCell.ECellType.WRONG_QUEEN) {
                    _drag = DragMode.None;
                    return;
                }

                // This gesture is a queen, not X edits — drop the two taps' X toggles from undo.
                _stroke = null;
                int qidx = cell.Y * _n + cell.X;
                if (_undo.Count > 0 && _undo[_undo.Count - 1].Count == 1 && _undo[_undo.Count - 1][0].idx == qidx)
                    _undo.RemoveAt(_undo.Count - 1);

                bool correct = cell.IsSolutionQueen;
                cell.MarkCell(correct ? MBCell.ECellType.QUEEN : MBCell.ECellType.WRONG_QUEEN);
                SaveBoard();
                if (correct) {
                    PlayQueens(MBCell.QueenState.HAPPY);   // every opened queen (incl. this one) celebrates
                    int placed = CountQueens();
                    _topBar?.SetProgress(placed);
                    MaybePrepareReview(placed);
                    if (placed == _n) Win();
                    else {
                        Haptics.Play(GameHaptic.Happy);
                        CommonSFX.Play(GPSFX.Instance.PlaceQueen);
                    }
                } else {
                    PlayQueens(MBCell.QueenState.DISAPPOINTED);   // a wrong queen — the board is let down
                    AppData.LastPlayData.bonesLost++;   // a bone is lost (saved with the board)
                    AppData.LastPlayData.Save();
                    _topBar?.SetWrongMoves(AppData.LastPlayData.bonesLost);
                    Haptics.Play(GameHaptic.Wrong);
                    CommonSFX.Play(GPSFX.Instance.Error);
                    if (_shake != null) StopCoroutine(_shake);
                    _shake = StartCoroutine(ShakeBoard());
                    if (AppData.LastPlayData.bonesLost >= _topBar.MaxWrongMoves) Fail();   // last bone gone
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
        public int CountQueens() {
            int placed = 0;
            foreach (var cell in _cells)
                if (cell.State == MBCell.ECellType.QUEEN) placed++;
            return placed;
        }

        // ---- in-app review: pre-fetch the flow 2 dogs before the win, show it with the win popup.
        // The platform (day period + store quota, handled in Common.ReviewManager) decides if the
        // dialog actually appears — we just ask on every qualifying win (level > 5).

        bool _reviewPrepareStarted;   // once per board build

        void MaybePrepareReview(int placed) {
#if !IGNORE_COMMON_REVIEW
            if (_reviewPrepareStarted || placed < _n - 2 || placed >= _n) return;
            if (AppData.LevelIdx.Value + 1 <= 9) return;  
            _reviewPrepareStarted = true;
            StartCoroutine(PrepareReview());
#endif
        }
#if !IGNORE_COMMON_REVIEW
        IEnumerator PrepareReview() {
            float t0 = Time.realtimeSinceStartup;
            yield return ReviewManager.Instance.Preapre();
            int ms = Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f);
            Analytics.ReviewPrepareTime(ms, ReviewManager.Instance.Preapred);
        }
#endif
        void Win() {
            _ready = false;              // stop input
            PlayQueens(MBCell.QueenState.HAPPY);   // the whole board celebrates, in sync
            Ads.HideBanner();            // banner off while the win popup is up
            Analytics.GameWin(AppData.LevelIdx.Value, AppData.LevelAttempts.Value);   // before LevelIdx++
            AppData.LastPlayData.Invalidate();   // level done — the saved attempt is history
            _saveQueued = false;   // a queued write would resurrect the board under the NEXT level
            AppData.LevelIdx.Value++;    // advance progress (persisted)
            if (_winPopup == null)
                _winPopup = FindAnyObjectByType<MBWinPopup>(FindObjectsInactive.Include);
            Debug.Log($"[MBGameplay] Win — popup {(_winPopup != null ? "found" : "MISSING")}");

            
            float happyLen = _cells[0, 0] != null ? _cells[0, 0].GetStateLength(MBCell.QueenState.HAPPY) : 0f;
            StartCoroutine(ShowWinPopupAfter(happyLen > 0f ? happyLen : 3f));

            Haptics.Play(GameHaptic.Win); // last, so nothing here can block the popup
            CommonSFX.Play(GPSFX.Instance.Win);
        }

        IEnumerator ShowWinPopupAfter(float seconds) {
            yield return new WaitForSecondsRealtime(seconds);
            if (_winPopup != null) _winPopup.Show();
#if !IGNORE_COMMON_REVIEW
            StartCoroutine(ReviewManager.Instance.TryReview());   // no-op unless Preapre finished
#endif
        }

        void Fail() {
            _ready = false;   // stop input; Continue or Reset decides what's next
            PlayQueens(MBCell.QueenState.CRY);   // the board mourns (overrides the disappointment)
            Ads.HideBanner();            // banner off while the fail popup is up
            Analytics.GameLose(AppData.LevelIdx.Value, AppData.LevelAttempts.Value);

            // Failed → the attempt is over: quitting now restarts the level fresh.
            // But Continue may revive it — it waits in the stash until the popup decides.
            AppData.LastPlayData.Stash();
            AppData.LastPlayData.Invalidate();
            _saveQueued = false;   // a queued write would resurrect the invalidated attempt

            if (_failPopup == null)
                _failPopup = FindAnyObjectByType<MBFailPopup>(FindObjectsInactive.Include);
            Debug.Log($"[MBGameplay] Fail — popup {(_failPopup != null ? "found" : "MISSING")}");
            CommonSFX.Play(GPSFX.Instance.Fail);

            // hold the popup for exactly one cry — but only when a queen is on screen to cry;
            // an empty board (all bones lost on wrong queens) gets the popup right away
            float wait = 0f;
            if (CountQueens() > 0) {
                float cryLen = _cells[0, 0] != null ? _cells[0, 0].GetStateLength(MBCell.QueenState.CRY) : 0f;
                wait = cryLen > 0f ? cryLen : 3f;
            }
            StartCoroutine(ShowFailPopupAfter(wait));
        }

        IEnumerator ShowFailPopupAfter(float seconds) {
            yield return new WaitForSecondsRealtime(seconds);
            if (_failPopup != null) _failPopup.Show();
        }

        // Fail-continue: every bone returns, but the wrong queens stay on the board — they're
        // permanent X's now. Re-saves the board that Fail() invalidated.
        public void ContinueAfterFail() {
            AppData.LastPlayData = LastPlayData.Unstash();   // the attempt Fail() invalidated is alive again
            AppData.LastPlayData.bonesLost = 0;
            AppData.LastPlayData.livesAdded += GameConfig.BonesAddedAfterRewarded;
            SaveBoard();
            Analytics.LivesAdded(GameConfig.BonesAddedAfterRewarded, AppData.LevelIdx.Value, AppData.LevelAttempts.Value);
            _topBar?.SetWrongMoves(0);
            _ready = true;

            // resume without a bloom → re-show the banner ourselves (same level gate as BuildBoard)
            if (AppData.LevelIdx.Value + 1 >= GameConfig.StartShowBannerAtLevel) Ads.ShowBanner();
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

        void Tick() {
            _lastTick = Time.unscaledTime;
            // one tick per painted cell, XMark or Erase by drag mode (same throttle for both)
            Haptics.Play(_drag == DragMode.Erase ? GameHaptic.Tap : GameHaptic.XMark);
            CommonSFX.Play(_drag == DragMode.Erase ? GPSFX.Instance.Erase : GPSFX.Instance.XMark);
        }
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
            CommonSFX.Play(GPSFX.Instance.BoardStart);

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
