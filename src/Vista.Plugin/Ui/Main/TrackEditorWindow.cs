using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Vista.Core.Camera;
using Vista.Core.Display;
using Vista.Core.Editing;
using Vista.Core.Input;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Playback;
using Vista.Plugin.Editor;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;
using Vista.Plugin.Ui.Windows;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Main;

/// <summary>The main Vista window: the menu bar, modes, the Hierarchy, track settings, the point list, the edited track's scrub bar and the Playlist.</summary>
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

    private const float SpeedWidth = 90f;
    private const float ModeWidth = 80f;
    private const float MinWidth = 420f;
    private const float MinHeight = 260f;

    // Speeds in yalms per second, and shot lengths in seconds, within the editor's limits.
    internal static readonly PendingField.Range SpeedRange = new(0.05f, TrackEditing.MinSpeed, TrackEditing.MaxSpeed);
    private static readonly PendingField.Range ShotRange = new(
        0.1f,
        TrackEditing.MinShotSeconds,
        TrackEditing.MaxShotSeconds
    );
    private static readonly PendingField.Range LookAheadRange = new(0.05f, 0f, TrackEditing.MaxLookAhead);
    private const string LookAheadId = "look-ahead";

    private const string WindowId = "###vista-track-editor";

    private readonly GameSession game;
    private readonly SessionState session;
    private readonly CameraWindow camera;
    private readonly SwitchboardWindow switchboard;
    private readonly Configuration config;
    private bool aimMenuOpen;
    private readonly PendingEdit<float> fields;
    private readonly EditCommands commands;
    private readonly TimingWindow timing;
    private readonly WatchTargetWindow watchTarget;
    private readonly FollowTargetWindow followTarget;
    private readonly MainMenu menu;
    private readonly GuideWindow guide;
    private readonly HierarchyPanel hierarchy;
    private readonly PlaylistPanel playlist;
    private readonly PointList points;
    private CameraMode lastMode;
    private readonly Scrubber scrub;
    private readonly ViewZoom<(CameraMode Mode, Guid Track)> scrubZoom = new();
    private bool showHierarchy = true;
    private bool showPlaylist = true;
    private float pendingWidth;
    private float? edgeGrabbed;

    // Where last frame's track row put its buttons, in screen X, so the top bar can line up with them.
    private float? aimX;
    private float? directionX;
    private float? loopX;
    private float? trashRight;

    public TrackEditorWindow(
        GameSession game,
        Configuration config,
        PendingEdit<float> fields,
        TimingWindow timing,
        CameraWindow camera,
        SwitchboardWindow switchboard,
        GuideWindow guide,
        WatchTargetWindow watchTarget,
        FollowTargetWindow followTarget,
        SceneFiles files,
        SetupWindow setup,
        FilePickerWindow picker,
        EditorLayer layer
    )
        : base("Vista" + WindowId, ImGuiWindowFlags.MenuBar)
    {
        this.game = game;
        session = game.State;
        this.camera = camera;
        this.switchboard = switchboard;
        this.config = config;
        config.HierarchyWidth = PanelWidth.Clamp(config.HierarchyWidth);
        config.PlaylistWidth = PanelWidth.Clamp(config.PlaylistWidth);
        config.TrackNameScale = TrackNameSize.Clamp(config.TrackNameScale);
        this.fields = fields;
        commands = new EditCommands(game, fields);
        this.timing = timing;
        this.watchTarget = watchTarget;
        this.followTarget = followTarget;
        var presetSave = new PresetSave(files);
        menu = new MainMenu(
            game,
            config,
            fields,
            commands,
            files,
            layer,
            timing,
            camera,
            switchboard,
            guide,
            setup,
            picker,
            presetSave
        );
        this.guide = guide;
        scrub = new Scrubber(game);
        hierarchy = new HierarchyPanel(game, files, picker, presetSave);
        playlist = new PlaylistPanel(session, picker);
        points = new PointList(game, fields);
        RespectCloseHotkey = false;
        SizeCondition = ImGuiCond.FirstUseEver;
        SetMinimumWidth(MinWidth);
    }

    /// <summary>Widens the minimum size to fit the top bar, the track row and any open compartment, and opens at that width on first use.</summary>
    public override void PreDraw()
    {
        // The measured widths are on-screen sizes, and Dalamud multiplies the size and constraints by the global scale.
        var scale = ImGuiHelpers.GlobalScale;
        var width =
            MathF.Max(MathF.Max(MinWidth * scale, TrackRowWidth()) + CompartmentsWidth(), TopRowWidth()) / scale;
        SetMinimumWidth(width);
        Size = new Vector2(width, MinHeight);
    }

    /// <summary>The width the open compartments beside the track editor take, with their gap.</summary>
    private float CompartmentsWidth() =>
        (showHierarchy ? config.HierarchyWidth + Layout.Spacing.X : 0f)
        + (showPlaylist ? config.PlaylistWidth + Layout.Spacing.X : 0f);

    /// <summary>Applies an unfinished field edit and ends a scrub, since a closed window never reports either finishing.</summary>
    public override void OnClose()
    {
        fields.Commit();
        scrub.End();
    }

    public override void Draw()
    {
        if (session.Mode != lastMode)
        {
            fields.Clear();
            lastMode = session.Mode;
        }

        using var style = WindowStyle.Push();
        var editing = session.Mode == CameraMode.Editing;
        DrawMenuBar();
        DrawTopRow(editing);

        if (showHierarchy)
        {
            if (ImGui.BeginChild("hierarchy", new Vector2(config.HierarchyWidth, 0f), true))
                hierarchy.Draw(editing);
            ImGui.EndChild();
            ImGui.SameLine(0f, 0f);
            if (DrawEdge("hierarchy-edge", config.HierarchyWidth, 1f) is { } width)
                config.HierarchyWidth = width;
            ImGui.SameLine(0f, 0f);
        }

        // On a side with no compartment the editor reaches into the window's padding, so its rows line up with the top row.
        var padding = ImGui.GetStyle().WindowPadding.X;
        if (!showHierarchy)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - padding);
        var editorWidth = MathF.Floor(
            ImGui.GetContentRegionAvail().X + (showPlaylist ? -(config.PlaylistWidth + Layout.Spacing.X) : padding)
        );
        // Its content is exactly as wide as it shows, so the points list reaching into the right padding can't make it scroll sideways.
        ImGui.SetNextWindowContentSize(new Vector2(editorWidth - (padding * 2f), 0f));
        // The same inner padding as the bordered compartments, so the rows and separators line up.
        if (
            ImGui.BeginChild(
                "track-editor",
                new Vector2(editorWidth, 0f),
                false,
                ImGuiWindowFlags.AlwaysUseWindowPadding
            )
        )
        {
            ImGui.BeginDisabled(!editing);
            DrawTrackRow();
            ImGui.EndDisabled();
            ImGui.Separator();

            points.Draw(editing);
            ImGui.Separator();
            // The points list reserves more than a row for this footer; centre the row in it, then nudge it down half a gap to look centred against the window's bottom padding.
            var slack = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeight();
            if (slack > 0f)
                ImGui.SetCursorPosY(
                    ImGui.GetCursorPosY() + MathF.Min(slack, (slack / 2f) + (ImGui.GetStyle().ItemSpacing.Y / 2f))
                );
            DrawScrubRow();
        }

        ImGui.EndChild();

        if (showPlaylist)
        {
            ImGui.SameLine(0f, 0f);
            if (DrawEdge("playlist-edge", config.PlaylistWidth, -1f) is { } width)
                config.PlaylistWidth = width;
            ImGui.SameLine(0f, 0f);
            if (ImGui.BeginChild("playlist", new Vector2(config.PlaylistWidth, 0f), true))
                playlist.Draw(editing);
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
        ImGui.InvisibleButton(id, new Vector2(Layout.Spacing.X, MathF.Max(1f, ImGui.GetContentRegionAvail().Y)));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        if (ImGui.IsItemActivated())
            edgeGrabbed = width;
        if (ImGui.IsItemDeactivated())
            config.Save();
        if (!ImGui.IsItemActive() || edgeGrabbed is not { } grabbed)
            return null;
        return PanelWidth.Clamp(grabbed + (sign * ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0f).X));
    }

    /// <summary>The menu bar; showing or hiding a panel grows or shrinks the window by its width.</summary>
    private void DrawMenuBar()
    {
        var (hierarchyShown, playlistShown) = (showHierarchy, showPlaylist);
        menu.Draw(ref showHierarchy, ref showPlaylist);
        if (showHierarchy != hierarchyShown)
            PanelShown(showHierarchy, config.HierarchyWidth);
        if (showPlaylist != playlistShown)
            PanelShown(showPlaylist, config.PlaylistWidth);
    }

    /// <summary>Grows the window by a panel <paramref name="width"/> wide as it shows, or shrinks it as it hides.</summary>
    private void PanelShown(bool shown, float width) => pendingWidth += (shown ? 1f : -1f) * (width + Layout.Spacing.X);

    /// <summary>The Hierarchy and Playlist toggles, at the start of the top row.</summary>
    private void DrawPanelToggles()
    {
        if (
            IconButton.Toggle(
                "hierarchy",
                FontAwesomeIcon.Sitemap,
                showHierarchy,
                showHierarchy ? "Hide hierarchy" : "Show hierarchy"
            )
        )
        {
            showHierarchy = !showHierarchy;
            PanelShown(showHierarchy, config.HierarchyWidth);
        }

        ImGui.SameLine();
        if (
            IconButton.Toggle(
                "playlist",
                FontAwesomeIcon.ListOl,
                showPlaylist,
                showPlaylist ? "Hide playlist" : "Show playlist"
            )
        )
        {
            showPlaylist = !showPlaylist;
            PanelShown(showPlaylist, config.PlaylistWidth);
        }
    }

    private void DrawTopRow(bool editing)
    {
        DrawPanelToggles();
        ImGui.SameLine();
        DrawModeCombo();

        var gap = ImGui.GetStyle().ItemSpacing.X * 3f;
        AlignTo(aimX, gap);
        ImGui.BeginDisabled(!session.CanUndo);
        if (IconButton.Draw("undo", FontAwesomeIcon.Undo, HotkeyTable.Undo.Hotkey.Tooltip("Undo")))
            commands.Undo();
        ImGui.EndDisabled();

        AlignTo(directionX, ImGui.GetStyle().ItemSpacing.X);
        ImGui.BeginDisabled(!session.CanRedo);
        if (IconButton.Draw("redo", FontAwesomeIcon.Redo, HotkeyTable.Redo.Hotkey.Tooltip("Redo")))
            commands.Redo();
        ImGui.EndDisabled();

        AlignTo(loopX, ImGui.GetStyle().ItemSpacing.X);
        IconButton.WindowToggle("timing", FontAwesomeIcon.ChartLine, "Timing", timing);

        // Level camera roll, Camera and fly speed are Edit's, so they show disabled in the other modes.
        var tools = IconButton.RowWidth(FontAwesomeIcon.RulerHorizontal, FontAwesomeIcon.Camera, FontAwesomeIcon.Video);
        AlignTo(FlySpeedStart() - tools - ImGui.GetStyle().ItemSpacing.X, gap);
        using (ImRaii.Disabled(!editing))
        {
            if (
                IconButton.Draw(
                    "level-roll",
                    FontAwesomeIcon.RulerHorizontal,
                    HotkeyTable.LevelRoll.Hotkey.Tooltip("Level camera roll")
                )
            )
                game.LevelCameraRoll();
            ImGui.SameLine();
            IconButton.WindowToggle("camera", FontAwesomeIcon.Camera, "Camera", camera);
        }

        ImGui.SameLine();
        IconButton.WindowToggle("switchboard", FontAwesomeIcon.Video, "Switchboard", switchboard);
        ImGui.SameLine();
        using (ImRaii.Disabled(!editing))
            DrawFlySpeed();

        // Hide game UI and User Guide at the right end, with LIVE just before them in Live.
        var rightEnd = RightEndWidth();
        ImGui.SameLine();
        if (session.Mode == CameraMode.Live)
        {
            Layout.RightAlign(LiveWidth() + rightEnd);
            DrawLive();
            ImGui.SameLine();
        }
        Layout.RightAlign(rightEnd);
        if (IconButton.Toggle("hide-ui", FontAwesomeIcon.EyeSlash, game.HideUiInLive, "Hide game UI when Live"))
            game.HideUiInLive = !game.HideUiInLive;
        ImGui.SameLine();
        if (IconButton.Draw("feedback", FontAwesomeIcon.Comment, Feedback.Label))
            Feedback.Open();
        ImGui.SameLine();
        IconButton.WindowToggle("guide", FontAwesomeIcon.Question, "User Guide", guide);
    }

    /// <summary>LIVE and the gap after it.</summary>
    private static float LiveWidth() => ImGui.CalcTextSize("LIVE").X + ImGui.GetStyle().ItemSpacing.X;

    /// <summary>Hide game UI, Give feedback and User Guide, at the top row's right end.</summary>
    private static float RightEndWidth() =>
        IconButton.RowWidth(FontAwesomeIcon.EyeSlash, FontAwesomeIcon.Comment, FontAwesomeIcon.Question);

    /// <summary>Where fly speed's slider starts so it ends under the track row's trash, or before LIVE and the Hide game UI button if that's nearer; null before the first frame.</summary>
    private float? FlySpeedStart()
    {
        if (trashRight is not { } right)
            return null;
        var edge =
            ImGui.GetWindowPos().X
            + ImGui.GetWindowContentRegionMax().X
            - RightEndWidth()
            - ImGui.GetStyle().ItemSpacing.X
            - (session.Mode == CameraMode.Live ? LiveWidth() : 0f);
        return MathF.Min(right, edge) - SpeedWidth;
    }

    /// <summary>Continues the row at <paramref name="screenX"/> when that's at least <paramref name="gap"/> past the last item, else just after it.</summary>
    private static void AlignTo(float? screenX, float gap)
    {
        ImGui.SameLine(0f, gap);
        var at = ImGui.GetCursorScreenPos();
        if (screenX is { } x && x > at.X)
            ImGui.SetCursorScreenPos(at with { X = x });
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
        var speed = game.Speed;
        var step = speed.Index;
        if (
            ImGui.SliderInt(
                "##speed",
                ref step,
                0,
                FlySpeed.Steps.Count - 1,
                FormattableString.Invariant($"{speed.Multiplier:0.##}x")
            )
        )
            speed.Set(step);
        Tooltip.OnHover("Fly speed");
    }

    /// <summary>Play/Pause and Restart, left of the scrub bar on the same line.</summary>
    private void DrawTransport()
    {
#if DEBUG
        using var selfTest = commands.SelfTestGuard();
#endif
        var playing = session.IsPlaying;
        ImGui.BeginDisabled(!session.CanStart);
        if (
            IconButton.Draw(
                "play-pause",
                playing ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play,
                playing ? HotkeyTable.Play.Hotkey.Tooltip("Pause") : HotkeyTable.Play.Hotkey.Tooltip("Play")
            )
        )
            commands.TogglePlay();

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!session.CanRestart);
        if (IconButton.Draw("restart", FontAwesomeIcon.StepBackward, HotkeyTable.Restart.Hotkey.Tooltip("Restart")))
            commands.Restart();
        ImGui.EndDisabled();
        ImGui.SameLine();
    }

    /// <summary>View, Edit and Live; View releases the camera and Live enters with the Program shot paused where it had got to.</summary>
    private void DrawModeCombo()
    {
        var current = session.Mode switch
        {
            CameraMode.View => 1,
            CameraMode.Editing => 2,
            CameraMode.Live => 3,
            _ => 0,
        };
        ImGui.SetNextItemWidth(ModeWidth);
#if DEBUG
        using var selfTest = commands.SelfTestGuard();
#endif
        if (!ImGui.BeginCombo("##mode", ModeNames[current]))
            return;

        if (ImGui.Selectable(ModeNames[0], current == 0) && current != 0)
        {
            fields.Commit();
            game.Release("window");
        }
        if (ImGui.Selectable(ModeNames[1], current == 1) && current != 1)
        {
            fields.Commit();
            game.Release("window", CameraMode.View);
        }
        ImGui.BeginDisabled(session.Stopped);
        if (ImGui.Selectable(ModeNames[2], current == 2) && current != 2)
        {
            fields.Commit();
            game.EnterEdit();
        }
        if (session.Stopped)
            Tooltip.OnHover(SessionState.StopMessage);
        ImGui.EndDisabled();
        ImGui.BeginDisabled(!session.CanGoLive);
        if (ImGui.Selectable(ModeNames[3], current == 3) && current != 3)
        {
            fields.Commit();
            game.CueLive();
        }
        if (session.Stopped)
            Tooltip.OnHover(SessionState.StopMessage);
        ImGui.EndDisabled();
        ImGui.EndCombo();
    }

    /// <summary>Aim and direction icons with their menus, the loop toggle, the Speed and Duration fields, the add button and its menu, and Clear track at the right end.</summary>
    private void DrawTrackRow()
    {
        DrawAim();

        var direction = Array.FindIndex(DirectionChoices, c => c.Direction == session.Track.Direction);
        ImGui.SameLine();
        if (
            IconButton.Draw(
                "direction",
                DirectionChoices[direction].Icon,
                $"Select direction ({DirectionChoices[direction].Name})"
            )
        )
            ImGui.OpenPopup("direction-menu");
        directionX = ImGui.GetItemRectMin().X;
        if (ImGui.BeginPopup("direction-menu"))
        {
            for (var i = 0; i < DirectionChoices.Length; i++)
            {
                if (!ImGui.Selectable(DirectionChoices[i].Name, i == direction) || i == direction)
                    continue;
                var chosen = DirectionChoices[i].Direction;
                Report(session.ChangeTrack(t => TrackEditing.SetDirection(t, chosen)));
            }

            ImGui.EndPopup();
        }

        ImGui.SameLine();
        DrawLoop();

        ImGui.BeginDisabled(TrackEditing.AllPinned(session.Track));
        ImGui.SameLine();
        LabelledField(
            FontAwesomeIcon.TachometerAlt,
            "track-speed",
            session.Track.Speed,
            Units.YalmsPerSecondField,
            SpeedRange,
            "Track speed",
            v => Report(session.SetTrackSpeed(v))
        );
        ImGui.SameLine();
        LabelledField(
            FontAwesomeIcon.Stopwatch,
            "track-duration",
            (float)session.Duration,
            Units.SecondsField,
            ShotRange,
            "Track duration",
            v => Report(session.SetTrackDuration(v))
        );
        ImGui.EndDisabled();

        ImGui.SameLine();
        DrawAddButton();

        ImGui.SameLine();
        Layout.RightAlign(IconButton.Width(FontAwesomeIcon.Trash));
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (IconButton.Draw("clear-track", FontAwesomeIcon.Trash, "Clear track", danger: true))
        {
            fields.Clear();
            Report(session.ChangeTrack(TrackEditing.Clear));
        }
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
            AimMode.WatchTarget => AimTip(track, "Watch Target", "using recorded aim"),
            AimMode.FollowTarget => AimTip(track, "Follow Target", null),
            _ => (null, $"Select aim ({Aims[aim].Name})"),
        };
        if (IconButton.Draw("aim", FontAwesomeIcon.Crosshairs, tooltip, colour))
            ImGui.OpenPopup("aim-menu");
        aimX = ImGui.GetItemRectMin().X;
        var wasOpen = aimMenuOpen;
        aimMenuOpen = ImGui.BeginPopup("aim-menu");
        if (!aimMenuOpen)
        {
            // A Look ahead value typed and then clicked away from closes the menu before the field can apply it.
            if (wasOpen)
                fields.Commit(LookAheadId);
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
                    var refusal = TrackEditing.AimRefusal(track, AimMode.FollowTarget);
                    DrawTargetEntry(
                        AimMode.FollowTarget,
                        Aims[i].Name,
                        i == aim,
                        width,
                        pencil,
                        followTarget.Open,
                        refusal
                    );
                    continue;
            }

            if (ImGui.Selectable(Aims[i].Name, i == aim) && i != aim)
                Report(game.SetAim(Aims[i].Mode));
            if (i == aim && Aims[i].Mode == AimMode.PathTangent)
                DrawLookAhead(track);
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
        Tooltip.OnHover(tooltip);
        ImGui.SameLine();
        fields.Draw(
            LookAheadId,
            track.LookAhead,
            Units.YalmsUnitField,
            Layout.FieldWidth,
            LookAheadRange,
            v => Report(session.SetLookAhead(v))
        );
        Tooltip.OnHover(tooltip);
        ImGui.Unindent();
    }

    /// <summary>The aim icon's colour and tooltip under a character mode: accent when found, red when lost or none is chosen.</summary>
    private (uint? Colour, string Tooltip) AimTip(Track track, string mode, string? lost)
    {
        var name = track.TargetName;
        return session.World.StateOfTarget(track) switch
        {
            TargetState.Unchosen => (UiColours.Red, $"{mode}: choose a character"),
            TargetState.Lost => (UiColours.Red, lost is null ? $"{name} (Not found)" : $"{name} (Not found): {lost}"),
            _ => (UiColours.Accent, $"{mode}: {name}"),
        };
    }

    /// <summary>A character mode's aim menu entry, which opens its dialog when the track switches into it, and on the current mode a pencil that reopens it; disabled with <paramref name="refusal"/> as its tooltip.</summary>
    private void DrawTargetEntry(
        AimMode mode,
        string label,
        bool chosen,
        float width,
        float pencil,
        Action open,
        string? refusal
    )
    {
        ImGui.BeginDisabled(refusal is not null);
        var start = ImGui.GetCursorPosX();
        var picked = ImGui.Selectable(label, chosen, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(width, 0f));
        if (refusal is not null)
            Tooltip.OnHover(refusal);
        var edit = false;
        if (chosen)
        {
            ImGui.SameLine(start + width - pencil);
            using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding with { Y = 0f }))
                edit = IconButton.Draw($"edit-{mode}", FontAwesomeIcon.PencilAlt, $"Edit {label}");
        }

        ImGui.EndDisabled();
        if (!picked && !edit)
            return;

        if (!chosen)
        {
            Report(game.SetAim(mode));
            if (session.Track.Aim == mode)
                open();
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
        if (IconButton.Draw("add-point", FontAwesomeIcon.Plus, HotkeyTable.AddToEnd.Hotkey.Tooltip("Add point")))
            Report(game.AddToEnd());
        ImGui.SameLine(0f, 0f);
        if (IconButton.Draw("add-menu", FontAwesomeIcon.CaretDown, "More ways to add"))
            ImGui.OpenPopup("add-menu");
        if (!ImGui.BeginPopup("add-menu"))
            return;

        AddPointItems.Draw(game);
        ImGui.EndPopup();
    }

    /// <summary>Play/Pause and Restart, the edited track's scrub bar, then how far through it the head is.</summary>
    private void DrawScrubRow()
    {
        var (head, total) = (session.Transport.ScrubHead, session.Duration);
        var view = scrubZoom.View((session.Mode, session.EditedTrackId), (float)total);

        DrawTransport();
        // The time sits right of the bar, sized for its longest reading so the bar doesn't shift as it counts.
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var timeWidth = ImGui.CalcTextSize(Units.ClockOf(total, total)).X;
        var width = ImGui.GetContentRegionAvail().X - timeWidth - spacing;
        using (ImRaii.PushStyle(ImGuiStyleVar.GrabMinSize, ImGui.GetStyle().GrabMinSize * ScrubBar.GrabScale))
            DrawTrackBar(width, view);
        ScrubBar.Zoom(scrubZoom, view, (float)total, scrub.Active);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Units.ClockOf(head, total));
    }

    /// <summary>The edited track's scrub bar, <paramref name="width"/> wide over <paramref name="view"/>, disabled outside Edit; the grab hides while the head is outside it.</summary>
    private void DrawTrackBar(float width, TimingView view)
    {
        var duration = (float)session.Duration;
        var head = (float)session.Transport.ScrubHead;
        ImGui.BeginDisabled(session.Mode != CameraMode.Editing || duration <= 0f);
        ImGui.SetNextItemWidth(MathF.Max(width, 1f));
        var outside = head < view.From || head > view.To;
        using var hidden = ImRaii
            .PushColor(ImGuiCol.SliderGrab, 0u, outside)
            .Push(ImGuiCol.SliderGrabActive, 0u, outside);
        var moved = ImGui.SliderFloat("##scrub", ref head, view.From, MathF.Max(view.To, view.From + 0.001f), "");
        if (ImGui.IsItemActivated())
        {
            fields.Commit();
            scrub.Begin();
        }
        if (moved || ImGui.IsItemActivated())
            session.Transport.ScrubTo(head);
        // A window that stops drawing mid-drag never reports the slider deactivating, so any idle frame ends the scrub too.
        if (ImGui.IsItemDeactivated() || !ImGui.IsItemActive())
            scrub.End();
        ImGui.EndDisabled();
    }

    /// <summary>The track row's full width: its items, the eight gaps between them, and the window's and the editor's padding.</summary>
    private static float TrackRowWidth()
    {
        var style = ImGui.GetStyle();
        var direction = DirectionChoices.Max(c => IconButton.Width(c.Icon));
        var items =
            IconButton.Width(FontAwesomeIcon.Crosshairs)
            + direction
            + IconButton.Width(FontAwesomeIcon.Repeat)
            + IconButton.GlyphWidth(FontAwesomeIcon.TachometerAlt)
            + IconButton.GlyphWidth(FontAwesomeIcon.Stopwatch)
            + (Layout.FieldWidth * 2f)
            + IconButton.Width(FontAwesomeIcon.Plus)
            + IconButton.Width(FontAwesomeIcon.CaretDown)
            + IconButton.Width(FontAwesomeIcon.Trash);
        return items + (Layout.Spacing.X * 8f) + (style.WindowPadding.X * 4f);
    }

    /// <summary>The top bar's full width: its items, LIVE and fly speed, the gaps between them, and the window padding.</summary>
    private static float TopRowWidth()
    {
        var style = ImGui.GetStyle();
        var items =
            ModeWidth
            + IconButton.Width(FontAwesomeIcon.Sitemap)
            + IconButton.Width(FontAwesomeIcon.ListOl)
            + IconButton.Width(FontAwesomeIcon.Undo)
            + IconButton.Width(FontAwesomeIcon.Redo)
            + IconButton.Width(FontAwesomeIcon.ChartLine)
            + IconButton.Width(FontAwesomeIcon.RulerHorizontal)
            + IconButton.Width(FontAwesomeIcon.Camera)
            + IconButton.Width(FontAwesomeIcon.Video)
            + IconButton.Width(FontAwesomeIcon.EyeSlash)
            + IconButton.Width(FontAwesomeIcon.Comment)
            + IconButton.Width(FontAwesomeIcon.Question);
        var live = Layout.Spacing.X + ImGui.CalcTextSize("LIVE").X;
        var flySpeed = (Layout.Spacing.X * 3f) + SpeedWidth;
        return items + live + flySpeed + (Layout.Spacing.X * 13f) + (style.WindowPadding.X * 2f);
    }

    private void SetMinimumWidth(float width) => SizeConstraints = Layout.AtLeast(new Vector2(width, MinHeight));

    /// <summary>An icon, then a drag field; both show the tooltip, even while disabled.</summary>
    private void LabelledField(
        FontAwesomeIcon icon,
        string id,
        float current,
        string format,
        PendingField.Range range,
        string tooltip,
        Action<float> apply
    )
    {
        ImGui.AlignTextToFramePadding();
        IconButton.Glyph(icon);
        Tooltip.OnHover(tooltip);
        ImGui.SameLine();
        fields.Draw(id, current, format, Layout.FieldWidth, range, apply);
        Tooltip.OnHover(tooltip);
    }
}
