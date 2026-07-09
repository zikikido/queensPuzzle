using Common;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    /// <summary>
    /// The first-play tutorial: on the very first level it hides the boosts and the top bar,
    /// then walks the player hint-by-hint (spotlight + lock + hand, all via MBToturial) until
    /// the first 3 queens stand — after that the chrome returns and the player finishes alone.
    ///
    /// Plain static class — no scene object, no component. MBGameplay calls TryBegin after the
    /// bloom and hosts the coroutine.
    /// </summary>
    public static class MBFirstPlayToturial {

        const int GuidedQueens = 3;
        const bool ForceRun = false;   // testing: run even when progress is past level 1

        static Button _hintForFree = null;

        /// <summary>Called by MBGameplay once the board is ready. First level only.</summary>
        public static void TryBegin(MBGameplay gp) {
            if (!ForceRun && AppData.LevelIdx.Value != 0) return;
            gp.StartCoroutine(Run(gp));
        }

        static IEnumerator Run(MBGameplay gp) {
            while (MBToturial.instance == null) yield return null;
            var tut = MBToturial.instance;

            ShowChrome(false);           // no boosts, no top bar — just the board and the guide
            tut.SetHandVisible(true);    // first play: the hand demonstrates…
            tut.SetApplyVisible(false);  // …and the player must do it — no Apply shortcut

            // guide hint-by-hint (X steps included) until the first queens stand
            while (gp.CountQueens() < GuidedQueens) {
                gp.OpenHint();
                yield return null;                                    // let the step open
                while (tut.gameObject.activeSelf) yield return null;  // closes when the player did it
            }

            var btn = MBGameplay.instance.transform.RecursiveFindChild<Button>("$HintForFree");

            btn.gameObject.SetActive(true);
            btn.onClick.AddListener(() => {
                gp.OpenHint();
            });

            while (gp.Remaining > 0) {
                yield return null;
            }

            tut.SetHandVisible(false);   // back to normal hint behaviour (Apply, no hand)
            tut.SetApplyVisible(true);

            btn.gameObject.SetActive(false);
            ShowChrome(true);            
        }

        static void ShowChrome(bool on) {
            foreach (var boost in Object.FindObjectsByType<MBBoostButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                boost.gameObject.SetActive(on);
            var top = Object.FindAnyObjectByType<MBTopBar>(FindObjectsInactive.Include);
            if (top != null) top.gameObject.SetActive(on);
        }
    }
}
