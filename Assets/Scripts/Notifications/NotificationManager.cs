#if NOTIFICATION_INSTALLER

using System;
using System.Collections.Generic;
using Common;
using UnityEngine;
#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif
#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

namespace qp {

    /// <summary>
    /// The whole local-notification lifecycle in one place. Local notifications only — no Firebase,
    /// no remote push. Content and settings live in the <see cref="PawdokuNotificationSettings"/>
    /// asset (Resources), loaded once at boot and cached; background scheduling never touches
    /// Resources so it stays instant.
    ///
    /// Lifecycle (driven by <see cref="MBNotifications"/>):
    ///   OnAppLoad       — load+validate config, init platform, register the Android channel,
    ///                     cancel any leftover scheduled notifications. Does NOT schedule (the
    ///                     player is entering the game).
    ///   OnAppResume     — the player is active again: cancel everything scheduled, reset the guard.
    ///   OnAppBackground — the one scheduling moment: (re)build the whole schedule from cache.
    ///
    /// Permission is owned entirely by <see cref="Kido.NotificationPermissionHandler"/> — this
    /// class only READS the state and schedules when it is Allowed. It never requests permission
    /// and never touches the permission PlayerPrefs.
    /// </summary>
    public static class NotificationManager {

        const string SettingsResource = "PawdokuNotificationSettings";

        // ---- stable, deterministic IDs -------------------------------------------------
        // id(day, type) = IdBase + day*PerDay + type. Collision-free across the whole
        // scheduling window; predictable so notifications can be cancelled and debugged.
        const int IdBase = 900000;
        const int PerDay = 2;          // one Daily Challenge + one General slot per day
        const int TypeDaily = 0;
        const int TypeGeneral = 1;
        const int IdTest = 999001;     // the manual single test notification

        // iOS wants string identifiers; keep them 1:1 with the int ids so cancelling is symmetric.
        static string IosId(int id) => "pawdoku_" + id;
        static int NotificationId(int dayOffset, int type) => IdBase + dayOffset * PerDay + type;

        // ---- cached config -------------------------------------------------------------

        static PawdokuNotificationSettings _settings;
        static bool _loadAttempted;

        /// <summary>Load + validate the settings asset once, then cache it. Never reloads —
        /// so a background transition never calls Resources.Load. Returns false if unavailable.</summary>
        static bool EnsureLoaded() {
            if (_settings != null) return true;
            if (_loadAttempted) return false;   // already tried and failed — don't hammer Resources
            _loadAttempted = true;

            _settings = Resources.Load<PawdokuNotificationSettings>(SettingsResource);
            if (_settings == null) {
                Debug.LogError($"[Notifications] Resources/{SettingsResource}.asset not found — notifications disabled.");
                return false;
            }

            Validate(_settings);
            Log("settings loaded and cached");
            return true;
        }

        // Keep the runtime honest even if the asset was authored with bad values.
        static void Validate(PawdokuNotificationSettings s) {
            // iOS silently drops pending notifications past 64. With PerDay per day, cap the
            // window with headroom so the test notification and any slack always fit.
            const int iosPendingCap = 60;
            int maxDays = iosPendingCap / PerDay;
            if (s.daysAhead < 1) s.daysAhead = 1;
            if (s.daysAhead > maxDays) {
                Log($"daysAhead {s.daysAhead} clamped to {maxDays} (iOS 64 pending-notification cap)");
                s.daysAhead = maxDays;
            }

            s.dailyChallengeHour = Mathf.Clamp(s.dailyChallengeHour, 0, 23);
            s.dailyChallengeMinute = Mathf.Clamp(s.dailyChallengeMinute, 0, 59);
            s.generalHour = Mathf.Clamp(s.generalHour, 0, 23);
            s.generalMinute = Mathf.Clamp(s.generalMinute, 0, 59);
            if (s.debugIntervalSeconds < 1) s.debugIntervalSeconds = 1;
        }

