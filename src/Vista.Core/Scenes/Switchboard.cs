namespace Vista.Core.Scenes;

/// <summary>A scene's slots, empty ones null, its three toggles, Live's place on it, and whether the scene uses it.</summary>
public sealed record Switchboard(
    IReadOnlyList<Slot?> Slots,
    bool DirectCut,
    bool KeepRolling,
    bool AutoNext,
    OnAir Live,
    bool Enabled = false
);
