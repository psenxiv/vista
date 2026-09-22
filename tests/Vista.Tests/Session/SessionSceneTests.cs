using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionSceneTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Editing; Track 1 has points at x = 0, 10, 20 (two 5 s legs).
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.Edit();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        return state;
    }

    private static Guid First(SessionState state) => state.Scene.Tracks[0].Id;

    [Fact]
    public void StartsEditingTheOnlyTrack()
    {
        var state = new SessionState();
        var track = Assert.Single(state.Scene.Tracks);
        Assert.Equal(track.Id, state.EditedTrackId);
        Assert.Same(track, state.Track);
        Assert.Equal("Track 1", state.Track.Name);
    }

    [Fact]
    public void TrackEditsChangeOnlyTheEditedTrack()
    {
        var state = Editing();
        Assert.Null(state.AddTrack());
        state.AddToEnd(Point(50f));

        Assert.Equal(3, state.Scene.Tracks[0].Points.Count);
        Assert.Single(state.Scene.Tracks[1].Points);
        Assert.Same(state.WorldOf(state.Scene.Tracks[1]), state.Track);
    }

    [Fact]
    public void AddTrackSwitchesToItAndClearsTheSelectionAndScrubHead()
    {
        var state = Editing();
        state.Select(1);
        state.ScrubTo(3.0);

        state.AddTrack();

        Assert.Equal(state.Scene.Tracks[1].Id, state.EditedTrackId);
        Assert.Null(state.Selected);
        Assert.Null(state.SelectedKey);
        Assert.Equal(0.0, state.ScrubHead);
    }

    [Fact]
    public void SwitchingIsNotAnUndoStepAndClearsTheSelectionAndScrubHead()
    {
        var state = Editing();
        state.AddTrack();
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;
        state.SwitchTrack(First(state));
        state.Select(2);
        state.ScrubTo(4.0);

        Assert.Null(state.SwitchTrack(second));

        Assert.Equal(second, state.EditedTrackId);
        Assert.Null(state.Selected);
        Assert.Equal(0.0, state.ScrubHead);
        Assert.True(state.Undo());
        Assert.Equal(2, state.Scene.Tracks.Count);
    }

    [Fact]
    public void UndoingAChangeInAnotherTrackSwitchesBackToIt()
    {
        var state = Editing();
        var first = First(state);
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;
        state.SwitchTrack(first);
        state.Select(1);
        state.AddToEnd(Point(30f));
        state.SwitchTrack(second);

        Assert.True(state.Undo());

        Assert.Equal(first, state.EditedTrackId);
        Assert.Equal(3, state.Track.Points.Count);
        Assert.Equal(1, state.Selected);

        Assert.True(state.Redo());

        Assert.Equal(second, state.EditedTrackId);
        Assert.Equal(4, state.Scene.Tracks[0].Points.Count);
        Assert.Null(state.Selected);
    }

    [Fact]
    public void UndoAndRedoCoverSceneEdits()
    {
        var state = Editing();
        var first = First(state);
        state.RenameTrack(first, "Crane");
        Assert.Equal("Crane", state.Track.Name);

        Assert.True(state.Undo());
        Assert.Equal("Track 1", state.Track.Name);
        Assert.True(state.Redo());
        Assert.Equal("Crane", state.Track.Name);
    }

    [Fact]
    public void DuplicateSwitchesToTheCopy()
    {
        var state = Editing();
        Assert.Null(state.DuplicateTrack(First(state)));

        Assert.Equal(2, state.Scene.Tracks.Count);
        Assert.Equal(state.Scene.Tracks[1].Id, state.EditedTrackId);
        Assert.Equal("Track 1 copy", state.Track.Name);
        Assert.Equal(3, state.Track.Points.Count);
    }

    [Fact]
    public void DeletingTheEditedTrackSwitchesToTheOneTakingItsPlace()
    {
        var state = Editing();
        state.AddTrack();
        state.AddTrack();
        var third = state.Scene.Tracks[2].Id;
        state.SwitchTrack(state.Scene.Tracks[1].Id);

        Assert.Null(state.DeleteTrack(state.EditedTrackId));

        Assert.Equal(third, state.EditedTrackId);
    }

    [Fact]
    public void DeletingTheEditedTrackShowsAHiddenNeighbourTakingItsPlace()
    {
        var state = Editing();
        var first = First(state);
        state.AddTrack();
        var second = state.EditedTrackId;
        state.SwitchTrack(first);
        state.SetTrackHidden(second, true);

        Assert.Null(state.DeleteTrack(first));

        Assert.Equal(second, state.EditedTrackId);
        Assert.DoesNotContain(second, state.Scene.Hidden);

        Assert.True(state.Undo());
        Assert.Equal(first, state.EditedTrackId);
        Assert.Contains(second, state.Scene.Hidden);
    }

    [Fact]
    public void DeletingAnotherTrackKeepsTheEditedTrackAndSelection()
    {
        var state = Editing();
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;
        state.SwitchTrack(First(state));
        state.Select(1);

        Assert.Null(state.DeleteTrack(second));

        Assert.Equal(First(state), state.EditedTrackId);
        Assert.Equal(1, state.Selected);
    }

    [Fact]
    public void TheLastTrackCannotBeDeleted()
    {
        var state = Editing();
        Assert.NotNull(state.DeleteTrack(First(state)));
        Assert.Single(state.Scene.Tracks);
    }

    [Fact]
    public void AnEmptyNameIsRefused()
    {
        var state = Editing();
        Assert.NotNull(state.RenameTrack(First(state), " "));
        Assert.Equal("Track 1", state.Track.Name);
    }

    [Fact]
    public void MoveReordersTracksAsAnUndoStep()
    {
        var state = Editing();
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;

        Assert.Null(state.MoveTrack(1, 0));
        Assert.Equal(second, state.Scene.Tracks[0].Id);
        Assert.True(state.Undo());
        Assert.Equal(second, state.Scene.Tracks[1].Id);
    }

    [Fact]
    public void TheEditedTrackCannotBeHiddenButOthersCan()
    {
        var state = Editing();
        state.AddTrack();
        var second = state.EditedTrackId;
        var first = First(state);

        Assert.NotNull(state.SetTrackHidden(second, true));
        Assert.Null(state.SetTrackHidden(first, true));
        Assert.Contains(first, state.Scene.Hidden);
    }

    [Fact]
    public void SwitchingToAHiddenTrackShowsItAsAnUndoStep()
    {
        var state = Editing();
        var first = First(state);
        state.AddTrack();
        state.SetTrackHidden(first, true);

        Assert.Null(state.SwitchTrack(first));

        Assert.Equal(first, state.EditedTrackId);
        Assert.DoesNotContain(first, state.Scene.Hidden);
        Assert.True(state.Undo());
        Assert.Contains(first, state.Scene.Hidden);
    }

    [Fact]
    public void ClearKeepsTheTracksIdAndName()
    {
        var state = Editing();
        var id = state.EditedTrackId;
        state.RenameTrack(id, "Crane");

        Assert.Null(state.ChangeTrack(TrackEditing.Clear));

        Assert.Equal(id, state.EditedTrackId);
        Assert.Equal("Crane", state.Track.Name);
        Assert.Empty(state.Track.Points);
    }

    [Fact]
    public void AChangeThatSwapsTheTrackIsRefused()
    {
        var state = Editing();
        var before = state.Scene;

        Assert.NotNull(state.ChangeTrack(_ => TrackEditing.Empty()));

        Assert.Same(before, state.Scene);
    }

    [Fact]
    public void SceneEditsAndSwitchingAreRefusedUnlessEditing()
    {
        var state = Editing();
        state.AddTrack();
        var first = First(state);
        var second = state.Scene.Tracks[1].Id;
        state.SwitchTrack(first);
        state.Cue();
        state.Play();
        Assert.Equal(CameraMode.Live, state.Mode);

        Assert.NotNull(state.AddTrack());
        Assert.NotNull(state.SwitchTrack(first));
        Assert.NotNull(state.RenameTrack(first, "Crane"));
        Assert.Equal(2, state.Scene.Tracks.Count);

        state.Stop();
        Assert.True(state.Director.IsPaused);

        Assert.NotNull(state.AddTrack());
        Assert.NotNull(state.SwitchTrack(first));
        Assert.NotNull(state.RenameTrack(first, "Crane"));
        Assert.NotNull(state.DuplicateTrack(first));
        Assert.NotNull(state.DeleteTrack(first));
        Assert.NotNull(state.MoveTrack(0, 1));
        Assert.NotNull(state.SetTrackHidden(second, true));

        Assert.Equal(2, state.Scene.Tracks.Count);
        Assert.Empty(state.Scene.Hidden);
    }

    [Fact]
    public void LivePlaysTheEditedTrack()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(4f));

        state.Cue();
        state.Play();
        state.Director.Tick(10f);

        Assert.Equal(2.0, state.Director.ShotTime, 3);
    }
}
