using Vista.Core.Session;
using Vista.Core.Tracks.Aiming;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The edited track's Watch Target settings: the character to watch, its aim height and smoothing.</summary>
internal sealed class WatchTargetWindow(SessionState session, NearbyCharacters characters)
    : TargetWindow(session, characters, "Watch Target###vista-watch-target", AimMode.WatchTarget, "aim-height");
