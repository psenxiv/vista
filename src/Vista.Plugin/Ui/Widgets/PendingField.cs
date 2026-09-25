using System.Numerics;
using Dalamud.Bindings.ImGui;
using Vista.Core.Editing;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Fields whose dragged or typed value is held until the field is let go and then applied, so each change is one undo step.</summary>
internal static class PendingField
{
    /// <summary>How far a field moves per pixel dragged, and the values it stops at.</summary>
    public readonly record struct Range(float Speed, float Min, float Max);

    /// <summary>Draws a drag field showing <paramref name="current"/> within <paramref name="range"/>; double-click to type. Applies the new value once the field is let go.</summary>
    public static void Draw(
        this PendingEdit<float> edit,
        string id,
        float current,
        string format,
        float width,
        Range range,
        Action<float> apply
    )
    {
        var value = edit.ValueOr(id, current);
        ImGui.SetNextItemWidth(width);
        if (
            ImGui.DragFloat(
                $"##{id}",
                ref value,
                range.Speed,
                range.Min,
                range.Max,
                format,
                ImGuiSliderFlags.AlwaysClamp
            )
        )
            edit.Hold(id, value, current, apply);
        Settle(edit, id);
    }

    /// <summary>Draws field <paramref name="id"/> with <paramref name="field"/>, showing <paramref name="current"/> or the value it holds, and applies the new value once the field is let go.</summary>
    public static void Draw<T>(this PendingEdit<T> edit, string id, T current, FieldDraw<T> field, Action<T> apply)
        where T : IEqualityOperators<T, T, bool>
    {
        var value = edit.ValueOr(id, current);
        if (field(ref value))
            edit.Hold(id, value, current, apply);
        Settle(edit, id);
    }

    /// <summary>Applies field <paramref name="id"/>'s held value once the item just drawn is no longer active.</summary>
    private static void Settle<T>(PendingEdit<T> edit, string id)
        where T : IEqualityOperators<T, T, bool>
    {
        if (!ImGui.IsItemActive())
            edit.Commit(id);
    }
}
