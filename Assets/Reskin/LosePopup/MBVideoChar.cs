using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace qp {
    /// <summary>
    /// Prototype: plays the lose dog as a green-screen video instead of a sprite flipbook.
    /// Uses APIOnly render mode — the VideoPlayer owns its output texture (more reliable than a
    /// hand-made RenderTexture) and we bind that texture to a RawImage whose material keys out the
    /// green (UI/ChromaKey). Instrumented with [VideoChar] logs for diagnosis.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class MBVideoChar : MonoBehaviour {

        [SerializeField] VideoPlayer _player;
        [SerializeField] VideoClip _clip;
/*        [SerializeField, Range(0f, 1f)] float _keyThreshold = 0.20f;
        [SerializeField, Range(0.001f, 0.5f)] float _keySoftness = 0.12f;*/

        RawImage _raw;

        void Awake() {
            _raw = GetComponent<RawImage>();
            if (_player == null) _player = GetComponent<VideoPlayer>();

         /*   var shader = Shader.Find("UI/ChromaKey");
            var mat = new Material(shader);
            mat.SetFloat("_KeyThreshold", _keyThreshold);
            mat.SetFloat("_KeySoftness", _keySoftness);
            _raw.material = mat;*/
            _raw.color = Color.white;

            _player.playOnAwake = false;
            _player.isLooping = false;
            _player.waitForFirstFrame = false;
            _player.skipOnDrop = true;
            _player.renderMode = VideoRenderMode.APIOnly;   // player owns the texture
            _player.audioOutputMode = VideoAudioOutputMode.None;
            if (_clip != null) _player.clip = _clip;

            _player.prepareCompleted += vp => {
                _raw.texture = vp.texture;   // bind the player's own output once it exists
                Debug.Log("[VideoChar] prepared frameCount=" + vp.frameCount + " tex=" + (vp.texture != null ? vp.texture.width + "x" + vp.texture.height : "NULL"));
            };
            _player.errorReceived += (vp, msg) => Debug.LogError("[VideoChar] ERROR: " + msg);

     /*       Debug.Log("[VideoChar] Awake OK shader=" + (shader != null ? shader.name : "NULL")
                + " clip=" + (_clip != null ? _clip.name : "NULL"));*/
        }

        // The popup is inactive during the level, so Prepare must run each time it is shown.
        void OnEnable() {
            if (_player != null && _player.clip != null) {
                _player.Prepare();
                Debug.Log("[VideoChar] OnEnable -> Prepare()");
            }
        }

        float _watchUntil;

        /// <summary>Restart the clip from frame 0. Called on the character beat of the show clip.</summary>
        public void Play() {
            _player.Stop();
            _player.Play();
            if (_player.texture != null) _raw.texture = _player.texture;
            _watchUntil = Time.realtimeSinceStartup + 3f;
            Debug.Log("[VideoChar] Play() isPrepared=" + _player.isPrepared + " active=" + gameObject.activeInHierarchy
                + " timeScale=" + Time.timeScale + " rawTex=" + (_raw.texture != null ? "set" : "NULL"));
        }

        bool _captured;
        void Update() {
            if (_raw.texture == null && _player.texture != null) _raw.texture = _player.texture;
            if (Time.realtimeSinceStartup > _watchUntil) return;

            // ~1.5s after Play, dump the decoder's own output texture to disk — proof that decode
            // works, independent of the RawImage/shader and of whether anyone is watching.
            if (!_captured && Time.realtimeSinceStartup > _watchUntil - 1.5f) {
                _captured = true;
                var src = _player.texture;
                if (src == null) { Debug.Log("[VideoChar] CAPTURE: player.texture is NULL, frame=" + _player.frame); return; }
                var tmp = RenderTexture.GetTemporary(src.width, src.height, 0);
                Graphics.Blit(src, tmp);
                var prev = RenderTexture.active; RenderTexture.active = tmp;
                var tex = new Texture2D(src.width, src.height);
                tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0); tex.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(tmp);
                System.IO.File.WriteAllBytes(
                    @"C:\Users\ziki\AppData\Local\Temp\claude\C--repos-queensPuzzle\636ffceb-257c-4522-887d-9df95e48fe4f\scratchpad\decode_proof.png",
                    tex.EncodeToPNG());
                Debug.Log("[VideoChar] CAPTURE wrote decode_proof.png " + src.width + "x" + src.height
                    + " frame=" + _player.frame + " isPlaying=" + _player.isPlaying);
                Destroy(tex);
            }
        }
    }
}
