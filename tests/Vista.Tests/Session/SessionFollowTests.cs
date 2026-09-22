using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionFollowTests
{
    private static readonly ControlPoint Camera = new(new Vector3(1f, 2f, 3f), 0f, 0f, 1f);

    private static LoadedCharacter Guard(Vector3 feet, float facing = 0f) => new("Guard", null, feet, facing);

    // Editing with n recorded points, and Guard at (10, 0, 0) facing 0.
    private static (SessionState State, NearbyCharacters Characters) EditingWith(int points)
    {
        var characters = new NearbyCharacters();
        characters.Update([Guard(new Vector3(10f, 0f, 0f))]);
        var state = new SessionState(null, characters);
        state.Edit();
        for (var i = 0; i < points; i++) state.AddToEnd(new ControlPoint(new Vector3(3f + (5f * i), 1f, -4f), 0.3f, 0.1f, 1f));
        return (state, characters);
    }

    // Following Guard, captured at (10, 2, 5): the offset (0, 2, 5).
    private static (SessionState State, NearbyCharacters Characters) FollowingGuard()
    {
        var (state, characters) = EditingWith(points: 0);
        state.SetAim(AimMode.FollowTarget, Camera);
        state.SetTarget("Guard", null);
        state.AddToEnd(new ControlPoint(new Vector3(10f, 2f, 5f), 0f, 0f, 1f));
        return (state, characters);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }

    [Fact]
    public void FollowTargetIsRefusedForATrackWithMoreThanOnePoint()
    {
        var (state, _) = EditingWith(points: 2);
        Assert.Equal("Follow Target needs a track with one point", state.SetAim(AimMode.FollowTarget, Camera));
    }

    [Fact]
    public void ASecondPointIsRefusedUnderFollowTarget()
    {
        var (state, _) = FollowingGuard();
        Assert.Equal("A Follow Target track has one point", state.AddToEnd(Camera));
    }

    [Fact]
    public void AFollowTracksAnchorCannotBeSelected()
    {
        var (state, _) = FollowingGuard();
        Assert.Equal("A Follow Target track's anchor is hidden", state.SelectTrackAnchor(state.EditedTrackId));
        Assert.Null(state.SelectedAnchor);
    }

    [Fact]
    public void EnteringFollowTargetClearsATrackAnchorSelection()
    {
        var (state, _) = EditingWith(points: 1);
        state.SetTarget("Guard", null);
        Assert.Null(state.SelectTrackAnchor(state.EditedTrackId));

        Assert.Null(state.SetAim(AimMode.FollowTarget, Camera));

        Assert.Null(state.SelectedAnchor);
        Assert.Null(state.SelectedAnchorInWorld);
    }

    [Fact]
    public void CapturingNeedsAFoundCharacter()
    {
        var (state, characters) = EditingWith(points: 0);
        state.SetAim(AimMode.FollowTarget, Camera);
        Assert.Equal("Choose a character to follow", state.AddToEnd(Camera));

        state.SetTarget("Guard", null);
        characters.Update([]);
        Assert.Equal("Character not found", state.AddToEnd(Camera));
    }

    [Fact]
    public void CapturingStoresTheOffsetAndTheTrackShowsItAtTheCharacter()
    {
        var (state, characters) = EditingWith(points: 0);
        state.SetAim(AimMode.FollowTarget, Camera);
        state.SetTarget("Guard", null);
        state.AddToEnd(new ControlPoint(new Vector3(10f, 2f, 5f), 0f, 0f, 1f));

        characters.Update([Guard(new Vector3(20f, 0f, 0f))]);

        Assert.Equal(new Vector3(20f, 2f, 5f), state.Track.Points[0].Position);
    }

    [Fact]
    public void SwitchingToFollowKeepsTheCameraWhereItIs()
    {
        var (state, characters) = EditingWith(points: 1);
        characters.Update([Guard(new Vector3(10f, 0f, 0f), 0.5f)]);
        var before = state.Track.Points[0];
        state.SetTarget("Guard", null);

        state.SetAim(AimMode.FollowTarget, Camera);

        AssertNear(before.Position, state.Track.Points[0].Position);
        Assert.Equal(before.Yaw, state.Track.Points[0].Yaw, 3);
    }

    [Fact]
    public void ChoosingANewCharacterKeepsTheOrbit()
    {
        var (state, characters) = FollowingGuard();
        characters.Update([Guard(new Vector3(10f, 0f, 0f)), new LoadedCharacter("Scout", null, new Vector3(-5f, 0f, 8f), 0f)]);
        var stored = state.StoredTrack.Points[0];

        state.SetTarget("Scout", null);

        Assert.Equal(stored, state.StoredTrack.Points[0]);
        AssertNear(new Vector3(-5f, 2f, 13f), state.Track.Points[0].Position);
    }

    [Fact]
    public void ChoosingTheFirstCharacterKeepsTheCameraWhereItIs()
    {
        var (state, _) = EditingWith(points: 1);
        state.SetAim(AimMode.FollowTarget, Camera);
        var before = state.Track.Points[0];

        state.SetTarget("Guard", null);

        AssertNear(before.Position, state.Track.Points[0].Position);
    }

    [Fact]
    public void LeavingFollowKeepsTheCameraWhereItIs()
    {
        var (state, characters) = FollowingGuard();
        characters.Update([Guard(new Vector3(20f, 0f, -3f), 0.5f)]);
        var before = state.Track.Points[0];

        state.SetAim(AimMode.AimKeys, Camera);

        AssertNear(before.Position, state.Track.Points[0].Position);
        Assert.Equal(before.Yaw, state.Track.Points[0].Yaw, 3);
    }

    [Fact]
    public void EditingTheFollowPointEditsItWhereItIsShown()
    {
        var (state, _) = FollowingGuard();
        var target = new ControlPoint(new Vector3(12f, 3f, 4f), 0.2f, 0f, 1f);

        state.ReplacePoint(0, target);

        AssertNear(target.Position, state.Track.Points[0].Position);
    }

    [Fact]
    public void TheFollowSwitchesAreUndoSteps()
    {
        var (state, _) = FollowingGuard();

        Assert.Null(state.SetFollowTurns(false));
        Assert.False(state.Track.FollowTurns);
        Assert.Null(state.SetFollowLooks(false));
        Assert.False(state.Track.FollowLooks);

        Assert.True(state.Undo());
        Assert.True(state.Track.FollowLooks);
        Assert.True(state.Undo());
        Assert.True(state.Track.FollowTurns);
    }

    [Fact]
    public void PlaybackUsesTheOffsetNotTheShownPoint()
    {
        var (state, characters) = FollowingGuard();
        characters.Update([Guard(new Vector3(40f, 0f, 0f))]);

        var frame = state.FrameAt(0.0)!.Value;

        AssertNear(new Vector3(40f, 2f, 5f), frame.Position);
    }

    [Fact]
    public void UndoingTheSwitchToFollowRestoresThePointsNumbers()
    {
        var (state, characters) = EditingWith(points: 1);
        characters.Update([Guard(new Vector3(10f, 0f, 0f), 0.5f)]);
        state.SetTarget("Guard", null);
        var stored = state.StoredTrack.Points[0];
        state.SetAim(AimMode.FollowTarget, Camera);
        Assert.NotEqual(stored, state.StoredTrack.Points[0]);

        Assert.True(state.Undo());

        Assert.Equal(stored, state.StoredTrack.Points[0]);
        Assert.Equal(AimMode.AimKeys, state.StoredTrack.Aim);
    }

    [Fact]
    public void UndoingTheFirstCharacterRestoresThePointsNumbers()
    {
        var (state, _) = EditingWith(points: 1);
        state.SetAim(AimMode.FollowTarget, Camera);
        var stored = state.StoredTrack.Points[0];
        state.SetTarget("Guard", null);
        Assert.NotEqual(stored, state.StoredTrack.Points[0]);

        Assert.True(state.Undo());

        Assert.Equal(stored, state.StoredTrack.Points[0]);
        Assert.Null(state.StoredTrack.TargetName);
    }

    [Fact]
    public void TheShownTrackIsTheSameInstanceWhileTheCharacterStandsStill()
    {
        var (state, characters) = FollowingGuard();
        Assert.Same(state.Track, state.Track);

        characters.Update([Guard(new Vector3(11f, 0f, 0f))]);

        var moved = state.Track;
        Assert.Equal(11f, moved.Points[0].Position.X, 3);
        Assert.Same(moved, state.Track);
    }

    [Fact]
    public void TheStoredTrackIsTheSameInstanceWhileTheCharacterWalks()
    {
        var (state, characters) = FollowingGuard();
        var stored = state.StoredTrack;

        characters.Update([Guard(new Vector3(30f, 0f, 0f), 1f)]);

        Assert.Same(stored, state.StoredTrack);
    }

    [Fact]
    public void TheOrbitReadsTheOffsetOnlyUnderFollowTarget()
    {
        var (state, _) = FollowingGuard();
        var orbit = state.FollowOrbit!.Value;

        Assert.Equal(5f, orbit.Distance, 3);
        Assert.Equal(0f, orbit.Angle, 3);
        Assert.Equal(2f, orbit.Height, 3);
        Assert.Null(EditingWith(points: 1).State.FollowOrbit);
    }

    [Fact]
    public void DraggingTheOrbitMovesThePointLiveAsOneUndoStep()
    {
        var (state, _) = FollowingGuard();
        var before = state.Track.Points[0].Position;

        state.BeginLiveEdit();
        Assert.Null(state.PreviewFollowOrbit(new Orbit(5f, MathF.PI / 4f, 2f)));
        Assert.Null(state.PreviewFollowOrbit(new Orbit(5f, MathF.PI / 2f, 2f)));
        state.EndLiveEdit();

        AssertNear(new Vector3(15f, 2f, 0f), state.Track.Points[0].Position);
        Assert.Equal(MathF.PI / 2f, state.FollowOrbit!.Value.Angle, 3);
        state.Undo();
        AssertNear(before, state.Track.Points[0].Position);
    }

    [Fact]
    public void TheOrbitIsRefusedOutsideALiveEditOrFollowTarget()
    {
        var (following, _) = FollowingGuard();
        Assert.NotNull(following.PreviewFollowOrbit(new Orbit(5f, 0f, 2f)));

        var (recorded, _) = EditingWith(points: 1);
        recorded.BeginLiveEdit();
        Assert.NotNull(recorded.PreviewFollowOrbit(new Orbit(5f, 0f, 2f)));
    }

    [Fact]
    public void DraggingTheAimHeightIsLiveAndOneUndoStep()
    {
        var (state, _) = FollowingGuard();
        var before = state.Track.AimHeight;

        Assert.NotNull(state.PreviewAimHeight(2f));
        state.BeginLiveEdit();
        Assert.Null(state.PreviewAimHeight(2f));
        Assert.Equal(2f, state.Track.AimHeight);
        Assert.Null(state.PreviewAimHeight(2.5f));
        state.EndLiveEdit();

        Assert.Equal(2.5f, state.Track.AimHeight);
        Assert.True(state.Undo());
        Assert.Equal(before, state.Track.AimHeight);
    }
}
