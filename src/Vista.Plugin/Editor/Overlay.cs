using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Dalamud.Bindings.ImGui;

namespace CinematicCam.Plugin.Editor;

/// <summary>Draws the track's path, and a wireframe camera per point with its number on the point.</summary>
internal sealed class Overlay
{
    public const float MarkerRadius = 20f;
    private const float LabelScale = 2f;
    private const float PathSpacing = 0.25f;
    private const float PathThickness = 3f;
    private const float GlyphDepth = 1f;
    private const float GlyphThickness = 1.5f;
    private const float SelectedGlyphThickness = 2.5f;

    private IReadOnlyList<ControlPoint>? sampledPoints;
    private IReadOnlyList<Vector3> samples = [];
    private Track? evaluatedTrack;
    private TrackEvaluator? evaluator;

    /// <summary>Draws <paramref name="track"/> and returns each number's absolute screen position, null when off screen.</summary>
    public IReadOnlyList<Vector2?> Draw(EditorView view, Track track, int? selected)
    {
        var list = ImGui.GetBackgroundDrawList();
        DrawPath(list, view, track);

        var aspect = view.Size.Y > 0f ? view.Size.X / view.Size.Y : 1f;
        var labels = new Vector2?[track.Points.Count];
        for (var i = 0; i < track.Points.Count; i++)
        {
            var (forward, roll, fov) = Pose(track, i);
            var up = CameraOrientation.UpFor(Vector3.Zero, forward, roll);
            var glyph = CameraGlyph.Build(track.Points[i].Position, forward, up, fov, aspect, GlyphDepth);
            DrawGlyph(list, view, glyph, i == selected);
            labels[i] = view.ToScreen(track.Points[i].Position);
        }

        DrawLabels(list, labels, selected);
        return labels;
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

    /// <summary>Point <paramref name="index"/>'s aim, roll and FoV: recorded, or along the path in Direction-of-travel mode.</summary>
    private (Vector3 Forward, float Roll, float Fov) Pose(Track track, int index)
    {
        var point = track.Points[index];
        if (track.Aim == AimMode.AimKeys)
            return (FreeCamMotion.LookAtFrom(Vector3.Zero, point.Yaw, point.Pitch), point.Roll, point.Fov);

        if (!ReferenceEquals(evaluatedTrack, track))
        {
            evaluator = new TrackEvaluator(track);
            evaluatedTrack = track;
        }

        return evaluator!.Evaluate(TrackEditing.PointSeconds(track, index)) is { } frame
            ? (frame.LookAt - frame.Position, frame.Roll, point.Fov)
            : (FreeCamMotion.LookAtFrom(Vector3.Zero, point.Yaw, point.Pitch), point.Roll, point.Fov);
    }

    private static void DrawGlyph(ImDrawListPtr list, EditorView view, CameraGlyph glyph, bool selected)
    {
        var colour = selected ? EditorColours.Selected : EditorColours.AimLine;
        var thickness = selected ? SelectedGlyphThickness : GlyphThickness;
        var corners = glyph.Corners;
        for (var i = 0; i < corners.Length; i++)
        {
            DrawEdge(list, view, glyph.Apex, corners[i], colour, thickness);
            DrawEdge(list, view, corners[i], corners[(i + 1) % corners.Length], colour, thickness);
        }

        if (view.ToScreenBeyondNear(glyph.TabLeft) is { } left && view.ToScreenBeyondNear(glyph.TabTip) is { } tip
            && view.ToScreenBeyondNear(glyph.TabRight) is { } right)
            list.AddTriangleFilled(left, tip, right, EditorColours.UpLine);
    }

    private static void DrawEdge(ImDrawListPtr list, EditorView view, Vector3 from, Vector3 to, uint colour, float thickness)
    {
        if (ScreenProjection.ProjectSegment(from, to, view.ViewProjection, view.Size, view.Near) is { } s)
            list.AddLine(view.Origin + s.Start, view.Origin + s.End, colour, thickness);
    }

    private static void DrawLabels(ImDrawListPtr list, Vector2?[] labels, int? selected)
    {
        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] is not { } at) continue;

            var ring = i == selected ? EditorColours.Selected : EditorColours.MarkerRing;
            list.AddCircleFilled(at, MarkerRadius, EditorColours.Marker);
            list.AddCircle(at, MarkerRadius, ring, 0, i == selected ? 3f : 1.5f);

            var label = (i + 1).ToString();
            var size = ImGui.CalcTextSize(label) * LabelScale;
            list.AddText(ImGui.GetFont(), ImGui.GetFontSize() * LabelScale, at - (size / 2f), EditorColours.MarkerText, label);
        }
    }
}
