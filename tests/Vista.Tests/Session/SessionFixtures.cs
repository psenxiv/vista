using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

/// <summary>Sessions set up the way the session tests start them.</summary>
internal static class SessionFixtures
{
    /// <summary>Where Guard's aim point starts in <see cref="EditingWatchingGuard"/>, ahead of the track's start along −z.</summary>
    internal static readonly Vector3 WatchedAtA = new(0f, 0f, -10f);

    /// <summary>A second spot for Guard's aim point, level with the track's end.</summary>
    internal static readonly Vector3 WatchedAtB = new(10f, 0f, -10f);

    /// <summary>A control point at head height: <see cref="Fixtures.Point"/> with <paramref name="y"/> defaulting to 5.</summary>
    internal static ControlPoint HeadHeightPoint(float x, float y = 5f, float z = 0f) => Fixtures.Point(x, y, z);

    /// <summary>Editing a 2 s track, x = 0 to 10, watching Guard with heavy smoothing, with an empty playlist; Guard aimed at <see cref="WatchedAtA"/>.</summary>
    internal static (SessionState State, NearbyCharacters Characters) EditingWatchingGuard()
    {
        var characters = new NearbyCharacters();
        GuardAt(characters, WatchedAtA);
        var state = EmptyPlaylistSession(characters);
        state.Edit();
        state.AddToEnd(Fixtures.Point(0f));
        state.AddToEnd(Fixtures.Point(10f));
        state.ChangeTrack(t => t with { Aim = AimMode.WatchTarget, TargetName = "Guard", Smoothing = 1f });
        return (state, characters);
    }

    /// <summary>Editing with an empty playlist; Track 1 has points at x = 0, 10, 20 at 2 yalms per second: two 5 s legs, keys at 0, 5 and 10 s, nothing selected.</summary>
    internal static SessionState EditingThreePoints()
    {
        var state = EmptyPlaylistSession();
        state.Edit();
        state.SetTrackSpeed(2f);
        state.AddToEnd(Fixtures.Point(0f));
        state.AddToEnd(Fixtures.Point(10f));
        state.AddToEnd(Fixtures.Point(20f));
        return state;
    }

    /// <summary>Editing with no undo history and Use switchboard on. Track 1: x = 0, 10, 20 at 2 yalms per second, a 10 s shot with x = 2t. Track 2: x = 0 and 4, a 2 s shot with x = 2t. Track 3: no points. Playlist 1: Track 2 then Track 1, 12 s. Slots: 0 Track 1, 1 Track 2, 2 Track 3 (can't play), 3 Playlist 1.</summary>
    internal static SessionState EditingSwitchboard()
    {
        var state = EditingThreePoints();
        state.AddTrack();
        state.SetTrackSpeed(2f);
        state.AddToEnd(Fixtures.Point(0f));
        state.AddToEnd(Fixtures.Point(4f));
        state.AddTrack();
        state.AddToPlaylist([TrackId(state, 1)]);
        state.AddToPlaylist([TrackId(state, 0)]);
        state.SetUseSwitchboard(true);
        state.AssignSlot(0, TrackId(state, 0));
        state.AssignSlot(1, TrackId(state, 1));
        state.AssignSlot(2, TrackId(state, 2));
        state.AssignSlot(3, state.Scene.Playlists[0].Id);
        state.LoadScene(state.Scene);
        return state;
    }

    /// <summary>Makes slot <paramref name="slot"/> Next on <paramref name="board"/> and cuts to it.</summary>
    internal static void CutTo(SwitchboardPlayer board, int slot)
    {
        board.Click(slot);
        board.Cut();
    }

    /// <summary>Editing with the ground at y = 1; Track 1 has points at x = 10, 20, 30 at head height, y = 5, all aimed along −z.</summary>
    internal static SessionState EditingOverGround()
    {
        var state = new SessionState(_ => 1f);
        state.Edit();
        foreach (var x in new[] { 10f, 20f, 30f })
            state.AddToEnd(HeadHeightPoint(x));
        return state;
    }

    /// <summary>The Id of the scene's track at <paramref name="index"/>.</summary>
    internal static Guid TrackId(SessionState state, int index) => state.Scene.Tracks[index].Id;

    /// <summary>The Id of the playlist entry at <paramref name="index"/>.</summary>
    internal static Guid EntryId(SessionState state, int index) => Entries(state.Scene)[index].Id;
}
