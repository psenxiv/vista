using System.Numerics;
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
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;

    internal static CameraController Camera { get; private set; } = null!;
    internal static InputBlocker Input { get; private set; } = null!;
    internal static MovementLock Movement { get; private set; } = null!;
    internal static CameraSession Session { get; private set; } = null!;

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/ccam fly | selftest | hold | push <d> | nudge <x> <y> <z> | release | reset"
        });

        Movement = new MovementLock();
        Session = new CameraSession(Movement);
        Camera = new CameraController(() => Session.Frame((float)Framework.UpdateDelta.TotalSeconds));
        Input = new InputBlocker(() => Session.LocksInput);

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
            case "reset":
                CameraAccess.ResetToDefaults();
                break;
            case "fly":
                if (Session.Mode == CameraMode.Editing) Session.Release("fly toggled off");
                else Session.Edit();
                break;
            case "selftest":
                SelfTest.Run();
                break;
            case "hold":
            {
                var current = CameraAccess.ReadState();
                if (current is null) { Log.Error("[ccam] cannot read camera state."); break; }
                Session.Hold(current.Value);
                Log.Information("[ccam] holding at {Pos} looking at {Look}",
                    current.Value.Position, current.Value.LookAt);
                break;
            }
            case "release":
                Session.Release("command");
                break;
            case "push":
            {
                if (Session.TestState is null) { Log.Error("[ccam] push requires /ccam hold first."); break; }
                var parts = args.Trim().Split(' ');
                if (parts.Length < 2 || !float.TryParse(parts[1], out var distance))
                {
                    Log.Error("[ccam] usage: /ccam push <distance>");
                    break;
                }
                var st = Session.TestState.Value;
                var forward = Vector3.Normalize(st.LookAt - st.Position);
                var step = forward * distance;
                Session.TestState = st with { Position = st.Position + step, LookAt = st.LookAt + step };
                Log.Information("[ccam] pushed {Distance} along view to {Pos}", distance, Session.TestState.Value.Position);
                break;
            }
            case "nudge":
            {
                if (Session.TestState is null) { Log.Error("[ccam] nudge requires /ccam hold first."); break; }
                var parts = args.Trim().Split(' ');
                if (parts.Length < 4
                    || !float.TryParse(parts[1], out var dx)
                    || !float.TryParse(parts[2], out var dy)
                    || !float.TryParse(parts[3], out var dz))
                {
                    Log.Error("[ccam] usage: /ccam nudge <dx> <dy> <dz>");
                    break;
                }
                var s = Session.TestState.Value;
                var delta = new Vector3(dx, dy, dz);
                Session.TestState = s with { Position = s.Position + delta, LookAt = s.LookAt + delta };
                Log.Information("[ccam] nudged to {Pos}", Session.TestState.Value.Position);
                break;
            }
            default:
                Log.Information("[ccam] unknown verb '{Verb}'.", verb);
                break;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Camera.TryInstallHook();
        Input.SyncHookState();

        if (!Session.Ownership.IsOwned) return;

        // TerritoryChanged misses transitions that keep the same territory id, such as an
        // aethernet hop, a cutscene or a duty starting. This flag covers all of them.
        if (Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51])
        {
            Session.Release("area transition");
            return;
        }

        // Someone else reset the shared counter. Stop tracking our hold so we never
        // decrement theirs, but keep flying: dropping a shot mid-take is worse.
        if (Movement.Held && Movement.Count == 0) Movement.Forget();
    }

    private void OnTerritoryChanged(uint territory)
        => Session.Release($"zone change to {territory}");

    private void OnLogout(int type, int code)
        => Session.Release("logout");

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        ClientState.Logout -= OnLogout;
        Session?.Release("plugin unload");
        Movement?.Dispose();
        Input?.Dispose();
        Camera?.Dispose();
        CommandManager.RemoveHandler(CommandName);
        Log.Information("CinematicCam unloaded.");
    }
}
