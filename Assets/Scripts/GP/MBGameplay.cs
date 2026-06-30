using Common;
using Core;
using QueensPuzzle;
using System.Collections;
using UnityEngine;

namespace qp {
    public class MBGameplay : MonoBehaviour, IMBTouchesListener {


        // SessionState key the Level Builder writes so it can hand its level to play mode (editor only).
        public const string PlayLevelGuidKey = "qp.playLevelGuid";

        [Header("Board layout (fractions of a cell)")]
        [SerializeField] float _spacing = 0.08f;   // gap between cells
        [SerializeField] float _margin = 0.25f;    // border between the grid and the board edge

        [Header("Start animation")]
        [SerializeField] float _bloomPop = 0.35f;       // per-cell pop duration
        [SerializeField] float _bloomStagger = 0.05f;   // extra delay per ring out from the centre

        enum DragMode { None, PaintX, Erase }

        MBCell[,] _cells;            // [row, col]
        RectTransform _board;
        int _n;
        float _step, _cellSize;
        DragMode _drag;
        MBTouches _touches;
        bool _ready;                 // input gated until the bloom reveal finishes

        IEnumerator Start() {
            yield return BuildBoard();
        }

        // Re-run while already in play mode (called by the Level Builder): clears the current
        // board and rebuilds it from the current level.
        public void Replay() {
            StopAllCoroutines();
            StartCoroutine(BuildBoard());
        }

        IEnumerator BuildBoard() {

            LevelData level = null;
#if UNITY_EDITOR
            if (level == null) {
                string guid = UnityEditor.SessionState.GetString(PlayLevelGuidKey, "");
                if (!string.IsNullOrEmpty(guid)) {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path))
                        level = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(path);
                }
            }
#endif
            if (level == null) {
                Debug.LogError("[MBGameplay] No level assigned — drag a LevelData onto MBGameplay.");
                yield break;
            }

            _ready = false;   // no input while we (re)build and bloom

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
            if (_touches == null) _touches = this.RegisterToRoot();

            // wait for UI to refresh our layout
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            // "Bloom" the cells in from the centre, then let the player interact
            yield return BloomReveal();
            _ready = true;
        }

        void OnDisable() {
            if (_touches != null) { this.UnRegister(_touches); _touches = null; }
        }

        // ---- touch input: paint X / erase on drag, double-click to place a queen ----------

        public void TouchDown(MBTouches.TouchData touch, bool firstTime) {
            if (!_ready || !firstTime) return;
            var cell = HitTest(touch.WorldPoint);
            if (cell == null) { _drag = DragMode.None; return; }

            switch (cell.State) {
                case MBCell.ECellType.EMPTY:
                    _drag = DragMode.PaintX;
                    cell.MarkCell(MBCell.ECellType.X);
                    break;
                case MBCell.ECellType.X:
                    _drag = DragMode.Erase;
                    cell.MarkCell(MBCell.ECellType.EMPTY);
                    break;
                default:
                    _drag = DragMode.None;   // don't paint over queens
                    break;
            }
        }

        public void TouchDrag(MBTouches.TouchData touch, bool samePoint) {
            if (!_ready || samePoint || _drag == DragMode.None) return;
            var cell = HitTest(touch.WorldPoint);
            if (cell == null) return;

            if (_drag == DragMode.PaintX && cell.State == MBCell.ECellType.EMPTY)
                cell.MarkCell(MBCell.ECellType.X);
            else if (_drag == DragMode.Erase && cell.State == MBCell.ECellType.X)
                cell.MarkCell(MBCell.ECellType.EMPTY);
        }

        public void TouchUp(MBTouches.TouchData touch, bool clicked, bool doubleClick) {
            if (!_ready) return;
            if (doubleClick) {
                var cell = HitTest(touch.WorldPoint);
                if (cell != null)
                    cell.MarkCell(cell.IsSolutionQueen ? MBCell.ECellType.QUEEN : MBCell.ECellType.WRONG_QUEEN);
            }
            _drag = DragMode.None;
        }

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
