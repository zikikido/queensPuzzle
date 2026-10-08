using Common;
using TMPro;
using UnityEngine;

namespace qp {

    /// <summary>
    /// One row of the leaderboard. Its whole API is <see cref="Set"/> — tell it a rank, a name and
    /// a score, and whether it is the player's own row, and it looks right.
    ///
    /// It knows nothing about tournaments. It does not read the manager, it does not decide who is
    /// on the list or in what order, and it has no idea how many rows there are. That keeps the
    /// list free to be built, reordered and reused without this having an opinion about it.
    ///
    /// Every variation is already in the prefab — two backgrounds, three medals, two name styles,
    /// two score pills, three prizes — so all this does is turn the right ones on. Nothing here
    /// swaps a sprite, tints a colour or sets a font: the look belongs to the art, and the art can
    /// change without touching this file.
    /// </summary>
    public sealed class MBTournamentRow : MonoBehaviour {

        /// <summary>Ranks that get a medal and a prize, which is also how many medals exist.</summary>
        public const int MedalRanks = 3;

        GameObject _bg, _bgMe;
        GameObject[] _medals, _prizes;
        GameObject _pill, _pillMe;
        TMP_Text _place, _name, _nameMe, _score, _scoreMe;

        // What is on screen, so setting a row to what it already shows costs nothing. A TMP label
        // assigned the string it already holds still dirties its mesh and rebuilds the layout.
        int _rankShown = -1, _scoreShown = -1;
        string _nameShown;
        bool _meShown, _everSet;

        void Awake() {
            _bg     = Child("$BG");
            _bgMe   = Child("$BGSelected");
            _pill   = Child("$ScorePill");
            _pillMe = Child("$ScorePillSelected");

            _medals = new[] { Child("$Medal1"), Child("$Medal2"), Child("$Medal3") };
            _prizes = new[] { Child("$Prize1"), Child("$Prize2"), Child("$Prize3") };

            _place   = Label("$Place");
            _name    = Label("$Name");
            _nameMe  = Label("$NameSelected");
            _score   = Label("$Score");
            _scoreMe = Label("$ScoreSelected");
        }

        /// <summary>
        /// Draw this row. <paramref name="rank"/> is 1-based — 1 to <see cref="MedalRanks"/> get a
        /// medal and a prize, everyone else gets the number. <paramref name="isMe"/> switches the
        /// row to the player's own colours.
        /// </summary>
        public void Set(int rank, string name, int score, bool isMe) {
            bool first = !_everSet;
            _everSet = true;

            if (first || isMe != _meShown) {
                Show(_bg, !isMe);   Show(_bgMe, isMe);
                Show(_pill, !isMe); Show(_pillMe, isMe);
                _meShown = isMe;
                _nameShown = null;          // the label in use changed, so it has to be written
                _scoreShown = -1;
            }

            if (first || rank != _rankShown) {
                for (int i = 0; i < _medals.Length; i++) {
                    Show(_medals[i], rank == i + 1);
                    Show(_prizes[i], rank == i + 1);
                }
                // The number is for everyone a medal does not cover.
                bool plain = rank > MedalRanks;
                Show(_place, plain);
                if (plain) SetText(_place, rank.ToString());
                _rankShown = rank;
            }

            if (name != _nameShown) {
                SetText(isMe ? _nameMe : _name, name);
                _nameShown = name;
            }

            if (score != _scoreShown) {
                SetText(isMe ? _scoreMe : _score, score.ToString());
                _scoreShown = score;
            }
        }

        // ---- the prefab ------------------------------------------------------------------

        GameObject Child(string name) {
            var found = transform.RecursiveFindChild(name);
            if (found == null) Debug.LogError($"[TournamentRow] {name} is missing from the prefab");
            return found != null ? found.gameObject : null;
        }

        TMP_Text Label(string name) {
            var found = transform.RecursiveFindChild(name);
            if (found == null) { Debug.LogError($"[TournamentRow] {name} is missing from the prefab"); return null; }
            return found.GetComponent<TMP_Text>();
        }

        static void Show(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }
        static void Show(TMP_Text label, bool on) { if (label != null) Show(label.gameObject, on); }
        static void SetText(TMP_Text label, string value) { if (label != null) label.text = value; }
    }
}