        // ---- background dedup guard ----------------------------------------------------
        // Unity can fire pause/focus more than once per real transition (permission dialogs,
        // system overlays, app switching). Schedule at most once per background, reset on resume.
        static bool _scheduledThisBackground;

        // ---- lifecycle -----------------------------------------------------------------

        public static void OnAppLoad() {
            if (!EnsureLoaded()) return;
            InitPlatform();
            ClearAll();          // never inherit a stale schedule
            _scheduledThisBackground = false;
            Log("OnAppLoad ready");
        }

        public static void OnAppResume() {
            _scheduledThisBackground = false;
            if (_settings == null) return;
            ClearAll();          // player is active — don't leave reminders pending
            Log("OnAppResume — cleared, ready for next background");
        }

        public static void OnAppBackground() {
            if (_scheduledThisBackground) return;             // already scheduled this transition
            // Cache-only: never load Resources while going to background. Boot's OnAppLoad is the
            // sole loader; if it never ran or failed, we simply don't schedule.
            if (_settings == null) { Log("background: settings not cached — skipping"); return; }
            if (!_settings.notificationsEnabled) { Log("background: notifications disabled"); return; }
            if (!PermissionAllowed()) { Log("background: permission not allowed"); return; }

            ClearAll();
            ScheduleAll();
            _scheduledThisBackground = true;
            Log("OnAppBackground — schedule built");
        }

        // ---- platform init / channel ---------------------------------------------------

        static void InitPlatform() {
#if UNITY_ANDROID
            var channel = new AndroidNotificationChannel {
                Id = _settings.androidChannelId,
                Name = _settings.androidChannelName,
                Description = _settings.androidChannelDescription,
                Importance = _settings.defaultSound ? Importance.Default : Importance.Low,
                CanShowBadge = true,
                EnableVibration = true,
                EnableLights = true,
            };
            AndroidNotificationCenter.RegisterNotificationChannel(channel);
            Log("Android channel registered: " + _settings.androidChannelId);
#endif
            // iOS needs no channel; authorization is owned by NotificationPermissionHandler.
        }

        // ---- cancel --------------------------------------------------------------------
        // Clears BOTH still-pending (scheduled) notifications AND ones already delivered to the
        // tray — otherwise reminders that fired while backgrounded stay in the shade after the
        // player reopens. This app has no other notification system, so the package-wide clear is
        // safe; it never touches permission state, repetition history, or any gameplay data.
        static void ClearAll() {
#if UNITY_ANDROID
            AndroidNotificationCenter.CancelAllNotifications();   // scheduled + already displayed
#elif UNITY_IOS
            iOSNotificationCenter.RemoveAllScheduledNotifications();
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
#endif
        }

        // ---- permission (read-only) ----------------------------------------------------

        static bool PermissionAllowed() {
#if UNITY_EDITOR
            return true;   // editor can't request; treat as allowed so scheduling can be tested
#else
            return Kido.NotificationPermissionHandler.GetCurrentState()
                == Kido.NotificationPermissionHandler.PermissionState.Allowed;
#endif
        }

        // ---- scheduling ----------------------------------------------------------------
        // Immediate-repeat history: the last message picked for each stream, so we never send
        // the same text twice in a row. Persisted (local, lightweight) and carried across the
        // whole batch so consecutive scheduled days also differ. Never cleared on load.
        const string PrefLastDaily = "notif_last_daily";
        const string PrefLastGeneral = "notif_last_general";

        // Valid General categories (non-null, positive weight, at least one usable message) and
        // their total weight — prepared once per ScheduleAll so the per-day loop is pure math.
        static readonly List<PawdokuNotificationSettings.Category> _validCategories = new List<PawdokuNotificationSettings.Category>();
        static float _validWeightTotal;
        static readonly List<PawdokuNotificationSettings.Message> _scratch = new List<PawdokuNotificationSettings.Message>();

