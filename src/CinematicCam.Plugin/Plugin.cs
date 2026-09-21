using System.Linq;
using CinematicCam.Core.Session;
using CinematicCam.Plugin.Game;
using CinematicCam.Plugin.Probes;
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
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;

    internal static CameraController Camera { get; private set; } = null!;
    internal static InputBlocker Input { get; private set; } = null!;
    internal static MovementLock Movement { get; private set; } = null!;
    internal static CameraSession Session { get; private set; } = null!;

    private readonly WindowSystem windows = new("CinematicCam");
    private float wheel;
    private bool escapeWasDown;
    private static bool blockEscape;
    private readonly TestWindow testWindow;
    private readonly GizmoProbe gizmoProbe = new();
    private readonly AimProbe aimProbe = new();

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/ccam opens the test window | release | probe gizmo|aim|input"
        });

        Movement = new MovementLock();
        Session = new CameraSession(Movement);
        testWindow = new TestWindow(Session);
        windows.AddWindow(testWindow);
        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.DisableGposeUiHide = true;
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
            case "release":
                Session.Release("command");
                break;
            case "probe":
                OnProbe(args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray());
                break;
            default:
                Log.Information("[ccam] unknown verb '{Verb}'.", verb);
                break;
        }
    }

    /// <summary>Temporary in-game probes for phase 2c-1; removed once they have answered.</summary>
    private void OnProbe(string[] words)
    {
        switch (words.FirstOrDefault())
        {
            case "gizmo":
                gizmoProbe.Toggle(words.ElementAtOrDefault(1) ?? "");
                break;
            case "aim":
                aimProbe.Run(Session.Mode, words.Skip(1).ToArray());
                break;
            default:
                Log.Information("[probe] usage: /ccam probe gizmo [game|ours] | aim <yaw> <pitch> | input");
                break;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Camera.TryInstallHook();
        Input.SyncHookState();

        if (Camera.Faulted)
        {
            Session.Release("hook error");
            Camera.ClearFault();
        }

        aimProbe.Update();

        // Escape while live brings back a UI we hid, so nobody needs a Toggle UI key bound.
        // The game's own Escape handling is held off while we hide its UI, and until that
        // Escape is released, so it does not also open the system menu.
        var escape = KeyState[VirtualKey.ESCAPE];
        if (escape && !escapeWasDown && Session.Mode == CameraMode.Live) GameUi.Restore();
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

        gizmoProbe.Draw(Session.LastFrame);
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
