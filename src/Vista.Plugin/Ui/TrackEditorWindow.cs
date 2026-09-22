using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui;

/// <summary>The main editor window: modes, track settings, the point list and the scrub bar.</summary>
internal sealed unsafe class TrackEditorWindow : Window
{
    private const string PointPayload = "VISTA_POINT";

    private static readonly string[] ModeNames = ["Off", "Edit", "Live"];
    private static readonly string[] AimNames = ["Recorded aim", "Direction of travel"];
    private static readonly string[] PlaybackNames = ["Once", "Loop"];
    private static readonly Vector2 Spacing = new(8f, 7f);
    private static readonly Vector2 CellPadding = new(6f, 4f);
    private const float SpeedWidth = 90f;
    private const float FieldWidth = 70f;

    private readonly CameraSession session;
    private readonly PendingField fields;
    private readonly TimingWindow timing;
    private CameraMode lastMode;
    private bool scrubbing;

    public TrackEditorWindow(CameraSession session, PendingField fields, TimingWindow timing)
        : base("Vista###vista-track-editor")
    {
        this.session = session;
        this.fields = fields;
        this.timing = timing;
        RespectCloseHotkey = false;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420f, 260f), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }

    /// <summary>Applies an unfinished field edit and ends a scrub, since a closed window never reports either finishing.</summary>
    public override void OnClose()
    {
        fields.Commit();
        EndScrub();
    }

    public override void Draw()
    {
        if (session.Mode != lastMode)
        {
            fields.Clear();
            lastMode = session.Mode;
        }

        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Spacing);
        var editing = session.Mode == CameraMode.Editing;
        DrawTopRow(editing);

        ImGui.BeginDisabled(!editing);
        DrawTrackRow();
        ImGui.EndDisabled();
        ImGui.Separator();

        DrawPoints(editing);
        ImGui.Separator();
        DrawScrubRow(editing);
    }

    private void DrawTopRow(bool editing)
    {
        DrawModeCombo();

        var gap = ImGui.GetStyle().ItemSpacing.X * 3f;
        ImGui.SameLine(0f, gap);
        ImGui.BeginDisabled(!editing);
        DrawAddButton();
        ImGui.EndDisabled();

        ImGui.SameLine(0f, gap);
        ImGui.BeginDisabled(!session.CanUndo);
        if (IconButton.Draw("undo", FontAwesomeIcon.Undo, "Undo")) { fields.Commit(); session.Undo(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!session.CanRedo);
        if (IconButton.Draw("redo", FontAwesomeIcon.Redo, "Redo")) { fields.Commit(); session.Redo(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (IconButton.Draw("timing", FontAwesomeIcon.ChartLine, "Timing")) timing.Toggle();
    }

    /// <summary>Play/Pause and Restart, left of the scrub bar on the same line.</summary>
    private void DrawTransport()
    {
        var playing = session.Mode == CameraMode.Live && !session.Director.IsPaused && !session.Director.IsFinished;
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (IconButton.Draw("play-pause", playing ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, playing ? "Pause" : "Play"))
        {
            fields.Commit();
            if (playing) session.Stop();
            else session.Play();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode != CameraMode.Live);
        if (IconButton.Draw("restart", FontAwesomeIcon.StepBackward, "Restart")) { fields.Commit(); session.Restart(); }
        ImGui.EndDisabled();
        ImGui.SameLine();
    }

    /// <summary>Off, Edit and Live; Off releases the camera and Live cues the shot paused at its start.</summary>
    private void DrawModeCombo()
    {
        var current = session.Mode switch { CameraMode.Editing => 1, CameraMode.Live => 2, _ => 0 };
        ImGui.SetNextItemWidth(80f);
        if (!ImGui.BeginCombo("##mode", ModeNames[current])) return;

        if (ImGui.Selectable(ModeNames[0], current == 0) && current != 0) { fields.Commit(); session.Release("window"); }
        if (ImGui.Selectable(ModeNames[1], current == 1) && current != 1) { fields.Commit(); session.Edit(); }
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (ImGui.Selectable(ModeNames[2], current == 2) && current != 2) { fields.Commit(); session.Cue(); }
        ImGui.EndDisabled();
        ImGui.EndCombo();
    }

    /// <summary>Aim and playback drop-downs, track Speed and Duration, and Clear track as a trash icon at the right end.</summary>
    private void DrawTrackRow()
    {
        var aim = session.Track.Aim == AimMode.AimKeys ? 0 : 1;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("##aim", $"Aim: {AimNames[aim]}"))
        {
            for (var i = 0; i < AimNames.Length; i++)
            {
                if (!ImGui.Selectable(AimNames[i], i == aim) || i == aim) continue;
                var mode = i == 0 ? AimMode.AimKeys : AimMode.PathTangent;
                Report(session.ChangeTrack(t => t with { Aim = mode }));
            }

            ImGui.EndCombo();
        }

        var playback = session.Track.Playback == PlaybackMode.Once ? 0 : 1;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(140f);
        if (ImGui.BeginCombo("##playback", $"Playback: {PlaybackNames[playback]}"))
        {
            for (var i = 0; i < PlaybackNames.Length; i++)
            {
                if (!ImGui.Selectable(PlaybackNames[i], i == playback) || i == playback) continue;
                var mode = i == 0 ? PlaybackMode.Once : PlaybackMode.Loop;
                Report(session.ChangeTrack(t => TrackEditing.SetPlayback(t, mode)));
            }

            ImGui.EndCombo();
        }

        ImGui.BeginDisabled(TrackEditing.AllPinned(session.Track));
        ImGui.SameLine();
        LabelledField("Speed", "track-speed", session.Track.Speed, "%.2f", "Track speed, yalms per second", v => Report(session.SetTrackSpeed(v)));
        ImGui.SameLine();
        LabelledField("Duration", "track-duration", (float)session.Duration, "%.1f", "Whole shot, holds included, in seconds", v => Report(session.SetTrackDuration(v)));
        ImGui.EndDisabled();

        ImGui.SameLine();
        RightAlign(IconButton.Width(FontAwesomeIcon.Trash));
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (IconButton.Draw("clear-track", FontAwesomeIcon.Trash, "Clear track")) { fields.Clear(); Report(session.ChangeTrack(_ => TrackEditing.Empty())); }
        ImGui.EndDisabled();
    }

    private void DrawAddButton()
    {
        if (ImGui.Button("+ Add")) Report(session.AddToEnd());
        ImGui.SameLine(0f, 0f);
        if (ImGui.ArrowButton("##add-menu", ImGuiDir.Down)) ImGui.OpenPopup("add-menu");
        if (!ImGui.BeginPopup("add-menu")) return;

        var selected = session.Selected is not null;
        var ticked = false;
        if (ImGui.MenuItem("Add to end", "Backtick", ref ticked)) Report(session.AddToEnd());
        if (ImGui.MenuItem("Add after selected", "Alt + Backtick", ref ticked, selected)) Report(session.AddAfterSelected());
        if (ImGui.MenuItem("Overwrite selected", "Ctrl + Backtick", ref ticked, selected)) Report(session.OverwriteSelected());
        ImGui.EndPopup();
    }

    private void DrawPoints(bool editing)
    {
        var track = session.Track;
        var footer = ImGui.GetFrameHeightWithSpacing() + (ImGui.GetStyle().ItemSpacing.Y * 2f);
        if (ImGui.BeginChild("points", new Vector2(0f, -footer)) && track.Points.Count > 0)
        {
            using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, CellPadding);
            if (ImGui.BeginTable("point-table", 7, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("##handle");
                ImGui.TableSetupColumn("#");
                ImGui.TableSetupColumn("Duration (s)");
                ImGui.TableSetupColumn("Speed");
                ImGui.TableSetupColumn("Hold (s)");
                ImGui.TableSetupColumn("##pin", ImGuiTableColumnFlags.WidthFixed, IconButton.Width(FontAwesomeIcon.Thumbtack));
                ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                for (var i = 0; i < track.Points.Count; i++) DrawPointRow(track, i, editing);
                ImGui.EndTable();
            }
        }

        ImGui.EndChild();
    }

    /// <summary>One point and the leg arriving at it: the whole row selects on click, jumps on double-click and drags to reorder; the trash icon deletes it.</summary>
    private void DrawPointRow(Track track, int index, bool editing)
    {
        ImGui.TableNextRow();
        ImGui.BeginDisabled(!editing);

        ImGui.TableNextColumn();
        var rowFlags = ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap;
        if (ImGui.Selectable($"##row{index}", session.Selected == index, rowFlags, new Vector2(0f, ImGui.GetFrameHeight()))) session.Select(index);
        if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) session.JumpToPoint(index);
        if (editing && ImGui.BeginDragDropSource())
        {
            SetPayload(index);
            ImGui.TextUnformatted($"Point {index + 1}");
            ImGui.EndDragDropSource();
        }

        DropTarget(index, editing);

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextUnformatted(FontAwesomeIcon.GripLines.ToIconString());

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{index + 1}");

        ImGui.TableNextColumn();
        if (index > 0) fields.Draw($"leg{index}", session.Evaluator.LegSeconds(index), "%.1f", FieldWidth, v => Report(session.SetLegDuration(index, v)));

        ImGui.TableNextColumn();
        if (index > 0) fields.Draw($"leg-speed{index}", TrackEditing.LegSpeed(track, index), "%.2f", FieldWidth, v => Report(session.SetLegSpeed(index, v)));

        ImGui.TableNextColumn();
        fields.Draw($"hold{index}", TrackEditing.HoldSeconds(track, index), "%.1f", FieldWidth,
            v => Report(session.ChangeTrack(t => TrackEditing.SetHold(t, index, EditLimits.Hold(v)))));

        ImGui.TableNextColumn();
        if (index > 0 && TrackEditing.IsPinned(track, index)
            && IconButton.Draw($"pin{index}", FontAwesomeIcon.Thumbtack, "Pinned: click to follow the track speed"))
            Report(session.ResetLeg(index));

        ImGui.TableNextColumn();
        RightAlign(IconButton.Width(FontAwesomeIcon.Trash));
        if (IconButton.Draw($"delete{index}", FontAwesomeIcon.Trash, "Delete point"))
        {
            fields.Clear();
            Report(session.DeletePoint(index));
        }

        ImGui.EndDisabled();
    }

    /// <summary>Play/Pause and Restart, the scrub bar showing current and total time, and fly speed at its right while editing.</summary>
    private void DrawScrubRow(bool editing)
    {
        var duration = (float)session.Duration;
        var head = (float)session.ScrubHead;
        var speedWidth = editing ? ImGui.CalcTextSize("Speed").X + SpeedWidth + (ImGui.GetStyle().ItemSpacing.X * 2f) : 0f;

        DrawTransport();
        ImGui.BeginDisabled(session.Mode == CameraMode.Off || duration <= 0f);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - speedWidth);
        var moved = ImGui.SliderFloat("##scrub", ref head, 0f, MathF.Max(duration, 0.001f), $"%.1f / {duration:0.0} s");
        if (ImGui.IsItemActivated()) { fields.Commit(); session.BeginScrub(); scrubbing = session.Scrubbing; }
        if (moved || ImGui.IsItemActivated()) session.ScrubTo(head);
        // A window that stops drawing mid-drag never reports the slider deactivating, so any idle frame ends the scrub too.
        if (ImGui.IsItemDeactivated() || !ImGui.IsItemActive()) EndScrub();
        ImGui.EndDisabled();

        if (!editing) return;
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Speed");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(SpeedWidth);
        var speed = session.Speed;
        var step = speed.Index;
        if (ImGui.SliderInt("##speed", ref step, 0, FlySpeed.Steps.Count - 1, $"{speed.Multiplier:0.##}x")) speed.Set(step);
    }

    /// <summary>A text label, then a number field with a tooltip that shows even while disabled.</summary>
    private void LabelledField(string label, string id, float current, string format, string tooltip, Action<float> apply)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        fields.Draw(id, current, format, FieldWidth, apply);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
    }

    /// <summary>Moves the dragged point to <paramref name="index"/> when it is dropped on this item.</summary>
    private void DropTarget(int index, bool editing)
    {
        if (!editing || !ImGui.BeginDragDropTarget()) return;
        var payload = ImGui.AcceptDragDropPayload(PointPayload);
        if (!payload.IsNull && *(int*)payload.Handle->Data is var from && from != index) Report(session.MovePoint(from, index));
        ImGui.EndDragDropTarget();
    }

    /// <summary>Ends a scrub the scrub bar started, leaving the Timing window's alone.</summary>
    private void EndScrub()
    {
        if (!scrubbing) return;
        scrubbing = false;
        session.EndScrub();
    }

    private static void SetPayload(int index) => ImGui.SetDragDropPayload(PointPayload, new ReadOnlySpan<byte>(&index, sizeof(int)));

    /// <summary>Moves the cursor so an item of <paramref name="width"/> ends at the right edge.</summary>
    private static void RightAlign(float width)
        => ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - width));

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
