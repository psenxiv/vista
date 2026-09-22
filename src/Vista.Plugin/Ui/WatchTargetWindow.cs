using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui;

/// <summary>The edited track's Watch Target settings: the character to watch, its aim height and smoothing.</summary>
internal sealed class WatchTargetWindow : Window
{
    private const float ListWidth = 260f;
    private const float FieldWidth = 70f;

    private readonly CameraSession session;
    private readonly PendingField fields;
    private string search = string.Empty;
    private float? smoothingDrag;
    private Guid openedFor;

    public WatchTargetWindow(CameraSession session, PendingField fields)
        : base("Watch Target###vista-watch-target", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.session = session;
        this.fields = fields;
        RespectCloseHotkey = false;
    }

    /// <summary>Opens the window for the edited track.</summary>
    public void Open()
    {
        openedFor = session.EditedTrackId;
        IsOpen = true;
    }

    /// <summary>Closes when editing ends, another track is edited, or the edited track leaves Watch Target.</summary>
    public override void PreOpenCheck()
    {
        if (!IsOpen) return;
        if (session.Mode != CameraMode.Editing || session.EditedTrackId != openedFor || session.Track.Aim != AimMode.WatchTarget) IsOpen = false;
    }

    /// <summary>Applies an unfinished aim height and drops an unfinished smoothing drag, since a closed window never reports either finishing.</summary>
    public override void OnClose()
    {
        fields.Commit();
        smoothingDrag = null;
    }

    public override void Draw()
    {
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 7f));
        using var selection = ImRaii.PushColor(ImGuiCol.Header, UiColours.AccentAt(0.45f))
            .Push(ImGuiCol.HeaderHovered, UiColours.AccentAt(0.30f))
            .Push(ImGuiCol.HeaderActive, UiColours.AccentAt(0.55f));

        ImGui.BeginDisabled(session.Mode != CameraMode.Editing);
        DrawCharacters();
        DrawAimHeight();
        DrawSmoothing();
        ImGui.EndDisabled();

        ImGui.Separator();
        if (ImGui.Button("Done", new Vector2(ListWidth, 0f))) IsOpen = false;
    }

    /// <summary>A drop-down naming the character followed; it opens on a search box and the characters loaded nearby, by name, once each.</summary>
    private void DrawCharacters()
    {
        var track = session.Track;
        var chosen = track.TargetName is { } name ? $"{name} · {track.TargetWorld ?? "NPC"}" : "Choose a character";
        ImGui.SetNextItemWidth(ListWidth);
        if (!ImGui.BeginCombo("##character", chosen, ImGuiComboFlags.HeightLarge)) return;

        if (ImGui.IsWindowAppearing())
        {
            search = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##search", "Search", ref search, 64);

        var filter = search.Trim();
        var listed = session.Characters.All
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

        for (var i = 0; i < listed.Count; i++)
        {
            var character = listed[i];
            using var id = ImRaii.PushId($"character{i}");
            var followed = character.Name == track.TargetName && character.World == track.TargetWorld;
            if (ImGui.Selectable($"{character.Name} · {character.World ?? "NPC"}###character", followed))
                Report(session.SetTarget(character.Name, character.World));
        }

        ImGui.EndCombo();
    }

    /// <summary>The aim height above the character's feet, as a labelled field.</summary>
    private void DrawAimHeight()
    {
        Label("Aim height");
        fields.Draw("aim-height", session.Track.AimHeight, "%.1f", FieldWidth, v => Report(session.SetAimHeight(v)));
    }

    /// <summary>The smoothing slider; a drag is applied as one undo step when it lets go.</summary>
    private void DrawSmoothing()
    {
        Label("Smoothing");
        var value = smoothingDrag ?? session.Track.Smoothing;
        ImGui.SetNextItemWidth(FieldWidth);
        if (ImGui.SliderFloat("##smoothing", ref value, 0f, 1f, "%.2f")) smoothingDrag = value;
        if (ImGui.IsItemActive() || smoothingDrag is not { } done) return;
        smoothingDrag = null;
        Report(session.SetSmoothing(done));
    }

    /// <summary>A field's label, with the field following at the right end of the list's width.</summary>
    private static void Label(string text)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(text);
        ImGui.SameLine(ListWidth - FieldWidth + ImGui.GetStyle().WindowPadding.X);
    }

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
