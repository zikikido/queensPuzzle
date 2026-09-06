using UnityEditor;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Play button for MBPawStamp: previews the one-shot stamp in edit mode by driving
    /// Evaluate() from the editor update loop; in play mode it fires the real Play().
    /// </summary>
    [CustomEditor(typeof(MBPawStamp))]
    public class MBPawStampEditor : Editor {

        double _startTime = -1.0;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            GUILayout.Space(8);
            if (GUILayout.Button("▶ Play", GUILayout.Height(30)))
                _play();
        }

        void _play() {
            var ps = (MBPawStamp)target;
            ps.Rebuild();   // apply the current inspector values every press
            if (Application.isPlaying) {
                ps.Play();
                return;
            }
            _startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= _tick;
            EditorApplication.update += _tick;
        }

        void _tick() {
            var ps = target as MBPawStamp;
            if (ps == null) { EditorApplication.update -= _tick; return; }
            float t = (float)(EditorApplication.timeSinceStartup - _startTime);
            if (t >= ps.Duration) {
                EditorApplication.update -= _tick;
                ps.Hide();
            } else {
                ps.Evaluate(t);
            }
            SceneView.RepaintAll();
        }

        void OnDisable() {
            EditorApplication.update -= _tick;
        }
    }
}
