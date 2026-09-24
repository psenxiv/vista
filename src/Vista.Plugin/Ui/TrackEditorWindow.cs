using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using static Vista.Plugin.Ui.Refusal;
namespace Vista.Plugin.Ui;

/// <summary>The main Vista window: modes, the Hierarchy, track settings, the point list, the scrub bar and the Playlist.</summary>
internal sealed class TrackEditorWindow : Window
{
    private static readonly string[] ModeNames = ["Off", "View", "Edit", "Live"];

    /// <summary>One entry in the aim menu.</summary>
    private readonly record struct AimChoice(AimMode Mode, string Name);

    /// <summary>One entry in the direction menu.</summary>
    private readonly record struct DirectionChoice(PlaybackDirection Direction, string Name, FontAwesomeIcon Icon);

    private static readonly AimChoice[] Aims =
    [
        new(AimMode.AimKeys, "Recorded aim"),
        new(AimMode.PathTangent, "Direction of travel"),
        new(AimMode.LookAt, "Look At"),
        new(AimMode.WatchTarget, "Watch Target"),
        new(AimMode.FollowTarget, "Follow Target"),
    ];

    private static readonly DirectionChoice[] DirectionChoices =
    [
        new(PlaybackDirection.Forward, "Forward", FontAwesomeIcon.ArrowRight),
        new(PlaybackDirection.Reverse, "Reverse", FontAwesomeIcon.ArrowLeft),
        new(PlaybackDirection.PingPong, "Ping-pong", FontAwesomeIcon.ArrowsAltH),
    ];

    private static readonly Vector2 Spacing = new(8f, 7f);
    private static readonly Vector2 CellPadding = new(6f, 4f);
    private const float SpeedWidth = 90f;
    private const float ModeWidth = 80f;
    private const float FieldWidth = 70f;
    private const float MinWidth = 420f;
    private const float MinHeight = 260f;

    // Speeds in yalms per second, and shot, leg and hold lengths in seconds, within the editor's limits.
    private static readonly PendingField.Range SpeedRange = new(0.05f, TrackEditing.MinSpeed, TrackEditing.MaxSpeed);
    private static readonly PendingField.Range ShotRange = new(0.1f, EditLimits.MinShotSeconds, TrackEditing.MaxShotSeconds);
    private static readonly PendingField.Range LegRange = new(0.05f, TrackEditing.MinLegSeconds, TrackEditing.MaxSeconds);
    private static readonly PendingField.Range HoldRange = new(0.05f, 0f, TrackEditing.MaxSeconds);
    private static readonly PendingField.Range LookAheadRange = new(0.01f, 0f, TrackEditing.MaxLookAhead);
    private const string LookAheadId = "look-ahead";

    private readonly CameraSession session;
    private readonly Configuration config;
    private bool aimMenuOpen;
    private readonly PendingField fields;
    private readonly TimingWindow timing;
    private readonly CameraWindow camera;
    private readonly GuideWindow guide;
    private readonly WatchTargetWindow watchTarget;
    private readonly FollowTargetWindow followTarget;
    private readonly SetupWindow setup;
    private readonly HierarchyPanel hierarchy;
    private readonly PlaylistPanel playlist;
    private CameraMode lastMode;
    private bool scrubbing;
    private bool showHierarchy = true;
    private bool showPlaylist = true;
    private float pendingWidth;
    private float? edgeGrabbed;

    // Where last frame's track row put its buttons, in screen X, so the top bar can line up with them.
    private float? aimX;
    private float? directionX;
    private float? loopX;
    private float? trashRight;

    public TrackEditorWindow(CameraSession session, Configuration config, PendingField fields, TimingWindow timing, CameraWindow camera, GuideWindow guide, WatchTargetWindow watchTarget, FollowTargetWindow followTarget, SceneFiles files, SetupWindow setup)
        : base("Vista###vista-track-editor")
    {
        this.session = session;
        this.config = config;
        config.HierarchyWidth = PanelWidth.Clamp(config.HierarchyWidth);
        config.PlaylistWidth = PanelWidth.Clamp(config.PlaylistWidth);
        this.fields = fields;
        this.timing = timing;
        this.camera = camera;
        this.guide = guide;
        this.watchTarget = watchTarget;
        this.followTarget = followTarget;
        this.setup = setup;
        hierarchy = new HierarchyPanel(session, files);
        playlist = new PlaylistPanel(session);
        RespectCloseHotkey = false;
        SizeCondition = ImGuiCond.FirstUseEver;
        SetMinimumWidth(MinWidth);
    }

