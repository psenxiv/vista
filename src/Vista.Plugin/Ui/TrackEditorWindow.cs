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

/// <summary>The main Vista window: modes, the Hierarchy, track settings, the point list, the scrub bar and the Playlist.</summary>
internal sealed unsafe class TrackEditorWindow : Window
{
    private const string PointPayload = "VISTA_POINT";

    private static readonly string[] ModeNames = ["View", "Edit", "Live"];
    private static readonly string[] AimNames = ["Recorded aim", "Direction of travel", "Look At", "Follow Target"];
    private static readonly AimMode[] AimModes = [AimMode.AimKeys, AimMode.PathTangent, AimMode.LookAt, AimMode.FollowTarget];
    private static readonly string[] DirectionNames = ["Forward", "Reverse", "Ping-pong"];
    private static readonly PlaybackDirection[] Directions = [PlaybackDirection.Forward, PlaybackDirection.Reverse, PlaybackDirection.PingPong];
    private static readonly FontAwesomeIcon[] DirectionIcons = [FontAwesomeIcon.ArrowRight, FontAwesomeIcon.ArrowLeft, FontAwesomeIcon.ArrowsAltH];
    private static readonly Vector2 Spacing = new(8f, 7f);
    private static readonly Vector2 CellPadding = new(6f, 4f);
    private const float SpeedWidth = 90f;
    private const float ModeWidth = 80f;
    private const float FieldWidth = 70f;
    private const float MaxCharacterWidth = 220f;
    private const string NotFound = " (Not found)";
    private const float SmallFieldWidth = 50f;
    private const float MinWidth = 420f;
    private const float MinHeight = 260f;

    private readonly CameraSession session;
    private readonly PendingField fields;
    private readonly TimingWindow timing;
    private readonly HierarchyPanel hierarchy;
    private readonly PlaylistPanel playlist;
    private CameraMode lastMode;
    private bool scrubbing;
    private bool showHierarchy = true;
    private bool showPlaylist = true;
    private float pendingWidth;
    private float? smoothingDrag;

    public TrackEditorWindow(CameraSession session, PendingField fields, TimingWindow timing)
        : base("Vista###vista-track-editor")
    {
        this.session = session;
        this.fields = fields;
        this.timing = timing;
        hierarchy = new HierarchyPanel(session);
        playlist = new PlaylistPanel(session);
        RespectCloseHotkey = false;
        SizeCondition = ImGuiCond.FirstUseEver;
        SetMinimumWidth(MinWidth);
    }

    /// <summary>Widens the minimum size to fit the top bar, the track row and any open compartment, and opens at that width on first use.</summary>
    public override void PreDraw()
    {
        var width = MathF.Max(MathF.Max(MinWidth, TrackRowWidth(session.Track.Aim == AimMode.FollowTarget ? CharacterWidth(CharacterLabel()) : null)) + CompartmentsWidth(), TopRowWidth());
        SetMinimumWidth(width);
        Size = new Vector2(width, MinHeight);
    }

    /// <summary>The width the open compartments beside the track editor take, with their gap.</summary>
    private float CompartmentsWidth()
        => (showHierarchy ? HierarchyPanel.Width + Spacing.X : 0f) + (showPlaylist ? PlaylistPanel.Width + Spacing.X : 0f);

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
        using var selection = ImRaii.PushColor(ImGuiCol.Header, UiColours.AccentAt(0.45f))
            .Push(ImGuiCol.HeaderHovered, UiColours.AccentAt(0.30f))
            .Push(ImGuiCol.HeaderActive, UiColours.AccentAt(0.55f));
        var editing = session.Mode == CameraMode.Editing;
        DrawTopRow(editing);

        if (showHierarchy)
        {
            if (ImGui.BeginChild("hierarchy", new Vector2(HierarchyPanel.Width, 0f), true)) hierarchy.Draw(editing);
            ImGui.EndChild();
            ImGui.SameLine();
        }

        var editorWidth = showPlaylist ? -(PlaylistPanel.Width + Spacing.X) : 0f;
        // The same inner padding as the bordered compartments, so the rows and separators line up.
        if (ImGui.BeginChild("track-editor", new Vector2(editorWidth, 0f), false, ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            ImGui.BeginDisabled(!editing);
            DrawTrackRow();
            ImGui.EndDisabled();
            ImGui.Separator();

            DrawPoints(editing);
            ImGui.Separator();
            DrawScrubRow();
        }

        ImGui.EndChild();

        if (showPlaylist)
        {
            ImGui.SameLine();
            if (ImGui.BeginChild("playlist", new Vector2(PlaylistPanel.Width, 0f), true)) playlist.Draw(editing);
            ImGui.EndChild();
        }

        // Showing or hiding a compartment grows or shrinks the window by its width, so the track editor keeps its size.
        if (pendingWidth != 0f)
        {
            ImGui.SetWindowSize(ImGui.GetWindowSize() + new Vector2(pendingWidth, 0f));
            pendingWidth = 0f;
        }
    }

