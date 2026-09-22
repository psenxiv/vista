using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Editor;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace CinematicCam.Plugin.Ui;

/// <summary>The timing graph: distance along the path against time, with its keys, legs and playhead.</summary>
internal sealed class TimingWindow : Window
{
    private const float LeftInset = 28f;
    private const float TopInset = 8f;
    private const float RightInset = 8f;
    private const float AxisStrip = 22f;
    private const float SampleStep = 2f;
    private const float CurveThickness = 2f;
    private const float PointKeyRadius = 9f;
    private const float HoldEndRadius = 5f;
    private const float InnerKeyHalfSize = 6f;
    private const float KeyHitRadius = 10f;
    private const float CurveHitDistance = 8f;
    private const float TickLength = 4f;
    private const float HandleLength = 40f;
    private const float HandleRadius = 5f;
    private const float HandleHitRadius = 8f;
    private const float EasingWidth = 130f;
    private const string EmptyText = "Add two points to shape timing.";
    private const string KeyPopup = "##timing-key";

    private static readonly Easing[] Presets = [Easing.Smooth, Easing.Linear, Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut];
    private static readonly KeySide[] Sides = [KeySide.In, KeySide.Out];

    private readonly CameraSession session;
    private readonly List<Vector2?> keyScreens = [];
    private readonly List<(KeySide Side, Vector2 End)> handleEnds = [];
    private bool scrubbing;
    private Drag? drag;
    private int? popupKey;

