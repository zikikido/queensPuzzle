using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Puzzby
{
    public enum HapticType { Selection = 0, Light = 1, Medium = 2, Heavy = 3, Success = 4, Warning = 5, Failure = 6 }

    /// <summary>
    /// Cross-platform haptics. iOS uses UIFeedbackGenerator (Taptic Engine); Android uses
    /// VibrationEffect (API 26+) with a duration/amplitude per type. No-op in the editor and on
    /// devices without a vibrator. Toggle with <see cref="Enabled"/>.
    /// </summary>
    public static class Haptics
    {
        public static bool Enabled = true;

        public static void Play(HapticType type)
        {
            if (!Enabled) return;
#if UNITY_IOS && !UNITY_EDITOR
            _hapticImpact((int)type);
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidPlay(type);
#endif
        }

        public static void Selection() => Play(HapticType.Selection);
        public static void Light()     => Play(HapticType.Light);
        public static void Medium()    => Play(HapticType.Medium);
        public static void Heavy()     => Play(HapticType.Heavy);
        public static void Success()   => Play(HapticType.Success);
        public static void Warning()   => Play(HapticType.Warning);
        public static void Failure()   => Play(HapticType.Failure);

        // gameplay-intent shortcuts
        public static void Wrong() => Play(HapticType.Failure);
        public static void Win()   => Play(HapticType.Success);

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void _hapticImpact(int type);
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _vibrator;
        static int _sdk;       // 0 = not resolved, -1 = unavailable
        static bool _tried;

        static void AndroidPlay(HapticType type)
        {
            EnsureVibrator();
            if (_vibrator == null) return;

            long ms; int amp;
            switch (type)
            {
                case HapticType.Selection: ms = 20; amp = 70;  break;
                case HapticType.Light:     ms = 30; amp = 110; break;
                case HapticType.Medium:    ms = 45; amp = 180; break;
                case HapticType.Heavy:     ms = 70; amp = 255; break;
                case HapticType.Success:   ms = 50; amp = 180; break;
                case HapticType.Warning:   ms = 60; amp = 220; break;
                case HapticType.Failure:   ms = 85; amp = 255; break;
                default:                   ms = 40; amp = 160; break;
            }

            try
            {
                if (_sdk >= 26)
                {
                    using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amp))
                        _vibrator.Call("vibrate", effect);
                }
                else
                {
                    // old ERM motors (no amplitude control) need a longer pulse to be felt
                    _vibrator.Call("vibrate", System.Math.Max(ms, 35));
                }
            }
            catch { /* missing VIBRATE permission or unsupported device — never break gameplay */ }
        }

        static void EnsureVibrator()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    _sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
            catch { _vibrator = null; }
        }
#endif
    }
}
