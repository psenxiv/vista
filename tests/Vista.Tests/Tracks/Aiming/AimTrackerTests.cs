using System.Numerics;
using CsCheck;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Playback;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Aiming;

public class AimTrackerTests
{
    // One point at the origin, recorded aim yaw 0.5.
    private static readonly ControlPoint Camera = new(Vector3.Zero, 0.5f, 0f, 1f);
    private static readonly Vector3 Recorded = FreeCamMotion.LookAtFrom(Vector3.Zero, 0.5f, 0f);

    private static Track Single(AimMode aim) => TrackEditing.Append(TrackEditing.Empty(aim), Camera);

    private static Track Watching(string? name = "Guard", float smoothing = 0f) =>
        WatchingGuard(smoothing, yaw: 0.5f) with
        {
            TargetName = name,
        };

    private static CameraState Frame(AimTracker tracker, Track track, float dt = 1f / 60f) =>
        tracker.Frame(new TrackEvaluator(track), track, 0.0, dt)!.Value;

    [Fact]
    public void WatchAimsAtTheCharacterAtItsAimHeight()
    {
        AimsAt(new Vector3(0f, 0f, -10f), Frame(new AimTracker(GuardAt(0f)), Watching()), 3);
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
        characters.Update([
            Guard(new Vector3(-20f, -TrackEditing.DefaultAimHeight, -10f)),
            Guard(new Vector3(20f, -TrackEditing.DefaultAimHeight, -10f)),
        ]);
        var track = Watching() with { Anchor = new Anchor(new Vector3(15f, 0f, 0f), 0f) };

        AimsAt(new Vector3(20f, 0f, -10f), Frame(new AimTracker(characters), track), 3);
    }

    [Fact]
    public void TheTracksWorldReachesTheCharacterSearch()
    {
        var characters = new NearbyCharacters();
        characters.Update([
            new LoadedCharacter("Aya", "Gilgamesh", new Vector3(-20f, -TrackEditing.DefaultAimHeight, -10f)),
            new LoadedCharacter("Aya", "Cactuar", new Vector3(20f, -TrackEditing.DefaultAimHeight, -10f)),
        ]);
        var track = Watching("Aya") with
        {
            TargetWorld = "Gilgamesh",
            Anchor = new Anchor(new Vector3(15f, 0f, 0f), 0f),
        };

        Assert.Equal(new Vector3(-20f, 0f, -10f), AimTracker.CharacterAim(track, characters));
    }

    [Fact]
    public void LookAtAimsAtThePoint()
    {
        var track = Single(AimMode.LookAt) with { LookAt = new Vector3(0f, 0f, -10f), LookAtPlaced = true };

        AimsAt(new Vector3(0f, 0f, -10f), Frame(new AimTracker(null), track), 3);
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

        GuardAt(characters, 10f);
        AimsAt(
            Vector3.Lerp(new Vector3(0f, 0f, -10f), new Vector3(10f, 0f, -10f), 1f - MathF.Exp(-1f)),
            Frame(tracker, track, 0.5f),
            3
        );

        tracker.Reset();
        AimsAt(new Vector3(10f, 0f, -10f), Frame(tracker, track), 3);
    }

    [Fact]
    public void FindingTheCharacterAgainEasesFromTheRecordedAim()
    {
        var characters = new NearbyCharacters();
        var tracker = new AimTracker(characters);
        var track = Watching(smoothing: 1f);
        Frame(tracker, track);

        GuardAt(characters, 0f);

        AimsAt(Vector3.Lerp(Recorded, new Vector3(0f, 0f, -10f), 1f - MathF.Exp(-1f)), Frame(tracker, track, 0.5f), 3);
    }

    [Fact]
    public void WatchingACharacterPassingOverheadTurnsThePictureRoundWithoutFlipping()
    {
        // The guard crosses from x = -20 to 20 at 10 a second, their aim point 10 yalms up, straight over the camera at
        // 2 s. The picture never turns more than the spin limit in a frame, and 2 s after the pass it's upright again.
        var characters = new NearbyCharacters();
        var tracker = new AimTracker(characters);
        var track = Watching();
        var evaluator = new TrackEvaluator(track);
        CameraState? last = null;
        CameraState frame = default;
        for (var i = 0; i <= 240; i++)
        {
            GuardAt(characters, new Vector3(-20f + (i / 6f), 10f, 0f));
            frame = tracker.Frame(evaluator, track, 0.0, 1f / 60f)!.Value;
            if (last is { } previous)
                Assert.InRange(
                    Twist(previous.LookAt - previous.Position, previous.Up, frame.LookAt - frame.Position, frame.Up),
                    0f,
                    PictureSpinLimit
                );
            last = frame;
        }

        // The guard ends at x = 20, their aim point (20, 10, 0): facing (20, 10, 0)/√500, upright leans back, (-10, 20, 0)/√500.
        Near(new Vector3(-10f, 20f, 0f) / MathF.Sqrt(500f), frame.Up, 1e-3f);
    }

