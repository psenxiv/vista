using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Vista.Core.Display;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Editor;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;
using Vista.Plugin.Ui.Windows;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Main;

/// <summary>The Vista window's menu bar: Scene, Edit, View, Preview and Help.</summary>
internal sealed class MainMenu
{
    private const float SliderWidth = 120f;

    private readonly GameSession game;
    private readonly SessionState session;
    private readonly Configuration config;
    private readonly PendingEdit<float> fields;
    private readonly SceneFiles files;
    private readonly EditorLayer layer;
    private readonly TimingWindow timing;
    private readonly CameraWindow camera;
    private readonly GuideWindow guide;
    private readonly SetupWindow setup;
    private readonly FilePickerWindow picker;
    private readonly NamePrompt namePrompt = new("scene");
    private readonly DeleteConfirm deleteConfirm = new("scene");

    public MainMenu(
        GameSession game,
        Configuration config,
        PendingEdit<float> fields,
        SceneFiles files,
        EditorLayer layer,
        TimingWindow timing,
        CameraWindow camera,
        GuideWindow guide,
        SetupWindow setup,
        FilePickerWindow picker
    )
    {
        this.game = game;
        session = game.State;
        this.config = config;
        this.fields = fields;
        this.files = files;
        this.layer = layer;
        this.timing = timing;
        this.camera = camera;
        this.guide = guide;
        this.setup = setup;
        this.picker = picker;
    }

    /// <summary>Draws the menu bar; View's Hierarchy and Playlist items flip <paramref name="showHierarchy"/> and <paramref name="showPlaylist"/>.</summary>
    public void Draw(ref bool showHierarchy, ref bool showPlaylist)
    {
        using (var bar = ImRaii.MenuBar())
        {
            if (bar)
            {
                var editing = session.Mode == CameraMode.Editing;
                DrawScene(editing);
                DrawEdit(editing);
                DrawView(editing, ref showHierarchy, ref showPlaylist);
                DrawPreview(editing);
                DrawHelp();
            }
        }

        // Drawn outside the menu bar's own popup scope, so opening one of these doesn't close with it.
        namePrompt.Draw();
        deleteConfirm.Draw();
    }

    private void DrawScene(bool editing)
    {
        using var menu = ImRaii.Menu("Scene");
        if (!menu)
            return;
        var live = session.Mode == CameraMode.Live;
        // Opening another scene loads it, which Live refuses.
        if (Menu.Item("Open scene...", !live))
            picker.Show(FilePickerKind.Scene);
        if (Menu.Item("New scene..."))
            namePrompt.Ask(
                "New scene",
                files.NewSuggestion(),
                text => (files.NameRefusal(text), null),
                name => Report(files.New(name))
            );
        if (Menu.Item("Rename scene..."))
            namePrompt.Ask(
                "Rename scene",
                files.CurrentName,
                text => (files.NameRefusal(text, renaming: files.CurrentName), null),
                name => Report(files.Rename(name))
            );
        if (Menu.Item("Duplicate scene..."))
            namePrompt.Ask(
                "Duplicate scene",
                files.CopySuggestion(),
                text => (files.NameRefusal(text), null),
                name => Report(files.Duplicate(name))
            );
        if (Menu.Item("Delete scene..."))
            deleteConfirm.Ask(files.CurrentName, () => Report(files.Delete()));
        ImGui.Separator();
        if (Menu.Item("Select scene anchor", editing && session.Scene.AnchorPlaced))
            Report(session.Selection.SelectSceneAnchor());
        ImGui.Separator();
        // A new folder loads a scene, which Live refuses.
        if (Menu.Item("Save folder...", !live))
            Show(setup);
        if (Menu.Item("Open save folder", files.Ready))
            files.OpenFolder(presets: false);
        ImGui.Separator();
        var hideUi = game.HideUiInLive;
        if (Menu.Check("Hide game UI when Live", ref hideUi))
            game.HideUiInLive = hideUi;
    }

    private void DrawEdit(bool editing)
    {
        using var menu = ImRaii.Menu("Edit");
        if (!menu)
            return;
        if (Menu.Item("Undo", session.CanUndo, "Ctrl + Z"))
        {
            fields.Commit();
            session.Undo();
        }
        if (Menu.Item("Redo", session.CanRedo, "Ctrl + Y"))
        {
            fields.Commit();
            session.Redo();
        }

        ImGui.Separator();
        DrawAddPoint(editing);
        var points = session.Selection.Points;
        if (Menu.Item("Duplicate point", editing && points.Count > 0 && TrackEditing.CanDuplicate(session.Track)))
        {
            fields.Commit();
            Report(session.DuplicatePoints(points));
        }
        if (Menu.Item("Delete selected points", editing && points.Count > 0, "Delete"))
        {
            fields.Clear();
            Report(session.DeleteSelected());
        }

        ImGui.Separator();
        if (Menu.Item("Level camera roll", editing, "Alt + R"))
            game.LevelCameraRoll();
    }

