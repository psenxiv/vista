using System.Numerics;
using Dalamud.Bindings.ImGui;
using Vista.Core.Display;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Vista.Plugin.Ui.Widgets;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The scrub bar over the whole of a playing shot, drawn like a slider: a segment per entry, pass ticks, the playing entry filled up to the head, and each entry's name above it on hover.</summary>
internal static class PlaylistBar
{
    // ImGui's inset of a slider's grab from its frame.
    private const float GrabPadding = 2f;

    /// <summary>Draws <paramref name="shot"/>'s bar <paramref name="width"/> wide over <paramref name="view"/>, labelled from the track or playlist with id <paramref name="playing"/>, and seeks while it's held, calling <paramref name="activated"/> as a drag starts; false, drawing nothing, with nothing playing.</summary>
    public static bool Draw(
        IPlayingShot shot,
        Scene scene,
        Guid playing,
        Action activated,
        float width,
        TimingView view
    )
    {
        if (shot.Timeline is not { } timeline)
            return false;

        var bar = new PlaylistScrub(timeline, scene, playing, view);
        var style = ImGui.GetStyle();
        ImGui.BeginDisabled(timeline.Total <= 0.0);

        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(MathF.Max(width, 1f), ImGui.GetFrameHeight());
        var max = min + size;
        ImGui.InvisibleButton("##playlist-scrub", size);
        var grab = style.GrabMinSize;
        var left = min.X + GrabPadding + (grab / 2f);
        var right = max.X - GrabPadding - (grab / 2f);
        var mouse = Fraction.Between(ImGui.GetMousePos().X, left, right, 0f);

        var activating = ImGui.IsItemActivated();
        if (activating)
        {
            activated();
            shot.BeginScrub();
        }
        if (activating || (ImGui.IsItemActive() && ImGui.GetIO().MouseDelta.X != 0f))
            shot.ScrubTo(bar.TimeAt(mouse));
        // A window that stops drawing mid-drag never reports the bar deactivating, so any idle frame ends the scrub too.
        if (ImGui.IsItemDeactivated() || !ImGui.IsItemActive())
            shot.EndScrub();

        var active = ImGui.IsItemActive();
        var hovered = !active && ImGui.IsItemHovered() ? bar.SegmentAt(mouse) : null;
        var list = ImGui.GetWindowDrawList();
        list.AddRectFilled(
            min,
            max,
            ImGui.GetColorU32(active ? ImGuiCol.FrameBgActive : ImGuiCol.FrameBg),
            style.FrameRounding
        );
        // Past the grab's travel the fill runs to the frame's edge, so a finished shot reads as full.
        var headX = float.Lerp(left, right, bar.FractionOf(shot.Head));
        var filledTo = headX >= right ? max.X : headX;
        foreach (var segment in timeline.Segments)
            DrawSegment(
                list,
                bar,
                segment,
                segment == hovered,
                segment.Index == shot.EntryIndex,
                filledTo,
                min,
                max,
                left,
                right
            );

        var head = shot.Head;
        if (bar.Shows(head))
        {
            var at = float.Lerp(left, right, bar.FractionOf(head));
            list.AddRectFilled(
                new Vector2(at - (grab / 2f), min.Y + GrabPadding),
                new Vector2(at + (grab / 2f), max.Y - GrabPadding),
                ImGui.GetColorU32(active ? ImGuiCol.SliderGrabActive : ImGuiCol.SliderGrab),
                style.GrabRounding
            );
        }
        if (style.FrameBorderSize > 0f)
            list.AddRect(
                min,
                max,
                ImGui.GetColorU32(ImGuiCol.Border),
                style.FrameRounding,
                ImDrawFlags.None,
                style.FrameBorderSize
            );

        if (hovered is not null && bar.Label(hovered) is { } label)
        {
            // Above the bar at the mouse, cut to the bar's width.
            ImGui.SetNextWindowPos(
                new Vector2(ImGui.GetMousePos().X, min.Y - style.ItemSpacing.Y),
                ImGuiCond.Always,
                new Vector2(0.5f, 1f)
            );
            ImGui.SetTooltip(RowFit.Ellipsis(label, size.X, s => ImGui.CalcTextSize(s).X));
        }
        ImGui.EndDisabled();
        return true;
    }

    /// <summary>One entry's segment: its fill when hovered, filled up to <paramref name="filledTo"/> when playing, its pass ticks, and the divider at its start.</summary>
    private static void DrawSegment(
        ImDrawListPtr list,
        PlaylistScrub bar,
        PlaylistSegment segment,
        bool hovered,
        bool playing,
        float filledTo,
        Vector2 min,
        Vector2 max,
        float left,
        float right
    )
    {
        if (!bar.Visible(segment))
            return;
        // A segment reaching either end of the view runs to the frame's edge there, rounded like it.
        var first = bar.FractionOf(segment.Start) <= 0f;
        var last = bar.FractionOf(segment.End) >= 1f;
        var from = first ? min.X : float.Lerp(left, right, bar.FractionOf(segment.Start));
        var to = last ? max.X : float.Lerp(left, right, bar.FractionOf(segment.End));
        var corners =
            first && last ? ImDrawFlags.RoundCornersAll
            : first ? ImDrawFlags.RoundCornersLeft
            : last ? ImDrawFlags.RoundCornersRight
            : ImDrawFlags.RoundCornersNone;
        var rounding = ImGui.GetStyle().FrameRounding;
        if (hovered)
            list.AddRectFilled(
                min with
                {
                    X = from,
                },
                max with
                {
                    X = to,
                },
                ImGui.GetColorU32(ImGuiCol.FrameBgHovered),
                rounding,
                corners
            );
        var end = MathF.Min(to, filledTo);
        if (playing && end > from)
            list.AddRectFilled(
                min with
                {
                    X = from,
                },
                max with
                {
                    X = end,
                },
                ImGui.GetColorU32(UiColours.Selected()),
                rounding,
                end >= max.X ? corners : corners & ~ImDrawFlags.RoundCornersRight
            );

        foreach (var tick in bar.PassTicks(segment))
        {
            var x = float.Lerp(left, right, tick);
            list.AddLine(min with { X = x }, max with { X = x }, ImGui.GetColorU32(UiColours.Faint()));
        }

        if (!first)
            list.AddLine(min with { X = from }, max with { X = from }, ImGui.GetColorU32(UiColours.Dim()));
    }
}
