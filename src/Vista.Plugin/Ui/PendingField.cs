using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui;

/// <summary>A number field's dragged or typed value, held until the field is let go and then applied, so each change is one undo step.</summary>
internal sealed class PendingField(Func<bool> canApply)
{
    /// <summary>How far a field moves per pixel dragged, and the values it stops at.</summary>
    public readonly record struct Range(float Speed, float Min, float Max);

    private (string Id, float Value, float Shown, Action<float> Apply)? pending;

    /// <summary>Draws a drag field showing <paramref name="current"/> within <paramref name="range"/>; double-click to type. Applies the new value once the field is let go.</summary>
    public void Draw(string id, float current, string format, float width, Range range, Action<float> apply)
    {
        var value = pending is { } p && p.Id == id ? p.Value : current;
        ImGui.SetNextItemWidth(width);
        if (ImGui.DragFloat($"##{id}", ref value, range.Speed, range.Min, range.Max, format, ImGuiSliderFlags.AlwaysClamp)) pending = (id, value, current, apply);
        if (pending is { } done && done.Id == id && !ImGui.IsItemActive()) Commit();
    }

    /// <summary>Applies a typed value still waiting for its field to lose focus; dropped if it can no longer apply or did not change.</summary>
    public void Commit()
    {
        if (pending is not { } p) return;
        pending = null;
        if (canApply() && p.Value != p.Shown) p.Apply(p.Value);
    }

    /// <summary>Applies field <paramref name="id"/>'s waiting value, for a field whose menu closed before it could lose focus.</summary>
    public void Commit(string id)
    {
        if (pending is { } p && p.Id == id) Commit();
    }

    /// <summary>Drops a typed value without applying it.</summary>
    public void Clear() => pending = null;
}
