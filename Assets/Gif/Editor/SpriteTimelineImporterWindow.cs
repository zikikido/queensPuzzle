#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kido.SpriteTimeline.Editor
{
    public sealed class SpriteTimelineImporterWindow : EditorWindow
    {
        private string gifPath;
        private DecodedGif gif;
        private Texture2D[] previews;
        private Vector2 scroll;
        private int selected;
        private bool playing;
        private double lastTime;
        private float elapsed;

        private float zeroTimingFallback = 1f / 30f;
        private bool ignoreDuplicates = true;
        private bool fuzzyDuplicates;
        private float fuzzyTolerance = 0.02f;
        private int[] duplicateOf;

        [MenuItem("Tools/Kido/GIF to Flipbook Animation")]
        public static void Open()
        {
            var window = GetWindow<SpriteTimelineImporterWindow>("GIF Flipbook Importer");
            window.minSize = new Vector2(620f, 520f);
        }

        [MenuItem("Assets/Kido/Import GIF as Flipbook Animation", false, 2000)]
        private static void OpenFromSelectedGif()
        {
            string assetPath = GetSelectedGifAssetPath();
            if (string.IsNullOrEmpty(assetPath)) return;

            var window = GetWindow<SpriteTimelineImporterWindow>("GIF Flipbook Importer");
            window.minSize = new Vector2(620f, 520f);
            window.Show();
            window.Focus();
            window.LoadGif(AssetPathToAbsolutePath(assetPath));
        }

        [MenuItem("Assets/Kido/Import GIF as Flipbook Animation", true)]
        private static bool ValidateOpenFromSelectedGif() => !string.IsNullOrEmpty(GetSelectedGifAssetPath());

        private static string GetSelectedGifAssetPath()
        {
            string[] guids = Selection.assetGUIDs;
            if (guids == null || guids.Length != 1) return null;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase)
                ? path
                : null;
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private void OnEnable() => EditorApplication.update += Tick;
        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            DestroyPreviews();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("GIF → SOFlipbookAnim", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Choose an animated GIF, inspect its sprites and timing, then choose the Unity folder where the Images folder and SOFlipbookAnim asset will be created.", MessageType.Info);

            EditorGUILayout.LabelField("GIF File", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                gifPath = EditorGUILayout.TextField(gifPath ?? string.Empty);
                if (GUILayout.Button("Load", GUILayout.Width(55))) LoadGif(gifPath);
                if (GUILayout.Button("Browse…", GUILayout.Width(80))) ChooseGif();
            }

            Rect dropArea = GUILayoutUtility.GetRect(0f, 46f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag & drop a .gif file here", EditorStyles.helpBox);
            HandleGifDrop(dropArea);

            using (new EditorGUILayout.HorizontalScope())
            {
                zeroTimingFallback = Mathf.Max(0.001f, EditorGUILayout.FloatField(new GUIContent("0 timing fallback", "Duration used for GIF frames whose stored delay is zero. This value is written into the generated SOFlipbookAnim."), zeroTimingFallback));
                ignoreDuplicates = EditorGUILayout.ToggleLeft(new GUIContent("Ignore Duplicates", "Do not create another PNG for a duplicate frame. The timeline still keeps the frame and reuses the first matching Sprite reference."), ignoreDuplicates, GUILayout.Width(145));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                fuzzyDuplicates = EditorGUILayout.ToggleLeft(new GUIContent("Fuzzy duplicate detection", "Also detect nearly identical images. Useful for tiny export noise, but can merge real animation changes if tolerance is too high."), fuzzyDuplicates, GUILayout.Width(190));
                using (new EditorGUI.DisabledScope(!fuzzyDuplicates))
                    fuzzyTolerance = EditorGUILayout.Slider("Tolerance", fuzzyTolerance, 0f, 0.15f);
                if (GUILayout.Button("Recheck", GUILayout.Width(75))) FindDuplicates();
            }

            if (gif == null) return;

            EditorGUILayout.Space(6);
            DrawSummary();
            DrawPreview();
            DrawFrames();

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Choose Save Folder and Generate", GUILayout.Height(34))) Generate();
        }

        private void ChooseGif()
        {
            string start = GetExistingDirectory(gifPath, Application.dataPath);
            string p = EditorUtility.OpenFilePanel("Choose animated GIF", start, "gif");
            if (!string.IsNullOrEmpty(p)) LoadGif(p);
        }

        private void LoadGif(string path)
        {
            path = NormalizeInputPath(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                EditorUtility.DisplayDialog("GIF file required", "Choose, drag, or type the path to a GIF file.", "OK");
                return;
            }
            if (!File.Exists(path))
            {
                EditorUtility.DisplayDialog("GIF not found", "The file does not exist:" + path, "OK");
                return;
            }
            if (!string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Invalid file", "Please select a .gif file.", "OK");
                return;
            }

            try
            {
                var decoded = GifDecoder.Decode(path);
                DestroyPreviews();
                gifPath = Path.GetFullPath(path);
                gif = decoded;
                previews = new Texture2D[gif.Frames.Count];
                selected = 0;
                elapsed = 0;
                FindDuplicates();
            }
            catch (Exception e)
            {
                gif = null;
                EditorUtility.DisplayDialog("GIF import failed", e.Message, "OK");
            }
        }

        private void HandleGifDrop(Rect area)
        {
            Event e = Event.current;
            if (!area.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;

            string candidate = DragAndDrop.paths.FirstOrDefault(p => string.Equals(Path.GetExtension(p), ".gif", StringComparison.OrdinalIgnoreCase));
            DragAndDrop.visualMode = string.IsNullOrEmpty(candidate) ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform && !string.IsNullOrEmpty(candidate))
            {
                DragAndDrop.AcceptDrag();
                LoadGif(candidate);
            }
            e.Use();
        }

        private static string NormalizeInputPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            path = path.Trim().Trim('"');
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || string.Equals(path, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                path = Path.Combine(projectRoot, path);
            }
            return Path.GetFullPath(path);
        }

        private static string GetExistingDirectory(string path, string fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    string normalized = NormalizeInputPath(path);
                    if (Directory.Exists(normalized)) return normalized;
                    string directory = Path.GetDirectoryName(normalized);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) return directory;
                }
            }
            catch { }
            return fallback;
        }

        private void DrawSummary()
        {
            int zero = gif.Frames.Count(f => f.Duration <= 0f);
            int duplicates = duplicateOf == null ? 0 : duplicateOf.Count(i => i >= 0);
            EditorGUILayout.LabelField($"{gif.Width} × {gif.Height}   •   {gif.Frames.Count} timeline frames   •   {gif.Frames.Count - duplicates} unique sprites   •   {zero} zero-timing frames", EditorStyles.boldLabel);
            if (zero > 0)
                EditorGUILayout.HelpBox($"GIF frames with zero timing will be saved as {zeroTimingFallback:0.###} seconds in SOFlipbookAnim.", MessageType.Warning);
        }

        private void DrawPreview()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                Rect r = GUILayoutUtility.GetRect(220, 220, GUILayout.Width(220), GUILayout.Height(220));
                EditorGUI.DrawRect(r, new Color(0.13f, 0.13f, 0.13f));
                GUI.DrawTexture(r, Preview(selected), ScaleMode.ScaleToFit, true);

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField($"Frame {selected + 1}/{gif.Frames.Count}", EditorStyles.boldLabel);
                    float raw = gif.Frames[selected].Duration;
                    EditorGUILayout.LabelField(raw <= 0 ? $"Timing: 0 sec → preview {zeroTimingFallback:0.###} sec" : $"Timing: {raw:0.###} sec");
                    if (duplicateOf != null && duplicateOf[selected] >= 0)
                        EditorGUILayout.HelpBox($"Duplicate of frame {duplicateOf[selected] + 1}. With Ignore Duplicates enabled, both timeline entries use the same Sprite asset.", MessageType.Info);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(playing ? "Pause" : "Play")) { playing = !playing; lastTime = EditorApplication.timeSinceStartup; }
                        if (GUILayout.Button("Stop")) { playing = false; selected = 0; elapsed = 0; }
                    }
                }
            }
        }

        private void DrawFrames()
        {
            EditorGUILayout.LabelField("Timeline", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(190));
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 30) / 118f));
            for (int row = 0; row * columns < gif.Frames.Count; row++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns; c++)
                    {
                        int i = row * columns + c;
                        if (i >= gif.Frames.Count) break;
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(110)))
                        {
                            Rect rr = GUILayoutUtility.GetRect(96, 96, GUILayout.Width(96), GUILayout.Height(96));
                            GUI.DrawTexture(rr, Preview(i), ScaleMode.ScaleToFit, true);
                            if (Event.current.type == EventType.MouseDown && rr.Contains(Event.current.mousePosition)) { selected = i; Repaint(); }
                            string d = gif.Frames[i].Duration <= 0 ? $"0 → {zeroTimingFallback:0.###}s" : $"{gif.Frames[i].Duration:0.###}s";
                            GUILayout.Label($"#{i + 1}  {d}", EditorStyles.miniLabel);
                            if (duplicateOf != null && duplicateOf[i] >= 0)
                                GUILayout.Label($"= #{duplicateOf[i] + 1}", EditorStyles.miniBoldLabel);
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private Texture2D Preview(int i)
        {
            i = Mathf.Clamp(i, 0, gif.Frames.Count - 1);
            if (previews[i] != null) return previews[i];
            var t = new Texture2D(gif.Width, gif.Height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            t.SetPixels32(FlipVertical(gif.Frames[i].Pixels, gif.Width, gif.Height));
            t.Apply(false, false);
            previews[i] = t;
            return t;
        }

        private void FindDuplicates()
        {
            if (gif == null) return;
            duplicateOf = Enumerable.Repeat(-1, gif.Frames.Count).ToArray();
            for (int i = 1; i < gif.Frames.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    float diff = Difference(gif.Frames[i].Pixels, gif.Frames[j].Pixels);
                    bool same = fuzzyDuplicates ? diff <= fuzzyTolerance : diff <= 0.000001f;
                    if (same) { duplicateOf[i] = duplicateOf[j] >= 0 ? duplicateOf[j] : j; break; }
                }
            }
            Repaint();
        }

        private static float Difference(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 1f;
            long total = 0;
            int step = Mathf.Max(1, a.Length / 4096);
            int samples = 0;
            for (int i = 0; i < a.Length; i += step)
            {
                total += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) + Math.Abs(a[i].a - b[i].a);
                samples++;
            }
            return total / (samples * 4f * 255f);
        }

        private void Generate()
        {
            if (gif == null) return;
            string chosen = EditorUtility.OpenFolderPanel("Choose folder for Images and SOFlipbookAnim", GetDefaultSaveDirectory(), "");
            if (string.IsNullOrEmpty(chosen)) return;
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string full = Path.GetFullPath(chosen);
            if (!full.StartsWith(Path.GetFullPath(Application.dataPath), StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Invalid folder", "Choose a folder inside this Unity project's Assets folder.", "OK");
                return;
            }

            string assetFolder = "Assets" + full.Substring(Path.GetFullPath(Application.dataPath).Length).Replace('\\', '/');
            assetFolder = assetFolder.TrimEnd('/');
            string imagesFolder = assetFolder + "/Images";
            string baseName = Sanitize(Path.GetFileNameWithoutExtension(gifPath));
            string assetPath = assetFolder + "/" + baseName + ".asset";

            bool imagesAlreadyExist = AssetDatabase.IsValidFolder(imagesFolder);
            bool animationAlreadyExists = AssetDatabase.LoadMainAssetAtPath(assetPath) != null;

            if (imagesAlreadyExist || animationAlreadyExists)
            {
                string existing = string.Join("\n", new[]
                {
                    animationAlreadyExists ? "• Existing animation asset: " + assetPath : null,
                    imagesAlreadyExist ? "• Existing Images folder: " + imagesFolder : null
                }.Where(x => !string.IsNullOrEmpty(x)));

                bool replace = EditorUtility.DisplayDialog(
                    "Replace existing animation?",
                    "The importer found existing generated content:\n\n" + existing +
                    "\n\nContinue? The existing animation asset and the entire Images folder will be deleted, then recreated from the selected GIF. This cannot be undone.",
                    "Delete and Replace",
                    "Cancel");

                if (!replace) return;
            }

            try
            {
                // Delete old generated content first, so no stale sprites remain after re-import.
                if (animationAlreadyExists && !AssetDatabase.DeleteAsset(assetPath))
                    throw new IOException("Could not delete the existing animation asset: " + assetPath);

                if (imagesAlreadyExist && !AssetDatabase.DeleteAsset(imagesFolder))
                    throw new IOException("Could not delete the existing Images folder: " + imagesFolder);

                AssetDatabase.Refresh();
                EnsureFolder(imagesFolder);

                var spriteForFrame = new Sprite[gif.Frames.Count];
                var firstSavedFor = new Dictionary<int, Sprite>();

                for (int i = 0; i < gif.Frames.Count; i++)
                {
                    int source = ignoreDuplicates && duplicateOf != null && duplicateOf[i] >= 0 ? duplicateOf[i] : i;
                    if (firstSavedFor.TryGetValue(source, out var reused)) { spriteForFrame[i] = reused; continue; }

                    var tex = new Texture2D(gif.Width, gif.Height, TextureFormat.RGBA32, false);
                    tex.SetPixels32(FlipVertical(gif.Frames[source].Pixels, gif.Width, gif.Height));
                    tex.Apply(false, false);
                    string pngPath = $"{imagesFolder}/{baseName}_{source + 1:0000}.png";
                    File.WriteAllBytes(Path.Combine(project, pngPath), tex.EncodeToPNG());
                    DestroyImmediate(tex);
                    AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceSynchronousImport);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.SaveAndReimport();
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
                    firstSavedFor[source] = sprite;
                    spriteForFrame[i] = sprite;
                }

                // Write a real positive duration for zero-delay GIF frames. Do not rely only on
                // SOFlipbookAnim.DurationOf(), because the raw array and cached _length should also
                // contain the effective imported timing.
                float[] importedDurations = gif.Frames
                    .Select(f => f.Duration > 0f ? f.Duration : zeroTimingFallback)
                    .ToArray();

                var asset = CreateInstance<qp.SOFlipbookAnim>();
                asset.frames = spriteForFrame;
                asset.frameDurations = importedDurations;
                AssetDatabase.CreateAsset(asset, assetPath);

                // Keep the private cached length correct immediately after programmatic creation,
                // without waiting for Unity to invoke OnValidate later.
                var serializedAsset = new SerializedObject(asset);
                SerializedProperty lengthProperty = serializedAsset.FindProperty("_length");
                if (lengthProperty != null)
                    lengthProperty.floatValue = importedDurations.Sum();
                serializedAsset.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                EditorUtility.DisplayDialog("GIF imported", $"Created:\n{imagesFolder}\n{assetPath}", "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Generation failed", e.Message, "OK");
            }
        }


        private string GetDefaultSaveDirectory()
        {
            string gifDirectory = GetExistingDirectory(gifPath, Application.dataPath);
            string assetsFull = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(gifDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Saving is only allowed inside Assets. When the GIF is already inside Assets,
            // open its own folder by default; otherwise fall back to the Assets root.
            bool insideAssets = candidate.Equals(assetsFull, StringComparison.OrdinalIgnoreCase) ||
                                candidate.StartsWith(assetsFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                                candidate.StartsWith(assetsFull + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            return insideAssets ? candidate : Application.dataPath;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string Sanitize(string n)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return string.IsNullOrWhiteSpace(n) ? "FlipbookAnim" : n;
        }

        private static Color32[] FlipVertical(Color32[] src, int w, int h)
        {
            var dst = new Color32[src.Length];
            for (int y = 0; y < h; y++) Array.Copy(src, y * w, dst, (h - 1 - y) * w, w);
            return dst;
        }

        private float EffectiveDuration(int i) => gif.Frames[i].Duration > 0 ? gif.Frames[i].Duration : zeroTimingFallback;

        private void Tick()
        {
            if (!playing || gif == null || gif.Frames.Count == 0) return;
            double now = EditorApplication.timeSinceStartup;
            elapsed += (float)(now - lastTime);
            lastTime = now;
            while (elapsed >= EffectiveDuration(selected))
            {
                elapsed -= EffectiveDuration(selected);
                selected = (selected + 1) % gif.Frames.Count;
            }
            Repaint();
        }

        private void DestroyPreviews()
        {
            if (previews == null) return;
            foreach (var t in previews) if (t != null) DestroyImmediate(t);
            previews = null;
        }
    }
}
#endif
