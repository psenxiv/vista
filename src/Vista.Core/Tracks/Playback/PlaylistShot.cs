namespace Vista.Core.Tracks.Playback;

/// <summary>Something the Director can put on program: play its entries in turn, wrapping to the first at the end when <paramref name="Loops"/>.</summary>
public sealed record PlaylistShot(IReadOnlyList<PlaylistItem> Items, bool Loops = false);
