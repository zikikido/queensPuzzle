using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Puzzby
{
    /// <summary>
    /// A haptic effect: one or more events, each described by <b>intensity</b> (how strong, 0..1)
    /// and <b>sharpness</b> (dull/bassy 0 .. crisp/tick 1), placed on a timeline with a relative
    /// <b>delay</b> (seconds). Build with <see cref="Haptics.New"/> and chain.
    /// </summary>
    public sealed class HapticPattern
    {
        internal struct Event { public float intensity, sharpness, delay, duration; }
        internal readonly List<Event> events = new List<Event>(4);

        /// <summary>A single tap.</summary>
        public HapticPattern Transient(float intensity, float sharpness, float delay = 0f)
        {
            events.Add(new Event {
                intensity = Clamp01(intensity), sharpness = Clamp01(sharpness),
                delay = Mathf.Max(0f, delay), duration = 0f
            });
            return this;
        }

        /// <summary>A sustained buzz for <paramref name="duration"/> seconds.</summary>
        public HapticPattern Continuous(float intensity, float sharpness, float duration, float delay = 0f)
        {
            events.Add(new Event {
                intensity = Clamp01(intensity), sharpness = Clamp01(sharpness),
                delay = Mathf.Max(0f, delay), duration = Mathf.Max(0f, duration)
            });
            return this;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    /// <summary>
    /// Cross-platform haptics engine. Effects are described portably as intensity + sharpness and
    /// map natively to iOS Core Haptics (Taptic Engine) and to Android VibrationEffect
    /// (primitives → waveform → legacy). The engine is game-agnostic: register effects by id with
    /// <see cref="Define(int,HapticPattern)"/> (or an enum), then trigger them with
    /// <see cref="Play(int)"/>. No-op in the editor, when disabled, or on devices without haptics.
    /// </summary>
    public static class Haptics
    {
        public static bool Enabled = true;

        static readonly Dictionary<int, HapticPattern> _catalog = new Dictionary<int, HapticPattern>();

        /// <summary>Start building a new effect.</summary>
        public static HapticPattern New() => new HapticPattern();

        /// <summary>Register (or replace) an effect under an integer id.</summary>
        public static void Define(int id, HapticPattern pattern)
        {
            if (pattern != null) _catalog[id] = pattern;
        }

        /// <summary>Register (or replace) an effect under an enum id.</summary>
        public static void Define<T>(T id, HapticPattern pattern) where T : Enum
            => Define(Convert.ToInt32(id), pattern);

        /// <summary>Play a registered effect by id. Unknown ids are ignored.</summary>
        public static void Play(int id)
        {
            if (_catalog.TryGetValue(id, out var p)) Play(p);
        }

        /// <summary>Play a registered effect by enum id.</summary>
        public static void Play<T>(T id) where T : Enum => Play(Convert.ToInt32(id));

        /// <summary>Play an ad-hoc effect without registering it.</summary>
        public static void Play(HapticPattern pattern)
        {
            if (!Enabled || pattern == null || pattern.events.Count == 0) return;
#if UNITY_IOS && !UNITY_EDITOR
            IOSPlay(pattern);
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidPlay(pattern);
#endif
        }

        /// <summary>
        /// Warm the haptic engine so the next effect fires without spin-up latency (the first buzz
        /// after idle is otherwise ~10-40 ms late). Call a moment before you expect to play — e.g.
        /// when a level becomes interactive, or on finger-down. Cheap; safe to call repeatedly.
        /// </summary>
        public static void Prepare()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try { _hapticPrepare(); } catch { }
#elif UNITY_ANDROID && !UNITY_EDITOR
            EnsureVibrator();   // resolve JNI handles now so the first Play isn't slowed by lookups
#endif
        }

        /// <summary>True when the device actually has a working haptic engine.</summary>
        public static bool Supported
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return _hapticSupported() != 0;
#elif UNITY_ANDROID && !UNITY_EDITOR
                EnsureVibrator();
                return _vibrator != null;
#else
                return false;
#endif
            }
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void _hapticPlay(float[] intensity, float[] sharpness, float[] delay, float[] duration, int count);
        [DllImport("__Internal")]
        static extern int _hapticSupported();
        [DllImport("__Internal")]
        static extern void _hapticPrepare();

        static void IOSPlay(HapticPattern p)
        {
            int n = p.events.Count;
            var inten = new float[n]; var sharp = new float[n];
            var delay = new float[n]; var dur = new float[n];
            for (int i = 0; i < n; i++)
            {
                var e = p.events[i];
                inten[i] = e.intensity; sharp[i] = e.sharpness; delay[i] = e.delay; dur[i] = e.duration;
            }
            try { _hapticPlay(inten, sharp, delay, dur, n); } catch { }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _vibrator;
        static int _sdk;       // Build.VERSION.SDK_INT
        static bool _tried;
        static bool _hasAmp;   // motor can vary strength (LRA); false on old on/off ERM motors

        // VibrationEffect.Composition primitive ids
        const int PRIMITIVE_CLICK = 1, PRIMITIVE_THUD = 2, PRIMITIVE_TICK = 6;

        static void AndroidPlay(HapticPattern p)
        {
            EnsureVibrator();
            if (_vibrator == null) return;
            try
            {
                bool allTransient = true;
                foreach (var e in p.events) if (e.duration > 0f) { allTransient = false; break; }

                if (_sdk >= 30 && allTransient && TryComposition(p)) return;   // best: tuned primitives
                if (_sdk >= 26 && TryWaveform(p)) return;                       // amplitude-controlled pattern
                LegacyPlay(p);                                                  // on/off pattern
            }
            catch { /* missing permission / unsupported device — never break gameplay */ }
        }

        // Map an event to the closest tuned primitive: dull -> thud, crisp+light -> tick, else click.
        static int PrimitiveFor(HapticPattern.Event e)
        {
            if (e.sharpness < 0.4f) return _sdk >= 31 ? PRIMITIVE_THUD : PRIMITIVE_CLICK;
            if (e.sharpness >= 0.7f && e.intensity < 0.5f) return PRIMITIVE_TICK;
            return PRIMITIVE_CLICK;
        }

        static bool TryComposition(HapticPattern p)
        {
            int n = p.events.Count;
            var prims = new int[n];
            for (int i = 0; i < n; i++) prims[i] = PrimitiveFor(p.events[i]);

            try { if (!_vibrator.Call<bool>("areAllPrimitivesSupported", prims)) return false; }
            catch { return false; }

            using (var ve = new AndroidJavaClass("android.os.VibrationEffect"))
            using (var comp = ve.CallStatic<AndroidJavaObject>("startComposition"))
            {
                for (int i = 0; i < n; i++)
                {
                    var e = p.events[i];
                    int delayMs = Mathf.RoundToInt(e.delay * 1000f);
                    float scale = e.intensity <= 0f ? 0.05f : e.intensity;   // scale must be > 0
                    using (comp.Call<AndroidJavaObject>("addPrimitive", prims[i], scale, delayMs)) { }
                }
                using (var effect = comp.Call<AndroidJavaObject>("compose"))
                    _vibrator.Call("vibrate", effect);
            }
            return true;
        }

        // Pulse length for a transient. With amplitude control, strength is carried by amplitude so
        // we keep pulses short/crisp (sharp = shorter). Without it (ERM motors), amplitude is ignored,
        // so we encode strength as length instead — otherwise every tap feels the same full-blast pulse.
        static int PulseMs(HapticPattern.Event e)
        {
            if (e.duration > 0f) return Mathf.RoundToInt(e.duration * 1000f);
            if (_hasAmp)         return e.sharpness >= 0.6f ? 12 : 28;
            // No amplitude control (old ERM motor): pulse length is the only knob. Short reads as a
            // crisp "blop/tick"; long reads as a buzzy "bass" buzz. So sharpness shortens it, and
            // intensity only lengthens it a little — a sharp tap stays snappy even when strong.
            return Mathf.Max(18, Mathf.RoundToInt(Mathf.Lerp(32f, 16f, e.sharpness) + Mathf.Lerp(0f, 10f, e.intensity)));
        }

        static bool TryWaveform(HapticPattern p)
        {
            var timings = new List<long>();
            var amps = new List<int>();
            foreach (var e in p.events)
            {
                int gap = Mathf.RoundToInt(e.delay * 1000f);
                if (gap > 0) { timings.Add(gap); amps.Add(0); }                       // silent gap
                timings.Add(PulseMs(e));
                amps.Add(_hasAmp ? Mathf.Clamp(Mathf.RoundToInt(e.intensity * 255f), 1, 255) : 255);
            }
            try
            {
                using (var ve = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var effect = ve.CallStatic<AndroidJavaObject>("createWaveform", timings.ToArray(), amps.ToArray(), -1))
                    _vibrator.Call("vibrate", effect);
                return true;
            }
            catch { return false; }
        }

        static void LegacyPlay(HapticPattern p)
        {
            // vibrate(long[]): [wait, on, wait, on, ...] — amplitude is ignored, so strength is length.
            var pattern = new List<long>();
            foreach (var e in p.events)
            {
                pattern.Add(Mathf.RoundToInt(e.delay * 1000f));   // wait
                pattern.Add(PulseMs(e));                          // on
            }
            try { _vibrator.Call("vibrate", pattern.ToArray(), -1); } catch { }
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

                if (_vibrator != null && _sdk >= 26)
                    try { _hasAmp = _vibrator.Call<bool>("hasAmplitudeControl"); } catch { _hasAmp = false; }
            }
            catch { _vibrator = null; }
        }
#endif
    }
}
