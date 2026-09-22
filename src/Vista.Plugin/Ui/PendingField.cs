using Dalamud.Bindings.ImGui;

namespace CinematicCam.Plugin.Ui;

/// <summary>A number field's typed value, held until the field loses focus and then applied.</summary>
internal sealed class PendingField(Func<bool> canApply)
{
    private (string Id, float Value, float Shown, Action<float> Apply)? pending;

    /// <summary>Draws a number field showing <paramref name="current"/>, and applies a typed value once the field is no longer active.</summary>
    public void Draw(string id, float current, string format, float width, Action<float> apply)
    {
        var value = pending is { } p && p.Id == id ? p.Value : current;
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputFloat($"##{id}", ref value, 0f, 0f, format)) pending = (id, value, current, apply);
        if (pending is { } done && done.Id == id && !ImGui.IsItemActive()) Commit();
    }

    /// <summary>Applies a typed value still waiting for its field to lose focus; dropped if it can no longer apply or did not change.</summary>
    public void Commit()
    {
        if (pending is not { } p) return;
        pending = null;
        if (canApply() && p.Value != p.Shown) p.Apply(p.Value);
    }

    /// <summary>Drops a typed value without applying it.</summary>
    public void Clear() => pending = null;
}
