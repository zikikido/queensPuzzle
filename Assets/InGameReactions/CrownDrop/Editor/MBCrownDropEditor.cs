using UnityEditor;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Play button for MBCrownDrop: previews the one-shot drop in edit mode by driving
    /// Evaluate() from the editor update loop; in play mode it fires the real Play().
    /// </summary>
    [CustomEditor(typeof(MBCrownDrop))]
    public class MBCrownDropEditor : Editor {

        double _startTime = -1.0;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            GUILayout.Space(8);
            if (GUILayout.Button("▶ Play", GUILayout.Height(30)))
                _play();
        }

        void _play() {
            var cd = (MBCrownDrop)target;
            cd.Rebuild();   // apply the current inspector values every press
            if (Application.isPlaying) {
                cd.Play();
                return;
            }
            _startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= _tick;
            EditorApplication.update += _tick;
        }

        void _tick() {
            var cd = target as MBCrownDrop;
            if (cd == null) { EditorApplication.update -= _tick; return; }
            float t = (float)(EditorApplication.timeSinceStartup - _startTime);
            if (t >= cd.Duration) {
                EditorApplication.update -= _tick;
                cd.Hide();
            } else {
                cd.Evaluate(t);
            }
            SceneView.RepaintAll();
        }

        void OnDisable() {
            EditorApplication.update -= _tick;
        }
    }
}
