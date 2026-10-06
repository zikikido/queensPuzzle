using System;
using System.Threading.Tasks;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// The one place that talks to the profile server: it decides whether there is anything to send,
    /// sends it, and marks the blob as confirmed. Nothing else touches
    /// <see cref="IProfileBackend"/> — <see cref="ProfileManager"/> edits the profile, this carries
    /// it over, and <see cref="MBProfileRetry"/> only says when to try again.
    /// A failure changes nothing: the profile stays unsent and goes out on the next try.
    /// </summary>
    public class ProfilePusher {

        readonly IProfileBackend _backend;
        readonly ProfileState _state;

        public ProfilePusher(IProfileBackend backend, ProfileState state) {
            _backend = backend;
            _state = state;
        }

        /// <summary>A send is in flight.</summary>
        public bool Running { get; private set; }

        /// <summary>Send the profile if the server hasn't got this version. Safe to call any time:
        /// nothing to send, no connection, or one already in flight all return at once.</summary>
        public async Task PushIfUnsent() {
            if (!_state.unsent || Running) return;

            var net = InternetConnection.Instance;
            if (net == null || !net.HasInternet) return;   // the next try will catch it

            Running = true;
            try {
                await _backend.Push(new PlayerProfile { name = _state.name });
                _state.unsent = false;
                _state.Save();
            } catch (Exception e) {
                // No network is an ordinary state, not a defect — it stays a log.
                Debug.LogWarning($"[ProfilePusher] push failed, will retry: {e.Message}");
            } finally {
                Running = false;
            }
        }
    }
}
