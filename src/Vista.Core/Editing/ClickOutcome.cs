namespace Vista.Core.Editing;

/// <summary>A click's effect; <see cref="Index"/> is the marker for <see cref="ClickKind.Select"/>.</summary>
public readonly record struct ClickOutcome(ClickKind Kind, int Index = -1);
