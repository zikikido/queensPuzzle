using UnityEngine.SceneManagement;

namespace qp {

    /// <summary>
    /// The one way to switch scenes: every navigation reports ScreenClose for the scene the
    /// player leaves and ScreenOpen for the one they enter, so the screen flow is always
    /// visible in analytics and in crash breadcrumbs.
    /// </summary>
    public static class Navigator {

        public const string Lobby = "Lobby";
        public const string Gameplay = "Gameplay";

        /// <summary>The scene the player is in right now.</summary>
        public static string Current => SceneManager.GetActiveScene().name;

        public static void Go(string scene) {
            Analytics.ScreenClose(Current);

            SceneManager.LoadScene(scene);

            Analytics.ScreenOpen(scene);
        }
    }
}
