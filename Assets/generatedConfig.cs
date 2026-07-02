using System.Collections.Generic;

namespace Common
{
    /// <summary>
    /// Per-app config consumed by the vendored kido <c>Common</c> library. Trimmed to only the
    /// params <c>Common</c> actually references — the subscription / sharing / Firebase-app-id params
    /// in hairSalon's full version are used by Kido code that isn't vendored here. Add them back (and
    /// fill the placeholders) when you wire up subscriptions / analytics for queensPuzzle.
    /// </summary>
    public static class generatedConfig
    {
        // Shared kido signing keystore.
        public static string keyaliasName = "puzzby";
        public static string keyaliasPass = "puzzby!";

        // Singular attribution — off until configured.
        public static bool HasSingualarForAndroid = false;
        public static bool HasSingualarForIos = false;

        // queensPuzzle Firebase app id (encoded) — fill when the Firebase project is set up.
        public static string FirebaseEncodeAppID = "";

        // Firebase Remote Config default values.
        public static Dictionary<string, object> FBRCDefaults = null;

#if UNITY_IOS
        // queensPuzzle App Store id — fill when the app is registered.
        public static string AppleStoreId = "";
#endif
    }
}
