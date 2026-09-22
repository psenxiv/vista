using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Editor;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui;

/// <summary>The edited track's Follow Target settings: the character to follow, how the camera turns and aims, its aim height and smoothing.</summary>
internal sealed class FollowTargetWindow : Window
{
    private const float ListWidth = 260f;
    private const float FieldWidth = 70f;

    private readonly CameraSession session;
    private string search = string.Empty;
    private float? smoothingDrag;
    private Guid openedFor;
    private bool dragging;

    public FollowTargetWindow(CameraSession session)
        : base("Follow Target###vista-follow-target", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.session = session;
        RespectCloseHotkey = false;
    }

    /// <summary>Opens the window for the edited track.</summary>
    public void Open()
    {
        openedFor = session.EditedTrackId;
        IsOpen = true;
    }

    /// <summary>Closes when editing ends, another track is edited, or the edited track leaves Follow Target.</summary>
    public override void PreOpenCheck()
    {
        if (!IsOpen) return;
        if (session.Mode != CameraMode.Editing || session.EditedTrackId != openedFor || session.Track.Aim != AimMode.FollowTarget) IsOpen = false;
    }

    /// <summary>Drops an unfinished smoothing drag and ends a live drag, since a closed window never reports either letting go.</summary>
    public override void OnClose()
    {
        smoothingDrag = null;
        LiveDrag.End(session, ref dragging);
    }

    public override void Draw()
    {
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 7f));
        using var selection = ImRaii.PushColor(ImGuiCol.Header, UiColours.AccentAt(0.45f))
            .Push(ImGuiCol.HeaderHovered, UiColours.AccentAt(0.30f))
            .Push(ImGuiCol.HeaderActive, UiColours.AccentAt(0.55f));

        ImGui.BeginDisabled(session.Mode != CameraMode.Editing);
        Report(CharacterPicker.Draw(session, ref search, ListWidth));
        DrawToggles();
        DrawAimHeight();
        DrawSmoothing();
        ImGui.Separator();
        DrawOrbit();
        ImGui.EndDisabled();

        ImGui.Separator();
        if (ImGui.Button("Done", new Vector2(ListWidth, 0f))) IsOpen = false;
    }

    /// <summary>Whether the offset turns with the character, and whether the camera looks at them.</summary>
    private void DrawToggles()
    {
        var turns = session.Track.FollowTurns;
        if (ImGui.Checkbox("Turn with character", ref turns)) Report(session.SetFollowTurns(turns));
        var looks = session.Track.FollowLooks;
        if (ImGui.Checkbox("Look at character", ref looks)) Report(session.SetFollowLooks(looks));
    }

    /// <summary>The aim height above the character's feet: a labelled drag field, live and one undo step per drag.</summary>
    private void DrawAimHeight()
    {
        Label("Aim height");
        var height = session.Track.AimHeight;
        var changed = BorderedField.Draw("follow-aim-height", "Aim height", null, ref height, 0.02f, "%.2f", FieldWidth);
        // Refused once an undo mid-drag has ended the edit; the rest of that drag does nothing.
        LiveDrag.Handle(session, changed, () => _ = session.PreviewAimHeight(height), ref dragging);
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


    /// <summary>The smoothing slider; a drag is applied as one undo step when it lets go.</summary>
    private void DrawSmoothing()
    {
        Label("Smoothing");
        var value = smoothingDrag ?? session.Track.Smoothing;
        ImGui.SetNextItemWidth(FieldWidth);
        if (ImGui.SliderFloat("##smoothing", ref value, 0f, 1f, "%.2f")) smoothingDrag = value;
        if (ImGui.IsItemActive() || smoothingDrag is not { } done) return;
        smoothingDrag = null;
        Report(session.SetSmoothing(done));
    }

    /// <summary>A field's label, with the field following at the right end of the list's width.</summary>
    private static void Label(string text)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(text);
        ImGui.SameLine(ListWidth - FieldWidth + ImGui.GetStyle().WindowPadding.X);
    }

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
