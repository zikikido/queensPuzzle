using System;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The tournament card in the lobby. It shows exactly one of its eight states, and which one
    /// is <see cref="TournamentManager.Status"/>'s decision alone — this only draws it.
    ///
    /// Each state is a whole sub-object in the prefab with its own art and its own button, so the
    /// states cannot half-overlap: turning one on turns the other seven off. The only things read
    /// out of them are the $-named fields, which is also the only contract with the art — anything
    /// else in there can be rearranged freely.
    ///
    /// Everything is found once, in Awake, and every value is compared before it is written. The
    /// card re-reads once a second because the clock moves, and on the 59 ticks out of 60 where
    /// nothing has changed it does nothing at all — no search, no assignment, no layout rebuild.
    /// A TMP label that is assigned the string it already holds still dirties its mesh.
    /// </summary>
    public sealed class MBTournamentCard : MonoBehaviour {

        /// <summary>Tapped, and in which state — so the lobby decides what opens without this
        /// knowing any screen exists.</summary>
        public event Action<ETournamentStatus> Tapped;

        const float RefreshSeconds = 1f;

        /// <summary>One state of the card: its object, and whichever of the fields it has. Found
        /// once; a state that has no $Place simply holds null for it.</summary>
        sealed class State {
            public GameObject root;
            public TMP_Text stopwatch;
            public TMP_Text place;
            public GameObject pendingBones;
            public TMP_Text pendingText;
        }

        State _lock, _offline, _notJoined, _active, _endingSoon, _calculating, _claim, _results;
        State[] _all;

        // What is on screen right now, so a tick that changes nothing writes nothing.
        State _shown;
        string _clockShown, _placeShown;
        int _pendingShown = -1;

        void Awake() {
            _lock        = _find("$Lock");
            _offline     = _find("$Offline");
            _notJoined   = _find("$NotJoined");
            _active      = _find("$Active");
            _endingSoon  = _find("$EndingSoon");
            _calculating = _find("$EndCalculating");
            _claim       = _find("$EndClaim");
            _results     = _find("$EndSeeResults");

            _all = new[] { _lock, _offline, _notJoined, _active, _endingSoon, _calculating, _claim, _results };
        }

        State _find(string name) {
            var root = transform.RecursiveFindChild(name);
            if (root == null) { Debug.LogError($"[TournamentCard] {name} is missing from the prefab"); return null; }

            // Every state is a button, and every button means the same thing: the player asked
            // about the tournament. What that opens is the lobby's business, not the card's.
            var button = root.GetComponent<Button>();
            if (button != null) button.onClick.AddListener(() => Tapped?.Invoke(TournamentManager.Status));

            var bones = root.RecursiveFindChild("$PendingBones");
            return new State {
                root         = root.gameObject,
                stopwatch    = _label(root, "$Stopwatch"),
                place        = _label(root, "$Place"),
                pendingBones = bones != null ? bones.gameObject : null,
                pendingText  = _label(root, "$PendingText"),
            };
        }

        static TMP_Text _label(Transform root, string name) {
            var found = root.RecursiveFindChild(name);
            return found != null ? found.GetComponent<TMP_Text>() : null;
        }

        void OnEnable() {
            // Forget what was on screen: the card may have been away long enough for all of it to
            // be wrong, and the first tick has to write everything again.
            _shown = null;
            _clockShown = _placeShown = null;
            _pendingShown = -1;

            InvokeRepeating(nameof(_draw), 0f, RefreshSeconds);
        }

        void OnDisable() => CancelInvoke(nameof(_draw));

        void _draw() {
            var status = TournamentManager.Status;
            var wanted = _stateFor(status);

            if (wanted != _shown) {
                foreach (var state in _all)
                    if (state != null) state.root.SetActive(state == wanted);
                _shown = wanted;
            }

            if (wanted == null) return;   // None — the card is hidden, nothing to fill in

            _set(wanted.stopwatch, _clock(TournamentManager.TimeLeft), ref _clockShown);

            // A finished tournament shows where the player ENDED; a running one shows where they
            // stand right now, pending wins included, so a win moves the card before the server
            // has heard about it.
            bool ended = status == ETournamentStatus.Ended || status == ETournamentStatus.Calculating;
            int rank = ended ? _closedRank : TournamentManager.MyIndexWithPending + 1;
            _set(wanted.place, rank > 0 ? rank.ToString() : "-", ref _placeShown);

            // Offline only: what is waiting to be sent. Hidden entirely at zero rather than shown
            // as "0", which would read as a score and not as a queue.
            int pending = TournamentManager.State != null ? TournamentManager.State.PendingScore : 0;
            if (pending != _pendingShown) {
                if (wanted.pendingBones != null) wanted.pendingBones.SetActive(pending > 0);
                if (wanted.pendingText != null) wanted.pendingText.text = pending.ToString();
                _pendingShown = pending;
            }
        }

        /// <summary>Write only a value that is new. The label may be null — not every state has
        /// every field — and then there is nothing to remember either.</summary>
        static void _set(TMP_Text label, string value, ref string shown) {
            if (label == null || value == shown) return;
            label.text = value;
            shown = value;
        }

        /// <summary>
        /// Which state a status draws. Ended is the one that splits: the same closed tournament is
        /// a prize to claim or a table to look at, and the difference is only whether the player
        /// landed inside the paying places.
        /// </summary>
        State _stateFor(ETournamentStatus status) {
            switch (status) {
                case ETournamentStatus.Locked:      return _lock;
                case ETournamentStatus.Offline:     return _offline;
                case ETournamentStatus.NotJoined:   return _notJoined;
                case ETournamentStatus.Active:      return _active;
                case ETournamentStatus.EndingSoon:  return _endingSoon;
                case ETournamentStatus.Calculating: return _calculating;
                case ETournamentStatus.Ended:       return _won ? _claim : _results;
                default:                            return null;   // None — the card is hidden
            }
        }

        /// <summary>The closed tournament finished in a paying place.</summary>
        bool _won {
            get {
                var cfg = TournamentConfig.Instance;
                int rank = _closedRank;
                return cfg != null && rank > 0 && rank <= cfg.prizePlaces;
            }
        }

        /// <summary>1-based, or 0 when the player was not in that table.</summary>
        int _closedRank {
            get {
                var closed = TournamentManager.State?.lastSyncClosed;
                return closed != null && closed.Exists ? closed.standings.myIndex + 1 : 0;
            }
        }

        /// <summary>
        /// hh:mm:ss, the same shape the daily challenge card uses — two countdowns side by side in
        /// the same lobby should not be read two different ways.
        ///
        /// Built rather than ToString(@"hh\:mm\:ss"), which would be wrong here: that "hh" is the
        /// Hours COMPONENT, 0-23, with days held separately. The daily resets every 24 hours so it
        /// never notices; a 48-hour tournament would spend its whole first day showing the second
        /// one's time, and 41 hours left would read as 17.
        /// </summary>
        static string _clock(TimeSpan left) {
            if (left <= TimeSpan.Zero) left = TimeSpan.Zero;
            return $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
        }
    }
}
