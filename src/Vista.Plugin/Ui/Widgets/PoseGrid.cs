using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Vista.Plugin.Editor;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>The layout the Point and Camera windows share: gizmo and clipboard buttons, then position, rotation and field-of-view rows, each led by its icon.</summary>
internal static class PoseGrid
{
    public const float FieldWidth = 70f;
    public const float PositionSpeed = 0.02f;
    public const float AngleSpeed = 0.25f;
    public const float FovSpeed = 0.1f;

    /// <summary>Which clipboard button was pressed this frame.</summary>
    public enum Clip
    {
        None,
        Copy,
        Paste,
        Delete,
    }

    /// <summary>The spacing both windows draw the header and grid with.</summary>
    public static IDisposable Style() =>
        ImRaii
            .PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 7f))
            .Push(ImGuiStyleVar.CellPadding, new Vector2(4f, 3f));

    /// <summary>The move and rotate buttons for <paramref name="mode"/>, then copy, paste and delete right-aligned over the grid; the clipboard buttons that cannot act are disabled.</summary>
    public static Clip Header(
        GizmoMode mode,
        Action<GizmoMode> setMode,
        bool rotates,
        float gridWidth,
        bool canCopy,
        bool canPaste,
        bool canDelete
    )
    {
        // A move button lights when rotate is not the mode, or cannot be: an anchor falls back to world.
        var moving = !rotates || mode != GizmoMode.Rotate;
        var local = moving && mode == GizmoMode.MoveLocal;

        if (IconButton.Toggle("gizmo-world", FontAwesomeIcon.Globe, moving && !local, "Move (world)"))
            setMode(GizmoMode.Move);
        ImGui.SameLine();
        if (IconButton.Toggle("gizmo-local", FontAwesomeIcon.Cube, local, "Move (local)"))
            setMode(GizmoMode.MoveLocal);
        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X * 3f);
        ImGui.BeginDisabled(!rotates);
        if (IconButton.Toggle("gizmo-rotate", FontAwesomeIcon.SyncAlt, rotates && mode == GizmoMode.Rotate, "Rotate"))
            setMode(GizmoMode.Rotate);
        ImGui.EndDisabled();

        // Right-align to last frame's grid, whose width is its columns' own, not the window's.
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var icons =
            IconButton.Width(FontAwesomeIcon.Copy)
            + IconButton.Width(FontAwesomeIcon.Paste)
            + IconButton.Width(FontAwesomeIcon.Trash)
            + (gap * 2f);
        ImGui.SameLine();
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetStyle().WindowPadding.X + gridWidth - icons));

        var clip = Clip.None;
        ImGui.BeginDisabled(!canCopy);
        if (IconButton.Draw("copy-pose", FontAwesomeIcon.Copy, "Copy position, aim, roll and FoV"))
            clip = Clip.Copy;
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!canPaste);
        if (IconButton.Draw("paste-pose", FontAwesomeIcon.Paste, "Paste position, aim, roll and FoV"))
            clip = Clip.Paste;
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!canDelete);
        if (IconButton.Draw("delete-pose", FontAwesomeIcon.Trash, "Delete point", danger: true))
            clip = Clip.Delete;
        ImGui.EndDisabled();
        return clip;
    }

    /// <summary>Starts the grid: an icon column, then three field columns.</summary>
    public static bool BeginGrid() =>
        ImGui.BeginTable("pose", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX);

    /// <summary>Ends the grid and returns its width, for aligning next frame's header.</summary>
    public static float EndGrid()
    {
        ImGui.EndTable();
        return ImGui.GetItemRectSize().X;
    }

    /// <summary>A row's icon in the first column, naming the row in its tooltip. Inset like an icon button's glyph, so it sits under the gizmo buttons.</summary>
    public static void Label(FontAwesomeIcon icon, string name)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetStyle().FramePadding.X);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
            ImGui.TextUnformatted(icon.ToIconString());
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(name);
    }

    /// <summary>A row's action in the first column, in place of its label.</summary>
    public static bool Button(string id, FontAwesomeIcon icon, string tooltip, bool enabled)
    {
        ImGui.TableNextColumn();
        ImGui.BeginDisabled(!enabled);
        var pressed = IconButton.Draw(id, icon, tooltip);
        ImGui.EndDisabled();
        return pressed;
    }

    /// <summary>One number field in the next column, bordered in its axis colour and named by its tooltip.</summary>
    public static bool Field(string id, string name, uint? border, ref float value, float speed, string format)
    {
        ImGui.TableNextColumn();
        return BorderedField.Draw(id, name, border, ref value, speed, format, FieldWidth);
    }

    /// <summary>A disabled field showing "—", for what the selection does not have.</summary>
    public static void Missing(string id, string name, uint? border)
    {
        var none = 0f;
        ImGui.BeginDisabled(true);
        _ = Field(id, name, border, ref none, 0f, "—");
        ImGui.EndDisabled();
    }

    public static float Degrees(float radians) => radians * 180f / MathF.PI;

    public static float Radians(float degrees) => degrees * MathF.PI / 180f;
}
