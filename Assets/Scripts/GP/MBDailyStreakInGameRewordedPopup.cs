using System.Collections;
using UnityEngine;

public class MBDailyStreakInGameRewordedPopup : MonoBehaviour
{
    private CanvasGroup _group;

    bool _showing;   // a real fail is on screen (guards the layout pass from hiding it)

    private void Awake() {
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;   // invisible, but alive for the layout pass
    }

    IEnumerator Start() {
        // wait for UI to refresh our layout
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        _group.alpha = 1f;
        if (!_showing) gameObject.SetActive(false);   // layout pass only — hidden until Show()
    }
}
