using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Vista.Core.Display;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks.Aiming;
using Vista.Plugin.Ui.Widgets;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The panel a target aim mode edits: the character to aim at, its aim height and smoothing.</summary>
internal abstract class TargetWindow : Window
{
    protected readonly SessionState session;
    private readonly NearbyCharacters characters;
    private readonly AimMode aim;
    private readonly string aimHeightId;
    private readonly PendingEdit<float> smoothing;
    private string search = string.Empty;
    private Guid openedFor;

    /// <summary>True while a drag field of this window is held; shared so a close ends whichever one it is.</summary>
    protected bool dragging;

    protected TargetWindow(
        SessionState session,
        NearbyCharacters characters,
        string name,
        AimMode aim,
        string aimHeightId
    )
        : base(name, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.session = session;
        this.characters = characters;
        this.aim = aim;
        this.aimHeightId = aimHeightId;
        smoothing = new PendingEdit<float>(() => session.Mode == CameraMode.Editing);
        RespectCloseHotkey = false;
    }

    /// <summary>Opens the window for the edited track.</summary>
    public void Open()
    {
        openedFor = session.EditedTrackId;
        IsOpen = true;
    }

    /// <summary>Closes when editing ends, another track is edited, or the edited track leaves this aim mode.</summary>
    public override void PreOpenCheck()
    {
        if (!IsOpen)
            return;
        if (session.Mode != CameraMode.Editing || session.EditedTrackId != openedFor || session.Track.Aim != aim)
            IsOpen = false;
    }

    /// <summary>Drops an unfinished smoothing drag and ends a live drag, since a closed window never reports either letting go.</summary>
    public override void OnClose()
    {
        smoothing.Clear();
        LiveDrag.End(session, ref dragging);
    }

    public override void Draw()
    {
        using var style = WindowStyle.Push();

        ImGui.BeginDisabled(session.Mode != CameraMode.Editing);
        Report(CharacterPicker.Draw(session, characters, ref search, Layout.DialogWidth));
        DrawAboveAimHeight();
        DrawAimHeight();
        DrawSmoothing();
        DrawBelowSmoothing();
        ImGui.EndDisabled();

        ImGui.Separator();
        if (ImGui.Button("Done", new Vector2(Layout.DialogWidth, 0f)))
            IsOpen = false;
    }

    /// <summary>The mode's own settings between the character list and the aim height; none by default.</summary>
    protected virtual void DrawAboveAimHeight() { }

    /// <summary>The mode's own settings below the smoothing slider, disabled with the rest; none by default.</summary>
    protected virtual void DrawBelowSmoothing() { }

    /// <summary>The aim height above the character's feet: a labelled drag field, live and one undo step per drag.</summary>
    private void DrawAimHeight()
    {
        Label("Aim height");
        // Refused once an undo mid-drag has ended the edit; the rest of that drag does nothing.
        LiveDrag.Field(
            session,
            session.Track.AimHeight,
            (ref float height) =>
                BorderedField.Draw(
                    aimHeightId,
                    "Aim height",
                    null,
                    ref height,
                    PoseGrid.PositionSpeed,
                    Units.YalmsField,
                    Layout.FieldWidth
                ),
            height => _ = session.PreviewAimHeight(height),
            ref dragging
        );
    }

    /// <summary>The smoothing slider; a drag that changes it is applied as one undo step when it lets go.</summary>
    private void DrawSmoothing()
    {
        Label("Smoothing");
        smoothing.Draw(
            "smoothing",
            session.Track.Smoothing,
            (ref float value) =>
            {
                ImGui.SetNextItemWidth(Layout.FieldWidth);
                return ImGui.SliderFloat("##smoothing", ref value, 0f, 1f, "%.2f");
            },
            value => Report(session.SetSmoothing(value))
        );
    }

    /// <summary>A field's label, with the field following at the right end of the list's width.</summary>
    private static void Label(string text)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(text);
        ImGui.SameLine(Layout.DialogWidth - Layout.FieldWidth + ImGui.GetStyle().WindowPadding.X);
    }
}
