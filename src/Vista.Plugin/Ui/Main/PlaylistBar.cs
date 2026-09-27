using System.Numerics;
using Dalamud.Bindings.ImGui;
using Vista.Core.Display;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;

namespace Vista.Plugin.Ui.Main;

/// <summary>Live's scrub bar over the whole playlist, drawn like a slider: a segment per entry, pass ticks, the playing entry lit, and a readout.</summary>
internal static class PlaylistBar
{
    // ImGui's inset of a slider's grab from its frame.
    private const float GrabPadding = 2f;

    /// <summary>Draws the bar across the row and seeks while it's held, calling <paramref name="activated"/> as a drag starts; false, drawing nothing, when Live has no playlist.</summary>
    public static bool Draw(SessionState session, Scrubber scrub, Action activated)
    {
        if (session.Transport.Timeline is not { } timeline || session.Director.Playlist is not { } playlist)
            return false;

        var bar = new PlaylistScrub(timeline, session.Scene);
        var style = ImGui.GetStyle();
        ImGui.BeginDisabled(session.Released || timeline.Total <= 0.0);

        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(MathF.Max(ImGui.GetContentRegionAvail().X, 1f), ImGui.GetFrameHeight());
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
            scrub.Begin();
        }
        if (activating || (ImGui.IsItemActive() && ImGui.GetIO().MouseDelta.X != 0f))
            session.Transport.ScrubPlaylistTo(bar.TimeAt(mouse));
        // A window that stops drawing mid-drag never reports the bar deactivating, so any idle frame ends the scrub too.
        if (ImGui.IsItemDeactivated() || !ImGui.IsItemActive())
            scrub.End();

        var active = ImGui.IsItemActive();
        var hovered = !active && ImGui.IsItemHovered() ? bar.SegmentAt(mouse) : null;
        var list = ImGui.GetWindowDrawList();
        list.AddRectFilled(
            min,
            max,
            ImGui.GetColorU32(active ? ImGuiCol.FrameBgActive : ImGuiCol.FrameBg),
            style.FrameRounding
        );
        foreach (var segment in timeline.Segments)
            DrawSegment(list, bar, segment, segment == hovered, segment.Index == playlist.Index, min, max, left, right);

        var head = session.Transport.PlaylistHead;
        var at = float.Lerp(left, right, bar.FractionOf(head));
        list.AddRectFilled(
            new Vector2(at - (grab / 2f), min.Y + GrabPadding),
            new Vector2(at + (grab / 2f), max.Y - GrabPadding),
            ImGui.GetColorU32(active ? ImGuiCol.SliderGrabActive : ImGuiCol.SliderGrab),
            style.GrabRounding
        );
        if (style.FrameBorderSize > 0f)
            list.AddRect(
                min,
                max,
                ImGui.GetColorU32(ImGuiCol.Border),
                style.FrameRounding,
                ImDrawFlags.None,
                style.FrameBorderSize
            );

        var padding = style.FramePadding.X;
        var readout = bar.Readout(playlist.Index, head, size.X - (padding * 2f), s => ImGui.CalcTextSize(s).X);
        var text = ImGui.CalcTextSize(readout);
        var textAt = new Vector2(min.X + MathF.Max(padding, (size.X - text.X) / 2f), min.Y + ((size.Y - text.Y) / 2f));
        list.PushClipRect(min, max, true);
        list.AddText(textAt, ImGui.GetColorU32(ImGuiCol.Text), readout);
        list.PopClipRect();

        if (hovered is not null && bar.Label(hovered) is { } label)
            ImGui.SetTooltip(label);
        ImGui.EndDisabled();
        return true;
    }

    /// <summary>One entry's segment: its fill when hovered or playing, its pass ticks, and the divider at its start.</summary>
    private static void DrawSegment(
        ImDrawListPtr list,
        PlaylistScrub bar,
        PlaylistSegment segment,
        bool hovered,
        bool playing,
        Vector2 min,
        Vector2 max,
        float left,
        float right
    )
    {
        var first = segment.Index == 0;
        var last = segment.Index == bar.Timeline.Segments.Count - 1;
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
        if (playing)
            list.AddRectFilled(
                min with
                {
                    X = from,
                },
                max with
                {
                    X = to,
                },
                ImGui.GetColorU32(UiColours.Selected()),
                rounding,
                corners
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
