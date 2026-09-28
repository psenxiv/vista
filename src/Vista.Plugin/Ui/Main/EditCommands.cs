using Vista.Core.Editing;
using Vista.Plugin.Session;
#if DEBUG
using Dalamud.Interface.Utility.Raii;
#endif

namespace Vista.Plugin.Ui.Main;

/// <summary>Undo, Redo, Play/Pause and Restart as the toolbar and menus run them, applying the held field edit first.</summary>
internal sealed class EditCommands(GameSession game, PendingEdit<float> fields)
{
    public void Undo()
    {
        fields.Commit();
        game.State.Undo();
    }

    public void Redo()
    {
        fields.Commit();
        game.State.Redo();
    }

    public void TogglePlay()
    {
        fields.Commit();
        game.TogglePlay();
    }

    public void Restart()
    {
        fields.Commit();
        game.RestartPlay();
    }

#if DEBUG
    /// <summary>Disables what's drawn inside it while a self-test runs.</summary>
    public ImRaii.DisabledDisposable SelfTestGuard() => ImRaii.Disabled(game.SelfTestRunning);
#endif
}