    /// <summary>Add point's submenu: the camera's pose at the end, after the selected point, or in its place.</summary>
    private void DrawAddPoint(bool editing)
    {
        using var menu = ImRaii.Menu("Add point", editing);
        if (!menu)
            return;
        AddPointItems.Draw(game);
    }

    private void DrawView(bool editing, ref bool showHierarchy, ref bool showPlaylist)
    {
        using var menu = ImRaii.Menu("View");
        if (!menu)
            return;
        Menu.Check("Hierarchy", ref showHierarchy);
        Menu.Check("Playlist", ref showPlaylist);
        ImGui.Separator();
        WindowCheck("Timing", timing, enabled: true);
        WindowCheck("Camera", camera, editing);

        ImGui.Separator();
        var names = config.ShowTrackNames;
        if (Menu.Check("Track names", ref names))
        {
            config.ShowTrackNames = names;
            config.Save();
        }
        var percent = TrackNameSize.Percent(config.TrackNameScale);
        if (
            Menu.Slider(
                "Track name size",
                ref percent,
                TrackNameSize.Percent(TrackNameSize.Min),
                TrackNameSize.Percent(TrackNameSize.Max),
                "%.0f%%",
                SliderWidth
            )
        )
            config.TrackNameScale = TrackNameSize.FromPercent(percent);
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save();

        ImGui.Separator();
        var heat = layer.Heat;
        if (Menu.Check("Colour path by turn speed", ref heat, session.OverlayShown, "G"))
            layer.Heat = heat;
    }

    private void DrawPreview(bool editing)
    {
        using var menu = ImRaii.Menu("Preview");
        if (!menu)
            return;
        DrawTransport();
        ImGui.Separator();
        var transport = session.Transport;
        var ghost = transport.Ghost;
        if (Menu.Check("Ghost camera", ref ghost, editing))
            Report(transport.SetGhost(ghost));
        DrawPlaybackSpeed(transport, editing);
    }

    /// <summary>Play / Pause and Restart.</summary>
    private void DrawTransport()
    {
#if DEBUG
        using var selfTest = ImRaii.Disabled(game.SelfTestRunning);
#endif
        if (Menu.Item("Play / Pause", session.CanStart, "Space"))
        {
            fields.Commit();
            game.TogglePlay();
        }
        if (Menu.Item("Restart", session.CanRestart, "Ctrl + Space"))
        {
            fields.Commit();
            game.RestartPlay();
        }
    }

    /// <summary>The Edit preview's speed: a drag moves in steps, a typed value is taken as it is.</summary>
    private static void DrawPlaybackSpeed(Transport transport, bool editing)
    {
        var rate = transport.PlaybackRate;
        if (
            !Menu.Slider(
                "Playback speed",
                ref rate,
                Transport.MinPlaybackRate,
                Transport.MaxPlaybackRate,
                "%.2fx",
                SliderWidth,
                editing
            )
        )
            return;
        var dragged =
            ImGui.IsItemActive()
            && ImGui.IsMouseDown(ImGuiMouseButton.Left)
            && !ImGuiP.TempInputIsActive(ImGuiP.GetItemID());
        Report(transport.SetPlaybackRate(dragged ? Transport.SnapPlaybackRate(rate) : rate));
    }

    private void DrawHelp()
    {
        using var menu = ImRaii.Menu("Help");
        if (!menu)
            return;
        if (Menu.Item("User Guide"))
            Show(guide);
        if (Menu.Item("Hotkeys"))
            guide.Show(GuideWindow.HotkeysPage);
    }

    /// <summary>A ticked item that opens or closes <paramref name="window"/>.</summary>
    private static void WindowCheck(string label, Window window, bool enabled)
    {
        var open = window.IsOpen;
        if (Menu.Check(label, ref open, enabled))
            window.IsOpen = open;
    }

    /// <summary>Opens <paramref name="window"/> in front.</summary>
    private static void Show(Window window)
    {
        window.IsOpen = true;
        window.BringToFront();
    }
}
