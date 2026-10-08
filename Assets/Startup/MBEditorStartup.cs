#if UNITY_EDITOR
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Runs the startup stages when you press Play in a scene that is not Loading. EDITOR ONLY —
    /// a build always comes up through Loading, and this file does not exist there.
    ///
    /// <see cref="MBStartup.Boot"/> already runs from any scene: it has
    /// [RuntimeInitializeOnLoadMethod], and it REGISTERS every stage. What it does not do is run
    /// them — that is MBLoading, which lives in the Loading scene. So opening Lobby or Gameplay
    /// and hitting Play leaves all of it registered and none of it executed: no profile, no
    /// tournament, no name.
    ///
    /// This drives the same stages, in the same order, without sending you anywhere: you stay in
    /// the scene you opened, behind a cover, until the boot is done — the same deal the loading
    /// screen makes. Without it the scene is already live while its data is still arriving, and
    /// you spend the first second watching things appear and correct themselves.
    ///
    /// Running twice is refused by <see cref="MBStartup.Run"/> itself, which is where that
    /// belongs: the danger is two drivers, not two of this.
    /// </summary>
    public sealed class MBEditorStartup : MonoBehaviour {

        /// <summary>The scene whose own loading screen runs the stages.</summary>
        const string BootScene = "Loading";

        /// <summary>Over everything, including anything the scene puts up itself.</summary>
        const int CoverSortingOrder = short.MaxValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void _run() {
            // AfterSceneLoad: the active scene is the one actually opened, and Boot has already
            // registered everything there is to run.
            if (SceneManager.GetActiveScene().name == BootScene) return;   // MBLoading drives it
            if (MBStartup.Finished || MBStartup.Running) return;           // somebody already is

            var go = new GameObject("EditorStartup");
            DontDestroyOnLoad(go);
            go.AddComponent<MBEditorStartup>().StartCoroutine(_boot());
        }

        /// <summary>Hold the screen, run the stages to completion, then let go.</summary>
        static IEnumerator _boot() {
            Debug.Log($"[EditorStartup] not in {BootScene} — running {MBStartup.TasksTotal} startup tasks here");
            float began = Time.realtimeSinceStartup;

            var cover = _cover();
            yield return MBStartup.Run();
            Destroy(cover);

            Debug.Log($"[EditorStartup] startup finished in {Time.realtimeSinceStartup - began:0.00}s " +
                      $"({MBStartup.TasksDone}/{MBStartup.TasksTotal} tasks)");
        }

        /// <summary>A plain opaque canvas over the whole screen. Its Image is a raycast target
        /// and the canvas has a raycaster, so it swallows taps too — a button pressed against
        /// half-booted state is worse than a button that does not answer yet.</summary>
        static GameObject _cover() {
            var go = new GameObject("EditorStartupCover");
            DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CoverSortingOrder;
            go.AddComponent<GraphicRaycaster>();

            var image = new GameObject("Fill", typeof(Image));
            image.transform.SetParent(go.transform, false);
            var rect = image.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            image.GetComponent<Image>().color = Color.black;

            Debug.Log($"[EditorStartup] cover up (sorting {canvas.sortingOrder}, " +
                      $"{((RectTransform)go.transform).rect.size})");
            return go;
        }
    }
}
#endif
