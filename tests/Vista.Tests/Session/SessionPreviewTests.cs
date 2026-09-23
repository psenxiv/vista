using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

public class SessionPreviewTests
{
    // Editing; three points at x = 0, 10, 20 at 2 yalms per second: a 10 s shot.
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.Edit();
        state.SetTrackSpeed(2f);
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        return state;
    }

    [Fact]
    public void PlayInEditPreviewsFromTheScrubHeadAndStaysInEdit()
    {
        var state = Editing();
        state.ScrubTo(4.0);

        Assert.Equal(PlayOutcome.Previewed, state.Play());

        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.True(state.Previewing);
        Assert.Equal(4.0, state.ScrubHead, 5);
        state.AdvancePreview(1f);
        Assert.Equal(5.0, state.ScrubHead, 5);
        Assert.False(state.Director.IsLive);
    }

    [Theory]
    [InlineData(PlaybackDirection.Forward, 10.0, 0.0)]
    [InlineData(PlaybackDirection.Reverse, 0.0, 10.0)]
    [InlineData(PlaybackDirection.PingPong, 0.0, 0.0)]
    public void PlayFromWhereTheShotFinishesStartsFromTheBeginning(PlaybackDirection direction, double finish, double start)
    {
        var state = Editing();
        state.ChangeTrack(t => TrackEditing.SetDirection(t, direction));
        state.ScrubTo(finish);

        state.Play();

        Assert.Equal(start, state.ScrubHead, 5);
    }

    [Fact]
    public void RestartInEditPreviewsFromTheBeginning()
    {
        var state = Editing();
        state.ScrubTo(6.0);

        Assert.Equal(PlayOutcome.Previewed, state.Restart());

        Assert.Equal(0.0, state.ScrubHead, 5);
        Assert.True(state.Previewing);
    }

    [Fact]
    public void StopKeepsTheScrubHeadWhereThePreviewWas()
    {
        var state = Editing();
        state.Play();
        state.AdvancePreview(3f);

        Assert.True(state.Stop());

        Assert.False(state.Previewing);
        Assert.Equal(3.0, state.ScrubHead, 5);
        Assert.Equal(CameraMode.Editing, state.Mode);
    }

    [Fact]
    public void ATrackThatDoesNotLoopStopsAtTheEndAndALoopingOneCarriesOn()
    {
        var state = Editing();
        state.Play();
        Assert.NotNull(state.AdvancePreview(15f));
        Assert.False(state.Previewing);
        Assert.Equal(10.0, state.ScrubHead, 5);

        state.ChangeTrack(t => TrackEditing.SetLoop(t, true));
        state.Restart();
        state.AdvancePreview(15f);
        Assert.True(state.Previewing);
        Assert.Equal(5.0, state.ScrubHead, 3);
    }

    [Fact]
    public void EditsStopThePreview()
    {
        var state = Editing();

        state.Play();
        state.AddToEnd(Point(30f));
        Assert.False(state.Previewing);

        state.Play();
        state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 1f));
        Assert.False(state.Previewing);

        state.Play();
        state.Undo();
        Assert.False(state.Previewing);

        state.Play();
        state.Redo();
        Assert.False(state.Previewing);

        state.Play();
        state.BeginLiveEdit();
        Assert.False(state.Previewing);
        state.EndLiveEdit();

        state.Play();
        state.AddTrack();
        Assert.False(state.Previewing);
    }

    [Fact]
    public void SwitchingTracksAndScrubbingStopThePreview()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.SwitchTrack(first);

        state.Play();
        state.SwitchTrack(state.Scene.Tracks[1].Id);
        Assert.False(state.Previewing);

        state.SwitchTrack(first);
        state.Play();
        state.BeginScrub();
        Assert.False(state.Previewing);
        state.EndScrub();

        state.Play();
        state.ScrubTo(2.0);
        Assert.False(state.Previewing);
    }

    [Fact]
    public void ReselectingTheEditedTrackKeepsThePreviewButSwitchingAwayStopsIt()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.SwitchTrack(first);

        state.Play();
        Assert.Null(state.SelectTrackAnchor(first));
        Assert.True(state.Previewing);

        Assert.Null(state.SwitchTrack(first));
        Assert.True(state.Previewing);

        Assert.Null(state.SwitchTrack(state.Scene.Tracks[1].Id));
        Assert.False(state.Previewing);
    }

    [Fact]
    public void MovingTheAnchorStopsThePreview()
    {
        var state = Editing();

        state.Play();
        Assert.Null(state.SelectSceneAnchor());
        Assert.True(state.Previewing);
        state.BeginLiveEdit();
        Assert.Null(state.PreviewAnchor(new Anchor(new Vector3(1f, 0f, 0f), 0f), carry: false));
        state.EndLiveEdit();
        Assert.False(state.Previewing);

        state.Play();
        Assert.True(state.Previewing);
        state.BeginLiveEdit();
        Assert.Null(state.PreviewAnchor(new Anchor(new Vector3(2f, 0f, 0f), 0f), carry: true));
        state.EndLiveEdit();
        Assert.False(state.Previewing);
    }

    [Fact]
    public void APreviewRecordsNoUndoStep()
    {
        var state = Editing();
        state.Play();
        state.AdvancePreview(15f);

        Assert.True(state.Undo());
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void LeavingEditEndsThePreview()
    {
        var state = Editing();
        state.AddToPlaylist([state.EditedTrackId]);
        state.Play();

        state.Cue();
        Assert.False(state.Previewing);
        Assert.Equal(CameraMode.Live, state.Mode);

        state.Edit();
        state.Play();
        state.Release();
        Assert.False(state.Previewing);
    }
}
