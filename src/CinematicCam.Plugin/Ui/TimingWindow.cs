using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Editor;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGui;
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
    private const string EmptyText = "Add two points to shape timing.";

    private readonly CameraSession session;
    private readonly List<Vector2?> keyScreens = [];
    private bool scrubbing;

    public TimingWindow(CameraSession session)
        : base("Timing###ccam-timing")
    {
        this.session = session;
        RespectCloseHotkey = false;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360f, 200f), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }

    /// <summary>Ends a scrub, since a closed window never reports the mouse letting go.</summary>
    public override void OnClose() => EndScrub();

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
            keyScreens.Clear();
            list.AddText(topLeft + ((region - ImGui.CalcTextSize(EmptyText)) / 2f), ImGui.GetColorU32(ImGuiCol.Text), EmptyText);
            return;
        }

        var plotTopLeft = topLeft + new Vector2(LeftInset, TopInset);
        var plotSize = Vector2.Max(region - new Vector2(LeftInset + RightInset, TopInset + AxisStrip), Vector2.One);
        var graph = new TimingGraph(plotTopLeft, plotSize, (float)session.Duration, session.Evaluator.TotalDistance);
        var stripBottom = topLeft.Y + region.Y;

        UpdateKeyScreens(graph);
        DrawPlot(list, graph);
        DrawTimeAxis(list, graph, stripBottom);
        DrawCurve(list, graph, 0f, graph.Duration, EditorColours.Path);
        if (SelectedLegSpan() is var (start, end)) DrawCurve(list, graph, start, end, EditorColours.Selected);
        DrawPlayhead(list, graph, stripBottom);
        DrawKeys(list);

        HandleMouse(graph, stripBottom);
    }

    /// <summary>The selected key's time, read-only.</summary>
    private void DrawTopRow()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(SelectedKeyIndex() is { } key ? $"Key: {session.Track.Timing[key].Time:0.0} s" : string.Empty);
    }

    /// <summary>Works out where each key sits on screen this frame.</summary>
    private void UpdateKeyScreens(TimingGraph graph)
    {
        keyScreens.Clear();
        foreach (var key in session.Track.Timing)
            keyScreens.Add(graph.ToScreen(key.Time, session.Evaluator.DistanceOf(key.Position)));
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

    /// <summary>Point keys as numbered dots, hold ends as plain dots and inner keys as diamonds.</summary>
    private void DrawKeys(ImDrawListPtr list)
    {
        var track = session.Track;
        var selected = SelectedKeyIndex();
        for (var i = 0; i < keyScreens.Count; i++)
        {
            if (keyScreens[i] is not { } at) continue;
            var ring = i == selected ? EditorColours.Selected : EditorColours.MarkerRing;
            var thickness = i == selected ? 2.5f : 1.5f;

            switch (TrackEditing.RoleOf(track, i))
            {
                case KeyRole.Point:
                    list.AddCircleFilled(at, PointKeyRadius, EditorColours.Marker);
                    list.AddCircle(at, PointKeyRadius, ring, 0, thickness);
                    var label = ((int)track.Timing[i].Position + 1).ToString();
                    list.AddText(at - (ImGui.CalcTextSize(label) / 2f), EditorColours.MarkerText, label);
                    break;
                case KeyRole.HoldEnd:
                    list.AddCircleFilled(at, HoldEndRadius, EditorColours.Marker);
                    list.AddCircle(at, HoldEndRadius, ring, 0, thickness);
                    break;
                default:
                    var up = at with { Y = at.Y - InnerKeyHalfSize };
                    var rightCorner = at with { X = at.X + InnerKeyHalfSize };
                    var down = at with { Y = at.Y + InnerKeyHalfSize };
                    var leftCorner = at with { X = at.X - InnerKeyHalfSize };
                    list.AddQuadFilled(up, rightCorner, down, leftCorner, EditorColours.Marker);
                    list.AddQuad(up, rightCorner, down, leftCorner, ring, thickness);
                    break;
            }
        }
    }

    /// <summary>Carries on or ends a scrub, then acts on a left press: a handle, then a key, then the curve, then the time axis.</summary>
    private void HandleMouse(TimingGraph graph, float stripBottom)
    {
        if (scrubbing)
        {
            if (ImGui.IsItemActive()) session.ScrubTo(graph.TimeAt(ImGui.GetMousePos().X));
            else EndScrub();
        }

        if (!ImGui.IsItemClicked(ImGuiMouseButton.Left)) return;
        var mouse = ImGui.GetMousePos();
        if (ClickHandle(mouse) || ClickKey(mouse) || ClickCurve(graph, mouse)) return;
        ClickTimeAxis(graph, mouse, stripBottom);
    }

    /// <summary>Acts on a handle under the cursor. There are none yet.</summary>
    private static bool ClickHandle(Vector2 mouse) => false;

    /// <summary>Selects the key under the cursor.</summary>
    private bool ClickKey(Vector2 mouse)
    {
        if (MarkerHitTest.Nearest(keyScreens, mouse, KeyHitRadius) is not { } key) return false;
        session.SelectKey(key);
        return true;
    }

    /// <summary>Selects the leg under the cursor when it is close to the curve.</summary>
    private bool ClickCurve(TimingGraph graph, Vector2 mouse)
    {
        var inside = mouse.X >= graph.Origin.X && mouse.X <= graph.Origin.X + graph.Size.X
            && mouse.Y >= graph.Origin.Y && mouse.Y <= graph.Origin.Y + graph.Size.Y;
        if (!inside) return false;

        var time = graph.TimeAt(mouse.X);
        if (MathF.Abs(CurvePoint(graph, time).Y - mouse.Y) > CurveHitDistance) return false;
        if (TrackEditing.LegAt(session.Track, time) is not { } leg) return false;
        session.SelectLeg(leg);
        return true;
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

    /// <summary>The selected key's index, or null when none is selected or it is out of range.</summary>
    private int? SelectedKeyIndex()
        => session.SelectedKey is { } key && key < session.Track.Timing.Count ? key : null;

    /// <summary>The selected leg's start and end times, or null.</summary>
    private (float Start, float End)? SelectedLegSpan()
    {
        if (session.SelectedLeg is not { } leg || leg < 1 || leg >= session.Track.Points.Count) return null;
        var track = session.Track;
        return (track.Timing[TrackEditing.LegStartKey(track, leg)].Time, track.Timing[TrackEditing.LegEndKey(track, leg)].Time);
    }
}
