using Dalamud.Interface.Utility.Raii;
using Vista.Core.Editing;
using Vista.Plugin.Session;

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

    /// <summary>Disables what's drawn inside it while a self-test runs, in debug builds only.</summary>
    public ImRaii.DisabledDisposable SelfTestGuard()
    {
#if DEBUG
        return ImRaii.Disabled(game.SelfTestRunning);
#else
        return ImRaii.Disabled(false);
#endif
    }
}
