using Vista.Core.Session;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

/// <summary>Sessions set up the way the session tests start them.</summary>
internal static class SessionFixtures
{
    /// <summary>Editing; Track 1 has points at x = 0, 10, 20 at 2 yalms per second: two 5 s legs, keys at 0, 5 and 10 s, nothing selected.</summary>
    internal static SessionState EditingThreePoints()
    {
        var state = new SessionState();
        state.Edit();
        state.SetTrackSpeed(2f);
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        return state;
    }

    /// <summary>Editing with the ground at y = 1; Track 1 has points at x = 10, 20, 30 at head height, y = 5, all aimed along −z.</summary>
    internal static SessionState EditingOverGround()
    {
        var state = new SessionState(_ => 1f);
        state.Edit();
        foreach (var x in new[] { 10f, 20f, 30f })
            state.AddToEnd(Point(x, 5f));
        return state;
    }

    /// <summary>The Id of the scene's track at <paramref name="index"/>.</summary>
    internal static Guid TrackId(SessionState state, int index) => state.Scene.Tracks[index].Id;

    /// <summary>The Id of the playlist entry at <paramref name="index"/>.</summary>
    internal static Guid EntryId(SessionState state, int index) => state.Scene.Playlist[index].Id;
}
