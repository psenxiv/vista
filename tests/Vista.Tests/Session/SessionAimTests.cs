using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionAimTests
{
    private static readonly Vector3 A = new(0f, 0f, -10f);
    private static readonly Vector3 B = new(10f, 0f, -10f);
    private static readonly Vector3 Eased = Vector3.Lerp(A, B, 1f - MathF.Exp(-1f));

    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Puts Guard's aim point, 1.3 above the feet, at <paramref name="aim"/>.
    private static void GuardAt(NearbyCharacters characters, Vector3 aim)
        => characters.Update([new LoadedCharacter("Guard", null, aim - new Vector3(0f, 1.3f, 0f))]);

    // Editing a 2 s track, x = 0 to 10, following Guard with heavy smoothing; Guard aimed at A.
    private static (SessionState State, NearbyCharacters Characters) Following()
    {
        var characters = new NearbyCharacters();
        GuardAt(characters, A);
        var state = new SessionState(null, characters);
        state.Edit();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.ChangeTrack(t => t with { Aim = AimMode.FollowTarget, TargetName = "Guard", Smoothing = 1f });
        return (state, characters);
    }

    private static void AimsAt(Vector3 target, CameraState frame)
    {
        var want = Vector3.Normalize(target - frame.Position);
        var got = Vector3.Normalize(frame.LookAt - frame.Position);
        Assert.Equal(want.X, got.X, 3);
        Assert.Equal(want.Y, got.Y, 3);
        Assert.Equal(want.Z, got.Z, 3);
    }

    [Fact]
    public void ScrubbedFramesAimAtTheCharacterWhereTheyAreNow()
    {
        var (state, characters) = Following();
        AimsAt(A, state.FrameAt(0.0)!.Value);

        GuardAt(characters, B);

        AimsAt(B, state.FrameAt(0.0)!.Value);
    }

    [Fact]
    public void APreviewStartsOnTheCharacterAndEasesAfterThem()
    {
        var (state, characters) = Following();
        state.Play();
        AimsAt(A, state.AdvancePreview(1f / 60f)!.Value);

        GuardAt(characters, B);

        AimsAt(Eased, state.AdvancePreview(0.5f)!.Value);
    }

    [Fact]
    public void LiveFollowsTheCharacterAndAScrubSnapsBackOntoThem()
    {
        var (state, characters) = Following();
        state.AddToPlaylist(state.EditedTrackId);
        state.Cue();
        state.Play();
        AimsAt(A, state.Director.Tick(1f / 60f)!.Value);

        GuardAt(characters, B);
        AimsAt(Eased, state.Director.Tick(0.5f)!.Value);

        state.BeginScrub();
        state.ScrubTo(1.0);
        AimsAt(B, state.Director.Tick(1f / 60f)!.Value);
    }

    [Fact]
    public void TheSessionSaysWhenTheCharacterIsLost()
    {
        var (state, characters) = Following();
        Assert.False(state.TargetLost(state.Track));
        Assert.Equal(A, state.CharacterAim(state.Track));

        characters.Update([]);

        Assert.True(state.TargetLost(state.Track));
        Assert.Null(state.CharacterAim(state.Track));
    }
}
