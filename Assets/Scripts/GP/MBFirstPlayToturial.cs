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
        const float StepPause = 0.8f;  // linger on a done step so the player sees what he did
        const bool ForceRun = true;    // TESTING - set back to false before shipping

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
            tut.SetApplyVisible(false);  // …and the player must do it — no Apply shortcut
            tut.AutoHide = false;        // curtain stays up between steps — we lift it ourselves

            // guide hint-by-hint (X steps included) until the first queens stand
            while (gp.CountQueens() < GuidedQueens) {
                tut.SetHandVisible(true);
                if (!gp.OpenHint()) break;                            // no simple step — bail out
                yield return null;                                    // let the step open
                while (!tut.StepDone()) yield return null;            // the player does the move
                tut.SetHandVisible(false);
                yield return new WaitForSecondsRealtime(StepPause);   // let it sink in before the next step
            }

            tut.Hide();                  // guided phase over — lift the curtain
            tut.AutoHide = true;         // hints close on their own again

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
