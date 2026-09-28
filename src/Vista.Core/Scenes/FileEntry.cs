namespace Vista.Core.Scenes;

/// <summary>A scene or preset file as the picker lists it; <paramref name="Tracks"/> is null for a preset.</summary>
public sealed record FileEntry(string Name, DateTime Modified, int? Tracks);