    public TimingWindow(CameraSession session)
        : base("Timing###ccam-timing")
    {
        this.session = session;
        RespectCloseHotkey = false;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360f, 200f), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }

    /// <summary>Ends a scrub or a drag, since a closed window never reports the mouse letting go.</summary>
    public override void OnClose()
    {
        EndScrub();
        EndDrag();
    }

    public override void Draw()
    {
        DrawTopRow();

        var region = Vector2.Max(ImGui.GetContentRegionAvail(), Vector2.One);
        var topLeft = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##timing-graph", region, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);

        var list = ImGui.GetWindowDrawList();
        if (session.Track.Points.Count < 2)
        {
            EndScrub();
            EndDrag();
            keyScreens.Clear();
            handleEnds.Clear();
            list.AddText(topLeft + ((region - ImGui.CalcTextSize(EmptyText)) / 2f), ImGui.GetColorU32(ImGuiCol.Text), EmptyText);
            DrawKeyPopup();
            return;
        }

        var plotTopLeft = topLeft + new Vector2(LeftInset, TopInset);
        var plotSize = Vector2.Max(region - new Vector2(LeftInset + RightInset, TopInset + AxisStrip), Vector2.One);
        var graph = new TimingGraph(plotTopLeft, plotSize, (float)session.Duration, session.Evaluator.TotalDistance);
        var stripBottom = topLeft.Y + region.Y;

        UpdateKeyScreens(graph);
        UpdateHandleEnds(graph);
        DrawPlot(list, graph);
        DrawTimeAxis(list, graph, stripBottom);
        DrawCurve(list, graph, 0f, graph.Duration, EditorColours.Path);
        if (SelectedLegSpan() is var (start, end)) DrawCurve(list, graph, start, end, EditorColours.Selected);
        DrawPlayhead(list, graph, stripBottom);
        DrawHandles(list);
        DrawKeys(list);

        HandleMouse(graph, stripBottom);
        DrawKeyPopup();
    }

    private bool Editing => session.Mode == CameraMode.Editing;

    /// <summary>The selected leg's easing drop-down, or the selected key's time and its buttons.</summary>
    private void DrawTopRow()
    {
        ImGui.AlignTextToFramePadding();
        if (SelectedLegIndex() is { } leg) DrawLegControls(leg);
        else if (SelectedKeyIndex() is { } key) DrawKeyControls(key);
        else ImGui.TextUnformatted(string.Empty);
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
            foreach (var preset in Presets)
            {
                if (!ImGui.Selectable(EasingName(preset), preset == current) || preset == current) continue;
                Report(session.SetEasing(leg, preset));
            }

            ImGui.EndCombo();
        }

        ImGui.EndDisabled();
    }

    /// <summary>The key's time, the Smooth, Linear and Flat buttons, and a trash icon for an inner key or hold end.</summary>
    private void DrawKeyControls(int key)
    {
        var role = TrackEditing.RoleOf(session.Track, key);
        ImGui.TextUnformatted($"Key: {session.Track.Timing[key].Time:0.00} s");

        ImGui.BeginDisabled(!Editing);
        ImGui.SameLine();
        if (IconButton.Draw("key-smooth", FontAwesomeIcon.BezierCurve, "Smooth")) Report(session.SetKeyMode(key, TangentMode.Auto));
        ImGui.SameLine();
        if (IconButton.Draw("key-linear", FontAwesomeIcon.Slash, "Linear")) Report(session.SetKeyMode(key, TangentMode.Linear));
        ImGui.SameLine();
        if (IconButton.Draw("key-flat", FontAwesomeIcon.GripLines, "Flat")) Report(session.SetKeyMode(key, TangentMode.Flat));
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!Editing || role == KeyRole.Point);
        if (IconButton.Draw("key-delete", FontAwesomeIcon.Trash, role == KeyRole.HoldEnd ? "Remove hold" : "Delete key")) Report(session.DeleteKey(key));
        ImGui.EndDisabled();
    }

    /// <summary>Works out where each key sits on screen this frame.</summary>
    private void UpdateKeyScreens(TimingGraph graph)
    {
        keyScreens.Clear();
        foreach (var key in session.Track.Timing)
            keyScreens.Add(graph.ToScreen(key.Time, session.Evaluator.DistanceOf(key.Position)));
    }

    /// <summary>Works out where the selected key's handles end this frame, while editing.</summary>
    private void UpdateHandleEnds(TimingGraph graph)
    {
        handleEnds.Clear();
        if (!Editing || SelectedKeyIndex() is not { } key || keyScreens[key] is not { } at) return;
        foreach (var side in Sides)
        {
            if (!TimingEditing.HasHandle(session.Track, key, side)) continue;
            handleEnds.Add((side, graph.HandleEnd(at, side, session.Evaluator.SideSlope(key, side), HandleLength)));
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
            var y = graph.ToScreen(0f, session.Evaluator.DistanceOf(i)).Y;
            list.AddLine(new Vector2(graph.Origin.X, y), new Vector2(right, y), EditorColours.GraphGrid);

            var label = (i + 1).ToString();
            var size = ImGui.CalcTextSize(label);
            list.AddText(new Vector2(graph.Origin.X - 4f - size.X, y - (size.Y / 2f)), text, label);
        }
    }

    /// <summary>Second ticks along the bottom strip, and the shot's length at its right end.</summary>
    private void DrawTimeAxis(ImDrawListPtr list, TimingGraph graph, float stripBottom)
    {
        var text = ImGui.GetColorU32(ImGuiCol.Text);
        var bottom = graph.Origin.Y + graph.Size.Y;
        var total = $"{session.Duration:0.0} s";
        var totalSize = ImGui.CalcTextSize(total);
        var totalLeft = graph.Origin.X + graph.Size.X + RightInset - totalSize.X;
        var labelY = stripBottom - totalSize.Y;
        list.AddText(new Vector2(totalLeft, labelY), text, total);

        var step = graph.Duration > 30f ? 5 : 1;
        var lastRight = float.MinValue;
        for (var t = 0; t <= graph.Duration; t += step)
        {
            var x = graph.ToScreen(t, 0f).X;
            list.AddLine(new Vector2(x, bottom), new Vector2(x, bottom + TickLength), EditorColours.GraphGrid);

            var label = $"{t:0}";
            var width = ImGui.CalcTextSize(label).X;
            var left = x - (width / 2f);
            if (left < lastRight + 4f || left + width > totalLeft - 4f) continue;
            list.AddText(new Vector2(left, labelY), text, label);
            lastRight = left + width;
        }
    }

    /// <summary>The curve from <paramref name="from"/> to <paramref name="to"/> seconds, sampled every few pixels.</summary>
    private void DrawCurve(ImDrawListPtr list, TimingGraph graph, float from, float to, uint colour)
    {
        var left = graph.ToScreen(from, 0f).X;
        var right = graph.ToScreen(to, 0f).X;
        for (var x = left; x < right; x += SampleStep) list.PathLineTo(CurvePoint(graph, graph.TimeAt(x)));
        list.PathLineTo(CurvePoint(graph, to));
        list.PathStroke(colour, ImDrawFlags.None, CurveThickness);
    }

    /// <summary>A vertical line at the scrub head, across the plot and the strip.</summary>
    private void DrawPlayhead(ImDrawListPtr list, TimingGraph graph, float stripBottom)
    {
        var x = graph.ToScreen((float)session.ScrubHead, 0f).X;
        list.AddLine(new Vector2(x, graph.Origin.Y), new Vector2(x, stripBottom), EditorColours.Playhead);
    }

    /// <summary>A line from the selected key to each handle, with a dot at its end.</summary>
    private void DrawHandles(ImDrawListPtr list)
    {
        if (SelectedKeyIndex() is not { } key || keyScreens[key] is not { } at) return;
        foreach (var (_, end) in handleEnds)
        {
            list.AddLine(at, end, EditorColours.UpLine);
            list.AddCircleFilled(end, HandleRadius, EditorColours.UpLine);
        }
    }

    /// <summary>Point keys as numbered dots, hold ends as plain dots and inner keys as diamonds; a selected hold end or inner key is filled.</summary>
    private void DrawKeys(ImDrawListPtr list)
    {
        var track = session.Track;
        var selected = SelectedKeyIndex();
        for (var i = 0; i < keyScreens.Count; i++)
        {
            if (keyScreens[i] is not { } at) continue;
            var ring = i == selected ? EditorColours.Selected : EditorColours.MarkerRing;
            var thickness = i == selected ? 2.5f : 1.5f;
            var fill = i == selected ? EditorColours.Selected : EditorColours.Marker;

            switch (TrackEditing.RoleOf(track, i))
            {
                case KeyRole.Point:
                    list.AddCircleFilled(at, PointKeyRadius, EditorColours.Marker);
                    list.AddCircle(at, PointKeyRadius, ring, 0, thickness);
                    var label = ((int)track.Timing[i].Position + 1).ToString();
                    list.AddText(at - (ImGui.CalcTextSize(label) / 2f), EditorColours.MarkerText, label);
                    break;
                case KeyRole.HoldEnd:
                    list.AddCircleFilled(at, HoldEndRadius, fill);
                    list.AddCircle(at, HoldEndRadius, ring, 0, thickness);
                    break;
                default:
                    var up = at with { Y = at.Y - InnerKeyHalfSize };
                    var rightCorner = at with { X = at.X + InnerKeyHalfSize };
                    var down = at with { Y = at.Y + InnerKeyHalfSize };
                    var leftCorner = at with { X = at.X - InnerKeyHalfSize };
                    list.AddQuadFilled(up, rightCorner, down, leftCorner, fill);
                    list.AddQuad(up, rightCorner, down, leftCorner, ring, thickness);
                    break;
            }
        }
    }

    /// <summary>Carries on or ends a scrub or a drag, acts on a right press over a key, then on a left press: a handle, then a key, then the curve, then the time axis.</summary>
    private void HandleMouse(TimingGraph graph, float stripBottom)
    {
        if (scrubbing)
        {
            if (ImGui.IsItemActive()) session.ScrubTo(graph.TimeAt(ImGui.GetMousePos().X));
            else EndScrub();
        }

        ContinueDrag();

        var mouse = ImGui.GetMousePos();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) RightClickKey(mouse);
        if (!ImGui.IsItemClicked(ImGuiMouseButton.Left)) return;
        if (ClickHandle(graph, mouse) || ClickKey(graph, mouse) || ClickCurve(graph, mouse)) return;
        ClickTimeAxis(graph, mouse, stripBottom);
    }

    /// <summary>Starts dragging the selected key's handle under the cursor, while editing.</summary>
    private bool ClickHandle(TimingGraph graph, Vector2 mouse)
    {
        if (SelectedKeyIndex() is not { } key || keyScreens[key] is not { } at) return false;

        (KeySide Side, float Distance)? best = null;
        foreach (var (side, end) in handleEnds)
        {
            var distance = Vector2.Distance(end, mouse);
            if (distance <= HandleHitRadius && (best is null || distance < best.Value.Distance)) best = (side, distance);
        }

        if (best is not { } hit) return false;
        BeginDrag(new Drag(key, hit.Side, at, graph));
        return true;
    }

    /// <summary>Selects the key under the cursor and, while editing, starts dragging it.</summary>
    private bool ClickKey(TimingGraph graph, Vector2 mouse)
    {
        if (MarkerHitTest.Nearest(keyScreens, mouse, KeyHitRadius) is not { } key) return false;
        session.SelectKey(key);
        if (keyScreens[key] is { } at) BeginDrag(new Drag(key, null, at, graph));
        return true;
    }

    /// <summary>Adds an inner key on a double-click near the curve while editing; otherwise selects the leg under the cursor.</summary>
    private bool ClickCurve(TimingGraph graph, Vector2 mouse)
    {
        var inside = mouse.X >= graph.Origin.X && mouse.X <= graph.Origin.X + graph.Size.X
            && mouse.Y >= graph.Origin.Y && mouse.Y <= graph.Origin.Y + graph.Size.Y;
        if (!inside) return false;

        var time = graph.TimeAt(mouse.X);
        if (MathF.Abs(CurvePoint(graph, time).Y - mouse.Y) > CurveHitDistance) return false;
        if (Editing && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            Report(session.AddInnerKey(time));
            return true;
        }

        if (TrackEditing.LegAt(session.Track, time) is not { } leg) return false;
        session.SelectLeg(leg);
        return true;
    }

    /// <summary>Selects the key under the cursor and opens its menu, while editing.</summary>
    private void RightClickKey(Vector2 mouse)
    {
        if (!Editing || drag is not null || MarkerHitTest.Nearest(keyScreens, mouse, KeyHitRadius) is not { } key) return;
        session.SelectKey(key);

        var track = session.Track;
        var hasHandle = TimingEditing.HasHandle(track, key, KeySide.In) || TimingEditing.HasHandle(track, key, KeySide.Out);
        if (TrackEditing.RoleOf(track, key) == KeyRole.Point && !track.Timing[key].Broken && !hasHandle) return;
        popupKey = key;
        ImGui.OpenPopup(KeyPopup);
    }

    /// <summary>The key menu: delete or remove hold, and break or unify handles.</summary>
    private void DrawKeyPopup()
    {
        if (!ImGui.BeginPopup(KeyPopup)) return;
        if (!Editing || popupKey is not { } key || key != session.SelectedKey || key >= session.Track.Timing.Count)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        var track = session.Track;
        var role = TrackEditing.RoleOf(track, key);
        var broken = track.Timing[key].Broken;
        var hasIn = TimingEditing.HasHandle(track, key, KeySide.In);
        var hasOut = TimingEditing.HasHandle(track, key, KeySide.Out);

        if (role == KeyRole.Inner && ImGui.MenuItem("Delete key")) Report(session.DeleteKey(key));
        if (role == KeyRole.HoldEnd && ImGui.MenuItem("Remove hold")) Report(session.DeleteKey(key));
        if (!broken && (hasIn || hasOut) && ImGui.MenuItem("Break handles")) Report(session.BreakHandles(key));
        if (broken && ImGui.MenuItem("Unify handles")) Report(session.UnifyHandles(key, hasOut ? KeySide.Out : KeySide.In));
        ImGui.EndPopup();
    }

    /// <summary>Remembers a pressed key or handle, while editing; the live edit waits for the mouse to move.</summary>
    private void BeginDrag(Drag started)
    {
        if (!Editing) return;
        EndDrag();
        drag = started;
    }

    /// <summary>Starts the live edit once the mouse has moved, then previews until the button is let go or a preview is refused.</summary>
    private void ContinueDrag()
    {
        if (drag is not { } d) return;
        if (!ImGui.IsItemActive() || !Editing)
        {
            EndDrag();
            return;
        }

        if (!d.Moved)
        {
            if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left)) return;
            d.Moved = true;
            session.BeginLiveEdit();
        }

        if (d.Refused) return;

        var mouse = ImGui.GetMousePos();
        var refusal = d.Side is { } side
            ? session.PreviewHandle(d.Key, side, d.Graph.SlopeFromHandle(d.KeyScreen, side, mouse))
            : session.PreviewKeyMove(d.Key, DragTime(d.Graph, mouse.X), d.Graph.DistanceAt(mouse.Y));
        d.Refused = refusal is not null;
    }

    private void EndDrag()
    {
        if (drag is not { } d) return;
        drag = null;
        if (d.Moved) session.EndLiveEdit();
    }

    /// <summary>Starts a scrub when the cursor is in the bottom strip.</summary>
    private void ClickTimeAxis(TimingGraph graph, Vector2 mouse, float stripBottom)
    {
        if (mouse.Y < graph.Origin.Y + graph.Size.Y || mouse.Y > stripBottom) return;
        session.BeginScrub();
        session.ScrubTo(graph.TimeAt(mouse.X));
        scrubbing = session.Scrubbing;
    }

    private void EndScrub()
    {
        if (!scrubbing) return;
        scrubbing = false;
        session.EndScrub();
    }

    private Vector2 CurvePoint(TimingGraph graph, float time) => graph.ToScreen(time, session.Evaluator.DistanceAt(time));

    /// <summary>The time under pixel column <paramref name="x"/>, running on past the shot's end so the last key can lengthen it.</summary>
    private static float DragTime(TimingGraph graph, float x)
        => x > graph.Origin.X + graph.Size.X ? (x - graph.Origin.X) / graph.Size.X * graph.Duration : graph.TimeAt(x);

    private static string EasingName(Easing easing) => easing switch
    {
        Easing.Smooth => "Smooth",
        Easing.Linear => "Linear",
        Easing.EaseIn => "Ease in",
        Easing.EaseOut => "Ease out",
        Easing.EaseInOut => "Ease in-out",
        _ => "Custom",
    };

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }

    /// <summary>The selected key's index, or null when none is selected or it is out of range.</summary>
    private int? SelectedKeyIndex()
        => session.SelectedKey is { } key && key < session.Track.Timing.Count ? key : null;

    /// <summary>The selected leg, or null when none is selected or it is out of range.</summary>
    private int? SelectedLegIndex()
        => session.SelectedLeg is { } leg && leg >= 1 && leg < session.Track.Points.Count ? leg : null;

    /// <summary>The selected leg's start and end times, or null.</summary>
    private (float Start, float End)? SelectedLegSpan()
    {
        if (SelectedLegIndex() is not { } leg) return null;
        var track = session.Track;
        return (track.Timing[TrackEditing.LegStartKey(track, leg)].Time, track.Timing[TrackEditing.LegEndKey(track, leg)].Time);
    }

    /// <summary>A key or handle being dragged, with the graph and key position from the frame it began.</summary>
    private sealed class Drag(int key, KeySide? side, Vector2 keyScreen, TimingGraph graph)
    {
        public int Key { get; } = key;

        public KeySide? Side { get; } = side;

        public Vector2 KeyScreen { get; } = keyScreen;

        public TimingGraph Graph { get; } = graph;

        public bool Moved { get; set; }

        public bool Refused { get; set; }
    }
}
