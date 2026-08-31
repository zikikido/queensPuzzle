using System.Globalization;
using System.IO;
using System.Text;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace qp {
    /// <summary>
    /// Bakes the puppy's Spine animations to transparent PNG frame sequences for the HTML
    /// playable ads. playable/build.py reads the output (playable/src/spine_frames/) and
    /// packs it into the CSS sprite strip + still frames the ads use.
    /// Every animation renders through ONE shared camera framing (union bounds over all
    /// frames of all animations), so all baked frames share the same world-to-pixel scale
    /// and build.py can crop them with one common box.
    /// </summary>
    public static class SpineFrameBaker {

        const string PrefabPath = "Assets/Reskin/QueenInGame/QueenSpine.prefab";
        static readonly string[] Anims = { "idle_thinking", "celebrate", "disappointed", "cry_in", "cry_loop" };
        const int Fps = 12;
        const int RtSize = 512;
        const float Margin = 1.03f;

        [MenuItem("QueensPuzzle/Bake Playable Spine Frames")]
        public static void Bake() {
            string outRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "playable", "src", "spine_frames"));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError("[SpineFrameBaker] Missing " + PrefabPath); return; }

            // instantiate the real in-game prefab: both Spine components are ExecuteAlways,
            // so they wire themselves up in edit mode exactly as they do in the game
            var go = Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            // far from the open scene's content, so the bake camera sees nothing else
            go.transform.position = new Vector3(10000f, 10000f, 0f);
            var sr = go.GetComponentInChildren<SkeletonRenderer>(true);
            var sa = go.GetComponentInChildren<SkeletonAnimation>(true);
            if (sr == null || sa == null) { Debug.LogError("[SpineFrameBaker] Prefab has no SkeletonRenderer/SkeletonAnimation"); Object.DestroyImmediate(go); return; }

            var camGo = new GameObject("~SpineBakeCam");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.cullingMask = ~0;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            var rt = new RenderTexture(RtSize, RtSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(RtSize, RtSize, TextureFormat.RGBA32, false, false);

            try {
                sa.Initialize(false);
                var meshRenderer = sr.GetComponent<MeshRenderer>();

                // pass 1: union bounds across every frame of every animation -> shared framing
                Bounds union = default;
                bool hasBounds = false;
                foreach (string anim in Anims) {
                    int frames = FrameCount(sa, anim);
                    StartAnim(sa, sr, anim);
                    for (int f = 0; f < frames; f++) {
                        if (f > 0) StepFrame(sa, sr);
                        if (!hasBounds) { union = meshRenderer.bounds; hasBounds = true; }
                        else union.Encapsulate(meshRenderer.bounds);
                    }
                }
                if (!hasBounds) { Debug.LogError("[SpineFrameBaker] Nothing to bake"); return; }

                cam.orthographicSize = Mathf.Max(union.extents.x, union.extents.y) * Margin;
                cam.transform.position = new Vector3(union.center.x, union.center.y, union.center.z - 10f);

                // pass 2: render + save (same stepping as pass 1, so stateful physics match)
                var manifest = new StringBuilder();
                manifest.Append("{\n  \"fps\": ").Append(Fps).Append(",\n  \"size\": ").Append(RtSize).Append(",\n  \"anims\": {");
                for (int a = 0; a < Anims.Length; a++) {
                    string anim = Anims[a];
                    string dir = Path.Combine(outRoot, anim);
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    Directory.CreateDirectory(dir);

                    int frames = FrameCount(sa, anim);
                    float duration = sa.Skeleton.Data.FindAnimation(anim).Duration;
                    StartAnim(sa, sr, anim);
                    for (int f = 0; f < frames; f++) {
                        if (f > 0) StepFrame(sa, sr);
                        Render(cam, rt, tex);
                        File.WriteAllBytes(Path.Combine(dir, f.ToString("000") + ".png"), tex.EncodeToPNG());
                    }
                    manifest.Append(a > 0 ? "," : "")
                        .Append("\n    \"").Append(anim).Append("\": {\"frames\": ").Append(frames)
                        .Append(", \"duration\": ").Append(duration.ToString("0.###", CultureInfo.InvariantCulture)).Append("}");
                    Debug.Log("[SpineFrameBaker] " + anim + ": " + frames + " frames (" + duration.ToString("0.###") + "s) -> " + dir);
                }
                manifest.Append("\n  }\n}\n");
                File.WriteAllText(Path.Combine(outRoot, "bake.json"), manifest.ToString());
                Debug.Log("[SpineFrameBaker] Done -> " + outRoot);
            } finally {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }

        static int FrameCount(SkeletonAnimation sa, string animName) {
            var anim = sa.Skeleton.Data.FindAnimation(animName);
            if (anim == null) { Debug.LogError("[SpineFrameBaker] No animation '" + animName + "'"); return 0; }
            return Mathf.Max(1, Mathf.RoundToInt(anim.Duration * Fps));
        }

        static void StartAnim(SkeletonAnimation sa, SkeletonRenderer sr, string animName) {
            sa.AnimationState.ClearTracks();
            sa.Skeleton.SetupPose();
            var entry = sa.AnimationState.SetAnimation(0, animName, false);
            entry.MixDuration = 0f;
            sa.Update(0f);
            sr.LateUpdate();
        }

        static void StepFrame(SkeletonAnimation sa, SkeletonRenderer sr) {
            sa.Update(1f / Fps);
            sr.LateUpdate();
        }

        static void Render(Camera cam, RenderTexture rt, Texture2D tex) {
            var req = new RenderPipeline.StandardRequest();
            req.destination = rt;
            if (RenderPipeline.SupportsRenderRequest(cam, req)) {
                RenderPipeline.SubmitRenderRequest(cam, req);
            } else {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
            }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            // The Spine shader writes premultiplied color; PNG wants straight alpha.
            // PMA happened in linear space, so un-premultiply there, then re-encode to sRGB.
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++) {
                var c = px[i];
                if (c.a <= 0.002f) { px[i] = Color.clear; continue; }
                float r = Mathf.Clamp01(Mathf.GammaToLinearSpace(c.r) / c.a);
                float g = Mathf.Clamp01(Mathf.GammaToLinearSpace(c.g) / c.a);
                float b = Mathf.Clamp01(Mathf.GammaToLinearSpace(c.b) / c.a);
                px[i] = new Color(Mathf.LinearToGammaSpace(r), Mathf.LinearToGammaSpace(g), Mathf.LinearToGammaSpace(b), c.a);
            }
            tex.SetPixels(px);
            tex.Apply();
        }
    }
}
