using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Drives the Unity Recorder package for 🎬 Record GP: capture starts together with the
    /// replay and stops after it, so the video's t=0 equals the record's t=0 — voices and
    /// subtitles land in sync by construction. Captures the Game View with audio.
    /// </summary>
    public static class GPVideoCapture {

        static RecorderController _controller;

        public static bool IsCapturing => _controller != null && _controller.IsRecording();

        // capture options (machine-level prefs, not part of the record)
        public static int Quality {   // 0 Low · 1 Medium · 2 High
            get => UnityEditor.EditorPrefs.GetInt("GPRecorder.CaptureQuality", 2);
            set => UnityEditor.EditorPrefs.SetInt("GPRecorder.CaptureQuality", value);
        }

        public static int Fps {
            get => UnityEditor.EditorPrefs.GetInt("GPRecorder.CaptureFps", 60);
            set => UnityEditor.EditorPrefs.SetInt("GPRecorder.CaptureFps", value);
        }

        // the Recorder FORCES the Game View to this resolution while capturing
        public static int Width {
            get => UnityEditor.EditorPrefs.GetInt("GPRecorder.CaptureW", 1080);
            set => UnityEditor.EditorPrefs.SetInt("GPRecorder.CaptureW", value);
        }

        public static int Height {
            get => UnityEditor.EditorPrefs.GetInt("GPRecorder.CaptureH", 1920);
            set => UnityEditor.EditorPrefs.SetInt("GPRecorder.CaptureH", value);
        }

        /// <summary>Start capturing to <paramref name="outputNoExt"/>.mp4 (path without extension).</summary>
        public static void Start(string outputNoExt) {
            Stop();
            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "GP Recorder";
            movie.Enabled = true;
            movie.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            movie.EncoderSettings = new UnityEditor.Recorder.Encoder.CoreEncoderSettings {
                Codec = UnityEditor.Recorder.Encoder.CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = (UnityEditor.Recorder.Encoder.CoreEncoderSettings.VideoEncodingQuality)Quality
            };
            movie.ImageInputSettings = new GameViewInputSettings {
                OutputWidth = Width,
                OutputHeight = Height
            };
            movie.CaptureAudio = true;
            movie.OutputFile = outputNoExt;
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRatePlayback = FrameRatePlayback.Constant;
            settings.FrameRate = Fps;
            settings.CapFrameRate = true;
            _controller = new RecorderController(settings);
            _controller.PrepareRecording();
            _controller.StartRecording();
        }

        public static void Stop() {
            if (_controller == null) return;
            if (_controller.IsRecording()) _controller.StopRecording();
            _controller = null;
        }
    }
}
