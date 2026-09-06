using UnityEditor;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Adds a Play button to MBHeartsFloat: in play mode it fires Play(); in edit mode it
    /// scrubs Evaluate() from the editor update loop so the effect previews without playing.
    /// </summary>
    [CustomEditor(typeof(MBHeartsFloat))]
    public class MBHeartsFloatEditor : Editor {

        double _startTime = -1.0;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            GUILayout.Space(8);
            if (GUILayout.Button("▶ Play", GUILayout.Height(30)))
                _play();
        }

        void _play() {
            var hf = (MBHeartsFloat)target;
            hf.Rebuild();   // apply the current inspector values every press
            if (Application.isPlaying) {
                hf.Play(hf.transform.position);
                return;
            }
            _startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= _tick;
            EditorApplication.update += _tick;
        }

        void _tick() {
            var hf = target as MBHeartsFloat;
            if (hf == null) { EditorApplication.update -= _tick; return; }
            float t = (float)(EditorApplication.timeSinceStartup - _startTime);
            if (t >= hf.Duration) {
                EditorApplication.update -= _tick;
                hf.Hide();
            } else {
                hf.Evaluate(t);
            }
            SceneView.RepaintAll();
        }

        void OnDisable() {
            EditorApplication.update -= _tick;
        }
    }
}
