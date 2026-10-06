using System;
using System.Threading.Tasks;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// The one place that talks to the profile server. Call <see cref="Sync"/> whenever the profile
    /// changes — a new name today, an equipped skin later; everything else it handles itself.
    ///
    /// The retry lives here, not in a MonoBehaviour: a failed send (no connection yet at boot, a
    /// server that is down) is retried every <see cref="RetrySeconds"/> until the server confirms,
    /// and the loop stops the moment nothing is unsent. One loop at a time, and none at all while
    /// everything is confirmed.
    ///
    /// Nothing comes back yet. When owning an item has to come FROM the server, this is where the
    /// answer lands, and the name stays right.
    /// </summary>
    public class ProfileSyncer {

        const int RetrySeconds = 5;

        readonly IProfileBackend _backend;
        readonly ProfileState _state;

        bool _running;      // a send is in flight
        bool _retrying;     // the retry loop is alive

        public ProfileSyncer(IProfileBackend backend, ProfileState state) {
            _backend = backend;
            _state = state;
        }

        /// <summary>Send the profile if the server hasn't got this version. Safe to call any time:
        /// nothing to send, no connection, or one already in flight all return at once — and a
        /// failure leaves a retry running behind it.</summary>
        public async Task Sync() {
            if (_state.rev == _state.syncedRev || _running) return;

            var net = InternetConnection.Instance;
            if (net == null || !net.HasInternet) { _ = _retryUntilSent(); return; }

            _running = true;
            try {
                int sentRev = _state.rev;
                await _backend.Push(new PlayerProfile { name = _state.name });

                // Confirm exactly what went out. An edit made while this was in flight moved rev
                // past it, so the profile stays unsent and the next call takes it.
                _state.syncedRev = sentRev;
                _state.Save();
                if (_state.rev != sentRev) { _running = false; await Sync(); return; }
            } catch (Exception e) {
                // No network is an ordinary state, not a defect — it stays a log, and the loop below
                // keeps trying.
                Debug.LogWarning($"[ProfileSyncer] send failed, will retry: {e.Message}");
                _ = _retryUntilSent();
            } finally {
                _running = false;
            }
        }

        async Task _retryUntilSent() {
            if (_retrying) return;
            _retrying = true;
            try {
                while (_state.rev != _state.syncedRev && Application.isPlaying) {
                    await Task.Delay(RetrySeconds * 1000);
                    if (_state.rev == _state.syncedRev) break;
                    await Sync();
                }
            } finally {
                _retrying = false;
            }
        }
    }
}
