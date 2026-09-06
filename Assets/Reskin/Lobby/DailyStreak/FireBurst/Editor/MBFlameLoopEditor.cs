using UnityEditor;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Play/Stop buttons for MBFlameLoop: previews the looping fire in edit mode by driving
    /// Evaluate() from the editor update loop.
    /// </summary>
    [CustomEditor(typeof(MBFlameLoop))]
    public class MBFlameLoopEditor : Editor {

        double _startTime = -1.0;
        bool _running;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            GUILayout.Space(8);
            if (!_running || Application.isPlaying) {
                if (GUILayout.Button("▶ Play", GUILayout.Height(30)))
                    _play();
            } else if (GUILayout.Button("■ Stop", GUILayout.Height(30))) {
                _stop();
            }
        }

        void _play() {
            var fl = (MBFlameLoop)target;
            fl.Rebuild();   // apply current inspector values
            if (Application.isPlaying) return;   // runtime loop already drives it while enabled
            _startTime = EditorApplication.timeSinceStartup;
            _running = true;
            EditorApplication.update -= _tick;
            EditorApplication.update += _tick;
        }

        void _stop() {
            _running = false;
            EditorApplication.update -= _tick;
            var fl = target as MBFlameLoop;
            if (fl != null) fl.Hide();
            Repaint();
        }

        void _tick() {
            var fl = target as MBFlameLoop;
            if (fl == null) { _stop(); return; }
            fl.Evaluate((float)(EditorApplication.timeSinceStartup - _startTime));
            SceneView.RepaintAll();
        }

        void OnDisable() {
            EditorApplication.update -= _tick;
        }
    }
}
