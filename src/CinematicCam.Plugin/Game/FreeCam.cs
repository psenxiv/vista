using System.Numerics;
using CinematicCam.Core.Camera;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace CinematicCam.Plugin.Game;

/// <summary>Flies the camera with WASD, space and ctrl. Mouse-look still steers.</summary>
internal sealed class FreeCam
{
    private const float BaseSpeed = 8f;
    private const float SprintMultiplier = 4f;

    private Vector3 position;

    public bool Enabled { get; private set; }

    public void Enable(Vector3 startPosition)
    {
        position = startPosition;
        Enabled = true;
        Plugin.Log.Information("[freecam] enabled at {Pos}", position);
    }

    public void Disable()
    {
        if (!Enabled) return;
        Enabled = false;
        Plugin.Log.Information("[freecam] disabled");
    }

    public CameraState? Tick(float deltaSeconds)
    {
        if (!Enabled) return null;

        var (yaw, pitch) = CameraAccess.ReadAngles() ?? (0f, 0f);
        var input = IsTyping() ? Vector3.Zero : ReadInput();
        var speed = BaseSpeed * (Plugin.KeyState[VirtualKey.SHIFT] ? SprintMultiplier : 1f);
        position = FreeCamMotion.Step(position, input, yaw, pitch, speed, deltaSeconds);

        return new CameraState(
            position,
            FreeCamMotion.LookAtFrom(position, yaw, pitch),
            CameraAccess.ReadState()?.Fov ?? 0.78f);
    }

    /// <summary>True while the player is typing, so chat does not fly the camera.</summary>
    private static unsafe bool IsTyping()
    {
        var module = RaptureAtkModule.Instance();
        return module != null && module->IsTextInputActive();
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
        // Under Wine, Cmd and Ctrl are indistinguishable; the game does not separate them either.
        if (Plugin.KeyState[VirtualKey.CONTROL]) up -= 1f;

        return new Vector3(forward, up, right);
    }
}
