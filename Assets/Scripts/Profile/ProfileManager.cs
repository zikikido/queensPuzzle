using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Common;

namespace qp {

    /// <summary>Why a name was rejected — the popup turns this into a line of text.</summary>
    public enum ENameError {
        Ok,
        TooShort,
        TooLong,
        BadCharacters,   // letters, digits and single spaces only
        NotAllowed,      // hits one of the banned lists
    }

    /// <summary>
    /// The player as other players see them: for now a name, later what they have equipped too.
    /// Editing is local and instant — <see cref="ProfileSyncer"/> carries it to the server in the
    /// background, and a send that fails is retried, so nothing here ever waits on the network.
    /// There is no profile server yet, so that half is dormant; see <see cref="Init"/>.
    ///
    /// The name is generated on first launch (never empty) and can be changed from the Profile
    /// popup. The rules live in <see cref="ProfileConfig"/>, and the same check belongs on the
    /// server — a client can always lie.
    /// </summary>
    public static class ProfileManager {

        static ProfileState _state;
        static ProfileSyncer _syncer;

        /// <summary>Boot (MBStartup). Loads the blob, makes sure there is a name, and sends it if
        /// the server hasn't got this version yet. Calling it twice does nothing.</summary>
        public static void Init() {
            if (_state != null) return;
            _state = ProfileState.Load();

            // TODO: when a profile server exists, this one line turns the whole half back on.
            // _syncer = new ProfileSyncer(new HttpProfileBackend(UserID.GetUserIDLocal()), _state);
            //
            // Left off until then, because there is nothing to push to: the tournament's opponents
            // are recordings under generated names, so nothing anywhere reads this player's name
            // but this player. Sync() is a no-op and syncedRev stays 0 — which is the truth.
            //
            // And NOT a backend that accepts and discards, which is the obvious shortcut and a
            // trap: it would move syncedRev up to rev, so on the day the line above goes in, every
            // player already installed would look synced and never push. Only someone who happened
            // to rename themselves afterwards ever would.

            if (string.IsNullOrEmpty(_state.name)) {
                _state.name = GenerateName();
                _state.rev++;            // the server has never heard of this player
                _state.Save();
            }

            _ = Sync();
        }

        /// <summary>Send the profile to the server — one line over <see cref="ProfileSyncer"/>,
        /// which owns the backend, the sending and the retry. Called at boot and on every change,
        /// a skin later as well as a name.</summary>
        public static Task Sync() => _syncer == null ? Task.CompletedTask : _syncer.Sync();

        /// <summary>The blob. Written only here — the UI reads.</summary>
        public static ProfileState State => _state;

        /// <summary>The name shown to other players. Never empty after <see cref="Init"/>.</summary>
        public static string Name => _state == null ? "" : _state.name;

        /// <summary>The player has named themselves at some point, rather than keeping the one
        /// they were given. Once true, true for good.</summary>
        public static bool Named => _state != null && _state.named;

        /// <summary>Change the name. Returns why it was refused; <see cref="ENameError.Ok"/> means
        /// it was saved and is on its way to the server.</summary>
        public static ENameError SetName(string name) {
            if (_state == null) return ENameError.NotAllowed;   // before Init there is nothing to name
            var error = Validate(name);
            if (error != ENameError.Ok) return error;

            name = name.Trim();
            if (name == _state.name) return ENameError.Ok;   // nothing changed, nothing to send

            _state.name = name;
            _state.rev++;
            _state.named = true;     // they have found the field; stop pointing at it
            _state.Save();
            _ = Sync();
            return ENameError.Ok;
        }

        /// <summary>Check a name without saving it — for live feedback while typing.</summary>
        public static ENameError Validate(string name) {
            var cfg = ProfileConfig.Instance;
            if (cfg == null) return ENameError.NotAllowed;   // no rules shipped = nothing is allowed

            name = (name ?? "").Trim();
            if (name.Length < cfg.minNameLength) return ENameError.TooShort;
            if (name.Length > cfg.maxNameLength) return ENameError.TooLong;
            if (!Shape.IsMatch(name)) return ENameError.BadCharacters;

            return _isClean(name) ? ENameError.Ok : ENameError.NotAllowed;
        }

