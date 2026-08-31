using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace qp {

    /// <summary>
    /// ElevenLabs text-to-speech for the recorder's voice lines. Generates
    /// recordFolder/Voices/&lt;set&gt;/&lt;name&gt;.mp3 (44.1kHz 128kbps — raw PCM needs the Pro tier)
    /// and writes the relative path back into the line. The API key and voice id live in
    /// EditorPrefs — never in the repo.
    /// </summary>
    public static class GPVoiceGen {

        const string KeyPref = "GPRecorder.ElevenKey";
        const string Model = "eleven_multilingual_v2";

        // the key is a SECRET — EditorPrefs only. The voice id is content — it lives in the
        // voice set's json, so each set carries its own voice.
        public static string ApiKey {
            get => EditorPrefs.GetString(KeyPref, "");
            set => EditorPrefs.SetString(KeyPref, value);
        }

        public static bool Ready => !string.IsNullOrEmpty(ApiKey);

        static readonly HashSet<string> _busy = new HashSet<string>();
        public static bool IsBusy(string lineName) => _busy.Contains(lineName);

        // ---- the account's voice list (for the Voice ID picker) -----------------------

        [Serializable] class VoiceInfo { public string voice_id; public string name; public string category; }
        [Serializable] class VoicesResponse { public VoiceInfo[] voices; }

        public class VoiceOption { public string id; public string name; public bool premade; }

        public static List<VoiceOption> AccountVoices { get; private set; }
        static bool _fetchTried;

        public static string VoiceName(string id) {
            if (AccountVoices == null || string.IsNullOrEmpty(id)) return null;
            foreach (var v in AccountVoices) if (v.id == id) return v.name;
            return null;
        }

        /// <summary>Fetch once per session (or again via <paramref name="force"/> from the picker).</summary>
        public static async void FetchVoices(bool force = false) {
            if (!Ready || (_fetchTried && !force)) return;
            _fetchTried = true;
            try {
                using (var http = new HttpClient()) {
                    http.DefaultRequestHeaders.Add("xi-api-key", ApiKey);
                    var resp = await http.GetAsync("https://api.elevenlabs.io/v1/voices");
                    string body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode) {
                        Debug.LogError("[GPVoiceGen] voice list failed: " + body);
                        return;
                    }
                    var parsed = JsonUtility.FromJson<VoicesResponse>(body);
                    var list = new List<VoiceOption>();
                    foreach (var v in parsed.voices) list.Add(new VoiceOption { id = v.voice_id, name = v.name, premade = v.category == "premade" });
                    list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                    AccountVoices = list;
                }
            } catch (Exception e) {
                Debug.LogError("[GPVoiceGen] voice list: " + e.Message);
            }
        }

        // The free tier caps concurrent requests at 2 — Generate All must not fire everything at
        // once, so requests run through a queue, strictly one at a time (lines are short).
        static readonly Queue<Func<System.Threading.Tasks.Task>> _queue = new Queue<Func<System.Threading.Tasks.Task>>();
        static bool _draining;

        /// <summary>Queue one line for generation. onDone runs on the main thread after success
        /// OR failure — save the voices file and repaint there.</summary>
        public static void Generate(GPVoiceLine line, string voiceId, string recordFolder, string subFolder, Action onDone) {
            if (line == null || string.IsNullOrEmpty(voiceId) || _busy.Contains(line.name)) return;
            _busy.Add(line.name);   // shows as busy while queued too
            _queue.Enqueue(() => GenerateNow(line, voiceId, recordFolder, subFolder, onDone));
            Drain();
        }

        static async void Drain() {
            if (_draining) return;
            _draining = true;
            while (_queue.Count > 0) await _queue.Dequeue()();
            _draining = false;
        }

        static async System.Threading.Tasks.Task GenerateNow(GPVoiceLine line, string voiceId, string recordFolder, string subFolder, Action onDone) {
            try {
                var inv = CultureInfo.InvariantCulture;
                string json = "{\"text\":" + JsonString(line.text) +
                    ",\"model_id\":\"" + Model + "\"" +
                    ",\"voice_settings\":{" +
                    "\"stability\":" + line.stability.ToString(inv) +
                    ",\"similarity_boost\":0.9" +
                    ",\"style\":" + line.style.ToString(inv) +
                    ",\"use_speaker_boost\":true" +
                    ",\"speed\":" + line.speed.ToString(inv) + "}}";

                using (var http = new HttpClient()) {
                    http.DefaultRequestHeaders.Add("xi-api-key", ApiKey);
                    // mp3 — raw PCM/WAV needs the Pro tier
                    var resp = await http.PostAsync(
                        $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}?output_format=mp3_44100_128",
                        new StringContent(json, Encoding.UTF8, "application/json"));
                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    if (!resp.IsSuccessStatusCode) {
                        Debug.LogError($"[GPVoiceGen] '{line.name}' failed {(int)resp.StatusCode}: {Encoding.UTF8.GetString(bytes)}");
                        return;
                    }

                    string file = SafeName(line.name) + ".mp3";
                    string dir = Path.Combine(recordFolder, subFolder);
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(Path.Combine(dir, file), bytes);
                    line.path = subFolder + "/" + file;
                    Debug.Log($"[GPVoiceGen] ✓ {line.path}");
                }
            } catch (Exception e) {
                Debug.LogError($"[GPVoiceGen] '{line.name}': {e.Message}");
            } finally {
                _busy.Remove(line.name);
                onDone?.Invoke();
            }
        }

        static string SafeName(string name) {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        static string JsonString(string s) =>
            "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    }
}
