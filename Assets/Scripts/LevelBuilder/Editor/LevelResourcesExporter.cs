using System.IO;
using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Copies the authoring levels (Assets/Levels/{n}.asset) into a skin's Resources folder so the
    /// runtime can load them by index (LevelLoader → Resources.Load("Levels/{n}")). Authoring stays
    /// in Assets/Levels; the export is the runtime copy. Future skins = other Resources targets.
    /// </summary>
    public static class LevelResourcesExporter
    {
        const string SourceFolder = "Assets/Levels";
        const string ResourcesRoot = "Assets/Reskin/Resources";
        const string TargetFolder = ResourcesRoot + "/Levels";
        const string DataPath = ResourcesRoot + "/SOLevelsData.asset";

        [MenuItem("QueensPuzzle/Export Levels to Resources")]
        public static void Export()
        {
            if (!EditorUtility.DisplayDialog(
                    "Export levels to Resources",
                    $"Copy all levels from {SourceFolder} into {TargetFolder} (overwriting) and update SOLevelsData?",
                    "Export", "Cancel"))
                return;

            if (!AssetDatabase.IsValidFolder(SourceFolder))
            { Debug.LogWarning($"[Levels] No source folder {SourceFolder}."); return; }

            EnsureFolder(TargetFolder);

            int count = 0;
            foreach (var file in Directory.GetFiles(SourceFolder, "*.asset"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(name, out _)) continue;   // only numbered levels (skip __Play etc.)

                string dst = $"{TargetFolder}/{name}.asset";
                AssetDatabase.DeleteAsset(dst);             // overwrite
                if (AssetDatabase.CopyAsset($"{SourceFolder}/{name}.asset", dst)) count++;
            }

            // write / update the level-set data asset (loaded at runtime for the levels count)
            var data = AssetDatabase.LoadAssetAtPath<qp.SOLevelsData>(DataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<qp.SOLevelsData>();
                AssetDatabase.CreateAsset(data, DataPath);
            }
            data.LevelsCount = count;
            EditorUtility.SetDirty(data);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Levels] Exported {count} levels → {TargetFolder} (SOLevelsData.LevelsCount = {count})");
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
