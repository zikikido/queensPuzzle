using Common;
using Puzzby;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace qp {

    // The game's own haptic vocabulary. The Puzzby.Haptics package is game-agnostic; these effects
    // are defined here at boot and then triggered by id from gameplay.
    public enum GameHaptic { Tap, Happy, Win, Wrong }

    public static class MBStartup {

        // One boot task: Begin fires the (usually async) init, IsDone is polled every frame on
        // the main thread. A task that overruns its timeout is logged and counted as done —
        // the boot never hangs on a bad network.
        class StartupTask {
            public string Name;
            public Action Begin;
            public Func<bool> IsDone;
            public float TimeoutSec;
            public bool Finished;
            public float StartedAt;
        }

        static readonly List<StartupTask> _tasks = new List<StartupTask>();

        // For the loading bar: progress = TasksDone / (float)TasksTotal.
        public static int TasksTotal => _tasks.Count;
        public static int TasksDone { get; private set; }
        public static bool Finished { get; private set; }

        /// <summary>Add a boot task. Call before Run() — normally from Boot() below.</summary>
        public static void Register(string name, Action begin, Func<bool> isDone, float timeoutSec = 10f) {
            _tasks.Add(new StartupTask { Name = name, Begin = begin, IsDone = isDone, TimeoutSec = timeoutSec });
        }

        // Runs automatically when the app loads.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() {
            QualitySettings.vSyncCount = 0;      // so vSync doesn't override the target
            Application.targetFrameRate = 60;    // run at 60 FPS

            Haptics.Enabled = AppData.Haptics.Value;
            DefineHaptics();

            // ---- the boot task list (registered here so TasksTotal is known up front) ----
#if !IGNORE_FIREBASE
            Register("firebase", FirebaseBootstrap.Init, () => FirebaseBootstrap.FBSetupFinished);
#endif
            Register("singular", SingularBoot.Begin, () => SingularBoot.Done);
        }

        /// <summary>
        /// The loading screen drives this: yield return MBStartup.Run(); — all tasks Begin at
        /// once (parallel), then each is polled on the main thread until done or timed out.
        /// Safe to yield again after completion (scene reload) — returns immediately.
        /// </summary>
        public static IEnumerator Run() {
            if (Finished) yield break;

            foreach (var task in _tasks) {
                task.StartedAt = Time.realtimeSinceStartup;
                try { task.Begin?.Invoke(); }
                catch (Exception e) {
                    CDebug.LogError(e);
                    task.Finished = true;   // a task that can't start must not block the boot
                    TasksDone++;
                }
            }

            while (TasksDone < _tasks.Count) {
                foreach (var task in _tasks) {
                    if (task.Finished) continue;

                    bool done;
                    try { done = task.IsDone == null || task.IsDone(); }
                    catch (Exception e) { CDebug.LogError(e); done = true; }

                    if (!done && Time.realtimeSinceStartup - task.StartedAt >= task.TimeoutSec) {
                        Debug.LogWarning($"[MBStartup] '{task.Name}' timed out after {task.TimeoutSec:0}s — continuing without it.");
                        done = true;
                    }

                    if (done) {
                        task.Finished = true;
                        TasksDone++;
                        Debug.Log($"[MBStartup] '{task.Name}' done ({TasksDone}/{_tasks.Count})");
                    }
                }
                yield return null;
            }

            Finished = true;
        }

        // intensity = how strong (0..1), sharpness = dull/bassy (0) .. crisp/tick (1).
        static void DefineHaptics() {
            Haptics.Define(GameHaptic.Tap,   Haptics.New().Transient(0.85f, 0.8f));                // strong crisp "blop"
            Haptics.Define(GameHaptic.Happy, Haptics.New().Transient(0.5f, 0.9f)
                                                          .Transient(0.9f, 0.9f, 0.08f));           // bouncy double
            Haptics.Define(GameHaptic.Win,   Haptics.New().Transient(0.5f, 0.8f)
                                                          .Transient(1.0f, 0.9f, 0.06f)
                                                          .Transient(0.6f, 0.7f, 0.12f));           // rising celebration
            Haptics.Define(GameHaptic.Wrong, Haptics.New().Transient(1.0f, 0.4f)
                                                          .Transient(0.7f, 0.3f, 0.08f));           // heavy double thud
        }
    }
}
