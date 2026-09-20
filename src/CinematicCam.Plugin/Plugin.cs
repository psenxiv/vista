using System.Numerics;
using CinematicCam.Core;
using CinematicCam.Plugin.Game;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CinematicCam.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/ccam";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Hooks { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;

    internal static CameraController Camera { get; private set; } = null!;
    internal static CameraState? TestState { get; set; }

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/ccam selftest | hold | nudge <x> <y> <z> | release"
        });

        Camera = new CameraController(() => TestState);

        Log.Information("CinematicCam loaded. Build {Build}.", typeof(Plugin).Assembly.GetName().Version);
    }

    private void OnCommand(string command, string args)
    {
        var verb = args.Trim().Split(' ', 2)[0].ToLowerInvariant();
        switch (verb)
        {
            case "selftest":
                SelfTest.Run();
                break;
            case "hold":
            {
                var current = CameraAccess.ReadState();
                if (current is null) { Log.Error("[ccam] cannot read camera state."); break; }
                TestState = current;
                Log.Information("[ccam] holding at {Pos} looking at {Look}",
                    current.Value.Position, current.Value.LookAt);
                break;
            }
            case "release":
                TestState = null;
                Log.Information("[ccam] released.");
                break;
            case "nudge":
            {
                if (TestState is null) { Log.Error("[ccam] nudge requires /ccam hold first."); break; }
                var parts = args.Trim().Split(' ');
                if (parts.Length < 4
                    || !float.TryParse(parts[1], out var dx)
                    || !float.TryParse(parts[2], out var dy)
                    || !float.TryParse(parts[3], out var dz))
                {
                    Log.Error("[ccam] usage: /ccam nudge <dx> <dy> <dz>");
                    break;
                }
                var s = TestState.Value;
                var delta = new Vector3(dx, dy, dz);
                TestState = s with { Position = s.Position + delta, LookAt = s.LookAt + delta };
                Log.Information("[ccam] nudged to {Pos}", TestState.Value.Position);
                break;
            }
            default:
                Log.Information("[ccam] unknown verb '{Verb}'.", verb);
                break;
        }
    }

    public void Dispose()
    {
        Camera.Dispose();
        CommandManager.RemoveHandler(CommandName);
        Log.Information("CinematicCam unloaded.");
    }
}
