using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace CinematicCam.Plugin.Ui;

/// <summary>The main editor window: modes, track settings, the point list and status.</summary>
internal sealed unsafe class TrackEditorWindow : Window
{
    private const string PointPayload = "CCAM_POINT";

    private static readonly string[] ModeNames = ["Edit", "Live", "Off"];
    private static readonly string[] AimNames = ["Recorded aim", "Direction of travel"];
    private static readonly string[] PlaybackNames = ["Once", "Loop"];

    private readonly CameraSession session;
    private readonly PendingField fields;
    private CameraMode lastMode;

    public TrackEditorWindow(CameraSession session, PendingField fields)
        : base("Cinematic Cam###ccam-track-editor")
    {
        this.session = session;
        this.fields = fields;
        RespectCloseHotkey = false;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420f, 260f), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }

    /// <summary>Applies an unfinished field edit and ends a scrub, since a closed window never reports either finishing.</summary>
    public override void OnClose()
    {
        fields.Commit();
        if (session.Scrubbing) session.EndScrub();
    }

    public override void Draw()
    {
        if (session.Mode != lastMode)
        {
            fields.Clear();
            lastMode = session.Mode;
        }

        var editing = session.Mode == CameraMode.Editing;
        DrawTopRow(editing);

        ImGui.BeginDisabled(!editing);
        DrawTrackRow();
        ImGui.EndDisabled();
        ImGui.Separator();

        DrawPoints(editing);
        ImGui.Separator();
        DrawScrubBar();
        DrawStatus();
    }

    private void DrawTopRow(bool editing)
    {
        DrawModeCombo();

        ImGui.SameLine();
        var playing = session.Mode == CameraMode.Live && !session.Director.IsPaused && !session.Director.IsFinished;
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (Icon("play-pause", playing ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, playing ? "Pause" : "Play"))
        {
            fields.Commit();
            if (playing) session.Stop();
            else session.Play();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode != CameraMode.Live);
        if (Icon("restart", FontAwesomeIcon.StepBackward, "Restart")) { fields.Commit(); session.Restart(); }
        ImGui.EndDisabled();

        var gap = ImGui.GetStyle().ItemSpacing.X * 3f;
        ImGui.SameLine(0f, gap);
        ImGui.BeginDisabled(!session.CanUndo);
        if (Icon("undo", FontAwesomeIcon.Undo, "Undo")) { fields.Commit(); session.Undo(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!session.CanRedo);
        if (Icon("redo", FontAwesomeIcon.Redo, "Redo")) { fields.Commit(); session.Redo(); }
        ImGui.EndDisabled();

        ImGui.SameLine(0f, gap);
        ImGui.BeginDisabled(!editing);
        DrawAddButton();
        ImGui.EndDisabled();
    }

    /// <summary>Edit, Live and Off; Live plays the shot and Off releases the camera.</summary>
    private void DrawModeCombo()
    {
        var current = session.Mode switch { CameraMode.Editing => 0, CameraMode.Live => 1, _ => 2 };
        ImGui.SetNextItemWidth(80f);
        if (!ImGui.BeginCombo("##mode", ModeNames[current])) return;

        if (ImGui.Selectable(ModeNames[0], current == 0) && current != 0) { fields.Commit(); session.Edit(); }
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (ImGui.Selectable(ModeNames[1], current == 1) && current != 1) { fields.Commit(); session.Play(); }
        ImGui.EndDisabled();
        if (ImGui.Selectable(ModeNames[2], current == 2) && current != 2) { fields.Commit(); session.Release("window"); }
        ImGui.EndCombo();
    }

    /// <summary>An icon button with a tooltip naming it, shown even while disabled.</summary>
    private static bool Icon(string id, FontAwesomeIcon icon, string tooltip)
    {
        var pressed = ImGuiComponents.IconButton(id, icon);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
        return pressed;
    }

    private void DrawTrackRow()
    {
        var aim = session.Track.Aim == AimMode.AimKeys ? 0 : 1;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Aim");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        if (ImGui.Combo("##aim", ref aim, AimNames))
        {
            var mode = aim == 0 ? AimMode.AimKeys : AimMode.PathTangent;
            Report(session.ChangeTrack(t => t with { Aim = mode }));
        }

        var playback = session.Track.Playback == PlaybackMode.Once ? 0 : 1;
        ImGui.SameLine();
        ImGui.TextUnformatted("Playback");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        if (ImGui.Combo("##playback", ref playback, PlaybackNames))
        {
            var mode = playback == 0 ? PlaybackMode.Once : PlaybackMode.Loop;
            Report(session.ChangeTrack(t => TrackEditing.SetPlayback(t, mode)));
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear track")) { fields.Clear(); Report(session.ChangeTrack(_ => TrackEditing.Empty())); }
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
        var footer = (ImGui.GetFrameHeightWithSpacing() * 3f) + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginChild("points", new Vector2(0f, -footer)) && track.Points.Count > 0
            && ImGui.BeginTable("point-table", 5, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("##handle");
            ImGui.TableSetupColumn("#");
            ImGui.TableSetupColumn("Leg (s)");
            ImGui.TableSetupColumn("Hold (s)");
            ImGui.TableSetupColumn("##selected");
            ImGui.TableHeadersRow();

            for (var i = 0; i < track.Points.Count; i++) DrawPointRow(track, i, editing);
            ImGui.EndTable();
        }

        ImGui.EndChild();
    }

    private void DrawPointRow(Track track, int index, bool editing)
    {
        var selected = session.Selected == index;
        ImGui.TableNextRow();
        ImGui.BeginDisabled(!editing);

        ImGui.TableNextColumn();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.Selectable($"{FontAwesomeIcon.GripLines.ToIconString()}##grip{index}");
        if (editing && ImGui.BeginDragDropSource())
        {
            SetPayload(index);
            ImGui.TextUnformatted($"Point {index + 1}");
            ImGui.EndDragDropSource();
        }

        DropTarget(index, editing);

        ImGui.TableNextColumn();
        if (ImGui.Selectable($"{index + 1}##row{index}", selected)) session.Select(index);
        if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) session.JumpToPoint(index);
        DropTarget(index, editing);

        ImGui.TableNextColumn();
        if (index == 0) ImGui.TextUnformatted("-");
        else fields.Draw($"leg{index}", TrackEditing.LegSeconds(track, index), "%.1f", 70f,
            v => Report(session.ChangeTrack(t => TrackEditing.SetLeg(t, index, EditLimits.Leg(v)))));

        ImGui.TableNextColumn();
        fields.Draw($"hold{index}", TrackEditing.HoldSeconds(track, index), "%.1f", 70f,
            v => Report(session.ChangeTrack(t => TrackEditing.SetHold(t, index, EditLimits.Hold(v)))));

        ImGui.EndDisabled();

        ImGui.TableNextColumn();
        if (selected)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextUnformatted(FontAwesomeIcon.CaretLeft.ToIconString());
        }
    }

    private void DrawScrubBar()
    {
        var duration = (float)session.Duration;
        var head = (float)session.ScrubHead;
        var tail = $"{duration:0.0} s   {head:0.0} s";

        ImGui.BeginDisabled(session.Mode == CameraMode.Off || duration <= 0f);
        ImGui.TextUnformatted("0.0");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(tail).X - ImGui.GetStyle().ItemSpacing.X);
        var moved = ImGui.SliderFloat("##scrub", ref head, 0f, MathF.Max(duration, 0.001f), "");
        if (ImGui.IsItemActivated()) { fields.Commit(); session.BeginScrub(); }
        if (moved || ImGui.IsItemActivated()) session.ScrubTo(head);
        if (ImGui.IsItemDeactivated()) session.EndScrub();
        ImGui.SameLine();
        ImGui.TextUnformatted(tail);
        ImGui.EndDisabled();
    }

    /// <summary>Moves the dragged point to <paramref name="index"/> when it is dropped on this item.</summary>
    private void DropTarget(int index, bool editing)
    {
        if (!editing || !ImGui.BeginDragDropTarget()) return;
        var payload = ImGui.AcceptDragDropPayload(PointPayload);
        if (!payload.IsNull && *(int*)payload.Handle->Data is var from && from != index) Report(session.MovePoint(from, index));
        ImGui.EndDragDropTarget();
    }

    private static void SetPayload(int index) => ImGui.SetDragDropPayload(PointPayload, new ReadOnlySpan<byte>(&index, sizeof(int)));

    /// <summary>The status line, with fly speed at its right end.</summary>
    private void DrawStatus()
    {
        const float sliderWidth = 120f;
        var count = session.Track.Points.Count;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{count} point{(count == 1 ? "" : "s")} | total {session.Duration:0.0} s | {ModeText()}");

        var width = ImGui.CalcTextSize("Fly speed").X + ImGui.GetStyle().ItemSpacing.X + sliderWidth;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - width));
        ImGui.TextUnformatted("Fly speed");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(sliderWidth);
        var speed = session.Speed;
        var step = speed.Index;
        if (ImGui.SliderInt("##speed", ref step, 0, FlySpeed.Steps.Count - 1, $"{speed.Multiplier:0.##}x")) speed.Set(step);
    }

    private string ModeText() => session.Mode switch
    {
        CameraMode.Live => session.Director.IsFinished ? "finished" : session.Director.IsPaused ? "paused" : "playing",
        CameraMode.Editing => "editing",
        _ => "off",
    };

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
