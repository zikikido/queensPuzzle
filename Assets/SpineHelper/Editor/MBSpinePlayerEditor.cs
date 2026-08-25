using Spine.Unity;
using UnityEditor;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Guards the controller/renderer pairing: the controller's spineAnim dropdowns are picked
    /// from ITS skeletonDataAsset, so if the SkeletonRenderer on this object plays a different
    /// data asset the configured animation names may not exist — show that as an error.
    /// </summary>
    [CustomEditor(typeof(MBSpinePlayer))]
    public class MBSpinePlayerEditor : Editor {

        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            var character = (MBSpinePlayer)target;
            var controller = character.Controller;
            var renderer = character.GetComponent<SkeletonRenderer>();
            if (controller == null || renderer == null) return;

            if (controller.skeletonDataAsset == null)
                EditorGUILayout.HelpBox($"Controller '{controller.name}' has no SkeletonData Asset — its spineAnim dropdowns are empty.", MessageType.Warning);
            else if (controller.skeletonDataAsset != renderer.skeletonDataAsset)
                EditorGUILayout.HelpBox(
                    $"Data mismatch: controller '{controller.name}' is set up for '{controller.skeletonDataAsset.name}' " +
                    $"but the SkeletonRenderer plays '{(renderer.skeletonDataAsset != null ? renderer.skeletonDataAsset.name : "(none)")}'. " +
                    "Its state animation names may not exist there.", MessageType.Error);
        }
    }
}
