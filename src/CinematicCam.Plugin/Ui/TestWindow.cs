using CinematicCam.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;

namespace CinematicCam.Plugin.Ui;

/// <summary>Stand-in editor for building and playing a track in game.</summary>
internal sealed class TestWindow : Window
{
    private static readonly string[] AimNames = ["Recorded aim", "Direction of travel"];
    private static readonly string[] PlaybackNames = ["Once", "Loop"];

    private readonly CameraSession session;
    private string? error;
    private (string Id, float Value)? pending;

    public TestWindow(CameraSession session)
        : base("Cinematic Cam (test)###ccam-test", ImGuiWindowFlags.AlwaysAutoResize)
        => this.session = session;

    public override void Draw()
    {
        DrawModeRow();

        ImGui.BeginDisabled(session.Mode != CameraMode.Editing);
        DrawTrackRow();
        DrawPoints();
        ImGui.EndDisabled();

        DrawStatus();
        if (error is not null) ImGui.TextColored(ImGuiColors.DalamudRed, error);
    }

    private void DrawModeRow()
    {
        ImGui.TextUnformatted($"Mode: {session.Mode}");

        ImGui.SameLine();
        if (ImGui.Button("Edit")) { error = null; session.Edit(); }

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (ImGui.Button("Play")) { error = null; session.Play(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode != CameraMode.Live || session.Director.IsPaused);
        if (ImGui.Button("Stop")) session.Stop();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Release")) { error = null; session.Release("window"); }
    }

    private void DrawTrackRow()
    {
        var aim = session.Track.Aim == AimMode.AimKeys ? 0 : 1;
        ImGui.TextUnformatted("Aim:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        if (ImGui.Combo("##aim", ref aim, AimNames))
        {
            var mode = aim == 0 ? AimMode.AimKeys : AimMode.PathTangent;
            error = session.ChangeTrack(t => t with { Aim = mode });
        }

        var playback = session.Track.Playback == PlaybackMode.Once ? 0 : 1;
        ImGui.SameLine();
        ImGui.TextUnformatted("Playback:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        if (ImGui.Combo("##playback", ref playback, PlaybackNames))
        {
            var mode = playback == 0 ? PlaybackMode.Once : PlaybackMode.Loop;
            error = session.ChangeTrack(t => TrackEditing.SetPlayback(t, mode));
        }

        if (ImGui.Button("Capture point")) error = session.CapturePoint();
        ImGui.SameLine();
        if (ImGui.Button("New track")) error = session.ChangeTrack(_ => TrackEditing.Empty());
    }

    private void DrawPoints()
    {
        var track = session.Track;
        if (track.Points.Count == 0 || !ImGui.BeginTable("points", 3, ImGuiTableFlags.SizingFixedFit)) return;

        ImGui.TableSetupColumn("#");
        ImGui.TableSetupColumn("Leg (s)");
        ImGui.TableSetupColumn("Hold (s)");
        ImGui.TableHeadersRow();

        for (var i = 0; i < track.Points.Count; i++)
        {
            var index = i;
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{index}");

            ImGui.TableNextColumn();
            if (index == 0) ImGui.TextUnformatted("-");
            else DrawSeconds($"leg{index}", TrackEditing.LegSeconds(track, index), (t, s) => TrackEditing.SetLeg(t, index, s));

            ImGui.TableNextColumn();
            DrawSeconds($"hold{index}", TrackEditing.HoldSeconds(track, index), (t, s) => TrackEditing.SetHold(t, index, s));
        }

        ImGui.EndTable();
    }

    /// <summary>A seconds field that applies its value only when the user finishes editing it.</summary>
    private void DrawSeconds(string id, float current, Func<Track, float, Track> set)
    {
        var value = pending is { } p && p.Id == id ? p.Value : current;
        ImGui.SetNextItemWidth(70f);
        if (ImGui.InputFloat($"##{id}", ref value, 0f, 0f, "%.1f")) pending = (id, value);

        if (ImGui.IsItemDeactivatedAfterEdit() && pending is { } done && done.Id == id)
        {
            pending = null;
            error = session.ChangeTrack(t => set(t, done.Value));
        }
    }

    private void DrawStatus()
    {
        var track = session.Track;
        var total = track.Timing.Count == 0 ? 0f : track.Timing[^1].Time;
        var status = $"{track.Points.Count} points | total {total:0.0} s";

        var director = session.Director;
        if (director.IsLive)
        {
            var state = director.IsFinished ? "finished at" : director.IsPaused ? "paused at" : "playing";
            status += $" | {state} {director.Elapsed:0.0} s";
        }

        ImGui.TextUnformatted(status);
    }
}
