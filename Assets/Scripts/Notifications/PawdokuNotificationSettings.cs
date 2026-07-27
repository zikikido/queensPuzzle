using System;
using System.Collections.Generic;
using UnityEngine;

namespace qp {

    /// <summary>
    /// All local-notification content and settings in one reskinnable asset. Lives in Resources
    /// as "PawdokuNotificationSettings" (per-reskin, next to DailyChallengeTiers) so Pawdoku,
    /// Meowdoku, etc. each ship their own copy. NotificationManager loads this once at boot and
    /// never touches Resources again (so background scheduling is instant).
    ///
    /// Daily Challenge messages are picked in order/rotation; General messages are picked by
    /// weighted category. Emoji placement is authored here verbatim and never injected at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "PawdokuNotificationSettings", menuName = "Pawdoku/Notification Settings", order = 4)]
    public class PawdokuNotificationSettings : ScriptableObject {

        [Serializable]
        public class Message {
            public string title;
            [TextArea] public string body;
        }

        [Serializable]
        public class Category {
            public string name;
            [Tooltip("Relative weight — the set need not total 100. Zero/negative categories are ignored.")]
            [Min(0f)] public float weight = 1f;
            public List<Message> messages = new List<Message>();
        }

        [Header("General")]
        public bool notificationsEnabled = true;
        [Tooltip("Days ahead to schedule. Clamped by NotificationManager so days*2 stays under the iOS 64 pending cap.")]
        [Min(1)] public int daysAhead = 30;
        // Dev logging is driven by the in-game debug panel (AppData.DebugMode), not from here.

        [Header("Android channel")]
        public string androidChannelId = "pawdoku_default";
        public string androidChannelName = "Pawdoku";
        public string androidChannelDescription = "Daily puzzles and gentle reminders.";
        [Tooltip("Small-icon id registered in Project Settings > Mobile Notifications (white silhouette).")]
        public string androidSmallIcon = "icon_small";
        [Tooltip("Large-icon id registered in Project Settings > Mobile Notifications; leave empty to skip.")]
        public string androidLargeIcon = "icon_large";
        public bool defaultSound = true;

        [Header("Daily Challenge time (local device time)")]
        [Range(0, 23)] public int dailyChallengeHour = 10;
        [Range(0, 59)] public int dailyChallengeMinute = 0;

        [Header("General time (local device time)")]
        [Range(0, 23)] public int generalHour = 18;
        [Range(0, 59)] public int generalMinute = 0;

        [Header("Debug — fire the whole schedule on a short interval so it can be verified on the next open")]
        public bool debugFastMode = false;
        [Min(1)] public int debugIntervalSeconds = 20;
        [Tooltip("Delay for the manual single test notification.")]
        [Min(1f)] public float testNotificationDelaySec = 5f;

        [Header("Fallback content (used when a list/selection is missing or invalid)")]
        public Message dailyChallengeFallback = new Message { title = "Daily Challenge", body = "Today's Daily Challenge is ready." };
        public Message generalFallback = new Message { title = "Pawdoku", body = "Your next puzzle is waiting." };

        [Header("Daily Challenge messages")]
        public List<Message> dailyChallengeMessages = new List<Message>();

        [Header("General categories (weighted)")]
        public List<Category> generalCategories = new List<Category>();
    }
}
