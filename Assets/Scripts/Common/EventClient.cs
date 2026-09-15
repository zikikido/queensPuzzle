using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        public int lives_lost;       // lives (bones) lost during the attempt — LastPlayData.bonesLost
        public int lives_added;      // fail-continue refills — LastPlayData.livesAdded
        public int hints_used;       // LastPlayData.hintsUsed — winstats "solved without hints"
        public int queen_boosts_used;// LastPlayData.queenBoostsUsed — counts as a "helper" too
    }

    /// <summary>
    /// Fire-and-forget event reporter. Events are appended to an in-memory rolling buffer and
    /// flushed to the events server every <see cref="FLUSH_INTERVAL_SEC"/> seconds as a single
    /// mixed batch — the server routes each event by its `eventname`. Nothing is persisted
    /// between sessions; if it didn't send before the app closes, it's dropped.
    /// Never blocks gameplay, never throws. Auto-bootstraps, so no scene wiring is needed.
    ///
    /// Delivery runs on a background Task over HttpClient, NOT a coroutine over UnityWebRequest:
    /// a fullscreen ad pauses the Unity activity on Android, which would stop a coroutine for the
    /// ad's whole duration. Producers may therefore call Enqueue from any thread, and nothing
    /// below Add() may touch a Unity API.
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
        // Ceiling for the back-off while the server is unreachable. Kept fairly low on purpose:
        // the buffer is in memory only, so every second an event waits is a second in which the
        // OS could reclaim the process and take it with it. Battery against loss risk.
        const float MAX_RETRY_SEC = 60f;
        const int MAX_BUFFER = 200;            // rolling cap — oldest dropped when exceeded
        // The server accepts 1 MB bodies and 500 items; stay well inside both so a flush can never
        // be rejected as too large and lose the whole batch. The remainder rides the next tick.
        const int MAX_BATCH_BYTES = 256 * 1024;

        // Editor/dev play sessions are kept out of the production data.
        const bool SEND_FROM_EDITOR = false;

        // One buffered event: the payload + when it happened (realtime clock, so pause-safe).
        // Serialized at flush time so `age_sec` is fresh on every attempt, including retries.
        // Stopwatch, not Time.realtimeSinceStartup: Add() runs on a background thread for fullscreen
        // ad revenue, and Unity's clock is a Unity API. Stopwatch is a plain monotonic counter.
        struct Pending {
            public EventBase payload;
            public long createdAt;   // Stopwatch ticks
        }

        static EventClient _instance;
        readonly List<Pending> _buffer = new List<Pending>();
        readonly ConcurrentQueue<(string Message, bool IsError)> _diagnostics
            = new ConcurrentQueue<(string, bool)>();
        CancellationTokenSource _stop;
        HttpClient _http;
        double _retryDelaySec = FLUSH_INTERVAL_SEC;   // grows only while the server is unreachable
        bool _reportedFlushBug;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() {
            if (_instance != null) return;
            var go = new GameObject("EventClient");
            _instance = go.AddComponent<EventClient>();
            DontDestroyOnLoad(go);
        }

        void Start() {
            // Task + HttpClient, not coroutine + UnityWebRequest: both of those are driven by the
            // player loop, and behind a fullscreen ad on Android the Unity activity is paused, so
            // the loop stops and NOTHING can send for the ad's whole duration. Measured on live
            // data, that left 95% of interstitial and 99% of rewarded impressions arriving with
            // age_sec > 10s (banner: 0.15%) — and whatever the OS reclaimed inside that window was
            // lost outright. A thread-pool task keeps running as long as the process does, so
            // delivery no longer depends on the engine being awake.
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _http.DefaultRequestHeaders.Add("X-Api-Key", API_KEY);
            _http.DefaultRequestHeaders.Add("X-Db-Name", DB_NAME);
            _stop = new CancellationTokenSource();
            _ = FlushLoop(_stop.Token);
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

            // MAX delivers fullscreen ad revenue on a background thread, so this runs off the main
            // thread while Flush() may be reading the same List on it. List<T> is not thread-safe:
            // new List<T>(_buffer) reads Count, allocates, then copies, and a concurrent Add
            // between those steps throws.
            lock (_buffer) {
                _buffer.Add(new Pending { payload = payload, createdAt = Stopwatch.GetTimestamp() });
                while (_buffer.Count > MAX_BUFFER) _buffer.RemoveAt(0);   // drop oldest
            }
        }

        // ---- flushing ----

        // One sequential loop owns delivery, so there is no _sending latch to get stuck any more.
        // No Unity API below this point — it all runs off the main thread.
        async Task FlushLoop(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try { await Task.Delay(TimeSpan.FromSeconds(_retryDelaySec), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                bool serverUnavailable;
                try {
                    serverUnavailable = await Flush(token).ConfigureAwait(false);
                } catch (Exception e) {
                    // Flush throwing is a bug in OUR code, not a server condition. Waiting longer
                    // fixes nothing — it would throw again next tick — so keep the normal cadence
                    // and just make sure one broken build cannot flood Crashlytics.
                    if (!_reportedFlushBug) { _reportedFlushBug = true; Report("flush failed: " + e.GetType().Name, true); }
                    serverUnavailable = false;
                }

                // Slow down ONLY while the server is out of reach. Any other outcome — delivered,
                // nothing queued, or a malformed batch dropped — goes straight back to the normal
                // interval, because none of those get better by waiting.
                _retryDelaySec = serverUnavailable
                    ? Math.Min(_retryDelaySec * 2, MAX_RETRY_SEC)
                    : FLUSH_INTERVAL_SEC;
            }
        }

        /// <summary>
        /// Sends one batch. Returns TRUE only when the server could not be reached or was not ready
        /// — a connection failure, a timeout, 5xx, 408 or 429 — which is the single condition worth
        /// slowing the loop down for. Everything else returns FALSE: an empty queue, a delivered
        /// batch, an event that failed to serialize (dropped in place), and a malformed batch the
        /// server will never accept (dropped). None of those get better by waiting longer.
        /// </summary>
        async Task<bool> Flush(CancellationToken token) {
            var http = _http;
            if (http == null) return false;

            List<Pending> batch;
            lock (_buffer) {
                if (_buffer.Count == 0) return false;
#if UNITY_EDITOR
                if (!SEND_FROM_EDITOR) { _buffer.Clear(); return false; }
#endif
                // Taken out under the lock; events arriving mid-send stay for the next tick.
                batch = new List<Pending>(_buffer);
                _buffer.Clear();
            }

            // Serializing caps the request at MAX_BATCH_BYTES; anything that did not fit goes
            // straight back so the next tick carries it, rather than riding along and risking a
            // 413 that would take the whole batch down with it.
            string json = BuildJsonArray(batch, out int used);
            if (used < batch.Count) {
                Requeue(batch.GetRange(used, batch.Count - used));
                batch.RemoveRange(used, batch.Count - used);
            }
            // "[]" — every event in it failed to serialize. They were dropped, not deferred, so
            // this is not a server problem and must not trigger a back-off.
            if (json.Length <= 2) return false;

            long code = 0;
            bool ok = false;
            try {
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var res = await http.PostAsync(EVENTS_URL, content, token).ConfigureAwait(false)) {
                    code = (int)res.StatusCode;
                    ok = res.IsSuccessStatusCode;
                }
            } catch (Exception e) {
                // Offline, DNS, timeout, shutdown — the server is out of reach, so keep the batch
                // and let the loop slow down.
                if (!token.IsCancellationRequested)
                    Report("delivery failed, retained " + batch.Count + ": " + e.GetType().Name);
                Requeue(batch);
                return true;
            }

            if (ok) return false;

            // 408 (request timeout) and 429 (rate limited) are 4xx but transient — retrying is
            // exactly what the server is asking for. Everything else in the 4xx range means this
            // batch is malformed and will never be accepted, so dropping it beats blocking every
            // newer event behind it forever. 413 cannot come from our side any more (see the byte
            // cap above), but if it ever does, the batch really is unsendable as built.
            bool transient = code == 408 || code == 429;
            if (code >= 400 && code < 500 && !transient) {
                Report("batch of " + batch.Count + " rejected with " + code + " — dropped", true);
                return false;   // the queue moved on; nothing to wait for
            }
            Requeue(batch);
            return true;        // 5xx, 408, 429: the server is not ready — back off
        }

        // Put a failed batch back in front of whatever was queued while it was in flight.
        void Requeue(List<Pending> batch) {
            lock (_buffer) {
                batch.AddRange(_buffer);
                _buffer.Clear();
                _buffer.AddRange(batch);
                while (_buffer.Count > MAX_BUFFER) _buffer.RemoveAt(0);
            }
        }

        // Serializes NOW so age_sec reflects this attempt (a retried event keeps ageing).
        // JsonUtility.ToJson is documented as safe on background threads for plain serializable
        // types, which is all EventBase and its subclasses are.
        //
        // Stops once the request would exceed MAX_BATCH_BYTES and reports how many items it
        // consumed, so the caller can hand the rest back. `used` counts items disposed of — an
        // item that failed to serialize is counted, because it is being dropped, not deferred.
        string BuildJsonArray(List<Pending> items, out int used) {
            long now = Stopwatch.GetTimestamp();
            var sb = new StringBuilder();
            sb.Append('[');
            int appended = 0;
            used = 0;
            for (int i = 0; i < items.Count; i++) {
                var it = items[i];
                double waited = (now - it.createdAt) / (double)Stopwatch.Frequency;
                it.payload.age_sec = (int)System.Math.Max(0, System.Math.Round(waited));
                string json;
                try { json = JsonUtility.ToJson(it.payload); }
                catch (Exception e) {
                    Report("could not serialize event: " + e.GetType().Name, true);
                    used = i + 1;   // unserializable: drop it rather than retry it forever
                    continue;
                }
                // Always take at least one, even if it alone blows the budget: the server will
                // reject that single event and it gets dropped, instead of wedging the queue.
                if (appended > 0 && sb.Length + json.Length + 2 > MAX_BATCH_BYTES) break;
                if (appended > 0) sb.Append(',');
                sb.Append(json);
                appended++;
                used = i + 1;
            }
            sb.Append(']');
            return sb.ToString();
        }

        // Both sinks are Unity APIs, so the send loop cannot call them directly. Messages are
        // queued here and drained from Update on the main thread.
        //
        // isError decides WHICH sink. CDebug.LogError files a Crashlytics NON-FATAL — a crash
        // report — so it must be reserved for actual faults. Losing network is not a fault: it is
        // the single most ordinary thing that happens to a phone, and reporting it was filing a
        // crash for every player who walked into a lift. Those go to CDebug.CrashLog instead,
        // which is a breadcrumb: invisible on its own, but attached as context to any real crash
        // report that follows.
        void Report(string message, bool isError = false) {
            if (_diagnostics.Count < 50) _diagnostics.Enqueue((message, isError));
        }

        void Update() {
            for (int i = 0; i < 5 && _diagnostics.TryDequeue(out var d); i++) {
                if (d.IsError) CDebug.LogError("[EventClient] " + d.Message);
                else CDebug.CrashLog("[EventClient] " + d.Message);
            }
        }

        // No OnApplicationPause flush any more: the loop keeps running while the app is in the
        // background, which is the entire point of moving off the coroutine. Coming back to the
        // foreground does clear any accumulated back-off though — that is the moment connectivity
        // has usually returned, so there is no reason to sit out a delay earned while away.
        void OnApplicationPause(bool paused) {
            if (!paused) _retryDelaySec = FLUSH_INTERVAL_SEC;
        }

        void OnApplicationQuit() { Stop(); }
        void OnDestroy() { Stop(); }

        void Stop() {
            var stop = _stop;
            var http = _http;
            _stop = null;
            _http = null;
            if (stop != null) { stop.Cancel(); stop.Dispose(); }
            http?.Dispose();   // aborts anything in flight; FlushLoop catches and exits
        }
    }
}
