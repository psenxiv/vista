using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>A track saved on its own: the track local to its anchor, its own Anchor ignored, and the anchor's world yaw.</summary>
public sealed record Preset(Track Track, float Yaw);
