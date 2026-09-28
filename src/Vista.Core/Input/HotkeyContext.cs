using Vista.Core.Session;

namespace Vista.Core.Input;

/// <summary>What the session is doing when a key goes down, for deciding which hotkey the press is.</summary>
public readonly record struct HotkeyContext(CameraMode Mode, bool OverlayShown, bool PointsSelected, bool GizmoTarget)
{
    /// <summary>The context <paramref name="session"/> is in now.</summary>
    public static HotkeyContext Of(SessionState session) =>
        new(
            session.Mode,
            session.OverlayShown,
            session.Selection.Points.Count > 0,
            session.Selection.Point is not null || session.Selection.Anchor is AnchorKind.Scene or AnchorKind.Track
        );
}
