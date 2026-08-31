// Boot-time helper: reports whether a native popup is currently covering the Unity view — either
// a view controller presented over Unity's root (alerts, forms presented from it) or a separate
// visible UIWindow at/above Unity's level (some SDKs, Google UMP included, show their form in a
// window of their own). OS permission alerts (notifications, ATT) never show up here — those
// resign the app active and pause the engine, so the boot freezes on its own while they're up.
#import "UnityAppController.h"

static bool _kidoWindowIsPopup(UIWindow *w, UIWindow *unityWindow) {
    if (w == unityWindow || w.hidden || w.rootViewController == nil) return false;
    // The system's keyboard/text-effects windows are not popups.
    NSString *cls = NSStringFromClass([w class]);
    if ([cls containsString:@"Keyboard"] || [cls containsString:@"TextEffects"]) return false;
    return w.windowLevel >= unityWindow.windowLevel;
}

extern "C" bool _kidoIsModalPresented() {
    UIViewController *root = UnityGetGLViewController();
    if (root != nil) {
        UIViewController *presented = root.presentedViewController;
        if (presented != nil && !presented.isBeingDismissed) return true;
    }

    UIWindow *unityWindow = UnityGetMainWindow();
    if (unityWindow == nil) return false;
    for (UIWindow *w in UIApplication.sharedApplication.windows) {
        if (_kidoWindowIsPopup(w, unityWindow)) return true;
    }
    return false;
}
