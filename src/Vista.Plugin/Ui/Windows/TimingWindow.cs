using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Vista.Core.Display;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Timing;
using Vista.Plugin.Editor;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The timing graph: distance along the path against time, with its keys, legs and playhead.</summary>
internal sealed class TimingWindow : Window
{
    private const float LeftInset = 28f;
    private const float TopInset = 8f;
    private const float AxisStrip = 22f;
    private const float SampleStep = 2f;
    private const float CurveThickness = 2f;
    private const float PointKeyRadius = 9f;
    private const float HoldEndRadius = 5f;
    private const float KeyHitRadius = 10f;
    private const float CurveHitDistance = 8f;
    private const float TickLength = 4f;
    private const float TickLabelGap = 2f;
    private const float HandleLength = 40f;
    private const float HandleRadius = 5f;
    private const float HandleHitRadius = 8f;
    private const float HoverRadius = 3f;
    private const float EasingWidth = 130f;
    private const string EmptyText = "Add two points to shape timing.";
    private const string KeyPopup = "##timing-key";

    private static readonly KeySide[] Sides = [KeySide.In, KeySide.Out];
    private const float ZoomPerNotch = 1.25f;
    private const int TimeTicks = 16;
    private const int YalmTicks = 6;

    private readonly GameSession game;
    private readonly SessionState session;
    private readonly List<Vector2?> keyScreens = [];
    private readonly List<(KeySide Side, Vector2 End)> handleEnds = [];
    private bool scrubbing;
    private Drag? drag;
    private int? popupKey;
    private TimingView? view;
    private Guid viewTrack;
    private (float StartX, TimingView Start)? pan;

