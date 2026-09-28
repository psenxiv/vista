using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Plugin.Session;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Saves a track as a preset: the name prompt shared by the Hierarchy row menu and the Scene menu.</summary>
internal sealed class PresetSave(SceneFiles files)
{
    private readonly NamePrompt namePrompt = new("preset");
    private IReadOnlyList<string> presets = [];
    private Guid presetTrack;

    /// <summary>Opens the prompt to save <paramref name="track"/> as a preset.</summary>
    public void Ask(Track track)
    {
        presets = files.PresetNames();
        presetTrack = track.Id;
        namePrompt.Ask(
            "Save as preset",
            track.Name,
            text =>
            {
                var (refusal, replaces) = SceneNames.PresetCheck(text, presets);
                return (refusal, replaces ? $"A preset called {text.Trim()} exists." : null);
            },
            saved => Refusal.Report(files.SavePreset(saved, presetTrack))
        );
    }

    /// <summary>Opens and draws the popup; call once a frame from outside any other popup's scope.</summary>
    public void Draw() => namePrompt.Draw();
}
