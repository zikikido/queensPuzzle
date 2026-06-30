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

        enum DragMode { None, PaintX, Erase }

        MBCell[,] _cells;            // [row, col]
        RectTransform _board;
        int _n;
        float _step, _cellSize;
        DragMode _drag;
        MBTouches _touches;

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
        }

        void OnDisable() {
            if (_touches != null) { this.UnRegister(_touches); _touches = null; }
        }

        // ---- touch input: paint X / erase on drag, double-click to place a queen ----------

        public void TouchDown(MBTouches.TouchData touch, bool firstTime) {
            if (!firstTime) return;
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
            if (samePoint || _drag == DragMode.None) return;
            var cell = HitTest(touch.WorldPoint);
            if (cell == null) return;

            if (_drag == DragMode.PaintX && cell.State == MBCell.ECellType.EMPTY)
                cell.MarkCell(MBCell.ECellType.X);
            else if (_drag == DragMode.Erase && cell.State == MBCell.ECellType.X)
                cell.MarkCell(MBCell.ECellType.EMPTY);
        }

        public void TouchUp(MBTouches.TouchData touch, bool clicked, bool doubleClick) {
            if (doubleClick) {
                var cell = HitTest(touch.WorldPoint);
                if (cell != null) cell.MarkCell(MBCell.ECellType.QUEEN);
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
    }
}
