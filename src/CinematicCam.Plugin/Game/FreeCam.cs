using System.Numerics;
using CinematicCam.Core.Camera;
using Dalamud.Game.ClientState.Keys;

namespace CinematicCam.Plugin.Game;

/// <summary>Flies the camera with WASD, space and C, and rolls it with Q and E. Mouse-look still steers.</summary>
internal sealed class FreeCam
{
    private const float BaseSpeed = 8f;
    private const float SprintMultiplier = 4f;
    private const float RollRate = MathF.PI / 3f;

    private Vector3 position;
    private (float Yaw, float Pitch)? lastAngles;

    public bool Enabled { get; private set; }

    /// <summary>Current roll in radians, positive rolls right.</summary>
    public float Roll { get; private set; }

    /// <summary>The stepped speed setting; Shift still boosts on top.</summary>
    public FlySpeed Speed { get; } = new();

    public void Enable(Vector3 startPosition, float startRoll = 0f)
    {
        position = startPosition;
        Roll = startRoll;
        lastAngles = null;
        Enabled = true;
    }

    public void Disable() => Enabled = false;

    public CameraState? Tick(float deltaSeconds)
    {
        if (!Enabled) return null;

        var typing = PhysicalKeys.IsTyping();
        if (!typing) Roll = Wrap(Roll + (ReadRoll() * RollRate * deltaSeconds));
        var (yaw, pitch) = LookAlongRoll(CameraAccess.ReadAngles() ?? (0f, 0f));
        var input = typing ? Vector3.Zero : ReadInput();
        var speed = BaseSpeed * Speed.Multiplier * (Plugin.KeyState[VirtualKey.SHIFT] ? SprintMultiplier : 1f);
        position = FreeCamMotion.Step(position, input, yaw, pitch, speed, deltaSeconds);

        return new CameraState(
            position,
            FreeCamMotion.LookAtFrom(position, yaw, pitch),
            CameraAccess.ReadState()?.Fov ?? 0.78f,
            Roll);
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
        if (yawDelta == 0f && pitchDelta == 0f) return last;

        var (turnYaw, turnPitch) = FreeCamMotion.RollLook(yawDelta, pitchDelta, Roll);
        var (min, max) = CameraAccess.ReadPitchLimits() ?? (-MathF.PI / 2f, MathF.PI / 2f);
        (float Yaw, float Pitch) result = (Wrap(last.Yaw + turnYaw), Math.Clamp(last.Pitch + turnPitch, min, max));

        CameraAccess.WriteAngles(result.Yaw, result.Pitch);
        lastAngles = result;
        return result;
    }

    private static Vector3 ReadInput()
    {
        var forward = 0f;
        var up = 0f;
        var right = 0f;

        if (Plugin.KeyState[VirtualKey.W]) forward += 1f;
        if (Plugin.KeyState[VirtualKey.S]) forward -= 1f;
        if (Plugin.KeyState[VirtualKey.D]) right += 1f;
        if (Plugin.KeyState[VirtualKey.A]) right -= 1f;
        if (Plugin.KeyState[VirtualKey.SPACE]) up += 1f;
        if (PhysicalKeys.IsDown(VirtualKey.C)) up -= 1f;

        return new Vector3(forward, up, right);
    }

    /// <summary>Q rolls left, E rolls right.</summary>
    private static float ReadRoll()
        => (Plugin.KeyState[VirtualKey.E] ? 1f : 0f) - (Plugin.KeyState[VirtualKey.Q] ? 1f : 0f);

    /// <summary>Keeps an angle within one turn of zero.</summary>
    private static float Wrap(float angle) => MathF.IEEERemainder(angle, 2f * MathF.PI);
}
