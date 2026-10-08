using System.Collections;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>Why the leaderboard closed — the whole answer the caller needs. Info is not here:
    /// it opens a screen that belongs to this one, and the lobby never hears about it.</summary>
    public enum ETournamentScreenResult {
        None,     // still open
        Closed,   // back, or the darkened area outside
        Play,     // the button at the bottom
    }

    /// <summary>
    /// The leaderboard. Open it, wait for it, and read why it closed:
    ///
    /// <code>
    /// var showing = leaderboard.Show();
    /// yield return showing;
    /// if (showing.Result == ETournamentScreenResult.Play) StartLevel();
    /// </code>
    ///
    /// No callbacks and nothing to unsubscribe. The coroutine finishes once the screen is fully
    /// gone, so whatever the caller does next cannot land on top of an animation that is still
    /// running — which is the usual way "it closed" and "it is closed" get confused.
    ///
    /// The table is read ONCE, when it opens. Rivals join as they play, so a list that kept up
    /// with every sync would grow rows under the player's finger; the next time they open it, it
    /// is current again. Only the clock keeps ticking.
    /// </summary>
    public sealed class MBTournamentLeaderboard : MonoBehaviour {

        /// <summary>Yieldable, and holds the answer. <see cref="Result"/> is None until the screen
        /// has finished closing.</summary>
        public sealed class Showing : CustomYieldInstruction {
            public ETournamentScreenResult Result { get; internal set; }
            public override bool keepWaiting => Result == ETournamentScreenResult.None;
        }

        const float FadeIn = 0.2f, FadeOut = 0.15f;
        const float ClockSeconds = 1f;

        CanvasGroup _group;
        MBTournamentRow[] _rows;
        RectTransform _content;
        ScrollRect _scroll;
        TMP_Text _clock;

        Showing _showing;
        ETournamentScreenResult _pending;
        string _clockShown;
        bool _laidOut;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            _scroll  = GetComponentInChildren<ScrollRect>(true);
            _content = _scroll != null ? _scroll.content : null;
            _rows    = _content != null ? _content.GetComponentsInChildren<MBTournamentRow>(true)
                                        : new MBTournamentRow[0];
            _clock = Label("$StopWatchText");

            Tap("$LvlButton",  ETournamentScreenResult.Play);
            Tap("$BackButton", ETournamentScreenResult.Closed);

            // Info stays here. It explains this screen, so it opens on top of it and closes back
            // to it — the lobby has no part in that and should not be told.
            var info = transform.RecursiveFindChild("$InfoButton")?.GetComponent<Button>();
            if (info != null) info.onClick.AddListener(OpenInfo);
        }

        /// <summary>TODO: the info screen is not built yet. It opens from here, over this screen.</summary>
        void OpenInfo() => Debug.Log("[Leaderboard] info screen not built yet");

        // Stay active but invisible for the first frames so everything lays out at its real size,
        // then hide until Show(). Laying out while hidden gives zero-sized rows.
        IEnumerator Start() {
            _group.alpha = 0f;
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _laidOut = true;
            if (_showing == null) gameObject.SetActive(false);
        }

        // ---- the one thing this screen is for --------------------------------------------

        /// <summary>
        /// Open it. Yield on what comes back; its Result says why it closed.
        ///
        /// Called while it is already open, it hands back the SAME handle rather than opening a
        /// second time — both callers then wake on the one answer, and neither has to check for
        /// null or wonder which of them the screen belongs to.
        /// </summary>
        public Showing Show() {
            if (_showing != null) return _showing;

            _showing = new Showing();
            _pending = ETournamentScreenResult.None;

            gameObject.SetActive(true);
            Fill();
            StartCoroutine(Open());
            return _showing;
        }

        IEnumerator Open() {
            // A first Show() can arrive before the layout pass has run; let it finish, or the
            // rows are still zero high and the scroll jumps once they are not.
            while (!_laidOut) yield return null;

            ScrollToMe();
            InvokeRepeating(nameof(Tick), 0f, ClockSeconds);

            for (float e = 0f; e < FadeIn; e += Time.unscaledDeltaTime) {
                _group.alpha = Mathf.Clamp01(e / FadeIn);
                yield return null;
            }
            _group.alpha = 1f;
        }

        // Nothing is disabled on the way out: _pending already refuses a second button, and
        // SetActive at the end takes the whole screen out of the raycast anyway.
        IEnumerator Close(ETournamentScreenResult result) {
            CancelInvoke(nameof(Tick));

            for (float e = 0f; e < FadeOut; e += Time.unscaledDeltaTime) {
                _group.alpha = 1f - Mathf.Clamp01(e / FadeOut);
                yield return null;
            }
            _group.alpha = 0f;
            gameObject.SetActive(false);

            // Last, on purpose: the caller wakes up to a screen that is already gone.
            var showing = _showing;
            _showing = null;
            if (showing != null) showing.Result = result;
        }

        // ---- contents ---------------------------------------------------------------------

        void Tick() {
            string now = TournamentManager.TimeLeftText;
            if (now == _clockShown) return;      // the clock moves once a second, the label less
            if (_clock != null) _clock.text = now;
            _clockShown = now;
        }

        /// <summary>Every row the table has, and nothing else — an unused row is switched off, so
        /// the layout shrinks to fit and a short list does not scroll at all.</summary>
        void Fill() {
            var standings = TournamentManager.State?.lastSyncCurrent?.standings;
            var entries = standings?.entries;
            int count = entries != null ? entries.Length : 0;

            for (int i = 0; i < _rows.Length; i++) {
                bool used = i < count;
                _rows[i].gameObject.SetActive(used);
                if (used) _rows[i].Set(i + 1, entries[i].name, entries[i].score, standings.IsMe(i));
            }

            if (count > _rows.Length)
                Debug.LogWarning($"[Leaderboard] {count} players but only {_rows.Length} rows in the prefab");

            _clockShown = null;   // reopened later, the old time is wrong
            Tick();
        }

        /// <summary>Open on the player's own row rather than at the top. In a group of twenty the
        /// one row they came to see can easily be below the fold.</summary>
        void ScrollToMe() {
            if (_scroll == null || _content == null) return;

            int mine = TournamentManager.State?.lastSyncCurrent?.standings?.myIndex ?? -1;
            if (mine < 0) { _scroll.verticalNormalizedPosition = 1f; return; }

            // The fitter only resizes at the end of the frame, and the maths below needs the
            // height NOW.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);

            float view = ((RectTransform)_scroll.viewport).rect.height;
            float scrollable = _content.rect.height - view;
            if (scrollable <= 0f) { _scroll.verticalNormalizedPosition = 1f; return; }   // it all fits

            float rowHeight = _content.rect.height / Mathf.Max(1, CountActiveRows());
            float centred = mine * rowHeight - (view - rowHeight) * 0.5f;
            _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(centred / scrollable);
        }

        int CountActiveRows() {
            int n = 0;
            foreach (var row in _rows) if (row.gameObject.activeSelf) n++;
            return n;
        }

        // ---- the prefab ---------------------------------------------------------------------

        void Tap(string name, ETournamentScreenResult result) {
            var found = transform.RecursiveFindChild(name);
            if (found == null) return;                       // not in the art yet
            var button = found.GetComponent<Button>();
            if (button == null) return;
            button.onClick.AddListener(() => {
                if (_pending != ETournamentScreenResult.None) return;   // already on its way out
                _pending = result;
                StartCoroutine(Close(result));
            });
        }

        TMP_Text Label(string name) {
            var found = transform.RecursiveFindChild(name);
            if (found == null) { Debug.LogError($"[Leaderboard] {name} is missing from the prefab"); return null; }
            return found.GetComponent<TMP_Text>();
        }
    }
}
