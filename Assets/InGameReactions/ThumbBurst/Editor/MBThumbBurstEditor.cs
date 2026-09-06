using UnityEditor;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Play button for MBThumbBurst: previews the one-shot burst in edit mode by driving
    /// Evaluate() from the editor update loop; in play mode it fires the real Play().
    /// </summary>
    [CustomEditor(typeof(MBThumbBurst))]
    public class MBThumbBurstEditor : Editor {

        double _startTime = -1.0;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            GUILayout.Space(8);
            if (GUILayout.Button("▶ Play", GUILayout.Height(30)))
                _play();
        }

        void _play() {
            var tb = (MBThumbBurst)target;
            tb.Rebuild();   // apply the current inspector values every press
            if (Application.isPlaying) {
                tb.Play();
                return;
            }
            _startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= _tick;
            EditorApplication.update += _tick;
        }

        void _tick() {
            var tb = target as MBThumbBurst;
            if (tb == null) { EditorApplication.update -= _tick; return; }
            float t = (float)(EditorApplication.timeSinceStartup - _startTime);
            if (t >= tb.Duration) {
                EditorApplication.update -= _tick;
                tb.Hide();
            } else {
                tb.Evaluate(t);
            }
            SceneView.RepaintAll();
        }

        void OnDisable() {
            EditorApplication.update -= _tick;
        }
    }
}