    private void DrawTopRow(bool editing)
    {
        if (IconButton.Toggle("hierarchy", FontAwesomeIcon.Sitemap, showHierarchy, showHierarchy ? "Hide hierarchy" : "Show hierarchy"))
        {
            showHierarchy = !showHierarchy;
            pendingWidth += showHierarchy ? HierarchyPanel.Width + Spacing.X : -(HierarchyPanel.Width + Spacing.X);
        }

        ImGui.SameLine();
        if (IconButton.Toggle("playlist", FontAwesomeIcon.ListOl, showPlaylist, showPlaylist ? "Hide playlist" : "Show playlist"))
        {
            showPlaylist = !showPlaylist;
            pendingWidth += showPlaylist ? PlaylistPanel.Width + Spacing.X : -(PlaylistPanel.Width + Spacing.X);
        }

        ImGui.SameLine();
        DrawModeCombo();

        var gap = ImGui.GetStyle().ItemSpacing.X * 3f;
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

        if (editing)
        {
            ImGui.SameLine(0f, gap);
            DrawFlySpeed();
        }

        var live = session.Mode == CameraMode.Live ? ImGui.CalcTextSize("LIVE").X + ImGui.GetStyle().ItemSpacing.X : 0f;
        ImGui.SameLine();
        RightAlign(live + IconButton.Width(FontAwesomeIcon.EyeSlash));
        if (session.Mode == CameraMode.Live)
        {
            DrawLive();
            ImGui.SameLine();
        }

        if (IconButton.Toggle("hide-ui", FontAwesomeIcon.EyeSlash, session.HideUiInLive, "Hide game UI when Live"))
            session.HideUiInLive = !session.HideUiInLive;
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

    /// <summary>The free-cam's speed: a feather icon and a short slider showing the multiplier.</summary>
    private void DrawFlySpeed()
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextUnformatted(FontAwesomeIcon.Feather.ToIconString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fly speed");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(SpeedWidth);
        var speed = session.Speed;
        var step = speed.Index;
        if (ImGui.SliderInt("##speed", ref step, 0, FlySpeed.Steps.Count - 1, $"{speed.Multiplier:0.##}x")) speed.Set(step);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fly speed");
    }

    /// <summary>Play/Pause and Restart, left of the scrub bar on the same line.</summary>
    private void DrawTransport()
    {
        var playing = session.Previewing || (session.Mode == CameraMode.Live && !session.Director.IsPaused && !session.Director.IsFinished);
        ImGui.BeginDisabled(session.Mode == CameraMode.Editing ? session.Track.Points.Count == 0 : !session.CanGoLive);
        if (IconButton.Draw("play-pause", playing ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, playing ? "Pause" : "Play"))
        {
            fields.Commit();
            if (playing) session.Stop();
            else session.Play();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode == CameraMode.View || (session.Mode == CameraMode.Editing ? session.Track.Points.Count == 0 : !session.CanGoLive));
        if (IconButton.Draw("restart", FontAwesomeIcon.StepBackward, "Restart")) { fields.Commit(); session.Restart(); }
        ImGui.EndDisabled();
        ImGui.SameLine();
    }

    /// <summary>View, Edit and Live; View releases the camera and Live cues the playlist paused at its first entry's start.</summary>
    private void DrawModeCombo()
    {
        var current = session.Mode switch { CameraMode.Editing => 1, CameraMode.Live => 2, _ => 0 };
        ImGui.SetNextItemWidth(ModeWidth);
        if (!ImGui.BeginCombo("##mode", ModeNames[current])) return;

        if (ImGui.Selectable(ModeNames[0], current == 0) && current != 0) { fields.Commit(); session.Release("window"); }
        if (ImGui.Selectable(ModeNames[1], current == 1) && current != 1) { fields.Commit(); session.Edit(); }
        ImGui.BeginDisabled(!session.CanGoLive);
        if (ImGui.Selectable(ModeNames[2], current == 2) && current != 2) { fields.Commit(); session.Cue(); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !session.CanGoLive) ImGui.SetTooltip("Add a track with points to the playlist");
        ImGui.EndDisabled();
        ImGui.EndCombo();
    }

    /// <summary>Aim and direction icons with their menus, the character, aim height and smoothing under Follow Target, the loop toggle, the Speed and Duration fields, the add button and its menu, and Clear track at the right end.</summary>
    private void DrawTrackRow()
    {
        var aim = Array.IndexOf(AimModes, session.Track.Aim);
        if (IconButton.Draw("aim", FontAwesomeIcon.Crosshairs, $"Select aim ({AimNames[aim]})")) ImGui.OpenPopup("aim-menu");
        if (ImGui.BeginPopup("aim-menu"))
        {
            for (var i = 0; i < AimNames.Length; i++)
            {
                if (!ImGui.Selectable(AimNames[i], i == aim) || i == aim) continue;
                Report(session.SetAim(AimModes[i]));
            }

            ImGui.EndPopup();
        }

        if (session.Track.Aim == AimMode.FollowTarget)
        {
            ImGui.SameLine();
            DrawCharacter();
            ImGui.SameLine();
            DrawAimHeight();
            ImGui.SameLine();
            DrawSmoothing();
        }

        var direction = Array.IndexOf(Directions, session.Track.Direction);
        ImGui.SameLine();
        if (IconButton.Draw("direction", DirectionIcons[direction], $"Select direction ({DirectionNames[direction]})")) ImGui.OpenPopup("direction-menu");
        if (ImGui.BeginPopup("direction-menu"))
        {
            for (var i = 0; i < DirectionNames.Length; i++)
            {
                if (!ImGui.Selectable(DirectionNames[i], i == direction) || i == direction) continue;
                var chosen = Directions[i];
                Report(session.ChangeTrack(t => TrackEditing.SetDirection(t, chosen)));
            }

            ImGui.EndPopup();
        }

        ImGui.SameLine();
        DrawLoop();

        ImGui.BeginDisabled(TrackEditing.AllPinned(session.Track));
        ImGui.SameLine();
        LabelledField(FontAwesomeIcon.TachometerAlt, "track-speed", session.Track.Speed, "%.2f", "Track speed", v => Report(session.SetTrackSpeed(v)));
        ImGui.SameLine();
        LabelledField(FontAwesomeIcon.Stopwatch, "track-duration", (float)session.Duration, "%.1f s", "Track duration", v => Report(session.SetTrackDuration(v)));
        ImGui.EndDisabled();

        ImGui.SameLine();
        DrawAddButton();

        ImGui.SameLine();
        RightAlign(IconButton.Width(FontAwesomeIcon.Trash));
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (IconButton.Draw("clear-track", FontAwesomeIcon.Trash, "Clear track", danger: true)) { fields.Clear(); Report(session.ChangeTrack(TrackEditing.Clear)); }
        ImGui.EndDisabled();
    }

    /// <summary>The followed character's button: its name, or red with a warning and "(Not found)" when not loaded; opens the nearby list.</summary>
    private void DrawCharacter()
    {
        var track = session.Track;
        var lost = session.TargetLost(track);
        if (lost)
        {
            IconButton.TargetNotFound();
            ImGui.SameLine(0f, 4f);
        }

        var label = CharacterLabel();
        bool pressed;
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Red, lost))
            pressed = ImGui.Button($"{label}##character", new Vector2(CharacterWidth(label), 0f));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(lost ? IconButton.NotFoundTooltip : "Choose a character");
        if (pressed) ImGui.OpenPopup("character-menu");
        DrawCharacterMenu(track.TargetName);
    }

    /// <summary>The character button's label, its name shortened with an ellipsis so the button fits the cap with " (Not found)" in full.</summary>
    private string CharacterLabel()
    {
        var track = session.Track;
        if (track.TargetName is not { } name) return "Choose a character";
        var suffix = session.TargetLost(track) ? NotFound : "";
        var room = MaxCharacterWidth - (ImGui.GetStyle().FramePadding.X * 2f) - ImGui.CalcTextSize(suffix).X;
        if (ImGui.CalcTextSize(name).X <= room) return name + suffix;
        var cut = name.Length;
        while (cut > 0 && ImGui.CalcTextSize(name[..cut] + "…").X > room) cut--;
        return name[..cut].TrimEnd() + "…" + suffix;
    }

    /// <summary>The character button's width: its label and frame padding.</summary>
    private static float CharacterWidth(string label) => ImGui.CalcTextSize(label).X + (ImGui.GetStyle().FramePadding.X * 2f);

    /// <summary>The characters loaded nearby, nearest the camera first, each with its distance; picking one follows it.</summary>
    private void DrawCharacterMenu(string? chosen)
    {
        if (!ImGui.BeginPopup("character-menu")) return;

        var origin = session.CameraPosition ?? session.PlayerPosition;
        var nearby = session.Characters.NearestTo(origin ?? Vector3.Zero);
        if (nearby.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
                ImGui.TextUnformatted("No characters nearby");
        }

        for (var i = 0; i < nearby.Count; i++)
        {
            var character = nearby[i];
            using var id = ImRaii.PushId($"character{i}");
            var text = origin is { } from ? $"{character.Name}  ({Vector3.Distance(character.Position, from):0.0} yalms)" : character.Name;
            if (ImGui.Selectable(text, character.Name == chosen))
                Report(session.SetTarget(character.Name));
        }

        ImGui.EndPopup();
    }

    /// <summary>The aim height above the character's feet, as a small field.</summary>
    private void DrawAimHeight()
    {
        fields.Draw("aim-height", session.Track.AimHeight, "%.1f", SmallFieldWidth, v => Report(session.SetAimHeight(v)));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Aim height above the character's feet, in yalms");
    }

    /// <summary>The smoothing slider; a drag is applied as one undo step when it lets go.</summary>
    private void DrawSmoothing()
    {
        var value = smoothingDrag ?? session.Track.Smoothing;
        ImGui.SetNextItemWidth(SmallFieldWidth);
        if (ImGui.SliderFloat("##smoothing", ref value, 0f, 1f, "%.2f")) smoothingDrag = value;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Smoothing: 0 exact, 1 heavy");
        if (ImGui.IsItemActive() || smoothingDrag is not { } done) return;
        smoothingDrag = null;
        Report(session.SetSmoothing(done));
    }

    /// <summary>The loop toggle: accent when the track loops, dimmed when it plays once.</summary>
    private void DrawLoop()
    {
        var loop = session.Track.Loop;
        if (IconButton.Toggle("loop", FontAwesomeIcon.Repeat, loop, loop ? "Play once" : "Loop"))
            Report(session.ChangeTrack(t => TrackEditing.SetLoop(t, !loop)));
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
            if (ImGui.BeginTable("point-table", 6, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("#");
                ImGui.TableSetupColumn("Duration (s)");
                ImGui.TableSetupColumn("Speed");
                ImGui.TableSetupColumn("Hold (s)");
                ImGui.TableSetupColumn("##pin", ImGuiTableColumnFlags.WidthFixed, IconButton.Width(FontAwesomeIcon.Thumbtack));
                ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                for (var i = 0; i < track.Points.Count; i++) DrawPointRow(track, evaluator, i, editing);
                ImGui.EndTable();
            }
        }

        ImGui.EndChild();
    }

    /// <summary>One point and the leg arriving at it: the whole row selects on click, jumps on double-click and drags to reorder; the trash icon, shown on hover, deletes it.</summary>
    /// <remarks><paramref name="track"/> and <paramref name="evaluator"/> are a snapshot taken once for the whole list: an earlier row's delete or reorder must not change what a later row reads.</remarks>
    private void DrawPointRow(Track track, TrackEvaluator evaluator, int index, bool editing)
    {
        ImGui.TableNextRow();
        ImGui.BeginDisabled(!editing);

        ImGui.TableNextColumn();
        // The row's full height, cell padding included, so neighbouring rows' hover areas meet.
        var top = ImGui.GetCursorScreenPos().Y - CellPadding.Y;
        var rowFlags = ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap;
        if (ImGui.Selectable($"##row{index}", session.Selected == index, rowFlags, new Vector2(0f, ImGui.GetFrameHeight()))) session.Select(index);
        var rowHovered = editing && IconButton.RowHovered(
            new Vector2(ImGui.GetItemRectMin().X, top), new Vector2(ImGui.GetItemRectMax().X, top + ImGui.GetFrameHeight() + (CellPadding.Y * 2f)));
        if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) session.JumpToPoint(index);
        if (editing && ImGui.BeginDragDropSource())
        {
            SetPayload(index);
            ImGui.TextUnformatted($"Point {index + 1}");
            ImGui.EndDragDropSource();
        }

        DropTarget(index, editing);

        ImGui.SameLine(0f, 0f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{index + 1}");

        ImGui.TableNextColumn();
        if (index > 0) fields.Draw($"leg{index}", evaluator.LegSeconds(index), "%.1f", FieldWidth, v => Report(session.SetLegDuration(index, v)));

        ImGui.TableNextColumn();
        if (index > 0)
            fields.Draw($"leg-speed{index}", evaluator.LegLength(index) / evaluator.LegSeconds(index), "%.2f", FieldWidth, v => Report(session.SetLegSpeed(index, v)));

        ImGui.TableNextColumn();
        fields.Draw($"hold{index}", TrackEditing.HoldSeconds(track, index), "%.1f", FieldWidth,
            v => Report(session.ChangeTrack(t => TrackEditing.SetHold(t, index, EditLimits.Hold(v)))));

        ImGui.TableNextColumn();
        if (index > 0) DrawPin(track, index, rowHovered);

        ImGui.TableNextColumn();
        RightAlign(IconButton.Width(FontAwesomeIcon.Trash));
        if (IconButton.RowAction($"delete{index}", FontAwesomeIcon.Trash, "Delete point", rowHovered, danger: true))
        {
            fields.Clear();
            Report(session.DeletePoint(index));
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
        ImGui.BeginDisabled(session.Mode == CameraMode.View || duration <= 0f);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var moved = ImGui.SliderFloat("##scrub", ref head, 0f, MathF.Max(duration, 0.001f), $"%.1f / {duration:0.0} s");
        if (ImGui.IsItemActivated()) { fields.Commit(); session.BeginScrub(); scrubbing = session.Scrubbing; }
        if (moved || ImGui.IsItemActivated()) session.ScrubTo(head);
        // A window that stops drawing mid-drag never reports the slider deactivating, so any idle frame ends the scrub too.
        if (ImGui.IsItemDeactivated() || !ImGui.IsItemActive()) EndScrub();
        ImGui.EndDisabled();
    }

    /// <summary>The track row's full width: its items, the Follow Target items when <paramref name="character"/> is given, the gaps between them, and the window's and the editor's padding.</summary>
    private static float TrackRowWidth(float? character)
    {
        var style = ImGui.GetStyle();
        var direction = DirectionIcons.Max(IconButton.Width);
        var items = IconButton.Width(FontAwesomeIcon.Crosshairs) + direction + IconButton.Width(FontAwesomeIcon.Repeat)
            + IconWidth(FontAwesomeIcon.TachometerAlt) + IconWidth(FontAwesomeIcon.Stopwatch) + (FieldWidth * 2f)
            + IconButton.Width(FontAwesomeIcon.Plus) + IconButton.Width(FontAwesomeIcon.CaretDown) + IconButton.Width(FontAwesomeIcon.Trash);
        var follow = character is { } width ? IconButton.WarningWidth() + 4f + width + (SmallFieldWidth * 2f) + (Spacing.X * 3f) : 0f;
        return items + follow + (Spacing.X * 8f) + (style.WindowPadding.X * 4f);
    }

    /// <summary>The top bar's full width: its items, the larger of LIVE and fly speed, the gaps between them, and the window padding.</summary>
    private static float TopRowWidth()
    {
        var style = ImGui.GetStyle();
        var items = IconButton.Width(FontAwesomeIcon.Sitemap) + IconButton.Width(FontAwesomeIcon.ListOl) + ModeWidth
            + IconButton.Width(FontAwesomeIcon.Undo) + IconButton.Width(FontAwesomeIcon.Redo) + IconButton.Width(FontAwesomeIcon.ChartLine)
            + IconButton.Width(FontAwesomeIcon.EyeSlash);
        var live = ImGui.CalcTextSize("LIVE").X + Spacing.X;
        var flySpeed = (Spacing.X * 3f) + IconWidth(FontAwesomeIcon.Feather) + Spacing.X + SpeedWidth;
        return items + MathF.Max(live, flySpeed) + (Spacing.X * 8f) + (style.WindowPadding.X * 2f);
    }

    /// <summary>The width of an icon drawn as text in the icon font.</summary>
    private static float IconWidth(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(icon.ToIconString()).X;
    }

    private void SetMinimumWidth(float width)
        => SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(width, MinHeight), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };

    /// <summary>An icon, then a number field; both show the tooltip, even while disabled.</summary>
    private void LabelledField(FontAwesomeIcon icon, string id, float current, string format, string tooltip, Action<float> apply)
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextUnformatted(icon.ToIconString());
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
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
