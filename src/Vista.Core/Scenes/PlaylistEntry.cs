namespace Vista.Core.Scenes;

/// <summary>One playlist entry: the track it plays, how many times (null follows the track), and the transition into the next.</summary>
public sealed record PlaylistEntry(Guid Id, Guid TrackId, int? Loops = null, Transition Transition = Transition.Cut);
