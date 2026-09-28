using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Input;

namespace Vista.Plugin.Game;

/// <summary>Flies the camera like a plane with WASD and Q/E along its own axes, and rolls it with Ctrl + Q/E. Mouse-look turns it about its own axes, with no limit.</summary>
internal sealed class FreeCam
{
    private const float BaseSpeed = 8f;
    private const float SprintMultiplier = 4f;
    private const float RollRate = MathF.PI / 3f;

    private Vector3 position;
    private Quaternion rotation = Quaternion.Identity;
    private (float Yaw, float Pitch)? written;
    private float fov;

    public bool Enabled { get; private set; }

    /// <summary>Where the camera is.</summary>
    public Vector3 Position
    {
        get => position;
        set => position = value;
    }

    /// <summary>Which way the camera faces and which way is up in its picture.</summary>
    public Quaternion Rotation
    {
        get => rotation;
        set => rotation = Quaternion.Normalize(value);
    }

    /// <summary>Field of view in radians, clamped to the editor's range.</summary>
    public float Fov
    {
        get => fov;
        set => fov = EditLimits.Fov(value, fov);
    }

    /// <summary>The stepped speed setting; Shift still boosts on top.</summary>
    public FlySpeed Speed { get; } = new();

    public void Enable(Vector3 startPosition, Quaternion startRotation, float startFov)
    {
        position = startPosition;
        Rotation = startRotation;
        fov = startFov;
        written = null;
        Enabled = true;
    }

    public void Disable() => Enabled = false;

    /// <summary>True when a flight or roll key is held this frame, outside text fields.</summary>
    public static bool HasFlightInput() =>
        !PhysicalKeys.IsTyping() && Read() is var keys && (keys.Move != Vector3.Zero || keys.Roll != 0f);

    public CameraState? Tick(float deltaSeconds)
    {
        if (!Enabled)
            return null;

        var typing = PhysicalKeys.IsTyping();
        var keys = typing ? default : Read();
        if (!typing)
            rotation = FreeCamMotion.Roll(rotation, keys.Roll * RollRate * deltaSeconds);
        Look();
        var input = typing ? Vector3.Zero : keys.Move;
        var speed = BaseSpeed * Speed.Multiplier * (keys.Faster ? SprintMultiplier : 1f);
        position = FreeCamMotion.Step(position, input, rotation, speed, deltaSeconds);

        return new CameraState(
            position,
            FreeCamMotion.LookAtFrom(position, rotation),
            CameraRotation.Up(rotation),
            fov
        );
    }

    /// <summary>Turns by how far the mouse moved the game's angles since last written, then puts pitch back to 0 so it never reaches the game's limits.</summary>
    private void Look()
    {
        if (CameraAccess.ReadAngles() is not { } read)
            return;

        if (written is { } last)
        {
            var yawDelta = Angles.Delta(last.Yaw, read.Yaw);
            rotation = FreeCamMotion.Turn(rotation, yawDelta, read.Pitch - last.Pitch);
        }

        CameraAccess.WriteAngles(read.Yaw, 0f);
        written = (read.Yaw, 0f);
    }

    /// <summary>The flight keys held, read from the game's key state, as Dalamud reports it; modifiers from the physical keys.</summary>
    private static FlightKeys Read() =>
        HotkeyResolver.Flight(key => Plugin.KeyState[HotkeyKeys.Virtual(key)], HotkeyKeys.HeldModifiers());
}
