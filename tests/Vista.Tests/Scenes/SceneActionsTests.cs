using Vista.Core.Scenes;
using Vista.Core.Session;
using Xunit;

namespace Vista.Tests.Scenes;

public class SceneActionsTests
{
    [Theory]
    [InlineData(CameraMode.Off)]
    [InlineData(CameraMode.View)]
    [InlineData(CameraMode.Editing)]
    public void EveryActionIsAllowedOutsideLiveWhicheverItTargets(CameraMode mode)
    {
        foreach (var action in Enum.GetValues<SceneAction>())
        {
            Assert.True(SceneActions.Allowed(action, targetsOpenScene: true, mode));
            Assert.True(SceneActions.Allowed(action, targetsOpenScene: false, mode));
        }
    }

    [Theory]
    [InlineData(SceneAction.Open, false)]
    [InlineData(SceneAction.Open, true)]
    [InlineData(SceneAction.New, false)]
    [InlineData(SceneAction.New, true)]
    [InlineData(SceneAction.Duplicate, false)]
    [InlineData(SceneAction.Duplicate, true)]
    public void OpenNewAndDuplicateAreRefusedInLiveWhicheverTheyTarget(SceneAction action, bool targetsOpenScene) =>
        Assert.False(SceneActions.Allowed(action, targetsOpenScene, CameraMode.Live));

    [Fact]
    public void DeletingTheOpenSceneIsRefusedInLive() =>
        Assert.False(SceneActions.Allowed(SceneAction.Delete, targetsOpenScene: true, CameraMode.Live));

    [Fact]
    public void DeletingAnotherSceneIsAllowedInLive() =>
        Assert.True(SceneActions.Allowed(SceneAction.Delete, targetsOpenScene: false, CameraMode.Live));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenamingIsAlwaysAllowedInLive(bool targetsOpenScene) =>
        Assert.True(SceneActions.Allowed(SceneAction.Rename, targetsOpenScene, CameraMode.Live));
}
