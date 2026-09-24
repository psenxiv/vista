namespace Vista.Core.Tracks.Playback;

/// <summary>A playlist shot: play its entries in turn, wrapping to the first at the end when <paramref name="Loops"/>.</summary>
public sealed record PlaylistShot(IReadOnlyList<PlaylistItem> Items, bool Loops = false) : Shot;
