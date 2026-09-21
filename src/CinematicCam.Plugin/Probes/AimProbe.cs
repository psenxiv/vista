using CinematicCam.Core.Session;
using CinematicCam.Plugin.Game;

namespace CinematicCam.Plugin.Probes;

/// <summary>Probe 2: writes DirH and DirV once and logs whether the game keeps them.</summary>
internal sealed class AimProbe
{
    private int framesToLog;

    /// <summary>Writes the angles in degrees while editing, then logs them for two frames.</summary>
    public void Run(CameraMode mode, string[] words)
    {
        if (mode != CameraMode.Editing) { Plugin.Log.Error("[probe] aim needs editing mode."); return; }
        if (words.Length < 2 || !float.TryParse(words[0], out var yawDeg) || !float.TryParse(words[1], out var pitchDeg))
        {
            Plugin.Log.Error("[probe] usage: /ccam probe aim <yawDegrees> <pitchDegrees>");
            return;
        }

        Plugin.Log.Information("[probe] aim before {Angles}", CameraAccess.ReadAngles()?.ToString() ?? "none");
        CameraAccess.WriteAngles(yawDeg * MathF.PI / 180f, pitchDeg * MathF.PI / 180f);
        Plugin.Log.Information("[probe] aim after write {Angles}", CameraAccess.ReadAngles()?.ToString() ?? "none");
        framesToLog = 2;
    }

    /// <summary>Logs the angles on the frames after a write. Call from Framework.Update.</summary>
    public void Update()
    {
        if (framesToLog == 0) return;
        framesToLog--;
        Plugin.Log.Information("[probe] aim next frame {Angles}", CameraAccess.ReadAngles()?.ToString() ?? "none");
    }
}
