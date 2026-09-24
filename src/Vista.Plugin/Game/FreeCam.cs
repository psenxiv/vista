using System.Numerics;
using Dalamud.Game.ClientState.Keys;
using Vista.Core.Camera;
using Vista.Core.Editing;

namespace Vista.Plugin.Game;

/// <summary>Flies the camera with WASD and Q/E, and rolls it with Ctrl + Q/E. Mouse-look still steers.</summary>
internal sealed class FreeCam
{
    private const float BaseSpeed = 8f;
    private const float SprintMultiplier = 4f;
    private const float RollRate = MathF.PI / 3f;

    private Vector3 position;
    private (float Yaw, float Pitch)? lastAngles;
    private float fov;
    private float roll;

    public bool Enabled { get; private set; }

    /// <summary>Where the camera is.</summary>
    public Vector3 Position
    {
        get => position;
        set => position = value;
    }

    /// <summary>Current roll in radians, positive rolls right; wrapped to within half a turn.</summary>
    public float Roll
    {
        get => roll;
        set => roll = EditLimits.Angle(value);
    }

    /// <summary>Field of view in radians, clamped to the editor's range.</summary>
    public float Fov
    {
        get => fov;
        set => fov = EditLimits.Fov(value);
    }

    /// <summary>The stepped speed setting; Shift still boosts on top.</summary>
    public FlySpeed Speed { get; } = new();

    public void Enable(Vector3 startPosition, float startRoll, float startFov)
    {
        position = startPosition;
        Roll = startRoll;
        fov = startFov;
        lastAngles = null;
        Enabled = true;
    }

    public void Disable() => Enabled = false;

    /// <summary>Forgets the last mouse-look angles, so an angle written from elsewhere is taken as-is rather than read as a mouse movement.</summary>
    public void Resync() => lastAngles = null;

    /// <summary>True when a flight or roll key is held this frame, outside text fields.</summary>
    public static bool HasFlightInput() =>
        !PhysicalKeys.IsTyping() && (ReadInput() != Vector3.Zero || ReadRoll() != 0f);

    public CameraState? Tick(float deltaSeconds)
    {
        if (!Enabled)
            return null;

        var typing = PhysicalKeys.IsTyping();
        if (!typing)
            Roll += ReadRoll() * RollRate * deltaSeconds;
        var (yaw, pitch) = LookAlongRoll(CameraAccess.ReadAngles() ?? (0f, 0f));
        var input = typing ? Vector3.Zero : ReadInput();
        var speed = BaseSpeed * Speed.Multiplier * (Plugin.KeyState[VirtualKey.SHIFT] ? SprintMultiplier : 1f);
        position = FreeCamMotion.Step(position, input, yaw, pitch, speed, deltaSeconds);

        return CameraState.FromAngles(position, yaw, pitch, Roll, fov);
    }

    /// <summary>Re-applies this frame's mouse-look change along the rolled screen and writes it back; unrolled, the game's angles stand.</summary>
    private (float Yaw, float Pitch) LookAlongRoll((float Yaw, float Pitch) read)
    {
        if (lastAngles is not { } last || Roll == 0f)
        {
            lastAngles = read;
            return read;
        }

        var yawDelta = MathF.IEEERemainder(read.Yaw - last.Yaw, MathF.Tau);
        var pitchDelta = read.Pitch - last.Pitch;
        if (yawDelta == 0f && pitchDelta == 0f)
            return last;

        var (turnYaw, turnPitch) = FreeCamMotion.RollLook(yawDelta, pitchDelta, Roll);
        var (min, max) = CameraAccess.ReadPitchLimits() ?? (-MathF.PI / 2f, MathF.PI / 2f);
        (float Yaw, float Pitch) result = (
            Angles.Wrap(last.Yaw + turnYaw),
            Math.Clamp(last.Pitch + turnPitch, min, max)
        );

        CameraAccess.WriteAngles(result.Yaw, result.Pitch);
        lastAngles = result;
        return result;
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

    /// <summary>Ctrl + Q rolls left, Ctrl + E rolls right; without Ctrl, Q and E fly down and up.</summary>
    private static float ReadRoll() =>
        PhysicalKeys.IsDown(VirtualKey.CONTROL)
            ? (Plugin.KeyState[VirtualKey.E] ? 1f : 0f) - (Plugin.KeyState[VirtualKey.Q] ? 1f : 0f)
            : 0f;
}
