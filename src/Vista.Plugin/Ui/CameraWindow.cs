using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Plugin.Editor;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui;

/// <summary>The free camera's own position, aim, roll and field of view; shown only while editing.</summary>
internal sealed class CameraWindow : Window
{
    private const float FieldWidth = 70f;
    private const float PositionSpeed = 0.02f;
    private const float AngleSpeed = 0.25f;
    private const float FovSpeed = 0.1f;

    private readonly CameraSession session;

    public CameraWindow(CameraSession session) : base("Camera###vista-camera")
    {
        this.session = session;
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
    }

    /// <summary>Open only while editing, since the free camera exists only then.</summary>
    public override bool DrawConditions() => session.Mode == CameraMode.Editing;

    public override void Draw()
    {
        var position = session.CameraPosition;
        Field("cam-x", "X", EditorColours.AxisX, position.X, PositionSpeed, "%.2f",
            v => session.CameraPosition = position with { X = EditLimits.Coordinate(v, position.X) });
        ImGui.SameLine();
        Field("cam-y", "Y", EditorColours.AxisY, position.Y, PositionSpeed, "%.2f",
            v => session.CameraPosition = position with { Y = EditLimits.Coordinate(v, position.Y) });
        ImGui.SameLine();
        Field("cam-z", "Z", EditorColours.AxisZ, position.Z, PositionSpeed, "%.2f",
            v => session.CameraPosition = position with { Z = EditLimits.Coordinate(v, position.Z) });

        var (yaw, pitch) = session.CameraAngles ?? (0f, 0f);
        Field("cam-pitch", "Pitch", EditorColours.AxisX, Degrees(pitch), AngleSpeed, "%.1f°",
            v => session.TurnCamera(yaw, EditLimits.Pitch(Radians(v))));
        ImGui.SameLine();
        Field("cam-yaw", "Yaw", EditorColours.AxisY, Degrees(EditLimits.Angle(yaw)), AngleSpeed, "%.1f°",
            v => session.TurnCamera(EditLimits.Angle(Radians(v)), pitch));
        ImGui.SameLine();
        Field("cam-roll", "Roll", EditorColours.AxisZ, Degrees(session.CameraRoll), AngleSpeed, "%.1f°",
            v => session.CameraRoll = EditLimits.Angle(Radians(v)));

        Field("cam-fov", "FoV", null, Degrees(session.CameraFov), FovSpeed, "%.1f°",
            v => session.CameraFov = EditLimits.Fov(Radians(v)));
    }

    /// <summary>A field that writes straight to the camera. Flying is not scene state, so this is not an undo step.</summary>
    private static void Field(string id, string name, uint? border, float value, float speed, string format, Action<float> set)
    {
        var edited = value;
        if (BorderedField.Draw(id, name, border, ref edited, speed, format, FieldWidth)) set(edited);
    }

    private static float Degrees(float radians) => radians * 180f / MathF.PI;

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;
}
