using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Plugin.Editor;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The free camera's own position, aim, roll and field of view, in the Point window's layout; shown only while editing.</summary>
internal sealed class CameraWindow : Window
{
    private readonly GameSession game;
    private float gridWidth;
    private GizmoMode mode = GizmoMode.Move;

    public CameraWindow(GameSession game)
        : base("Camera###vista-camera")
    {
        this.game = game;
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
    }

    /// <summary>Open only while editing, since the free camera exists only then.</summary>
    public override bool DrawConditions() => game.State.Mode == CameraMode.Editing;

    public override void Draw()
    {
        using var style = PoseGrid.Style();

        // The camera can't rotate by gizmo, or be copied, pasted or deleted; the row stays so the windows match.
        _ = PoseGrid.Header(
            mode,
            m => mode = m,
            rotates: false,
            gridWidth,
            canCopy: false,
            canPaste: false,
            canDelete: false
        );
        if (!PoseGrid.BeginGrid())
            return;

        var position = game.CameraPosition;
        var (yaw, pitch) = GameSession.CameraAngles ?? (0f, 0f);
        ImGui.TableNextRow();
        PoseGrid.Label(FontAwesomeIcon.ArrowsAlt, "Position");
        if (mode == GizmoMode.MoveLocal)
            DrawNudges(position, yaw, pitch);
        else
            DrawWorld(position);

        ImGui.TableNextRow();
        if (PoseGrid.Button("level-roll", FontAwesomeIcon.SyncAlt, "Level roll", enabled: true))
            game.CameraRoll = 0f;
        Field(
            "cam-pitch",
            "Pitch",
            EditorColours.AxisX,
            PoseGrid.Degrees(pitch),
            PoseGrid.AngleSpeed,
            "%.1f°",
            v => game.TurnCamera(yaw, EditLimits.Pitch(PoseGrid.Radians(v)))
        );
        Field(
            "cam-yaw",
            "Yaw",
            EditorColours.AxisY,
            PoseGrid.Degrees(EditLimits.Angle(yaw)),
            PoseGrid.AngleSpeed,
            "%.1f°",
            v => game.TurnCamera(EditLimits.Angle(PoseGrid.Radians(v)), pitch)
        );
        Field(
            "cam-roll",
            "Roll",
            EditorColours.AxisZ,
            PoseGrid.Degrees(game.CameraRoll),
            PoseGrid.AngleSpeed,
            "%.1f°",
            v => game.CameraRoll = EditLimits.Angle(PoseGrid.Radians(v))
        );

        ImGui.TableNextRow();
        var takeover = game.TakeoverFov;
        if (
            PoseGrid.Button(
                "reset-fov",
                FontAwesomeIcon.History,
                "Reset to the game's field of view",
                enabled: takeover is not null
            ) && takeover is { } fov
        )
            game.CameraFov = fov;
        Field(
            "cam-fov",
            "FoV",
            null,
            PoseGrid.Degrees(game.CameraFov),
            PoseGrid.FovSpeed,
            "%.1f°",
            v => game.CameraFov = EditLimits.Fov(PoseGrid.Radians(v))
        );

        gridWidth = PoseGrid.EndGrid();
    }

    /// <summary>X, Y and Z: where the camera is in the world.</summary>
    private void DrawWorld(Vector3 position)
    {
        Field(
            "cam-x",
            "X",
            EditorColours.AxisX,
            position.X,
            PoseGrid.PositionSpeed,
            "%.2f",
            v => game.CameraPosition = position with { X = EditLimits.Coordinate(v, position.X) }
        );
        Field(
            "cam-y",
            "Y",
            EditorColours.AxisY,
            position.Y,
            PoseGrid.PositionSpeed,
            "%.2f",
            v => game.CameraPosition = position with { Y = EditLimits.Coordinate(v, position.Y) }
        );
        Field(
            "cam-z",
            "Z",
            EditorColours.AxisZ,
            position.Z,
            PoseGrid.PositionSpeed,
            "%.2f",
            v => game.CameraPosition = position with { Z = EditLimits.Coordinate(v, position.Z) }
        );
    }

    /// <summary>Right, Up and Forward, which read 0 and move the camera by what is dragged or typed, along the axes the fly keys use.</summary>
    private void DrawNudges(Vector3 position, float yaw, float pitch)
    {
        // A drag reports this frame's movement from 0, and the field reads 0 again next frame.
        void Nudge(string id, string name, uint border, Func<float, Vector3> input) =>
            Field(
                id,
                name,
                border,
                0f,
                PoseGrid.PositionSpeed,
                "%.2f",
                v =>
                {
                    if (float.IsFinite(v))
                        game.CameraPosition = FreeCamMotion.Step(position, input(v), yaw, pitch, 1f, 1f);
                }
            );

        // FreeCamMotion's input is (forward, up, right).
        Nudge("cam-right", "Right", EditorColours.AxisX, v => new Vector3(0f, 0f, v));
        Nudge("cam-up", "Up", EditorColours.AxisY, v => new Vector3(0f, v, 0f));
        Nudge("cam-forward", "Forward", EditorColours.AxisZ, v => new Vector3(v, 0f, 0f));
    }

    /// <summary>A field that writes straight to the camera. Flying is not scene state, so this is not an undo step.</summary>
    private static void Field(
        string id,
        string name,
        uint? border,
        float value,
        float speed,
        string format,
        Action<float> set
    )
    {
        var edited = value;
        if (PoseGrid.Field(id, name, border, ref edited, speed, format))
            set(edited);
    }
}
