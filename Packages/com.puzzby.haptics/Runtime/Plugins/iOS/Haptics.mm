#import <UIKit/UIKit.h>

// type: 0 Selection, 1 Light, 2 Medium, 3 Heavy, 4 Success, 5 Warning, 6 Failure
extern "C" void _hapticImpact(int type) {
    if (@available(iOS 10.0, *)) {
        switch (type) {
            case 0: {
                UISelectionFeedbackGenerator *g = [[UISelectionFeedbackGenerator alloc] init];
                [g selectionChanged];
                break;
            }
            case 1: {
                UIImpactFeedbackGenerator *g = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
                [g impactOccurred];
                break;
            }
            case 2: {
                UIImpactFeedbackGenerator *g = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
                [g impactOccurred];
                break;
            }
            case 3: {
                UIImpactFeedbackGenerator *g = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
                [g impactOccurred];
                break;
            }
            case 4: {
                UINotificationFeedbackGenerator *g = [[UINotificationFeedbackGenerator alloc] init];
                [g notificationOccurred:UINotificationFeedbackTypeSuccess];
                break;
            }
            case 5: {
                UINotificationFeedbackGenerator *g = [[UINotificationFeedbackGenerator alloc] init];
                [g notificationOccurred:UINotificationFeedbackTypeWarning];
                break;
            }
            case 6: {
                UINotificationFeedbackGenerator *g = [[UINotificationFeedbackGenerator alloc] init];
                [g notificationOccurred:UINotificationFeedbackTypeError];
                break;
            }
        }
    }
}
