using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Dalamud.Bindings.ImGui;

namespace CinematicCam.Plugin.Editor;

/// <summary>Draws the track's path, numbered markers and aim arrows over the game.</summary>
internal sealed class Overlay
{
    public const float MarkerRadius = 20f;
    private const float LabelScale = 2f;
    private const float PathSpacing = 0.25f;
    private const float AimLength = 1.5f;
    private const float ArrowHeadLength = 14f;
    private const float ArrowHeadHalfWidth = 7f;

    private IReadOnlyList<ControlPoint>? sampledPoints;
    private IReadOnlyList<Vector3> samples = [];

    /// <summary>Draws <paramref name="track"/> and returns each marker's absolute screen position, null when off screen.</summary>
    public IReadOnlyList<Vector2?> Draw(EditorView view, Track track, int? selected)
    {
        var list = ImGui.GetBackgroundDrawList();
        DrawPath(list, view, track);
        if (track.Aim == AimMode.AimKeys) DrawAimArrows(list, view, track);
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
                list.AddLine(view.Origin + s.Start, view.Origin + s.End, EditorColours.Path, 2f);
        }
    }

    private static void DrawAimArrows(ImDrawListPtr list, EditorView view, Track track)
    {
        foreach (var point in track.Points)
        {
            var tip = point.Position + (Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, point.Yaw, point.Pitch)) * AimLength);
            if (ScreenProjection.ProjectSegment(point.Position, tip, view.ViewProjection, view.Size, view.Near) is not { } s) continue;

            var start = view.Origin + s.Start;
            var end = view.Origin + s.End;
            list.AddLine(start, end, EditorColours.AimLine, 2f);
            if (view.ToScreen(tip) is not null) DrawArrowHead(list, start, end);
        }
    }

    private static void DrawArrowHead(ImDrawListPtr list, Vector2 start, Vector2 end)
    {
        var shaft = end - start;
        if (shaft.LengthSquared() < 1f) return;

        var along = Vector2.Normalize(shaft);
        var across = new Vector2(-along.Y, along.X) * ArrowHeadHalfWidth;
        var back = end - (along * ArrowHeadLength);
        list.AddTriangleFilled(end, back + across, back - across, EditorColours.AimLine);
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
