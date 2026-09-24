using System.Numerics;
using Dalamud.Game.ClientState.Keys;
using Vista.Core.Camera;
using Vista.Core.Editing;

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
        set => fov = EditLimits.Fov(value);
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
        !PhysicalKeys.IsTyping() && (ReadInput() != Vector3.Zero || ReadRoll() != 0f);

    public CameraState? Tick(float deltaSeconds)
    {
        if (!Enabled)
            return null;

        var typing = PhysicalKeys.IsTyping();
        if (!typing)
            rotation = FreeCamMotion.Roll(rotation, ReadRoll() * RollRate * deltaSeconds);
        Look();
        var input = typing ? Vector3.Zero : ReadInput();
        var speed = BaseSpeed * Speed.Multiplier * (Plugin.KeyState[VirtualKey.SHIFT] ? SprintMultiplier : 1f);
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
            var yawDelta = MathF.IEEERemainder(read.Yaw - last.Yaw, MathF.Tau);
            rotation = FreeCamMotion.Turn(rotation, yawDelta, read.Pitch - last.Pitch);
        }

        CameraAccess.WriteAngles(read.Yaw, 0f);
        written = (read.Yaw, 0f);
    }

    private static Vector3 ReadInput()
    {
        var forward = 0f;
        var up = 0f;
        var right = 0f;

        if (Plugin.KeyState[VirtualKey.W])
            forward += 1f;
        if (Plugin.KeyState[VirtualKey.S])
            forward -= 1f;
        if (Plugin.KeyState[VirtualKey.D])
            right += 1f;
        if (Plugin.KeyState[VirtualKey.A])
            right -= 1f;
        if (!PhysicalKeys.IsDown(VirtualKey.CONTROL))
        {
            if (Plugin.KeyState[VirtualKey.E])
                up += 1f;
            if (Plugin.KeyState[VirtualKey.Q])
                up -= 1f;
        }

        return new Vector3(forward, up, right);
    }

    /// <summary>Ctrl + Q rolls left, Ctrl + E rolls right; without Ctrl, Q and E fly down and up the picture.</summary>
    private static float ReadRoll() =>
        PhysicalKeys.IsDown(VirtualKey.CONTROL)
            ? (Plugin.KeyState[VirtualKey.E] ? 1f : 0f) - (Plugin.KeyState[VirtualKey.Q] ? 1f : 0f)
            : 0f;
}
