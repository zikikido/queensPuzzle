using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Common {

    /// <summary>
    /// Entries are Resources paths, but editing works by dropping AudioClips: the clip must
    /// live under a Resources/ folder and is stored as its Resources-relative path. Entries
    /// that no longer resolve are shown in red.
    /// </summary>
    [CustomEditor(typeof(BGMusicPlaylist))]
    public class BGMusicPlaylistEditor : Editor {

        public override void OnInspectorGUI() {
            var playlist = (BGMusicPlaylist)target;

            for (int i = 0; i < playlist.ResourcePaths.Length; i++) {
                EditorGUILayout.BeginHorizontal();

                var path = playlist.ResourcePaths[i];
                bool ok = Resources.Load<AudioClip>(path) != null;

                var style = new GUIStyle(EditorStyles.label);
                if (!ok)
                    style.normal.textColor = Color.red;

                EditorGUILayout.LabelField($"{i}. {path}" + (ok ? "" : "  (missing)"), style);

                if (GUILayout.Button("X", GUILayout.Width(22))) {
                    Undo.RecordObject(playlist, "Remove BG track");
                    var list = new List<string>(playlist.ResourcePaths);
                    list.RemoveAt(i);
                    playlist.ResourcePaths = list.ToArray();
                    EditorUtility.SetDirty(playlist);
                    EditorGUILayout.EndHorizontal();
                    return;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();

            var clip = (AudioClip)EditorGUILayout.ObjectField("Add clip (from Resources)", null, typeof(AudioClip), false);
            if (clip == null)
                return;

            var resPath = ToResourcesPath(AssetDatabase.GetAssetPath(clip));
            if (resPath == null) {
                EditorUtility.DisplayDialog("Not under Resources",
                    $"'{clip.name}' is not inside a Resources/ folder, so it can't be loaded by path at runtime. Move it under any Resources/ folder first.", "OK");
                return;
            }

            Undo.RecordObject(playlist, "Add BG track");
            var l = new List<string>(playlist.ResourcePaths) { resPath };
            playlist.ResourcePaths = l.ToArray();
            EditorUtility.SetDirty(playlist);
        }

        // "Assets/Reskin/Resources/Music/bg1.wav" -> "Music/bg1"
        static string ToResourcesPath(string assetPath) {
            const string marker = "/Resources/";
            var i = assetPath.LastIndexOf(marker);
            if (i < 0)
                return null;

            var p = assetPath.Substring(i + marker.Length);
            var dot = p.LastIndexOf('.');
            return dot < 0 ? p : p.Substring(0, dot);
        }
    }
}
