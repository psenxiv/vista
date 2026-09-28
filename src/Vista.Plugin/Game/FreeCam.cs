using System.Numerics;
using Dalamud.Game.ClientState.Keys;
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

    private static readonly VirtualKey ForwardKey = HotkeyKeys.Virtual(HotkeyTable.FlyForward.Hotkey.Key);
    private static readonly VirtualKey BackKey = HotkeyKeys.Virtual(HotkeyTable.FlyBack.Hotkey.Key);
    private static readonly VirtualKey LeftKey = HotkeyKeys.Virtual(HotkeyTable.FlyLeft.Hotkey.Key);
    private static readonly VirtualKey RightKey = HotkeyKeys.Virtual(HotkeyTable.FlyRight.Hotkey.Key);

    // Also RollRight's key (Ctrl + E); RollLeft's key below is also FlyDown's (Ctrl + Q).
    private static readonly VirtualKey UpKey = HotkeyKeys.Virtual(HotkeyTable.FlyUp.Hotkey.Key);
    private static readonly VirtualKey DownKey = HotkeyKeys.Virtual(HotkeyTable.FlyDown.Hotkey.Key);
    private static readonly VirtualKey SprintKey = HotkeyKeys.Virtual(HotkeyTable.FlyFaster.Hotkey.Key);

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
        var speed = BaseSpeed * Speed.Multiplier * (Plugin.KeyState[SprintKey] ? SprintMultiplier : 1f);
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

    private static Vector3 ReadInput()
    {
        var forward = 0f;
        var up = 0f;
        var right = 0f;

        if (Plugin.KeyState[ForwardKey])
            forward += 1f;
        if (Plugin.KeyState[BackKey])
            forward -= 1f;
        if (Plugin.KeyState[RightKey])
            right += 1f;
        if (Plugin.KeyState[LeftKey])
            right -= 1f;
        if (!PhysicalKeys.IsDown(HotkeyKeys.Ctrl))
        {
            if (Plugin.KeyState[UpKey])
                up += 1f;
            if (Plugin.KeyState[DownKey])
                up -= 1f;
        }

        return new Vector3(forward, up, right);
    }

    /// <summary>Ctrl + Q rolls left, Ctrl + E rolls right; without Ctrl, Q and E fly down and up the picture.</summary>
    private static float ReadRoll() =>
        PhysicalKeys.IsDown(HotkeyKeys.Ctrl)
            ? (Plugin.KeyState[UpKey] ? 1f : 0f) - (Plugin.KeyState[DownKey] ? 1f : 0f)
            : 0f;
}
