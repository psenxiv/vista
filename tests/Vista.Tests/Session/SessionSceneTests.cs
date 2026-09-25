using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Session;

public class SessionSceneTests
{
    [Fact]
    public void StartsEditingTheOnlyTrack()
    {
        var state = new SessionState();
        var track = Assert.Single(state.Scene.Tracks);
        Assert.Equal(track.Id, state.EditedTrackId);
        Assert.Same(track, state.Track);
    }

    [Fact]
    public void TrackEditsChangeOnlyTheEditedTrack()
    {
        var state = EditingThreePoints();
        Assert.Null(state.AddTrack());
        state.AddToEnd(Point(50f));

        Assert.Equal(3, state.Scene.Tracks[0].Points.Count);
        Assert.Single(state.Scene.Tracks[1].Points);
        Assert.Same(state.World.WorldOf(state.Scene.Tracks[1]), state.Track);
    }

    [Fact]
    public void AddTrackSwitchesToItAndClearsTheSelectionAndScrubHead()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);
        state.Transport.ScrubTo(3.0);

        state.AddTrack();

        Assert.Equal(state.Scene.Tracks[1].Id, state.EditedTrackId);
        Assert.Null(state.Selection.Point);
        Assert.Null(state.Selection.Key);
        Assert.Equal(0.0, state.Transport.ScrubHead);
    }

    [Fact]
    public void SwitchingIsNotAnUndoStepAndClearsTheSelectionAndScrubHead()
    {
        var state = EditingThreePoints();
        state.AddTrack();
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;
        state.SwitchTrack(TrackId(state, 0));
        state.Selection.Select(2);
        state.Transport.ScrubTo(4.0);

        Assert.Null(state.SwitchTrack(second));

        Assert.Equal(second, state.EditedTrackId);
        Assert.Null(state.Selection.Point);
        Assert.Equal(0.0, state.Transport.ScrubHead);
        Assert.True(state.Undo());
        Assert.Equal(2, state.Scene.Tracks.Count);
    }

    [Fact]
    public void UndoingAChangeInAnotherTrackSwitchesBackToIt()
    {
        var state = EditingThreePoints();
        var first = TrackId(state, 0);
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;
        state.SwitchTrack(first);
        state.Selection.Select(1);
        state.AddToEnd(Point(30f));
        state.SwitchTrack(second);

        Assert.True(state.Undo());

        Assert.Equal(first, state.EditedTrackId);
        Assert.Equal(3, state.Track.Points.Count);
        Assert.Equal(1, state.Selection.Point);

        Assert.True(state.Redo());

        Assert.Equal(second, state.EditedTrackId);
        Assert.Equal(4, state.Scene.Tracks[0].Points.Count);
        Assert.Null(state.Selection.Point);
    }

    [Fact]
    public void UndoAndRedoCoverSceneEdits()
    {
        var state = EditingThreePoints();
        var first = TrackId(state, 0);
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
        var state = EditingThreePoints();
        Assert.Null(state.DuplicateTrack(TrackId(state, 0)));

        Assert.Equal(2, state.Scene.Tracks.Count);
        Assert.Equal(state.Scene.Tracks[1].Id, state.EditedTrackId);
        Assert.Equal(3, state.Track.Points.Count);
    }

    [Fact]
    public void DeletingTheEditedTrackSwitchesToTheOneTakingItsPlace()
    {
        var state = EditingThreePoints();
        state.AddTrack();
        state.AddTrack();
        var third = state.Scene.Tracks[2].Id;
        state.SwitchTrack(state.Scene.Tracks[1].Id);

        Assert.Null(state.DeleteTracks([state.EditedTrackId]));

        Assert.Equal(third, state.EditedTrackId);
    }

    [Fact]
    public void DeletingTheEditedTrackShowsAHiddenNeighbourTakingItsPlace()
    {
        var state = EditingThreePoints();
        var first = TrackId(state, 0);
        state.AddTrack();
        var second = state.EditedTrackId;
        state.SwitchTrack(first);
        state.SetTracksHidden([second], true);

        Assert.Null(state.DeleteTracks([first]));

        Assert.Equal(second, state.EditedTrackId);
        Assert.DoesNotContain(second, state.Scene.Hidden);

        Assert.True(state.Undo());
        Assert.Equal(first, state.EditedTrackId);
        Assert.Contains(second, state.Scene.Hidden);
    }

    [Fact]
    public void DeletingAnotherTrackKeepsTheEditedTrackAndSelection()
    {
        var state = EditingThreePoints();
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;
        state.SwitchTrack(TrackId(state, 0));
        state.Selection.Select(1);

        Assert.Null(state.DeleteTracks([second]));

        Assert.Equal(TrackId(state, 0), state.EditedTrackId);
        Assert.Equal(1, state.Selection.Point);
    }

    [Fact]
    public void MoveReordersTracksAsAnUndoStep()
    {
        var state = EditingThreePoints();
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;

        Assert.Null(state.MoveTracks([second], second, state.Scene.Tracks[0].Id));
        Assert.Equal(second, state.Scene.Tracks[0].Id);
        Assert.True(state.Undo());
        Assert.Equal(second, state.Scene.Tracks[1].Id);
    }

    [Fact]
    public void MovingAnUnknownTrackSaysThereIsNoSuchTrack()
    {
        var state = EditingThreePoints();
        var first = state.Scene.Tracks[0].Id;

        Assert.Equal("There is no such track.", state.MoveTracks([Guid.NewGuid()], first, null));
        Assert.Equal("There is no such track.", state.MoveTracks([first], first, Guid.NewGuid()));
    }

    [Fact]
    public void TheEditedTrackCannotBeHiddenButOthersCan()
    {
        var state = EditingThreePoints();
        state.AddTrack();
        var second = state.EditedTrackId;
        var first = TrackId(state, 0);

        Assert.NotNull(state.SetTracksHidden([second], true));
        Assert.Null(state.SetTracksHidden([first], true));
        Assert.Contains(first, state.Scene.Hidden);
    }

    [Fact]
    public void SwitchingToAHiddenTrackShowsItAsAnUndoStep()
    {
        var state = EditingThreePoints();
        var first = TrackId(state, 0);
        state.AddTrack();
        state.SetTracksHidden([first], true);

        Assert.Null(state.SwitchTrack(first));

        Assert.Equal(first, state.EditedTrackId);
        Assert.DoesNotContain(first, state.Scene.Hidden);
        Assert.True(state.Undo());
        Assert.Contains(first, state.Scene.Hidden);
    }

    [Fact]
    public void ClearKeepsTheTracksIdAndName()
    {
        var state = EditingThreePoints();
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
        var state = EditingThreePoints();
        var before = state.Scene;

        Assert.NotNull(state.ChangeTrack(_ => TrackEditing.Empty()));

        Assert.Same(before, state.Scene);
    }

    [Fact]
    public void SceneEditsAndSwitchingAreRefusedUnlessEditing()
    {
        const string refused = "The scene can only change while editing.";
        const string noSwitch = "Tracks can only be switched while editing.";

        var state = EditingThreePoints();
        state.AddTrack();
        var first = TrackId(state, 0);
        var second = state.Scene.Tracks[1].Id;
        state.SwitchTrack(first);
        state.AddToPlaylist([first]);
        GoLive(state);
        Assert.Equal(CameraMode.Live, state.Mode);

        Assert.Equal(refused, state.AddTrack());
        Assert.Equal(noSwitch, state.SwitchTrack(first));
        Assert.Equal(refused, state.RenameTrack(first, "Crane"));
        Assert.Equal(2, state.Scene.Tracks.Count);

        state.Stop();
        Assert.True(state.Director.IsPaused);

        Assert.Equal(refused, state.AddTrack());
        Assert.Equal(noSwitch, state.SwitchTrack(first));
        Assert.Equal(refused, state.RenameTrack(first, "Crane"));
        Assert.Equal(refused, state.DuplicateTrack(first));
        Assert.Equal(refused, state.DeleteTracks([first]));
        Assert.Equal(refused, state.MoveTracks([first], first, second));
        Assert.Equal(refused, state.SetTracksHidden([second], true));

        Assert.Equal(2, state.Scene.Tracks.Count);
        Assert.Empty(state.Scene.Hidden);
    }

    [Fact]
    public void LiveCuesAndPlaysThePlaylistEntryNotTheEditedTrack()
    {
        var state = EditingThreePoints();
        state.AddTrack();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(4f));
        var first = TrackId(state, 0);
        state.AddToPlaylist([first]);
        Assert.NotEqual(first, state.EditedTrackId);

        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal(first, state.PlayingEntry!.TrackId);
        Assert.Equal(10.0, state.Transport.ScrubLength, 3);

        state.Release();
        Assert.Equal(PlayOutcome.StartedFromGame, state.Play());
        Assert.Equal(first, state.PlayingEntry!.TrackId);
    }

    // "Crane": one point at local (2, 0, 0), its anchor facing yaw π/2.
    private static Preset Crane() =>
        new(TrackEditing.Append(TrackEditing.Empty(name: "Crane"), Point(2f)), MathF.PI / 2f);

    [Fact]
    public void AddingAPresetPutsItOnTheGroundUnderTheCameraAndEditsIt()
    {
        var state = new SessionState(_ => 1f);
        state.Edit();

        Assert.Null(state.AddPreset(Crane(), new Vector3(10f, 5f, 20f)));

        Assert.Equal(2, state.Scene.Tracks.Count);
        Assert.Equal(state.Scene.Tracks[1].Id, state.EditedTrackId);
        Assert.Equal("Crane", state.Track.Name);
        // Ground (10, 1, 20); Turn((2, 0, 0), π/2) = (0, 0, −2).
        Near(new Vector3(10f, 1f, 18f), state.Track.Points[0].Position, 1e-4f);
    }

    [Fact]
    public void WithNoGroundAPresetGoesAtTheCamerasHeight()
    {
        var state = new SessionState();
        state.Edit();

        state.AddPreset(Crane(), new Vector3(10f, 5f, 20f));

        // Anchor at the camera (10, 5, 20); Turn((2, 0, 0), π/2) = (0, 0, −2).
        Near(new Vector3(10f, 5f, 18f), state.Track.Points[0].Position, 1e-4f);
    }

    [Fact]
    public void AddingAPresetIsOneUndoStep()
    {
        var state = new SessionState();
        state.Edit();
        var first = TrackId(state, 0);
        state.AddPreset(Crane(), new Vector3(10f, 5f, 20f));

        Assert.True(state.Undo());

        Assert.Single(state.Scene.Tracks);
        Assert.False(state.Scene.AnchorPlaced);
        Assert.Equal(first, state.EditedTrackId);
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void HidingSeveralSkipsTheEditedTrack()
    {
        var state = EditingThreePoints();
        var first = TrackId(state, 0);
        state.AddTrack();
        var second = state.EditedTrackId;

        Assert.Null(state.SetTracksHidden([first, second], true));

        Assert.Equal(new[] { first }, state.Scene.Hidden);
    }

    [Fact]
    public void DeletingSeveralIsOneUndoStep()
    {
        var state = EditingThreePoints();
        state.AddTrack();
        state.AddTrack();
        var ids = state.Scene.Tracks.Select(t => t.Id).ToArray();

        Assert.Null(state.DeleteTracks([ids[1], ids[2]]));
        Assert.Equal(new[] { ids[0] }, state.Scene.Tracks.Select(t => t.Id));
        Assert.Equal(ids[0], state.EditedTrackId);

        Assert.True(state.Undo());
        Assert.Equal(ids, state.Scene.Tracks.Select(t => t.Id));
        Assert.Equal(ids[2], state.EditedTrackId);
    }
}
