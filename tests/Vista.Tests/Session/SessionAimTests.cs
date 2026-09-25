using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

public class SessionAimTests
{
    private static readonly Vector3 A = new(0f, 0f, -10f);
    private static readonly Vector3 B = new(10f, 0f, -10f);

    // Puts Guard's aim point, 1.3 above the feet, at <paramref name="aim"/>.
    private static void GuardAt(NearbyCharacters characters, Vector3 aim) =>
        characters.Update([new LoadedCharacter("Guard", null, aim - new Vector3(0f, 1.3f, 0f))]);

    // Editing a 2 s track, x = 0 to 10, watching Guard with heavy smoothing; Guard aimed at A.
    private static (SessionState State, NearbyCharacters Characters) Watching()
    {
        var characters = new NearbyCharacters();
        GuardAt(characters, A);
        var state = new SessionState(null, characters);
        state.Edit();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.ChangeTrack(t => t with { Aim = AimMode.WatchTarget, TargetName = "Guard", Smoothing = 1f });
        return (state, characters);
    }

    [Fact]
    public void ScrubbedFramesAimAtTheCharacterWhereTheyAreNow()
    {
        var (state, characters) = Watching();
        AimsAt(A, state.World.FrameAt(0.0)!.Value, 3);

        GuardAt(characters, B);

        AimsAt(B, state.World.FrameAt(0.0)!.Value, 3);
    }

    [Fact]
    public void APreviewStartsOnTheCharacter()
    {
        var (state, _) = Watching();
        state.Play();

        AimsAt(A, state.Transport.AdvancePreview(1f / 60f)!.Value, 3);
    }

    [Fact]
    public void LiveWatchesTheCharacterAndAScrubSnapsBackOntoThem()
    {
        var (state, characters) = Watching();
        state.AddToPlaylist([state.EditedTrackId]);
        state.Cue();
        state.Play();
        AimsAt(A, state.Director.Tick(1f / 60f)!.Value, 3);

        GuardAt(characters, B);
        state.Director.Tick(0.5f);

        state.Transport.BeginScrub();
        state.Transport.ScrubTo(1.0);
        AimsAt(B, state.Director.Tick(1f / 60f)!.Value, 3);
    }

    [Fact]
    public void TheSessionSaysWhenTheCharacterIsLost()
    {
        var (state, characters) = Watching();
        Assert.False(state.World.TargetLost(state.Track));
        Assert.Equal(A, state.World.TargetPoint(state.Track));

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

        GuardAt(characters, A);
        Assert.Equal(TargetState.Found, state.World.StateOfTarget(state.Track));
    }
}
