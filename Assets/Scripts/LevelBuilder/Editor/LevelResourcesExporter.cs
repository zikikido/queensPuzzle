using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Packs a level set (Assets/Levels/{set}/{n}.asset) into ONE encrypted binary -
    /// Resources/Levels/levels.bytes - that LevelLoader reads at runtime via LevelPack.
    /// Authoring stays in Assets/Levels/{set}; the pack is the shipped copy. Old per-level
    /// .asset copies in the target folder are deleted (the pack replaces them completely).
    /// </summary>
    public static class LevelResourcesExporter
    {
        const string DefaultSource = "Assets/Levels/Puzzby";
        const string ResourcesRoot = "Assets/Reskin/Resources";
        const string TargetFolder = ResourcesRoot + "/Levels";
        const string PackPath = TargetFolder + "/levels.bytes";

        [MenuItem("QueensPuzzle/Export Levels to Resources")]
        public static void Export() => Export(DefaultSource, "Puzzby");

        public static void Export(string sourceFolder, string setName)
        {
            if (!AssetDatabase.IsValidFolder(sourceFolder))
            { Debug.LogWarning($"[Levels] No source folder {sourceFolder}."); return; }

            // gather the numbered levels, in slot order; gaps break the 1-based index -> abort
            var numbers = new System.Collections.Generic.List<int>();
            foreach (var file in Directory.GetFiles(sourceFolder, "*.asset"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(file), out int num)) numbers.Add(num);
            numbers.Sort();
            for (int i = 0; i < numbers.Count; i++)
                if (numbers[i] != i + 1)
                {
                    Debug.LogError($"[Levels] Export aborted - {setName} has a gap: expected level {i + 1}, found {numbers[i]}.");
                    return;
                }

            var levels = new System.Collections.Generic.List<LevelPack.Level>(numbers.Count);
            foreach (int num in numbers)
            {
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{sourceFolder}/{num}.asset");
                if (lvl == null || lvl.regions == null || lvl.solutionColumns == null)
                { Debug.LogError($"[Levels] Export aborted - level {num} is unreadable."); return; }
                levels.Add(lvl.ToPacked());
            }

            ResolveLevelSetId(setName, levels, levelSetId =>
            {
                if (string.IsNullOrWhiteSpace(levelSetId)) return;
                ExportResolved(sourceFolder, setName, levels, levelSetId.Trim());
            });
        }

        static void ExportResolved(string sourceFolder, string setName, System.Collections.Generic.IList<LevelPack.Level> levels, string levelSetId)
        {
            if (!EditorUtility.DisplayDialog(
                    "Export levels to Resources",
                    BuildExportConfirmMessage(sourceFolder, setName, levels, levelSetId),
                    "Export", "Cancel"))
                return;

            EnsureFolder(TargetFolder);

            byte[] plain = LevelPack.EncodePlain(levels, levelSetId);
            byte[] file2 = LevelPack.Encrypt(plain);   // gzip + AES

            // the pack replaces the per-level assets completely - delete every numbered copy
            foreach (var old in Directory.GetFiles(TargetFolder, "*.asset"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(old), out _))
                    AssetDatabase.DeleteAsset(old.Replace('\\', '/'));

            File.WriteAllBytes(PackPath, file2);
            AssetDatabase.ImportAsset(PackPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Levels] Packed {levels.Count} levels -> {PackPath}: {file2.Length / 1024f:0.0} KB shipped "
                    + $"(plain {plain.Length / 1024f:0.0} KB, levelSetId {levelSetId})");
        }

        static string BuildExportConfirmMessage(string sourceFolder, string setName, System.Collections.Generic.IList<LevelPack.Level> levels, string levelSetId)
        {
            string oldSetId = "none";
            int oldCount = 0;
            int same = 0;
            int changed = 0;
            bool existingReadable = false;

            if (File.Exists(PackPath))
            {
                try
                {
                    byte[] oldPlain = LevelPack.Decrypt(File.ReadAllBytes(PackPath));
                    oldSetId = LevelPack.LevelSetId(oldPlain);
                    oldCount = LevelPack.Count(oldPlain);
                    existingReadable = true;

                    int compareCount = Math.Min(oldCount, levels.Count);
                    for (int i = 0; i < compareCount; i++)
                    {
                        if (FullExportHash(LevelPack.Decode(oldPlain, i)) == FullExportHash(levels[i])) same++;
                        else changed++;
                    }
                }
                catch (Exception e)
                {
                    oldSetId = "unreadable current pack";
                    Debug.LogWarning($"[Levels] Existing pack could not be summarized. {e.Message}");
                }
            }

            int added = existingReadable ? Math.Max(0, levels.Count - oldCount) : levels.Count;
            int removed = existingReadable ? Math.Max(0, oldCount - levels.Count) : 0;
            string idAction = oldSetId == levelSetId ? "keep same ID" : $"replace ID: {oldSetId} -> {levelSetId}";
            string packAction = File.Exists(PackPath) ? "Replace existing levels.bytes" : "Create new levels.bytes";

            return
                $"Export {setName} level pack\n\n" +
                $"Source: {sourceFolder}\n" +
                $"Target: {PackPath}\n" +
                $"Action: {packAction}\n\n" +
                $"Levels\n" +
                $"Current pack: {(existingReadable ? oldCount.ToString() : "none / unreadable")}\n" +
                $"New export: {levels.Count}\n" +
                $"Same existing slots: {same}\n" +
                $"Changed existing slots: {changed}\n" +
                $"Added at end: {added}\n" +
                $"Removed from end: {removed}\n\n" +
                $"levelSetId\n" +
                $"Current: {oldSetId}\n" +
                $"Exporting: {levelSetId}\n" +
                $"ID action: {idAction}\n\n" +
                "Export will overwrite the shipped encrypted pack.";
        }
        static void ResolveLevelSetId(string setName, System.Collections.Generic.IList<LevelPack.Level> newLevels, Action<string> onResolved)
        {
            if (!File.Exists(PackPath))
            {
                PromptNewLevelSetId(setName, NewLevelSetId(setName), onResolved);
                return;
            }

            byte[] oldPlain;
            string oldSetId;
            int oldCount;
            try
            {
                oldPlain = LevelPack.Decrypt(File.ReadAllBytes(PackPath));
                oldSetId = LevelPack.LevelSetId(oldPlain);
                oldCount = LevelPack.Count(oldPlain);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Levels] Existing pack could not be read as the current format; creating a new levelSetId. {e.Message}");
                PromptNewLevelSetId(setName, NewLevelSetId(setName), onResolved);
                return;
            }

            bool appendOnly = newLevels.Count >= oldCount;
            if (appendOnly)
            {
                for (int i = 0; i < oldCount; i++)
                {
                    if (FullExportHash(LevelPack.Decode(oldPlain, i)) != FullExportHash(newLevels[i]))
                    {
                        appendOnly = false;
                        break;
                    }
                }
            }

            if (appendOnly)
            {
                onResolved(oldSetId);
                return;
            }

            string newSetId = NewLevelSetId(setName);
            int choice = EditorUtility.DisplayDialogComplex(
                "Level set changed",
                $"The existing exported levels are not an append-only match.\n\nOld levelSetId: {oldSetId}\nSuggested new levelSetId: {newSetId}\nEdit box default: {oldSetId}\n\nFor clean analytics graphs, use a changed levelSetId unless you are sure this is the same shipped set.",
                "Edit ID",
                "Keep Old ID",
                "Cancel");

            if (choice == 0) PromptNewLevelSetId(setName, oldSetId, onResolved);
            else if (choice == 1) onResolved(oldSetId);
            else onResolved(null);
        }
        static void PromptNewLevelSetId(string setName, string suggestedId, Action<string> onResolved)
        {
            LevelSetIdPromptWindow.Open($"Set {setName} levelSetId", suggestedId, onResolved);
        }

        static int FullExportHash(LevelPack.Level level)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + level.size;
                h = h * 31 + Mathf.Clamp(level.weight, 0, ushort.MaxValue);
                HashArray(level.regions, ref h);
                HashArray(level.solutionColumns, ref h);

                ushort revealed = 0;
                if (level.revealedRows != null)
                    foreach (int r in level.revealedRows)
                        if (r >= 0 && r < 16) revealed |= (ushort)(1 << r);
                h = h * 31 + revealed;

                for (int i = 0; i < level.size; i++)
                    h = h * 31 + (level.regionColors != null && i < level.regionColors.Length ? level.regionColors[i] : i);
                return h;
            }
        }

        static void HashArray(int[] values, ref int h)
        {
            h = h * 31 + (values != null ? values.Length : -1);
            if (values == null) return;
            foreach (int value in values) h = h * 31 + value;
        }
        static string NewLevelSetId(string setName)
        {
            string clean = string.IsNullOrWhiteSpace(setName) ? "LevelSet" : setName.Trim().Replace(' ', '_');
            return $"{clean}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        }

        // Create a folder (and any missing parents) via the AssetDatabase.
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        sealed class LevelSetIdPromptWindow : EditorWindow
        {
            string _value;
            Action<string> _onResolved;
            bool _resolved;
            bool _focusTextField;

            public static void Open(string title, string suggestedId, Action<string> onResolved)
            {
                var window = CreateInstance<LevelSetIdPromptWindow>();
                window.titleContent = new GUIContent(title);
                window._value = suggestedId;
                window._onResolved = onResolved;
                window.minSize = new Vector2(460f, 128f);
                window.maxSize = window.minSize;
                window.ShowUtility();
            }

            void OnGUI()
            {
                EditorGUILayout.LabelField("levelSetId", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Use the suggested ID, or type your own before exporting.", MessageType.Info);

                GUI.SetNextControlName("levelSetIdField");
                _value = EditorGUILayout.TextField(_value);
                if (!_focusTextField)
                {
                    _focusTextField = true;
                    EditorGUI.FocusTextInControl("levelSetIdField");
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Cancel", GUILayout.Width(90f))) Resolve(null);
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_value)))
                        if (GUILayout.Button("OK", GUILayout.Width(90f))) Resolve(_value.Trim());
                }
            }

            void Resolve(string value)
            {
                if (_resolved) return;
                _resolved = true;
                _onResolved?.Invoke(value);
                Close();
            }

            void OnDestroy()
            {
                if (!_resolved) Resolve(null);
            }
        }
    }
}