using Vista.Core.Scenes;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.TrackRuns;

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
            var run = new Run(world);
            var steps = run.Snaps();
            if (steps.Count != expected.Snaps)
                problems.Add(
                    $"{track.Name}: {steps.Count} steps, {expected.Snaps} expected"
                        + string.Concat(steps.Select(s => $"\n  {s}"))
                );
            var twist = run.LargestTwist();
            if (world.Aim != AimMode.AimKeys && twist > PictureSpinLimit)
                problems.Add($"{track.Name}: the picture turns {twist / Deg:0.###}° in a frame");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
