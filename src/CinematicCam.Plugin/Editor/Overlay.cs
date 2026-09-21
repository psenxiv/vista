using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Dalamud.Bindings.ImGui;

namespace CinematicCam.Plugin.Editor;

/// <summary>Draws the track's path, numbered markers, and aim and up arrows over the game.</summary>
internal sealed class Overlay
{
    public const float MarkerRadius = 20f;
    private const float LabelScale = 2f;
    private const float PathSpacing = 0.25f;
    private const float PathThickness = 3f;
    private const float AimLength = 1.5f;
    private const float UpLength = 0.75f;
    private const float ArrowHeadLength = 14f;
    private const float ArrowHeadHalfWidth = 7f;

    private IReadOnlyList<ControlPoint>? sampledPoints;
    private IReadOnlyList<Vector3> samples = [];
    private Track? evaluatedTrack;
    private TrackEvaluator? evaluator;

    /// <summary>Draws <paramref name="track"/> and returns each marker's absolute screen position, null when off screen.</summary>
    public IReadOnlyList<Vector2?> Draw(EditorView view, Track track, int? selected)
    {
        var list = ImGui.GetBackgroundDrawList();
        DrawPath(list, view, track);
        if (track.Aim == AimMode.AimKeys) DrawAimArrows(list, view, track);
        else DrawTravelUpArrows(list, view, track);
        return DrawMarkers(list, view, track, selected);
    }

    private void DrawPath(ImDrawListPtr list, EditorView view, Track track)
    {
        if (!ReferenceEquals(sampledPoints, track.Points))
        {
            samples = TrackPath.Sample(track.Points.Select(p => p.Position).ToArray(), PathSpacing);
            sampledPoints = track.Points;
        }

        for (var i = 1; i < samples.Count; i++)
        {
            if (ScreenProjection.ProjectSegment(samples[i - 1], samples[i], view.ViewProjection, view.Size, view.Near) is { } s)
                list.AddLine(view.Origin + s.Start, view.Origin + s.End, EditorColours.Path, PathThickness);
        }
    }

    private static void DrawAimArrows(ImDrawListPtr list, EditorView view, Track track)
    {
        foreach (var point in track.Points)
        {
            var forward = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, point.Yaw, point.Pitch));
            var up = Vector3.Normalize(CameraOrientation.UpFor(Vector3.Zero, forward, point.Roll));
            DrawArrow(list, view, point.Position, point.Position + (forward * AimLength), EditorColours.AimLine);
            DrawArrow(list, view, point.Position, point.Position + (up * UpLength), EditorColours.UpLine);
        }
    }

    /// <summary>Up arrows around the path's direction at each point, for Direction-of-travel mode.</summary>
    private void DrawTravelUpArrows(ImDrawListPtr list, EditorView view, Track track)
    {
        if (!ReferenceEquals(evaluatedTrack, track))
        {
            evaluator = new TrackEvaluator(track);
            evaluatedTrack = track;
        }

        for (var i = 0; i < track.Points.Count; i++)
        {
            if (evaluator!.Evaluate(TrackEditing.PointSeconds(track, i)) is not { } frame) continue;
            var forward = Vector3.Normalize(frame.LookAt - frame.Position);
            var up = Vector3.Normalize(CameraOrientation.UpFor(Vector3.Zero, forward, frame.Roll));
            var position = track.Points[i].Position;
            DrawArrow(list, view, position, position + (up * UpLength), EditorColours.UpLine);
        }
    }

    private static void DrawArrow(ImDrawListPtr list, EditorView view, Vector3 from, Vector3 tip, uint colour)
    {
        if (ScreenProjection.ProjectSegment(from, tip, view.ViewProjection, view.Size, view.Near) is not { } s) return;

        var start = view.Origin + s.Start;
        var end = view.Origin + s.End;
        list.AddLine(start, end, colour, 2f);
        if (view.ToScreen(tip) is not null) DrawArrowHead(list, start, end, colour);
    }

    private static void DrawArrowHead(ImDrawListPtr list, Vector2 start, Vector2 end, uint colour)
    {
        var shaft = end - start;
        if (shaft.LengthSquared() < 1f) return;

        var along = Vector2.Normalize(shaft);
        var across = new Vector2(-along.Y, along.X) * ArrowHeadHalfWidth;
        var back = end - (along * ArrowHeadLength);
        list.AddTriangleFilled(end, back + across, back - across, colour);
    }

    private static Vector2?[] DrawMarkers(ImDrawListPtr list, EditorView view, Track track, int? selected)
    {
        var screens = new Vector2?[track.Points.Count];
        for (var i = 0; i < track.Points.Count; i++)
        {
            if (view.ToScreen(track.Points[i].Position) is not { } at) continue;
            screens[i] = at;

            var ring = i == selected ? EditorColours.Selected : EditorColours.MarkerRing;
            list.AddCircleFilled(at, MarkerRadius, EditorColours.Marker);
            list.AddCircle(at, MarkerRadius, ring, 0, i == selected ? 3f : 1.5f);

            var label = (i + 1).ToString();
            var size = ImGui.CalcTextSize(label) * LabelScale;
            list.AddText(ImGui.GetFont(), ImGui.GetFontSize() * LabelScale, at - (size / 2f), EditorColours.MarkerText, label);
        }

        return screens;
    }
}
