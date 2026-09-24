using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>The drop-down naming the edited track's character; it opens on a search box and the characters loaded nearby, by name, once each.</summary>
internal static class CharacterPicker
{
    /// <summary>Draws the drop-down <paramref name="width"/> wide, listing <paramref name="characters"/>. Returns why choosing a character was refused, or null.</summary>
    public static string? Draw(SessionState session, NearbyCharacters characters, ref string search, float width)
    {
        var track = session.Track;
        var chosen = track.TargetName is { } name ? $"{name} · {track.TargetWorld ?? "NPC"}" : "Choose a character";
        ImGui.SetNextItemWidth(width);
        if (!ImGui.BeginCombo("##character", chosen, ImGuiComboFlags.HeightLarge)) return null;

        if (ImGui.IsWindowAppearing())
        {
            search = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##search", "Search", ref search, 64);

        var filter = search.Trim();
        var listed = characters.All
            .Where(c => filter.Length == 0 || c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(c => (c.Name, c.World))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.World ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (listed.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
                ImGui.TextUnformatted(filter.Length == 0 ? "No characters nearby" : "No matches");
        }

        string? refusal = null;
        for (var i = 0; i < listed.Count; i++)
        {
            var character = listed[i];
            using var id = ImRaii.PushId($"character{i}");
            var named = character.Name == track.TargetName && character.World == track.TargetWorld;
            if (ImGui.Selectable($"{character.Name} · {character.World ?? "NPC"}###character", named))
                refusal = session.SetTarget(character.Name, character.World);
        }

        ImGui.EndCombo();
        return refusal;
    }
}
