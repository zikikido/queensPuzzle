#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Puzzby
{
    /// <summary>
    /// Links the iOS frameworks the haptics use. UIKit (UIImpact/UINotificationFeedbackGenerator)
    /// is already linked by Unity, so this is mostly explicit/future-proof — the hook to add more
    /// (e.g. CoreHaptics) lives here.
    /// </summary>
    public static class HapticsIOSBuild
    {
        static readonly string[] Frameworks = { "UIKit.framework" };

        [PostProcessBuild]
        public static void OnPostProcessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;

            string projPath = PBXProject.GetPBXProjectPath(buildPath);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);

            string frameworkTarget = proj.GetUnityFrameworkTargetGuid();
            foreach (var fw in Frameworks)
                proj.AddFrameworkToProject(frameworkTarget, fw, weak: false);

            proj.WriteToFile(projPath);
        }
    }
}
#endif
