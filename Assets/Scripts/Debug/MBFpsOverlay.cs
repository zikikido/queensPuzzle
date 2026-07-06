using UnityEngine;

namespace qp {
    /// <summary>
    /// Tiny FPS counter in the screen corner (IMGUI, survives scene loads). Toggled from
    /// MBDebugWin; the state persists in AppData.ShowFps, so it auto-starts on every run
    /// until turned off again.
    /// </summary>
    public class MBFpsOverlay : MonoBehaviour {

        static MBFpsOverlay _instance;

        const float Interval = 0.5f;   // refresh rate of the readout
        float _accum, _timeLeft;
        int _frames;
        float _fps;
        GUIStyle _style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart() {
            if (AppData.ShowFps.Value) Show();
        }

        public static void Toggle() {
            AppData.ShowFps.Value = !AppData.ShowFps.Value;
            if (AppData.ShowFps.Value) Show();
            else if (_instance != null) Destroy(_instance.gameObject);
        }

        static void Show() {
            if (_instance != null) return;
            var go = new GameObject("$FpsOverlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MBFpsOverlay>();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Update() {
            _accum += Time.unscaledDeltaTime;
            _frames++;
            _timeLeft -= Time.unscaledDeltaTime;
            if (_timeLeft <= 0f) {
                _fps = _frames / _accum;
                _accum = 0f; _frames = 0; _timeLeft = Interval;
            }
        }

        void OnGUI() {
            if (_style == null)
                _style = new GUIStyle {
                    fontSize = Mathf.RoundToInt(16 * Mathf.Max(1f, Screen.dpi / 96f)),
                    fontStyle = FontStyle.Bold
                };
            _style.normal.textColor = _fps < 30f ? Color.red : Color.green;
            GUI.Label(new Rect(8, 8, 300, _style.fontSize * 2), $"FPS {_fps:0.}", _style);
        }
    }
}
