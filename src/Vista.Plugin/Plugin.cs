using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Vista.Core.Editing;
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

    internal static CameraController Camera { get; private set; } = null!;
    internal static InputBlocker Input { get; private set; } = null!;
    internal static MovementLock Movement { get; private set; } = null!;

    private readonly GameSession game;
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

    private readonly WheelSteps wheel = new();
    private bool escapeWasDown;
    private static bool blockEscape;

    public Plugin()
    {
        var config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        Movement = new MovementLock();
        game = new GameSession(config, Movement);
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

        Camera = new CameraController(() => game.Frame((float)Framework.UpdateDelta.TotalSeconds));
        Input = new InputBlocker(() => game.State.LocksInput, () => blockEscape);

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

    private void OnCommand(string command, string args)
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

    private void OnFrameworkUpdate(IFramework framework)
    {
        Camera.TryInstallHook();
        Input.SyncHookState();

        if (Camera.Faulted)
        {
            game.Release("hook error");
            Camera.ClearFault();
        }

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

        if (!game.OwnsCamera)
            return;

        // Covers every transition: a teleport, an aethernet hop, a cutscene, a duty starting.
        if (Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51])
        {
            game.Release("area transition");
            return;
        }

        // Someone else reset the shared counter. Stop tracking our hold so we never
        // decrement theirs, but keep flying: dropping a shot mid-take is worse.
        if (Movement.Held && Movement.Count == 0)
            Movement.Forget();
    }

    /// <summary>Opens the Vista window, or Setup in its place while there is no save folder.</summary>
    private void OpenTrackEditor()
    {
        if (sceneFiles.Ready)
            trackEditor.IsOpen = true;
        else
            setupWindow.IsOpen = true;
    }

    /// <summary>Steps fly speed with the scroll wheel while editing, then draws the windows.</summary>
    private void OnDraw()
    {
        var io = ImGui.GetIO();
        if (game.State.Mode == CameraMode.Editing && !io.WantCaptureMouse)
        {
            var steps = wheel.Take(io.MouseWheel);
            if (steps != 0)
            {
                game.State.Transport.StopPreview();
                game.Speed.Step(steps);
            }
        }
        else
        {
            wheel.Reset();
        }

        editorLayer.Draw();
        windows.Draw();
    }

    private void OnLogout(int type, int code) => game.Release("logout");

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenTrackEditor;
        windows.RemoveAllWindows();
        guideWindow.Dispose();
        Framework.Update -= OnFrameworkUpdate;
        ClientState.Logout -= OnLogout;
        sceneFiles?.SaveNow();
        game?.Release("plugin unload");
        Movement?.Dispose();
        Input?.Dispose();
        Camera?.Dispose();
        CommandManager.RemoveHandler(CommandName);
        Log.Information("Vista unloaded.");
    }
}
