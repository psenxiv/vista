namespace Vista.Core.Scenes;

/// <summary>A scene's slots, empty ones null, its three toggles, and Live's place on it.</summary>
public sealed record Switchboard(
    IReadOnlyList<Slot?> Slots,
    bool DirectCut,
    bool KeepRolling,
    bool AutoNext,
    OnAir Live
);
