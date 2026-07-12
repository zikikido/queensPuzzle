using Common;
using Puzzby;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace qp {

    // The game's own haptic vocabulary. The Puzzby.Haptics package is game-agnostic; these effects
    // are defined here at boot and then triggered by id from gameplay.
    public enum GameHaptic { Tap, Happy, Win, Wrong, XMark }

    public static class MBStartup {

        // One boot task: Begin fires the (usually async) init, IsDone is polled every frame on
        // the main thread. A task that overruns its timeout is logged and counted as done —
        // the boot never hangs on a bad network.
        public class StartupTask {
            public string Name;
            public Action Begin;
            public Func<bool> IsDone;
            public float TimeoutSec;
            public bool Finished;
            public float StartedAt;
        }

        // Boot runs as an ordered list of STAGES. Stages run one after another (sequence); the
        // tasks inside one stage run together (parallel). Each Register() call adds one stage.
        static readonly List<StartupTask[]> _stages = new List<StartupTask[]>();

        // For the loading bar: progress = TasksDone / (float)TasksTotal, across all stages.
        public static int TasksTotal { get { int n = 0; foreach (var s in _stages) n += s.Length; return n; } }
        public static int TasksDone { get; private set; }
        public static bool Finished { get; private set; }

        /// <summary>Build one task (use inside Register() for a parallel stage).</summary>
        public static StartupTask Task(string name, Action begin, Func<bool> isDone, float timeoutSec = 10f)
            => new StartupTask { Name = name, Begin = begin, IsDone = isDone, TimeoutSec = timeoutSec };

        /// <summary>
        /// Add one boot STAGE. Stages run in sequence (this call after the previous Register);
        /// the tasks passed here run in parallel within the stage. Call before Run().
        /// </summary>
        public static void Register(params StartupTask[] tasks) {
            if (tasks != null && tasks.Length > 0) _stages.Add(tasks);
        }

        /// <summary>Convenience: a stage with a single task.</summary>
        public static void Register(string name, Action begin, Func<bool> isDone, float timeoutSec = 10f)
            => Register(Task(name, begin, isDone, timeoutSec));

        // Runs automatically when the app loads.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() {
            QualitySettings.vSyncCount = 0;      // so vSync doesn't override the target
            Application.targetFrameRate = 60;    // run at 60 FPS

            Haptics.Enabled = AppData.Haptics.Value;
            DefineHaptics();

            new GameObject("SFX").AddComponent<MBSFX>();   // registers itself as MBSFX.Instance

            // ---- boot stages (registered here so TasksTotal is known up front) ----
            // Stage 1: MAX alone — it owns the consent flow (UMP/ATT).
            Register("max", MaxBoot.Begin, () => MaxBoot.Done, timeoutSec: 30f);

            // Stage 2 (parallel): consent is resolved now — Firebase + Singular init together.
            Register(
#if !IGNORE_FIREBASE
                Task("firebase", FirebaseBootstrap.Init, () => FirebaseBootstrap.FBSetupFinished),
#endif
                Task("singular", SingularBoot.Begin, () => SingularBoot.Done)
            );

            // Stage 3: start loading ads. Instant-done — ad loading runs in the background, so
            // the loading screen never waits on it.
            Register("ads", Ads.Init, () => true);
        }

        /// <summary>
        /// The loading screen drives this: yield return MBStartup.Run(); — stages run in order,
        /// and the tasks within each stage run in parallel (all Begin, then polled on the main
        /// thread until done or timed out). Safe to yield again after completion (scene reload).
        /// </summary>
        public static IEnumerator Run() {
            if (Finished) yield break;

            foreach (var stage in _stages)
                yield return RunStage(stage);

            Finished = true;
        }

        // One stage: begin all its tasks together, then poll until every one is done or timed out.
        static IEnumerator RunStage(StartupTask[] stage) {
            foreach (var task in stage) {
                task.StartedAt = Time.realtimeSinceStartup;
                try { task.Begin?.Invoke(); }
                catch (Exception e) {
                    CDebug.LogError(e);
                    task.Finished = true;   // a task that can't start must not block the boot
                    TasksDone++;
                }
            }

            bool stageDone = false;

            while (!stageDone) {

                stageDone = true;

                foreach (var task in stage) {
                    if (task.Finished) continue;

                    bool done;
                    try { 
                        done = task.IsDone == null || task.IsDone(); 
                    }
                    catch (Exception e) { 
                        CDebug.LogError(e); done = true; 
                    }

                    if (!done && Time.realtimeSinceStartup - task.StartedAt >= task.TimeoutSec) {
                        Debug.LogWarning($"[MBStartup] '{task.Name}' timed out after {task.TimeoutSec:0}s — continuing without it.");
                        done = true;
                    }

                    if (done) {
                        task.Finished = true;
                        TasksDone++;
                        Debug.Log($"[MBStartup] '{task.Name}' done ({TasksDone}/{TasksTotal})");
                    } else {
                        stageDone = false;
                    }    
                }

                if (!stageDone) {
                    yield return null;
                }
                
            }
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
            Haptics.Define(GameHaptic.XMark, Haptics.New().Transient(1.0f, 0.7f));                  // fuller "thunk" — a notch above Tap
        }
    }
}