        static void ScheduleAll() {
            BuildValidCategories();

            if (_settings.debugFastMode) { ScheduleDebugFast(); return; }

            DateTime now = DateTime.Now;   // local device time — notifications are player-local
            int days = _settings.daysAhead;

            string lastDaily = PlayerPrefs.GetString(PrefLastDaily, "");
            string lastGeneral = PlayerPrefs.GetString(PrefLastGeneral, "");

            int scheduled = 0;
            for (int d = 0; d < days; d++) {
                DateTime midnight = now.Date.AddDays(d);

                DateTime dailyAt = midnight.AddHours(_settings.dailyChallengeHour).AddMinutes(_settings.dailyChallengeMinute);
                if (dailyAt > now) {                                   // never schedule in the past
                    var m = PickFromList(_settings.dailyChallengeMessages, ref lastDaily, _settings.dailyChallengeFallback);
                    Schedule(NotificationId(d, TypeDaily), m, dailyAt);
                    scheduled++;
                }

                DateTime generalAt = midnight.AddHours(_settings.generalHour).AddMinutes(_settings.generalMinute);
                if (generalAt > now) {
                    var m = PickGeneral(ref lastGeneral);
                    Schedule(NotificationId(d, TypeGeneral), m, generalAt);
                    scheduled++;
                }
            }

            PlayerPrefs.SetString(PrefLastDaily, lastDaily);
            PlayerPrefs.SetString(PrefLastGeneral, lastGeneral);
            PlayerPrefs.Save();

            Log($"scheduled {scheduled} notifications across {days} days");
        }

        // ---- message selection ---------------------------------------------------------

        static void BuildValidCategories() {
            _validCategories.Clear();
            _validWeightTotal = 0f;
            if (_settings.generalCategories == null) return;
            foreach (var c in _settings.generalCategories) {
                if (c == null || c.weight <= 0f || !HasUsableMessage(c.messages)) continue;
                _validCategories.Add(c);
                _validWeightTotal += c.weight;
            }
        }

        static bool HasUsableMessage(List<PawdokuNotificationSettings.Message> list) {
            if (list == null) return false;
            foreach (var m in list)
                if (m != null && !string.IsNullOrEmpty(m.body)) return true;
            return false;
        }

        // Weighted category pick → one message from it, avoiding the last General text.
        static PawdokuNotificationSettings.Message PickGeneral(ref string lastKey) {
            if (_validCategories.Count == 0) return _settings.generalFallback;

            float r = UnityEngine.Random.value * _validWeightTotal;
            var cat = _validCategories[_validCategories.Count - 1];
            for (int i = 0; i < _validCategories.Count; i++) {
                r -= _validCategories[i].weight;
                if (r <= 0f) { cat = _validCategories[i]; break; }
            }
            return PickFromList(cat.messages, ref lastKey, _settings.generalFallback);
        }

        // Pick a random usable message avoiding an immediate repeat of lastKey; updates lastKey.
        // If only one usable message exists it is reused (acceptable per spec). Falls back when none.
        static PawdokuNotificationSettings.Message PickFromList(
            List<PawdokuNotificationSettings.Message> list, ref string lastKey, PawdokuNotificationSettings.Message fallback) {

            _scratch.Clear();
            if (list != null)
                foreach (var m in list)
                    if (m != null && !string.IsNullOrEmpty(m.body)) _scratch.Add(m);

            if (_scratch.Count == 0) { lastKey = Key(fallback); return fallback; }

            PawdokuNotificationSettings.Message chosen;
            if (_scratch.Count == 1) {
                chosen = _scratch[0];
            } else {
                int i = UnityEngine.Random.Range(0, _scratch.Count);
                if (Key(_scratch[i]) == lastKey) i = (i + 1) % _scratch.Count;   // step off an immediate repeat
                chosen = _scratch[i];
            }
            lastKey = Key(chosen);
            return chosen;
        }

        static string Key(PawdokuNotificationSettings.Message m) =>
            m == null ? "" : m.title + "\n" + m.body;

        // ---- platform scheduling -------------------------------------------------------

