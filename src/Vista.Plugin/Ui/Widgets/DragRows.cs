using System.Numerics;
using Dalamud.Bindings.ImGui;
using Vista.Core.Display;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Dragging list rows: the payload types, the drag source, what a drag carries, the drop hint shown in its label, the space under a list, and reading Ctrl and Shift for a row click.</summary>
internal static unsafe class DragRows
{
    public const string Track = "VISTA_TRACK";
    public const string Point = "VISTA_POINT";
    public const string Entry = "VISTA_ENTRY";

    /// <summary>A dragged row's index in its list, and whether it carries the whole selection with it.</summary>
    public readonly record struct Payload(int Grabbed, bool Group);

    // What the drop target under the cursor will do, and the frame it said so.
    private static (string Text, int Frame)? hint;

    /// <summary>Makes the item just drawn, row <paramref name="grabbed"/>, a drag source: labelled "<paramref name="count"/> <paramref name="plural"/>" when it carries the group, else <paramref name="single"/>.</summary>
    public static void Source(string type, int grabbed, bool group, int count, string plural, string single)
    {
        if (!ImGui.BeginDragDropSource())
            return;
        Carry(type, grabbed, group, group ? FormattableString.Invariant($"{count} {plural}") : single);
        ImGui.EndDragDropSource();
    }

    /// <summary>Inside a drag source: sets the payload and draws the label, or the hovered target's hint.</summary>
    private static void Carry(string type, int grabbed, bool group, string label)
    {
        var payload = new Payload(grabbed, group);
        ImGui.SetDragDropPayload(type, new ReadOnlySpan<byte>(&payload, sizeof(Payload)));
        ImGui.TextUnformatted(hint is { } h && h.Frame >= ImGui.GetFrameCount() - 1 ? h.Text : label);
    }

    /// <summary>Inside a drop target: the payload once dropped, or null; while one only hovers, <paramref name="hintText"/> becomes the drag's label.</summary>
    public static Payload? Accept(string type, string? hintText = null)
    {
        var payload = ImGui.AcceptDragDropPayload(type, ImGuiDragDropFlags.AcceptBeforeDelivery);
        if (payload.IsNull)
            return null;
        if (hintText is not null)
            hint = (hintText, ImGui.GetFrameCount());
        return payload.Handle->IsDelivery() ? *(Payload*)payload.Data : null;
    }

    /// <summary>The tracks a dropped track payload carries: the selection, or the grabbed track alone.</summary>
    public static IReadOnlyList<Guid> Tracks(SessionState session, Scene scene, Payload payload) =>
        RowPicking.Carried(
            scene.Tracks.Select(t => t.Id).ToArray(),
            session.Selection.Tracks,
            payload.Grabbed,
            payload.Group
        );

    /// <summary>The points a dropped point payload carries: the selection, or the grabbed point alone while it is still in the track.</summary>
    public static IReadOnlyList<int> Points(SessionState session, Payload payload) =>
        payload.Group ? session.Selection.Points
        : TrackEditing.IsPoint(session.Track, payload.Grabbed) ? [payload.Grabbed]
        : [];

    /// <summary>The entries a dropped entry payload carries: the selection, or the grabbed entry alone.</summary>
    public static IReadOnlyList<Guid> Entries(SessionState session, Scene scene, Payload payload) =>
        RowPicking.Carried(
            scene.Playlist.Select(e => e.Id).ToArray(),
            session.Selection.Entries,
            payload.Grabbed,
            payload.Group
        );

    /// <summary>True while a row of <paramref name="type"/> is being dragged.</summary>
    public static bool Dragging(string type)
    {
        var payload = ImGui.GetDragDropPayload();
        return !payload.IsNull && payload.IsDataType(type);
    }

    /// <summary>Inside a list's child window: while a row of one of <paramref name="types"/> is dragged over it near its top or bottom, scrolls it that way.</summary>
    public static void ScrollNearEdges(params string[] types)
    {
        if (!types.Any(Dragging))
            return;
        var (corner, size, mouse) = (ImGui.GetWindowPos(), ImGui.GetWindowSize(), ImGui.GetMousePos());
        if (mouse.X < corner.X || mouse.X > corner.X + size.X)
            return;
        var row = ImGui.GetFrameHeightWithSpacing();
        var rows = EdgeScroll.RowsPerSecond(mouse.Y, corner.Y, corner.Y + size.Y, row);
        if (rows != 0f)
            ImGui.SetScrollY(
                Math.Clamp(ImGui.GetScrollY() + (rows * row * ImGui.GetIO().DeltaTime), 0f, ImGui.GetScrollMaxY())
            );
    }

    /// <summary>The space under a list's rows, filling the rest of the list and at least a row tall; while <paramref name="editing"/>, a plain click there clears the selection. The space is the last item, for the caller's drop target.</summary>
    public static void Space(SessionState session, bool editing)
    {
        ImGui.Dummy(
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                MathF.Max(ImGui.GetContentRegionAvail().Y, ImGui.GetFrameHeight())
            )
        );
        if (editing && ImGui.IsItemClicked() && Click() == RowClick.Plain)
            session.Selection.Select(null);
    }

    /// <summary>The click just made on a row: Shift for a range, Ctrl to add or remove, otherwise plain.</summary>
    public static RowClick Click()
    {
        var io = ImGui.GetIO();
        return RowPicking.FromKeys(io.KeyShift, io.KeyCtrl);
    }
}
