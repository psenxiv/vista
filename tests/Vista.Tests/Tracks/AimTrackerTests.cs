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

    private static Track Watching(string? name = "Guard", float smoothing = 0f)
        => Single(AimMode.WatchTarget) with { TargetName = name, Smoothing = smoothing };

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
    public void WatchAimsAtTheCharacterAtItsAimHeight()
    {
        AimsAt(new Vector3(0f, 0f, -10f), Frame(new AimTracker(GuardAt(0f)), Watching()));
        Assert.Equal(new Vector3(0f, 0f, -10f), AimTracker.CharacterAim(Watching(), GuardAt(0f)));
    }

    [Fact]
    public void RecordedAimIsUsedWhenTheCharacterIsNotFoundOrNoneIsChosen()
    {
        Assert.Equal(Recorded, Frame(new AimTracker(new NearbyCharacters()), Watching()).LookAt);
        Assert.Equal(Recorded, Frame(new AimTracker(GuardAt(0f)), Watching(name: null)).LookAt);
        Assert.True(AimTracker.TargetLost(Watching(), new NearbyCharacters()));
        Assert.False(AimTracker.TargetLost(Watching(name: null), new NearbyCharacters()));
        Assert.False(AimTracker.TargetLost(Watching(), GuardAt(0f)));
    }

    [Fact]
    public void ADuplicateNameResolvesNearestTheTracksAnchor()
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Guard", null, new Vector3(-20f, -1.3f, -10f)), new LoadedCharacter("Guard", null, new Vector3(20f, -1.3f, -10f))]);
        var track = Watching() with { Anchor = new Anchor(new Vector3(15f, 0f, 0f), 0f) };

        AimsAt(new Vector3(20f, 0f, -10f), Frame(new AimTracker(characters), track));
    }

    [Fact]
    public void APlayerIsFoundOnlyOnTheirWorld()
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Aya", "Gilgamesh", new Vector3(-20f, -1.3f, -10f)), new LoadedCharacter("Aya", "Cactuar", new Vector3(20f, -1.3f, -10f))]);
        var track = Watching("Aya") with { TargetWorld = "Gilgamesh", Anchor = new Anchor(new Vector3(15f, 0f, 0f), 0f) };

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
        var track = Watching(smoothing: 1f);
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
        var track = Watching(smoothing: 1f);
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

    // A Follow track whose one point is the offset (0, 2, 5) behind and above, looking back along −z (yaw 0).
    private static Track FollowingAt(ControlPoint offset, bool turns = true, bool looks = false, float smoothing = 0f)
        => TrackEditing.Append(TrackEditing.Empty(AimMode.FollowTarget), offset) with { TargetName = "Guard", FollowTurns = turns, FollowLooks = looks, Smoothing = smoothing };

    private static readonly ControlPoint Behind = new(new Vector3(0f, 2f, 5f), 0f, 0f, 1f);

    private static NearbyCharacters GuardStanding(Vector3 feet, float facing)
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Guard", null, feet, facing)]);
        return characters;
    }

    [Fact]
    public void FollowPlacesTheCameraAtTheOffsetFromTheCharacter()
    {
        var frame = Frame(new AimTracker(GuardStanding(new Vector3(10f, 0f, 0f), 0f)), FollowingAt(Behind));

        Assert.Equal(new Vector3(10f, 2f, 5f), frame.Position);
        Assert.Equal(FreeCamMotion.LookAtFrom(frame.Position, 0f, 0f), frame.LookAt);
    }

    [Fact]
    public void FollowTurnsTheOffsetAndTheAimWithTheCharacter()
    {
        var quarter = MathF.PI / 2f;
        var frame = Frame(new AimTracker(GuardStanding(Vector3.Zero, quarter)), FollowingAt(Behind));

        var expected = new Anchor(Vector3.Zero, quarter).ToWorld(Behind);
        Assert.Equal(expected.Position.X, frame.Position.X, 4);
        Assert.Equal(expected.Position.Z, frame.Position.Z, 4);
        AimsAt(expected.Position + (FreeCamMotion.LookAtFrom(Vector3.Zero, expected.Yaw, 0f) - Vector3.Zero), frame);
    }

    [Fact]
    public void WithTurningOffTheFacingIsHeldFromTheFirstFrame()
    {
        var characters = GuardStanding(Vector3.Zero, 0.7f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind, turns: false);
        Frame(tracker, track);

        characters.Update([new LoadedCharacter("Guard", null, Vector3.Zero, MathF.PI / 2f)]);
        var frame = Frame(tracker, track);

        Near(new Anchor(Vector3.Zero, 0.7f).ToWorld(Behind.Position), frame.Position);
    }

    [Fact]
    public void LookAtCharacterAimsAtTheirAimHeight()
    {
        var frame = Frame(new AimTracker(GuardStanding(Vector3.Zero, 0f)), FollowingAt(Behind, looks: true) with { AimHeight = 1.3f });

        AimsAt(new Vector3(0f, 1.3f, 0f), frame);
    }

    [Fact]
    public void ALostCharacterHoldsTheLastFrameAndNeverFoundUsesTheAnchorFallback()
    {
        var characters = GuardStanding(new Vector3(10f, 0f, 0f), 0f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind);
        var seen = Frame(tracker, track);

        characters.Update([]);
        Assert.Equal(seen, Frame(tracker, track));

        var fresh = Frame(new AimTracker(characters), track);
        Assert.Equal(Behind.Position, fresh.Position);
    }

    [Fact]
    public void FollowPositionEasesAndAResetLandsOnTheCharacter()
    {
        var characters = GuardStanding(Vector3.Zero, 0f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind, smoothing: 1f);
        Frame(tracker, track, 0.5f);

        characters.Update([new LoadedCharacter("Guard", null, new Vector3(10f, 0f, 0f), 0f)]);
        var eased = Frame(tracker, track, 0.5f);
        Assert.Equal(10f * (1f - MathF.Exp(-1f)), eased.Position.X, 3);

        tracker.Reset();
        Assert.Equal(10f, Frame(tracker, track, 0.5f).Position.X, 4);
    }

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }

    private static float LookYaw(CameraState frame) => TrackAim.FromDirection(frame.LookAt - frame.Position).Yaw;

    [Fact]
    public void SmoothingEasesTheRecordedAimAsTheCharacterTurns()
    {
        var quarter = MathF.PI / 2f;
        var characters = GuardStanding(Vector3.Zero, 0f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind, smoothing: 1f);
        Frame(tracker, track, 0.5f);

        characters.Update([new LoadedCharacter("Guard", null, Vector3.Zero, quarter)]);

        Assert.Equal(quarter * (1f - MathF.Exp(-1f)), LookYaw(Frame(tracker, track, 0.5f)), 3);
    }

    [Fact]
    public void WithoutSmoothingTheRecordedAimTurnsExactly()
    {
        var quarter = MathF.PI / 2f;
        var characters = GuardStanding(Vector3.Zero, 0f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind);
        Frame(tracker, track, 0.5f);

        characters.Update([new LoadedCharacter("Guard", null, Vector3.Zero, quarter)]);

        Assert.Equal(quarter, LookYaw(Frame(tracker, track, 0.5f)), 4);
    }

    [Fact]
    public void AResetAfterLosingTheCharacterFallsBackToTheAnchor()
    {
        var characters = GuardStanding(new Vector3(10f, 0f, 0f), 0f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind);
        Frame(tracker, track);
        characters.Update([]);

        tracker.Reset();

        Assert.Equal(Behind.Position, Frame(tracker, track).Position);
    }

    [Fact]
    public void AResetHoldsTheFacingTheCharacterHasThen()
    {
        var characters = GuardStanding(Vector3.Zero, 0.7f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind, turns: false);
        Frame(tracker, track);
        characters.Update([new LoadedCharacter("Guard", null, Vector3.Zero, 1.2f)]);

        tracker.Reset();
        Frame(tracker, track);
        characters.Update([new LoadedCharacter("Guard", null, Vector3.Zero, 2f)]);

        Near(new Anchor(Vector3.Zero, 1.2f).ToWorld(Behind.Position), Frame(tracker, track).Position);
    }

    [Fact]
    public void ARotatedAnchorGivesBackTheOffsetAndItsFallback()
    {
        var anchor = new Anchor(new Vector3(5f, 1f, -3f), 0.8f);
        var world = FollowingAt(Behind) with { Anchor = anchor, Points = [anchor.ToWorld(Behind)] };

        Near(new Vector3(10f, 2f, 5f), Frame(new AimTracker(GuardStanding(new Vector3(10f, 0f, 0f), 0f)), world).Position);
        Near(anchor.ToWorld(Behind.Position), Frame(new AimTracker(new NearbyCharacters()), world).Position);
    }
}
