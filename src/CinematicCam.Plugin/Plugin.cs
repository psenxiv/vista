using System.Numerics;
using CinematicCam.Core;
using CinematicCam.Plugin.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
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
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    internal static CameraController Camera { get; private set; } = null!;
    internal static CameraState? TestState { get; set; }
    internal static CameraOwnership Ownership { get; } = new();
    internal static FreeCam FreeCamera { get; } = new();
    internal static InputBlocker Input { get; private set; } = null!;
    private static CameraAccess.Snapshot? snapshotBeforeTakeover;

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/ccam fly | selftest | hold | push <d> | nudge <x> <y> <z> | release"
        });

        Camera = new CameraController(() =>
        {
            if (!Ownership.IsOwned) return null;
            return FreeCamera.Tick((float)Framework.UpdateDelta.TotalSeconds) ?? TestState;
        });

        Input = new InputBlocker(() => FreeCamera.Enabled);

        Framework.Update += OnFrameworkUpdate;
        ClientState.TerritoryChanged += OnTerritoryChanged;
        ClientState.Logout += OnLogout;

        Log.Information("CinematicCam loaded. Build {Build}.", typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown");
    }

    private void OnCommand(string command, string args)
    {
        var verb = args.Trim().Split(' ', 2)[0].ToLowerInvariant();
        switch (verb)
        {
            case "inputprobe":
            {
                var starting = !Input.Learning;
                Input.Learning = starting;

                if (starting)
                {
                    Log.Information("[input] learning ON - press one key at a time");
                }
                else
                {
                    Input.BlockWhatWasLearned();
                    Log.Information("[input] learning OFF");
                }
                break;
            }
            case "reset":
                CameraAccess.ResetToDefaults();
                break;
            case "inputclear":
                Input.ClearBlocked();
                break;
            case "fly":
            {
                if (FreeCamera.Enabled) { ReleaseCamera("fly toggled off"); break; }

                var start = CameraAccess.ReadState();
                if (start is null) { Log.Error("[ccam] cannot read camera state."); break; }

                FreeCamera.Enable(start.Value.Position);
                TakeCamera();
                break;
            }
            case "selftest":
                SelfTest.Run();
                break;
            case "hold":
            {
                var current = CameraAccess.ReadState();
                if (current is null) { Log.Error("[ccam] cannot read camera state."); break; }
                TestState = current;
                TakeCamera();
                Log.Information("[ccam] holding at {Pos} looking at {Look}",
                    current.Value.Position, current.Value.LookAt);
                break;
            }
            case "release":
                ReleaseCamera("command");
                break;
            case "push":
            {
                if (TestState is null) { Log.Error("[ccam] push requires /ccam hold first."); break; }
                var parts = args.Trim().Split(' ');
                if (parts.Length < 2 || !float.TryParse(parts[1], out var distance))
                {
                    Log.Error("[ccam] usage: /ccam push <distance>");
                    break;
                }
                var st = TestState.Value;
                var forward = Vector3.Normalize(st.LookAt - st.Position);
                var step = forward * distance;
                TestState = st with { Position = st.Position + step, LookAt = st.LookAt + step };
                Log.Information("[ccam] pushed {Distance} along view to {Pos}", distance, TestState.Value.Position);
                break;
            }
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

    /// <summary>Takes the camera, remembering what to put back on release.</summary>
    private static void TakeCamera()
    {
        snapshotBeforeTakeover ??= CameraAccess.Capture();
        Ownership.Take();
    }

    private static void ReleaseCamera(string reason)
    {
        if (!Ownership.IsOwned && TestState is null) return;

        FreeCamera.Disable();
        TestState = null;
        Ownership.Release(reason);

        // Without this the game carries on from our values rather than its own,
        // which leaves the camera wrong long after we stop writing.
        if (snapshotBeforeTakeover is { } snapshot)
        {
            CameraAccess.Restore(snapshot);
            snapshotBeforeTakeover = null;
        }

        Log.Information("[ccam] camera released: {Reason}", reason);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Camera.TryInstallHook();
        Input.SyncHookState();

        if (!Ownership.IsOwned) return;

        // TerritoryChanged misses transitions that keep the same territory id, such as an
        // aethernet hop, a cutscene or a duty starting. This flag covers all of them.
        if (Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51])
            ReleaseCamera("area transition");
    }

    private void OnTerritoryChanged(uint territory)
        => ReleaseCamera($"zone change to {territory}");

    private void OnLogout(int type, int code)
        => ReleaseCamera("logout");

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        ClientState.Logout -= OnLogout;
        ReleaseCamera("plugin unload");
        Input?.Dispose();
        Camera?.Dispose();
        CommandManager.RemoveHandler(CommandName);
        Log.Information("CinematicCam unloaded.");
    }
}
