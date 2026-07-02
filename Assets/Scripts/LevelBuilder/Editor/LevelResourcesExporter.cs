using System.IO;
using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Copies a level set (Assets/Levels/{set}/{n}.asset) into a skin's Resources folder so the
    /// runtime can load them by index (LevelLoader → Resources.Load("Levels/{n}")). Authoring stays
    /// in Assets/Levels/{set}; the export is the runtime copy. Future skins = other Resources targets.
    /// </summary>
    public static class LevelResourcesExporter
    {
        const string DefaultSource = "Assets/Levels/Puzzby";
        const string ResourcesRoot = "Assets/Reskin/Resources";
        const string TargetFolder = ResourcesRoot + "/Levels";
        const string DataPath = ResourcesRoot + "/SOLevelsData.asset";

        [MenuItem("QueensPuzzle/Export Levels to Resources")]
        public static void Export() => Export(DefaultSource, "Puzzby");

        public static void Export(string sourceFolder, string setName)
        {
            if (!EditorUtility.DisplayDialog(
                    "Export levels to Resources",
                    $"Copy all {setName} levels ({sourceFolder}) into {TargetFolder} (replacing its content) and update SOLevelsData?",
                    "Export", "Cancel"))
                return;

            if (!AssetDatabase.IsValidFolder(sourceFolder))
            { Debug.LogWarning($"[Levels] No source folder {sourceFolder}."); return; }

            EnsureFolder(TargetFolder);

            // clear stale numbered levels first — a smaller set must not leave the old tail behind
            foreach (var file in Directory.GetFiles(TargetFolder, "*.asset"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(file), out _))
                    AssetDatabase.DeleteAsset(file.Replace('\\', '/'));

            int count = 0;
            foreach (var file in Directory.GetFiles(sourceFolder, "*.asset"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(name, out _)) continue;   // only numbered levels (skip __Play etc.)

                string dst = $"{TargetFolder}/{name}.asset";
                if (AssetDatabase.CopyAsset($"{sourceFolder}/{name}.asset", dst)) count++;
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
