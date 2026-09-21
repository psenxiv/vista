using System.Numerics;
using CinematicCam.Core;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace CinematicCam.Plugin.Game;

/// <summary>Flies the camera with the player's movement binds. Mouse-look still steers.</summary>
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

        var (yaw, pitch) = ReadCameraAngles();
        var input = IsTyping() ? Vector3.Zero : ReadInput();
        var speed = BaseSpeed * (Plugin.KeyState[VirtualKey.SHIFT] ? SprintMultiplier : 1f);
        position = FreeCamMotion.Step(position, input, yaw, pitch, speed, deltaSeconds);

        return new CameraState(
            position,
            FreeCamMotion.LookAtFrom(position, yaw, pitch),
            CameraAccess.ReadState()?.Fov ?? 0.78f);
    }

    /// <summary>True while the player is typing, so chat does not fly the camera.</summary>
    internal static unsafe bool IsTyping()
    {
        var module = RaptureAtkModule.Instance();
        return module != null && module->IsTextInputActive();
    }

    /// <summary>Reads the player's own movement binds rather than fixed keys.</summary>
    private static Vector3 ReadInput()
    {
        var input = Plugin.Input;
        var forward = 0f;
        var up = 0f;
        var right = 0f;

        if (input.IsDown(InputId.MOVE_FORE)) forward += 1f;
        if (input.IsDown(InputId.MOVE_BACK)) forward -= 1f;

        // Turn and strafe binds both move sideways; a free cam has nothing to turn.
        if (input.IsDown(InputId.MOVE_RIGHT) || input.IsDown(InputId.MOVE_STRIFE_R)) right += 1f;
        if (input.IsDown(InputId.MOVE_LEFT) || input.IsDown(InputId.MOVE_STRIFE_L)) right -= 1f;

        if (input.IsDown(InputId.JUMP) || input.IsDown(InputId.MOVE_RETENTION)) up += 1f;
        if (input.IsDown(InputId.MOVE_DESCENT)) up -= 1f;

        return new Vector3(forward, up, right);
    }

    private static unsafe (float Yaw, float Pitch) ReadCameraAngles()
    {
        if (!CameraAccess.TryGetWorldCamera(out var camera)) return (0f, 0f);
        return (camera->DirH, camera->DirV);
    }
}
