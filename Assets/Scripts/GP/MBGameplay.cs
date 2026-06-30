using Common;
using Core;
using QueensPuzzle;
using System.Collections;
using UnityEngine;

namespace qp {
    public class MBGameplay : MonoBehaviour {


        // SessionState key the Level Builder writes so it can hand its level to play mode (editor only).
        public const string PlayLevelGuidKey = "qp.playLevelGuid";

        [Header("Board layout (fractions of a cell)")]
        [SerializeField] float _spacing = 0.08f;   // gap between cells
        [SerializeField] float _margin = 0.25f;    // border between the grid and the board edge

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

            // load the cells into $Board — n x n grid, centered on the board, row 0 on top
            for (int r = 0; r < n; r++) {
                for (int c = 0; c < n; c++) {
                    var cell = Instantiate(cellPrefab, board, false);
                    cell.name = $"Cell_{r}_{c}";
                    float x = (c - (n - 1) * 0.5f) * step;
                    float y = ((n - 1) * 0.5f - r) * step;
                    cell.transform.localPosition = new Vector3(x, y, 0f);
                    cell.Init(level.RegionAt(r, c), c, r, level.IsSolutionQueen(r, c));
                }
            }

            // board size = grid + gaps + margin border on every side
            float boardSize = cellSize * (n + (n - 1) * _spacing + 2f * _margin);
            transform.RecursiveFindChild<RectTransform>("$BoardScaler").sizeDelta = board.sizeDelta = new Vector2(boardSize, boardSize);

            // force repost
            transform.GetComponent<RTResizeInvoker>().ReporteSize();

            // wait for UI to refresh our layout
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
        }
    }
}
