using Dalamud.Bindings.ImGui;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Session;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Dragging list rows: the payload types, what a drag carries, the drop hint shown in its label, and reading Ctrl and Shift for a row click.</summary>
internal static unsafe class DragRows
{
    public const string Track = "VISTA_TRACK";
    public const string Point = "VISTA_POINT";
    public const string Entry = "VISTA_ENTRY";

    /// <summary>A dragged row's index in its list, and whether it carries the whole selection with it.</summary>
    public readonly record struct Payload(int Grabbed, bool Group);

    // What the drop target under the cursor will do, and the frame it said so.
    private static (string Text, int Frame)? hint;

    /// <summary>Inside a drag source: sets the payload and draws the label, or the hovered target's hint.</summary>
    public static void Carry(string type, int grabbed, bool group, string label)
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
        payload.Group ? session.Selection.Tracks
        : payload.Grabbed < scene.Tracks.Count ? [scene.Tracks[payload.Grabbed].Id]
        : [];

    /// <summary>The points a dropped point payload carries: the selection, or the grabbed point alone.</summary>
    public static IReadOnlyList<int> Points(SessionState session, Payload payload) =>
        payload.Group ? session.Selection.Points : [payload.Grabbed];

    /// <summary>The entries a dropped entry payload carries: the selection, or the grabbed entry alone.</summary>
    public static IReadOnlyList<Guid> Entries(SessionState session, Scene scene, Payload payload) =>
        payload.Group ? session.Selection.Entries
        : payload.Grabbed < scene.Playlist.Count ? [scene.Playlist[payload.Grabbed].Id]
        : [];

    /// <summary>True while a row of <paramref name="type"/> is being dragged.</summary>
    public static bool Dragging(string type)
    {
        var payload = ImGui.GetDragDropPayload();
        return !payload.IsNull && payload.IsDataType(type);
    }

    /// <summary>The click just made on a row: Shift for a range, Ctrl to add or remove, otherwise plain.</summary>
    public static RowClick Click()
    {
        var io = ImGui.GetIO();
        return RowPicking.FromKeys(io.KeyShift, io.KeyCtrl);
    }
}