    /// <summary>Widens the minimum size to fit the top bar, the track row and any open compartment, and opens at that width on first use.</summary>
    public override void PreDraw()
    {
        var width = MathF.Max(MathF.Max(MinWidth, TrackRowWidth()) + CompartmentsWidth(), TopRowWidth());
        SetMinimumWidth(width);
        Size = new Vector2(width, MinHeight);
    }

    /// <summary>The width the open compartments beside the track editor take, with their gap.</summary>
    private float CompartmentsWidth()
        => (showHierarchy ? config.HierarchyWidth + Spacing.X : 0f) + (showPlaylist ? config.PlaylistWidth + Spacing.X : 0f);

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
        using var popups = PopupStyle.Push();
        using var selection = ImRaii.PushColor(ImGuiCol.Header, UiColours.AccentAt(0.45f))
            .Push(ImGuiCol.HeaderHovered, UiColours.AccentAt(0.30f))
            .Push(ImGuiCol.HeaderActive, UiColours.AccentAt(0.55f));
        var editing = session.Mode == CameraMode.Editing;
        DrawTopRow(editing);

        if (showHierarchy)
        {
            if (ImGui.BeginChild("hierarchy", new Vector2(config.HierarchyWidth, 0f), true)) hierarchy.Draw(editing);
            ImGui.EndChild();
            ImGui.SameLine(0f, 0f);
            if (DrawEdge("hierarchy-edge", config.HierarchyWidth, 1f) is { } width) config.HierarchyWidth = width;
            ImGui.SameLine(0f, 0f);
        }

        var editorWidth = showPlaylist ? -(config.PlaylistWidth + Spacing.X) : 0f;
        // The same inner padding as the bordered compartments, so the rows and separators line up.
        if (ImGui.BeginChild("track-editor", new Vector2(editorWidth, 0f), false, ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            ImGui.BeginDisabled(!editing);
            DrawTrackRow();
            ImGui.EndDisabled();
            ImGui.Separator();

            DrawPoints(editing);
            ImGui.Separator();
            // The points list reserves more than a row for this footer; centre the row in it, then nudge it down half a gap to look centred against the window's bottom padding.
            var slack = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeight();
            if (slack > 0f) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + MathF.Min(slack, (slack / 2f) + (ImGui.GetStyle().ItemSpacing.Y / 2f)));
            DrawScrubRow();
        }

        ImGui.EndChild();

        if (showPlaylist)
        {
            ImGui.SameLine(0f, 0f);
            if (DrawEdge("playlist-edge", config.PlaylistWidth, -1f) is { } width) config.PlaylistWidth = width;
            ImGui.SameLine(0f, 0f);
            if (ImGui.BeginChild("playlist", new Vector2(config.PlaylistWidth, 0f), true)) playlist.Draw(editing);
            ImGui.EndChild();
        }