        static void Schedule(int id, PawdokuNotificationSettings.Message msg, DateTime fireTime) {
            string title = Downgrade(msg != null ? msg.title : "");
            string body = Downgrade(msg != null ? msg.body : "");
#if UNITY_ANDROID
            var n = new AndroidNotification {
                Title = title,
                Text = body,
                FireTime = fireTime,
                SmallIcon = _settings.androidSmallIcon,
                LargeIcon = _settings.androidLargeIcon,
            };
            AndroidNotificationCenter.SendNotificationWithExplicitID(n, _settings.androidChannelId, id);
#elif UNITY_IOS
            var trigger = new iOSNotificationCalendarTrigger {
                Year = fireTime.Year, Month = fireTime.Month, Day = fireTime.Day,
                Hour = fireTime.Hour, Minute = fireTime.Minute, Second = 0,
                Repeats = false,
            };
            var n = new iOSNotification {
                Identifier = IosId(id),
                Title = title,
                Body = body,
                ShowInForeground = false,
                Trigger = trigger,
            };
            iOSNotificationCenter.ScheduleNotification(n);
#endif
        }

        // Fire at now + seconds (second precision — used by fast-mode and the test notification).
        // The per-day Schedule() uses a calendar trigger on the minute; this uses a time-interval
        // trigger so short delays work.
        static void ScheduleAfterSeconds(int id, PawdokuNotificationSettings.Message msg, int seconds, bool showInForeground = false) {
            string title = Downgrade(msg != null ? msg.title : "");
            string body = Downgrade(msg != null ? msg.body : "");
            seconds = Mathf.Max(1, seconds);
#if UNITY_ANDROID
            var n = new AndroidNotification {
                Title = title,
                Text = body,
                FireTime = DateTime.Now.AddSeconds(seconds),
                SmallIcon = _settings.androidSmallIcon,
                LargeIcon = _settings.androidLargeIcon,
            };
            AndroidNotificationCenter.SendNotificationWithExplicitID(n, _settings.androidChannelId, id);
#elif UNITY_IOS
            var trigger = new iOSNotificationTimeIntervalTrigger {
                TimeInterval = TimeSpan.FromSeconds(seconds),
                Repeats = false,
            };
            var n = new iOSNotification {
                Identifier = IosId(id),
                Title = title,
                Body = body,
                ShowInForeground = showInForeground,
                Trigger = trigger,
            };
            iOSNotificationCenter.ScheduleNotification(n);
#endif
        }

        // ---- debug --------------------------------------------------------------------
        // Fast-mode: instead of 10:00/18:00 over N days, fire the whole batch every
        // debugIntervalSeconds (alternating Daily/General) so the full flow can be verified on
        // the next open. Toggle it from the in-game debug panel, then press HOME.
        static void ScheduleDebugFast() {
            int interval = Mathf.Max(1, _settings.debugIntervalSeconds);
            int count = Mathf.Clamp(_settings.daysAhead * PerDay, 2, 60);

            string lastDaily = PlayerPrefs.GetString(PrefLastDaily, "");
            string lastGeneral = PlayerPrefs.GetString(PrefLastGeneral, "");

            for (int k = 0; k < count; k++) {
                bool daily = (k % 2 == 0);
                var m = daily
                    ? PickFromList(_settings.dailyChallengeMessages, ref lastDaily, _settings.dailyChallengeFallback)
                    : PickGeneral(ref lastGeneral);
                ScheduleAfterSeconds(IdBase + k, m, interval * (k + 1));
            }

            PlayerPrefs.SetString(PrefLastDaily, lastDaily);
            PlayerPrefs.SetString(PrefLastGeneral, lastGeneral);
            PlayerPrefs.Save();
            Log($"DEBUG fast-mode: scheduled {count} notifications, every {interval}s");
        }

        /// <summary>Runtime toggle for the debug panel (mutates the cached settings, not the asset on disk).</summary>
        public static bool DebugFastMode {
            get => _settings != null && _settings.debugFastMode;
            set { if (EnsureLoaded()) _settings.debugFastMode = value; }
        }

