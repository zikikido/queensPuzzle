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
            var enc = new UnityEditor.Recorder.Encoder.CoreEncoderSettings {
                Codec = UnityEditor.Recorder.Encoder.CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = (UnityEditor.Recorder.Encoder.CoreEncoderSettings.VideoEncodingQuality)Quality
            };
            // the Recorder's own "High" lands ~4.7 Mbps at 1080×1920 — fine for the static board,
            // but a full-screen BG video (hook / end card) breaks into blocks. High = 20 Mbps.
            if (Quality == 2) {
                enc.EncodingQuality = UnityEditor.Recorder.Encoder.CoreEncoderSettings.VideoEncodingQuality.Custom;
                enc.TargetBitRate = 20f;
                enc.EncodingProfile = UnityEditor.Recorder.Encoder.CoreEncoderSettings.H264EncodingProfile.High;
            }
            movie.EncoderSettings = enc;
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

        public static bool IsCompressing { get; private set; }

        /// <summary>The Recorder's real-time encoder needs a big bitrate to look clean, so the take
        /// is captured as a heavy master and re-encoded here with x264 (slow preset, CRF 20, 30 fps,
        /// faststart) — a small file at full quality for the ad networks. Runs in the background;
        /// the master is deleted on success, kept (and revealed) on failure.</summary>
        public static void Compress(string master, string final) {
            string ffmpeg = GPCardVideo.FindFfmpeg();
            if (ffmpeg == null) {
                Debug.LogError("[GPVideoCapture] ffmpeg not found — the uncompressed master is kept");
                UnityEditor.EditorUtility.RevealInFinder(master);
                return;
            }
            IsCompressing = true;
            double since = UnityEditor.EditorApplication.timeSinceStartup;
            System.Diagnostics.Process proc = null;
            var err = new System.Text.StringBuilder();
            UnityEditor.EditorApplication.CallbackFunction tick = null;
            tick = () => {
                if (proc == null) {
                    // the Recorder finalizes the mp4 a moment after Stop — wait until it's released
                    if (!IsReleased(master)) {
                        if (UnityEditor.EditorApplication.timeSinceStartup - since < 30) return;
                        Finish(false, "the master never got released");
                        return;
                    }
                    var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg,
                        $"-y -v error -i \"{master}\" -c:v libx264 -preset slow -crf 20 -r 30 -pix_fmt yuv420p " +
                        $"-profile:v high -movflags +faststart -c:a aac -b:a 128k \"{final}\"") {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
                    };
                    proc = System.Diagnostics.Process.Start(psi);
                    proc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                    proc.BeginErrorReadLine();
                    Debug.Log($"[GPVideoCapture] compressing → {System.IO.Path.GetFileName(final)}…");
                    return;
                }
                if (!proc.HasExited) return;
                bool ok = proc.ExitCode == 0 && System.IO.File.Exists(final);
                lock (err) Finish(ok, err.ToString());
            };
            void Finish(bool ok, string why) {
                UnityEditor.EditorApplication.update -= tick;
                IsCompressing = false;
                if (ok) {
                    long before = new System.IO.FileInfo(master).Length, after = new System.IO.FileInfo(final).Length;
                    System.IO.File.Delete(master);
                    Debug.Log($"[GPVideoCapture] {System.IO.Path.GetFileName(final)}: {after / 1048576f:0.0} MB (master was {before / 1048576f:0.0} MB)");
                    UnityEditor.EditorUtility.RevealInFinder(final);
                } else {
                    Debug.LogError($"[GPVideoCapture] compression failed — master kept\n{why}");
                    UnityEditor.EditorUtility.RevealInFinder(master);
                }
            }
            UnityEditor.EditorApplication.update += tick;
        }

        static bool IsReleased(string path) {
            if (!System.IO.File.Exists(path)) return false;
            try {
                using (System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None)) return true;
            } catch (System.IO.IOException) { return false; }
        }
    }
}
