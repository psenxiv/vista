using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Vista.Core.Camera;
using Vista.Core.Display;
using Vista.Core.Tracks;

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
    private const float AnchorRadius = 0.5f;
    private const float AnchorArrow = 0.9f;
    private const float SceneAnchorRadius = 1f;
    private const float SceneAnchorArrow = 1.6f;
    private const int AnchorSegments = 24;
    private const float NameGap = 4f;
    private const float NameScale = 1.5f;
    private const float NameRounding = 3f;
    private static readonly Vector2 NamePadding = new(6f, 3f);
    private const float LookAtCross = 0.5f;
    private const float TargetCross = 0.25f;

    private readonly Dictionary<Guid, TrackCache> caches = new();

    /// <summary>Draws <paramref name="track"/>, in grey unless <paramref name="edited"/>, its path by turn rate when <paramref name="heat"/>, its <paramref name="selected"/> points highlighted and its glyphs facing <paramref name="aimPoint"/> when given, and returns each number's absolute screen position, null when off screen.</summary>
    public IReadOnlyList<Vector2?> Draw(
        EditorView view,
        Track track,
        IReadOnlyCollection<int> selected,
        bool edited,
        Vector3? aimPoint = null,
        bool heat = false
    )
    {
        if (!caches.TryGetValue(track.Id, out var cache))
            caches[track.Id] = cache = new TrackCache();
        var palette = edited ? Palette.Edited : Palette.Other;
        var list = ImGui.GetBackgroundDrawList();
        if (heat)
            DrawHeat(list, view, track, cache, aimPoint, palette);
        else
            DrawPath(list, view, track, cache, palette);

        var aspect = view.Size.Y > 0f ? view.Size.X / view.Size.Y : 1f;
        var labels = new Vector2?[track.Points.Count];
        for (var i = 0; i < track.Points.Count; i++)
        {
            var (forward, up, fov) = CameraGlyph.Pose(track, i, aimPoint, () => EvaluatorFor(track, cache));
            var glyph = CameraGlyph.Build(track.Points[i].Position, forward, up, fov, aspect, GlyphDepth);
            DrawGlyph(list, view, glyph, selected.Contains(i), palette);
            labels[i] = view.ToScreen(track.Points[i].Position);
        }

        DrawLabels(list, labels, selected, palette);
        return labels;
    }

    /// <summary>Forgets cached paths of tracks not in <paramref name="ids"/>.</summary>
    public void Prune(IReadOnlySet<Guid> ids)
    {
        foreach (var id in caches.Keys.Where(id => !ids.Contains(id)).ToList())
            caches.Remove(id);
    }

    /// <summary>A track anchor: a ground ring, an arrow along its yaw, a faint line to the first point and its name above. Returns its centre on screen, or null.</summary>
    public static Vector2? DrawTrackAnchor(
        EditorView view,
        Anchor world,
        Vector3? firstPoint,
        bool edited,
        bool selected,
        string? name
    )
    {
        var list = ImGui.GetBackgroundDrawList();
        var colour =
            selected ? EditorColours.Selected
            : edited ? EditorColours.Anchor
            : EditorColours.OtherAnchor;
        if (firstPoint is { } first)
            DrawEdge(
                list,
                view,
                world.Position,
                first,
                edited ? EditorColours.AnchorLink : EditorColours.OtherAnchorLink,
                GlyphThickness
            );

        for (var i = 0; i < AnchorSegments; i++)
        {
            var a = MathF.Tau * i / AnchorSegments;
            var b = MathF.Tau * (i + 1) / AnchorSegments;
            DrawEdge(
                list,
                view,
                world.Position + Ring(a, AnchorRadius),
                world.Position + Ring(b, AnchorRadius),
                colour,
                selected ? SelectedGlyphThickness : GlyphThickness
            );
        }

        DrawArrow(list, view, world, AnchorArrow, colour, selected ? SelectedGlyphThickness : GlyphThickness);
        if (name is not null)
            DrawName(list, view, world.Position, name);
        return view.ToScreen(world.Position);
    }

    /// <summary>The scene anchor: a ground diamond and an arrow along its yaw. Returns its centre on screen, or null.</summary>
    public static Vector2? DrawSceneAnchor(EditorView view, Anchor world, bool selected)
    {
        var list = ImGui.GetBackgroundDrawList();
        var colour = selected ? EditorColours.Selected : EditorColours.SceneAnchor;
        var thickness = selected ? SelectedGlyphThickness : PathThickness;
        for (var i = 0; i < 4; i++)
        {
            var a = world.Yaw + (MathF.PI / 2f * i);
            var b = world.Yaw + (MathF.PI / 2f * (i + 1));
            DrawEdge(
                list,
                view,
                world.Position + Ring(a, SceneAnchorRadius),
                world.Position + Ring(b, SceneAnchorRadius),
                colour,
                thickness
            );
        }

        DrawArrow(list, view, world, SceneAnchorArrow, colour, thickness);
        return view.ToScreen(world.Position);
    }

    /// <summary>A Look At point: a crosshair in the anchor colour and a faint line to the first point. Returns its centre on screen, or null.</summary>
    public static Vector2? DrawLookAt(EditorView view, Vector3 world, Vector3? firstPoint, bool edited, bool selected)
    {
        var list = ImGui.GetBackgroundDrawList();
        var colour =
            selected ? EditorColours.Selected
            : edited ? EditorColours.Anchor
            : EditorColours.OtherAnchor;
        if (firstPoint is { } first)
            DrawEdge(
                list,
                view,
                world,
                first,
                edited ? EditorColours.AnchorLink : EditorColours.OtherAnchorLink,
                GlyphThickness
            );
        DrawCross(list, view, world, LookAtCross, colour, selected ? SelectedGlyphThickness : GlyphThickness);
        return view.ToScreen(world);
    }

    /// <summary>The aim point on a watched or followed character: a small crosshair and a faint line to the first point, dimmed unless <paramref name="edited"/>.</summary>
    public static void DrawTargetMarker(EditorView view, Vector3 world, Vector3? firstPoint, bool edited)
    {
        var list = ImGui.GetBackgroundDrawList();
        if (firstPoint is { } first)
            DrawEdge(
                list,
                view,
                world,
                first,
                edited ? EditorColours.AnchorLink : EditorColours.OtherAnchorLink,
                GlyphThickness
            );
        DrawCross(
            list,
            view,
            world,
            TargetCross,
            edited ? EditorColours.Anchor : EditorColours.OtherAnchor,
            GlyphThickness
        );
    }

    private static void DrawCross(
        ImDrawListPtr list,
        EditorView view,
        Vector3 centre,
        float size,
        uint colour,
        float thickness
    )
    {
        DrawEdge(list, view, centre - (Vector3.UnitX * size), centre + (Vector3.UnitX * size), colour, thickness);
        DrawEdge(list, view, centre - (Vector3.UnitY * size), centre + (Vector3.UnitY * size), colour, thickness);
        DrawEdge(list, view, centre - (Vector3.UnitZ * size), centre + (Vector3.UnitZ * size), colour, thickness);
    }

    /// <summary>An offset on the ground at angle <paramref name="angle"/>, measured like yaw.</summary>
    private static Vector3 Ring(float angle, float radius) => Anchor.Turn(new Vector3(0f, 0f, -radius), angle);

    /// <summary>Draws <paramref name="name"/> on a plate centred above the ring round <paramref name="centre"/>, unless the centre is behind the camera.</summary>
    private static void DrawName(ImDrawListPtr list, EditorView view, Vector3 centre, string name)
    {
        if (view.ToScreenBeyondNear(centre) is not { } at)
            return;
        var top = at.Y;
        for (var i = 0; i < AnchorSegments; i++)
        {
            if (view.ToScreenBeyondNear(centre + Ring(MathF.Tau * i / AnchorSegments, AnchorRadius)) is { } p)
                top = MathF.Min(top, p.Y);
        }

        var size = ImGui.CalcTextSize(name) * NameScale;
        var origin = new Vector2(at.X - (size.X / 2f), top - NameGap - size.Y);
        list.AddRectFilled(origin - NamePadding, origin + size + NamePadding, EditorColours.NamePlate, NameRounding);
        list.AddText(ImGui.GetFont(), ImGui.GetFontSize() * NameScale, origin, EditorColours.NameText, name);
    }

    private static void DrawArrow(
        ImDrawListPtr list,
        EditorView view,
        Anchor world,
        float length,
        uint colour,
        float thickness
    )
    {
        var tip = world.Position + Ring(world.Yaw, length);
        DrawEdge(list, view, world.Position, tip, colour, thickness);
        DrawEdge(list, view, tip, world.Position + Ring(world.Yaw - 0.4f, length * 0.7f), colour, thickness);
        DrawEdge(list, view, tip, world.Position + Ring(world.Yaw + 0.4f, length * 0.7f), colour, thickness);
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
            if (
                ScreenProjection.ProjectSegment(
                    cache.Samples[i - 1],
                    cache.Samples[i],
                    view.ViewProjection,
                    view.Size,
                    view.Near
                ) is
                { } s
            )
                list.AddLine(view.Origin + s.Start, view.Origin + s.End, palette.Path, PathThickness);
        }
    }

    /// <summary>The path in samples over time, each stretch coloured by how fast the look turns there: the path's colour at rest, warm, then hot.</summary>
    private static void DrawHeat(
        ImDrawListPtr list,
        EditorView view,
        Track track,
        TrackCache cache,
        Vector3? aimPoint,
        Palette palette
    )
    {
        var evaluator = EvaluatorFor(track, cache);
        // A watched character moves every frame, so its heat is redrawn only once it has moved a little.
        if (!ReferenceEquals(cache.HeatTrack, track) || TurnHeat.TargetMoved(cache.HeatTarget, aimPoint))
        {
            cache.Heat = TurnHeat.Samples(evaluator, aimPoint);
            cache.HeatTrack = track;
            cache.HeatTarget = aimPoint;
        }

        for (var i = 1; i < cache.Heat.Count; i++)
        {
            var (from, to) = (cache.Heat[i - 1], cache.Heat[i]);
            if (
                ScreenProjection.ProjectSegment(
                    from.Position,
                    to.Position,
                    view.ViewProjection,
                    view.Size,
                    view.Near
                ) is
                { } s
            )
                list.AddLine(
                    view.Origin + s.Start,
                    view.Origin + s.End,
                    TurnHeat.Colour(
                        TurnHeat.Level(to.DegreesPerSecond),
                        palette.Path,
                        EditorColours.HeatWarm,
                        EditorColours.HeatHot
                    ),
                    PathThickness
                );
        }
    }

    /// <summary>The cached evaluator for <paramref name="track"/>, rebuilt when the track changes.</summary>
    private static TrackEvaluator EvaluatorFor(Track track, TrackCache cache)
    {
        if (!ReferenceEquals(cache.EvaluatedTrack, track))
        {
            cache.Evaluator = new TrackEvaluator(track);
            cache.EvaluatedTrack = track;
        }

        return cache.Evaluator!;
    }

    private static void DrawGlyph(
        ImDrawListPtr list,
        EditorView view,
        CameraGlyph glyph,
        bool selected,
        Palette palette
    )
    {
        var colour = selected ? EditorColours.Selected : palette.Glyph;
        var thickness = selected ? SelectedGlyphThickness : GlyphThickness;
        var corners = glyph.Corners;
        for (var i = 0; i < corners.Length; i++)
        {
            DrawEdge(list, view, glyph.Apex, corners[i], colour, thickness);
            DrawEdge(list, view, corners[i], corners[(i + 1) % corners.Length], colour, thickness);
        }

        if (
            view.ToScreenBeyondNear(glyph.TabLeft) is { } left
            && view.ToScreenBeyondNear(glyph.TabTip) is { } tip
            && view.ToScreenBeyondNear(glyph.TabRight) is { } right
        )
            list.AddTriangleFilled(left, tip, right, palette.UpLine);
    }

    private static void DrawEdge(
        ImDrawListPtr list,
        EditorView view,
        Vector3 from,
        Vector3 to,
        uint colour,
        float thickness
    )
    {
        if (ScreenProjection.ProjectSegment(from, to, view.ViewProjection, view.Size, view.Near) is { } s)
            list.AddLine(view.Origin + s.Start, view.Origin + s.End, colour, thickness);
    }

    private static void DrawLabels(
        ImDrawListPtr list,
        Vector2?[] labels,
        IReadOnlyCollection<int> selected,
        Palette palette
    )
    {
        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] is not { } at)
                continue;

            var picked = selected.Contains(i);
            list.AddCircleFilled(at, MarkerRadius, palette.Marker);
            list.AddCircle(
                at,
                MarkerRadius,
                picked ? EditorColours.Selected : palette.MarkerRing,
                0,
                picked ? 3f : 1.5f
            );

            var label = (i + 1).ToString(CultureInfo.InvariantCulture);
            var size = ImGui.CalcTextSize(label) * LabelScale;
            list.AddText(
                ImGui.GetFont(),
                ImGui.GetFontSize() * LabelScale,
                at - (size / 2f),
                palette.MarkerText,
                label
            );
        }
    }

    /// <summary>A track's sampled path and evaluator, rebuilt when the track changes.</summary>
    private sealed class TrackCache
    {
        public IReadOnlyList<ControlPoint>? SampledPoints;
        public IReadOnlyList<Vector3> Samples = [];
        public Track? EvaluatedTrack;
        public TrackEvaluator? Evaluator;
        public IReadOnlyList<TurnHeat.Sample> Heat = [];
        public Track? HeatTrack;
        public Vector3? HeatTarget;
    }

    /// <summary>The colours one track draws in.</summary>
    private readonly record struct Palette(
        uint Path,
        uint Glyph,
        uint UpLine,
        uint Marker,
        uint MarkerRing,
        uint MarkerText
    )
    {
        public static readonly Palette Edited = new(
            EditorColours.Path,
            EditorColours.AimLine,
            EditorColours.UpLine,
            EditorColours.Marker,
            EditorColours.MarkerRing,
            EditorColours.MarkerText
        );
        public static readonly Palette Other = new(
            EditorColours.OtherPath,
            EditorColours.OtherGlyph,
            EditorColours.OtherUpLine,
            EditorColours.OtherMarker,
            EditorColours.OtherMarkerRing,
            EditorColours.OtherMarkerText
        );
    }
}