        /// <summary>Fire one notification after testNotificationDelaySec — for the debug panel.
        /// Shown even in the foreground on iOS so it can be seen without backgrounding.</summary>
        public static void FireTestNotification() {
            if (!EnsureLoaded()) return;
            InitPlatform();
#if !UNITY_EDITOR
            if (!PermissionAllowed()) { Log("test: permission not allowed"); return; }
#endif
            int delay = Mathf.Max(1, Mathf.RoundToInt(_settings.testNotificationDelaySec));
            var msg = new PawdokuNotificationSettings.Message {
                title = _settings.generalFallback != null ? _settings.generalFallback.title : "Pawdoku",
                body = $"🔔 Test notification — fires in {delay}s.",
            };
            ScheduleAfterSeconds(IdTest, msg, delay, showInForeground: true);
            Log($"test notification scheduled in {delay}s");
        }

        // ---- emoji compatibility -------------------------------------------------------
        // Notifications are drawn with the OS emoji font, so an emoji newer than the device's
        // Android/iOS version shows as a "tofu" box. Author messages with the BEST/newest emoji;
        // this layer swaps only the too-new ones for a safe fallback, per device. Modern devices
        // keep the nice emoji untouched.
        //
        // MinApi = the Android API level whose font first shipped that emoji (iOS is mapped to an
        // equivalent below). To use a new premium emoji anywhere, just add one row here.
        struct EmojiRule {
            public string Glyph; public int MinApi; public string Fallback;
            public EmojiRule(string glyph, int minApi, string fallback) { Glyph = glyph; MinApi = minApi; Fallback = fallback; }
        }

        static readonly EmojiRule[] _emojiRules = {
            new EmojiRule("🥹", 33, "😊"),   // Emoji 14 (2021) → Android 13 / iOS 15.4
            new EmojiRule("🧩", 28, "🎯"),   // Emoji 11 (2018) → Android 9
            new EmojiRule("🧠", 28, "💡"),
            new EmojiRule("♟️", 28, "✨"),
            new EmojiRule("♟",  28, "✨"),   // same glyph without the variation selector
            new EmojiRule("🥰", 28, "😍"),
        };

        static int _emojiLevel = -1;   // device emoji support, expressed as an Android-API level
        static int EmojiLevel => _emojiLevel >= 0 ? _emojiLevel : (_emojiLevel = ComputeEmojiLevel());

        static int ComputeEmojiLevel() {
#if UNITY_EDITOR
            return int.MaxValue;                       // editor: show the best emoji (for authoring/tests)
#elif UNITY_ANDROID
            try { using (var v = new AndroidJavaClass("android.os.Build$VERSION")) return v.GetStatic<int>("SDK_INT"); }
            catch { return 24; }
#elif UNITY_IOS
            int major = IosMajorVersion();             // map iOS → the Android API with the same emoji set
            if (major >= 18) return 34;
            if (major >= 16) return 33;
            if (major >= 15) return 31;
            if (major >= 14) return 29;
            return 28;
#else
            return int.MaxValue;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        static int IosMajorVersion() {
            string s = UnityEngine.iOS.Device.systemVersion;   // e.g. "16.4"
            int dot = s.IndexOf('.');
            string maj = dot > 0 ? s.Substring(0, dot) : s;
            return int.TryParse(maj, out int m) ? m : 0;
        }
#endif

        // Replace any emoji too new for this device with its safe fallback; leave the rest alone.
        static string Downgrade(string text) {
            if (string.IsNullOrEmpty(text)) return text;
            int level = EmojiLevel;
            for (int i = 0; i < _emojiRules.Length; i++) {
                var r = _emojiRules[i];
                if (level < r.MinApi && text.Contains(r.Glyph))
                    text = text.Replace(r.Glyph, r.Fallback);
            }
            return text;
        }

        // ---- logging -------------------------------------------------------------------

        // Dev logging follows the in-game debug panel's master flag, not a settings field.
        static void Log(string msg) {
            if (AppData.DebugMode.Value) Debug.Log("[Notifications] " + msg);
        }
    }
}

#endif
