using System.Numerics;
using CinematicCam.Plugin.Game;
using CinematicCam.Plugin.Session;
using CinematicCam.Plugin.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
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

    private readonly WindowSystem windows = new("CinematicCam");
    private float wheel;
    private bool escapeWasDown;
    private static bool blockEscape;
    private readonly TestWindow testWindow;

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/ccam opens the test window | release | hold | push <d> | nudge <x> <y> <z> | reset"
        });

        Movement = new MovementLock();
        Session = new CameraSession(Movement);
        testWindow = new TestWindow(Session);
        windows.AddWindow(testWindow);
        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenMainUi += OpenTestWindow;
        Camera = new CameraController(() => Session.Frame((float)Framework.UpdateDelta.TotalSeconds));
        Input = new InputBlocker(() => Session.LocksInput, () => blockEscape);

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
            case "":
                OpenTestWindow();
                break;
            case "reset":
                CameraAccess.ResetToDefaults();
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

        // Escape while live brings back a UI we hid, so nobody needs a Toggle UI key bound.
        // The game's own Escape handling is held off while we hide its UI, and until that
        // Escape is released, so it does not also open the system menu.
        var escape = KeyState[VirtualKey.ESCAPE];
        if (escape && !escapeWasDown)
        {
            Log.Information("[ui] escape pressed: mode {Mode}, hidden by us {Hidden}", Session.Mode, GameUi.HiddenByUs);
            if (Session.Mode == CameraMode.Live) GameUi.Restore();
        }
        escapeWasDown = escape;
        blockEscape = GameUi.HiddenByUs || (blockEscape && escape);

        if (!Session.OwnsCamera) return;

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

    private void OpenTestWindow() => testWindow.IsOpen = true;

    /// <summary>Steps fly speed with the scroll wheel while editing, then draws the windows.</summary>
    private void OnDraw()
    {
        var io = ImGui.GetIO();
        if (Session.Mode == CameraMode.Editing && !io.WantCaptureMouse)
        {
            wheel += io.MouseWheel;
            var steps = (int)wheel;
            if (steps != 0)
            {
                Session.Speed.Step(steps);
                wheel -= steps;
            }
        }
        else
        {
            wheel = 0f;
        }

        windows.Draw();
    }

    private void OnTerritoryChanged(uint territory)
        => Session.Release($"zone change to {territory}");

    private void OnLogout(int type, int code)
        => Session.Release("logout");

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenTestWindow;
        windows.RemoveAllWindows();
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
