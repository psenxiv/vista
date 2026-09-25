using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Vista.Core.Session;
using Vista.Plugin.Editor;
using Vista.Plugin.Game;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Main;
using Vista.Plugin.Ui.Widgets;
using Vista.Plugin.Ui.Windows;

namespace Vista.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/vista";

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IGameInteropProvider Hooks { get; private set; } = null!;

    [PluginService]
    internal static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static IKeyState KeyState { get; private set; } = null!;

    [PluginService]
    internal static ICondition Condition { get; private set; } = null!;

    [PluginService]
    internal static ISigScanner SigScanner { get; private set; } = null!;

    [PluginService]
    internal static INotificationManager Notifications { get; private set; } = null!;

    internal static CameraController Camera { get; private set; } = null!;
    internal static InputBlocker Input { get; private set; } = null!;
    internal static MovementLock Movement { get; private set; } = null!;

    private readonly GameSession game;
    private readonly Faults faults;
    private readonly SceneFiles sceneFiles;
    private readonly PendingField fields;
    private readonly EditorKeys editorKeys = new();
    private readonly PointGizmo pointGizmo = new();
    private readonly EditorLayer editorLayer;

    private readonly WindowSystem windows = new("Vista");
    private readonly SetupWindow setupWindow;
    private readonly TrackEditorWindow trackEditor;
    private readonly PointWindow pointWindow;
    private readonly TimingWindow timingWindow;
    private readonly CameraWindow cameraWindow;
    private readonly GuideWindow guideWindow;
    private readonly WatchTargetWindow watchTargetWindow;
    private readonly FollowTargetWindow followTargetWindow;

    private float wheel;
    private bool escapeWasDown;
    private bool cameraHookChecked;
    private static bool blockEscape;

    public Plugin()
    {
        var config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        Movement = new MovementLock();
        game = new GameSession(config, Movement);
        faults = new Faults(() => game.State.Mode);
        Input = new InputBlocker(() => game.State.LocksInput, () => blockEscape, faults);
        sceneFiles = new SceneFiles(config, game);
        fields = new PendingField(() => game.State.Mode == CameraMode.Editing);
        editorLayer = new EditorLayer(game, pointGizmo);

        setupWindow = new SetupWindow(sceneFiles, OpenTrackEditor);
        pointWindow = new PointWindow(game, pointGizmo);
        timingWindow = new TimingWindow(game);
        cameraWindow = new CameraWindow(game);
        guideWindow = new GuideWindow(PluginInterface.UiBuilder.FontAtlas);
        watchTargetWindow = new WatchTargetWindow(game.State, game.Characters);
        followTargetWindow = new FollowTargetWindow(game.State, game.Characters);
        trackEditor = new TrackEditorWindow(
            game,
            config,
            fields,
            timingWindow,
            cameraWindow,
            guideWindow,
            watchTargetWindow,
            followTargetWindow,
            sceneFiles,
            setupWindow
        );

        windows.AddWindow(trackEditor);
        windows.AddWindow(pointWindow);
        windows.AddWindow(timingWindow);
        windows.AddWindow(cameraWindow);
        windows.AddWindow(guideWindow);
        windows.AddWindow(watchTargetWindow);
        windows.AddWindow(followTargetWindow);
        windows.AddWindow(new WelcomeWindow(config, OpenTrackEditor));
        windows.AddWindow(setupWindow);

        sceneFiles.SetupNeeded += (_, _) => setupWindow.IsOpen = true;
        if (sceneFiles.Lost)
            setupWindow.IsOpen = true;

        Camera = new CameraController(() => game.Frame((float)Framework.UpdateDelta.TotalSeconds), faults);
        CheckTouchPointsAtLoad();
        CheckCameraHook();

        PluginInterface.UiBuilder.DisableGposeUiHide = true;
        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenMainUi += OpenTrackEditor;
        Framework.Update += OnFrameworkUpdate;
        ClientState.Logout += OnLogout;
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { HelpMessage = "/vista opens the editor" });

        Log.Information(
            "Vista loaded. Build {Build}.",
            typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown"
        );
    }

    private void OnCommand(string command, string args) => faults.Guard("the /vista command", () => RunCommand(args));

    private void RunCommand(string args)
    {
        var verb = args.Trim().Split(' ', 2)[0].ToLowerInvariant();
        switch (verb)
        {
            case "":
                OpenTrackEditor();
                break;
            case "release":
                game.Release("command");
                break;
            default:
                Log.Information("[vista] unknown verb '{Verb}'.", verb);
                break;
        }
    }

    /// <summary>The safety steps first, each on its own, so a fault in one never skips another; then everything else.</summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        faults.Guard("stopping after a fault", StopAfterFaults);
        faults.Guard("releasing on an area transition", ReleaseBetweenAreas);
        faults.Guard("syncing the input hooks", Input.SyncHookState);
        faults.Guard("watching the movement counter", NoticeCounterCleared);
        faults.Guard("the framework update", UpdateFeatures);
    }

    /// <summary>Releases the camera and stops Vista for every fault waiting, telling the player the first time Vista stops.</summary>
    private void StopAfterFaults()
    {
        while (faults.TryTake(out var fault))
        {
            game.Release($"fault in {fault.Where}");
            if (game.State.ReportFault(fault.Where))
                AnnounceStop(fault.Notifies);
        }
    }

    /// <summary>Logs each touch point checked at load, and stops Vista if any failed.</summary>
    private void CheckTouchPointsAtLoad()
    {
        (string Name, bool Passed)[] checks =
        [
            ("input query hooks", Input.QueriesHooked),
            ("mouse wheel hook", Input.WheelHooked),
            ("movement lock", Movement.Available),
        ];
        Log.Information(
            "[vista] touch points: {Results}",
            string.Join(", ", checks.Select(c => $"{c.Name} {Result(c.Passed)}"))
        );
        foreach (var (name, passed) in checks)
            CheckTouchPoint(name, passed);
    }

    /// <summary>Once the camera hook has been tried, logs whether it installed, and stops Vista if not.</summary>
    private void CheckCameraHook()
    {
        if (cameraHookChecked || Camera.Hooked is not { } hooked)
            return;
        cameraHookChecked = true;
        Log.Information("[vista] touch points: camera update hook {Result}", Result(hooked));
        CheckTouchPoint("camera update hook", hooked);
    }

    private static string Result(bool passed) => passed ? "ok" : "unavailable";

    /// <summary>Reports a touch point's check to the session, releasing and telling the player if it stops Vista.</summary>
    private void CheckTouchPoint(string name, bool passed)
    {
        if (!game.State.ReportTouchPoint(name, passed))
            return;
        game.Release($"{name} unavailable");
        AnnounceStop(notify: true);
    }

    /// <summary>Logs why Vista stopped and, unless Dalamud already tells the player, shows the stop message.</summary>
    private void AnnounceStop(bool notify)
    {
        Log.Error("[vista] stopped: {Reason}", game.State.StopReason ?? "unknown");
        if (notify)
            Notifications.AddNotification(
                new Notification
                {
                    Title = "Vista",
                    Content = SessionState.StopMessage,
                    Type = NotificationType.Error,
                }
            );
    }

    /// <summary>Covers every transition: a teleport, an aethernet hop, a cutscene, a duty starting.</summary>
    private void ReleaseBetweenAreas()
    {
        if (game.OwnsCamera && (Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51]))
            game.Release("area transition");
    }

    /// <summary>Someone else reset the shared counter. Stop tracking our hold so we never decrement theirs, but keep flying: dropping a shot mid-take is worse.</summary>
    private void NoticeCounterCleared()
    {
        if (game.OwnsCamera && Movement.Held && Movement.Count == 0)
            Movement.Forget();
    }

    private void UpdateFeatures()
    {
        Camera.TryInstallHook();
        CheckCameraHook();
        sceneFiles.Tick();
        editorKeys.Update(game, pointGizmo, editorLayer);
        game.RefreshCharacters();

        // Escape while live brings back a UI we hid, so nobody needs a Toggle UI key bound.
        // The game's own Escape handling is held off while we hide its UI, and until that
        // Escape is released, so it does not also open the system menu.
        var escape = KeyState[VirtualKey.ESCAPE];
        if (escape && !escapeWasDown && game.State.Mode == CameraMode.Live)
            GameUi.Restore();
        escapeWasDown = escape;
        blockEscape = GameUi.HiddenByUs || (blockEscape && escape);
    }

    /// <summary>Opens the Vista window, or Setup in its place while there is no save folder.</summary>
    private void OpenTrackEditor()
    {
        if (sceneFiles.Ready)
            trackEditor.IsOpen = true;
        else
            setupWindow.IsOpen = true;
    }

    /// <summary>Draws, recording a fault and passing it on so Dalamud still shows its own error window.</summary>
    private void OnDraw()
    {
        try
        {
            Draw();
        }
        catch (Exception ex)
        {
            faults.Record("drawing", ex, notifies: false);
            throw;
        }
    }

    /// <summary>Steps fly speed with the scroll wheel while editing, then draws the windows.</summary>
    private void Draw()
    {
        var io = ImGui.GetIO();
        if (game.State.Mode == CameraMode.Editing && !io.WantCaptureMouse)
        {
            wheel += io.MouseWheel;
            var steps = (int)wheel;
            if (steps != 0)
            {
                game.State.Transport.StopPreview();
                game.Speed.Step(steps);
                wheel -= steps;
            }
        }
        else
        {
            wheel = 0f;
        }

        editorLayer.Draw();
        windows.Draw();
    }

    private void OnLogout(int type, int code) => faults.Guard("logout", () => game.Release("logout"));

    public void Dispose()
    {
        Faults.Attempt(
            "unsubscribing",
            () =>
            {
                PluginInterface.UiBuilder.Draw -= OnDraw;
                PluginInterface.UiBuilder.OpenMainUi -= OpenTrackEditor;
                Framework.Update -= OnFrameworkUpdate;
                ClientState.Logout -= OnLogout;
            }
        );
        Faults.Attempt("closing the windows", windows.RemoveAllWindows);
        Faults.Attempt("disposing the User Guide", guideWindow.Dispose);
        Faults.Attempt("saving", () => sceneFiles?.SaveNow());
        Faults.Attempt("releasing", () => game?.Release("plugin unload"));
        Faults.Attempt("disposing the movement lock", () => Movement?.Dispose());
        Faults.Attempt("disposing the input hooks", () => Input?.Dispose());
        Faults.Attempt("disposing the camera hook", () => Camera?.Dispose());
        Faults.Attempt("removing the command", () => CommandManager.RemoveHandler(CommandName));
        Log.Information("Vista unloaded.");
    }
}
