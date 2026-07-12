using UnityEditor;
using UnityEngine;

namespace qp {
    /// <summary>Debug buttons: one per controller state — press in play mode to preview.</summary>
    [CustomEditor(typeof(MBSpriteFlipbook))]
    public class MBSpriteFlipbookEditor : Editor {

        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            var fb = (MBSpriteFlipbook)target;
            var ctrl = fb.Controller;
            if (ctrl == null || ctrl.states == null || ctrl.states.Length == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            if (Application.isPlaying)
                EditorGUILayout.LabelField("Current state", fb.CurrentState ?? "-");
            using (new EditorGUI.DisabledScope(!Application.isPlaying)) {
                foreach (var s in ctrl.states) {
                    if (s == null || string.IsNullOrEmpty(s.name)) continue;
                    if (GUILayout.Button($"Play  {s.name}")) fb.Play(s.name);
                }
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Enter Play mode to preview the states.", MessageType.Info);
        }
    }
}
