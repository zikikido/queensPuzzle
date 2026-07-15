using System.IO;
using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Packs a level set (Assets/Levels/{set}/{n}.asset) into ONE encrypted binary —
    /// Resources/Levels/levels.bytes — that LevelLoader reads at runtime via LevelPack.
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
            if (!EditorUtility.DisplayDialog(
                    "Export levels to Resources",
                    $"Pack all {setName} levels ({sourceFolder}) into {PackPath} (encrypted, replacing the old export)?",
                    "Export", "Cancel"))
                return;

            if (!AssetDatabase.IsValidFolder(sourceFolder))
            { Debug.LogWarning($"[Levels] No source folder {sourceFolder}."); return; }

            EnsureFolder(TargetFolder);

            // gather the numbered levels, in slot order; gaps break the 1-based index → abort
            var numbers = new System.Collections.Generic.List<int>();
            foreach (var file in Directory.GetFiles(sourceFolder, "*.asset"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(file), out int num)) numbers.Add(num);
            numbers.Sort();
            for (int i = 0; i < numbers.Count; i++)
                if (numbers[i] != i + 1)
                {
                    Debug.LogError($"[Levels] Export aborted — {setName} has a gap: expected level {i + 1}, found {numbers[i]}.");
                    return;
                }

            var levels = new System.Collections.Generic.List<LevelPack.Level>(numbers.Count);
            foreach (int num in numbers)
            {
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{sourceFolder}/{num}.asset");
                if (lvl == null || lvl.regions == null || lvl.solutionColumns == null)
                { Debug.LogError($"[Levels] Export aborted — level {num} is unreadable."); return; }
                levels.Add(lvl.ToPacked());
            }

            byte[] plain = LevelPack.EncodePlain(levels);
            byte[] file2 = LevelPack.Encrypt(plain);   // gzip + AES

            // the pack replaces the per-level assets completely — delete every numbered copy
            foreach (var old in Directory.GetFiles(TargetFolder, "*.asset"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(old), out _))
                    AssetDatabase.DeleteAsset(old.Replace('\\', '/'));

            File.WriteAllBytes(PackPath, file2);
            AssetDatabase.ImportAsset(PackPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Levels] Packed {levels.Count} levels → {PackPath}: {file2.Length / 1024f:0.0} KB shipped "
                    + $"(plain {plain.Length / 1024f:0.0} KB)");
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
    }
}
