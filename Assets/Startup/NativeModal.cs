using System.Runtime.InteropServices;

namespace qp {

    // True while a native view controller is presented over the Unity view on iOS — during boot
    // that means the AppLovin/UMP consent form. It's an in-app modal there, so the engine keeps
    // running underneath (unlike OS permission alerts, which resign the app active and pause it).
    // Android needs no equivalent: the consent flow pauses the Unity activity outright, so boot
    // timeouts freeze on their own — IsShowing is simply false everywhere but an iOS device.
    public static class NativeModal {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern bool _kidoIsModalPresented();
        public static bool IsShowing => _kidoIsModalPresented();
#else
        public static bool IsShowing => false;
#endif
    }
}
