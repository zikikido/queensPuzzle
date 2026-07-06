using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Common {
    /// <summary>
    /// Everything Singular in ONE place, with ONE menu item (Common > Setup Singular).
    ///
    /// Setup (menu, approve/deny showing the REAL project state):
    ///   1) Packages/manifest.json — the SDK git package (kids/regular, pinned to a tag).
    ///   2) proguard-user.txt — the Singular -keep rules.
    ///
    /// Automatic at build time (no extra menus):
    ///   - iOS: `pod repo update` + `pod update` after every iOS build (doc step).
    ///   - iOS: UnityAppController-conflict detection; when found, asks to apply Singular's
    ///     swizzle fix (optional in the doc, so it always asks — never silent).
    /// </summary>
    public static class SingularSetup {

        // ---- package -------------------------------------------------------------------
        const string PackageName = "singular-unity-package";

        // Pinned to release tags so UPM never auto-updates on a re-resolve; bump them here
        // deliberately. (Kids releases lag behind regular — different versions are expected.)
        const string RegularVersion = "5.8.0";
        const string KidsVersion = "5.6.0-kids";

        const string GitUrl = "https://github.com/singular-labs/Singular-Unity-SDK.git";
        const string UrlRegular = GitUrl + "#" + RegularVersion;
        const string UrlKids = GitUrl + "#" + KidsVersion;

        const string ManifestPath = "Packages/manifest.json";

        // ---- proguard ------------------------------------------------------------------
        const string ProguardPath = "Assets/Plugins/Android/proguard-user.txt";
        static readonly string[] ProguardRules = {
            "-keep class com.singular.sdk.** { *; }",
            "-keep public class com.android.installreferrer.** { *; }",
            "-keep public class com.singular.unitybridge.** { *; }",
        };

        // ---- SDK object (the SingularSDKObject prefab in the boot scene) -----------------
        // Deliberate settings: InitializeOnAwake = OFF because init must run AFTER the consent
        // flow (GDPR) and the ATT prompt on iOS — the bootstrap calls InitializeSingularSDK().
        // Everything else stays at the prefab defaults on purpose: SKANEnabled=true (iOS
        // attribution), waitForTrackingAuthorization=0 (we sequence ATT ourselves), ODM off,
        // clipboardAttribution/collectOAID/limitAdvertisingIdentifiers=false, timeouts default.
        // The API key/secret can't be automated — the fix selects the object for pasting.
        const string PrefabPath = "Packages/" + PackageName + "/SingularSDK/Prefabs/SingularSDKObject.prefab";
        const string KeyPlaceholder = "<YourAPIKey>";
        const string SecretPlaceholder = "<YourAPISecret>";

        // ================== the one menu item ==================
        // One window: the STATUS is the message, the ACTIONS are the buttons.

        [MenuItem("Common/Setup Singular")]
        public static void Run() {
            string current = InstalledUrl();               // null = not installed
            string[] missing = MissingProguardRules();

            string pkgLine =
                current == null ? "NOT INSTALLED" :
                current == UrlRegular ? $"Regular {RegularVersion}" :
                current == UrlKids ? $"Kids {KidsVersion}" :
                $"custom url:\n      \"{current}\"";
            string pgLine = missing.Length == 0 ? "all rules present"
                : $"{missing.Length} rule(s) MISSING:\n" + string.Join("\n", missing.Select(r => "      " + r));

            string status =
                $"Package:   {pkgLine}\n" +
                $"Proguard:  {pgLine}\n\n" +
                "iOS pod update + AppController-conflict check run automatically on every iOS build.";

            // not installed → the two install actions
            if (current == null) {
                switch (EditorUtility.DisplayDialogComplex("Singular Setup", status,
                        $"Install Regular {RegularVersion}", "Close", $"Install Kids {KidsVersion}")) {
                    case 0: Install(UrlRegular); break;
                    case 2: Install(UrlKids); break;
                }
                return;
            }

            // custom/unpinned url → offer both pinned variants
            if (current != UrlRegular && current != UrlKids) {
                switch (EditorUtility.DisplayDialogComplex("Singular Setup", status,
                        $"Replace → Regular {RegularVersion}", "Close", $"Replace → Kids {KidsVersion}")) {
                    case 0: Install(UrlRegular); break;
                    case 2: Install(UrlKids); break;
                }
                return;
            }

            // installed → SDK-object state joins the status; one Fix button covers everything
            string objLine = SdkObjectStatus(out bool objNeedsWork, out bool keysMissing);
            status =
                $"Package:   {pkgLine}\n" +
                $"Proguard:  {pgLine}\n" +
                $"SDK object: {objLine}\n\n" +
                "iOS pod update + AppController-conflict check run automatically on every iOS build.";

            string otherLabel = current == UrlRegular ? $"Replace → Kids {KidsVersion}" : $"Replace → Regular {RegularVersion}";
            string otherUrl = current == UrlRegular ? UrlKids : UrlRegular;

            if (missing.Length > 0 || objNeedsWork) {
                var fixes = new List<string>();
                if (missing.Length > 0) fixes.Add("proguard");
                if (objNeedsWork) fixes.Add("SDK object");
                switch (EditorUtility.DisplayDialogComplex("Singular Setup", status,
                        $"Fix ({string.Join(" + ", fixes)})", "Close", otherLabel)) {
                    case 0:
                        if (missing.Length > 0) EnsureProguardRules();
                        if (objNeedsWork) FixSdkObject(keysMissing);
                        break;
                    case 2: Install(otherUrl); break;
                }
                return;
            }

            if (EditorUtility.DisplayDialog("Singular Setup", status, otherLabel, "Close"))
                Install(otherUrl);
        }

        // Install/replace always ensures the proguard rules too — it's part of the setup.
        static void Install(string url) {
            AddPackage(url);
            EnsureProguardRules();
            Done(resolve: true);
        }

        static void Done(bool resolve) {
            EditorUtility.DisplayDialog("Singular Setup",
                (resolve ? "Done — resolving the package now.\n\n" : "Done.\n\n") +
                "Reminder: the proguard rules only apply when 'Custom Proguard File' is enabled " +
                "in Player > Publishing Settings.", "OK");
            if (resolve) UnityEditor.PackageManager.Client.Resolve();
        }

        // ================== state inspection ==================

        static string InstalledUrl() {
            string json = File.ReadAllText(Path.GetFullPath(ManifestPath));
            var m = Regex.Match(json, $"\"{PackageName}\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        static string[] MissingProguardRules() {
            string path = Path.GetFullPath(ProguardPath);
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            return ProguardRules.Where(r => !text.Contains(r)).ToArray();
        }

        // ---- the SingularSDKObject in the open scene --------------------------------------

        // By name so Common still compiles in projects without the Singular package.
        static System.Type SingularType() =>
            System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Singular.SingularSDK")).FirstOrDefault(t => t != null);

        static Component FindSdkInstance() {
            var type = SingularType();
            if (type == null) return null;
            var objs = Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None);
            return objs.Length > 0 ? (Component)objs[0] : null;
        }

        static string SdkObjectStatus(out bool needsWork, out bool keysMissing) {
            needsWork = false;
            keysMissing = false;

            if (SingularType() == null)
                return "package not compiled yet — close and reopen this setup after the resolve";

            var instance = FindSdkInstance();
            if (instance == null) {
                needsWork = true;
                return "NOT IN THE OPEN SCENE(S) — open your boot scene and press Fix to add it";
            }

            var so = new SerializedObject(instance);
            bool onAwake = so.FindProperty("InitializeOnAwake").boolValue;
            string key = so.FindProperty("SingularAPIKey").stringValue;
            string secret = so.FindProperty("SingularAPISecret").stringValue;
            keysMissing = string.IsNullOrEmpty(key) || key == KeyPlaceholder ||
                          string.IsNullOrEmpty(secret) || secret == SecretPlaceholder;

            var notes = new List<string> { $"in scene '{instance.gameObject.scene.name}'" };
            if (onAwake) { needsWork = true; notes.Add("InitializeOnAwake ON — must be OFF (init runs after consent/ATT)"); }
            else notes.Add("manual init (bootstrap calls InitializeSingularSDK after consent)");
            if (keysMissing) { needsWork = true; notes.Add("API KEY/SECRET NOT SET — Fix selects the object for pasting"); }
            else notes.Add("API keys set");
            return string.Join("\n      ", notes);
        }

        // Add the prefab to the open scene if missing, force InitializeOnAwake off, and hand
        // the object to the user (selected + pinged) when the API keys still need pasting.
        static void FixSdkObject(bool keysMissing) {
            var type = SingularType();
            if (type == null) return;

            var instance = FindSdkInstance();
            GameObject go;
            if (instance == null) {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (prefab == null) {
                    EditorUtility.DisplayDialog("Singular Setup", "SingularSDKObject prefab not found at:\n" + PrefabPath, "OK");
                    return;
                }
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Debug.Log($"[SingularSetup] SingularSDKObject added to scene '{go.scene.name}'.");
            } else {
                go = instance.gameObject;
            }

            var so = new SerializedObject(go.GetComponent(type));
            so.FindProperty("InitializeOnAwake").boolValue = false;   // init belongs to the consent flow
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(go.scene);

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            EditorUtility.DisplayDialog("Singular Setup",
                $"SingularSDKObject ready in scene '{go.scene.name}' (InitializeOnAwake OFF — save the scene)." +
                (keysMissing
                    ? "\n\nNow paste your SDK Key + Secret in the Inspector (selected for you):\n" +
                      "Singular dashboard > Developer Tools > SDK Integration > SDK Keys.\n" +
                      "Do NOT use the Reporting API key."
                    : ""), "OK");
        }

        // ================== the actual changes (only after approval) ==================

        // Insert (or update) the package entry in manifest.json, preserving the file layout.
        static void AddPackage(string url) {
            string path = Path.GetFullPath(ManifestPath);
            string json = File.ReadAllText(path);

            if (json.Contains($"\"{PackageName}\"")) {
                json = Regex.Replace(json, $"\"{PackageName}\"\\s*:\\s*\"[^\"]*\"", $"\"{PackageName}\": \"{url}\"");
            } else {
                int i = json.IndexOf('{', json.IndexOf("\"dependencies\""));
                json = json.Insert(i + 1, $"\n    \"{PackageName}\": \"{url}\",");
            }

            File.WriteAllText(path, json);
            Debug.Log($"[SingularSetup] manifest.json: \"{PackageName}\": \"{url}\"");
        }

        // Append only the rules that aren't in the file yet; create the file if needed.
        static void EnsureProguardRules() {
            string path = Path.GetFullPath(ProguardPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string text = File.Exists(path) ? File.ReadAllText(path) : "";

            int added = 0;
            foreach (var rule in ProguardRules) {
                if (text.Contains(rule)) continue;
                if (text.Length > 0 && !text.EndsWith("\n")) text += "\n";
                text += rule + "\n";
                added++;
            }

            if (added > 0) {
                File.WriteAllText(path, text);
                AssetDatabase.ImportAsset(ProguardPath);
            }
            Debug.Log($"[SingularSetup] {ProguardPath}: {added} rule(s) added.");
        }

        // ================== iOS: post-build steps (pods + conflict check) ==================

        [PostProcessBuild(10000)]   // high order = after the EDM iOS Resolver wrote the Podfile
        public static void OnPostProcessBuildIOS(BuildTarget target, string buildPath) {
            if (target != BuildTarget.iOS) return;
            UpdatePods(buildPath);
            CheckAppControllerConflict(buildPath);
        }

        // Doc step "Update CocoaPods Dependencies": pod repo update + pod update. macOS only.
        static void UpdatePods(string buildPath) {
            if (Application.platform != RuntimePlatform.OSXEditor) {
                Debug.LogWarning("[SingularSetup] Not on macOS — run 'pod repo update' and 'pod update' " +
                                 $"manually in '{buildPath}' before building in Xcode.");
                return;
            }
            if (!File.Exists(Path.Combine(buildPath, "Podfile"))) {
                Debug.LogWarning($"[SingularSetup] No Podfile in '{buildPath}' — nothing to update " +
                                 "(is the EDM iOS Resolver enabled?).");
                return;
            }
            if (RunPod("repo update", buildPath))   // no point updating off a broken repo
                RunPod("update", buildPath);
        }

        // Through a login shell so `pod` is found no matter how it was installed (gem/homebrew).
        static bool RunPod(string args, string workDir) {
            Debug.Log($"[SingularSetup] pod {args} … (in {workDir})");
            var psi = new System.Diagnostics.ProcessStartInfo {
                FileName = "/bin/zsh",
                Arguments = $"-lc \"pod {args}\"",
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var p = System.Diagnostics.Process.Start(psi)) {
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode != 0) {
                    Debug.LogError($"[SingularSetup] pod {args} FAILED (exit {p.ExitCode})\n{output}\n{error}");
                    return false;
                }
                Debug.Log($"[SingularSetup] pod {args} done.\n{output}");
                return true;
            }
        }

        // Doc "Resolving UnityAppController Conflicts" (optional there, so we always ASK):
        // more than one uncommented IMPL_APP_CONTROLLER_SUBCLASS registration in the export
        // means two SDKs fight over the controller and only one wins at runtime.
        static void CheckAppControllerConflict(string buildPath) {
            var registrations = FindAppControllerRegistrations(buildPath);
            if (registrations.Count <= 1) {
                Debug.Log($"[SingularSetup] No UnityAppController conflict ({registrations.Count} registration(s)).");
                return;
            }

            string who = string.Join("\n  ", registrations);
            if (Application.isBatchMode) {
                Debug.LogWarning("[SingularSetup] CONFLICT — several SDKs register the UnityAppController:\n  " + who +
                                 "\nRun an editor build to be offered the swizzle fix, or apply it manually (Singular docs).");
                return;
            }

            bool approved = EditorUtility.DisplayDialog("Setup Singular — AppController conflict",
                "Several SDKs register the UnityAppController (only one wins at runtime):\n\n  " + who +
                "\n\nTry to fix it? This uncomments Singular's swizzle block in " +
                "SingularSwizzledAppController.m (the fix from the Singular docs).", "Approve", "Deny");
            if (!approved) return;

            string swizzled = Directory.GetFiles(buildPath, "SingularSwizzledAppController.m", SearchOption.AllDirectories).FirstOrDefault();
            if (swizzled == null) {
                EditorUtility.DisplayDialog("Setup Singular", "SingularSwizzledAppController.m not found in the export — fix manually.", "OK");
                return;
            }

            switch (UncommentSwizzleBlock(swizzled)) {
                case FixResult.Applied: Debug.Log($"[SingularSetup] Swizzle block uncommented in {swizzled}"); break;
                case FixResult.AlreadyApplied: Debug.Log("[SingularSetup] Swizzle block already uncommented — nothing to do."); break;
                case FixResult.MarkerNotFound:
                    EditorUtility.DisplayDialog("Setup Singular", "Couldn't find the commented swizzle block — SDK layout changed? Fix manually.", "OK");
                    return;
            }

            // doc step 2: IMPL_APP_CONTROLLER_SUBCLASS(SingularAppDelegate) must stay active
            string delegateFile = Directory.GetFiles(buildPath, "SingularAppDelegate.m", SearchOption.AllDirectories).FirstOrDefault();
            bool implActive = delegateFile != null &&
                FindAppControllerRegistrations(Path.GetDirectoryName(delegateFile)).Any(r => r.Contains("SingularAppDelegate"));
            if (implActive) Debug.Log("[SingularSetup] Validated: IMPL_APP_CONTROLLER_SUBCLASS(SingularAppDelegate) is active.");
            else Debug.LogWarning("[SingularSetup] IMPL_APP_CONTROLLER_SUBCLASS(SingularAppDelegate) appears commented out — " +
                                  "the doc expects it active. Check SingularAppDelegate.m.");
        }

        enum FixResult { Applied, AlreadyApplied, MarkerNotFound }

        // The block ships as:  /// Uncomment the code below ...\n /* ... */  — drop the two
        // comment-fence lines, keep everything between them.
        static FixResult UncommentSwizzleBlock(string path) {
            var lines = File.ReadAllLines(path).ToList();
            int marker = lines.FindIndex(l => l.Contains("Uncomment the code below"));
            if (marker < 0) return FixResult.MarkerNotFound;

            int open = lines.FindIndex(marker, l => l.Trim() == "/*");
            if (open < 0) return FixResult.AlreadyApplied;
            int close = lines.FindIndex(open, l => l.Trim() == "*/");
            if (close < 0) return FixResult.MarkerNotFound;

            lines.RemoveAt(close);
            lines.RemoveAt(open);
            File.WriteAllLines(path, lines);
            return FixResult.Applied;
        }

        // All uncommented IMPL_APP_CONTROLLER_SUBCLASS(...) registrations under root
        // (line comments and /* */ blocks are skipped; Pods never registers, skip for speed).
        static List<string> FindAppControllerRegistrations(string root) {
            var found = new List<string>();
            foreach (string file in Directory.EnumerateFiles(root, "*.m*", SearchOption.AllDirectories)) {
                if (!file.EndsWith(".m") && !file.EndsWith(".mm")) continue;
                if (file.Replace('\\', '/').Contains("/Pods/")) continue;

                bool inBlock = false;
                foreach (string raw in File.ReadLines(file)) {
                    string line = raw;
                    if (inBlock) {
                        int end = line.IndexOf("*/");
                        if (end < 0) continue;
                        line = line.Substring(end + 2);
                        inBlock = false;
                    }
                    int open = line.IndexOf("/*");
                    if (open >= 0 && line.IndexOf("*/", open) < 0) { inBlock = true; line = line.Substring(0, open); }
                    if (line.TrimStart().StartsWith("//")) continue;

                    var m = Regex.Match(line, @"IMPL_APP_CONTROLLER_SUBCLASS\s*\(\s*(\w+)\s*\)");
                    if (m.Success) found.Add($"{m.Groups[1].Value}  ({Path.GetFileName(file)})");
                }
            }
            return found;
        }
    }
}
