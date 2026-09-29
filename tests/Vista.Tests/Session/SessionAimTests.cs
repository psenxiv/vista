using Vista.Core.Session;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Session;

public class SessionAimTests
{
    [Fact]
    public void ScrubbedFramesAimAtTheCharacterWhereTheyAreNow()
    {
        var (state, characters) = EditingWatchingGuard();
        AimsAt(WatchedAtA, state.World.FrameAt(0.0)!.Value, 3);

        GuardAt(characters, WatchedAtB);

        AimsAt(WatchedAtB, state.World.FrameAt(0.0)!.Value, 3);
    }

    [Fact]
    public void APreviewStartsOnTheCharacter()
    {
        var (state, _) = EditingWatchingGuard();
        state.Play();

        AimsAt(WatchedAtA, state.Transport.AdvancePreview(1f / 60f)!.Value, 3);
    }

    [Fact]
    public void LiveWatchesTheCharacterAndAScrubSnapsBackOntoThem()
    {
        var (state, characters) = EditingWatchingGuard();
        state.AddToPlaylist([state.EditedTrackId]);
        GoLive(state);
        AimsAt(WatchedAtA, state.LiveFrame(1f / 60f)!.Value, 3);

        GuardAt(characters, WatchedAtB);
        state.LiveFrame(0.5f);

        state.Board!.BeginScrub();
        state.Board.ScrubTo(1.0);
        AimsAt(WatchedAtB, state.LiveFrame(1f / 60f)!.Value, 3);
    }

    [Fact]
    public void TheSessionSaysWhenTheCharacterIsLost()
    {
        var (state, characters) = EditingWatchingGuard();
        Assert.False(state.World.TargetLost(state.Track));
        Assert.Equal(WatchedAtA, state.World.TargetPoint(state.Track));

        characters.Update([]);

        Assert.True(state.World.TargetLost(state.Track));
        Assert.Null(state.World.TargetPoint(state.Track));
    }

    // No name is Unchosen; a name with nobody of it loaded is Lost; once they load, Found.

    [Fact]
    public void TheTargetIsUnchosenThenLostThenFound()
    {
        var characters = new NearbyCharacters();
        var state = new SessionState(null, characters);
        state.Edit();
        state.ChangeTrack(t => t with { Aim = AimMode.WatchTarget });
        Assert.Equal(TargetState.Unchosen, state.World.StateOfTarget(state.Track));

        state.SetTarget("Guard", null);
        Assert.Equal(TargetState.Lost, state.World.StateOfTarget(state.Track));

        GuardAt(characters, WatchedAtA);
        Assert.Equal(TargetState.Found, state.World.StateOfTarget(state.Track));
    }
}
