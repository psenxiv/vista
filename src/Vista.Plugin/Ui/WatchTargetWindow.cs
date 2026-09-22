using Vista.Core.Tracks;
using Vista.Plugin.Session;

namespace Vista.Plugin.Ui;

/// <summary>The edited track's Watch Target settings: the character to watch, its aim height and smoothing.</summary>
internal sealed class WatchTargetWindow(CameraSession session)
    : TargetWindow(session, "Watch Target###vista-watch-target", AimMode.WatchTarget, "aim-height");