    [Fact]
    public void WatchingOverheadKeepsTheUpSquareToTheFacingAwayFromTheOrigin()
    {
        // The camera sits at (3, 0, 0) and the guard crosses straight over it, 10 yalms up. Wherever the camera is, the
        // picture's up stays square to the way it faces.
        var characters = new NearbyCharacters();
        var tracker = new AimTracker(characters);
        var track = TrackEditing.Append(
            TrackEditing.Empty(AimMode.WatchTarget),
            new ControlPoint(new Vector3(3f, 0f, 0f), 0.5f, 0f, 1f)
        ) with
        {
            TargetName = "Guard",
        };
        var evaluator = new TrackEvaluator(track);
        for (var i = 0; i <= 240; i++)
        {
            GuardAt(characters, new Vector3(-17f + (i / 6f), 10f, 0f));
            var frame = tracker.Frame(evaluator, track, 0.0, 1f / 60f)!.Value;
            Assert.Equal(0f, Vector3.Dot(frame.Up, Vector3.Normalize(frame.LookAt - frame.Position)), 1e-5f);
        }
    }

    [Fact]
    public void LosingTheCharacterPassesTheRecordedFrameThroughUnsettled()
    {
        // Watching the guard straight overhead, then losing them: the frame falls back to the recorded aim, level at yaw
        // 0.5, whose upright up is (0, 1, 0), exactly, rather than turning toward it from the overhead up.
        var characters = new NearbyCharacters();
        var tracker = new AimTracker(characters);
        var track = Watching();
        GuardAt(characters, new Vector3(0f, 10f, 0f));
        Frame(tracker, track);

        characters.Update([]);

        Assert.Equal(Vector3.UnitY, Frame(tracker, track).Up);
    }

    [Fact]
    public void ATargetOnTheCameraKeepsTheLastGoodAim()
    {
        var tracker = new AimTracker(null);
        var far = Single(AimMode.LookAt) with { LookAt = new Vector3(0f, 0f, -10f), LookAtPlaced = true };
        var near = far with { LookAt = new Vector3(0.05f, 0f, 0f) };
        Frame(tracker, far);

        AimsAt(new Vector3(0f, 0f, -10f), Frame(tracker, near), 3);
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
    private static Track FollowingAt(
        ControlPoint offset,
        bool turns = true,
        bool looks = false,
        float smoothing = 0f
    ) =>
        TrackEditing.Append(TrackEditing.Empty(AimMode.FollowTarget), offset) with
        {
            TargetName = "Guard",
            FollowTurns = turns,
            FollowLooks = looks,
            Smoothing = smoothing,
        };

    private static readonly ControlPoint Behind = new(new Vector3(0f, 2f, 5f), 0f, 0f, 1f);

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
        var frame = Frame(new AimTracker(GuardStanding(Vector3.Zero, QuarterTurn)), FollowingAt(Behind));

        var expected = new Anchor(Vector3.Zero, QuarterTurn).ToWorld(Behind);
        Assert.Equal(expected.Position.X, frame.Position.X, 4);
        Assert.Equal(expected.Position.Z, frame.Position.Z, 4);
        AimsAt(expected.Position + (FreeCamMotion.LookAtFrom(Vector3.Zero, expected.Yaw, 0f) - Vector3.Zero), frame, 3);
    }

    [Fact]
    public void WithTurningOffTheFacingIsHeldFromTheFirstFrame()
    {
        var characters = GuardStanding(Vector3.Zero, 0.7f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind, turns: false);
        Frame(tracker, track);

        characters.Update([Guard(Vector3.Zero, QuarterTurn)]);
        var frame = Frame(tracker, track);

        Near(new Anchor(Vector3.Zero, 0.7f).ToWorld(Behind.Position), frame.Position, 1e-4f);
    }

    [Fact]
    public void LookAtCharacterAimsAtTheirAimHeight()
    {
        var frame = Frame(
            new AimTracker(GuardStanding(Vector3.Zero, 0f)),
            FollowingAt(Behind, looks: true) with
            {
                AimHeight = 1.3f,
            }
        );

        AimsAt(new Vector3(0f, 1.3f, 0f), frame, 3);
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

        characters.Update([Guard(new Vector3(10f, 0f, 0f), 0f)]);
        var eased = Frame(tracker, track, 0.5f);
        Assert.Equal(10f * (1f - MathF.Exp(-1f)), eased.Position.X, 3);

        tracker.Reset();
        Assert.Equal(10f, Frame(tracker, track, 0.5f).Position.X, 4);
    }

