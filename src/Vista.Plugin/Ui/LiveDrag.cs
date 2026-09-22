using Dalamud.Bindings.ImGui;
using Vista.Plugin.Session;

namespace Vista.Plugin.Ui;

/// <summary>Runs a drag field as a live edit: begun on grab, previewed on each change, one undo step on release.</summary>
internal static class LiveDrag
{
    /// <summary>Handles the item just drawn; <paramref name="dragging"/> stays true until it lets go, so a closing window can end it.</summary>
    public static void Handle(CameraSession session, bool changed, Action preview, ref bool dragging)
    {
        if (ImGui.IsItemActivated())
        {
            session.BeginLiveEdit();
            dragging = true;
        }

        if (changed) preview();
        if (!ImGui.IsItemDeactivated()) return;
        dragging = false;
        session.EndLiveEdit();
    }

    /// <summary>Ends a drag a closed window never saw let go.</summary>
    public static void End(CameraSession session, ref bool dragging)
    {
        if (!dragging) return;
        dragging = false;
        session.EndLiveEdit();
    }
}
