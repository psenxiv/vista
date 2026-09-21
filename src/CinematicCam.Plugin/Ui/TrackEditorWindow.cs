using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace CinematicCam.Plugin.Ui;

/// <summary>The main editor window: modes, track settings, the point list and status.</summary>
internal sealed class TrackEditorWindow : Window
{
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
        DrawModeRow();
        DrawSpeedRow();

        ImGui.BeginDisabled(!editing);
        DrawTrackRow();
        ImGui.Separator();
        DrawAddButton();
        ImGui.EndDisabled();

        DrawPoints(editing);
        ImGui.Separator();
        DrawStatus();
    }

    private void DrawModeRow()
    {
        ImGui.TextUnformatted($"Mode: {session.Mode}");

        ImGui.SameLine();
        if (ImGui.Button("Edit")) { fields.Commit(); session.Edit(); }

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (ImGui.Button("Play")) { fields.Commit(); session.Play(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode != CameraMode.Live);
        if (ImGui.Button("Restart")) { fields.Commit(); session.Restart(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode != CameraMode.Live || session.Director.IsPaused);
        if (ImGui.Button("Stop")) { fields.Commit(); session.Stop(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode == CameraMode.Off);
        if (ImGui.Button("Release")) { fields.Commit(); session.Release("window"); }
        ImGui.EndDisabled();
    }

    private void DrawSpeedRow()
    {
        var speed = session.Speed;
        var step = speed.Index;
        ImGui.TextUnformatted("Fly speed");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        if (ImGui.SliderInt("##speed", ref step, 0, FlySpeed.Steps.Count - 1, $"{speed.Multiplier:0.##}x")) speed.Set(step);

        ImGui.SameLine();
        ImGui.BeginDisabled(!session.CanUndo);
        if (ImGui.Button("Undo")) { fields.Commit(); session.Undo(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!session.CanRedo);
        if (ImGui.Button("Redo")) { fields.Commit(); session.Redo(); }
        ImGui.EndDisabled();
    }

    private void DrawTrackRow()
    {
        var aim = session.Track.Aim == AimMode.AimKeys ? 0 : 1;
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
        if (ImGui.Button("New track")) { fields.Clear(); Report(session.ChangeTrack(_ => TrackEditing.Empty())); }
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
        var footer = (ImGui.GetFrameHeightWithSpacing() * 2f) + ImGui.GetStyle().ItemSpacing.Y;
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
            ImGui.TextUnformatted(FontAwesomeIcon.GripLines.ToIconString());

        ImGui.TableNextColumn();
        if (ImGui.Selectable($"{index + 1}##row{index}", selected)) session.Select(index);

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

    private void DrawStatus()
    {
        var count = session.Track.Points.Count;
        ImGui.TextUnformatted($"{count} point{(count == 1 ? "" : "s")} | total {session.Duration:0.0} s | {ModeText()}");
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
