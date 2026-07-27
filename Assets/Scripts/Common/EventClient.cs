using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>Serialized shape of one event document sent to the events server.</summary>
    [Serializable]
    public class EventPayload {
        public string eventname;     // "level_start" / "level_won" / "level_lost" — server fans out by this
        public int lvl_hash;         // LevelLoader.CurrentLevelHash is an int
        public string level_set_id;  // LevelLoader.CurrentLevelSetId
        public int lvl_attempts;
        public int lvl_time_sec;
        public string app_version;
        public bool daily;
    }

    /// <summary>
    /// Fire-and-forget event reporter. Events are appended to an in-memory rolling
    /// buffer and flushed to the events server every <see cref="FLUSH_INTERVAL_SEC"/>
    /// seconds (and once on app pause) as a single mixed batch — the server routes each
    /// event by its `eventname`. Nothing is persisted between sessions; if it didn't send
    /// before the app closes, it's dropped. Never blocks gameplay, never throws.
    /// Auto-bootstraps, so no scene wiring is needed.
    /// </summary>
    public sealed class EventClient : MonoBehaviour {

        // ---- config ----
        const string EVENTS_URL = "https://events-server-production-5f5c.up.railway.app/events";
        const string API_KEY = "42decc823d7c9a3d35b7d87dbccda5d48a98b0f77af2ad8b86ef1009f129ce3e";
        const string DB_NAME = "pawdoku_events";
        const float FLUSH_INTERVAL_SEC = 15f;
        const int MAX_BUFFER = 200;      // rolling cap — oldest dropped when exceeded

        // Editor/dev play sessions are kept out of the production data.
        const bool SEND_FROM_EDITOR = false;

        static EventClient _instance;
        readonly List<string> _buffer = new List<string>();   // pre-serialized event JSON
        bool _sending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() {
            if (_instance != null) return;
            var go = new GameObject("EventClient");
            _instance = go.AddComponent<EventClient>();
            DontDestroyOnLoad(go);
        }

        void Start() {
            StartCoroutine(FlushLoop());
        }

        // ---- public API ----

        /// <summary>Queue one event. `payload.eventname` decides its collection server-side.</summary>
        public static void Enqueue(EventPayload payload) {
            if (_instance == null || payload == null || string.IsNullOrEmpty(payload.eventname)) return;
            _instance.Add(payload);
        }

        void Add(EventPayload payload) {
            string json;
            try {
                json = JsonUtility.ToJson(payload);
            } catch (Exception e) {
                CDebug.LogError(e);
                return;
            }

            _buffer.Add(json);
            while (_buffer.Count > MAX_BUFFER) _buffer.RemoveAt(0);   // drop oldest
        }

        // ---- flushing ----

        IEnumerator FlushLoop() {
            var wait = new WaitForSecondsRealtime(FLUSH_INTERVAL_SEC);
            while (true) {
                yield return wait;
                yield return Flush();
            }
        }

        IEnumerator Flush() {
            if (_sending || _buffer.Count == 0) yield break;

#if UNITY_EDITOR
            if (!SEND_FROM_EDITOR) { _buffer.Clear(); yield break; }
#endif
            // Best effort: skip while we believe we're offline; keep buffering (bounded).
            if (InternetConnection.Instance != null && !InternetConnection.Instance.HasInternet) yield break;

            _sending = true;

            // Take the current buffer out; new events that arrive mid-send stay for next tick.
            var batch = new List<string>(_buffer);
            _buffer.Clear();

            bool ok = false;
            yield return SendBatch(BuildJsonArray(batch), res => ok = res);

            if (!ok) {
                // Requeue the failed batch in front of anything queued during the send.
                batch.AddRange(_buffer);
                _buffer.Clear();
                _buffer.AddRange(batch);
                while (_buffer.Count > MAX_BUFFER) _buffer.RemoveAt(0);
            }

            _sending = false;
        }

        static string BuildJsonArray(List<string> items) {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < items.Count; i++) {
                if (i > 0) sb.Append(',');
                sb.Append(items[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }

        IEnumerator SendBatch(string jsonArray, Action<bool> done) {
            var req = new Server.RequestWithResult(EVENTS_URL);
            req.WithParam(jsonArray);                    // pre-serialized array, sent as-is
            req.WithHeader("X-Api-Key", API_KEY);
            req.WithHeader("X-Db-Name", DB_NAME);
            req.SetNoRetries();                          // our flush loop owns retry cadence

            yield return req.SendRequest();
            done?.Invoke(req.Successful);
        }

        void OnApplicationPause(bool paused) {
            if (paused && isActiveAndEnabled) StartCoroutine(Flush());
        }
    }
}
