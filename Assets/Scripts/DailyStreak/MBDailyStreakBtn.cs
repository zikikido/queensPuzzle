using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Drives the lobby's daily-streak button — pure display over <see cref="DailyStreakManager"/>.
    /// Exactly one child root is active for the current status:
    ///   $DailyButtonActive     Status == Active  — streak count, progress, next reward
    ///   $DailyButtonOffline    Status == Offline — no trusted server time
    /// Ticks slowly so a status flip (server time syncing) while the lobby sits open just works.
    /// </summary>
    public class MBDailyStreakBtn : MonoBehaviour {

        GameObject _active, _offline;

        // What the UI currently shows. Sentinel (-1) so the first Apply always paints.
        EStreakStatus _shownStatus = (EStreakStatus)(-1);

        void Awake() {
            var activeT = transform.RecursiveFindChild("$DailyButtonActive");
            var offlineT = transform.RecursiveFindChild("$DailyButtonOffline");
            _active = activeT != null ? activeT.gameObject : null;
            _offline = offlineT != null ? offlineT.gameObject : null;
        }

        void OnEnable() {
            Apply();
            InvokeRepeating(nameof(Tick), 1f, 1f);
        }

        void OnDisable() => CancelInvoke(nameof(Tick));

        // Server time can land a second or two after the lobby opens — flip to Active when it does.
        void Tick() => Apply();

        // Rebuild the UI only when the status changed since it was last shown.
        void Apply() {
            var status = DailyStreakManager.Status;
            if (status == _shownStatus) return;
            _shownStatus = status;

            if (_active != null) _active.SetActive(status == EStreakStatus.Active);
            if (_offline != null) _offline.SetActive(status == EStreakStatus.Offline);

            // TODO: when Active, fill in streak count / progress bar / next reward icon.
        }
    }
}
