#if DEBUG
using Vista.Core.Session;

namespace Vista.Core.SelfTest;

/// <summary>What the plugin read after the error handling check's fault: why Vista stopped, the mode, the movement counter before and after, the game UI before and after (null when unreadable), how many input hooks are enabled, and whether the player was told.</summary>
public readonly record struct SelfTestAfterFault(
    string? StopReason,
    CameraMode Mode,
    bool OwnsCamera,
    int CounterBefore,
    int CounterAfter,
    bool? UiBefore,
    bool? UiAfter,
    int HooksEnabled,
    bool Notified
);
#endif
