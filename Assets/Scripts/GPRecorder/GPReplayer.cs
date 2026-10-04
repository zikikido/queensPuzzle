#if UNITY_EDITOR
using Common;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using QueensPuzzle;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Editor-only replay: fires the recorded actions at their times through MBGameplay's replay
    /// entries, so the board behaves exactly as if a player made the moves (anims, sounds, win).
    /// A replay run is started by the GPRecorderWindow: it saves the record, puts its path into
    /// SessionState and (re)enters play mode — LevelLoader then serves the embedded level and
    /// MBGameplay skips the board restore.
    /// </summary>
    public static class GPReplayer {

        // SessionState keys (same handover pattern as LevelLoader.PlayLevelGuidKey).
        public const string ReplayRecordKey = "qp.gpRecordPath";   // JSON path of the record to replay
        public const string FreshBoardKey = "qp.gpFreshBoard";     // fresh-record run: no board restore

        public static bool IsReplaying { get; private set; }
        public static float PlayheadTime { get; private set; }   // current replay clock, for the window

        /// <summary>The replay clock is moving (IsReplaying also covers the wait before it
        /// starts). Card videos play only while this is on.</summary>
        public static bool ClockRunning { get; private set; }

        /// <summary>Fired when the replay clock starts — with a hook that is after the hook is
        /// on screen and its video prepared, so a capture started here opens on its first frame.</summary>
        public static System.Action ClockStarted;
        public static string RecordFolder;   // where the record's voices/wavs live — window keeps it fresh

        static Coroutine _run;

        public static bool ReplayPending =>
            !string.IsNullOrEmpty(UnityEditor.SessionState.GetString(ReplayRecordKey, ""));

        /// <summary>True when the next board build must NOT restore a saved attempt — replays and
        /// fresh ad recordings both need the level to start empty. Checked by MBGameplay.</summary>
        public static bool WantsFreshBoard =>
            ReplayPending || UnityEditor.SessionState.GetBool(FreshBoardKey, false);

        /// <summary>The level embedded in the pending replay's record, or null when no replay is
        /// pending. Called by LevelLoader.LoadLevel — the record's level wins in-editor.</summary>
        public static LevelPack.Level LoadPendingLevel() {
            string path = UnityEditor.SessionState.GetString(ReplayRecordKey, "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            RecordFolder = Path.GetDirectoryName(path);
            try { return GPRecord.Load(path)?.level?.ToLevel(); }
            catch (System.Exception e) {
                Common.CDebug.LogError("[GPReplayer] record unreadable: " + e.Message);
                return null;
            }
        }

        /// <summary>True when the record can drive the CURRENT play session directly — play mode
        /// up, board ready, and the same puzzle on the board. Then Seek / Play / a re-record all
        /// work in place, with no play-mode restart.</summary>
        public static bool CanDrive(GPRecord record) =>
            Application.isPlaying && MBGameplay.instance != null && MBGameplay.instance.Ready
            && record != null && record.IsValid && MBGameplay.instance.Level != null
            && MBGameplay.instance.Level.ContentHash() == record.level.ToLevel().ContentHash();

        /// <summary>Video-style scrub: show the board exactly as it was at time <paramref name="t"/> —
        /// empty board + revealed queens, with every action ≤ t baked in. Instant, no anims.</summary>
        public static void Seek(GPRecord record, float t) {
            var gp = MBGameplay.instance;
            if (gp == null || !gp.Ready) return;
            record = record.Resolved();   // a template plays its referenced gameplay
            EnsureOverlays(record);
            int n = record.level.size;
            var states = new MBCell.ECellType[n * n];
            if (record.level.revealedRows != null)
                foreach (int row in record.level.revealedRows)
                    states[row * n + record.level.solutionColumns[row]] = MBCell.ECellType.QUEEN;
            foreach (var a in record.actions) {
                if (a.time > t) break;   // the list is time-sorted
                states[a.y * n + a.x] = a.to;
            }
            PlayheadTime = t;
            gp.ReplaySetBoard(states);
        }

        /// <summary>Bring the record's overlays (hand, spotlight, voices, subtitles, images,
        /// music, end card, hook) in line with it. Needs no board — the hook must already cover
        /// the screen while the board is still blooming in.</summary>
        static void EnsureOverlays(GPRecord record) {
            // hand, spotlight and voices run whenever their tracks have keys; all follow PlayheadTime
            if (record.handKeys.Count > 0) GPHand.Ensure(record);
            else GPHand.Remove();
            if (record.spotKeys.Count > 0) GPSpotlight.Ensure(record);
            else GPSpotlight.Remove();
            // a resolved template's voices live in the referenced record's folder
            string folder = !string.IsNullOrEmpty(record.voicesFolder) ? record.voicesFolder : RecordFolder;
            record.voicesFile = GPRecord.PickVoiceSet(folder, record.voicesFile);   // unset/missing = the first set
            if (record.voiceKeys.Count > 0 && !string.IsNullOrEmpty(folder)) GPVoicePlayer.Ensure(record, folder);
            else GPVoicePlayer.Remove();
            if (record.showAdText && !string.IsNullOrEmpty(folder)) GPAdText.Ensure(record, folder);
            else GPAdText.Remove();
            if (record.adImages.Count > 0) GPAdImages.Ensure(record);
            else GPAdImages.Remove();
            if (record.music) GPMusic.Ensure(record);
            else GPMusic.Remove();
            if (record.showEndCard) GPEndCard.Ensure(record);
            else GPEndCard.Remove();
            if (record.showHook) GPHook.Ensure(record);
            else GPHook.Remove();
        }

        /// <summary>Play the record from <paramref name="fromTime"/> to its end: the board jumps
        /// to that point instantly (Seek), then the remaining actions fire at their times.</summary>
        public static void Play(GPRecord record, float fromTime) {
            Stop();
            _run = MBGameplay.instance.StartCoroutine(Run(record.Resolved(), fromTime));
        }

        public static void Stop() {
            if (_run != null && MBGameplay.instance != null) MBGameplay.instance.StopCoroutine(_run);
            _run = null;
            if (IsReplaying && MBGameplay.instance != null) MBGameplay.instance.InputLocks--;
            IsReplaying = false;
            ClockRunning = false;
        }

        static IEnumerator Run(GPRecord record, float fromTime) {
            var gp = MBGameplay.instance;
            IsReplaying = true;
            gp.InputLocks++;   // the replay drives the board — block real touches

            // a hook covers the bloom reveal: wait until it is on screen with its video
            // prepared, then the clock (and video) run from the hook's first frame while the
            // board blooms underneath. Without a hook the clock waits for the board as before.
            EnsureOverlays(record);
            PlayheadTime = fromTime;
            bool hookFirst = record.showHook && fromTime < record.hookTime;
            if (hookFirst) {
                float giveUp = Time.realtimeSinceStartup + 5f;
                while (!GPHook.IsReady && Time.realtimeSinceStartup < giveUp) yield return null;
                ClockRunning = true;
                ClockStarted?.Invoke();
            }
            float t0 = Time.time - fromTime;
            while (!gp.Ready) {   // wait out the bloom reveal
                if (hookFirst) PlayheadTime = Time.time - t0;
                yield return null;
            }
            if (!hookFirst) {
                t0 = Time.time - fromTime;
                ClockRunning = true;
                ClockStarted?.Invoke();
            }

            float seekT = Time.time - t0;
            Seek(record, seekT);
            foreach (var a in record.actions) {
                if (a.time <= seekT) continue;   // baked in by the seek
                while (Time.time - t0 < a.time) { PlayheadTime = Time.time - t0; yield return null; }
                PlayheadTime = a.time;
                // one bad action (an edited record can hold anything) must not kill the run —
                // an uncaught throw here would also leak the InputLocks++ above
                try { Apply(gp, a); }
                catch (System.Exception e) { Common.CDebug.LogError($"[GPReplayer] action at {a.time:0.00}s ({a.to} on {a.x},{a.y}) failed: {e.Message}"); }
            }

            // keep the clock running to the END of all tracks — voice/hand/spot keys can sit
            // after the last board action (e.g. the closing voice line over the win)
            float end = record.Duration;
            while (Time.time - t0 < end) { PlayheadTime = Time.time - t0; yield return null; }
            PlayheadTime = end;

            IsReplaying = false;
            ClockRunning = false;
            gp.InputLocks--;
            _run = null;
        }

        static void Apply(MBGameplay gp, GPRecordAction a) {
            switch (a.to) {
                case MBCell.ECellType.X:
                case MBCell.ECellType.EMPTY:
                    gp.ReplayMark(a.x, a.y, a.to);
                    break;
                default:   // QUEEN / WRONG_QUEEN — the outcome is re-derived by the normal logic
                    gp.ReplayQueenTap(a.x, a.y);
                    break;
            }
        }
    }

    /// <summary>
    /// Tutorial-style spotlight driven by the record's spot track — implemented WITH the real
    /// tutorial: MBToturial.ShowCells lights the cells (curtain + popping holes, correct sorting,
    /// everything the tutorial already does) and Hide() drops it. The only twist: AutoHide is
    /// parked off while our spotlight is up — a targetless "step" counts as done, so the
    /// tutorial's Update would close it instantly — and restored on clear/remove.
    /// </summary>
    public class GPSpotlight : MonoBehaviour {

        public static GPSpotlight Instance { get; private set; }

        public static void Ensure(GPRecord record) {
            if (Instance == null) Instance = new GameObject("$GPSpotlight").AddComponent<GPSpotlight>();
            Instance._record = record;
            Instance._applied = null;   // force a re-apply with the fresh keys
        }

        public static void Remove() {
            if (Instance != null) Destroy(Instance.gameObject);   // OnDestroy clears the spotlight
            Instance = null;
        }

        /// <summary>Force a re-apply on the next frame (🎯 Pick edited a key's cell list).</summary>
        public static void Refresh() {
            if (Instance != null) Instance._applied = null;
        }

        GPRecord _record;
        string _applied;   // signature of the hole set currently shown — re-apply only on change

        void OnDestroy() {
            if (Instance == this) Instance = null;
            Clear();
        }

        static void Clear() {
            var tut = MBToturial.instance;
            if (tut == null) return;
            tut.AutoHide = true;
            tut.SetApplyVisible(true);            // flags only — nothing may turn visible here: the
            tut.SetHandVisible(true);             // tutorial stays awake through the curtain fade-out,
                                                  // so a restored text box would flash on screen.
                                                  // ShowHint re-activates $TextContainer itself.
            if (tut.gameObject.activeSelf) tut.Hide();
        }

        // Our spotlight is curtain + holes ONLY — no hint message bubble, no Apply button
        // (same trimming the first-level tutorial does for its hand-guided steps).
        static void SetTextContainerVisible(MBToturial tut, bool on) {
            var tc = tut.transform.RecursiveFindChild("$TextContainer");
            if (tc != null) tc.gameObject.SetActive(on);
        }

        void Update() {
            var gp = MBGameplay.instance;
            if (_record == null || gp == null || !gp.Ready) return;

            // the hole set at the playhead: every SHOW since the last CLEAR
            float t = GPReplayer.PlayheadTime;
            var indices = new List<int>();
            var sig = new StringBuilder();
            foreach (var k in _record.spotKeys) {   // time-sorted
                if (k.time > t) break;
                if (k.kind == GPSpotKey.EKind.CLEAR) { indices.Clear(); sig.Clear(); continue; }
                for (int i = 0; i < k.Count; i++) {
                    int idx = k.ys[i] * gp.N + k.xs[i];
                    if (indices.Contains(idx)) continue;
                    indices.Add(idx);
                    sig.Append(idx).Append(';');
                }
            }

            string s = sig.ToString();
            if (s == _applied) return;   // unchanged — never re-pop the open holes
            _applied = s;

            var tut = MBToturial.instance;
            if (tut == null) return;
            if (indices.Count == 0) Clear();
            else {
                tut.AutoHide = false;   // our targetless spotlight must not close itself
                tut.ShowCells(indices);
                tut.SetApplyVisible(false);
                tut.SetHandVisible(false);   // the scene leaves $Hand active-self — waking the tutorial would show it
                SetTextContainerVisible(tut, false);
            }
        }
    }

    /// <summary>
    /// Plays the record's voice lines during replay: loads the active set's wavs from the record
    /// folder and fires each key as the playhead crosses it. Scrub jumps stay silent — only
    /// smooth forward motion plays, so replay (and video capture) get the audio, editing doesn't.
    /// </summary>
    public class GPVoicePlayer : MonoBehaviour {

        public static GPVoicePlayer Instance { get; private set; }

        public static void Ensure(GPRecord record, string folder) {
            if (Instance == null) {
                Instance = new GameObject("$GPVoicePlayer").AddComponent<GPVoicePlayer>();
                Instance._source = Instance.gameObject.AddComponent<AudioSource>();
                Instance._source.spatialBlend = 0f;
            }
            // a template's resolved copy is a new object on every seek — reload only when the
            // folder moved (a voices-file swap is caught by Update)
            bool reload = Instance._record == null || Instance._folder != folder;
            Instance._record = record;
            Instance._folder = folder;
            if (reload) Instance.ReloadClips();
        }

        public static void Remove() {
            if (Instance != null) Destroy(Instance.gameObject);
            Instance = null;
        }

        GPRecord _record;
        string _folder, _voicesFile;
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        AudioSource _source;
        float _lastT;

        System.DateTime _voicesStamp;

        void ReloadClips() {
            _clips.Clear();
            _voicesFile = _record.voicesFile;
            string vp = Path.Combine(_folder, _record.voicesFile);
            _voicesStamp = File.Exists(vp) ? File.GetLastWriteTimeUtc(vp) : System.DateTime.MinValue;
            var voices = GPVoices.Load(vp);
            foreach (var l in voices.lines) {
                if (string.IsNullOrEmpty(l.path)) continue;
                string p = Path.Combine(_folder, l.path);
                if (File.Exists(p)) StartCoroutine(LoadClip(l.name, p));
            }
        }

        IEnumerator LoadClip(string name, string path) {
            var type = path.EndsWith(".mp3", System.StringComparison.OrdinalIgnoreCase) ? AudioType.MPEG : AudioType.WAV;
            using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + path, type)) {
                yield return req.SendWebRequest();
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    _clips[name] = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
                else Common.CDebug.LogError($"[GPVoicePlayer] {name}: {req.error}");
            }
        }

        void Update() {
            if (_record == null) return;
            // voice set switched, or its file changed on disk (regenerated lines / new audio)
            string vpath = Path.Combine(_folder, _record.voicesFile);
            if (_record.voicesFile != _voicesFile ||
                (File.Exists(vpath) && File.GetLastWriteTimeUtc(vpath) != _voicesStamp))
                ReloadClips();
            float t = GPReplayer.PlayheadTime;
            if (t < _lastT || t - _lastT > 0.5f) { _lastT = t; return; }   // a jump — scrubbing, not playing
            foreach (var k in _record.voiceKeys) {
                if (k.time <= _lastT || k.time > t) continue;
                if (_clips.TryGetValue(k.name, out var clip)) _source.PlayOneShot(clip);
            }
            _lastT = t;
        }

        /// <summary>Length of a loaded line's clip (0 when not loaded) — the ad-text typewriter
        /// paces itself with this.</summary>
        public static float ClipLength(string name) =>
            Instance != null && Instance._clips.TryGetValue(name, out var c) && c != null ? c.length : 0f;

        void OnDestroy() {
            if (Instance == this) Instance = null;
        }
    }

    /// <summary>
    /// The AdsBottonText overlay: loads the scene additively during the session, colors $BG and
    /// $Text from the record, and types each voice line letter-by-letter in sync with its audio.
    /// Pure function of the playhead — scrubbing always shows the right text state.
    /// </summary>
    public class GPAdText : MonoBehaviour {

        public static GPAdText Instance { get; private set; }
        const string SceneName = "AdsVoiceTextPortrait";

        // found by name, so moving the scene inside Assets/ never breaks the overlay
        static string _scenePath;
        static string ScenePath {
            get {
                if (!string.IsNullOrEmpty(_scenePath) && File.Exists(_scenePath)) return _scenePath;
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Scene " + SceneName)) {
                    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(p) == SceneName) return _scenePath = p;
                }
                Common.CDebug.LogError($"[GPAdText] scene '{SceneName}' not found in the project");
                return null;
            }
        }

        public static void Ensure(GPRecord record, string folder) {
            if (Instance == null) Instance = new GameObject("$GPAdText").AddComponent<GPAdText>();
            Instance._record = record;
            Instance._folder = folder;
        }

        public static void Remove() {
            if (Instance != null) {
                Instance.UnloadScene();
                Destroy(Instance.gameObject);
            }
            Instance = null;
        }

        GPRecord _record;
        string _folder, _voicesFile;
        System.DateTime _voicesStamp;
        GPVoices _voices;
        TMPro.TMP_Text _text;
        UnityEngine.UI.Image _bg;
        bool _loading;

        void Update() {
            if (_record == null) return;
            if (_text == null) { EnsureScene(); return; }
            // reload when the set is swapped OR its file changed on disk — a stale in-memory copy
            // silently drops lines whose names were edited (they'd render as empty subtitles)
            string vp = Path.Combine(_folder, _record.voicesFile);
            var stamp = File.Exists(vp) ? File.GetLastWriteTimeUtc(vp) : System.DateTime.MinValue;
            if (_voices == null || _voicesFile != _record.voicesFile || stamp != _voicesStamp) {
                _voicesFile = _record.voicesFile;
                _voicesStamp = stamp;
                _voices = GPVoices.Load(vp);
            }

            if (_bg != null) {
                _bg.color = _record.adTextBg;
                // $Text stretches inside $BG — placing the strip places both
                var rt = _bg.rectTransform;
                float h = Mathf.Clamp(_record.adTextHeight, 0.03f, 0.6f);
                float y0 = Mathf.Clamp01(_record.adTextPos) * (1f - h);
                rt.anchorMin = new Vector2(0f, y0);
                rt.anchorMax = new Vector2(1f, y0 + h);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            _text.color = _record.adTextColor;

            // the line at the playhead = the latest voice key before it
            float t = GPReplayer.PlayheadTime;
            GPVoiceKey cur = null;
            foreach (var k in _record.voiceKeys) {
                if (k.time > t) break;
                cur = k;
            }
            var line = cur != null ? _voices.Find(cur.name) : null;
            if (line == null || string.IsNullOrEmpty(line.text)) {
                _text.text = "";
                return;
            }

            // typewriter paced to the audio clip (fallback: ~16 chars/s when not loaded yet)
            float dur = GPVoicePlayer.ClipLength(cur.name);
            if (dur <= 0f) dur = Mathf.Max(0.5f, line.text.Length * 0.06f);
            _text.text = line.text;
            _text.maxVisibleCharacters =
                Mathf.Clamp(Mathf.CeilToInt(line.text.Length * (t - cur.time) / dur), 0, line.text.Length);
        }

        void EnsureScene() {
            string path = ScenePath;
            if (string.IsNullOrEmpty(path)) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            if (!scene.isLoaded) {
                if (!_loading) {
                    _loading = true;
                    // editor-only load — the scene lives under an Editor/ folder and needs no
                    // Build Settings entry
                    UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path,
                        new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
                }
                return;
            }
            _loading = false;
            foreach (var root in scene.GetRootGameObjects()) {
                var tt = root.transform.RecursiveFindChild("$Text");
                if (tt != null && _text == null) _text = tt.GetComponent<TMPro.TMP_Text>();
                var bb = root.transform.RecursiveFindChild("$BG");
                if (bb != null && _bg == null) _bg = bb.GetComponent<UnityEngine.UI.Image>();
            }
            if (_text != null) _text.maxVisibleCharacters = 0;
        }

        void UnloadScene() {
            if (string.IsNullOrEmpty(_scenePath)) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(_scenePath);
            if (scene.isLoaded) UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
        }

        void OnDestroy() {
            if (Instance == this) Instance = null;
        }
    }

    /// <summary>
    /// The end card (AdsEndCardPortrait scene): appears around the record's end — offset from it
    /// by −3…+3s — with a chosen entrance animation. Purely a function of the playhead, so
    /// scrubbing across the end shows it exactly as the video will.
    /// </summary>
    public class GPEndCard : MonoBehaviour {

        public static GPEndCard Instance { get; private set; }
        const string SceneName = "AdsEndCardPortrait";

        static string _scenePath;
        static string FindScene() {
            if (!string.IsNullOrEmpty(_scenePath) && File.Exists(_scenePath)) return _scenePath;
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Scene " + SceneName)) {
                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == SceneName) return _scenePath = p;
            }
            Common.CDebug.LogError($"[GPEndCard] scene '{SceneName}' not found");
            return null;
        }

        public static void Ensure(GPRecord record) {
            if (Instance == null) Instance = new GameObject("$GPEndCard").AddComponent<GPEndCard>();
            Instance._record = record;
        }

        public static void Remove() {
            if (Instance != null) {
                Instance.UnloadScene();
                Destroy(Instance.gameObject);
            }
            Instance = null;
        }

        GPRecord _record;
        RectTransform _card;
        CanvasGroup _cg;
        UnityEngine.UI.Image _bg;
        Vector2 _homeMin, _homeMax;   // the card's authored placement — slides return to it
        bool _loading;

        void Update() {
            if (_record == null) return;
            if (_card == null) { EnsureScene(); return; }

            if (_bg != null) _bg.color = _record.endCardBg;

            // it enters at its own key on the timeline
            float start = _record.endCardTime;
            float k = Mathf.Clamp01((GPReplayer.PlayheadTime - start) / Mathf.Max(0.05f, _record.endCardAnimTime));
            float e = k < 0.5f ? 2f * k * k : 1f - Mathf.Pow(-2f * k + 2f, 2f) * 0.5f;   // ease in-out

            bool visible = GPReplayer.PlayheadTime >= start;
            _cg.alpha = visible ? (_record.endCardAnim == GPRecord.EEndCardAnim.Fade ? e : 1f) : 0f;
            _card.localScale = Vector3.one * (visible && _record.endCardAnim == GPRecord.EEndCardAnim.ScaleUp
                ? Mathf.Lerp(0.75f, 1f, e) : 1f);

            float slide = 0f;
            if (visible && _record.endCardAnim == GPRecord.EEndCardAnim.SlideUp) slide = Mathf.Lerp(-1f, 0f, e);
            else if (visible && _record.endCardAnim == GPRecord.EEndCardAnim.SlideDown) slide = Mathf.Lerp(1f, 0f, e);
            _card.anchorMin = new Vector2(_homeMin.x, _homeMin.y + slide);
            _card.anchorMax = new Vector2(_homeMax.x, _homeMax.y + slide);

            _video?.Update(_record.endCardVideo, _record.endCardVideoSound, GPReplayer.PlayheadTime - start, visible);
        }

        GPCardVideo _video;

        void EnsureScene() {
            string path = FindScene();
            if (string.IsNullOrEmpty(path)) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            if (!scene.isLoaded) {
                if (!_loading) {
                    _loading = true;
                    UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path,
                        new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
                }
                return;
            }
            _loading = false;
            foreach (var root in scene.GetRootGameObjects()) {
                var bg = root.transform.RecursiveFindChild("$BG");
                if (bg == null) continue;
                _card = bg as RectTransform;
                _bg = bg.GetComponent<UnityEngine.UI.Image>();
                _cg = bg.GetComponent<CanvasGroup>() ?? bg.gameObject.AddComponent<CanvasGroup>();
                _cg.blocksRaycasts = false;
                _cg.alpha = 0f;   // hidden until its moment
                _homeMin = _card.anchorMin;
                _homeMax = _card.anchorMax;
                _video = new GPCardVideo(gameObject, bg);
                break;
            }
        }

        void UnloadScene() {
            if (string.IsNullOrEmpty(_scenePath)) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(_scenePath);
            if (scene.isLoaded) UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
        }

        void OnDestroy() {
            if (Instance == this) Instance = null;
            _video?.Dispose();
        }
    }

    /// <summary>
    /// The hook (AdsHookSimpleVideoPortrait scene — just $BG + a swappable $Video): the end card in reverse — covers the screen from t=0 and
    /// LEAVES at its own key with the chosen animation, revealing the board. Purely a function
    /// of the playhead, like the end card.
    /// </summary>
    public class GPHook : MonoBehaviour {

        public static GPHook Instance { get; private set; }
        const string SceneName = "AdsHookSimpleVideoPortrait";

        /// <summary>On screen and its video (if any) prepared — the replay clock waits for this.</summary>
        public static bool IsReady => Instance != null && Instance._card != null
            && (Instance._video == null || Instance._video.IsReady(Instance._record.hookVideo));

        static string _scenePath;
        static string FindScene() {
            if (!string.IsNullOrEmpty(_scenePath) && File.Exists(_scenePath)) return _scenePath;
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Scene " + SceneName)) {
                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == SceneName) return _scenePath = p;
            }
            Common.CDebug.LogError($"[GPHook] scene '{SceneName}' not found");
            return null;
        }

        public static void Ensure(GPRecord record) {
            if (Instance == null) Instance = new GameObject("$GPHook").AddComponent<GPHook>();
            Instance._record = record;
        }

        public static void Remove() {
            if (Instance != null) {
                Instance.UnloadScene();
                Destroy(Instance.gameObject);
            }
            Instance = null;
        }

        GPRecord _record;
        RectTransform _card;
        CanvasGroup _cg;
        UnityEngine.UI.Image _bg;
        Vector2 _homeMin, _homeMax;
        GPCardVideo _video;
        bool _loading;

        void Update() {
            if (_record == null) return;
            if (_card == null) { EnsureScene(); return; }

            if (_bg != null) _bg.color = _record.hookBg;

            // it leaves at its own key on the timeline
            float t = GPReplayer.PlayheadTime;
            float k = Mathf.Clamp01((t - _record.hookTime) / Mathf.Max(0.05f, _record.hookAnimTime));
            float e = k < 0.5f ? 2f * k * k : 1f - Mathf.Pow(-2f * k + 2f, 2f) * 0.5f;   // ease in-out

            bool visible = k < 1f;
            var anim = _record.hookAnim;
            // ScaleUp exits as a zoom-through (grows while fading) — a full-screen card that
            // only grew would show nothing new and then pop
            _cg.alpha = !visible ? 0f
                : anim == GPRecord.EEndCardAnim.Fade || anim == GPRecord.EEndCardAnim.ScaleUp ? 1f - e : 1f;
            _card.localScale = Vector3.one * (visible && anim == GPRecord.EEndCardAnim.ScaleUp
                ? Mathf.Lerp(1f, 1.25f, e) : 1f);

            float slide = 0f;
            if (visible && anim == GPRecord.EEndCardAnim.SlideUp) slide = Mathf.Lerp(0f, 1f, e);
            else if (visible && anim == GPRecord.EEndCardAnim.SlideDown) slide = Mathf.Lerp(0f, -1f, e);
            _card.anchorMin = new Vector2(_homeMin.x, _homeMin.y + slide);
            _card.anchorMax = new Vector2(_homeMax.x, _homeMax.y + slide);

            _video?.Update(_record.hookVideo, _record.hookVideoSound, t, visible);
        }

        void EnsureScene() {
            string path = FindScene();
            if (string.IsNullOrEmpty(path)) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            if (!scene.isLoaded) {
                if (!_loading) {
                    _loading = true;
                    UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path,
                        new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
                }
                return;
            }
            _loading = false;
            foreach (var root in scene.GetRootGameObjects()) {
                var bg = root.transform.RecursiveFindChild("$BG");
                if (bg == null) continue;
                _card = bg as RectTransform;
                _bg = bg.GetComponent<UnityEngine.UI.Image>();
                _cg = bg.GetComponent<CanvasGroup>() ?? bg.gameObject.AddComponent<CanvasGroup>();
                _cg.blocksRaycasts = false;
                _homeMin = _card.anchorMin;
                _homeMax = _card.anchorMax;
                _video = new GPCardVideo(gameObject, bg);
                // the hook covers EVERYTHING — the other overlay scenes (images, subtitles) sit
                // at order 0 too, and equal orders draw in load order
                var canvas = bg.GetComponentInParent<Canvas>();
                if (canvas != null) canvas.sortingOrder = 100;
                break;
            }
        }

        void UnloadScene() {
            if (string.IsNullOrEmpty(_scenePath)) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(_scenePath);
            if (scene.isLoaded) UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
        }

        void OnDestroy() {
            if (Instance == this) Instance = null;
            _video?.Dispose();
        }
    }

    /// <summary>
    /// A card's BG video (hook / end card), drawn into the card's $Video RawImage (added under
    /// $BG, behind everything, when the scene has none), cover-fitted and looped.
    ///
    /// No VideoPlayer — it runs on its own clock, can't redraw a paused seek and freezes under
    /// the Recorder's fixed-step capture. Instead ffmpeg splits the video ONCE into jpg frames +
    /// a wav (cached in HookVideos/.cache/&lt;name&gt;/), and every frame shows exactly the frame
    /// for the playhead: Play, scrubbing and capture all land on the same picture. The sound
    /// plays only while the replay clock runs.
    /// </summary>
    public class GPCardVideo {

        public const int Fps = 30;   // frames are extracted at this rate

        readonly UnityEngine.UI.RawImage _image;
        readonly UnityEngine.UI.AspectRatioFitter _fit;
        readonly AudioSource _audio;
        string _file;
        byte[][] _frames;   // jpg bytes — decoded only when the shown frame changes
        Texture2D _tex;
        int _shown = -1;
        float _lastLocal = -1f;

        public GPCardVideo(GameObject host, Transform bg) {
            var v = bg.RecursiveFindChild("$Video");
            if (v == null) {
                v = new GameObject("$Video", typeof(RectTransform)).transform;
                v.SetParent(bg, false);
                v.SetAsFirstSibling();   // behind the logo / PLAY NOW
            }
            _image = v.GetComponent<UnityEngine.UI.RawImage>() ?? v.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            _image.raycastTarget = false;
            _image.enabled = false;
            _fit = v.GetComponent<UnityEngine.UI.AspectRatioFitter>() ?? v.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
            _fit.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent;   // cover, no bars
            _audio = host.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = true;
            _audio.spatialBlend = 0f;
        }

        /// <summary><paramref name="localTime"/> = seconds since the card's video starts.</summary>
        public void Update(string file, bool sound, float localTime, bool visible) {
            if ((file ?? "") != _file) Load(file ?? "");
            if (_frames == null || _frames.Length == 0 || !visible) {
                _image.enabled = false;
                if (_audio.isPlaying) _audio.Stop();
                _lastLocal = -1f;
                return;
            }
            _image.enabled = true;

            float local = Mathf.Max(0f, localTime);
            int idx = (int)(local * Fps) % _frames.Length;   // loops
            if (idx != _shown) {
                _tex.LoadImage(_frames[idx]);
                _shown = idx;
                _fit.aspectRatio = _tex.height > 0 ? (float)_tex.width / _tex.height : 1f;
            }

            // sound: only while the replay clock runs — scrubbing stays silent, like the voices.
            // Started at the playhead's spot; re-placed only on a real jump, never nudged.
            bool wantSound = sound && GPReplayer.ClockRunning && _audio.clip != null;
            if (!wantSound) { if (_audio.isPlaying) _audio.Stop(); }
            else if (!_audio.isPlaying || local < _lastLocal || local - _lastLocal > 0.5f) {
                _audio.time = local % _audio.clip.length;
                if (!_audio.isPlaying) _audio.Play();
            }
            _lastLocal = local;
        }

        /// <summary>The card's video is loaded (or there's nothing to play).</summary>
        public bool IsReady(string file) => string.IsNullOrEmpty(file) || _file == file;

        void Load(string file) {
            _file = file;
            _frames = null;
            _shown = -1;
            _audio.Stop();
            _audio.clip = null;
            if (string.IsNullOrEmpty(file)) return;

            string cache = EnsureCache(file);
            if (cache == null) return;
            _frames = Directory.GetFiles(cache, "f_*.jpg")
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .Select(File.ReadAllBytes).ToArray();
            if (_tex == null) _tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            _image.texture = _tex;
            string wav = Path.Combine(cache, "audio.wav");
            if (File.Exists(wav)) _audio.clip = LoadWav(wav);
        }

        public void Dispose() {
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = null;
        }

        // ---- the frame cache ------------------------------------------------------------

        /// <summary>The video's extracted frames + wav — built with ffmpeg on first use (or when
        /// the video changed). The window calls this as soon as a video is picked, so a replay
        /// never stalls on it. Returns the cache folder, null on failure.</summary>
        public static string EnsureCache(string file) {
            string video = Path.Combine(GPRecord.VideosDir, file);
            if (!File.Exists(video)) { Common.CDebug.LogError($"[GPCardVideo] missing {video}"); return null; }
            string cache = Path.Combine(GPRecord.VideosDir, ".cache", Path.GetFileNameWithoutExtension(file));
            string done = Path.Combine(cache, "done");
            if (File.Exists(done) && File.GetLastWriteTimeUtc(done) >= File.GetLastWriteTimeUtc(video)) return cache;

            string ffmpeg = FindFfmpeg();
            if (ffmpeg == null) {
                Common.CDebug.LogError("[GPCardVideo] ffmpeg not found — install it (winget install Gyan.FFmpeg) or put it on PATH");
                return null;
            }
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
            Directory.CreateDirectory(cache);
            UnityEditor.EditorUtility.DisplayProgressBar("GP Recorder", $"Preparing {file}…", 0.5f);
            try {
                if (!RunFfmpeg(ffmpeg, $"-y -v error -i \"{video}\" -vf fps={Fps} -q:v 2 \"{Path.Combine(cache, "f_%05d.jpg")}\"")) return null;
                RunFfmpeg(ffmpeg, $"-y -v error -i \"{video}\" -vn -ac 2 -ar 48000 -c:a pcm_s16le \"{Path.Combine(cache, "audio.wav")}\"");   // no audio track = no wav, fine
            } finally { UnityEditor.EditorUtility.ClearProgressBar(); }
            File.WriteAllText(done, "");
            return cache;
        }

        public static string FindFfmpeg() {
            foreach (var dir in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
                try {
                    string p = Path.Combine(dir.Trim('"'), "ffmpeg.exe");
                    if (File.Exists(p)) return p;
                } catch (System.ArgumentException) { }
            }
            // winget installs without touching an already-running editor's PATH
            string winget = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(winget))
                foreach (var d in Directory.GetDirectories(winget, "*FFmpeg*"))
                    foreach (var p in Directory.GetFiles(d, "ffmpeg.exe", SearchOption.AllDirectories)) return p;
            return null;
        }

        static bool RunFfmpeg(string exe, string args) {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            };
            using (var p = System.Diagnostics.Process.Start(psi)) {
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) Debug.LogWarning($"[GPCardVideo] ffmpeg {args}\n{err}");
                return p.ExitCode == 0;
            }
        }

        /// <summary>16-bit PCM wav → AudioClip (ffmpeg writes exactly that).</summary>
        static AudioClip LoadWav(string path) {
            var b = File.ReadAllBytes(path);
            int channels = 2, rate = 48000, pos = 12;
            while (pos + 8 <= b.Length) {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int size = System.BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ") {
                    channels = System.BitConverter.ToInt16(b, pos + 10);
                    rate = System.BitConverter.ToInt32(b, pos + 12);
                } else if (id == "data") {
                    size = Mathf.Min(size, b.Length - pos - 8);
                    var samples = new float[size / 2];
                    for (int i = 0; i < samples.Length; i++)
                        samples[i] = System.BitConverter.ToInt16(b, pos + 8 + i * 2) / 32768f;
                    var clip = AudioClip.Create(Path.GetFileName(path), samples.Length / channels, channels, rate, false);
                    clip.SetData(samples, 0);
                    return clip;
                }
                pos += 8 + size + (size & 1);
            }
            return null;
        }
    }

    /// <summary>
    /// The game's BG music under a take. The real player (MBMusic) lives in the Loading scene,
    /// which a recorder session never goes through — so this plays a playlist track itself,
    /// looped, at the record's volume.
    /// </summary>
    public class GPMusic : MonoBehaviour {

        public static GPMusic Instance { get; private set; }

        /// <summary>Track names from the project's BGMusicPlaylist (for the window's menu).</summary>
        public static string[] Tracks() {
            var pl = Playlist();
            if (pl == null) return new string[0];
            var names = new string[pl.ResourcePaths.Length];
            for (int i = 0; i < names.Length; i++) names[i] = Path.GetFileName(pl.ResourcePaths[i]);
            return names;
        }

        static Common.BGMusicPlaylist Playlist() {
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:BGMusicPlaylist")) {
                var pl = UnityEditor.AssetDatabase.LoadAssetAtPath<Common.BGMusicPlaylist>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (pl != null && pl.ResourcePaths.Length > 0) return pl;
            }
            return null;
        }

        public static void Ensure(GPRecord record) {
            if (Instance == null) {
                Instance = new GameObject("$GPMusic").AddComponent<GPMusic>();
                Instance._source = Instance.gameObject.AddComponent<AudioSource>();
                Instance._source.loop = true;
                Instance._source.spatialBlend = 0f;
            }
            Instance._record = record;
        }

        public static void Remove() {
            if (Instance != null) Destroy(Instance.gameObject);
            Instance = null;
        }

        GPRecord _record;
        AudioSource _source;
        int _track = -1;

        void Update() {
            if (_record == null || _source == null) return;
            _source.volume = _record.musicVolume;
            if (_track == _record.musicTrack && _source.isPlaying) return;

            var pl = Playlist();
            if (pl == null || pl.ResourcePaths.Length == 0) return;
            _track = Mathf.Clamp(_record.musicTrack, 0, pl.ResourcePaths.Length - 1);
            var clip = Resources.Load<AudioClip>(pl.ResourcePaths[_track]);
            if (clip == null) { Common.CDebug.LogError("[GPMusic] missing " + pl.ResourcePaths[_track]); return; }
            _source.clip = clip;
            _source.Play();
        }

        void OnDestroy() {
            if (Instance == this) Instance = null;
        }
    }

    /// <summary>
    /// Image overlays: loads the AdsImagePortrait scene and shows one copy of its $BG/$Image
    /// strip per picked image, each at its own height/position. Shown for the whole session.
    /// </summary>
    public class GPAdImages : MonoBehaviour {

        public static GPAdImages Instance { get; private set; }
        const string SceneName = "AdsImagePortrait";

        /// <summary>The overlay scene's own folder, in AssetDatabase form (forward slashes).</summary>
        static string Dir() {
            string path = FindScene();
            return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path).Replace('\\', '/');
        }

        /// <summary>Images available to pick — every texture next to the overlay scene.</summary>
        public static List<string> AvailableImages() {
            var result = new List<string>();
            string dir = Dir();
            if (dir == null) return result;
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Texture", new[] { dir }))
                result.Add(Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)));
            result.Sort();
            return result;
        }

        static string _scenePath;
        static string FindScene() {
            if (!string.IsNullOrEmpty(_scenePath) && File.Exists(_scenePath)) return _scenePath;
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Scene " + SceneName)) {
                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == SceneName) return _scenePath = p;
            }
            return null;
        }

        public static void Ensure(GPRecord record) {
            if (Instance == null) Instance = new GameObject("$GPAdImages").AddComponent<GPAdImages>();
            Instance._record = record;
        }

        public static void Remove() {
            if (Instance != null) {
                Instance.UnloadScene();
                Destroy(Instance.gameObject);
            }
            Instance = null;
        }

        GPRecord _record;
        Transform _strip;    // the scene's own $BG — the first overlay uses it directly
        bool _loading;
        readonly List<Transform> _live = new List<Transform>();   // [0] is _strip, extras are copies

        void Update() {
            if (_record == null) return;
            if (_strip == null) { EnsureScene(); return; }

            // one strip per overlay row: the scene's own first, copies only for extra rows
            while (_live.Count > _record.adImages.Count) {
                var last = _live[_live.Count - 1];
                _live.RemoveAt(_live.Count - 1);
                if (last == _strip) last.gameObject.SetActive(false);   // never destroy the scene's own
                else if (last != null) Destroy(last.gameObject);
            }
            while (_live.Count < _record.adImages.Count) {
                var t = _live.Count == 0 ? _strip : Instantiate(_strip.gameObject, _strip.parent).transform;
                t.gameObject.SetActive(true);
                _live.Add(t);
            }

            for (int i = 0; i < _record.adImages.Count; i++) {
                var img = _record.adImages[i];
                var strip = _live[i];
                if (strip == null) continue;

                var bgImage = strip.GetComponent<UnityEngine.UI.Image>();
                if (bgImage != null) bgImage.color = img.bg;

                var im = strip.RecursiveFindChild("$Image")?.GetComponent<UnityEngine.UI.Image>();
                if (im != null) {
                    var want = LoadSprite(img.name);
                    if (im.sprite != want) im.sprite = want;
                    im.preserveAspect = true;
                }

                var rt = strip as RectTransform;
                float h = Mathf.Clamp(img.height, 0.02f, 1f);
                float y0 = Mathf.Clamp01(img.pos) * (1f - h);
                rt.anchorMin = new Vector2(0f, y0);
                rt.anchorMax = new Vector2(1f, y0 + h);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }

        static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

        static Sprite LoadSprite(string name) {
            if (string.IsNullOrEmpty(name)) return null;
            if (_sprites.TryGetValue(name, out var cached) && cached != null) return cached;   // Update runs every frame

            Sprite found = null;
            string dir = Dir();
            if (dir != null)
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Texture " + name, new[] { dir })) {
                    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(p) != name) continue;
                    // "Sprite Mode: Multiple" textures keep their sprites as SUB-assets, so the
                    // main asset is a Texture2D — take the first sprite in the file either way
                    foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p))
                        if (asset is Sprite s) { found = s; break; }
                    break;
                }
            if (found == null) Common.CDebug.LogError($"[GPAdImages] no sprite in '{name}' (check its Texture Type)");
            _sprites[name] = found;
            return found;
        }

        void EnsureScene() {
            string path = FindScene();
            if (string.IsNullOrEmpty(path)) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            if (!scene.isLoaded) {
                if (!_loading) {
                    _loading = true;
                    UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path,
                        new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
                }
                return;
            }
            _loading = false;
            foreach (var root in scene.GetRootGameObjects()) {
                var bg = root.transform.RecursiveFindChild("$BG");
                if (bg == null) continue;
                _strip = bg;   // the scene's own strip drives the first overlay row
                break;
            }
        }

        void UnloadScene() {
            if (string.IsNullOrEmpty(_scenePath)) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(_scenePath);
            if (scene.isLoaded) UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
        }

        void OnDestroy() {
            if (Instance == this) Instance = null;
        }
    }

    /// <summary>
    /// 🎯 Pick mode: while active, clicks on the live board toggle cells in a SHOW spot key's
    /// list WITHOUT touching the game — board input is locked via MBGameplay.InputLocks and the
    /// clicks are read here, mapped to cells, and previewed live through the spotlight.
    /// </summary>
    public class GPSpotPicker : MonoBehaviour {

        public static GPSpotPicker Instance { get; private set; }
        public static bool Active => Instance != null;

        public static void Begin(GPSpotKey key) {
            if (Instance == null) {
                Instance = new GameObject("$GPSpotPicker").AddComponent<GPSpotPicker>();
                MBGameplay.instance.InputLocks++;   // clicks must not paint the board
            }
            Instance._key = key;
        }

        public static void End() {
            if (Instance == null) return;
            if (MBGameplay.instance != null) MBGameplay.instance.InputLocks--;
            Destroy(Instance.gameObject);
            Instance = null;
        }

        GPSpotKey _key;

        void Update() {
            if (_key == null || !Input.GetMouseButtonDown(0)) return;
            var gp = MBGameplay.instance;
            var cam = Camera.main;
            if (gp == null || !gp.Ready || cam == null) return;

            Vector3 wp = cam.ScreenToWorldPoint(Input.mousePosition);
            for (int r = 0; r < gp.N; r++)
                for (int c = 0; c < gp.N; c++) {
                    var cell = gp.CellAt(r, c);
                    if (cell == null) continue;
                    float half = cell.GetSize().x * cell.transform.lossyScale.x * 0.5f;
                    var d = cell.transform.position - wp;
                    if (Mathf.Abs(d.x) > half || Mathf.Abs(d.y) > half) continue;
                    _key.ToggleCell(c, r);
                    GPSpotlight.Refresh();   // the curtain holes preview the new list next frame
                    return;
                }
        }
    }
}
#endif
