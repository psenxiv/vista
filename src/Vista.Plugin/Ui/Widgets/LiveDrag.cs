using Dalamud.Bindings.ImGui;
using Vista.Core.Session;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Runs a drag field as a live edit: begun on grab, previewed on each change, one undo step on release.</summary>
internal static class LiveDrag
{
    /// <summary>Draws a field showing <paramref name="value"/> with <paramref name="field"/> and runs it as a live edit, previewing each new value.</summary>
    public static void Field(
        SessionState session,
        float value,
        FieldDraw<float> field,
        Action<float> preview,
        ref bool dragging
    )
    {
        var edited = value;
        var changed = field(ref edited);
        Handle(session, changed, () => preview(edited), ref dragging);
    }

    /// <summary>Handles the item just drawn; <paramref name="dragging"/> stays true until it lets go, so a closing window can end it.</summary>
    private static void Handle(SessionState session, bool changed, Action preview, ref bool dragging)
    {
        if (ImGui.IsItemActivated())
        {
            session.BeginLiveEdit();
            dragging = true;
        }

        if (changed)
            preview();
        if (!ImGui.IsItemDeactivated())
            return;
        dragging = false;
        session.EndLiveEdit();
    }

    /// <summary>Ends a drag a closed window never saw let go.</summary>
    public static void End(SessionState session, ref bool dragging)
    {
        if (!dragging)
            return;
        dragging = false;
        session.EndLiveEdit();
    }
}
