using UnityEditor;
using UnityEngine;

namespace qp {
    /// <summary>Debug buttons: one per attached animation — press in play mode to preview.</summary>
    [CustomEditor(typeof(MBSpriteFlipbook))]
    public class MBSpriteFlipbookEditor : Editor {

        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            var fb = (MBSpriteFlipbook)target;
            if (fb.Animations == null || fb.Animations.Length == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Application.isPlaying)) {
                foreach (var anim in fb.Animations) {
                    if (anim == null) continue;
                    if (GUILayout.Button($"Play  {anim.name}")) fb.Play(anim);
                }
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Enter Play mode to preview the animations.", MessageType.Info);
        }
    }
}
