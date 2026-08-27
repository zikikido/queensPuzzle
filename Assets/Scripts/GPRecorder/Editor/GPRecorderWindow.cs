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
        enum EPending { None, RecordFresh, Replay, RecordAtPlayhead }

        // survives the domain reload on play-mode entry via EditorWindow serialization
        [SerializeField] GPRecord _record = new GPRecord();
        [SerializeField] string _path = "";
        [SerializeField] string _saveName = "record";
        [SerializeField] float _playhead;
        [SerializeField] float _pxPerSec = 80f;   // zoom
        [SerializeField] float _viewStart;        // seconds at the timeline's left edge (pan)
        [SerializeField] EPending _pending = EPending.None;
        [SerializeField] bool _noFail = true;   // wrong queens don't lose bones during record/replay

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
            if (_pending != EPending.None && EditorApplication.isPlaying && MBGameplay.instance != null
                && MBGameplay.instance.Ready && !GPReplayer.IsReplaying && !GPRecorder.IsRecording) {
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
                }
            }
            GPRecorder.ConsumeOverwritten();   // overdub: old keys the record head passed are consumed
            // 🎯 Pick lives only while its single SHOW key stays selected
            if (GPSpotPicker.Active && (_spotSel.Count != 1 || _spotSel[0].kind != GPSpotKey.EKind.SHOW))
                GPSpotPicker.End();

            GPReplayer.RecordFolder = string.IsNullOrEmpty(_path) ? null : GPRecord.FolderOf(_path);

            // the record file changed on disk (script, git, hand edit) — reload it, keeping the view
            if (_pending == EPending.None && !GPRecorder.IsRecording && !GPReplayer.IsReplaying
                && _dragging == null && _dragHand == null && _dragSpot == null && _dragVoice == null
                && !string.IsNullOrEmpty(_path) && File.Exists(_path)
                && File.GetLastWriteTimeUtc(_path) != _recordStamp) {
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
            return true;
        }

        System.DateTime _recordStamp;

        void LoadRecord(string path) {
            if (GPRecorder.IsRecording) { GPRecorder.End(); SaveRecord(); }   // ✎ Edit may still be on
            _record = GPRecord.Load(path);
            _path = Path.GetFullPath(path);
            _recordStamp = File.GetLastWriteTimeUtc(_path);
            _saveName = GPRecord.NameOf(path);
            _voices = null;
            ClearSelection();
            _playhead = _viewStart = 0f;
        }

        // ---- GUI -----------------------------------------------------------------------

        void OnGUI() {
            GPRecorder.NoFail = _noFail;   // statics reset on domain reload — the window is the source
            // ✎ Edit (insert) keeps the timeline fully editable — only a running record/replay locks it
            bool live = GPReplayer.IsReplaying || (GPRecorder.IsRecording && !GPRecorder.IsInserting);
            if (GPRecorder.IsRecording) _playhead = GPRecorder.Elapsed;
            else if (GPReplayer.IsReplaying) _playhead = GPReplayer.PlayheadTime;

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

        // wavs of set "voices1.json" land in Voices/1/, "voices2.json" in Voices/2/ — swapping
        // the set swaps the folder, the timeline keys never change
        void GenerateLine(GPVoiceLine l) {
            string setId = Path.GetFileNameWithoutExtension(_record.voicesFile).Replace("voices", "");
            if (string.IsNullOrEmpty(setId)) setId = "A";
            GPVoiceGen.Generate(l, Voices.voiceId, GPRecord.FolderOf(_path), "Voices/" + setId, () => { SaveVoices(); Repaint(); });
        }


        void FilesGUI(bool live) {
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(live)) {
                var paths = GPRecord.ListPaths();
                var names = paths.Select(GPRecord.NameOf).ToArray();
                int cur = System.Array.IndexOf(paths, _path);
                int pick = EditorGUILayout.Popup(cur, names, GUILayout.Width(170));
                if (pick != cur && pick >= 0) LoadRecord(paths[pick]);

                GUILayout.Label(_saveName, EditorStyles.boldLabel, GUILayout.Width(140f));
                using (new EditorGUI.DisabledScope(!_record.IsValid)) {
                    if (GUILayout.Button("Save", GUILayout.Width(48))) SaveRecord();
                }
                if (GUILayout.Button("New", GUILayout.Width(48))) {
                    if (GPRecorder.IsRecording) { GPRecorder.End(); SaveRecord(); }   // ✎ Edit may still be on
                    _record = new GPRecord();
                    _path = "";
                    _voices = null;
                    ClearSelection();
                    _playhead = _viewStart = 0f;
                }
            }
        }

        void TransportGUI() {
            using (new EditorGUILayout.HorizontalScope()) {
                if (GPRecorder.IsInserting) {
                    if (GUILayout.Button("✔ Done Editing", GUILayout.Width(130))) { GPRecorder.End(); SaveRecord(); }
                    GUILayout.Label($"EDIT — board actions are inserted at {_playhead:0.00}s");
                } else if (GPRecorder.IsRecording) {
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("■ Stop Recording", GUILayout.Width(130))) { GPRecorder.End(); SaveRecord(); }
                    GUI.color = Color.white;
                    GUILayout.Label($"REC  {_playhead:0.00}s");
                } else if (GPReplayer.IsReplaying) {
                    if (GUILayout.Button("■ Stop Replay", GUILayout.Width(130))) GPReplayer.Stop();
                    GUILayout.Label($"PLAY  {_playhead:0.00}s");
                } else if (_pending != EPending.None) {
                    GUILayout.Label("starting…");
                } else {
                    if (GUILayout.Button("● Record", GUILayout.Width(90))) StartFreshRecordRun();
                    using (new EditorGUI.DisabledScope(!_record.IsValid)) {
                        // when the record's level is already live on the board, drive it in place
                        // (video-player style, from the playhead); otherwise restart play mode
                        if (GUILayout.Button($"▶ Play from {_playhead:0.00}s", GUILayout.Width(130))) {
                            if (GPReplayer.CanDrive(_record)) GPReplayer.Play(_record, _playhead);
                            else StartReplayRun(EPending.Replay);
                        }
                        // overdub: old keys are consumed only as the head passes them — stop early
                        // and the tail of the previous recording survives
                        if (GUILayout.Button($"⦿ Re-record from {_playhead:0.00}s", GUILayout.Width(170))) {
                            if (GPReplayer.CanDrive(_record)) {
                                GPReplayer.Seek(_record, _playhead);
                                GPRecorder.Begin(_record, _playhead);
                            } else StartReplayRun(EPending.RecordAtPlayhead);
                        }
                        using (new EditorGUI.DisabledScope(!GPReplayer.CanDrive(_record))) {
                            if (GUILayout.Button(new GUIContent("✎ Edit",
                                    "Insert board actions at the playhead. Needs the level live — press ▶ once first."),
                                    GUILayout.Width(60)))
                                GPRecorder.BeginInsert(_record, _playhead);
                        }
                        GUILayout.Space(12f);
                        _noFail = GUILayout.Toggle(_noFail, new GUIContent("NoFail",
                            "During record & replay: wrong queens shake and flash but lose no bones and can never fail the board"),
                            GUILayout.Width(60f));
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"playhead {_playhead:0.00}s");
                }
            }

            // hand + spotlight key buttons, their own row — the transport row is full
            bool idle = !GPRecorder.IsRecording && !GPReplayer.IsReplaying && _pending == EPending.None;
            if (!idle) return;
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!_record.IsValid)) {
                if (GUILayout.Button(new GUIContent("+ Hand Point",
                        "Add a drag point. Select a board key first: it goes on that cell — a drag's first point " +
                        "slightly early so the press lands on the mark, a corner inside the drag exactly at the key. " +
                        "Otherwise at the playhead."),
                        GUILayout.Width(95f)))
                    AddHandKey(GPHandKey.EKind.POINT);
                if (GUILayout.Button(new GUIContent("+ Hand End",
                        "Add the drag's END: the finger lifts and the hand vanishes. On a selected board key → " +
                        "that cell, exactly at the key's time. Otherwise at the playhead."),
                        GUILayout.Width(85f)))
                    AddHandKey(GPHandKey.EKind.END_MOVE);
                if (GUILayout.Button(new GUIContent("+ Hand DblTap",
                        "Add a hand double-tap. Select a QUEEN board key first: it goes on that cell, timed so the " +
                        "2nd tap lands exactly when the queen appears. Otherwise it lands at the playhead."),
                        GUILayout.Width(105f)))
                    AddHandKey(GPHandKey.EKind.DOUBLE_CLICK);
                GUILayout.Space(12f);
                if (GUILayout.Button(new GUIContent("+ Spot",
                        "Spotlight: tutorial-style curtain with holes over this key's CELL LIST. Selected board keys' cells " +
                        "seed the list (time = earliest); refine it with 🎯 Pick by clicking cells on the live board. " +
                        "Holes add up until a Spot Clear."),
                        GUILayout.Width(60f)))
                    AddSpotKey(GPSpotKey.EKind.SHOW);
                if (GUILayout.Button(new GUIContent("+ Spot Clear",
                        "Drop the curtain and close all spotlight holes at the playhead"),
                        GUILayout.Width(90f)))
                    AddSpotKey(GPSpotKey.EKind.CLEAR);
                GUILayout.Space(12f);
                if (GUILayout.Button(new GUIContent("+ Voice",
                        "Add a voice key at the playhead — pick which line it plays in the row below"),
                        GUILayout.Width(66f)))
                    AddVoiceKey();
            }
        }

        void InfoGUI() {
            if (!_record.IsValid) {
                EditorGUILayout.HelpBox("No recording. ● Record starts play mode on the current level " +
                                        "with an empty board and captures your play. Or pick a saved file above.", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Label($"level {_record.level.size}x{_record.level.size}   " +
                                $"{_record.actions.Count} actions   {_record.Duration:0.00}s", EditorStyles.miniLabel);
                GUILayout.Space(16f);
                GUILayout.Label("voices:", EditorStyles.miniLabel, GUILayout.Width(40f));
                if (string.IsNullOrEmpty(_path)) {
                    GUILayout.Label("save the record first", EditorStyles.miniLabel);
                } else {
                    var files = Directory.GetFiles(GPRecord.FolderOf(_path), "voices*.json")
                        .Select(Path.GetFileName).ToArray();
                    if (files.Length == 0) GUILayout.Label($"(no voices*.json in the folder yet)", EditorStyles.miniLabel);
                    else {
                        int cur = System.Array.IndexOf(files, _record.voicesFile);
                        int pick = EditorGUILayout.Popup(cur, files, GUILayout.Width(110f));
                        if (pick != cur && pick >= 0) {
                            _record.voicesFile = files[pick];   // swap the whole voice set
                            _voices = null;
                        }
                    }
                }
                GUILayout.FlexibleSpace();
            }

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
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && r.Contains(e.mousePosition)) {
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
                    if (_dragging != null) {
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
                    if (_dragging != null) { _record.Sort(); _dragging = null; e.Use(); }
                    if (_dragHand != null) { _record.Sort(); _dragHand = null; ScrubPreview(); e.Use(); }
                    if (_dragSpot != null) { _record.Sort(); _dragSpot = null; ScrubPreview(); e.Use(); }
                    if (_dragVoice != null) { _record.Sort(); _dragVoice = null; e.Use(); }
                    _scrubbing = false;
                } else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete
                           && (_selection.Count > 0 || _handSel.Count > 0 || _spotSel.Count > 0 || _voiceSel.Count > 0)) {
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

            // playhead
            float px2 = TimeToX(r, _playhead);
            if (px2 >= r.x && px2 <= r.xMax)
                EditorGUI.DrawRect(new Rect(px2, r.y, 2f, r.height), new Color(1f, 0.25f, 0.25f));
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
                    if (EditorGUI.EndChangeCheck()) { sel.time = Mathf.Max(0f, t); _record.Sort(); }
                } else {
                    GUILayout.Label($"{_selection.Count} selected — drag together, or:", GUILayout.Width(220));
                }
                if (GUILayout.Button("Delete", GUILayout.Width(60))) {
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
