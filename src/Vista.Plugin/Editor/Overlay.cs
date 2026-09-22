using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Tracks;
using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Editor;

/// <summary>Draws a track's path, and a wireframe camera per point with its number on the point; tracks not being edited draw in grey.</summary>
internal sealed class Overlay
{
    public const float MarkerRadius = 20f;
    private const float LabelScale = 2f;
    private const float PathSpacing = 0.25f;
    private const float PathThickness = 3f;
    private const float GlyphDepth = 1f;
    private const float GlyphThickness = 1.5f;
    private const float SelectedGlyphThickness = 2.5f;

    private readonly Dictionary<Guid, TrackCache> caches = new();

    /// <summary>Draws <paramref name="track"/>, in grey unless <paramref name="edited"/>, and returns each number's absolute screen position, null when off screen.</summary>
    public IReadOnlyList<Vector2?> Draw(EditorView view, Track track, int? selected, bool edited)
    {
        if (!caches.TryGetValue(track.Id, out var cache)) caches[track.Id] = cache = new TrackCache();
        var palette = edited ? Palette.Edited : Palette.Other;
        var list = ImGui.GetBackgroundDrawList();
        DrawPath(list, view, track, cache, palette);

        var aspect = view.Size.Y > 0f ? view.Size.X / view.Size.Y : 1f;
        var labels = new Vector2?[track.Points.Count];
        for (var i = 0; i < track.Points.Count; i++)
        {
            var (forward, roll, fov) = Pose(track, cache, i);
            var up = CameraOrientation.UpFor(Vector3.Zero, forward, roll);
            var glyph = CameraGlyph.Build(track.Points[i].Position, forward, up, fov, aspect, GlyphDepth);
            DrawGlyph(list, view, glyph, i == selected, palette);
            labels[i] = view.ToScreen(track.Points[i].Position);
        }

        DrawLabels(list, labels, selected, palette);
        return labels;
    }

    /// <summary>Forgets cached paths of tracks not in <paramref name="ids"/>.</summary>
    public void Prune(IReadOnlySet<Guid> ids)
    {
        foreach (var id in caches.Keys.Where(id => !ids.Contains(id)).ToList()) caches.Remove(id);
    }

    private static void DrawPath(ImDrawListPtr list, EditorView view, Track track, TrackCache cache, Palette palette)
    {
        if (!ReferenceEquals(cache.SampledPoints, track.Points))
        {
            cache.Samples = TrackPath.Sample(track.Points.Select(p => p.Position).ToArray(), PathSpacing);
            cache.SampledPoints = track.Points;
        }

        for (var i = 1; i < cache.Samples.Count; i++)
        {
            if (ScreenProjection.ProjectSegment(cache.Samples[i - 1], cache.Samples[i], view.ViewProjection, view.Size, view.Near) is { } s)
                list.AddLine(view.Origin + s.Start, view.Origin + s.End, palette.Path, PathThickness);
        }
    }

    /// <summary>Point <paramref name="index"/>'s aim, roll and FoV: recorded, or along the path in Direction-of-travel mode.</summary>
    private static (Vector3 Forward, float Roll, float Fov) Pose(Track track, TrackCache cache, int index)
    {
        var point = track.Points[index];
        if (track.Aim == AimMode.AimKeys)
            return (FreeCamMotion.LookAtFrom(Vector3.Zero, point.Yaw, point.Pitch), point.Roll, point.Fov);

        if (!ReferenceEquals(cache.EvaluatedTrack, track))
        {
            cache.Evaluator = new TrackEvaluator(track);
            cache.EvaluatedTrack = track;
        }

        return cache.Evaluator!.Evaluate(cache.Evaluator.PointSeconds(index)) is { } frame
            ? (frame.LookAt - frame.Position, frame.Roll, point.Fov)
            : (FreeCamMotion.LookAtFrom(Vector3.Zero, point.Yaw, point.Pitch), point.Roll, point.Fov);
    }

    private static void DrawGlyph(ImDrawListPtr list, EditorView view, CameraGlyph glyph, bool selected, Palette palette)
    {
        var colour = selected ? EditorColours.Selected : palette.Glyph;
        var thickness = selected ? SelectedGlyphThickness : GlyphThickness;
        var corners = glyph.Corners;
        for (var i = 0; i < corners.Length; i++)
        {
            DrawEdge(list, view, glyph.Apex, corners[i], colour, thickness);
            DrawEdge(list, view, corners[i], corners[(i + 1) % corners.Length], colour, thickness);
        }

        if (view.ToScreenBeyondNear(glyph.TabLeft) is { } left && view.ToScreenBeyondNear(glyph.TabTip) is { } tip
            && view.ToScreenBeyondNear(glyph.TabRight) is { } right)
            list.AddTriangleFilled(left, tip, right, palette.UpLine);
    }

    private static void DrawEdge(ImDrawListPtr list, EditorView view, Vector3 from, Vector3 to, uint colour, float thickness)
    {
        if (ScreenProjection.ProjectSegment(from, to, view.ViewProjection, view.Size, view.Near) is { } s)
            list.AddLine(view.Origin + s.Start, view.Origin + s.End, colour, thickness);
    }

    private static void DrawLabels(ImDrawListPtr list, Vector2?[] labels, int? selected, Palette palette)
    {
        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] is not { } at) continue;

            var ring = i == selected ? EditorColours.Selected : palette.MarkerRing;
            list.AddCircleFilled(at, MarkerRadius, palette.Marker);
            list.AddCircle(at, MarkerRadius, ring, 0, i == selected ? 3f : 1.5f);

            var label = (i + 1).ToString();
            var size = ImGui.CalcTextSize(label) * LabelScale;
            list.AddText(ImGui.GetFont(), ImGui.GetFontSize() * LabelScale, at - (size / 2f), palette.MarkerText, label);
        }
    }

    /// <summary>A track's sampled path and evaluator, rebuilt when the track changes.</summary>
    private sealed class TrackCache
    {
        public IReadOnlyList<ControlPoint>? SampledPoints;
        public IReadOnlyList<Vector3> Samples = [];
        public Track? EvaluatedTrack;
        public TrackEvaluator? Evaluator;
    }

    /// <summary>The colours one track draws in.</summary>
    private readonly record struct Palette(uint Path, uint Glyph, uint UpLine, uint Marker, uint MarkerRing, uint MarkerText)
    {
        public static readonly Palette Edited = new(EditorColours.Path, EditorColours.AimLine, EditorColours.UpLine, EditorColours.Marker, EditorColours.MarkerRing, EditorColours.MarkerText);
        public static readonly Palette Other = new(EditorColours.OtherPath, EditorColours.OtherGlyph, EditorColours.OtherUpLine, EditorColours.OtherMarker, EditorColours.OtherMarkerRing, EditorColours.OtherMarkerText);
    }
}