        /// <summary>Two words and a space — "Lucky Paw". Warm and readable instead of a handle, and
        /// clean by construction, so a generated name can never trip the filter.
        ///
        /// Public because the tournament's rivals are named from here too: a name that came from
        /// somewhere else would make the player's own row look like a different kind of thing.</summary>
        public static string GenerateName() {
            var cfg = ProfileConfig.Instance;
            if (cfg == null || cfg.nameAdjectives == null || cfg.nameAdjectives.Length == 0
                || cfg.nameNouns == null || cfg.nameNouns.Length == 0)
                return "Player";

            var rnd = new System.Random(Guid.NewGuid().GetHashCode());
            return cfg.nameAdjectives[rnd.Next(cfg.nameAdjectives.Length)]
                   + " " + cfg.nameNouns[rnd.Next(cfg.nameNouns.Length)];
        }

        // ---- internal --------------------------------------------------------------------

        // Letters, digits and single spaces between words — our font has nothing else, and a name
        // the game can't draw is worse than one the player didn't pick.
        static readonly Regex Shape = new Regex(@"^[A-Za-z0-9]+( [A-Za-z0-9]+)*$", RegexOptions.Compiled);

        // Built once from the config. Anywhere: strings with no innocent host, so a plain search.
        // WholeWord: between word boundaries, and the (?:…)+ makes "sexsex" a match while "Essex"
        // stays clean — the repeat rule for free.
        static Regex _anywhere, _wholeWord;

        /// <summary>The filter. Works on the normalised name, so "F.U.C.K", "s#e#x" and "fuuuck"
        /// all read as the plain word; see <see cref="_normalise"/>. The space-stripped form is
        /// checked too, which is what catches "S E X" while leaving "Essex" alone.</summary>
        static bool _isClean(string name) {
            _buildFilters();
            string flat = _normalise(name);                      // "best ass" -> "bestass"
            if (flat.Length == 0) return true;

            if (_anywhere != null && _anywhere.IsMatch(flat)) return false;
            if (_wholeWord == null) return true;
            return !_wholeWord.IsMatch(_normalise(name, keepSpaces: true)) && !_wholeWord.IsMatch(flat);
        }

        static void _buildFilters() {
            if (_anywhere != null || _wholeWord != null) return;
            var cfg = ProfileConfig.Instance;
            _anywhere = _union(cfg.bannedAnywhere, @"(?:{0})");
            _wholeWord = _union(cfg.bannedWholeWord, @"\b(?:{0})+\b");
        }

        static Regex _union(string[] words, string pattern) {
            if (words == null || words.Length == 0) return null;
            var escaped = new string[words.Length];
            for (int i = 0; i < words.Length; i++) escaped[i] = Regex.Escape(words[i]);
            return new Regex(string.Format(pattern, string.Join("|", escaped)), RegexOptions.Compiled);
        }

        /// <summary>Lowercase, digits folded back to the letters they imitate, everything else
        /// dropped, and runs of a letter squeezed to one — so "Fuuu_c4k" reads as "fuck".</summary>
        static string _normalise(string name, bool keepSpaces = false) {
            var sb = new StringBuilder(name.Length);
            char last = '\0';
            foreach (char raw in name.ToLowerInvariant()) {
                char c = raw;
                switch (c) {
                    case '4': case '@': c = 'a'; break;
                    case '8': c = 'b'; break;
                    case '3': c = 'e'; break;
                    case '1': case '!': case '|': c = 'i'; break;
                    case '0': c = 'o'; break;
                    case '5': case '$': c = 's'; break;
                    case '7': c = 't'; break;
                }
                if (c == ' ' && keepSpaces) { sb.Append(c); last = '\0'; continue; }
                if (c < 'a' || c > 'z') continue;
                if (c == last) continue;              // fuuuck -> fuck
                sb.Append(c);
                last = c;
            }
            return sb.ToString();
        }
    }
}