    private static float LookYaw(CameraState frame) => TrackAim.FromDirection(frame.LookAt - frame.Position).Yaw;

    [Fact]
    public void SmoothingEasesTheRecordedAimAsTheCharacterTurns()
    {
        var characters = GuardStanding(Vector3.Zero, 0f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind, smoothing: 1f);
        Frame(tracker, track, 0.5f);

        characters.Update([Guard(Vector3.Zero, QuarterTurn)]);

        Assert.Equal(QuarterTurn * (1f - MathF.Exp(-1f)), LookYaw(Frame(tracker, track, 0.5f)), 3);
    }

    [Fact]
    public void WithoutSmoothingTheRecordedAimTurnsExactly()
    {
        var characters = GuardStanding(Vector3.Zero, 0f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind);
        Frame(tracker, track, 0.5f);

        characters.Update([Guard(Vector3.Zero, QuarterTurn)]);

        Assert.Equal(QuarterTurn, LookYaw(Frame(tracker, track, 0.5f)), 4);
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
        characters.Update([Guard(Vector3.Zero, 1.2f)]);

        tracker.Reset();
        Frame(tracker, track);
        characters.Update([Guard(Vector3.Zero, 2f)]);

        Near(new Anchor(Vector3.Zero, 1.2f).ToWorld(Behind.Position), Frame(tracker, track).Position, 1e-4f);
    }

    [Fact]
    public void ARotatedAnchorGivesBackTheOffsetAndItsFallback()
    {
        var anchor = new Anchor(new Vector3(5f, 1f, -3f), 0.8f);
        var world = FollowingAt(Behind) with { Anchor = anchor, Points = [anchor.ToWorld(Behind)] };

        Near(
            new Vector3(10f, 2f, 5f),
            Frame(new AimTracker(GuardStanding(new Vector3(10f, 0f, 0f), 0f)), world).Position,
            1e-4f
        );
        Near(anchor.ToWorld(Behind.Position), Frame(new AimTracker(new NearbyCharacters()), world).Position, 1e-4f);
    }

    [Fact]
    public void TheTargetPointIsTheCharactersAimPointForWatchAndFollow()
    {
        var guard = GuardStanding(new Vector3(3f, 0f, 4f), 0f);

        Assert.Equal(new Vector3(3f, 1.3f, 4f), AimTracker.TargetPoint(Watching() with { AimHeight = 1.3f }, guard));
        Assert.Equal(
            new Vector3(3f, 1.3f, 4f),
            AimTracker.TargetPoint(FollowingAt(Behind) with { AimHeight = 1.3f }, guard)
        );
        Assert.Null(AimTracker.TargetPoint(Single(AimMode.AimKeys) with { TargetName = "Guard" }, guard));
        Assert.Null(AimTracker.TargetPoint(Watching(), new NearbyCharacters()));
    }

    [Fact]
    public void TheAimPointIsWhereTheCameraLooks()
    {
        var guard = GuardStanding(new Vector3(3f, 0f, 4f), 0f);
        var lookAt = Single(AimMode.LookAt) with { LookAt = new Vector3(1f, 2f, 3f), LookAtPlaced = true };

        Assert.Equal(new Vector3(1f, 2f, 3f), AimTracker.AimPoint(lookAt, guard));
        Assert.Equal(new Vector3(3f, 1.3f, 4f), AimTracker.AimPoint(Watching() with { AimHeight = 1.3f }, guard));
        Assert.Equal(
            new Vector3(3f, 1.3f, 4f),
            AimTracker.AimPoint(FollowingAt(Behind, looks: true) with { AimHeight = 1.3f }, guard)
        );
        Assert.Null(AimTracker.AimPoint(FollowingAt(Behind, looks: false), guard));
        Assert.Null(AimTracker.AimPoint(Single(AimMode.AimKeys), guard));
    }

    /// <summary>A character's walk: where their feet are and which way they face at each waypoint, and the seconds each leg between waypoints takes.</summary>
    private sealed record Walk((Vector3 Feet, float Facing)[] Waypoints, float[] Legs)
    {
        public double Duration => Legs.Sum();

