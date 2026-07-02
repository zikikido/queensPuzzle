#import <UIKit/UIKit.h>
#import <CoreHaptics/CoreHaptics.h>

// Core Haptics engine, created lazily and kept alive between plays.
static CHHapticEngine *g_engine = nil;

static void EnsureEngine(void) API_AVAILABLE(ios(13.0)) {
    if (g_engine != nil) return;
    if (!CHHapticEngine.capabilitiesForHardware.supportsHaptics) return;

    NSError *err = nil;
    g_engine = [[CHHapticEngine alloc] initAndReturnError:&err];
    if (err != nil) { g_engine = nil; return; }

    // Recreate on stop, restart on reset — keeps the engine robust across interruptions.
    g_engine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) { g_engine = nil; };
    g_engine.resetHandler   = ^{ NSError *e = nil; [g_engine startAndReturnError:&e]; };

    [g_engine startAndReturnError:&err];
    if (err != nil) { g_engine = nil; }
}

extern "C" int _hapticSupported(void) {
    if (@available(iOS 13.0, *)) return CHHapticEngine.capabilitiesForHardware.supportsHaptics ? 1 : 0;
    return 0;
}

// Kept warm by _hapticPrepare so the UIKit fallback fires without spin-up latency.
static UIImpactFeedbackGenerator *g_prep = nil;

// Warm the engine so the next play is instant. Core Haptics: start the engine now. Otherwise
// (iOS < 13) prime a UIKit generator.
extern "C" void _hapticPrepare(void) {
    if (@available(iOS 13.0, *)) {
        if (CHHapticEngine.capabilitiesForHardware.supportsHaptics) { EnsureEngine(); return; }
    }
    if (@available(iOS 10.0, *)) {
        if (g_prep == nil) g_prep = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        [g_prep prepare];
    }
}

// Best path: build a CHHapticPattern of transient/continuous events (intensity + sharpness).
extern "C" void _hapticPlay(float *intensity, float *sharpness, float *delay, float *duration, int count) {
    if (count <= 0) return;

    if (@available(iOS 13.0, *)) {
        if (CHHapticEngine.capabilitiesForHardware.supportsHaptics) {
            EnsureEngine();
            if (g_engine == nil) return;

            NSMutableArray<CHHapticEvent *> *events = [NSMutableArray arrayWithCapacity:count];
            for (int i = 0; i < count; i++) {
                CHHapticEventParameter *pi =
                    [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity value:intensity[i]];
                CHHapticEventParameter *ps =
                    [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness value:sharpness[i]];

                CHHapticEvent *e;
                if (duration[i] > 0.0f) {
                    e = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                                      parameters:@[pi, ps]
                                                    relativeTime:delay[i]
                                                        duration:duration[i]];
                } else {
                    e = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticTransient
                                                      parameters:@[pi, ps]
                                                    relativeTime:delay[i]];
                }
                [events addObject:e];
            }

            NSError *err = nil;
            CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:events parameters:@[] error:&err];
            if (err != nil) return;
            id<CHHapticPatternPlayer> player = [g_engine createPlayerWithPattern:pattern error:&err];
            if (err != nil) return;
            [player startAtTime:0 error:&err];
            return;
        }
    }

    // Fallback (iOS 10-12 or no Core Haptics): UIKit generators, approximating the first event.
    if (@available(iOS 10.0, *)) {
        float inten = intensity[0], sharp = sharpness[0];
        if (sharp >= 0.7f && inten <= 0.5f) {
            [[[UISelectionFeedbackGenerator alloc] init] selectionChanged];
        } else {
            UIImpactFeedbackStyle style = inten > 0.66f ? UIImpactFeedbackStyleHeavy
                                        : inten > 0.33f ? UIImpactFeedbackStyleMedium
                                                        : UIImpactFeedbackStyleLight;
            [[[UIImpactFeedbackGenerator alloc] initWithStyle:style] impactOccurred];
        }
    }
}
