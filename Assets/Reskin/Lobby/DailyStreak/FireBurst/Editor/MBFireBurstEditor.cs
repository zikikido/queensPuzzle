using UnityEditor;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Play button for MBFireBurst: previews the one-shot burst in edit mode by driving
    /// Evaluate() from the editor update loop; in play mode it fires the real Play().
    /// </summary>
    [CustomEditor(typeof(MBFireBurst))]
    public class MBFireBurstEditor : Editor {

        double _startTime = -1.0;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            GUILayout.Space(8);
            if (GUILayout.Button("▶ Play", GUILayout.Height(30)))
                _play();
        }

        void _play() {
            var fb = (MBFireBurst)target;
            fb.Rebuild();   // apply the current inspector values every press
            if (Application.isPlaying) {
                fb.Play(fb.transform.position);
                return;
            }
            _startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= _tick;
            EditorApplication.update += _tick;
        }

        void _tick() {
            var fb = target as MBFireBurst;
            if (fb == null) { EditorApplication.update -= _tick; return; }
            float t = (float)(EditorApplication.timeSinceStartup - _startTime);
            if (t >= fb.Duration) {
                EditorApplication.update -= _tick;
                fb.Hide();
            } else {
                fb.Evaluate(t);
            }
            SceneView.RepaintAll();
        }

        void OnDisable() {
            EditorApplication.update -= _tick;
        }
    }
}
