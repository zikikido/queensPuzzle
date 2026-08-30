using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Timeline window for UA ad recordings (Window → GP Recorder).
    ///
    /// ● Record   — (re)starts play mode on the current level with an empty board and captures
    ///              every cell change; stopping (or exiting play mode) saves the JSON.
    /// ▶ Play     — drives the board like a real player from the playhead on; capture the Game
    ///              View for the ad video. Restarts play mode first if the level isn't up yet.
    /// ⦿ Re-record — overdub-from-playhead: the board jumps to that point and YOUR live play
    ///              records over the old keys — each one is deleted only as the head passes it,
    ///              so stopping early keeps the tail of the previous recording.
    /// ✎ Edit     — insert mode: board actions are added AT the playhead time; nothing deleted.
    ///
    /// Timeline: wheel = zoom, alt/middle-drag = pan, click/drag = playhead (video-style: the
    /// board previews that moment when the level is live), drag a marker = retime,
    /// ctrl-click = toggle-select, shift-click = range-select, Del = delete.
    ///
    /// Hand track (thin strip under the board keys): a drag is POINTs closed by an END — the
    /// first POINT is where the hand appears and presses, each next POINT a corner it drags
    /// through (rows AND columns), END where it lifts and vanishes; DOUBLE_CLICK appears at the
    /// cell and taps twice. Keys land at the playhead (cell = nearest board key, editable in the
    /// row below); a line spans each drag on the strip. The hand exists only inside a gesture.
    /// Visual only — GPHand clones the tutorial's $Hand and follows these keys during replay
    /// and scrubbing.
    ///
    /// Spotlight track (bottom strip): SHOW pops a tutorial curtain hole over a cell (holes add
    /// up), CLEAR drops the curtain — driven by MBDrapeHoles, the tutorial's own spotlight.
    /// </summary>
    public class GPRecorderWindow : EditorWindow {

        [MenuItem("QueensPuzzle/GP Recorder")]
        static void Open() => GetWindow<GPRecorderWindow>("GP Recorder");

        // A queued run: what to start once play mode is up and the board is ready.
        enum EPending { None, RecordFresh, Replay, RecordAtPlayhead, RecordVideo }

        // survives the domain reload on play-mode entry via EditorWindow serialization
        [SerializeField] GPRecord _record = new GPRecord();
        [SerializeField] string _path = "";
        [SerializeField] string _saveName = "record";
        [SerializeField] float _playhead;
        [SerializeField] float _pxPerSec = 80f;   // zoom
        [SerializeField] float _viewStart;        // seconds at the timeline's left edge (pan)
        [SerializeField] EPending _pending = EPending.None;
        // session flags (NoFail/NoWin/Hide*) live IN the record — each video carries its setup

        // reset by the domain reload — selection is not precious
        readonly System.Collections.Generic.List<GPRecordAction> _selection = new System.Collections.Generic.List<GPRecordAction>();
        GPRecordAction _dragging;   // drag anchor — the whole selection moves with it
        GPRecordAction _anchor;     // last clicked marker — shift-click selects the range from here
        // the hand and spotlight tracks mirror the same selection model
        readonly System.Collections.Generic.List<GPHandKey> _handSel = new System.Collections.Generic.List<GPHandKey>();
        GPHandKey _dragHand;    // drag anchor for the hand selection
        GPHandKey _handAnchor;  // shift-range anchor on the hand track
        readonly System.Collections.Generic.List<GPSpotKey> _spotSel = new System.Collections.Generic.List<GPSpotKey>();
        GPSpotKey _dragSpot;
        GPSpotKey _spotAnchor;
        readonly System.Collections.Generic.List<GPVoiceKey> _voiceSel = new System.Collections.Generic.List<GPVoiceKey>();
        GPVoiceKey _dragVoice;
        GPVoiceKey _voiceAnchor;
        bool _scrubbing;

        GPVoices _voices;   // the record's active voice set — reloaded when the record/voicesFile changes

        const float RulerH = 18f;
        const float HandH = 20f;    // the hand strip under the board band — thin, it never stacks
        const float SpotH = 20f;    // the spotlight strip under the hand strip
        const float VoiceH = 20f;   // the voice strip at the bottom

        void OnEnable() => EditorApplication.playModeStateChanged += OnPlayMode;
        void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayMode;

        // Runs need MBGameplay — make sure play mode starts on the Gameplay scene, whatever is
        // open in the editor (same pattern as the Level Builder's Play).
        static bool EnsureGameplayScene() {
            const string scenePath = "Assets/Scenes/Gameplay.unity";
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == scenePath) return true;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) {
                Debug.LogError($"[GPRecorder] Scene not found: {scenePath}");
                return false;
            }
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            return true;
        }

        void OnPlayMode(PlayModeStateChange c) {
            if (c == PlayModeStateChange.ExitingPlayMode) {
                // whatever was live is over — a recording is kept, a replay is cut
                if (GPRecorder.IsRecording) { GPRecorder.End(); SaveRecord(); }
                GPReplayer.Stop();
            } else if (c == PlayModeStateChange.EnteredEditMode) {
                if (_pending != EPending.None) {   // a queued run asked for a play-mode restart
                    EditorApplication.delayCall += () => {
                        if (EnsureGameplayScene()) EditorApplication.isPlaying = true;
                        else _pending = EPending.None;
                    };
                    return;
                }
                // never leak the handover into a normal editor play
                SessionState.EraseString(GPReplayer.ReplayRecordKey);
                SessionState.SetBool(GPReplayer.FreshBoardKey, false);
            }
        }

        void Update() {
            // a queued run starts once the board finished its bloom
            // RecordVideo must not wait for Ready — the capture has to include the bloom intro
            // (GPReplayer.Play waits for the bloom internally before firing actions)
            if (_pending != EPending.None && EditorApplication.isPlaying && MBGameplay.instance != null
                && (MBGameplay.instance.Ready || _pending == EPending.RecordVideo)
                && !GPReplayer.IsReplaying && !GPRecorder.IsRecording) {
                var mode = _pending;
                _pending = EPending.None;
                switch (mode) {
                    case EPending.RecordFresh:
                        ArmFreshRecord();
                        break;
                    case EPending.Replay:
                        EnsureRecordLoaded();
                        GPReplayer.Play(_record, _playhead);
                        break;
                    case EPending.RecordAtPlayhead:
                        EnsureRecordLoaded();
                        GPReplayer.Seek(_record, _playhead);   // jump straight to the point
                        GPRecorder.Begin(_record, _playhead);
                        break;
                    case EPending.RecordVideo:
                        EnsureRecordLoaded();
                        // <recordName>.mp4 in the (git-ignored) output folder, never overwriting: _1, _2, …
                        string outDir = Path.Combine(GPRecord.Dir, "..", "GPRecorderOutput");
                        Directory.CreateDirectory(outDir);
                        string baseNoExt = Path.Combine(Path.GetFullPath(outDir), _saveName);
                        _captureOutput = baseNoExt;
                        for (int i = 1; File.Exists(_captureOutput + ".mp4"); i++) _captureOutput = baseNoExt + "_" + i;
                        GPVideoCapture.Start(_captureOutput);
                        GPReplayer.Play(_record, 0f);
                        _captureWaiting = true;
                        _replayDoneAt = 0;
                        break;
                }
            }
            GPRecorder.ConsumeOverwritten();   // overdub: old keys the record head passed are consumed
            // 🎯 Pick lives only while its single SHOW key stays selected
            if (GPSpotPicker.Active && (_spotSel.Count != 1 || _spotSel[0].kind != GPSpotKey.EKind.SHOW))
                GPSpotPicker.End();

            GPReplayer.RecordFolder = string.IsNullOrEmpty(_path) ? null : GPRecord.FolderOf(_path);
            ApplyHideFlags();

            // 🎬 Record GP: capture runs until the replay (incl. endTime) finishes
            if (_captureWaiting) {
                if (!EditorApplication.isPlaying) {
                    GPVideoCapture.Stop();
                    _captureWaiting = false;
                } else if (!GPReplayer.IsReplaying && _pending == EPending.None) {
                    if (_replayDoneAt == 0) _replayDoneAt = EditorApplication.timeSinceStartup;
                    else if (EditorApplication.timeSinceStartup - _replayDoneAt > 0.5) {
                        GPVideoCapture.Stop();
                        _captureWaiting = false;
                        _replayDoneAt = 0;
                        EditorApplication.isPlaying = false;
                        EditorUtility.RevealInFinder(_captureOutput + ".mp4");
                    }
                } else _replayDoneAt = 0;
            }
            if (_captureWaiting) Repaint();

            // the record file changed on disk (script, git, hand edit) — reload it, keeping the view
            if (_pending == EPending.None && !GPRecorder.IsRecording && !GPReplayer.IsReplaying
                && _dragging == null && _dragHand == null && _dragSpot == null && _dragVoice == null
                && !string.IsNullOrEmpty(_path) && File.Exists(_path)
                && File.GetLastWriteTimeUtc(_path) != _recordStamp) {
                PushUndo();   // Ctrl+Z can revert an external (AI/git) change
                float ph = _playhead, vs = _viewStart, zoom = _pxPerSec;
                LoadRecord(_path);
                _playhead = ph; _viewStart = vs; _pxPerSec = zoom;
                Repaint();
            }

            if (GPRecorder.IsRecording || GPReplayer.IsReplaying || GPSpotPicker.Active) Repaint();
        }

        // ---- run control ---------------------------------------------------------------

        void ArmFreshRecord() {
            _record = new GPRecord { level = GPRecord.LevelSnapshot.From(MBGameplay.instance.Level) };
            _path = "";
            _saveName = NextFreeName();   // a fresh recording NEVER inherits the loaded file's name
            ClearSelection();
            _playhead = 0f;
            GPRecorder.Begin(_record, 0f);
        }

        static string NextFreeName() {
            for (int i = 1; ; i++) {
                string name = "record_" + i;
                if (!Directory.Exists(Path.Combine(GPRecord.Dir, name)) &&
                    !File.Exists(Path.Combine(GPRecord.Dir, name + ".json")))   // legacy flat files
                    return name;
            }
        }

        void ClearSelection() {
            _selection.Clear();
            _dragging = _anchor = null;
            _handSel.Clear();
            _dragHand = _handAnchor = null;
            _spotSel.Clear();
            _dragSpot = _spotAnchor = null;
            _voiceSel.Clear();
            _dragVoice = _voiceAnchor = null;
        }

        // ---- voices --------------------------------------------------------------------

        string VoicesPath => string.IsNullOrEmpty(_path) ? null : Path.Combine(GPRecord.FolderOf(_path), _record.voicesFile);

        System.DateTime _voicesStamp;

        // reloads whenever the file on disk changes (edited by hand, git pull, generated…)
        GPVoices Voices {
            get {
                string p = VoicesPath;
                if (p == null) return null;
                var stamp = File.Exists(p) ? File.GetLastWriteTimeUtc(p) : System.DateTime.MinValue;
                if (_voices == null || stamp != _voicesStamp) {
                    _voices = GPVoices.Load(p);
                    _voicesStamp = stamp;
                }
                return _voices;
            }
        }

        void StartFreshRecordRun() {
            // recording over a record that has keys replaces it in the window — make sure
            if (_record.IsValid && (_record.actions.Count > 0 || _record.handKeys.Count > 0) &&
                !EditorUtility.DisplayDialog("GP Recorder",
                    $"Start a NEW recording?\n\nThe {_record.actions.Count} board keys and {_record.handKeys.Count} hand keys " +
                    "currently in the window are replaced. The saved file itself is not touched — the new recording " +
                    "saves under a new name.", "Record", "Cancel"))
                return;

            if (EditorApplication.isPlaying) { ArmFreshRecord(); return; }   // record the level being played
            if (!EnsureGameplayScene()) return;
            SessionState.SetBool(GPReplayer.FreshBoardKey, true);            // no saved-board restore
            _pending = EPending.RecordFresh;
            EditorApplication.isPlaying = true;
        }

        // Replay and re-record both restart play mode so the board is guaranteed fresh; the
        // record travels by file path via SessionState (LevelLoader serves its embedded level).
        void StartReplayRun(EPending mode) {
            if (!SaveRecord()) return;   // the run reads the record from its file — no file, no run
            if (!EditorApplication.isPlaying && !EnsureGameplayScene()) return;
            SessionState.SetString(GPReplayer.ReplayRecordKey, _path);
            _pending = mode;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;   // OnPlayMode relaunches
            else EditorApplication.isPlaying = true;
        }

        void EnsureRecordLoaded() {
            if (!_record.IsValid && File.Exists(_path)) _record = GPRecord.Load(_path);
        }

        // ---- files ---------------------------------------------------------------------

        bool SaveRecord() {
            if (!_record.IsValid) return false;
            if (string.IsNullOrWhiteSpace(_saveName)) _saveName = NextFreeName();
            string target = Path.GetFullPath(Path.Combine(GPRecord.Dir, _saveName, "record.json"));
            // saving over a DIFFERENT existing file than this record's own needs an OK; re-saving
            // the file the record was loaded from (or already saved to) is a normal save.
            // Compare normalized — the Load file panel returns forward slashes.
            bool samePath = !string.IsNullOrEmpty(_path) &&
                string.Equals(Path.GetFullPath(_path), target, System.StringComparison.OrdinalIgnoreCase);
            if (!samePath && File.Exists(target) &&
                !EditorUtility.DisplayDialog("GP Recorder",
                    $"Override the existing record \"{_saveName}\"?", "Override", "Cancel"))
                return false;
            _path = target;
            _record.Save(_path);
            _recordStamp = File.GetLastWriteTimeUtc(_path);   // our own write is not a "change"
            _savedJson = JsonUtility.ToJson(_record);         // baseline for the dirty dot
            return true;
        }

        System.DateTime _recordStamp;
        [SerializeField] string _savedJson = "";   // the record as last written/loaded — drives the dirty dot

        void LoadRecord(string path) {
            if (GPRecorder.IsRecording) { GPRecorder.End(); SaveRecord(); }   // ✎ Edit may still be on
            if (Path.GetFullPath(path) != _path) { _undoStack.Clear(); _redoStack.Clear(); }   // other record
            _record = GPRecord.Load(path);
            _path = Path.GetFullPath(path);
            _recordStamp = File.GetLastWriteTimeUtc(_path);
            _savedJson = JsonUtility.ToJson(_record);
            _saveName = GPRecord.NameOf(path);
            _voices = null;
            ClearSelection();
            _playhead = _viewStart = 0f;
        }

        // ---- GUI -----------------------------------------------------------------------

        void OnGUI() {
            GPRecorder.FailMode = _record.failMode;   // statics reset on domain reload — the record is the source
            GPRecorder.NoWin = _record.noWin;
            // ✎ Edit (insert) keeps the timeline fully editable — only a running record/replay locks it
            bool live = GPReplayer.IsReplaying || (GPRecorder.IsRecording && !GPRecorder.IsInserting);
            if (GPRecorder.IsRecording) _playhead = GPRecorder.Elapsed;
            else if (GPReplayer.IsReplaying) _playhead = GPReplayer.PlayheadTime;

            var ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.control && !live) {
                if (ev.keyCode == KeyCode.Z) { DoUndo(); ev.Use(); }
                else if (ev.keyCode == KeyCode.Y) { DoRedo(); ev.Use(); }
            }

            FilesGUI(live);
            TransportGUI();
            InfoGUI();
            TimelineGUI(live);
            SelectedGUI(live);
            LegendGUI();
            VoicesGUI();
        }

        // ---- voice lines editor: text + params per line, generated via ElevenLabs ------

        [SerializeField] bool _voicesOpen = true;

        void VoicesGUI() {
            if (!_record.IsValid || string.IsNullOrEmpty(_path)) return;   // lines live in the record's folder
            _voicesOpen = EditorGUILayout.Foldout(_voicesOpen, $"Voice lines ({_record.voicesFile})", true);
            if (!_voicesOpen) return;
            var v = Voices;
            if (v == null) return;

            GPVoiceGen.ApiKey = EditorGUILayout.PasswordField("ElevenLabs API key", GPVoiceGen.ApiKey);
            GPVoiceGen.FetchVoices();   // once per session, when the key is set
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUI.BeginChangeCheck();
                v.voiceId = EditorGUILayout.TextField($"Voice ID ({_record.voicesFile})", v.voiceId);
                if (EditorGUI.EndChangeCheck()) SaveVoices();
                string vname = GPVoiceGen.VoiceName(v.voiceId);
                GUILayout.Label(vname ?? "?", EditorStyles.miniLabel, GUILayout.Width(90f));
                if (GUILayout.Button("▾ Choose", GUILayout.Width(70f))) {
                    if (GPVoiceGen.AccountVoices == null) {
                        GPVoiceGen.FetchVoices(force: true);   // retry — list failed or not loaded yet
                        ShowNotification(new GUIContent("Loading voice list… (see Console if it fails)"));
                    } else {
                        var menu = new GenericMenu();
                        foreach (var opt in GPVoiceGen.AccountVoices) {
                            var o = opt;   // capture per item
                            // built-in voices work on every plan's API; the rest need a paid plan
                            menu.AddItem(new GUIContent(o.premade ? o.name : o.name + "  (paid plan)"),
                                o.id == v.voiceId, () => {
                                    v.voiceId = o.id;   // only the ID is saved
                                    SaveVoices();
                                    Repaint();
                                });
                        }
                        menu.AddSeparator("");
                        menu.AddItem(new GUIContent("↻ Refresh list"), false, () => GPVoiceGen.FetchVoices(force: true));
                        menu.ShowAsContext();
                    }
                }
            }

            string folder = GPRecord.FolderOf(_path);
            bool canGen = GPVoiceGen.Ready && !string.IsNullOrEmpty(v.voiceId);
            int remove = -1;
            for (int i = 0; i < v.lines.Count; i++) {
                var l = v.lines[i];
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUI.BeginChangeCheck();
                    l.name = EditorGUILayout.TextField(l.name, GUILayout.Width(70f));
                    l.text = EditorGUILayout.TextField(l.text);
                    GUILayout.Label("spd", EditorStyles.miniLabel, GUILayout.Width(24f));
                    l.speed = EditorGUILayout.FloatField(l.speed, GUILayout.Width(34f));
                    GUILayout.Label("stab", EditorStyles.miniLabel, GUILayout.Width(26f));
                    l.stability = EditorGUILayout.FloatField(l.stability, GUILayout.Width(34f));
                    GUILayout.Label("style", EditorStyles.miniLabel, GUILayout.Width(28f));
                    l.style = EditorGUILayout.FloatField(l.style, GUILayout.Width(34f));
                    if (EditorGUI.EndChangeCheck()) SaveVoices();

                    bool busy = GPVoiceGen.IsBusy(l.name);
                    bool hasFile = !string.IsNullOrEmpty(l.path) && File.Exists(Path.Combine(folder, l.path));
                    GUILayout.Label(busy ? "…" : hasFile ? "✓" : "—", GUILayout.Width(16f));
                    using (new EditorGUI.DisabledScope(!hasFile)) {
                        if (GUILayout.Button("▶", GUILayout.Width(24f)))
                            Audition(Path.Combine(folder, l.path));
                    }
                    using (new EditorGUI.DisabledScope(busy || !canGen || string.IsNullOrWhiteSpace(l.text))) {
                        if (GUILayout.Button("♪ Gen", GUILayout.Width(48f))) GenerateLine(l);
                    }
                    if (GUILayout.Button("✖", GUILayout.Width(22f))) remove = i;
                }
            }
            if (remove >= 0) { v.lines.RemoveAt(remove); SaveVoices(); }

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("+ Line", GUILayout.Width(60f))) {
                    v.lines.Add(new GPVoiceLine { name = "line" + (v.lines.Count + 1), text = "" });
                    SaveVoices();
                }
                using (new EditorGUI.DisabledScope(!canGen)) {
                    if (GUILayout.Button("♪ Generate All", GUILayout.Width(100f)))
                        foreach (var l in v.lines)
                            if (!string.IsNullOrWhiteSpace(l.text)) GenerateLine(l);
                }
                GUILayout.FlexibleSpace();
                if (!canGen) GUILayout.Label("set API key + Voice ID to generate", EditorStyles.miniLabel);
            }
        }

        // .wav plays inline (SoundPlayer); .mp3 opens in the default player — SoundPlayer is WAV-only
        static void Audition(string path) {
            if (path.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase))
                new System.Media.SoundPlayer(path).Play();
            else
                EditorUtility.OpenWithDefaultApp(path);
        }

        void SaveVoices() {
            if (VoicesPath == null || _voices == null) return;
            _voices.Save(VoicesPath);
            _voicesStamp = File.GetLastWriteTimeUtc(VoicesPath);   // our own write is not a "change"
        }

        // wavs of set "will.voices.json" land in Voices/will/ (legacy "voices1.json" → Voices/1/) —
        // swapping the set swaps the folder, the timeline keys never change
        void GenerateLine(GPVoiceLine l) {
            string n = _record.voicesFile;
            string setId = n.EndsWith(".voices.json")
                ? n.Substring(0, n.Length - ".voices.json".Length)
                : Path.GetFileNameWithoutExtension(n).Replace("voices", "");
            if (string.IsNullOrEmpty(setId)) setId = "A";
            GPVoiceGen.Generate(l, Voices.voiceId, GPRecord.FolderOf(_path), "Voices/" + setId, () => { SaveVoices(); Repaint(); });
        }


        // ---- header: three clean toolbar rows ------------------------------------------
        //  1  record ▾ · Save · New          ↶ ↷ · voices ▾
        //  2  transport (state-aware)                     playhead
        //  3  + Hand ▾ · + Spot ▾ · + Voice   NoFail NoWin · Hide ▾

        void FilesGUI(bool live) {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            using (new EditorGUI.DisabledScope(live)) {
                var paths = GPRecord.ListPaths();
                var names = paths.Select(GPRecord.NameOf).ToArray();
                int cur = System.Array.IndexOf(paths, _path);
                int pick = EditorGUILayout.Popup(cur, names, EditorStyles.toolbarPopup, GUILayout.Width(180f));
                if (pick != cur && pick >= 0) LoadRecord(paths[pick]);

                using (new EditorGUI.DisabledScope(!_record.IsValid)) {
                    // unsaved edits get a red dot — the record json is the source of truth for
                    // replay runs, so knowing when it's stale matters
                    bool dirty = _record.IsValid && JsonUtility.ToJson(_record) != _savedJson;
                    var save = GUILayoutUtility.GetRect(new GUIContent(dirty ? "Save •" : "Save"),
                        EditorStyles.toolbarButton, GUILayout.Width(52f));
                    if (GUI.Button(save, "Save", EditorStyles.toolbarButton)) SaveRecord();
                    if (dirty) {
                        var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.35f, 0.35f) } };
                        GUI.Label(new Rect(save.xMax - 13f, save.y - 1f, 12f, save.height), "•", style);
                    }
                }
                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(40f))) {
                    if (GPRecorder.IsRecording) { GPRecorder.End(); SaveRecord(); }   // ✎ Edit may still be on
                    _record = new GPRecord();
                    _path = "";
                    _voices = null;
                    ClearSelection();
                    _playhead = _viewStart = 0f;
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_undoStack.Count == 0)) {
                    if (GUILayout.Button(new GUIContent("↶", "Undo (Ctrl+Z)"), EditorStyles.toolbarButton, GUILayout.Width(28f))) DoUndo();
                }
                using (new EditorGUI.DisabledScope(_redoStack.Count == 0)) {
                    if (GUILayout.Button(new GUIContent("↷", "Redo (Ctrl+Y)"), EditorStyles.toolbarButton, GUILayout.Width(28f))) DoRedo();
                }

                GUILayout.Space(8f);
                GUILayout.Label("voices", EditorStyles.miniLabel, GUILayout.Width(38f));
                if (string.IsNullOrEmpty(_path)) {
                    GUILayout.Label("—", EditorStyles.miniLabel, GUILayout.Width(90f));
                } else {
                    // convention: <voiceName>.voices.json (legacy voicesN.json still listed)
                    var files = Directory.GetFiles(GPRecord.FolderOf(_path), "*.json")
                        .Select(Path.GetFileName)
                        .Where(n => n.EndsWith(".voices.json") || (n.StartsWith("voices") && n != "record.json"))
                        .ToArray();
                    int vcur = System.Array.IndexOf(files, _record.voicesFile);
                    int vpick = EditorGUILayout.Popup(vcur, files, EditorStyles.toolbarPopup, GUILayout.Width(110f));
                    if (vpick != vcur && vpick >= 0) {
                        _record.voicesFile = files[vpick];   // swap the whole voice set
                        _voices = null;
                    }
                }
            }
        }

        void TransportGUI() {
            // row 2 — the transport: a media-player strip showing only what is usable right now
            const float H = 26f;
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(4f);
                if (GPRecorder.IsInserting) {
                    GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
                    if (GUILayout.Button("\u2714 Done Editing", GUILayout.Width(120f), GUILayout.Height(H))) { GPRecorder.End(); SaveRecord(); }
                    GUI.backgroundColor = Color.white;
                    GUILayout.Label($"  inserting board actions at {_playhead:0.00}s \u2014 play the game", EditorStyles.boldLabel, GUILayout.Height(H));
                } else if (GPRecorder.IsRecording) {
                    GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
                    if (GUILayout.Button("\u25a0 Stop", GUILayout.Width(90f), GUILayout.Height(H))) { GPRecorder.End(); SaveRecord(); }
                    GUI.backgroundColor = Color.white;
                    GUILayout.Label($"  \u25cf REC {_playhead:0.00}s", EditorStyles.boldLabel, GUILayout.Height(H));
                } else if (GPReplayer.IsReplaying) {
                    if (GUILayout.Button("\u25a0 Stop", GUILayout.Width(90f), GUILayout.Height(H))) GPReplayer.Stop();
                    GUILayout.Label($"  \u25b6 {_playhead:0.00}s", EditorStyles.boldLabel, GUILayout.Height(H));
                } else if (_pending != EPending.None) {
                    GUILayout.Label("starting\u2026", EditorStyles.boldLabel, GUILayout.Height(H));
                } else {
                    GUI.backgroundColor = new Color(1f, 0.72f, 0.72f);
                    if (GUILayout.Button(new GUIContent(" \u25cf Record ",
                            "Record a NEW take: restarts play mode with an empty board and captures your play"),
                            GUILayout.Width(82f), GUILayout.Height(H)))
                        StartFreshRecordRun();
                    GUI.backgroundColor = Color.white;
                    GUILayout.Space(10f);
                    using (new EditorGUI.DisabledScope(!_record.IsValid)) {
                        if (GUILayout.Button(new GUIContent("\u23ee", "Playhead to 0"), GUILayout.Width(32f), GUILayout.Height(H))) {
                            _playhead = 0f;
                            ScrubPreview();
                        }
                        if (GUILayout.Button(new GUIContent(" \u25b6 Play ",
                                "Play from the playhead (restarts play mode when the level isn't up)"),
                                GUILayout.Width(70f), GUILayout.Height(H))) {
                            if (GPReplayer.CanDrive(_record)) GPReplayer.Play(_record, _playhead);
                            else StartReplayRun(EPending.Replay);
                        }
                        // overdub: old keys are consumed only as the head passes them
                        if (GUILayout.Button(new GUIContent(" \u29bf Re-record ",
                                "Overdub from the playhead: your live play records over the old keys as the head passes them"),
                                GUILayout.Width(100f), GUILayout.Height(H))) {
                            if (GPReplayer.CanDrive(_record)) {
                                GPReplayer.Seek(_record, _playhead);
                                GPRecorder.Begin(_record, _playhead);
                            } else StartReplayRun(EPending.RecordAtPlayhead);
                        }
                        using (new EditorGUI.DisabledScope(!GPReplayer.CanDrive(_record))) {
                            if (GUILayout.Button(new GUIContent(" \u270e Insert ",
                                    "Insert board actions at the playhead (needs the level live \u2014 press \u25b6 once first)"),
                                    GUILayout.Width(72f), GUILayout.Height(H)))
                                GPRecorder.BeginInsert(_record, _playhead);
                        }
                    }
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(!_record.IsValid)) {
                        GUI.backgroundColor = new Color(0.75f, 1f, 0.75f);
                        if (GUILayout.Button(new GUIContent(" 🎬 Record GP ",
                                "Capture the final video: restart play mode, record the Game View (with voices) from 0 " +
                                "to the END marker, save <record name>.mp4 into the record's folder"),
                                GUILayout.Width(110f), GUILayout.Height(H)))
                            StartRecordVideo();
                        GUI.backgroundColor = Color.white;
                    }
                    GUILayout.Space(10f);
                    GUILayout.Label($"{_playhead:0.00}s", EditorStyles.boldLabel, GUILayout.Height(H));
                    GUILayout.Space(6f);
                }
            }

            bool idle = !GPRecorder.IsRecording && !GPReplayer.IsReplaying && _pending == EPending.None;
            if (!idle) return;

            // row 3 — add keys, and everything rare inside ONE Options menu
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            using (new EditorGUI.DisabledScope(!_record.IsValid)) {
                if (GUILayout.Button(new GUIContent("+ Hand \u25be",
                        "Hand keys at the playhead (select a board key first for perfect cell+time sync)"),
                        EditorStyles.toolbarDropDown, GUILayout.Width(70f))) {
                    var m = new GenericMenu();
                    m.AddItem(new GUIContent("Point \u2014 press / drag corner"), false, () => AddHandKey(GPHandKey.EKind.POINT));
                    m.AddItem(new GUIContent("End \u2014 lift the finger"), false, () => AddHandKey(GPHandKey.EKind.END_MOVE));
                    m.AddItem(new GUIContent("Double Tap"), false, () => AddHandKey(GPHandKey.EKind.DOUBLE_CLICK));
                    m.ShowAsContext();
                }
                if (GUILayout.Button(new GUIContent("+ Spot \u25be",
                        "Spotlight keys at the playhead. Show = curtain holes over cells (refine with Pick), Clear = curtain drops"),
                        EditorStyles.toolbarDropDown, GUILayout.Width(66f))) {
                    var m = new GenericMenu();
                    m.AddItem(new GUIContent("Show \u2014 light cells"), false, () => AddSpotKey(GPSpotKey.EKind.SHOW));
                    m.AddItem(new GUIContent("Clear \u2014 curtain down"), false, () => AddSpotKey(GPSpotKey.EKind.CLEAR));
                    m.ShowAsContext();
                }
                if (GUILayout.Button(new GUIContent("+ Voice",
                        "Add a voice key at the playhead \u2014 pick which line it plays in the row below"),
                        EditorStyles.toolbarButton, GUILayout.Width(56f)))
                    AddVoiceKey();

                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("Options \u25be",
                        "Session flags: fail/win behavior, hidden game UI, subtitles overlay \u2014 all saved in the record"),
                        EditorStyles.toolbarDropDown, GUILayout.Width(76f))) {
                    var m = new GenericMenu();
                    m.AddItem(new GUIContent("Wrong move/No cost \u2014 unlimited wrong tries"),
                        _record.failMode == GPRecord.EFailMode.NoCost, () => _record.failMode = GPRecord.EFailMode.NoCost);
                    m.AddItem(new GUIContent("Wrong move/Loses bones, no fail popup"),
                        _record.failMode == GPRecord.EFailMode.NoPopup, () => _record.failMode = GPRecord.EFailMode.NoPopup);
                    m.AddItem(new GUIContent("Wrong move/Normal \u2014 bones and fail popup"),
                        _record.failMode == GPRecord.EFailMode.Normal, () => _record.failMode = GPRecord.EFailMode.Normal);
                    m.AddItem(new GUIContent("No Win popups \u2014 session stays alive"), _record.noWin, () => _record.noWin = !_record.noWin);
                    m.AddSeparator("");
                    m.AddItem(new GUIContent("Hide UI/Top (back, title, settings)"), _record.hideTop, () => _record.hideTop = !_record.hideTop);
                    m.AddItem(new GUIContent("Hide UI/Rule cards"), _record.hideRules, () => _record.hideRules = !_record.hideRules);
                    m.AddItem(new GUIContent("Hide UI/Boosters"), _record.hideBoosters, () => _record.hideBoosters = !_record.hideBoosters);
                    m.AddItem(new GUIContent("Hide UI/Counters (puppies, bones)"), _record.hideCounters, () => _record.hideCounters = !_record.hideCounters);
                    m.AddSeparator("");
                    m.AddItem(new GUIContent("Subtitles overlay"), _record.showAdText, () => _record.showAdText = !_record.showAdText);
                    // an overlay is added empty-ish: image, height and position are all params,
                    // picked in its own row below (nothing about the available list is stored)
                    m.AddItem(new GUIContent("Add image overlay"), false, () => {
                        PushUndo();
                        var names = GPAdImages.AvailableImages();
                        _record.adImages.Add(new GPAdImage { name = names.Count > 0 ? names[0] : "" });
                    });
                    m.AddSeparator("");
                    m.AddItem(new GUIContent("Capture/Quality: Low"), GPVideoCapture.Quality == 0, () => GPVideoCapture.Quality = 0);
                    m.AddItem(new GUIContent("Capture/Quality: Medium"), GPVideoCapture.Quality == 1, () => GPVideoCapture.Quality = 1);
                    m.AddItem(new GUIContent("Capture/Quality: High"), GPVideoCapture.Quality == 2, () => GPVideoCapture.Quality = 2);
                    m.AddSeparator("Capture/");
                    m.AddItem(new GUIContent("Capture/30 FPS"), GPVideoCapture.Fps == 30, () => GPVideoCapture.Fps = 30);
                    m.AddItem(new GUIContent("Capture/60 FPS"), GPVideoCapture.Fps == 60, () => GPVideoCapture.Fps = 60);
                    m.AddSeparator("Capture/");
                    m.AddItem(new GUIContent("Capture/1080 × 1920  (portrait)"),
                        GPVideoCapture.Width == 1080 && GPVideoCapture.Height == 1920,
                        () => { GPVideoCapture.Width = 1080; GPVideoCapture.Height = 1920; });
                    m.AddItem(new GUIContent("Capture/720 × 1280  (portrait small)"),
                        GPVideoCapture.Width == 720 && GPVideoCapture.Height == 1280,
                        () => { GPVideoCapture.Width = 720; GPVideoCapture.Height = 1280; });
                    m.AddItem(new GUIContent("Capture/1080 × 1080  (square)"),
                        GPVideoCapture.Width == 1080 && GPVideoCapture.Height == 1080,
                        () => { GPVideoCapture.Width = 1080; GPVideoCapture.Height = 1080; });
                    m.AddItem(new GUIContent("Capture/1920 × 1080  (landscape)"),
                        GPVideoCapture.Width == 1920 && GPVideoCapture.Height == 1080,
                        () => { GPVideoCapture.Width = 1920; GPVideoCapture.Height = 1080; });
                    m.ShowAsContext();
                }
            }

            // row 4 — subtitle settings, present only while the overlay is on
            if (_record.showAdText && _record.IsValid) {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                    GUILayout.Label("Subtitles", EditorStyles.miniBoldLabel, GUILayout.Width(54f));
                    GUILayout.Label("BG", EditorStyles.miniLabel, GUILayout.Width(20f));
                    _record.adTextBg = EditorGUILayout.ColorField(_record.adTextBg, GUILayout.Width(44f));
                    GUILayout.Label("Text", EditorStyles.miniLabel, GUILayout.Width(26f));
                    _record.adTextColor = EditorGUILayout.ColorField(_record.adTextColor, GUILayout.Width(44f));
                    GUILayout.Space(10f);
                    GUILayout.Label("Height", EditorStyles.miniLabel, GUILayout.Width(38f));
                    _record.adTextHeight = GUILayout.HorizontalSlider(_record.adTextHeight, 0.03f, 0.5f, GUILayout.Width(90f));
                    GUILayout.Space(10f);
                    GUILayout.Label("Pos", EditorStyles.miniLabel, GUILayout.Width(24f));
                    _record.adTextPos = GUILayout.HorizontalSlider(_record.adTextPos, 0f, 1f, GUILayout.Width(90f));
                    GUILayout.FlexibleSpace();
                }
            }

            // one row per picked image overlay — its own height/position
            for (int i = 0; i < _record.adImages.Count; i++) {
                var img = _record.adImages[i];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                    GUILayout.Label("Image", EditorStyles.miniLabel, GUILayout.Width(38f));
                    // which image is a param like the others — swap it without losing the placement
                    var names = GPAdImages.AvailableImages().ToArray();
                    int cur = System.Array.IndexOf(names, img.name);
                    int pick = EditorGUILayout.Popup(cur, names, EditorStyles.toolbarPopup, GUILayout.Width(110f));
                    if (pick != cur && pick >= 0) {
                        PushUndo();
                        _record.StoreImagePreset(img);                 // keep this image's placement
                        _record.ApplyImagePreset(img, names[pick]);    // and load the new one's
                    }
                    GUILayout.Label("BG", EditorStyles.miniLabel, GUILayout.Width(20f));
                    EditorGUI.BeginChangeCheck();
                    img.bg = EditorGUILayout.ColorField(img.bg, GUILayout.Width(44f));
                    GUILayout.Space(10f);
                    GUILayout.Label("Height", EditorStyles.miniLabel, GUILayout.Width(38f));
                    img.height = GUILayout.HorizontalSlider(img.height, 0.02f, 1f, GUILayout.Width(90f));
                    GUILayout.Space(10f);
                    GUILayout.Label("Pos", EditorStyles.miniLabel, GUILayout.Width(24f));
                    img.pos = GUILayout.HorizontalSlider(img.pos, 0f, 1f, GUILayout.Width(90f));
                    if (EditorGUI.EndChangeCheck()) _record.StoreImagePreset(img);   // tuned → remember
                    GUILayout.Space(10f);
                    if (GUILayout.Button("✖", EditorStyles.toolbarButton, GUILayout.Width(24f))) {
                        PushUndo();
                        _record.adImages.RemoveAt(i);
                        break;
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        void InfoGUI() {
            if (!_record.IsValid) {
                EditorGUILayout.HelpBox("No recording. ● Record starts play mode on the current level " +
                                        "with an empty board and captures your play. Or pick a saved file above.", MessageType.Info);
                return;
            }
            GUILayout.Label($"level {_record.level.size}x{_record.level.size}   " +
                            $"{_record.actions.Count} actions   {_record.Duration:0.00}s", EditorStyles.miniLabel);

            // a broken voice key would silently play nothing in the video — surface it loudly
            if (!string.IsNullOrEmpty(_path)) {
                var problems = _record.ValidateVoices(_path, Voices);
                if (problems.Count > 0)
                    EditorGUILayout.HelpBox("Voice problems:\n- " + string.Join("\n- ", problems), MessageType.Error);
            }
        }

        void TimelineGUI(bool live) {
            Rect r = GUILayoutUtility.GetRect(100, 100000, 210, 210, GUILayout.ExpandWidth(true));
            var e = Event.current;

            // zoom around the mouse / pan
            if (r.Contains(e.mousePosition)) {
                if (e.type == EventType.ScrollWheel) {
                    float tAtMouse = XToTime(r, e.mousePosition.x);
                    _pxPerSec = Mathf.Clamp(_pxPerSec * (e.delta.y > 0 ? 0.85f : 1.18f), 4f, 2000f);
                    _viewStart = Mathf.Max(0f, tAtMouse - (e.mousePosition.x - r.x) / _pxPerSec);
                    e.Use();
                } else if (e.type == EventType.MouseDrag && (e.button == 2 || (e.button == 0 && e.alt))) {
                    _viewStart = Mathf.Max(0f, _viewStart - e.delta.x / _pxPerSec);
                    e.Use();
                }
            }

            if (!live && _record.IsValid) {
                bool multiKey = e.control || e.command || e.shift;
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && r.Contains(e.mousePosition)
                    && e.mousePosition.y < r.y + RulerH
                    && Mathf.Abs(TimeToX(r, _record.Duration) - e.mousePosition.x) < 7f) {
                    PushUndo();
                    _dragEndKey = true;   // the END marker lives on the ruler
                    GUI.FocusControl(null);
                    e.Use();
                } else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && r.Contains(e.mousePosition)) {
                    PushUndo();   // pre-drag state; a click that changes nothing is deduped away
                    var hit = HitMarker(r, e.mousePosition);
                    if (hit != null) {
                        _handSel.Clear(); _dragHand = _handAnchor = null;
                        _spotSel.Clear(); _dragSpot = _spotAnchor = null;
                        _voiceSel.Clear(); _dragVoice = _voiceAnchor = null;
                        if (e.shift && _anchor != null && _record.actions.Contains(_anchor)) {
                            // shift-click: select the whole range between the anchor and here
                            float lo = Mathf.Min(_anchor.time, hit.time), hi = Mathf.Max(_anchor.time, hit.time);
                            _selection.Clear();
                            _selection.AddRange(_record.actions.Where(a => a.time >= lo && a.time <= hi));
                            _dragging = null;
                        } else if (e.control || e.command) {   // ctrl-click: toggle one, no drag
                            if (!_selection.Remove(hit)) _selection.Add(hit);
                            _anchor = hit;
                            _dragging = null;
                        } else {
                            // plain click on an already-selected marker keeps the group (group drag)
                            if (!_selection.Contains(hit)) { _selection.Clear(); _selection.Add(hit); }
                            _anchor = hit;
                            _dragging = hit;
                        }
                    } else if (HitHandKey(r, e.mousePosition) is GPHandKey hhit) {
                        _selection.Clear(); _dragging = _anchor = null;
                        _spotSel.Clear(); _dragSpot = _spotAnchor = null;
                        _voiceSel.Clear(); _dragVoice = _voiceAnchor = null;
                        if (e.shift && _handAnchor != null && _record.handKeys.Contains(_handAnchor)) {
                            // shift-click: select the whole range between the anchor and here
                            float lo = Mathf.Min(_handAnchor.time, hhit.time), hi = Mathf.Max(_handAnchor.time, hhit.time);
                            _handSel.Clear();
                            _handSel.AddRange(_record.handKeys.Where(k => k.time >= lo && k.time <= hi));
                            _dragHand = null;
                        } else if (e.control || e.command) {   // ctrl-click: toggle one, no drag
                            if (!_handSel.Remove(hhit)) _handSel.Add(hhit);
                            _handAnchor = hhit;
                            _dragHand = null;
                        } else {
                            // plain click on an already-selected key keeps the group (group drag)
                            if (!_handSel.Contains(hhit)) { _handSel.Clear(); _handSel.Add(hhit); }
                            _handAnchor = hhit;
                            _dragHand = hhit;
                        }
                    } else if (HitVoiceKey(r, e.mousePosition) is GPVoiceKey voiceHit) {
                        _selection.Clear(); _dragging = _anchor = null;
                        _handSel.Clear(); _dragHand = _handAnchor = null;
                        _spotSel.Clear(); _dragSpot = _spotAnchor = null;
                        if (e.shift && _voiceAnchor != null && _record.voiceKeys.Contains(_voiceAnchor)) {
                            float lo = Mathf.Min(_voiceAnchor.time, voiceHit.time), hi = Mathf.Max(_voiceAnchor.time, voiceHit.time);
                            _voiceSel.Clear();
                            _voiceSel.AddRange(_record.voiceKeys.Where(k => k.time >= lo && k.time <= hi));
                            _dragVoice = null;
                        } else if (e.control || e.command) {
                            if (!_voiceSel.Remove(voiceHit)) _voiceSel.Add(voiceHit);
                            _voiceAnchor = voiceHit;
                            _dragVoice = null;
                        } else {
                            if (!_voiceSel.Contains(voiceHit)) { _voiceSel.Clear(); _voiceSel.Add(voiceHit); }
                            _voiceAnchor = voiceHit;
                            _dragVoice = voiceHit;
                        }
                    } else if (HitSpotKey(r, e.mousePosition) is GPSpotKey spotHit) {
                        _selection.Clear(); _dragging = _anchor = null;
                        _handSel.Clear(); _dragHand = _handAnchor = null;
                        _voiceSel.Clear(); _dragVoice = _voiceAnchor = null;
                        if (e.shift && _spotAnchor != null && _record.spotKeys.Contains(_spotAnchor)) {
                            float lo = Mathf.Min(_spotAnchor.time, spotHit.time), hi = Mathf.Max(_spotAnchor.time, spotHit.time);
                            _spotSel.Clear();
                            _spotSel.AddRange(_record.spotKeys.Where(k => k.time >= lo && k.time <= hi));
                            _dragSpot = null;
                        } else if (e.control || e.command) {
                            if (!_spotSel.Remove(spotHit)) _spotSel.Add(spotHit);
                            _spotAnchor = spotHit;
                            _dragSpot = null;
                        } else {
                            if (!_spotSel.Contains(spotHit)) { _spotSel.Clear(); _spotSel.Add(spotHit); }
                            _spotAnchor = spotHit;
                            _dragSpot = spotHit;
                        }
                    } else if (!multiKey) {
                        ClearSelection();
                        _scrubbing = true;
                        _playhead = Mathf.Max(0f, XToTime(r, e.mousePosition.x));
                        ScrubPreview();
                    }
                    GUI.FocusControl(null);
                    e.Use();
                } else if (e.type == EventType.MouseDrag && e.button == 0) {
                    if (_dragEndKey) {
                        _record.endTime = Mathf.Max(0f, XToTime(r, e.mousePosition.x));
                        e.Use();
                    } else if (_dragging != null) {
                        // move the whole selection by the anchor's delta, nothing below 0
                        float delta = Mathf.Max(0f, XToTime(r, e.mousePosition.x)) - _dragging.time;
                        foreach (var s in _selection) delta = Mathf.Max(delta, -s.time);
                        foreach (var s in _selection) s.time += delta;
                        e.Use();
                    } else if (_dragHand != null) {
                        // move the whole hand selection by the anchor's delta, nothing below 0
                        float delta = Mathf.Max(0f, XToTime(r, e.mousePosition.x)) - _dragHand.time;
                        foreach (var s in _handSel) delta = Mathf.Max(delta, -s.time);
                        foreach (var s in _handSel) s.time += delta;
                        e.Use();
                    } else if (_dragSpot != null) {
                        float delta = Mathf.Max(0f, XToTime(r, e.mousePosition.x)) - _dragSpot.time;
                        foreach (var s in _spotSel) delta = Mathf.Max(delta, -s.time);
                        foreach (var s in _spotSel) s.time += delta;
                        e.Use();
                    } else if (_dragVoice != null) {
                        float delta = Mathf.Max(0f, XToTime(r, e.mousePosition.x)) - _dragVoice.time;
                        foreach (var s in _voiceSel) delta = Mathf.Max(delta, -s.time);
                        foreach (var s in _voiceSel) s.time += delta;
                        e.Use();
                    } else if (_scrubbing) { _playhead = Mathf.Max(0f, XToTime(r, e.mousePosition.x)); ScrubPreview(); e.Use(); }
                } else if (e.type == EventType.MouseUp && e.button == 0) {
                    _dragEndKey = false;
                    if (_dragging != null) { _record.Sort(); _dragging = null; e.Use(); }
                    if (_dragHand != null) { _record.Sort(); _dragHand = null; ScrubPreview(); e.Use(); }
                    if (_dragSpot != null) { _record.Sort(); _dragSpot = null; ScrubPreview(); e.Use(); }
                    if (_dragVoice != null) { _record.Sort(); _dragVoice = null; e.Use(); }
                    _scrubbing = false;
                } else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete
                           && (_selection.Count > 0 || _handSel.Count > 0 || _spotSel.Count > 0 || _voiceSel.Count > 0)) {
                    PushUndo();
                    foreach (var s in _selection) _record.actions.Remove(s);
                    foreach (var s in _handSel) _record.handKeys.Remove(s);
                    foreach (var s in _spotSel) _record.spotKeys.Remove(s);
                    foreach (var s in _voiceSel) _record.voiceKeys.Remove(s);
                    ClearSelection();
                    ScrubPreview();
                    e.Use();
                }
            }

            // scroll handle — thumb size = the visible window, range = the whole recording (+tail)
            float visibleSec = r.width / _pxPerSec;
            float totalSec = Mathf.Max((_record.IsValid ? _record.Duration : 0f) + 5f, _viewStart + visibleSec);
            _viewStart = GUILayout.HorizontalScrollbar(_viewStart, visibleSec, 0f, totalSec);

            if (e.type != EventType.Repaint) return;

            // keep a live playhead in view
            if (live && TimeToX(r, _playhead) > r.xMax - 40f)
                _viewStart = Mathf.Max(0f, _playhead - (r.width * 0.8f) / _pxPerSec);

            EditorGUI.DrawRect(r, new Color(0.13f, 0.13f, 0.13f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, RulerH), new Color(0.18f, 0.18f, 0.18f));

            // ruler: tick step from the zoom so labels stay ~60px apart
            float step = new[] { 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f }
                .FirstOrDefault(s => s * _pxPerSec >= 60f);
            if (step == 0f) step = 60f;
            for (float t = Mathf.Floor(_viewStart / step) * step; ; t += step) {
                float x = TimeToX(r, t);
                if (x > r.xMax) break;
                if (x < r.x) continue;
                EditorGUI.DrawRect(new Rect(x, r.y, 1f, r.height), new Color(1f, 1f, 1f, 0.07f));
                GUI.Label(new Rect(x + 2f, r.y, 60f, RulerH), t.ToString("0.##") + "s", EditorStyles.miniLabel);
            }

            // markers — same-time (or visually overlapping) keys stack vertically so each stays visible
            if (_record.IsValid) {
                foreach (var m in LayoutMarkers(r)) {
                    if (m.rect.xMax < r.x || m.rect.x > r.xMax) continue;
                    if (_selection.Contains(m.a))
                        EditorGUI.DrawRect(new Rect(m.rect.x - 2f, m.rect.y - 2f, m.rect.width + 4f, m.rect.height + 4f), Color.white);
                    EditorGUI.DrawRect(m.rect, ColorOf(m.a.to));
                    if (m.clusterFirst && m.clusterSize > 2)
                        GUI.Label(new Rect(m.rect.x + 7f, m.rect.y - 2f, 34f, 14f), "×" + m.clusterSize, EditorStyles.miniLabel);
                }

                // hand strip — thin single row (one hand, one key per moment)
                var hs = HandStrip(r);
                EditorGUI.DrawRect(hs, new Color(0.17f, 0.17f, 0.17f));
                EditorGUI.DrawRect(new Rect(hs.x, hs.y - 1f, hs.width, 1f), new Color(1f, 1f, 1f, 0.12f));
                // a line spans each drag: from its first POINT to its END
                float dragStartX = float.NaN;
                foreach (var k in _record.handKeys) {
                    float x = TimeToX(r, k.time);
                    if (k.kind == GPHandKey.EKind.POINT) { if (float.IsNaN(dragStartX)) dragStartX = x; }
                    else {
                        if (k.kind == GPHandKey.EKind.END_MOVE && !float.IsNaN(dragStartX)) {
                            float x0 = Mathf.Max(Mathf.Min(dragStartX, x), r.x), x1 = Mathf.Min(Mathf.Max(dragStartX, x), r.xMax);
                            if (x1 > x0) EditorGUI.DrawRect(new Rect(x0, hs.center.y - 1f, x1 - x0, 2f), new Color(0.3f, 0.85f, 0.45f, 0.5f));
                        }
                        dragStartX = float.NaN;   // END closes the drag, a DOUBLE_CLICK cuts it
                    }
                }
                foreach (var k in _record.handKeys) {
                    float x = TimeToX(r, k.time);
                    if (x < r.x - 4f || x > r.xMax + 4f) continue;
                    var m = new Rect(x - 3f, hs.y + 2f, 6f, hs.height - 4f);
                    if (_handSel.Contains(k)) EditorGUI.DrawRect(new Rect(m.x - 2f, m.y - 2f, m.width + 4f, m.height + 4f), Color.white);
                    EditorGUI.DrawRect(m, HandColorOf(k.kind));
                }

                // spotlight strip — a line spans each curtain-up stretch (first SHOW → CLEAR)
                var ss = SpotStrip(r);
                EditorGUI.DrawRect(ss, new Color(0.16f, 0.15f, 0.19f));
                EditorGUI.DrawRect(new Rect(ss.x, ss.y - 1f, ss.width, 1f), new Color(1f, 1f, 1f, 0.12f));
                float spotStartX = float.NaN;
                foreach (var k in _record.spotKeys) {
                    float x = TimeToX(r, k.time);
                    if (k.kind == GPSpotKey.EKind.SHOW) { if (float.IsNaN(spotStartX)) spotStartX = x; }
                    else {
                        if (!float.IsNaN(spotStartX)) {
                            float x0 = Mathf.Max(Mathf.Min(spotStartX, x), r.x), x1 = Mathf.Min(Mathf.Max(spotStartX, x), r.xMax);
                            if (x1 > x0) EditorGUI.DrawRect(new Rect(x0, ss.center.y - 1f, x1 - x0, 2f), new Color(0.72f, 0.50f, 1f, 0.5f));
                        }
                        spotStartX = float.NaN;
                    }
                }
                foreach (var k in _record.spotKeys) {
                    float x = TimeToX(r, k.time);
                    if (x < r.x - 4f || x > r.xMax + 4f) continue;
                    var m = new Rect(x - 3f, ss.y + 2f, 6f, ss.height - 4f);
                    if (_spotSel.Contains(k)) EditorGUI.DrawRect(new Rect(m.x - 2f, m.y - 2f, m.width + 4f, m.height + 4f), Color.white);
                    EditorGUI.DrawRect(m, SpotColorOf(k.kind));
                    if (k.kind == GPSpotKey.EKind.SHOW && k.Count > 1)
                        GUI.Label(new Rect(m.x + 6f, ss.y + 1f, 32f, 14f), "×" + k.Count, EditorStyles.miniLabel);
                }

                // voice strip — each key shows its line name
                var vs = VoiceStrip(r);
                EditorGUI.DrawRect(vs, new Color(0.20f, 0.16f, 0.13f));
                EditorGUI.DrawRect(new Rect(vs.x, vs.y - 1f, vs.width, 1f), new Color(1f, 1f, 1f, 0.12f));
                foreach (var k in _record.voiceKeys) {
                    float x = TimeToX(r, k.time);
                    if (x < r.x - 4f || x > r.xMax + 4f) continue;
                    var m = new Rect(x - 3f, vs.y + 2f, 6f, vs.height - 4f);
                    if (_voiceSel.Contains(k)) EditorGUI.DrawRect(new Rect(m.x - 2f, m.y - 2f, m.width + 4f, m.height + 4f), Color.white);
                    EditorGUI.DrawRect(m, VoiceColor);
                    GUI.Label(new Rect(m.x + 6f, vs.y + 2f, 90f, 14f), k.name, EditorStyles.miniLabel);
                }
            }

            // END marker on the ruler — drag it right to hold the ending longer
            if (_record.IsValid) {
                float ex = TimeToX(r, _record.Duration);
                if (ex >= r.x && ex <= r.xMax) {
                    EditorGUI.DrawRect(new Rect(ex - 1f, r.y, 2f, RulerH), new Color(1f, 1f, 1f, 0.85f));
                    GUI.Label(new Rect(ex + 3f, r.y + 1f, 34f, 14f), "END", EditorStyles.miniLabel);
                }
            }

            // playhead
            float px2 = TimeToX(r, _playhead);
            if (px2 >= r.x && px2 <= r.xMax)
                EditorGUI.DrawRect(new Rect(px2, r.y, 2f, r.height), new Color(1f, 0.25f, 0.25f));
        }

        bool _dragEndKey;
        bool _captureWaiting;
        double _replayDoneAt;
        string _captureOutput;   // capture target, extensionless — uniquified per take

        void StartRecordVideo() {
            if (!SaveRecord()) return;
            var problems = _record.ValidateVoices(_path, Voices);
            if (problems.Count > 0 && !EditorUtility.DisplayDialog("GP Recorder",
                    "Voice problems:\n- " + string.Join("\n- ", problems) + "\n\nRecord the video anyway (those keys stay silent)?",
                    "Record", "Cancel"))
                return;
            _playhead = 0f;
            StartReplayRun(EPending.RecordVideo);
        }

        // ---- undo / redo: whole-record snapshots at every commit point -----------------

        readonly System.Collections.Generic.List<string> _undoStack = new System.Collections.Generic.List<string>();
        readonly System.Collections.Generic.List<string> _redoStack = new System.Collections.Generic.List<string>();

        /// <summary>Capture the record BEFORE a mutation (drag start, delete, add, field edit).
        /// Duplicate states are skipped, so calling on a click that mutates nothing is free.</summary>
        void PushUndo() {
            string s = JsonUtility.ToJson(_record);
            if (_undoStack.Count > 0 && _undoStack[_undoStack.Count - 1] == s) return;
            _undoStack.Add(s);
            if (_undoStack.Count > 60) _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }

        void DoUndo() {
            if (_undoStack.Count == 0) return;
            _redoStack.Add(JsonUtility.ToJson(_record));
            RestoreSnapshot(_undoStack[_undoStack.Count - 1]);
            _undoStack.RemoveAt(_undoStack.Count - 1);
        }

        void DoRedo() {
            if (_redoStack.Count == 0) return;
            _undoStack.Add(JsonUtility.ToJson(_record));
            RestoreSnapshot(_redoStack[_redoStack.Count - 1]);
            _redoStack.RemoveAt(_redoStack.Count - 1);
        }

        void RestoreSnapshot(string json) {
            _record = JsonUtility.FromJson<GPRecord>(json);
            ClearSelection();
            ScrubPreview();
            Repaint();
        }

        // Hide/show the chrome pieces to match the flags, every editor tick while playing —
        // cheap, and it self-heals when the scene reloads (new level, replay restart).
        void ApplyHideFlags() {
            if (!EditorApplication.isPlaying || MBGameplay.instance == null) return;
            var layout = GameObject.Find("GameLayout");
            if (layout == null) return;
            SetActive(layout.transform.Find("TopBar/MinMax/Top"), !_record.hideTop);
            SetActive(layout.transform.Find("TopBar/MinMax/Help"), !_record.hideRules);
            SetActive(layout.transform.Find("Boosters"), !_record.hideBoosters);
            SetActive(layout.transform.Find("TopBar/MinMax/Bottom"), !_record.hideCounters);
        }

        static void SetActive(Transform t, bool on) {
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        void SelectedGUI(bool live) {
            _selection.RemoveAll(s => !_record.actions.Contains(s));   // drop stale refs
            _handSel.RemoveAll(k => !_record.handKeys.Contains(k));
            _spotSel.RemoveAll(k => !_record.spotKeys.Contains(k));
            _voiceSel.RemoveAll(k => !_record.voiceKeys.Contains(k));

            if (!live && _voiceSel.Count > 0) {
                using (new EditorGUILayout.HorizontalScope()) {
                    if (_voiceSel.Count == 1) {
                        var sel = _voiceSel[0];
                        GUILayout.Label("voice:", GUILayout.Width(38f));
                        var v = Voices;
                        var names = v != null ? v.lines.Select(l => l.name).ToArray() : System.Array.Empty<string>();
                        EditorGUI.BeginChangeCheck();
                        string name = sel.name;
                        if (names.Length > 0) {
                            int cur = System.Array.IndexOf(names, sel.name);
                            int pick = EditorGUILayout.Popup(cur, names, GUILayout.Width(110f));
                            if (pick >= 0) name = names[pick];
                        } else {
                            name = EditorGUILayout.TextField(sel.name, GUILayout.Width(110f));
                        }
                        GUILayout.Label("time", GUILayout.Width(32f));
                        float vt = EditorGUILayout.FloatField(sel.time, GUILayout.Width(60f));
                        if (EditorGUI.EndChangeCheck()) {
                            PushUndo();
                            sel.name = name;
                            sel.time = Mathf.Max(0f, vt);
                            _record.Sort();
                        }
                        var line = v?.Find(sel.name);
                        if (line != null) {
                            string wav = string.IsNullOrEmpty(line.path) ? null : Path.Combine(GPRecord.FolderOf(_path), line.path);
                            using (new EditorGUI.DisabledScope(wav == null || !File.Exists(wav))) {
                                if (GUILayout.Button("▶", GUILayout.Width(24f)))
                                    Audition(wav);
                            }
                            GUILayout.Label($"\"{line.text}\"", EditorStyles.miniLabel);
                        }
                    } else {
                        GUILayout.Label($"{_voiceSel.Count} voice keys selected — drag together, or:", GUILayout.Width(250f));
                    }
                    if (GUILayout.Button("Delete", GUILayout.Width(60))) {
                        PushUndo();
                        foreach (var k in _voiceSel) _record.voiceKeys.Remove(k);
                        _voiceSel.Clear();
                        _dragVoice = _voiceAnchor = null;
                    }
                }
                return;
            }

            if (!live && _spotSel.Count > 0) {
                using (new EditorGUILayout.HorizontalScope()) {
                    if (_spotSel.Count == 1) {
                        var sel = _spotSel[0];
                        GUILayout.Label("spot:", GUILayout.Width(38f));
                        EditorGUI.BeginChangeCheck();
                        var kind = (GPSpotKey.EKind)EditorGUILayout.EnumPopup(sel.kind, GUILayout.Width(80f));
                        GUILayout.Label("time", GUILayout.Width(32f));
                        float st = EditorGUILayout.FloatField(sel.time, GUILayout.Width(60f));
                        if (EditorGUI.EndChangeCheck()) {
                            PushUndo();
                            sel.kind = kind;
                            sel.time = Mathf.Max(0f, st);
                            _record.Sort();
                            ScrubPreview();
                        }
                        if (sel.kind == GPSpotKey.EKind.SHOW) {
                            GUILayout.Label($"{sel.Count} cells", GUILayout.Width(50f));
                            if (GPSpotPicker.Active) {
                                if (GUILayout.Button("✔ Done", GUILayout.Width(64f))) GPSpotPicker.End();
                                GUILayout.Label("click cells on the board to toggle them", EditorStyles.miniLabel);
                            } else using (new EditorGUI.DisabledScope(!GPReplayer.CanDrive(_record))) {
                                if (GUILayout.Button(new GUIContent("🎯 Pick",
                                        "Click cells on the live board to toggle them in this spotlight — the board is NOT " +
                                        "changed, and the curtain previews the result live. Needs the level live (press ▶ once)."),
                                        GUILayout.Width(64f))) {
                                    _playhead = sel.time;
                                    ScrubPreview();
                                    GPSpotPicker.Begin(sel);
                                }
                            }
                        }
                    } else {
                        GUILayout.Label($"{_spotSel.Count} spot keys selected — drag together, or:", GUILayout.Width(250f));
                    }
                    if (GUILayout.Button("Delete", GUILayout.Width(60))) {
                        PushUndo();
                        foreach (var k in _spotSel) _record.spotKeys.Remove(k);
                        _spotSel.Clear();
                        _dragSpot = _spotAnchor = null;
                        ScrubPreview();
                    }
                }
                return;
            }

            if (!live && _handSel.Count > 0) {
                using (new EditorGUILayout.HorizontalScope()) {
                    if (_handSel.Count == 1) {
                        var sel = _handSel[0];
                        GUILayout.Label("hand:", GUILayout.Width(38f));
                        int n = _record.level.size;
                        EditorGUI.BeginChangeCheck();
                        var kind = (GPHandKey.EKind)EditorGUILayout.EnumPopup(sel.kind, GUILayout.Width(110f));
                        GUILayout.Label("cell", GUILayout.Width(28f));
                        int hx = Mathf.Clamp(EditorGUILayout.IntField(sel.x, GUILayout.Width(30f)), 0, n - 1);
                        int hy = Mathf.Clamp(EditorGUILayout.IntField(sel.y, GUILayout.Width(30f)), 0, n - 1);
                        GUILayout.Label("time", GUILayout.Width(32f));
                        float ht = EditorGUILayout.FloatField(sel.time, GUILayout.Width(60f));
                        if (EditorGUI.EndChangeCheck()) {
                            PushUndo();
                            sel.kind = kind;
                            sel.x = hx;
                            sel.y = hy;
                            sel.time = Mathf.Max(0f, ht);
                            _record.Sort();
                            ScrubPreview();
                        }
                    } else {
                        GUILayout.Label($"{_handSel.Count} hand keys selected — drag together, or:", GUILayout.Width(250f));
                    }
                    if (GUILayout.Button("Delete", GUILayout.Width(60))) {
                        PushUndo();
                        foreach (var k in _handSel) _record.handKeys.Remove(k);
                        _handSel.Clear();
                        _dragHand = _handAnchor = null;
                        ScrubPreview();
                    }
                }
                return;
            }

            if (live || _selection.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope()) {
                if (_selection.Count == 1) {
                    var sel = _selection[0];
                    GUILayout.Label($"selected: {sel.to}  cell ({sel.x},{sel.y})", GUILayout.Width(220));
                    EditorGUI.BeginChangeCheck();
                    float t = EditorGUILayout.FloatField("time", sel.time);
                    if (EditorGUI.EndChangeCheck()) { PushUndo(); sel.time = Mathf.Max(0f, t); _record.Sort(); }
                } else {
                    GUILayout.Label($"{_selection.Count} selected — drag together, or:", GUILayout.Width(220));
                }
                if (GUILayout.Button("Delete", GUILayout.Width(60))) {
                    PushUndo();
                    foreach (var s in _selection) _record.actions.Remove(s);
                    ClearSelection();
                }
            }
        }

        void LegendGUI() {
            using (new EditorGUILayout.HorizontalScope()) {
                foreach (var t in new[] { MBCell.ECellType.X, MBCell.ECellType.EMPTY, MBCell.ECellType.QUEEN, MBCell.ECellType.WRONG_QUEEN }) {
                    var sw = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f));
                    EditorGUI.DrawRect(new Rect(sw.x, sw.y + 3f, 10f, 10f), ColorOf(t));
                    GUILayout.Label(t == MBCell.ECellType.EMPTY ? "UNMARK" : t.ToString(), EditorStyles.miniLabel, GUILayout.Width(90f));
                }
                foreach (var k in new[] { GPHandKey.EKind.POINT, GPHandKey.EKind.END_MOVE, GPHandKey.EKind.DOUBLE_CLICK }) {
                    var sw = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f));
                    EditorGUI.DrawRect(new Rect(sw.x, sw.y + 3f, 10f, 10f), HandColorOf(k));
                    GUILayout.Label("HAND " + k, EditorStyles.miniLabel, GUILayout.Width(k == GPHandKey.EKind.POINT ? 70f : 110f));
                }
                foreach (var k in new[] { GPSpotKey.EKind.SHOW, GPSpotKey.EKind.CLEAR }) {
                    var sw = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f));
                    EditorGUI.DrawRect(new Rect(sw.x, sw.y + 3f, 10f, 10f), SpotColorOf(k));
                    GUILayout.Label("SPOT " + k, EditorStyles.miniLabel, GUILayout.Width(74f));
                }
                {
                    var sw = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f));
                    EditorGUI.DrawRect(new Rect(sw.x, sw.y + 3f, 10f, 10f), VoiceColor);
                    GUILayout.Label("VOICE", EditorStyles.miniLabel, GUILayout.Width(44f));
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("wheel zoom · alt-drag pan · click playhead · ctrl-click toggle · shift-click range · drag = retime · Del", EditorStyles.miniLabel);
            }
        }

        // ---- hand keys -----------------------------------------------------------------

        // A new hand key lands on the selected board key's cell, timed so the hand's action
        // coincides with that mark appearing:
        //   DOUBLE_CLICK on a queen → 2nd tap lands on the queen (key − 0.42s)
        //   first POINT of a drag  → the press completes on the mark (key − PressIn)
        //   POINT inside a drag / END → exactly the key's time (the finger passes / lifts there)
        // With no board key selected: at the playhead, on the nearest board key's cell.
        void AddHandKey(GPHandKey.EKind kind) {
            PushUndo();
            var sel = _selection.Count == 1 ? _selection[0] : null;
            var cell = sel != null ? (sel.x, sel.y) : NearestAction(_playhead);
            float time = _playhead;
            if (sel != null) {
                switch (kind) {
                    case GPHandKey.EKind.DOUBLE_CLICK:
                        if (sel.to == MBCell.ECellType.QUEEN || sel.to == MBCell.ECellType.WRONG_QUEEN)
                            time = sel.time - GPHand.DcSecondPress;
                        break;
                    case GPHandKey.EKind.POINT:
                        time = DragOpenAt(sel.time) ? sel.time : sel.time - GPHand.PressIn;
                        break;
                    case GPHandKey.EKind.END_MOVE:
                        time = sel.time;
                        break;
                }
                time = Mathf.Max(0f, time);
            }

            var key = new GPHandKey { time = time, x = cell.x, y = cell.y, kind = kind };
            _record.handKeys.Add(key);
            _record.Sort();
            _selection.Clear(); _dragging = _anchor = null;
            _handSel.Clear();
            _handSel.Add(key);
            ScrubPreview();
        }

        // Is a drag open (a POINT seen, no END / DOUBLE_CLICK since) just before time t?
        bool DragOpenAt(float t) {
            bool open = false;
            foreach (var k in _record.handKeys) {   // time-sorted
                if (k.time >= t) break;
                open = k.kind == GPHandKey.EKind.POINT;
            }
            return open;
        }

        (int x, int y) NearestAction(float t) {
            GPRecordAction best = null;
            float bestD = float.MaxValue;
            foreach (var a in _record.actions) {
                float d = Mathf.Abs(a.time - t);
                if (d < bestD) { bestD = d; best = a; }
            }
            return best != null ? (best.x, best.y) : (0, 0);
        }

        // Live board preview while dragging the playhead — only when the record's level is
        // already on the board (a replay session); otherwise scrubbing just moves the line.
        void ScrubPreview() {
            if (GPReplayer.CanDrive(_record) && (!GPRecorder.IsRecording || GPRecorder.IsInserting))
                GPReplayer.Seek(_record, _playhead);
            if (GPRecorder.IsInserting) GPRecorder.SetInsertTime(_playhead);   // edits follow the line
        }

        // ---- helpers -------------------------------------------------------------------

        float TimeToX(Rect r, float t) => r.x + (t - _viewStart) * _pxPerSec;
        float XToTime(Rect r, float x) => _viewStart + (x - r.x) / _pxPerSec;

        struct MarkerRect {
            public GPRecordAction a;
            public Rect rect;
            public int clusterSize;     // how many keys share this visual spot
            public bool clusterFirst;   // draw the ×N count once per cluster
        }

        Rect HandStrip(Rect r) => new Rect(r.x, r.yMax - VoiceH - SpotH - HandH - 12f, r.width, HandH);
        Rect SpotStrip(Rect r) => new Rect(r.x, r.yMax - VoiceH - SpotH - 8f, r.width, SpotH);
        Rect VoiceStrip(Rect r) => new Rect(r.x, r.yMax - VoiceH - 4f, r.width, VoiceH);

        GPHandKey HitHandKey(Rect r, Vector2 mouse) {
            if (!HandStrip(r).Contains(mouse)) return null;
            GPHandKey best = null;
            float bestDx = 6f;   // hit slop in px
            foreach (var k in _record.handKeys) {
                float dx = Mathf.Abs(TimeToX(r, k.time) - mouse.x);
                if (dx < bestDx) { bestDx = dx; best = k; }
            }
            return best;
        }

        GPSpotKey HitSpotKey(Rect r, Vector2 mouse) {
            if (!SpotStrip(r).Contains(mouse)) return null;
            GPSpotKey best = null;
            float bestDx = 6f;   // hit slop in px
            foreach (var k in _record.spotKeys) {
                float dx = Mathf.Abs(TimeToX(r, k.time) - mouse.x);
                if (dx < bestDx) { bestDx = dx; best = k; }
            }
            return best;
        }

        static Color SpotColorOf(GPSpotKey.EKind kind) =>
            kind == GPSpotKey.EKind.SHOW ? new Color(0.72f, 0.50f, 1f) : new Color(0.50f, 0.50f, 0.55f);

        static readonly Color VoiceColor = new Color(1f, 0.58f, 0.25f);

        GPVoiceKey HitVoiceKey(Rect r, Vector2 mouse) {
            if (!VoiceStrip(r).Contains(mouse)) return null;
            GPVoiceKey best = null;
            float bestDx = 6f;   // hit slop in px
            foreach (var k in _record.voiceKeys) {
                float dx = Mathf.Abs(TimeToX(r, k.time) - mouse.x);
                if (dx < bestDx) { bestDx = dx; best = k; }
            }
            return best;
        }

        // A voice key lands at the playhead; the line starts as the active set's first line and
        // is switched in the selected-key row.
        void AddVoiceKey() {
            PushUndo();
            var v = Voices;
            var key = new GPVoiceKey { time = _playhead, name = v != null && v.lines.Count > 0 ? v.lines[0].name : "" };
            _record.voiceKeys.Add(key);
            _record.Sort();
            ClearSelection();
            _voiceSel.Add(key);
        }

        // A spot key starts from the selected board keys: their cells become the SHOW list, its
        // time the earliest of them (spotlight up as the first mark appears). With no selection:
        // playhead time, nearest key's cell. Cells are then refined on the board with 🎯 Pick.
        void AddSpotKey(GPSpotKey.EKind kind) {
            PushUndo();
            float time = _playhead;
            var xs = new System.Collections.Generic.List<int>();
            var ys = new System.Collections.Generic.List<int>();
            if (_selection.Count > 0) {
                time = float.MaxValue;
                foreach (var a in _selection) {
                    time = Mathf.Min(time, a.time);
                    bool dup = false;
                    for (int i = 0; i < xs.Count; i++) if (xs[i] == a.x && ys[i] == a.y) { dup = true; break; }
                    if (!dup) { xs.Add(a.x); ys.Add(a.y); }
                }
            } else {
                var c = NearestAction(_playhead);
                xs.Add(c.x);
                ys.Add(c.y);
            }

            var key = new GPSpotKey { time = time, x = xs[0], y = ys[0], kind = kind };
            if (kind == GPSpotKey.EKind.SHOW) { key.xs = xs.ToArray(); key.ys = ys.ToArray(); }
            _record.spotKeys.Add(key);
            _record.Sort();
            ClearSelection();
            _spotSel.Add(key);
            ScrubPreview();
        }

        static Color HandColorOf(GPHandKey.EKind kind) {
            switch (kind) {
                case GPHandKey.EKind.POINT:        return new Color(0.30f, 0.85f, 0.45f);
                case GPHandKey.EKind.END_MOVE:     return new Color(0.30f, 0.80f, 0.80f);
                default:                           return new Color(1.00f, 0.80f, 0.20f);   // DOUBLE_CLICK
            }
        }

        // Markers that overlap at the current zoom (within ~7px) split the band vertically —
        // a stack of same-time keys stays individually visible and clickable. Assumes the
        // action list is time-sorted (the window keeps that invariant).
        System.Collections.Generic.List<MarkerRect> LayoutMarkers(Rect r) {
            var result = new System.Collections.Generic.List<MarkerRect>();
            float bandY = r.y + RulerH + 4f;
            float bandH = r.height - RulerH - HandH - SpotH - VoiceH - 22f;   // room for hand + spot + voice strips
            var acts = _record.actions;
            int i = 0;
            while (i < acts.Count) {
                float x0 = TimeToX(r, acts[i].time);
                int j = i + 1;
                while (j < acts.Count && TimeToX(r, acts[j].time) - x0 < 7f) j++;   // cluster [i, j)
                int size = j - i;
                float h = bandH / size;
                for (int c = i; c < j; c++) {
                    result.Add(new MarkerRect {
                        a = acts[c],
                        rect = new Rect(TimeToX(r, acts[c].time) - 3f, bandY + (c - i) * h, 6f, Mathf.Max(h - 2f, 3f)),
                        clusterSize = size,
                        clusterFirst = c == i
                    });
                }
                i = j;
            }
            return result;
        }

        GPRecordAction HitMarker(Rect r, Vector2 mouse) {
            GPRecordAction best = null;
            float bestDx = float.MaxValue;
            foreach (var m in LayoutMarkers(r)) {
                if (mouse.y < m.rect.y || mouse.y > m.rect.yMax) continue;
                float dx = Mathf.Abs(m.rect.center.x - mouse.x);
                if (dx < 5f && dx < bestDx) { bestDx = dx; best = m.a; }   // 5px slop
            }
            return best;
        }

        static Color ColorOf(MBCell.ECellType t) {
            switch (t) {
                case MBCell.ECellType.X:           return new Color(0.30f, 0.60f, 1.00f);
                case MBCell.ECellType.QUEEN:       return new Color(1.00f, 0.80f, 0.20f);
                case MBCell.ECellType.WRONG_QUEEN: return new Color(1.00f, 0.30f, 0.30f);
                default:                           return new Color(0.62f, 0.62f, 0.62f);   // unmark
            }
        }
    }
}
