using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Plugin.Editor;
using Vista.Plugin.Ui.Widgets;
using Dalamud.Bindings.ImGui;

using static Vista.Plugin.Ui.Widgets.Refusal;
namespace Vista.Plugin.Ui.Windows;

/// <summary>The edited track's Follow Target settings: the character to follow, how the camera turns and aims, its aim height and smoothing.</summary>
internal sealed class FollowTargetWindow : TargetWindow
{
    public FollowTargetWindow(SessionState session, NearbyCharacters characters)
        : base(session, characters, "Follow Target###vista-follow-target", AimMode.FollowTarget, "follow-aim-height")
    {
    }

    /// <summary>Whether the offset turns with the character, and whether the camera looks at them.</summary>
    protected override void DrawAboveAimHeight()
    {
        var turns = session.Track.FollowTurns;
        if (ImGui.Checkbox("Turn with character", ref turns)) Report(session.SetFollowTurns(turns));
        var looks = session.Track.FollowLooks;
        if (ImGui.Checkbox("Look at character", ref looks)) Report(session.SetFollowLooks(looks));
    }

    /// <summary>The orbit, under a separator.</summary>
    protected override void DrawBelowSmoothing()
    {
        ImGui.Separator();
        DrawOrbit();
    }

    /// <summary>The orbit: distance, height and angle round the character, each dragged live as one undo step.</summary>
    private void DrawOrbit()
    {
        var orbit = session.FollowOrbit;
        var current = orbit ?? default;
        var width = (ListWidth - (ImGui.GetStyle().ItemSpacing.X * 2f)) / 3f;
        ImGui.BeginDisabled(orbit is null);

        var distance = current.Distance;
        var changed = BorderedField.Draw("orbit-distance", "Distance", EditorColours.AxisX, ref distance, 0.05f, "%.2f", width);
        LiveDrag.Handle(session, changed, () => _ = session.PreviewFollowOrbit(current with { Distance = distance }), ref dragging);

        ImGui.SameLine();
        var height = current.Height;
        changed = BorderedField.Draw("orbit-height", "Height", EditorColours.AxisY, ref height, 0.05f, "%.2f", width);
        LiveDrag.Handle(session, changed, () => _ = session.PreviewFollowOrbit(current with { Height = height }), ref dragging);

        ImGui.SameLine();
        var degrees = current.Angle * 180f / MathF.PI;
        changed = BorderedField.Draw("orbit-angle", "Angle", EditorColours.AxisZ, ref degrees, 0.5f, "%.0f°", width);
        LiveDrag.Handle(session, changed, () => _ = session.PreviewFollowOrbit(current with { Angle = degrees * MathF.PI / 180f }), ref dragging);

        ImGui.EndDisabled();
    }
}
