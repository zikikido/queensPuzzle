#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Puzzby
{
    /// <summary>
    /// Links the iOS frameworks the haptics use: UIKit (feedback generators) and CoreHaptics
    /// (CHHapticEngine). CoreHaptics is linked weak so the binary still loads on iOS &lt; 13, where
    /// the code paths are guarded by @available and fall back to UIKit.
    /// </summary>
    public static class HapticsIOSBuild
    {
        [PostProcessBuild]
        public static void OnPostProcessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;

            string projPath = PBXProject.GetPBXProjectPath(buildPath);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);

            string frameworkTarget = proj.GetUnityFrameworkTargetGuid();
            proj.AddFrameworkToProject(frameworkTarget, "UIKit.framework", weak: false);
            proj.AddFrameworkToProject(frameworkTarget, "CoreHaptics.framework", weak: true);

            proj.WriteToFile(projPath);
        }
    }
}
#endif
