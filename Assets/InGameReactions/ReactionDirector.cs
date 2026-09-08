using System.Collections;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Rare in-game reactions — the game "noticing" a nice move. Owns the whole feature:
    /// gameplay feeds it events, it decides AND plays. Adaptive per player: thresholds scale
    /// off the user's own average gap between correct actions this session. Rules:
    ///   Unstuck  — first correct queen OR X after a long think (4x their pace, 15–60s).
    ///   Comeback — 2nd correct queen while surviving on the last bone.
    ///   Streak   — 3rd fast-paced correct queen in a row, only in the first 60% of the
    ///              board (the endgame cascade is fast for everyone).
    ///   Clean    — 6 correct queens in a row with no mistakes and no boosts — but only for
    ///              a player whose recent levels HAD mistakes (going clean is their moment;
    ///              an always-clean player would get this every level).
    /// Fires every time a rule is earned. Early levels never fire, never with ≤2 queens
    /// left (the win popup owns the ending). Effects: every IInGameReactionFX in the scene,
    /// collected on first use — a random one plays, a beat after the move's own feedback.
    /// Callers pass Time.unscaledTime — it freezes while the app is backgrounded, so away
    /// time never counts as thinking time. Session-only state; nothing persisted.
    /// </summary>
    public class ReactionDirector {

        enum EKind { None, Unstuck, Comeback, Streak }

        const int MinLevel = 10;
        const float StuckFactor = 4f, StuckMin = 15f, StuckMax = 60f;
        const float FastFactor = 0.5f, FastMin = 2f, FastMax = 6f;
        const float DefaultPace = 8f;      // assumed pace until the session has measured one
        const float IgnoreGapOver = 120f;  // absurd gaps don't teach the average
        const float PlayDelay = 0.25f;     // after the move's own sound/haptic — reads as a response

        int _level;
        float _avgGap = -1f;               // running average of gaps between correct actions
        float _progressAnchor;             // stuck clock: now - anchor = time without progress
        float _lastCorrectAt = -1f;        // previous correct action, for pace + streak gaps
        float _fastStreak;                 // combo credit: fast queen +1, fast X +1/3
        int _cleanStreak;                  // consecutive correct queens, no wrongs/boosts between
        int _levelsSinceMistake = 99;      // 99 = no mistake seen this session
        bool _wrongThisLevel;
        int _correctSinceLastBone = -1;    // -1 = not on the last bone
        IInGameReactionFX[] _fx;           // scene effects, collected by interface on first use

        public void OnLevelStart(int levelIdx, float now) {
            _level = levelIdx;
            _progressAnchor = now;
            _lastCorrectAt = -1f;
            _fastStreak = 0;
            _cleanStreak = 0;
            if (_wrongThisLevel) _levelsSinceMistake = 0;
            else if (_levelsSinceMistake < 99) _levelsSinceMistake++;
            _wrongThisLevel = false;
            _correctSinceLastBone = -1;
            _fx = null;   // the scene may have been rebuilt
        }

        public void OnWrong(int bonesLost, int maxBones) {
            _fastStreak = 0;
            _cleanStreak = 0;
            _wrongThisLevel = true;
            if (bonesLost == maxBones - 1) _correctSinceLastBone = 0;   // last bone — watch the comeback
        }

        /// <summary>A boost revealed progress for the player — that thinking wasn't theirs.</summary>
        public void OnBoost(float now) {
            _progressAnchor = now;
            _fastStreak = 0;
            _cleanStreak = 0;
        }

        /// <summary>Correct X: worth a third of a queen — nudges the stuck clock, adds 1/3
        /// combo credit when fast (never breaks a combo), can fire unstuck.</summary>
        public void OnCorrectX(float now, Vector3 pos) {
            bool stuck = _isStuck(now);
            float gap = _lastCorrectAt < 0f ? float.MaxValue : now - _lastCorrectAt;
            if (gap <= Mathf.Clamp(_pace() * FastFactor, FastMin, FastMax))
                _fastStreak += 1f / 3f;
            _progressAnchor += (now - _progressAnchor) / 3f;
            _observePace(now);
            if (stuck && _gateOpen()) _fire(pos, now);
        }

        /// <summary>Correct queen that did NOT win the level.</summary>
        public void OnCorrectQueen(int placed, int total, float now, Vector3 pos) {
            bool stuck = _isStuck(now);
            float gap = _lastCorrectAt < 0f ? float.MaxValue : now - _lastCorrectAt;
            bool fast = gap <= Mathf.Clamp(_pace() * FastFactor, FastMin, FastMax);
            _fastStreak = fast ? _fastStreak + 1f : 0f;
            _cleanStreak++;
            if (_correctSinceLastBone >= 0) _correctSinceLastBone++;
            _progressAnchor = now;
            _observePace(now);

            if (total - placed <= 2) return;   // endgame belongs to the win popup
            if (!_gateOpen()) return;
            bool mistakesAreNormal = _wrongThisLevel || _levelsSinceMistake <= 3;
            if (stuck || _correctSinceLastBone == 2
                || (_fastStreak >= 2.99f && placed <= total * 0.6f)
                || (_cleanStreak >= 6 && mistakesAreNormal))
                _fire(pos, now);
        }

        // ---- decision helpers ----

        bool _isStuck(float now) =>
            now - _progressAnchor >= Mathf.Clamp(_pace() * StuckFactor, StuckMin, StuckMax);

        bool _gateOpen() => _level >= MinLevel;

        float _pace() => _avgGap > 0f ? _avgGap : DefaultPace;

        void _observePace(float now) {
            if (_lastCorrectAt >= 0f) {
                float gap = now - _lastCorrectAt;
                if (gap > 0.01f && gap <= IgnoreGapOver)
                    _avgGap = _avgGap <= 0f ? gap : Mathf.Lerp(_avgGap, gap, 0.25f);
            }
            _lastCorrectAt = now;
        }

        // ---- playing ----

        void _fire(Vector3 pos, float now) {
            _progressAnchor = now;   // a reaction acknowledges the progress — fresh stuck clock
            _fastStreak = 0f;
            _cleanStreak = 0;
            _correctSinceLastBone = -1;

            if (_fx == null) {
                var list = new System.Collections.Generic.List<IInGameReactionFX>();
                foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (mb is IInGameReactionFX fx) list.Add(fx);
                _fx = list.ToArray();
            }
            if (_fx.Length == 0) return;

            var pick = _fx[Random.Range(0, _fx.Length)];   // any effect fits any reaction
            if (pick is MonoBehaviour host && host != null)
                host.StartCoroutine(_playDelayed(pick, pos));
        }

        IEnumerator _playDelayed(IInGameReactionFX fx, Vector3 pos) {
            yield return new WaitForSecondsRealtime(PlayDelay);
            fx.Play(pos);
        }
    }
}
