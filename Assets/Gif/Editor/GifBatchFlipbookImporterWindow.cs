#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Kido.GifImporter.Editor
{
    internal sealed class GifBatchFlipbookImporterWindow : EditorWindow
    {
        const string LastOutputKey = "Kido.GifBatchImporter.LastOutputFolder";

        sealed class GifItem
        {
            public string Path;
            public GifAnimationData Data;
            public bool Expanded = true;
            public int PreviewFrame;
            public double NextPreviewTime;
            public Texture2D PreviewTexture;
            public byte[] PreviewBuffer;
            public int PreviewTextureFrame = -1;
            public readonly List<string> FrameHashes = new List<string>();
            public int UniqueSpriteCount;
            public int ZeroDelayCount;
            public int SharedSpriteCount;
            public float Length;
            public string Error;
        }

        sealed class FrameCandidate
        {
            public string Hash;
            public int Width;
            public int Height;
            public byte[] Rgba;
            public string AssetPath;
            public readonly List<string> Uses = new List<string>();
            public Texture2D PreviewTexture;
        }

        sealed class GenerationPlan
        {
            public string AssetPath;
            public string[] SpritePaths;
            public float[] Durations;
        }

        readonly List<GifItem> _items = new List<GifItem>();
        readonly Dictionary<string, FrameCandidate> _analysisLibrary = new Dictionary<string, FrameCandidate>();
        Vector2 _scroll;
        string _manualPath = "";
        string _outputFolder = "";
        float _zeroFallback = 1f / 30f;
        bool _ignoreDuplicates = true;
        bool _compareExisting = true;
        bool _fuzzyDuplicates;
        float _fuzzyTolerance = 0.015f;
        bool _showSpriteLibrary = true;
        bool _autoPlay;
        bool _analysisDirty = true;
        bool _outputFolderPinned;

        [MenuItem("Tools/Kido/GIF Batch Flipbook Importer")]
        public static void Open() => GetWindow<GifBatchFlipbookImporterWindow>("GIF → Flipbooks");

        [MenuItem("Assets/Kido/Import Selected GIFs as Flipbooks", false, 2000)]
        static void OpenSelected()
        {
            var paths = Selection.objects.Select(AssetDatabase.GetAssetPath)
                .Where(IsGifPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var w = GetWindow<GifBatchFlipbookImporterWindow>("GIF → Flipbooks");
            w.AddPaths(paths);
            w.Focus();
        }

        [MenuItem("Assets/Kido/Import Selected GIFs as Flipbooks", true)]
        static bool ValidateOpenSelected() => Selection.objects.Any(o => IsGifPath(AssetDatabase.GetAssetPath(o)));

        void OnEnable()
        {
            minSize = new Vector2(760, 560);
            string saved = EditorPrefs.GetString(LastOutputKey, "");
            if (AssetDatabase.IsValidFolder(saved)) _outputFolder = saved;
            EditorApplication.update += TickPreview;
        }

        void OnDisable()
        {
            EditorApplication.update -= TickPreview;
            foreach (var i in _items) DestroyPreviews(i);
            DestroyAnalysisPreviews();
        }

        void TickPreview()
        {
            if (!_autoPlay) return;
            bool changed = false;
            double now = EditorApplication.timeSinceStartup;
            foreach (var item in _items)
            {
                if (!item.Expanded || item.Data == null || item.Data.Frames.Count == 0) continue;
                if (now < item.NextPreviewTime) continue;
                item.PreviewFrame = (item.PreviewFrame + 1) % item.Data.Frames.Count;
                float d = EffectiveDuration(item.Data.Frames[item.PreviewFrame]);
                item.NextPreviewTime = now + Math.Max(0.01f, d);
                changed = true;
            }
            if (changed) Repaint();
        }

        void OnGUI()
        {
            DrawDropArea();
            DrawToolbar();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawItems();
            EditorGUILayout.Space(10);
            DrawAnalysisControls();
            EditorGUILayout.Space(10);
            DrawSettings();
            EditorGUILayout.Space(10);
            DrawSpriteLibrary();
            EditorGUILayout.Space(10);
            DrawOutput();
            EditorGUILayout.EndScrollView();
            using (new EditorGUI.DisabledScope(_items.Count == 0 || _items.Any(x => x.Data == null) || !AssetDatabase.IsValidFolder(_outputFolder)))
                if (GUILayout.Button($"Generate {_items.Count} Flipbook Animation{(_items.Count == 1 ? "" : "s")}", GUILayout.Height(40))) Generate();
        }

        void DrawDropArea()
        {
            Rect r = GUILayoutUtility.GetRect(0, 70, GUILayout.ExpandWidth(true));
            GUI.Box(r, "Drag one or more GIF files here\nRight-click selected GIFs in Project → Kido → Import Selected GIFs", EditorStyles.helpBox);
            Event e = Event.current;
            if (!r.Contains(e.mousePosition)) return;
            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); AddPaths(DragAndDrop.paths); }
                e.Use();
            }
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            _manualPath = EditorGUILayout.TextField("GIF path", _manualPath);
            if (GUILayout.Button("Add", GUILayout.Width(55))) { AddPaths(new[] { _manualPath }); _manualPath = ""; }
            if (GUILayout.Button("Browse…", GUILayout.Width(80)))
            {
                string p = EditorUtility.OpenFilePanelWithFilters("Choose GIF files", GetBrowseDirectory(), new[] { "GIF", "gif" });
                AddPaths(new[] { p });
            }
            if (GUILayout.Button("Clear", GUILayout.Width(55))) ClearItems();
            EditorGUILayout.EndHorizontal();
        }

        void DrawItems()
        {
            EditorGUILayout.LabelField($"Animations ({_items.Count})", EditorStyles.boldLabel);
            if (_items.Count == 0) { EditorGUILayout.HelpBox("Add GIF files to preview and import.", MessageType.Info); return; }
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                item.Expanded = EditorGUILayout.Foldout(item.Expanded, Path.GetFileName(item.Path), true, EditorStyles.foldoutHeader);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", GUILayout.Width(68))) { RemoveAt(i--); EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); continue; }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(item.Error)) EditorGUILayout.HelpBox(item.Error, MessageType.Error);
                else if (item.Data != null && item.Expanded)
                {
                    string sharedText = _analysisDirty ? "Build analysis to calculate" : item.SharedSpriteCount.ToString();
                    EditorGUILayout.LabelField($"Frames: {item.Data.Frames.Count}   Unique sprites: {item.UniqueSpriteCount}   Duplicates: {item.Data.Frames.Count - item.UniqueSpriteCount}");
                    EditorGUILayout.LabelField($"Shared with other animations: {sharedText}   Size: {item.Data.Width}×{item.Data.Height}   Zero-delay: {item.ZeroDelayCount}   Length: {item.Length:0.###}s");
                    DrawAnimationPreview(item);
                    DrawTimeline(item);
                }
                EditorGUILayout.EndVertical();
            }
        }

        void DrawAnimationPreview(GifItem item)
        {
            EnsurePreview(item);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(180));
            Rect previewRect = GUILayoutUtility.GetRect(180, 180, GUILayout.Width(180), GUILayout.Height(180));
            GUI.Box(previewRect, GUIContent.none);
            if (item.PreviewTexture != null)
                GUI.DrawTexture(previewRect, item.PreviewTexture, ScaleMode.ScaleToFit, true);
            DrawFrameSpriteName(FrameHash(item, item.PreviewFrame));
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical();
            _autoPlay = EditorGUILayout.ToggleLeft("Auto play previews", _autoPlay);
            EditorGUI.BeginChangeCheck();
            item.PreviewFrame = EditorGUILayout.IntSlider("Frame", item.PreviewFrame, 0, item.Data.Frames.Count - 1);
            if (EditorGUI.EndChangeCheck())
            {
                item.PreviewTextureFrame = -1;
                Repaint();
            }
            var f = item.Data.Frames[item.PreviewFrame];
            float effective = EffectiveDuration(f);
            string timing = f.RawDelayCentiseconds == 0 ? $"0 ms → fallback {effective * 1000f:0.#} ms" : $"{f.RawDelayCentiseconds * 10} ms";
            EditorGUILayout.LabelField("Timing", timing);
            string hash = FrameHash(item, item.PreviewFrame);
            if (!_analysisDirty && _analysisLibrary.TryGetValue(hash, out var c))
            {
                EditorGUILayout.LabelField("Sprite reuse", c.Uses.Count > 1 ? $"Reused {c.Uses.Count} times" : "Unique in current batch");
                foreach (string use in c.Uses.Take(8)) EditorGUILayout.LabelField("• " + use, EditorStyles.miniLabel);
                if (c.Uses.Count > 8) EditorGUILayout.LabelField($"…and {c.Uses.Count - 8} more", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        static string FrameHash(GifItem item, int frame)
        {
            if (item.FrameHashes.Count > frame) return item.FrameHashes[frame];
            var f = item.Data.Frames[frame];
            return HashFrame(f.Width, f.Height, f.Rgba);
        }

        void DrawFrameSpriteName(string hash)
        {
            string spriteFile = hash.Substring(0, 20) + ".png";
            string spritePath = CombineAsset(CombineAsset(_outputFolder, "Images"), spriteFile);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(spritePath);
            if (tex != null)
            {
                var content = new GUIContent(tex.name, spritePath + "\nClick to select in Project");
                if (GUILayout.Button(content, EditorStyles.linkLabel, GUILayout.Width(180)))
                {
                    UnityEngine.Object target = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                    if (target == null) target = tex;
                    Selection.activeObject = target;
                    EditorGUIUtility.PingObject(target);
                }
            }
            else
            {
                var content = new GUIContent(spriteFile + " (not generated)", "Sprite asset does not exist yet — click Generate to create it.");
                EditorGUILayout.LabelField(content, EditorStyles.miniLabel, GUILayout.Width(180));
            }
        }

        void DrawTimeline(GifItem item)
        {
            EditorGUILayout.LabelField("Timeline", EditorStyles.boldLabel);
            float total = Mathf.Max(0.001f, item.Data.Frames.Sum(EffectiveDuration));
            Rect row = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            float x = row.x;
            for (int i = 0; i < item.Data.Frames.Count; i++)
            {
                var f = item.Data.Frames[i];
                float w = Mathf.Max(3f, row.width * EffectiveDuration(f) / total);
                Rect cell = new Rect(x, row.y, w, row.height);
                bool selected = i == item.PreviewFrame;
                GUI.Box(cell, selected ? $"[{i}]" : i.ToString(), selected ? EditorStyles.helpBox : EditorStyles.miniButton);
                if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) item.PreviewFrame = i;
                x += w;
                if (x >= row.xMax) break;
            }
        }

        void DrawSettings()
        {
            EditorGUILayout.LabelField("Import settings", EditorStyles.boldLabel);
            float oldFallback = _zeroFallback;
            _zeroFallback = Mathf.Max(0.001f, EditorGUILayout.FloatField("0 ms fallback (seconds)", _zeroFallback));
            EditorGUI.BeginChangeCheck();
            _ignoreDuplicates = EditorGUILayout.Toggle("Ignore duplicate images", _ignoreDuplicates);
            using (new EditorGUI.DisabledScope(!_ignoreDuplicates))
            {
                _compareExisting = EditorGUILayout.Toggle("Reuse existing Images sprites", _compareExisting);
                _fuzzyDuplicates = EditorGUILayout.Toggle("Fuzzy duplicate matching", _fuzzyDuplicates);
                using (new EditorGUI.DisabledScope(!_fuzzyDuplicates))
                    _fuzzyTolerance = EditorGUILayout.Slider("Fuzzy tolerance", _fuzzyTolerance, 0.001f, 0.10f);
            }
            if (EditorGUI.EndChangeCheck()) MarkAnalysisDirty();
            if (!Mathf.Approximately(oldFallback, _zeroFallback))
            {
                foreach (var i in _items)
                {
                    i.NextPreviewTime = 0;
                    UpdateBasicStats(i);
                }
            }
            EditorGUILayout.HelpBox("Existing animation .asset files are updated in place, not deleted. Their .meta GUID stays unchanged, so prefab/script references remain valid.", MessageType.Info);
        }

        void DrawSpriteLibrary()
        {
            _showSpriteLibrary = EditorGUILayout.Foldout(_showSpriteLibrary, $"Shared sprite analysis ({(_analysisDirty ? "not built" : _analysisLibrary.Count + " unique")})", true);
            if (!_showSpriteLibrary) return;
            if (_analysisDirty) { EditorGUILayout.HelpBox("Click Build Analysis above. The heavy duplicate scan and sprite-library preview are manual to keep the window responsive.", MessageType.Info); return; }
            int shown = 0;
            foreach (var c in _analysisLibrary.Values.OrderByDescending(x => x.Uses.Count))
            {
                if (shown++ >= 100) { EditorGUILayout.LabelField("Showing first 100 sprites.", EditorStyles.miniLabel); break; }
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                if (c.PreviewTexture == null) c.PreviewTexture = MakePreviewTexture(c.Width, c.Height, c.Rgba);
                Rect r = GUILayoutUtility.GetRect(54, 54, GUILayout.Width(54), GUILayout.Height(54));
                GUI.DrawTexture(r, c.PreviewTexture, ScaleMode.ScaleToFit, true);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(c.Hash.Substring(0, 12), EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Used {c.Uses.Count} time{(c.Uses.Count == 1 ? "" : "s")}");
                foreach (string use in c.Uses.Take(6)) EditorGUILayout.LabelField("• " + use, EditorStyles.miniLabel);
                if (c.Uses.Count > 6) EditorGUILayout.LabelField($"…and {c.Uses.Count - 6} more", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawOutput()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField("Folder", _outputFolder);
            if (GUILayout.Button("Choose…", GUILayout.Width(85))) ChooseOutputFolder();
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_outputFolder)) EditorGUILayout.LabelField("Creates", _outputFolder + "/Images and one .asset per GIF");
        }

        void AddPaths(IEnumerable<string> paths)
        {
            foreach (string raw in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string p = raw.Trim().Trim('"');
                if (!Path.IsPathRooted(p) && p.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)) p = AssetPathToAbsolute(p);
                p = Path.GetFullPath(p).Replace('\\', '/');
                if (!File.Exists(p) || !IsGifPath(p) || _items.Any(x => string.Equals(x.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
                var item = new GifItem { Path = p };
                try
                {
                    item.Data = GifDecoder.Decode(p);
                    UpdateBasicStats(item);
                }
                catch (Exception ex) { item.Error = ex.Message; }
                _items.Add(item);
            }
            if (!_outputFolderPinned || !AssetDatabase.IsValidFolder(_outputFolder)) _outputFolder = DefaultOutputFolder();
            MarkAnalysisDirty();
            Repaint();
        }

        void DrawAnalysisControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Shared sprite analysis", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_items.Count == 0 || _items.Any(x => x.Data == null)))
            {
                if (GUILayout.Button(_analysisDirty ? "Build Analysis" : "Rebuild Analysis", GUILayout.Width(130), GUILayout.Height(26)))
                    RebuildAnalysis();
            }
            EditorGUILayout.EndHorizontal();
            if (_analysisDirty)
                EditorGUILayout.HelpBox("Analysis is not built or is out of date. Click Build Analysis when you want to calculate unique/shared sprites. It no longer runs automatically.", MessageType.Info);
            else
                EditorGUILayout.LabelField($"Ready: {_analysisLibrary.Count} unique sprites across {_items.Count} GIFs.", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        void MarkAnalysisDirty()
        {
            _analysisDirty = true;
            DestroyAnalysisPreviews();
            _analysisLibrary.Clear();
        }

        void DestroyAnalysisPreviews()
        {
            foreach (var c in _analysisLibrary.Values)
                if (c.PreviewTexture != null) DestroyImmediate(c.PreviewTexture);
        }

        void RebuildAnalysis()
        {
            DestroyAnalysisPreviews();
            _analysisLibrary.Clear();
            foreach (var item in _items.Where(x => x.Data != null))
            {
                string animName = Sanitize(Path.GetFileNameWithoutExtension(item.Path));
                for (int i = 0; i < item.Data.Frames.Count; i++)
                {
                    var f = item.Data.Frames[i];
                    string h = item.FrameHashes.Count > i
                        ? item.FrameHashes[i]
                        : HashFrame(f.Width, f.Height, f.Rgba);
                    if (!_analysisLibrary.TryGetValue(h, out var c))
                    {
                        c = new FrameCandidate { Hash = h, Width = f.Width, Height = f.Height, Rgba = f.Rgba };
                        _analysisLibrary.Add(h, c);
                    }
                    c.Uses.Add($"{animName} — frame {i}");
                }
            }
            foreach (var item in _items.Where(x => x.Data != null))
                item.SharedSpriteCount = CountSpritesSharedWithOtherAnimations(item);
            _analysisDirty = false;
            Repaint();
        }

        int CountSpritesSharedWithOtherAnimations(GifItem target)
        {
            if (target == null || target.Data == null) return 0;
            string targetName = Sanitize(Path.GetFileNameWithoutExtension(target.Path));
            var hashes = target.FrameHashes.Distinct(StringComparer.Ordinal);
            int count = 0;
            foreach (string hash in hashes)
            {
                if (!_analysisLibrary.TryGetValue(hash, out var candidate)) continue;
                if (candidate.Uses.Any(u => !u.StartsWith(targetName + " —", StringComparison.Ordinal))) count++;
            }
            return count;
        }

        void Generate()
        {
            try
            {
                string imagesFolder = CombineAsset(_outputFolder, "Images");
                EnsureAssetFolder(imagesFolder);
                var existing = BuildExistingLibrary(imagesFolder);
                var current = new List<FrameCandidate>(existing.Values);
                int created = 0, reused = 0, updated = 0, unchanged = 0;
                var plans = new List<GenerationPlan>(_items.Count);

                // Every import of a sprite repacks the whole sprite atlas from scratch, so the work is
                // split into three phases and the AssetDatabase is held closed for as long as possible.
                // Phase 1 only writes PNG bytes to disk; nothing is imported until StopAssetEditing.
                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int gi = 0; gi < _items.Count; gi++)
                    {
                        var item = _items[gi];
                        EditorUtility.DisplayProgressBar("Import GIF animations", "Writing frames: " + Path.GetFileName(item.Path), gi / (float)Math.Max(1, _items.Count) * 0.7f);
                        string animName = Sanitize(Path.GetFileNameWithoutExtension(item.Path));
                        var plan = new GenerationPlan
                        {
                            AssetPath = CombineAsset(_outputFolder, animName + ".asset"),
                            SpritePaths = new string[item.Data.Frames.Count],
                            Durations = new float[item.Data.Frames.Count]
                        };

                        for (int fi = 0; fi < item.Data.Frames.Count; fi++)
                        {
                            var f = item.Data.Frames[fi];
                            plan.Durations[fi] = EffectiveDuration(f);
                            string hash = HashFrame(f.Width, f.Height, f.Rgba);
                            FrameCandidate match = null;
                            if (_ignoreDuplicates)
                            {
                                match = current.FirstOrDefault(x => x.Hash == hash);
                                if (match == null && _fuzzyDuplicates)
                                    match = current.FirstOrDefault(x => x.Width == f.Width && x.Height == f.Height && FuzzyDifference(x.Rgba, f.Rgba) <= _fuzzyTolerance);
                            }
                            if (match != null) { plan.SpritePaths[fi] = match.AssetPath; reused++; continue; }

                            string imagePath = CombineAsset(imagesFolder, hash.Substring(0, 20) + ".png");
                            if (!File.Exists(AssetPathToAbsolute(imagePath))) { WritePng(imagePath, f.Width, f.Height, f.Rgba); created++; }
                            var c = new FrameCandidate { Hash = hash, Width = f.Width, Height = f.Height, Rgba = f.Rgba, AssetPath = imagePath };
                            current.Add(c);
                            plan.SpritePaths[fi] = imagePath;
                        }
                        plans.Add(plan);
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }

                // Phase 2: import every new PNG at once. They arrive as plain textures, not sprites, so
                // this pass costs no atlas work. The importer settings that do turn them into sprites are
                // then batched into a single reimport, which repacks the atlas exactly once.
                EditorUtility.DisplayProgressBar("Import GIF animations", "Importing frames", 0.75f);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var allSpritePaths = plans.SelectMany(p => p.SpritePaths).Distinct().ToArray();
                EditorUtility.DisplayProgressBar("Import GIF animations", "Packing sprites", 0.85f);
                AssetDatabase.StartAssetEditing();
                try { foreach (string p in allSpritePaths) ConfigureSpriteImporter(p); }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // Phase 3: the sprites exist now, so link them into the animation assets.
                for (int gi = 0; gi < plans.Count; gi++)
                {
                    var plan = plans[gi];
                    EditorUtility.DisplayProgressBar("Import GIF animations", "Writing animations", 0.9f + gi / (float)Math.Max(1, plans.Count) * 0.1f);
                    var sprites = plan.SpritePaths.Select(p => AssetDatabase.LoadAssetAtPath<Sprite>(p)).ToArray();

                    var anim = AssetDatabase.LoadAssetAtPath<qp.SOFlipbookAnim>(plan.AssetPath);
                    if (anim == null)
                    {
                        anim = ScriptableObject.CreateInstance<qp.SOFlipbookAnim>();
                        anim.frames = sprites;
                        anim.frameDurations = plan.Durations;
                        AssetDatabase.CreateAsset(anim, plan.AssetPath);
                        updated++;
                    }
                    else if (Same(anim, sprites, plan.Durations)) unchanged++;
                    else
                    {
                        Undo.RecordObject(anim, "Update Flipbook Animation");
                        anim.frames = sprites;
                        anim.frameDurations = plan.Durations;
                        EditorUtility.SetDirty(anim);
                        updated++;
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorPrefs.SetString(LastOutputKey, _outputFolder);
                EditorUtility.DisplayDialog("Import complete", $"Animations updated/new: {updated}\nUnchanged: {unchanged}\nNew shared images: {created}\nReused frame references: {reused}\n\nExisting .asset GUIDs were preserved.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("GIF import failed", ex.Message, "OK");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        Dictionary<string, FrameCandidate> BuildExistingLibrary(string imagesFolder)
        {
            var map = new Dictionary<string, FrameCandidate>();
            if (!_compareExisting || !AssetDatabase.IsValidFolder(imagesFolder)) return map;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { imagesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                try
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(File.ReadAllBytes(AssetPathToAbsolute(path)), false)) { DestroyImmediate(tex); continue; }
                    byte[] rgbaBottomUp = tex.GetRawTextureData().ToArray();
                    byte[] rgba = FlipRows(rgbaBottomUp, tex.width, tex.height);
                    string h = HashFrame(tex.width, tex.height, rgba);
                    map[h] = new FrameCandidate { Hash = h, Width = tex.width, Height = tex.height, Rgba = rgba, AssetPath = path };
                    DestroyImmediate(tex);
                }
                catch { }
            }
            return map;
        }

        static bool Same(qp.SOFlipbookAnim anim, Sprite[] sprites, float[] durations)
        {
            if (anim.frames == null || anim.frameDurations == null || anim.frames.Length != sprites.Length || anim.frameDurations.Length != durations.Length) return false;
            for (int i = 0; i < sprites.Length; i++)
                if (anim.frames[i] != sprites[i] || !Mathf.Approximately(anim.frameDurations[i], durations[i])) return false;
            return true;
        }

        float EffectiveDuration(GifFrameData f) => f.RawDelayCentiseconds <= 0 ? _zeroFallback : f.DelaySeconds;

        void EnsurePreview(GifItem item)
        {
            if (item == null || item.Data == null || item.Data.Frames.Count == 0) return;
            item.PreviewFrame = Mathf.Clamp(item.PreviewFrame, 0, item.Data.Frames.Count - 1);
            if (item.PreviewTexture != null && item.PreviewTextureFrame == item.PreviewFrame) return;

            var frame = item.Data.Frames[item.PreviewFrame];
            int requiredBytes = frame.Width * frame.Height * 4;
            if (item.PreviewBuffer == null || item.PreviewBuffer.Length != requiredBytes)
                item.PreviewBuffer = new byte[requiredBytes];
            FlipRowsInto(frame.Rgba, item.PreviewBuffer, frame.Width, frame.Height);

            if (item.PreviewTexture == null || item.PreviewTexture.width != frame.Width || item.PreviewTexture.height != frame.Height)
            {
                if (item.PreviewTexture != null) DestroyImmediate(item.PreviewTexture);
                item.PreviewTexture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear
                };
            }

            item.PreviewTexture.LoadRawTextureData(item.PreviewBuffer);
            item.PreviewTexture.Apply(false, false);
            item.PreviewTextureFrame = item.PreviewFrame;
        }

        void UpdateBasicStats(GifItem item)
        {
            if (item == null || item.Data == null) return;
            item.ZeroDelayCount = item.Data.Frames.Count(f => f.RawDelayCentiseconds == 0);
            item.FrameHashes.Clear();
            foreach (var frame in item.Data.Frames)
                item.FrameHashes.Add(HashFrame(frame.Width, frame.Height, frame.Rgba));
            item.UniqueSpriteCount = item.FrameHashes.Distinct(StringComparer.Ordinal).Count();
            item.Length = item.Data.Frames.Sum(EffectiveDuration);
            item.SharedSpriteCount = 0;
        }

        static Texture2D MakePreviewTexture(int w, int h, byte[] topDown)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            tex.LoadRawTextureData(FlipRows(topDown, w, h));
            tex.Apply(false, false);
            return tex;
        }

        static void DestroyPreviews(GifItem item)
        {
            if (item == null) return;
            if (item.PreviewTexture != null) DestroyImmediate(item.PreviewTexture);
            item.PreviewTexture = null;
            item.PreviewBuffer = null;
            item.PreviewTextureFrame = -1;
        }

        void RemoveAt(int i) { DestroyPreviews(_items[i]); _items.RemoveAt(i); MarkAnalysisDirty(); }
        void ClearItems() { foreach (var i in _items) DestroyPreviews(i); _items.Clear(); MarkAnalysisDirty(); }

        void ChooseOutputFolder()
        {
            string start = AssetPathToAbsolute(AssetDatabase.IsValidFolder(_outputFolder) ? _outputFolder : DefaultOutputFolder());
            string abs = EditorUtility.OpenFolderPanel("Choose animation output folder inside Assets", start, "");
            string asset = AbsoluteToAssetPath(abs);
            if (string.IsNullOrEmpty(asset)) { if (!string.IsNullOrEmpty(abs)) EditorUtility.DisplayDialog("Invalid folder", "Choose a folder inside this project's Assets folder.", "OK"); return; }
            _outputFolder = asset;
            _outputFolderPinned = true;
            EditorPrefs.SetString(LastOutputKey, asset);
        }

        // Defaults to the folder of the most recently added GIF that lives inside Assets. An explicit
        // pick via Choose… pins the folder and stops it following later additions.
        string DefaultOutputFolder()
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                string folder = AbsoluteToAssetPath(Path.GetDirectoryName(_items[i].Path));
                if (AssetDatabase.IsValidFolder(folder)) return folder;
            }
            string last = EditorPrefs.GetString(LastOutputKey, "");
            return AssetDatabase.IsValidFolder(last) ? last : "Assets";
        }

        string GetBrowseDirectory() => _items.Count > 0 ? Path.GetDirectoryName(_items.Last().Path) : AssetPathToAbsolute(AssetDatabase.IsValidFolder(_outputFolder) ? _outputFolder : "Assets");

        static void WritePng(string assetPath, int width, int height, byte[] topDown)
        {
            var tex = MakePreviewTexture(width, height, topDown);
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPathToAbsolute(assetPath)) ?? "");
            File.WriteAllBytes(AssetPathToAbsolute(assetPath), tex.EncodeToPNG());
            DestroyImmediate(tex);
        }

        static void FlipRowsInto(byte[] src, byte[] dst, int w, int h)
        {
            int stride = w * 4;
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(src, y * stride, dst, (h - 1 - y) * stride, stride);
        }

        static byte[] FlipRows(byte[] src, int w, int h)
        {
            int stride = w * 4; var dst = new byte[src.Length];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(src, y * stride, dst, (h - 1 - y) * stride, stride);
            return dst;
        }

        // Only writes the .meta; the caller's single Refresh does the actual import. Reimporting here
        // would repack the sprite atlas once per frame.
        static void ConfigureSpriteImporter(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }

        static string HashFrame(int width, int height, byte[] rgba)
        {
            // GIF decoders may leave different hidden RGB values under fully transparent pixels.
            // Unity never displays those RGB values, so normalize them before hashing. This makes
            // visually identical frames share one sprite across different GIF animations.
            byte[] normalized = new byte[rgba.Length];
            Buffer.BlockCopy(rgba, 0, normalized, 0, rgba.Length);
            for (int i = 0; i + 3 < normalized.Length; i += 4)
            {
                if (normalized[i + 3] == 0)
                {
                    normalized[i] = 0;
                    normalized[i + 1] = 0;
                    normalized[i + 2] = 0;
                }
            }

            using (var sha = SHA256.Create())
            using (var ms = new MemoryStream())
            {
                byte[] d = BitConverter.GetBytes(width); ms.Write(d, 0, d.Length);
                d = BitConverter.GetBytes(height); ms.Write(d, 0, d.Length);
                ms.Write(normalized, 0, normalized.Length);
                return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "");
            }
        }

        static float FuzzyDifference(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 1f;
            int pixels = a.Length / 4, step = Math.Max(1, pixels / 4096), count = 0; double sum = 0;
            for (int p = 0; p < pixels; p += step)
            {
                int i = p * 4;
                int aa = a[i + 3], ba = b[i + 3];
                // RGB beneath fully transparent pixels is irrelevant visually.
                if (aa != 0 || ba != 0)
                {
                    sum += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
                    count += 3;
                }
                sum += Math.Abs(aa - ba);
                count += 1;
            }
            return count == 0 ? 1f : (float)(sum / (count * 255.0));
        }

        static bool IsGifPath(string p) => !string.IsNullOrEmpty(p) && p.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
        static string Sanitize(string s) { foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s; }
        static string CombineAsset(string a, string b) => (a.TrimEnd('/') + "/" + b.TrimStart('/')).Replace('\\', '/');
        static string AssetPathToAbsolute(string p) { if (Path.IsPathRooted(p)) return Path.GetFullPath(p); return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, p)); }
        static string AbsoluteToAssetPath(string abs)
        {
            if (string.IsNullOrEmpty(abs)) return null;
            string full = Path.GetFullPath(abs).Replace('\\', '/'), data = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return full.StartsWith(data, StringComparison.OrdinalIgnoreCase) ? "Assets" + full.Substring(data.Length) : null;
        }
        static void EnsureAssetFolder(string folder)
        {
            string[] parts = folder.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++) { string next = current + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]); current = next; }
        }
    }
}
#endif
