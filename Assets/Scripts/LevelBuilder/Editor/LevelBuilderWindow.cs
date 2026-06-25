using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QueensPuzzle.EditorTools
{
    /// <summary>
    /// Editor hub for levels: Generate a new unique puzzle, Load a saved one, Save the current
    /// one as an asset, and Play it (phase 1: the game just shows the board).
    ///
    /// NOTE: the difficulty dropdown is a target only — the generator does not enforce it yet
    /// (difficulty rating is a later step). N is honoured; every level is currently Unrated.
    /// </summary>
    public class LevelBuilderWindow : EditorWindow
    {
        const string LevelsFolder = "Assets/Levels";
        const int LoadPickerId = 9210;

        int _requestedN = 8;
        Difficulty _requestedDifficulty = Difficulty.Medium;

        LevelData _level;
        string _status = "Pick the parameters and press Generate.";
        Texture2D _queenTex;

        [MenuItem("QueensPuzzle/Level Builder")]
        public static void Open()
        {
            var w = GetWindow<LevelBuilderWindow>("Level Builder");
            w.minSize = new Vector2(420, 540);
        }

        void OnEnable() => _queenTex = BoardVisuals.CreateQueenTexture(64);

        void OnDisable()
        {
            if (_queenTex != null) DestroyImmediate(_queenTex);
        }

        void OnGUI()
        {
            HandleObjectPicker();
            DrawControls();
            EditorGUILayout.Space(6);
            DrawBoard();
            EditorGUILayout.Space(6);
            DrawFooter();
        }

        // ---- controls ----------------------------------------------------------------

        void DrawControls()
        {
            EditorGUILayout.LabelField("Generate", EditorStyles.boldLabel);

            _requestedDifficulty = (Difficulty)EditorGUILayout.EnumPopup("Difficulty", _requestedDifficulty);
            EditorGUILayout.LabelField(" ", "target only — not enforced yet", EditorStyles.miniLabel);

            _requestedN = EditorGUILayout.IntSlider("Board size (N)", _requestedN,
                LevelGenerator.MinSize, LevelGenerator.MaxSize);
            EditorGUILayout.LabelField(" ", $"= {_requestedN} queens", EditorStyles.miniLabel);

            if (GUILayout.Button("Generate", GUILayout.Height(28))) Generate();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load", GUILayout.Height(24)))
                    EditorGUIUtility.ShowObjectPicker<LevelData>(_level, false, "", LoadPickerId);

                using (new EditorGUI.DisabledScope(_level == null))
                {
                    if (GUILayout.Button("Save", GUILayout.Height(24))) SaveAsset();
                    if (GUILayout.Button("Play", GUILayout.Height(24))) Play();
                }
            }
        }

        void HandleObjectPicker()
        {
            if (Event.current.commandName == "ObjectSelectorClosed"
                && EditorGUIUtility.GetObjectPickerControlID() == LoadPickerId)
            {
                if (EditorGUIUtility.GetObjectPickerObject() is LevelData picked)
                {
                    _level = picked;
                    _status = $"Loaded {picked.name} ({picked.size}x{picked.size}).";
                }
                Repaint();
            }
        }

        // ---- board (shows the solution) ----------------------------------------------

        void DrawBoard()
        {
            float avail = Mathf.Clamp(Mathf.Min(position.width - 24f, position.height - 230f), 220f, 600f);
            Rect board = GUILayoutUtility.GetRect(avail, avail, GUILayout.ExpandWidth(false));
            board.x = (position.width - avail) * 0.5f;

            if (_level == null)
            {
                EditorGUI.DrawRect(board, new Color(0f, 0f, 0f, 0.06f));
                GUI.Label(board, "No level yet — Generate or Load",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 });
                return;
            }

            int n = _level.size;
            float cs = avail / n;
            EditorGUI.DrawRect(board, new Color(0.42f, 0.42f, 0.48f)); // grid lines

            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    var cell = new Rect(board.x + c * cs + 1, board.y + r * cs + 1, cs - 2, cs - 2);
                    EditorGUI.DrawRect(cell, BoardVisuals.RegionColor(_level.RegionAt(r, c), n));

                    if (_level.IsSolutionQueen(r, c))
                        DrawGlyph(cell, _queenTex, new Color(0.13f, 0.13f, 0.17f), 0.78f);
                }
            }
        }

        void DrawGlyph(Rect cell, Texture2D tex, Color tint, float scale)
        {
            float sz = cell.width * scale;
            var r = new Rect(cell.center.x - sz / 2f, cell.center.y - sz / 2f, sz, sz);
            Color prev = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
            GUI.color = prev;
        }

        // ---- footer ------------------------------------------------------------------

        void DrawFooter()
        {
            if (_level != null)
            {
                EditorGUILayout.HelpBox(
                    $"{_level.size}x{_level.size}  ·  seed {_level.seed}  ·  difficulty {_level.difficulty}  ·  " +
                    $"~{Mathf.RoundToInt(_level.estimatedSolveSeconds)}s  ·  unique ✓",
                    MessageType.None);
            }
            EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
        }

        // ---- actions -----------------------------------------------------------------

        void Generate()
        {
            int seed = System.Environment.TickCount;
            LevelData lvl;
            try
            {
                lvl = LevelGenerator.Generate(_requestedN, seed, 250, p =>
                    EditorUtility.DisplayProgressBar("Generating level",
                        $"{_requestedN}x{_requestedN} — finding a unique puzzle…", p));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (lvl == null)
            {
                _status = $"Generation failed for {_requestedN}x{_requestedN} — press Generate again.";
                return;
            }
            _level = lvl;
            _status = $"Generated {lvl.size}x{lvl.size} (seed {seed}). Unique solution ✓";
            Repaint();
        }

        void SaveAsset()
        {
            EnsureLevelsFolder();
            var copy = Instantiate(_level); // keep the working level independent of the asset
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{LevelsFolder}/Level_{_level.size}x{_level.size}.asset");
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
            _status = $"Saved {path}";
        }

        void Play()
        {
            LevelData asset = EnsurePlayableAsset(_level);
            if (asset == null) { _status = "Couldn't prepare a level asset to play."; return; }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // Phase 1: spin up a fresh throwaway scene containing just the game — no scene file
            // to locate or modify. It is discarded automatically when you stop playing.
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("QueensGame").AddComponent<QueensGame>().level = asset;

            EditorApplication.isPlaying = true;
            _status = $"Playing {asset.name} — press the editor Play button again to stop.";
        }

        // ---- helpers -----------------------------------------------------------------

        /// <summary>Returns a persisted asset for the level (so the running game can reference it).
        /// Saved levels are used as-is; an unsaved working level is written to a temp asset.</summary>
        LevelData EnsurePlayableAsset(LevelData lvl)
        {
            if (lvl == null) return null;
            if (AssetDatabase.Contains(lvl)) return lvl;

            EnsureLevelsFolder();
            string tempPath = $"{LevelsFolder}/_Play.asset";
            var existing = AssetDatabase.LoadAssetAtPath<LevelData>(tempPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(lvl, existing);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }
            var copy = Instantiate(lvl);
            AssetDatabase.CreateAsset(copy, tempPath);
            AssetDatabase.SaveAssets();
            return copy;
        }

        static void EnsureLevelsFolder()
        {
            if (!AssetDatabase.IsValidFolder(LevelsFolder))
                AssetDatabase.CreateFolder("Assets", "Levels");
        }
    }
}
