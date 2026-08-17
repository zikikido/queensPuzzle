using System;
using System.Collections.Generic;

namespace qp {

    /// <summary>
    /// The install's attribution as resolved by Singular (device attribution callback) — which
    /// network / campaign brought this user. Persisted once (AppData.SingularSource) and attached
    /// to every events-server event as `singular_source`.
    ///
    /// Keys per Singular's SDK reference (withSingularDeviceAttribution): network, campaign_id,
    /// campaign_name, click_timestamp. Organic installs carry only network == "organic".
    /// Adgroup/creative are NOT delivered to the device — those are report/export only.
    /// </summary>
    [Serializable]
    public class SingularSource {
        public string network;         // e.g. "AppLovin", "Google Ads", "organic"; "" = not resolved yet
        public string campaign_name;   // attributed installs only
        public string campaign_id;     // attributed installs only
        public long   click_time_sec;  // click_timestamp (epoch µs) / 1e6; 0 when absent

        public bool IsOrganic => string.Equals(network, "organic", StringComparison.OrdinalIgnoreCase);

        public static SingularSource FromCallback(Dictionary<string, object> info) {
            string S(string key) => info.TryGetValue(key, out var v) && v != null ? v.ToString() : null;
            long clickUs = info.TryGetValue("click_timestamp", out var ts) && ts != null
                        && long.TryParse(ts.ToString(), out var us) ? us : 0;
            return new SingularSource {
                network        = S("network"),
                campaign_name  = S("campaign_name"),
                campaign_id    = S("campaign_id"),
                click_time_sec = clickUs / 1_000_000,
            };
        }

        public override string ToString() =>
            $"network={network} campaign={campaign_name}({campaign_id}) click={click_time_sec}";
    }
}
