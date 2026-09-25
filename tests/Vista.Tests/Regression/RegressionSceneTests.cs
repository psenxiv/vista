using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Regression;

public class RegressionSceneTests
{
    [Fact]
    public void TheSceneFileIsCurrent()
    {
        var built = SceneJson.Write(RegressionScene.Build());
        var path = RegressionScene.FilePath();
        if (Environment.GetEnvironmentVariable(RegressionScene.WriteVariable) == "1")
        {
            File.WriteAllText(path, built);
            return;
        }

        // Git may check the file out with either line ending.
        var committed = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : "";
        Assert.True(committed == built.ReplaceLineEndings("\n"), "Run `make regression-scene`");
    }

    [Fact]
    public void TheSceneHasOnlyItsDeclaredSnaps()
    {
        var scene = SceneJson.Read(File.ReadAllText(RegressionScene.FilePath()));
        Assert.Equal(RegressionScene.Cases.Count, scene.Tracks.Count);
        var problems = new List<string>();
        foreach (var (track, expected) in scene.Tracks.Zip(RegressionScene.Cases))
        {
            var world = SceneGeometry.InWorld(scene, track);
            var steps = StepsIn(world);
            if (steps.Count != expected.Snaps)
                problems.Add(
                    $"{track.Name}: {steps.Count} steps, {expected.Snaps} expected"
                        + string.Concat(steps.Select(s => $"\n  {s}"))
                );
            var evaluator = new TrackEvaluator(world);
            var target = AimTracker.AimPoint(world, null);
            var twist = LargestTwist(t => evaluator.Evaluate(t, target)!.Value, evaluator.Duration);
            if (world.Aim != AimMode.AimKeys && twist > PictureSpinLimit)
                problems.Add($"{track.Name}: the picture turns {twist / Deg:0.###}° in a frame");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Every step in a track in the world's facing, position, field of view and roll, described.</summary>
    private static List<string> StepsIn(Track world)
    {
        var evaluator = new TrackEvaluator(world);
        var target = AimTracker.AimPoint(world, null);
        CameraState Frame(double t) => evaluator.Evaluate(t, target)!.Value;
        var duration = evaluator.Duration;

        return
        [
            .. Steps(Frame, (a, b) => Vector3.Distance(a.Forward, b.Forward), FacingStepFloor, duration)
                .Select(s =>
                    $"facing turns {2f * MathF.Asin(MathF.Min(s.Size / 2f, 1f)) / Deg:0.###}° at {s.Time:0.####} s"
                ),
            .. Steps(Frame, (a, b) => Vector3.Distance(a.Position, b.Position), PositionStepFloor, duration)
                .Select(s => $"position jumps {s.Size:0.###} yalms at {s.Time:0.####} s"),
            .. Steps(Frame, (a, b) => MathF.Abs(a.Fov - b.Fov), FovStepFloor, duration)
                .Select(s => $"field of view pops {s.Size / Deg:0.###}° at {s.Time:0.####} s"),
            .. PictureSteps(Frame, duration)
                .Select(s =>
                    $"picture turns {2f * MathF.Asin(MathF.Min(s.Size / 2f, 1f)) / Deg:0.###}° at {s.Time:0.####} s"
                ),
        ];
    }
}