        /// <summary>The guard <paramref name="time"/> seconds into the walk, moving and turning evenly along each leg, and standing at the end once it's over.</summary>
        public LoadedCharacter At(double time)
        {
            for (var leg = 0; leg < Legs.Length; leg++)
            {
                if (time > Legs[leg])
                {
                    time -= Legs[leg];
                    continue;
                }

                var (from, to) = (Waypoints[leg], Waypoints[leg + 1]);
                var t = (float)(time / Legs[leg]);
                return Guard(Vector3.Lerp(from.Feet, to.Feet, t), from.Facing + ((to.Facing - from.Facing) * t));
            }

            return Guard(Waypoints[^1].Feet, Waypoints[^1].Facing);
        }

        public override string ToString() =>
            $"Waypoints: {string.Join(", ", Waypoints)}\nLegs: {string.Join(", ", Legs)}";
    }

    private static readonly Gen<(Vector3 Feet, float Facing)> AnyWaypoint = Gen.Select(
        AnyPosition,
        Gen.Float[-MathF.PI, MathF.PI]
    );

    /// <summary>A walk through any places that, on the way, drops the character's aim point <paramref name="aimHeight"/> above their feet from straight over <paramref name="camera"/> to as far straight under it, passing through it.</summary>
    private static Gen<Walk> AnyWalkPast(Vector3 camera, float aimHeight) =>
        from before in AnyWaypoint.Array[0, 2]
        from rise in Gen.Float[0f, 5f]
        from facing in Gen.Float[-MathF.PI, MathF.PI]
        from after in AnyWaypoint.Array[0, 2]
        let feet = camera - (Vector3.UnitY * aimHeight)
        from legs in Gen.Float[0.2f, 5f].Array[before.Length + after.Length + 1]
        select new Walk(
            [.. before, (feet + (Vector3.UnitY * rise), facing), (feet - (Vector3.UnitY * rise), facing), .. after],
            legs
        );

    /// <summary>A generated path track watching the guard, with any aim height and smoothing, sometimes cut to its first point so the camera stands still.</summary>
    private static readonly Gen<Track> AnyWatchTrack =
        from track in AnyPathTrack
        from single in Gen.Bool
        from settings in AnyTargetSettings
        let cut = single ? TrackEditing.Delete(track, [.. Enumerable.Range(1, track.Points.Count - 1)]) : track
        select WithTarget(TrackEditing.SetAim(cut, AimMode.WatchTarget, track.Points[0]), "Guard", null, settings);

    /// <summary>A Follow Target track on the guard at any offset, or straight over or under them, or on their aim point, with any aim height and smoothing, turning with them or not and looking at them or not.</summary>
    private static readonly Gen<Track> AnyFollowTrack =
        from settings in AnyTargetSettings
        // 0 any offset, 1 straight over or under the guard's feet, 2 on their aim point.
        from kind in Gen.Int[0, 2]
        from point in AnyPoint
        from height in Gen.Float[-5f, 5f]
        from turns in Gen.Bool
        from looks in Gen.Bool
        let offset = kind switch
        {
            0 => point.Position,
            1 => new Vector3(0f, height, 0f),
            _ => new Vector3(0f, settings.AimHeight, 0f),
        }
        let track = TrackEditing.Append(TrackEditing.Empty(AimMode.FollowTarget), point with { Position = offset })
        select TrackEditing.SetFollowLooks(
            TrackEditing.SetFollowTurns(WithTarget(track, "Guard", null, settings), turns),
            looks
        );

    /// <summary>The most frames a watched or followed walk is played for.</summary>
    private const int FrameBudget = 1500;

    [Fact]
    [Trait("Category", "Property")]
    public void EveryWatchAndFollowFrameIsWellFormed()
    {
        (
            from track in Gen.OneOf(AnyWatchTrack, AnyFollowTrack)
            from walk in AnyWalkPast(track.Points[0].Position, track.AimHeight)
            from steps in AnyFrameStep.Array[1, 32]
            select (Track: track, Walk: walk, Steps: steps)
        ).Sample(
            x =>
            {
                var characters = new NearbyCharacters();
                var playback = new TrackPlayback(x.Track, characters);
                var clock = 0.0;
                for (var i = 0; i < FrameBudget && clock <= x.Walk.Duration + 1.0; i++)
                {
                    var dt = x.Steps[i % x.Steps.Length];
                    clock += dt;
                    characters.Update([x.Walk.At(clock)]);
                    if (playback.Advance(dt) is { } frame)
                        AssertWellFormed(frame, $"Frame {i} at {clock:0.######} s");
                }
            },
            iter: 1000,
            print: Kept<(Track Track, Walk Walk, float[] Steps)>(x =>
                $"{PrintTrack(x.Track)}\n{x.Walk}\nSteps: {string.Join(", ", x.Steps)}"
            )
        );
    }
}
