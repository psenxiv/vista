namespace Vista.Core.Tracks.Playback;

/// <summary>A place in the playlist: the entry's index, its loop pass from 0, and the seconds into that pass.</summary>
public readonly record struct PlaylistPosition(int Index, int Pass, double Time);
