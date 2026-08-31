using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>Common head of every event document — the server routes by `eventname`.</summary>
    [Serializable]
    public abstract class EventBase {
        public string eventname;     // collection name server-side (letters/digits/underscore)
        public string event_id;      // GUID per event — server dedups on it, so retries never double-count
        public int age_sec;          // seconds between the event happening and this send (set at flush) — server: ts = now - age
        public string app_version;
        public int first_version;    // UserData.FirstVersion — build the player first installed on
        public string user_id;       // UserID.GetUserIDLocal() — per-install GUID (resets on clear data / reinstall)
        public string platform;      // "IOS" / "Android" (set in FillCommon; editor sends e.g. "OSXEditor")
        public SingularSource singular_source; // install attribution (network/campaign...). Until Singular resolves it
                                               // JsonUtility emits it with empty strings — network=="" means "not yet known"
    }

    /// <summary>One app launch (cold start). Retention = users with a session_start on day N after
    /// their first one; `session == 1` marks the install session.</summary>
    [Serializable]
    public class SessionStartPayload : EventBase {
        public int session;          // UserData.Sessions — 1-based launch counter
        public int lvl_idx;          // campaign progress at launch
    }

    /// <summary>One paid ad impression (MAX OnAdRevenuePaidEvent) → events-server `ad_impression`.
    /// Carries `user_id`, so ad revenue joins to install source (extra_data.user_id) for ROAS.</summary>
    [Serializable]
    public class AdImpressionPayload : EventBase {
        public string ad_platform;   // always "AppLovin" (the mediation layer)
        public string network;       // MAX AdInfo.NetworkName — the winning demand source
        public string format;        // AdInfo.AdFormat — banner / interstitial / rewarded
        public string ad_unit;       // AdInfo.AdUnitIdentifier
        public string placement;     // AdInfo.Placement (may be empty)
        public double revenue;       // AdInfo.Revenue, USD
        public string precision;     // AdInfo.RevenuePrecision — exact / estimated / ...
    }

    /// <summary>Serialized shape of one level event document sent to the events server.</summary>
    [Serializable]
    public class EventPayload : EventBase {
        public int lvl_idx;          // campaign level index (daily: day index)
        public int lvl_hash;         // LevelLoader.CurrentLevelHash is an int
        public string level_set_id;  // LevelLoader.CurrentLevelSetId
        public int lvl_attempts;
        public int lvl_time_sec;
        public bool daily;
    }

    /// <summary>
    /// Fire-and-forget event reporter. Events are appended to an in-memory rolling
    /// buffer and flushed to the events server every <see cref="FLUSH_INTERVAL_SEC"/>
    /// seconds (and once on app pause) as a single mixed batch — the server routes each
    /// event by its `eventname`. Nothing is persisted between sessions; if it didn't send
    /// before the app closes, it's dropped. Never blocks gameplay, never throws.
    /// Auto-bootstraps, so no scene wiring is needed.
    ///
    /// Delivery: at-least-once. A batch is retried on network errors / 5xx (server dedups
    /// on `event_id`, so a retry after a lost response can't double-count) and DROPPED on
    /// 4xx (the server will never accept it — retrying would only block newer events).
    /// </summary>
    public sealed class EventClient : MonoBehaviour {

        // ---- config ----
        const string EVENTS_URL = "https://events-server-production-5f5c.up.railway.app/events";
        const string API_KEY = "42decc823d7c9a3d35b7d87dbccda5d48a98b0f77af2ad8b86ef1009f129ce3e";
        const string DB_NAME = "pawdoku_events";
        const float FLUSH_INTERVAL_SEC = 3f;   // short on purpose: session_start must land even in a 5s session
        const int MAX_BUFFER = 200;            // rolling cap — oldest dropped when exceeded

        // Editor/dev play sessions are kept out of the production data.
        const bool SEND_FROM_EDITOR = false;

        // One buffered event: the payload + when it happened (realtime clock, so pause-safe).
        // Serialized at flush time so `age_sec` is fresh on every attempt, including retries.
        struct Pending {
            public EventBase payload;
            public float createdAt;
        }

        static EventClient _instance;
        readonly List<Pending> _buffer = new List<Pending>();
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
        public static void Enqueue(EventBase payload) {
            if (_instance == null || payload == null || string.IsNullOrEmpty(payload.eventname)) return;
            _instance.Add(payload);
        }

        void Add(EventBase payload) {
            if (string.IsNullOrEmpty(payload.event_id))
                payload.event_id = Guid.NewGuid().ToString("N");

            _buffer.Add(new Pending { payload = payload, createdAt = Time.realtimeSinceStartup });
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
            var batch = new List<Pending>(_buffer);
            _buffer.Clear();

            long code = 0;
            bool ok = false;
            yield return SendBatch(BuildJsonArray(batch), (res, c) => { ok = res; code = c; });

            if (!ok) {
                bool permanent = code >= 400 && code < 500;
                if (permanent) {
                    // The server will never accept this batch — dropping it beats blocking every
                    // newer event behind it forever.
                    CDebug.LogError($"[EventClient] batch of {batch.Count} rejected with {code} — dropped");
                } else {
                    // Network / 5xx: requeue in front of anything queued during the send.
                    batch.AddRange(_buffer);
                    _buffer.Clear();
                    _buffer.AddRange(batch);
                    while (_buffer.Count > MAX_BUFFER) _buffer.RemoveAt(0);
                }
            }

            _sending = false;
        }

        // Serializes NOW so age_sec reflects this attempt (a retried event keeps ageing).
        static string BuildJsonArray(List<Pending> items) {
            float now = Time.realtimeSinceStartup;
            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (var it in items) {
                it.payload.age_sec = Mathf.Max(0, Mathf.RoundToInt(now - it.createdAt));
                string json;
                try { json = JsonUtility.ToJson(it.payload); }
                catch (Exception e) { CDebug.LogError(e); continue; }
                if (!first) sb.Append(',');
                sb.Append(json);
                first = false;
            }
            sb.Append(']');
            return sb.ToString();
        }

        IEnumerator SendBatch(string jsonArray, Action<bool, long> done) {
            var req = new Server.RequestWithResult(EVENTS_URL);
            req.WithParam(jsonArray);                    // pre-serialized array, sent as-is
            req.WithHeader("X-Api-Key", API_KEY);
            req.WithHeader("X-Db-Name", DB_NAME);
            req.SetNoRetries();                          // our flush loop owns retry cadence

            yield return req.SendRequest();
            done?.Invoke(req.Successful, req.ResponseCode);
        }

        void OnApplicationPause(bool paused) {
            if (paused && isActiveAndEnabled) StartCoroutine(Flush());
        }
    }
}
