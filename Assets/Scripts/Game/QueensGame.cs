using UnityEngine;
using UnityEngine.UI;

namespace QueensPuzzle
{
    /// <summary>
    /// Runtime game component (phase 1): loads a <see cref="LevelData"/> and draws the board —
    /// colored regions plus the solution crowns. No gameplay/interaction yet.
    ///
    /// The board UI (Canvas + cells) is built in code at Start, so the play scene only needs a
    /// single GameObject with this component and a level assigned.
    /// </summary>
    [AddComponentMenu("QueensPuzzle/Queens Game")]
    public class QueensGame : MonoBehaviour
    {
        [Tooltip("Level to display. Assign a generated/saved LevelData asset.")]
        public LevelData level;

        void Start()
        {
            if (level == null)
            {
                Debug.LogWarning("[QueensGame] No level assigned — nothing to show.");
                return;
            }
            EnsureCamera();
            BuildBoard();
        }

        void EnsureCamera()
        {
            if (Camera.main != null) return;
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        }

        void BuildBoard()
        {
            var canvasGo = new GameObject("BoardCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            int n = level.size;
            float boardSize = Mathf.Min(Screen.width, Screen.height) * 0.82f;
            float cell = boardSize / n;
            float gap = Mathf.Max(1f, cell * 0.04f);

            var board = new GameObject("Board", typeof(RectTransform), typeof(Image));
            var brt = (RectTransform)board.transform;
            brt.SetParent(canvasGo.transform, false);
            brt.sizeDelta = new Vector2(boardSize, boardSize);
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            board.GetComponent<Image>().color = new Color(0.30f, 0.30f, 0.34f); // shows through gaps as grid lines

            Sprite queen = BoardVisuals.CreateQueenSprite(64);

            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    var cellGo = new GameObject($"Cell_{r}_{c}", typeof(RectTransform), typeof(Image));
                    var crt = (RectTransform)cellGo.transform;
                    crt.SetParent(brt, false);
                    crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0f, 1f); // top-left origin
                    crt.sizeDelta = new Vector2(cell - gap, cell - gap);
                    crt.anchoredPosition = new Vector2(c * cell + gap * 0.5f, -(r * cell + gap * 0.5f));
                    cellGo.GetComponent<Image>().color = BoardVisuals.RegionColor(level.RegionAt(r, c), n);

                    if (level.IsSolutionQueen(r, c))
                    {
                        var qGo = new GameObject("Queen", typeof(RectTransform), typeof(Image));
                        var qrt = (RectTransform)qGo.transform;
                        qrt.SetParent(crt, false);
                        qrt.anchorMin = Vector2.zero;
                        qrt.anchorMax = Vector2.one;
                        qrt.sizeDelta = new Vector2(-cell * 0.24f, -cell * 0.24f); // inset padding
                        qrt.anchoredPosition = Vector2.zero;
                        var qi = qGo.GetComponent<Image>();
                        qi.sprite = queen;
                        qi.color = new Color(0.13f, 0.13f, 0.17f);
                        qi.preserveAspect = true;
                    }
                }
            }
        }
    }
}