        // Showing or hiding a compartment grows or shrinks the window by its width, so the track editor keeps its size.
        if (pendingWidth != 0f)
        {
            ImGui.SetWindowSize(ImGui.GetWindowSize() + new Vector2(pendingWidth, 0f));
            pendingWidth = 0f;
        }
    }

    /// <summary>The gap beside a panel as a handle that drags its width, <paramref name="sign"/> 1 for a panel on the left and -1 on the right; the new width while dragged, saved when let go.</summary>
    private float? DrawEdge(string id, float width, float sign)
    {
        ImGui.InvisibleButton(id, new Vector2(Spacing.X, MathF.Max(1f, ImGui.GetContentRegionAvail().Y)));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        if (ImGui.IsItemActivated()) edgeGrabbed = width;
        if (ImGui.IsItemDeactivated()) config.Save();
        if (!ImGui.IsItemActive() || edgeGrabbed is not { } grabbed) return null;
        return PanelWidth.Clamp(grabbed + (sign * ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0f).X));
    }

    private void DrawTopRow(bool editing)
    {
        if (IconButton.Toggle("hierarchy", FontAwesomeIcon.Sitemap, showHierarchy, showHierarchy ? "Hide hierarchy" : "Show hierarchy"))
        {
            showHierarchy = !showHierarchy;
            pendingWidth += showHierarchy ? config.HierarchyWidth + Spacing.X : -(config.HierarchyWidth + Spacing.X);
        }

        ImGui.SameLine();
        if (IconButton.Toggle("playlist", FontAwesomeIcon.ListOl, showPlaylist, showPlaylist ? "Hide playlist" : "Show playlist"))
        {
            showPlaylist = !showPlaylist;
            pendingWidth += showPlaylist ? config.PlaylistWidth + Spacing.X : -(config.PlaylistWidth + Spacing.X);
        }

        ImGui.SameLine();
        DrawModeCombo();

        var gap = ImGui.GetStyle().ItemSpacing.X * 3f;
        AlignTo(aimX, gap);
        ImGui.BeginDisabled(!session.CanUndo);
        if (IconButton.Draw("undo", FontAwesomeIcon.Undo, "Undo")) { fields.Commit(); session.Undo(); }
        ImGui.EndDisabled();

        AlignTo(directionX, ImGui.GetStyle().ItemSpacing.X);
        ImGui.BeginDisabled(!session.CanRedo);
        if (IconButton.Draw("redo", FontAwesomeIcon.Redo, "Redo")) { fields.Commit(); session.Redo(); }
        ImGui.EndDisabled();

        AlignTo(loopX, ImGui.GetStyle().ItemSpacing.X);
        if (IconButton.Draw("timing", FontAwesomeIcon.ChartLine, "Timing")) timing.Toggle();

        if (editing)
        {
            AlignTo(CameraToolsStart(), gap);
            if (IconButton.Draw("level-roll", FontAwesomeIcon.RulerHorizontal, "Level camera roll")) session.CameraRoll = 0f;
            ImGui.SameLine();
            // Plain white when closed, not Toggle's dim, which reads as disabled.
            if (IconButton.Draw("camera", FontAwesomeIcon.Camera, "Camera", camera.IsOpen ? UiColours.Accent : null)) camera.Toggle();
            ImGui.SameLine();
            DrawFlySpeed();
        }

        var live = session.Mode == CameraMode.Live ? ImGui.CalcTextSize("LIVE").X + ImGui.GetStyle().ItemSpacing.X : 0f;
        ImGui.SameLine();
        RightAlign(live + IconButton.Width(FontAwesomeIcon.EyeSlash) + ImGui.GetStyle().ItemSpacing.X + IconButton.Width(FontAwesomeIcon.Cog) + ImGui.GetStyle().ItemSpacing.X + IconButton.Width(FontAwesomeIcon.Question));
        if (session.Mode == CameraMode.Live)
        {
            DrawLive();
            ImGui.SameLine();
        }

        if (IconButton.Toggle("hide-ui", FontAwesomeIcon.EyeSlash, session.HideUiInLive, "Hide game UI when Live"))
            session.HideUiInLive = !session.HideUiInLive;

        // A new folder loads a scene, which Live refuses.
        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode == CameraMode.Live);
        if (IconButton.Draw("save-folder", FontAwesomeIcon.Cog, "Save folder", setup.IsOpen ? UiColours.Accent : null)) setup.Toggle();
        ImGui.EndDisabled();

        // Plain white when closed, like the camera button.
        ImGui.SameLine();
        if (IconButton.Draw("guide", FontAwesomeIcon.Question, "User Guide", guide.IsOpen ? UiColours.Accent : null)) guide.Toggle();
    }

    /// <summary>Where the camera tools start so fly speed's slider still ends under the track row's trash, leaving room for the eye, the gear and the ?; null before the first frame.</summary>
    private float? CameraToolsStart()
    {
        if (trashRight is not { } right) return null;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var eyeLeft = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X - IconButton.Width(FontAwesomeIcon.Question) - spacing - IconButton.Width(FontAwesomeIcon.Cog) - spacing - IconButton.Width(FontAwesomeIcon.EyeSlash) - spacing;
        var tools = IconButton.Width(FontAwesomeIcon.RulerHorizontal) + spacing + IconButton.Width(FontAwesomeIcon.Camera) + spacing;
        return MathF.Min(right, eyeLeft) - SpeedWidth - tools;
    }

    /// <summary>Continues the row at <paramref name="screenX"/> when that's at least <paramref name="gap"/> past the last item, else just after it.</summary>
    private static void AlignTo(float? screenX, float gap)
    {
        ImGui.SameLine(0f, gap);
        var at = ImGui.GetCursorScreenPos();
        if (screenX is { } x && x > at.X) ImGui.SetCursorScreenPos(at with { X = x });
    }

    /// <summary>Red LIVE text pulsing on a two-second cycle.</summary>
    private static void DrawLive()
    {
        ImGui.AlignTextToFramePadding();
        var pulse = 0.55f + (0.45f * MathF.Cos((float)ImGui.GetTime() * MathF.PI));
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Red))
        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * pulse))
            ImGui.TextUnformatted("LIVE");
    }

    /// <summary>The free-cam's speed: a short slider showing the multiplier.</summary>
    private void DrawFlySpeed()
    {
        ImGui.SetNextItemWidth(SpeedWidth);
        var speed = session.Speed;
        var step = speed.Index;
        if (ImGui.SliderInt("##speed", ref step, 0, FlySpeed.Steps.Count - 1, $"{speed.Multiplier:0.##}x")) speed.Set(step);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fly speed");
    }

    /// <summary>Play/Pause and Restart, left of the scrub bar on the same line.</summary>
    private void DrawTransport()
    {
        var playing = session.IsPlaying;
        ImGui.BeginDisabled(session.Mode == CameraMode.Editing ? session.Track.Points.Count == 0 : !session.CanGoLive);
        if (IconButton.Draw("play-pause", playing ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, playing ? "Pause" : "Play"))
        {
            fields.Commit();
            if (playing) session.StopPlay();
            else session.StartPlay();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Released || (session.Mode == CameraMode.Editing ? session.Track.Points.Count == 0 : !session.CanGoLive));
        if (IconButton.Draw("restart", FontAwesomeIcon.StepBackward, "Restart")) { fields.Commit(); session.RestartPlay(); }
        ImGui.EndDisabled();
        ImGui.SameLine();
    }

    /// <summary>View, Edit and Live; View releases the camera and Live cues the playlist paused at its first entry's start.</summary>
    private void DrawModeCombo()
    {
        var current = session.Mode switch { CameraMode.View => 1, CameraMode.Editing => 2, CameraMode.Live => 3, _ => 0 };
        ImGui.SetNextItemWidth(ModeWidth);
        if (!ImGui.BeginCombo("##mode", ModeNames[current])) return;

        if (ImGui.Selectable(ModeNames[0], current == 0) && current != 0) { fields.Commit(); session.Release("window"); }
        if (ImGui.Selectable(ModeNames[1], current == 1) && current != 1) { fields.Commit(); session.Release("window", CameraMode.View); }
        if (ImGui.Selectable(ModeNames[2], current == 2) && current != 2) { fields.Commit(); session.EnterEdit(); }
        ImGui.BeginDisabled(!session.CanGoLive);
        if (ImGui.Selectable(ModeNames[3], current == 3) && current != 3) { fields.Commit(); session.CueLive(); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !session.CanGoLive) ImGui.SetTooltip("Add a track with points to the playlist");
        ImGui.EndDisabled();
        ImGui.EndCombo();
    }

    /// <summary>Aim and direction icons with their menus, the loop toggle, the Speed and Duration fields, the add button and its menu, and Clear track at the right end.</summary>
    private void DrawTrackRow()
    {
        DrawAim();

        var direction = Array.FindIndex(DirectionChoices, c => c.Direction == session.Track.Direction);
        ImGui.SameLine();
        if (IconButton.Draw("direction", DirectionChoices[direction].Icon, $"Select direction ({DirectionChoices[direction].Name})")) ImGui.OpenPopup("direction-menu");
        directionX = ImGui.GetItemRectMin().X;
        if (ImGui.BeginPopup("direction-menu"))
        {
            for (var i = 0; i < DirectionChoices.Length; i++)
            {
                if (!ImGui.Selectable(DirectionChoices[i].Name, i == direction) || i == direction) continue;
                var chosen = DirectionChoices[i].Direction;
                Report(session.ChangeTrack(t => TrackEditing.SetDirection(t, chosen)));
            }

            ImGui.EndPopup();
        }

        ImGui.SameLine();
        DrawLoop();

        ImGui.BeginDisabled(TrackEditing.AllPinned(session.Track));
        ImGui.SameLine();
        LabelledField(FontAwesomeIcon.TachometerAlt, "track-speed", session.Track.Speed, "%.2f", SpeedRange, "Track speed", v => Report(session.SetTrackSpeed(v)));
        ImGui.SameLine();
        LabelledField(FontAwesomeIcon.Stopwatch, "track-duration", (float)session.Duration, "%.1f s", ShotRange, "Track duration", v => Report(session.SetTrackDuration(v)));
        ImGui.EndDisabled();

        ImGui.SameLine();
        DrawAddButton();

        ImGui.SameLine();
        RightAlign(IconButton.Width(FontAwesomeIcon.Trash));
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (IconButton.Draw("clear-track", FontAwesomeIcon.Trash, "Clear track", danger: true)) { fields.Clear(); Report(session.ChangeTrack(TrackEditing.Clear)); }
        trashRight = ImGui.GetItemRectMax().X;
        ImGui.EndDisabled();
    }

    /// <summary>The aim icon, coloured and captioned by the character under Watch Target or Follow Target, and its menu.</summary>
    private void DrawAim()
    {
        var track = session.Track;
        var aim = Array.FindIndex(Aims, c => c.Mode == track.Aim);
        var (colour, tooltip) = track.Aim switch
        {
            AimMode.WatchTarget => TargetState(track, "Watch Target", "using recorded aim"),
            AimMode.FollowTarget => TargetState(track, "Follow Target", null),
            _ => ((uint?)null, $"Select aim ({Aims[aim].Name})"),
        };
        if (IconButton.Draw("aim", FontAwesomeIcon.Crosshairs, tooltip, colour)) ImGui.OpenPopup("aim-menu");
        aimX = ImGui.GetItemRectMin().X;
        var wasOpen = aimMenuOpen;
        aimMenuOpen = ImGui.BeginPopup("aim-menu");
        if (!aimMenuOpen)
        {
            // A Look ahead value typed and then clicked away from closes the menu before the field can apply it.
            if (wasOpen) fields.Commit(LookAheadId);
            return;
        }

        var pencil = IconButton.Width(FontAwesomeIcon.PencilAlt);
        var width = Aims.Max(c => ImGui.CalcTextSize(c.Name).X) + ImGui.GetStyle().ItemSpacing.X + pencil;
        for (var i = 0; i < Aims.Length; i++)
        {
            switch (Aims[i].Mode)
            {
                case AimMode.WatchTarget:
                    DrawTargetEntry(AimMode.WatchTarget, Aims[i].Name, i == aim, width, pencil, watchTarget.Open, null);
                    continue;
                case AimMode.FollowTarget:
                    var refusal = track.Points.Count > 1 ? "Follow Target needs a track with one point" : null;
                    DrawTargetEntry(AimMode.FollowTarget, Aims[i].Name, i == aim, width, pencil, followTarget.Open, refusal);
                    continue;
            }

            if (ImGui.Selectable(Aims[i].Name, i == aim) && i != aim) Report(session.SetAim(Aims[i].Mode));
            if (i == aim && Aims[i].Mode == AimMode.PathTangent) DrawLookAhead(track);
        }

        ImGui.EndPopup();
    }

    /// <summary>Direction of travel's Look ahead field, indented under it in the aim menu.</summary>
    private void DrawLookAhead(Track track)
    {
        const string tooltip = "How far ahead the camera looks along the path. 0 faces straight along it.";
        ImGui.Indent();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Look ahead");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        ImGui.SameLine();
        fields.Draw(LookAheadId, track.LookAhead, "%.2f s", FieldWidth, LookAheadRange, v => Report(session.SetLookAhead(v)));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        ImGui.Unindent();
    }

    /// <summary>The aim icon's colour and tooltip under a character mode: accent when found, red when lost or none is chosen.</summary>
    private (uint? Colour, string Tooltip) TargetState(Track track, string mode, string? lost)
    {
        if (track.TargetName is not { } name) return (UiColours.Red, $"{mode}: choose a character");
        return session.TargetLost(track) ? (UiColours.Red, lost is null ? $"{name} (Not found)" : $"{name} (Not found): {lost}") : (UiColours.Accent, $"{mode}: {name}");
    }

    /// <summary>A character mode's aim menu entry, which opens its dialog when the track switches into it, and on the current mode a pencil that reopens it; disabled with <paramref name="refusal"/> as its tooltip.</summary>
    private void DrawTargetEntry(AimMode mode, string label, bool chosen, float width, float pencil, Action open, string? refusal)
    {
        ImGui.BeginDisabled(refusal is not null);
        var start = ImGui.GetCursorPosX();
        var picked = ImGui.Selectable(label, chosen, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(width, 0f));
        if (refusal is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(refusal);
        var edit = false;
        if (chosen)
        {
            ImGui.SameLine(start + width - pencil);
            using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding with { Y = 0f }))
                edit = IconButton.Draw($"edit-{mode}", FontAwesomeIcon.PencilAlt, $"Edit {label}");
        }

        ImGui.EndDisabled();
        if (!picked && !edit) return;

        if (!chosen)
        {
            Report(session.SetAim(mode));
            if (session.Track.Aim == mode) open();
        }
        else if (edit)
        {
            open();
        }

        ImGui.CloseCurrentPopup();
    }

    /// <summary>The loop toggle: accent when the track loops, dimmed when it plays once.</summary>
    private void DrawLoop()
    {
        var loop = session.Track.Loop;
        if (IconButton.Toggle("loop", FontAwesomeIcon.Repeat, loop, loop ? "Play once" : "Loop"))
            Report(session.ChangeTrack(t => TrackEditing.SetLoop(t, !loop)));
        loopX = ImGui.GetItemRectMin().X;
    }

    /// <summary>A plus icon that appends a point, and a caret opening the insert menu.</summary>
    private void DrawAddButton()
    {
        if (IconButton.Draw("add-point", FontAwesomeIcon.Plus, "Add point")) Report(session.AddToEnd());
        ImGui.SameLine(0f, 0f);
        if (IconButton.Draw("add-menu", FontAwesomeIcon.CaretDown, "More ways to add")) ImGui.OpenPopup("add-menu");
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
        var evaluator = session.Evaluator;
        var footer = ImGui.GetFrameHeightWithSpacing() + (ImGui.GetStyle().ItemSpacing.Y * 2f);
        if (ImGui.BeginChild("points", new Vector2(0f, -footer)))
        {
            using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, CellPadding);
            if (ImGui.BeginTable("point-table", 6, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX))
            {
                ImGui.TableSetupColumn("#");
                ImGui.TableSetupColumn("Duration (s)");
                ImGui.TableSetupColumn("Speed");
                ImGui.TableSetupColumn("Hold (s)");
                ImGui.TableSetupColumn("##pin", ImGuiTableColumnFlags.WidthFixed, IconButton.Width(FontAwesomeIcon.Thumbtack));
                ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                var selected = session.SelectedPoints;
                for (var i = 0; i < track.Points.Count; i++) DrawPointRow(track, evaluator, i, selected, editing);
                ImGui.EndTable();
            }

            DrawPointSpace(track, editing);
        }

        ImGui.EndChild();
    }

    /// <summary>One point and the leg arriving at it: the whole row selects on click, jumps on double-click, drags to reorder or onto a track, and right-clicks for its menu; the trash icon, shown on hover, deletes it.</summary>
    /// <remarks><paramref name="track"/> and <paramref name="evaluator"/> are a snapshot taken once for the whole list: an earlier row's delete or reorder must not change what a later row reads.</remarks>
    private void DrawPointRow(Track track, TrackEvaluator evaluator, int index, IReadOnlyList<int> selected, bool editing)
    {
        ImGui.TableNextRow();
        ImGui.BeginDisabled(!editing);

        ImGui.TableNextColumn();
        // The row's full height, cell padding included, so neighbouring rows' hover areas meet.
        var top = ImGui.GetCursorScreenPos().Y - CellPadding.Y;
        var rowFlags = ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap;
        var picked = selected.Contains(index);
        var group = picked && selected.Count >= 2;
        if (ImGui.Selectable($"##row{index}", picked, rowFlags, new Vector2(0f, ImGui.GetFrameHeight()))) session.ClickPoint(index, DragRows.Click());
        var rowHovered = editing && IconButton.RowHovered(
            new Vector2(ImGui.GetItemRectMin().X, top), new Vector2(ImGui.GetItemRectMax().X, top + ImGui.GetFrameHeight() + (CellPadding.Y * 2f)));
        if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) session.JumpToPoint(index);
        if (editing && ImGui.BeginDragDropSource())
        {
            DragRows.Carry(DragRows.Point, index, group, group ? $"{selected.Count} points" : $"Point {index + 1}");
            ImGui.EndDragDropSource();
        }

        DropTarget(index, editing);
        if (editing && ImGui.BeginPopupContextItem($"point-menu{index}"))
        {
            DrawPointMenu(group ? selected : [index]);
            ImGui.EndPopup();
        }

        ImGui.SameLine(0f, 0f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{index + 1}");

        ImGui.TableNextColumn();
        if (index > 0) fields.Draw($"leg{index}", evaluator.LegSeconds(index), "%.1f", FieldWidth, LegRange, v => Report(session.SetLegDuration(index, v)));

        ImGui.TableNextColumn();
        if (index > 0)
            fields.Draw($"leg-speed{index}", evaluator.LegLength(index) / evaluator.LegSeconds(index), "%.2f", FieldWidth, SpeedRange, v => Report(session.SetLegSpeed(index, v)));

        ImGui.TableNextColumn();
        fields.Draw($"hold{index}", TrackEditing.HoldSeconds(track, index), "%.1f", FieldWidth, HoldRange,
            v => Report(session.ChangeTrack(t => TrackEditing.SetHold(t, index, EditLimits.Hold(v)))));

        ImGui.TableNextColumn();
        if (index > 0) DrawPin(track, index, rowHovered);

        ImGui.TableNextColumn();
        RightAlign(IconButton.Width(FontAwesomeIcon.Trash));
        if (IconButton.RowAction($"delete{index}", FontAwesomeIcon.Trash, "Delete point", rowHovered, danger: true))
        {
            fields.Clear();
            Report(session.DeletePoints([index]));
        }

        ImGui.EndDisabled();
    }

    /// <summary>The leg's pin: always shown in the accent colour when pinned, shown only on row hover when following the track speed.</summary>
    private void DrawPin(Track track, int index, bool rowHovered)
    {
        if (TrackEditing.IsPinned(track, index))
        {
            if (IconButton.Draw($"pin{index}", FontAwesomeIcon.Thumbtack, "Pin to Track speed", UiColours.Accent))
                Report(session.ResetLeg(index));
            return;
        }

        if (IconButton.RowAction($"pin{index}", FontAwesomeIcon.Thumbtack, "Pin to Leg speed", rowHovered))
            Report(session.SetLegSpeed(index, TrackEditing.LegSpeed(track, index)));
    }

    /// <summary>Play/Pause and Restart, then the scrub bar showing current and total time.</summary>
    private void DrawScrubRow()
    {
        var duration = (float)session.ScrubLength;
        var head = (float)session.ScrubHead;

        DrawTransport();
        ImGui.BeginDisabled(session.Released || duration <= 0f);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var moved = ImGui.SliderFloat("##scrub", ref head, 0f, MathF.Max(duration, 0.001f), $"%.1f / {duration:0.0} s");
        if (ImGui.IsItemActivated()) { fields.Commit(); session.BeginScrub(); scrubbing = session.Scrubbing; }
        if (moved || ImGui.IsItemActivated()) session.ScrubTo(head);
        // A window that stops drawing mid-drag never reports the slider deactivating, so any idle frame ends the scrub too.
        if (ImGui.IsItemDeactivated() || !ImGui.IsItemActive()) EndScrub();
        ImGui.EndDisabled();
    }

    /// <summary>The track row's full width: its items, the eight gaps between them, and the window's and the editor's padding.</summary>
    private static float TrackRowWidth()
    {
        var style = ImGui.GetStyle();
        var direction = DirectionChoices.Max(c => IconButton.Width(c.Icon));
        var items = IconButton.Width(FontAwesomeIcon.Crosshairs) + direction + IconButton.Width(FontAwesomeIcon.Repeat)
            + IconWidth(FontAwesomeIcon.TachometerAlt) + IconWidth(FontAwesomeIcon.Stopwatch) + (FieldWidth * 2f)
            + IconButton.Width(FontAwesomeIcon.Plus) + IconButton.Width(FontAwesomeIcon.CaretDown) + IconButton.Width(FontAwesomeIcon.Trash);
        return items + (Spacing.X * 8f) + (style.WindowPadding.X * 4f);
    }

    /// <summary>The top bar's full width: its items, the larger of LIVE and fly speed, the gaps between them, and the window padding.</summary>
    private static float TopRowWidth()
    {
        var style = ImGui.GetStyle();
        var items = IconButton.Width(FontAwesomeIcon.Sitemap) + IconButton.Width(FontAwesomeIcon.ListOl) + ModeWidth
            + IconButton.Width(FontAwesomeIcon.Undo) + IconButton.Width(FontAwesomeIcon.Redo) + IconButton.Width(FontAwesomeIcon.ChartLine)
            + IconButton.Width(FontAwesomeIcon.EyeSlash) + IconButton.Width(FontAwesomeIcon.Cog) + IconButton.Width(FontAwesomeIcon.Question);
        var live = ImGui.CalcTextSize("LIVE").X + Spacing.X;
        var tools = IconButton.Width(FontAwesomeIcon.RulerHorizontal) + Spacing.X + IconButton.Width(FontAwesomeIcon.Camera) + Spacing.X;
        var flySpeed = (Spacing.X * 3f) + tools + SpeedWidth;
        return items + MathF.Max(live, flySpeed) + (Spacing.X * 10f) + (style.WindowPadding.X * 2f);
    }

    /// <summary>The width of an icon drawn as text in the icon font.</summary>
    private static float IconWidth(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(icon.ToIconString()).X;
    }

    private void SetMinimumWidth(float width)
        => SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(width, MinHeight), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };

    /// <summary>An icon, then a drag field; both show the tooltip, even while disabled.</summary>
    private void LabelledField(FontAwesomeIcon icon, string id, float current, string format, PendingField.Range range, string tooltip, Action<float> apply)
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextUnformatted(icon.ToIconString());
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
        ImGui.SameLine();
        fields.Draw(id, current, format, FieldWidth, range, apply);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
    }

    /// <summary>Moves the dragged points to <paramref name="index"/>, or the end when null, when they are dropped on this item.</summary>
    private void DropTarget(int? index, bool editing)
    {
        if (!editing || !ImGui.BeginDragDropTarget()) return;
        if (DragRows.Accept(DragRows.Point) is { } points) Report(session.MovePoints(DragRows.Points(session, points), points.Grabbed, index));
        ImGui.EndDragDropTarget();
    }

    /// <summary>The menu for points: move them to a new track or another one, or delete them.</summary>
    private void DrawPointMenu(IReadOnlyList<int> points)
    {
        var ticked = false;
        var scene = session.Scene;
        if (ImGui.MenuItem("Move to new track", string.Empty, ref ticked)) Report(session.MovePointsTo(points, null));
        if (ImGui.BeginMenu("Move to", scene.Tracks.Count > 1))
        {
            foreach (var other in scene.Tracks.Where(t => t.Id != session.EditedTrackId))
            {
                using var id = ImRaii.PushId(other.Id.ToString());
                if (ImGui.MenuItem(other.Name, string.Empty, ref ticked, PointTransfer.CanTake(other, session.EditedTrackId))) Report(session.MovePointsTo(points, other.Id));
            }

            ImGui.EndMenu();
        }

        if (ImGui.MenuItem("Delete", string.Empty, ref ticked))
        {
            fields.Clear();
            Report(session.DeletePoints(points));
        }
    }

    /// <summary>The space under the points: it takes dropped points at the end, and a click there clears the selection.</summary>
    private void DrawPointSpace(Track track, bool editing)
    {
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, MathF.Max(ImGui.GetContentRegionAvail().Y, ImGui.GetFrameHeight())));
        if (!editing) return;
        if (ImGui.IsItemClicked() && DragRows.Click() == RowClick.Plain) session.Select(null);
        if (track.Points.Count > 0) DropTarget(null, editing);
    }

    /// <summary>Ends a scrub the scrub bar started, leaving the Timing window's alone.</summary>
    private void EndScrub()
    {
        if (!scrubbing) return;
        scrubbing = false;
        session.FinishScrub();
    }

    /// <summary>Moves the cursor so an item of <paramref name="width"/> ends at the right edge.</summary>
    private static void RightAlign(float width)
        => ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - width));
}