    public TimingWindow(GameSession game)
        : base("Timing###vista-timing")
    {
        this.game = game;
        session = game.State;
        RespectCloseHotkey = false;
        // The wheel zooms the graph; it must never scroll the window as well.
        Flags |= ImGuiWindowFlags.NoScrollWithMouse;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360f, 200f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    /// <summary>Ends a scrub or a drag, since a closed window never reports the mouse letting go.</summary>
    public override void OnClose()
    {
        EndScrub();
        EndDrag();
        pan = null;
    }

    public override void Draw()
    {
        using var popups = PopupStyle.Push();
        DrawTopRow();

        var region = Vector2.Max(ImGui.GetContentRegionAvail(), Vector2.One);
        var topLeft = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(
            "##timing-graph",
            region,
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight
        );

        var list = ImGui.GetWindowDrawList();
        if (session.Track.Points.Count < 2)
        {
            EndScrub();
            EndDrag();
            pan = null;
            keyScreens.Clear();
            handleEnds.Clear();
            list.AddText(
                topLeft + ((region - ImGui.CalcTextSize(EmptyText)) / 2f),
                ImGui.GetColorU32(ImGuiCol.Text),
                EmptyText
            );
            DrawKeyPopup();
            return;
        }

        var distance = session.World.Evaluator.TotalDistance;
        var duration = (float)session.Duration;
        var shown = UpdateView(duration);
        var (distanceFrom, distanceTo) = view is null ? (0f, distance) : shown.Distances(session.World.Evaluator);
        var yalmStep = Ticks.Step(distanceTo - distanceFrom, YalmTicks);
        // Sized for any label up to the whole path, so zooming never shifts the plot sideways.
        var rightInset = ImGui.CalcTextSize($"{distance:0.00} y").X + 6f;

        var plotTopLeft = topLeft + new Vector2(LeftInset, TopInset);
        var plotSize = Vector2.Max(region - new Vector2(LeftInset + rightInset, TopInset + AxisStrip), Vector2.One);
        var graph = new TimingGraph(plotTopLeft, plotSize, duration, distance)
        {
            TimeFrom = shown.From,
            TimeTo = shown.To,
            DistanceFrom = distanceFrom,
            DistanceTo = distanceTo,
        };
        var stripBottom = topLeft.Y + region.Y;

        UpdateKeyScreens(graph);
        UpdateHandleEnds(graph);
        DrawPlot(list, graph);
        DrawYalmScale(list, graph, yalmStep);
        DrawTimeAxis(list, graph, stripBottom, rightInset);
        DrawCurve(list, graph, graph.TimeFrom, graph.TimeTo, EditorColours.Path);
        if (SelectedLegSpan() is var (start, end))
            DrawCurve(list, graph, start, end, EditorColours.Selected);
        DrawPlayhead(list, graph, stripBottom);
        DrawHandles(list);
        DrawKeys(list);

        HandleMouse(graph, stripBottom);
        DrawHoverReadout(list, graph);
        DrawKeyPopup();
    }

    private bool Editing => session.Mode == CameraMode.Editing;

    /// <summary>The selected leg's easing drop-down, or the selected key's time and its buttons.</summary>
    private void DrawTopRow()
    {
        ImGui.AlignTextToFramePadding();
        if (SelectedLegIndex() is { } leg)
            DrawLegControls(leg);
        else if (SelectedKeyIndex() is { } key)
            DrawKeyControls(key);
        else
            ImGui.TextUnformatted(string.Empty);

        ImGui.SameLine();
        var fitLeft = ImGui.GetWindowContentRegionMax().X - IconButton.Width(FontAwesomeIcon.Expand);
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), fitLeft));
        ImGui.BeginDisabled(view is null);
        if (IconButton.Draw("fit", FontAwesomeIcon.Expand, "Show the whole track"))
            view = null;
        ImGui.EndDisabled();
    }

    /// <summary>The leg's name and a drop-down of easing presets, showing Custom when it matches none.</summary>
    private void DrawLegControls(int leg)
    {
        ImGui.TextUnformatted($"Leg {leg} → {leg + 1}");
        ImGui.SameLine();

        var current = LegEasing.Read(session.Track, leg);
        ImGui.BeginDisabled(!Editing);
        ImGui.SetNextItemWidth(EasingWidth);
        if (ImGui.BeginCombo("##easing", EasingName(current)))
        {
            foreach (var preset in LegEasing.Presets)
            {
                if (!ImGui.Selectable(EasingName(preset), preset == current) || preset == current)
                    continue;
                Report(session.SetEasing(leg, preset));
            }

            ImGui.EndCombo();
        }

        ImGui.EndDisabled();
    }

    /// <summary>The key's time, the Smooth, Linear and Flat buttons, and a trash icon for a hold end.</summary>
    private void DrawKeyControls(int key)
    {
        ImGui.TextUnformatted($"Key: {session.World.Evaluator.Keys[key].Time:0.00} s");

        ImGui.BeginDisabled(!Editing);
        ImGui.SameLine();
        if (IconButton.Draw("key-smooth", FontAwesomeIcon.BezierCurve, "Smooth"))
            Report(session.SetKeyMode(key, TangentMode.Auto));
        ImGui.SameLine();
        if (IconButton.Draw("key-linear", FontAwesomeIcon.Slash, "Linear"))
            Report(session.SetKeyMode(key, TangentMode.Linear));
        ImGui.SameLine();
        if (IconButton.Draw("key-flat", FontAwesomeIcon.GripLines, "Flat"))
            Report(session.SetKeyMode(key, TangentMode.Flat));
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!Editing || !TimingEditing.ActionsFor(session.Track, key).RemoveHold);
        if (IconButton.Draw("key-delete", FontAwesomeIcon.Trash, "Remove hold", danger: true))
            Report(session.RemoveHold(key));
        ImGui.EndDisabled();
    }

    /// <summary>Works out where each key sits on screen this frame.</summary>
    private void UpdateKeyScreens(TimingGraph graph)
    {
        keyScreens.Clear();
        foreach (var key in session.World.Evaluator.Keys)
            keyScreens.Add(
                graph.ShowsTime(key.Time)
                    ? graph.ToScreen(key.Time, session.World.Evaluator.DistanceOf(key.Position))
                    : null
            );
    }

    /// <summary>Works out where the selected key's handles end this frame, while editing.</summary>
    private void UpdateHandleEnds(TimingGraph graph)
    {
        handleEnds.Clear();
        if (!Editing || SelectedKeyIndex() is not { } key || keyScreens[key] is not { } at)
            return;
        foreach (var side in Sides)
        {
            if (!TimingEditing.HasHandle(session.Track, key, side))
                continue;
            handleEnds.Add(
                (side, graph.HandleEnd(at, side, session.World.Evaluator.SideSlope(key, side), HandleLength))
            );
        }
    }

    /// <summary>The plot's background, and each point's line and number.</summary>
    private void DrawPlot(ImDrawListPtr list, TimingGraph graph)
    {
        var right = graph.Origin.X + graph.Size.X;
        list.AddRectFilled(graph.Origin, graph.Origin + graph.Size, EditorColours.GraphBackground);

        var text = ImGui.GetColorU32(ImGuiCol.Text);
        for (var i = 0; i < session.Track.Points.Count; i++)
        {
            var along = session.World.Evaluator.DistanceOf(i);
            if (!graph.ShowsDistance(along))
                continue;
            var y = graph.ToScreen(0f, along).Y;
            list.AddLine(new Vector2(graph.Origin.X, y), new Vector2(right, y), EditorColours.GraphGrid);

            var label = (i + 1).ToString(CultureInfo.CurrentCulture);
            var size = ImGui.CalcTextSize(label);
            list.AddText(new Vector2(graph.Origin.X - 4f - size.X, y - (size.Y / 2f)), text, label);
        }
    }

    /// <summary>Yalm ticks up the plot's right edge at a round step over the view, at most six, leaving out zero.</summary>
    private static void DrawYalmScale(ImDrawListPtr list, TimingGraph graph, float step)
    {
        var text = FullAlpha(EditorColours.GraphGrid);
        var right = graph.Origin.X + graph.Size.X;
        foreach (var d in graph.TickDistances(step))
        {
            var y = graph.ToScreen(0f, d).Y;
            list.AddLine(new Vector2(right, y), new Vector2(right + TickLength, y), EditorColours.GraphGrid);

            var label = YalmLabel(d);
            list.AddText(
                new Vector2(right + TickLength + TickLabelGap, y - (ImGui.CalcTextSize(label).Y / 2f)),
                text,
                label
            );
        }
    }

    /// <summary>Second ticks along the bottom strip, and the shot's length at its right end.</summary>
    private void DrawTimeAxis(ImDrawListPtr list, TimingGraph graph, float stripBottom, float rightInset)
    {
        var text = ImGui.GetColorU32(ImGuiCol.Text);
        var bottom = graph.Origin.Y + graph.Size.Y;
        var total = $"{session.Duration:0.0} s";
        var totalSize = ImGui.CalcTextSize(total);
        var totalLeft = graph.Origin.X + graph.Size.X + rightInset - totalSize.X;
        var labelY = stripBottom - totalSize.Y;
        list.AddText(new Vector2(totalLeft, labelY), text, total);

        var step = Ticks.Step(graph.TimeTo - graph.TimeFrom, TimeTicks);
        var format = Ticks.Format(step);
        var lastRight = float.MinValue;
        foreach (var t in graph.TickTimes(step))
        {
            var x = graph.ToScreen(t, 0f).X;
            list.AddLine(new Vector2(x, bottom), new Vector2(x, bottom + TickLength), EditorColours.GraphGrid);

            var label = t.ToString(format, CultureInfo.CurrentCulture);
            var width = ImGui.CalcTextSize(label).X;
            var left = x - (width / 2f);
            if (left < lastRight + 4f || left + width > totalLeft - 4f)
                continue;
            list.AddText(new Vector2(left, labelY), text, label);
            lastRight = left + width;
        }
    }

    /// <summary>The curve from <paramref name="from"/> to <paramref name="to"/> seconds, sampled every few pixels.</summary>
    private void DrawCurve(ImDrawListPtr list, TimingGraph graph, float from, float to, uint colour)
    {
        from = MathF.Max(from, graph.TimeFrom);
        to = MathF.Min(to, graph.TimeTo);
        if (from >= to)
            return;
        var left = graph.ToScreen(from, 0f).X;
        var right = graph.ToScreen(to, 0f).X;
        for (var x = left; x < right; x += SampleStep)
            list.PathLineTo(CurvePoint(graph, graph.TimeAt(x)));
        list.PathLineTo(CurvePoint(graph, to));
        list.PathStroke(colour, ImDrawFlags.None, CurveThickness);
    }

    /// <summary>A vertical line at the scrub head, across the plot and the strip; none while Live plays another track.</summary>
    private void DrawPlayhead(ImDrawListPtr list, TimingGraph graph, float stripBottom)
    {
        if (!session.Transport.HeadOnEditedTrack)
            return;
        if (!graph.ShowsTime((float)session.Transport.ScrubHead))
            return;
        var x = graph.ToScreen((float)session.Transport.ScrubHead, 0f).X;
        list.AddLine(new Vector2(x, graph.Origin.Y), new Vector2(x, stripBottom), EditorColours.Playhead);
    }

    /// <summary>A line from the selected key to each handle, with a dot at its end.</summary>
    private void DrawHandles(ImDrawListPtr list)
    {
        if (SelectedKeyIndex() is not { } key || keyScreens[key] is not { } at)
            return;
        foreach (var (_, end) in handleEnds)
        {
            list.AddLine(at, end, EditorColours.UpLine);
            list.AddCircleFilled(end, HandleRadius, EditorColours.UpLine);
        }
    }

    /// <summary>Point keys as numbered dots and hold ends as plain dots; a selected hold end is filled.</summary>
    private void DrawKeys(ImDrawListPtr list)
    {
        var track = session.Track;
        var selected = SelectedKeyIndex();
        for (var i = 0; i < keyScreens.Count; i++)
        {
            if (keyScreens[i] is not { } at)
                continue;
            var ring = i == selected ? EditorColours.Selected : EditorColours.MarkerRing;
            var thickness = i == selected ? 2.5f : 1.5f;
            var fill = i == selected ? EditorColours.Selected : EditorColours.Marker;

            if (TrackEditing.RoleOf(track, i) == KeyRole.Point)
            {
                list.AddCircleFilled(at, PointKeyRadius, EditorColours.Marker);
                list.AddCircle(at, PointKeyRadius, ring, 0, thickness);
                var label = (TrackEditing.PointOf(track, i) + 1).ToString(CultureInfo.CurrentCulture);
                list.AddText(at - (ImGui.CalcTextSize(label) / 2f), EditorColours.MarkerText, label);
            }
            else
            {
                list.AddCircleFilled(at, HoldEndRadius, fill);
                list.AddCircle(at, HoldEndRadius, ring, 0, thickness);
            }
        }
    }

    /// <summary>A marker and tooltip on the curve under the mouse, while nothing is being dragged or scrubbed.</summary>
    private void DrawHoverReadout(ImDrawListPtr list, TimingGraph graph)
    {
        if (drag is not null || scrubbing || !ImGui.IsItemHovered())
            return;
        var mouse = ImGui.GetMousePos();
        if (!graph.Contains(mouse))
            return;

        var t = graph.TimeAt(mouse.X);
        var d = session.World.Evaluator.DistanceAt(t);
        var speed = session.World.Evaluator.SlopeAt(t);
        list.AddCircleFilled(graph.ToScreen(t, d), HoverRadius, EditorColours.Playhead);
        ImGui.SetTooltip($"{t:0.00} s  ·  {d:0.0} y  ·  {speed:0.00} y/s");
    }

    /// <summary>Carries on or ends a scrub, a drag or a pan, zooms on the wheel, acts on a right press over a key, then on a left press: a handle, then a key, then the curve, then empty plot space to pan, then the time axis.</summary>
    private void HandleMouse(TimingGraph graph, float stripBottom)
    {
        if (scrubbing)
        {
            if (ImGui.IsItemActive())
                session.Transport.ScrubTo(graph.TimeAt(ImGui.GetMousePos().X));
            else
                EndScrub();
        }

        ContinueDrag();

        var mouse = ImGui.GetMousePos();
        ContinuePan(graph, mouse);
        Zoom(graph, mouse);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            RightClickKey(mouse);
        if (!ImGui.IsItemClicked(ImGuiMouseButton.Left))
            return;
        if (ClickHandle(graph, mouse) || ClickKey(graph, mouse) || ClickCurve(graph, mouse))
            return;
        if (view is { } zoomed && graph.Contains(mouse))
        {
            pan = (mouse.X, zoomed);
            return;
        }
        ClickTimeAxis(graph, mouse, stripBottom);
    }

    /// <summary>Starts dragging the selected key's handle under the cursor, while editing.</summary>
    private bool ClickHandle(TimingGraph graph, Vector2 mouse)
    {
        if (SelectedKeyIndex() is not { } key || keyScreens[key] is not { } at)
            return false;

        if (
            MarkerHitTest.Nearest(handleEnds.Select(h => (Vector2?)h.End).ToList(), mouse, HandleHitRadius)
            is not { } hit
        )
            return false;
        BeginDrag(new Drag(key, handleEnds[hit].Side, at, graph));
        return true;
    }

    /// <summary>Selects the key under the cursor and, while editing, starts dragging it.</summary>
    private bool ClickKey(TimingGraph graph, Vector2 mouse)
    {
        if (MarkerHitTest.Nearest(keyScreens, mouse, KeyHitRadius) is not { } key)
            return false;
        session.Selection.SelectKey(key);
        if (keyScreens[key] is { } at)
            BeginDrag(new Drag(key, null, at, graph, ImGui.GetIO().KeyCtrl));
        return true;
    }

    /// <summary>Selects the leg under the cursor when it is near the curve.</summary>
    private bool ClickCurve(TimingGraph graph, Vector2 mouse)
    {
        if (!graph.Contains(mouse))
            return false;

        var time = graph.TimeAt(mouse.X);
        if (MathF.Abs(CurvePoint(graph, time).Y - mouse.Y) > CurveHitDistance)
            return false;
        if (session.World.Evaluator.LegAt(time) is not { } leg)
            return false;
        session.Selection.SelectLeg(leg);
        return true;
    }

    /// <summary>Selects the key under the cursor and opens its menu, while editing.</summary>
    private void RightClickKey(Vector2 mouse)
    {
        if (!Editing || drag is not null || MarkerHitTest.Nearest(keyScreens, mouse, KeyHitRadius) is not { } key)
            return;
        session.Selection.SelectKey(key);

        if (!TimingEditing.ActionsFor(session.Track, key).Any)
            return;
        popupKey = key;
        ImGui.OpenPopup(KeyPopup);
    }

    /// <summary>The key menu: remove hold, and break or unify handles.</summary>
    private void DrawKeyPopup()
    {
        if (!ImGui.BeginPopup(KeyPopup))
            return;
        if (
            !Editing
            || popupKey is not { } key
            || key != session.Selection.Key
            || key >= TrackEditing.KeyCount(session.Track)
        )
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        var actions = TimingEditing.ActionsFor(session.Track, key);
        if (actions.RemoveHold && ImGui.MenuItem("Remove hold"))
            Report(session.RemoveHold(key));
        if (actions.BreakHandles && ImGui.MenuItem("Break handles"))
            Report(session.BreakHandles(key));
        if (actions.UnifyHandles && ImGui.MenuItem("Unify handles"))
            Report(session.UnifyHandles(key, actions.UnifyFrom));
        ImGui.EndPopup();
    }

    /// <summary>Remembers a pressed key or handle, while editing; the live edit waits for the mouse to move.</summary>
    private void BeginDrag(Drag started)
    {
        if (!Editing)
            return;
        EndDrag();
        drag = started;
    }

    /// <summary>Starts the live edit once the mouse has moved, then previews until the button is let go or a preview is refused.</summary>
    private void ContinueDrag()
    {
        if (drag is not { } d)
            return;
        if (!ImGui.IsItemActive() || !Editing)
        {
            EndDrag();
            return;
        }

        if (!d.Moved)
        {
            if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                return;
            d.Moved = true;
            session.BeginLiveEdit();
        }

        if (d.Refused)
            return;

        var mouse = ImGui.GetMousePos();
        var refusal = d.Side is { } side
            ? session.PreviewHandle(d.Key, side, d.Graph.SlopeFromHandle(d.KeyScreen, side, mouse))
            : session.PreviewKeyMove(d.Key, DragTime(d.Graph, mouse.X), d.Ripple);
        d.Refused = refusal is not null;
    }

    private void EndDrag()
    {
        if (drag is not { } d)
            return;
        drag = null;
        if (d.Moved)
            session.EndLiveEdit();
    }

    /// <summary>Starts a scrub when the cursor is in the bottom strip.</summary>
    private void ClickTimeAxis(TimingGraph graph, Vector2 mouse, float stripBottom)
    {
        if (mouse.Y < graph.Origin.Y + graph.Size.Y || mouse.Y > stripBottom)
            return;
        session.Transport.BeginScrub();
        session.Transport.ScrubTo(graph.TimeAt(mouse.X));
        scrubbing = session.Transport.Scrubbing;
    }

    /// <summary>The view for this frame: whole for a new track or one that no longer needs zooming, else kept within the shot.</summary>
    private TimingView UpdateView(float duration)
    {
        if (session.EditedTrackId != viewTrack)
        {
            view = null;
            pan = null;
            viewTrack = session.EditedTrackId;
        }
        if (view is { } v)
            view = v.Clamp(duration).UnlessWhole(duration);

        return view ?? TimingView.Whole(duration);
    }

    /// <summary>The wheel zooms time around the cursor, while nothing is being dragged.</summary>
    private void Zoom(TimingGraph graph, Vector2 mouse)
    {
        var wheel = ImGui.GetIO().MouseWheel;
        if (wheel == 0f || !ImGui.IsItemHovered() || drag is not null || scrubbing || pan is not null)
            return;
        var from = view ?? TimingView.Whole(graph.Duration);
        var next = from.Zoom(graph.TimeAt(mouse.X), MathF.Pow(ZoomPerNotch, -wheel), graph.Duration);
        view = next.UnlessWhole(graph.Duration);
    }

    /// <summary>Slides the view with the mouse while the pan is held, from where it was grabbed.</summary>
    private void ContinuePan(TimingGraph graph, Vector2 mouse)
    {
        if (pan is not { } p)
            return;
        if (!ImGui.IsItemActive())
        {
            pan = null;
            return;
        }
        view = p.Start.Drag(p.StartX - mouse.X, graph.Size.X, graph.Duration);
    }

    private void EndScrub()
    {
        if (!scrubbing)
            return;
        scrubbing = false;
        game.FinishScrub();
    }

    private Vector2 CurvePoint(TimingGraph graph, float time) =>
        graph.ToScreen(time, session.World.Evaluator.DistanceAt(time));

    /// <summary>The time under pixel column <paramref name="x"/>, running on past the shot's end so the last key can lengthen it.</summary>
    private static float DragTime(TimingGraph graph, float x) => graph.TimeAtOpenEnded(x);

    private static string YalmLabel(float distance) => $"{distance:0.##} y";

    /// <summary>An ImGui colour with its alpha forced to full.</summary>
    private static uint FullAlpha(uint colour) => (colour & 0x00FFFFFFu) | 0xFF000000u;

    private static string EasingName(Easing easing) =>
        easing switch
        {
            Easing.Smooth => "Smooth",
            Easing.Linear => "Linear",
            Easing.EaseIn => "Ease in",
            Easing.EaseOut => "Ease out",
            Easing.EaseInOut => "Ease in-out",
            _ => "Custom",
        };

    /// <summary>The selected key's index, or null when none is selected or it is out of range.</summary>
    private int? SelectedKeyIndex() =>
        session.Selection.Key is { } key && key < TrackEditing.KeyCount(session.Track) ? key : null;

    /// <summary>The selected leg, or null when none is selected or it is out of range.</summary>
    private int? SelectedLegIndex() =>
        session.Selection.Leg is { } leg && leg >= 1 && leg < session.Track.Points.Count ? leg : null;

    /// <summary>The selected leg's start and end times, or null.</summary>
    private (float Start, float End)? SelectedLegSpan() =>
        SelectedLegIndex() is { } leg ? session.World.Evaluator.LegSpan(leg) : null;

    /// <summary>A key or handle being dragged, with the graph and key position from the frame it began.</summary>
    private sealed class Drag(int key, KeySide? side, Vector2 keyScreen, TimingGraph graph, bool ripple = false)
    {
        public int Key { get; } = key;

        /// <summary>Whether Ctrl was held when the drag began; sampling it per frame would change the edit mid-drag.</summary>
        public bool Ripple { get; } = ripple;

        public KeySide? Side { get; } = side;

        public Vector2 KeyScreen { get; } = keyScreen;

        public TimingGraph Graph { get; } = graph;

        public bool Moved { get; set; }

        public bool Refused { get; set; }
    }
}
