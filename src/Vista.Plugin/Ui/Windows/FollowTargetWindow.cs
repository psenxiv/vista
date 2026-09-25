using Dalamud.Bindings.ImGui;
using Vista.Core.Camera;
using Vista.Core.Display;
using Vista.Core.Session;
using Vista.Core.Tracks.Aiming;
using Vista.Plugin.Editor;
using Vista.Plugin.Ui.Widgets;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The edited track's Follow Target settings: the character to follow, how the camera turns and aims, its aim height and smoothing.</summary>
internal sealed class FollowTargetWindow : TargetWindow
{
    public FollowTargetWindow(SessionState session, NearbyCharacters characters)
        : base(session, characters, "Follow Target###vista-follow-target", AimMode.FollowTarget, "follow-aim-height")
    { }

    /// <summary>Whether the offset turns with the character, and whether the camera looks at them.</summary>
    protected override void DrawAboveAimHeight()
    {
        var turns = session.Track.FollowTurns;
        if (ImGui.Checkbox("Turn with character", ref turns))
            Report(session.SetFollowTurns(turns));
        var looks = session.Track.FollowLooks;
        if (ImGui.Checkbox("Look at character", ref looks))
            Report(session.SetFollowLooks(looks));
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
        var width = (Layout.DialogWidth - (ImGui.GetStyle().ItemSpacing.X * 2f)) / 3f;
        ImGui.BeginDisabled(orbit is null);

        OrbitField(
            "orbit-distance",
            "Distance",
            EditorColours.AxisX,
            current.Distance,
            0.05f,
            Units.YalmsField,
            width,
            distance => current with { Distance = distance }
        );
        ImGui.SameLine();
        OrbitField(
            "orbit-height",
            "Height",
            EditorColours.AxisY,
            current.Height,
            0.05f,
            Units.YalmsField,
            width,
            height => current with { Height = height }
        );
        ImGui.SameLine();
        OrbitField(
            "orbit-angle",
            "Angle",
            EditorColours.AxisZ,
            Angles.Degrees(current.Angle),
            0.5f,
            Units.DegreesField,
            width,
            degrees => current with { Angle = Angles.Radians(degrees) }
        );

        ImGui.EndDisabled();
    }

    /// <summary>One orbit field, dragged live as one undo step, previewing the orbit <paramref name="set"/> makes of its value.</summary>
    private void OrbitField(
        string id,
        string name,
        uint border,
        float value,
        float speed,
        string format,
        float width,
        Func<float, Orbit> set
    )
    {
        LiveDrag.Field(
            session,
            value,
            (ref float edited) => BorderedField.Draw(id, name, border, ref edited, speed, format, width),
            edited => _ = session.PreviewFollowOrbit(set(edited)),
            ref dragging
        );
    }
}
