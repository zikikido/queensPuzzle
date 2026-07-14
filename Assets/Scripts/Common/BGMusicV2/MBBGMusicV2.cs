using System.Collections;
using UnityEngine;

namespace Common {

    /// <summary>
    /// BG music player V2: tracks come from a BGMusicPlaylist as Resources paths, and only the
    /// clip being played is ever in memory. Pure playback — no static instance, no
    /// DontDestroyOnLoad, no ads/UI knowledge; the hosting project owns lifetime and wiring.
    /// The GameObject name keys the PlayerPrefs entries, so keep it unique per player.
    /// </summary>
    public class MBBGMusicV2 : MonoBehaviour {

        public static float MAX_VOLUME = 0.4f;

        [SerializeField] BGMusicPlaylist _playlist;

        AudioSource _ac;

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }

        // -1 = never played, so the first nextTrack lands on track 0
        int _lastPlayed {
            get => PlayerPrefs.GetInt("cmn_music_track_" + name, -1);
            set => PlayerPrefs.SetInt("cmn_music_track_" + name, value);
        }

        public bool Mute {
            get => PlayerPrefs.GetInt("cmn_music_mute_" + name, 0) == 1;
            set {
                PlayerPrefs.SetInt("cmn_music_mute_" + name, value ? 1 : 0);

                if (value)
                    Stop(0);
                else
                    Play(nextTrack: false, fadeDuration: 2f);
            }
        }

        void Awake() {
            _ac = gameObject.AddComponent<AudioSource>();
            _ac.loop = true;
            _ac.playOnAwake = false;
            _ac.volume = 0;
        }

        void OnDestroy() {
            _unloadClip();
        }

        /// <summary>Start the looped BG track. nextTrack=true advances the persisted rotation
        /// index (also switches mid-play); false plays the last track. No-op when muted, when
        /// the playlist is empty, or when already playing and nextTrack is false.</summary>
        public void Play(bool nextTrack, float fadeDuration) {
            if (Mute)
                return;

            var paths = _playlist ? _playlist.ResourcePaths : null;
            if (paths == null || paths.Length == 0)
                return;

            if (IsPlaying && !nextTrack)
                return;

            var idx = nextTrack ? (_lastPlayed + 1) % paths.Length
                                : Mathf.Max(_lastPlayed, 0) % paths.Length;

            var clip = Resources.Load<AudioClip>(paths[idx]);
            if (clip == null) {
                CDebug.LogError($"[MBBGMusicV2] track not found in Resources: '{paths[idx]}'");
                return;
            }

            _lastPlayed = idx;

            if (_ac.clip != null && _ac.clip != clip)
                _unloadClip();

            IsPlaying = true;
            IsPaused = false;
            _ac.clip = clip;
            StopAllCoroutines();
            StartCoroutine(_fadeIn(fadeDuration));
        }

        public void Stop(float fadeDuration) {
            if (!IsPlaying)
                return;

            IsPlaying = false;
            IsPaused = false;
            StopAllCoroutines();
            StartCoroutine(_fadeOut(fadeDuration));
        }

        /// <summary>Pause playback (e.g. while a fullscreen ad or OS interruption owns audio).</summary>
        public void Pause() {
            if (!IsPlaying || IsPaused)
                return;

            IsPaused = true;
            _ac.Pause();
        }

        /// <summary>Resume after Pause(). Ignored if not paused, or muted meanwhile.</summary>
        public void Resume() {
            if (!IsPlaying || !IsPaused)
                return;

            IsPaused = false;

            if (Mute)
                return;

            _ac.UnPause();
        }

        IEnumerator _fadeIn(float fadeDuration) {
            _ac.Play();
            var anim = Animators.AnimateNumber(_ac.volume, MAX_VOLUME, fadeDuration);
            while (anim.MoveNext()) {
                _ac.volume = anim.Current;
                yield return null;
            }
        }

        IEnumerator _fadeOut(float fadeDuration) {
            var anim = Animators.AnimateNumber(_ac.volume, 0, fadeDuration);
            while (anim.MoveNext()) {
                _ac.volume = anim.Current;
                yield return null;
            }
            _unloadClip();
        }

        void _unloadClip() {
            if (_ac == null || _ac.clip == null)
                return;

            var clip = _ac.clip;
            _ac.Stop();
            _ac.clip = null;
            clip.UnloadAudioData();
        }
    }
}
