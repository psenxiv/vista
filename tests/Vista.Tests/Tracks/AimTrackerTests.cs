using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class AimTrackerTests
{
    // One point at the origin, recorded aim yaw 0.5.
    private static readonly ControlPoint Camera = new(Vector3.Zero, 0.5f, 0f, 1f);
    private static readonly Vector3 Recorded = FreeCamMotion.LookAtFrom(Vector3.Zero, 0.5f, 0f);

    private static Track Single(AimMode aim) => TrackEditing.Append(TrackEditing.Empty(aim), Camera);

    private static Track Following(string? name = "Guard", float smoothing = 0f)
        => Single(AimMode.FollowTarget) with { TargetName = name, Smoothing = smoothing };

    // A guard whose aim point, 1.3 above the feet, is at (x, 0, −10).
    private static NearbyCharacters GuardAt(float x)
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Guard", null, new Vector3(x, -1.3f, -10f))]);
        return characters;
    }

    private static CameraState Frame(AimTracker tracker, Track track, float dt = 1f / 60f)
        => tracker.Frame(new TrackEvaluator(track), track, 0.0, dt)!.Value;

    private static void AimsAt(Vector3 target, CameraState frame)
    {
        var want = Vector3.Normalize(target - frame.Position);
        var got = Vector3.Normalize(frame.LookAt - frame.Position);
        Assert.Equal(want.X, got.X, 3);
        Assert.Equal(want.Y, got.Y, 3);
        Assert.Equal(want.Z, got.Z, 3);
    }

    [Fact]
    public void FollowAimsAtTheCharacterAtItsAimHeight()
    {
        AimsAt(new Vector3(0f, 0f, -10f), Frame(new AimTracker(GuardAt(0f)), Following()));
        Assert.Equal(new Vector3(0f, 0f, -10f), AimTracker.CharacterAim(Following(), GuardAt(0f)));
    }

    [Fact]
    public void RecordedAimIsUsedWhenTheCharacterIsNotFoundOrNoneIsChosen()
    {
        Assert.Equal(Recorded, Frame(new AimTracker(new NearbyCharacters()), Following()).LookAt);
        Assert.Equal(Recorded, Frame(new AimTracker(GuardAt(0f)), Following(name: null)).LookAt);
        Assert.True(AimTracker.TargetLost(Following(), new NearbyCharacters()));
        Assert.False(AimTracker.TargetLost(Following(name: null), new NearbyCharacters()));
        Assert.False(AimTracker.TargetLost(Following(), GuardAt(0f)));
    }

    [Fact]
    public void ADuplicateNameResolvesNearestTheTracksAnchor()
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Guard", null, new Vector3(-20f, -1.3f, -10f)), new LoadedCharacter("Guard", null, new Vector3(20f, -1.3f, -10f))]);
        var track = Following() with { Anchor = new Anchor(new Vector3(15f, 0f, 0f), 0f) };

        AimsAt(new Vector3(20f, 0f, -10f), Frame(new AimTracker(characters), track));
    }

    [Fact]
    public void APlayerIsFoundOnlyOnTheirWorld()
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Aya", "Gilgamesh", new Vector3(-20f, -1.3f, -10f)), new LoadedCharacter("Aya", "Cactuar", new Vector3(20f, -1.3f, -10f))]);
        var track = Following("Aya") with { TargetWorld = "Gilgamesh", Anchor = new Anchor(new Vector3(15f, 0f, 0f), 0f) };

        Assert.Equal(new Vector3(-20f, 0f, -10f), AimTracker.CharacterAim(track, characters));
        Assert.True(AimTracker.TargetLost(track with { TargetWorld = "Sargatanas" }, characters));
    }

    [Fact]
    public void LookAtAimsAtThePoint()
    {
        var track = Single(AimMode.LookAt) with { LookAt = new Vector3(0f, 0f, -10f), LookAtPlaced = true };

        AimsAt(new Vector3(0f, 0f, -10f), Frame(new AimTracker(null), track));
    }

    [Theory]
    [InlineData(AimMode.AimKeys)]
    [InlineData(AimMode.PathTangent)]
    public void ASinglePointKeepsItsRecordedAimUnderTheOtherModes(AimMode aim)
    {
        var track = Single(aim) with { TargetName = "Guard", LookAt = new Vector3(0f, 0f, -10f), LookAtPlaced = true };

        Assert.Equal(Recorded, Frame(new AimTracker(GuardAt(5f)), track).LookAt);
    }

    [Fact]
    public void TheAimEasesOntoAMovingCharacterAndAResetSnapsBack()
    {
        var characters = GuardAt(0f);
        var tracker = new AimTracker(characters);
        var track = Following(smoothing: 1f);
        Frame(tracker, track);

        characters.Update(GuardAt(10f).All);
        AimsAt(Vector3.Lerp(new Vector3(0f, 0f, -10f), new Vector3(10f, 0f, -10f), 1f - MathF.Exp(-1f)), Frame(tracker, track, 0.5f));

        tracker.Reset();
        AimsAt(new Vector3(10f, 0f, -10f), Frame(tracker, track));
    }

    [Fact]
    public void FindingTheCharacterAgainEasesFromTheRecordedAim()
    {
        var characters = new NearbyCharacters();
        var tracker = new AimTracker(characters);
        var track = Following(smoothing: 1f);
        Frame(tracker, track);

        characters.Update(GuardAt(0f).All);

        AimsAt(Vector3.Lerp(Recorded, new Vector3(0f, 0f, -10f), 1f - MathF.Exp(-1f)), Frame(tracker, track, 0.5f));
    }

    [Fact]
    public void ATargetOnTheCameraKeepsTheLastGoodAim()
    {
        var tracker = new AimTracker(null);
        var far = Single(AimMode.LookAt) with { LookAt = new Vector3(0f, 0f, -10f), LookAtPlaced = true };
        var near = far with { LookAt = new Vector3(0.05f, 0f, 0f) };
        Frame(tracker, far);

        AimsAt(new Vector3(0f, 0f, -10f), Frame(tracker, near));
        Assert.Equal(Recorded, Frame(new AimTracker(null), near).LookAt);
    }

    [Fact]
    public void AResetForgetsTheLastGoodAim()
    {
        var tracker = new AimTracker(null);
        var far = Single(AimMode.LookAt) with { LookAt = new Vector3(0f, 0f, -10f), LookAtPlaced = true };
        var near = far with { LookAt = new Vector3(0.05f, 0f, 0f) };
        Frame(tracker, far);

        tracker.Reset();

        Assert.Equal(Recorded, Frame(tracker, near).LookAt);
    }
}
