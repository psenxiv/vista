using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Plugin.Ui.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using static Vista.Plugin.Ui.Widgets.Refusal;
namespace Vista.Plugin.Ui.Windows;

/// <summary>The panel a target aim mode edits: the character to aim at, its aim height and smoothing.</summary>
internal abstract class TargetWindow : Window
{
    protected const float ListWidth = 260f;
    protected const float FieldWidth = 70f;

    protected readonly SessionState session;
    private readonly NearbyCharacters characters;
    private readonly AimMode aim;
    private readonly string aimHeightId;
    private string search = string.Empty;
    private float? smoothingDrag;
    private Guid openedFor;

    /// <summary>True while a drag field of this window is held; shared so a close ends whichever one it is.</summary>
    protected bool dragging;

    protected TargetWindow(SessionState session, NearbyCharacters characters, string name, AimMode aim, string aimHeightId)
        : base(name, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.session = session;
        this.characters = characters;
        this.aim = aim;
        this.aimHeightId = aimHeightId;
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
        if (!IsOpen) return;
        if (session.Mode != CameraMode.Editing || session.EditedTrackId != openedFor || session.Track.Aim != aim) IsOpen = false;
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
        using var popups = PopupStyle.Push();
        using var selection = ImRaii.PushColor(ImGuiCol.Header, UiColours.AccentAt(0.45f))
            .Push(ImGuiCol.HeaderHovered, UiColours.AccentAt(0.30f))
            .Push(ImGuiCol.HeaderActive, UiColours.AccentAt(0.55f));

        ImGui.BeginDisabled(session.Mode != CameraMode.Editing);
        Report(CharacterPicker.Draw(session, characters, ref search, ListWidth));
        DrawAboveAimHeight();
        DrawAimHeight();
        DrawSmoothing();
        DrawBelowSmoothing();
        ImGui.EndDisabled();

        ImGui.Separator();
        if (ImGui.Button("Done", new Vector2(ListWidth, 0f))) IsOpen = false;
    }

    /// <summary>The mode's own settings between the character list and the aim height; none by default.</summary>
    protected virtual void DrawAboveAimHeight()
    {
    }

    /// <summary>The mode's own settings below the smoothing slider, disabled with the rest; none by default.</summary>
    protected virtual void DrawBelowSmoothing()
    {
    }

    /// <summary>The aim height above the character's feet: a labelled drag field, live and one undo step per drag.</summary>
    private void DrawAimHeight()
    {
        Label("Aim height");
        var height = session.Track.AimHeight;
        var changed = BorderedField.Draw(aimHeightId, "Aim height", null, ref height, 0.02f, "%.2f", FieldWidth);
        // Refused once an undo mid-drag has ended the edit; the rest of that drag does nothing.
        LiveDrag.Handle(session, changed, () => _ = session.PreviewAimHeight(height), ref dragging);
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
}
