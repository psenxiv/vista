using System.Numerics;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Session;

public class SessionStateTests
{
    private static ControlPoint Point(float x)
        => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Two points, one 5 s leg.
    private static SessionState EditingWithTrack()
    {
        var state = new SessionState();
        state.Edit();
        state.ChangeTrack(t => TrackEditing.Append(TrackEditing.Append(t, Point(0f)), Point(10f)));
        return state;
    }

    private static SessionState Live()
    {
        var state = EditingWithTrack();
        state.Play();
        return state;
    }

    [Fact]
    public void StartsOffWithAnEmptyTrackAndNothingLocked()
    {
        var state = new SessionState();
        Assert.Equal(CameraMode.Off, state.Mode);
        Assert.Empty(state.Track.Points);
        Assert.False(state.LocksInput);
    }

    [Fact]
    public void EditFromOffEntersEditingAndLocksInput()
    {
        var state = new SessionState();
        Assert.Equal(EditOutcome.FromOff, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.True(state.LocksInput);
    }

    [Fact]
    public void EditWhileEditingChangesNothing()
    {
        var state = EditingWithTrack();
        Assert.Equal(EditOutcome.Unchanged, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
    }

    [Fact]
    public void EditFromLiveTakesTheDirectorOffline()
    {
        var state = Live();
        Assert.Equal(EditOutcome.FromLive, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.False(state.Director.IsLive);
    }

    [Fact]
    public void PlayWithNoPointsIsRefusedAndChangesNothing()
    {
        var off = new SessionState();
        Assert.Equal(PlayOutcome.Refused, off.Play());
        Assert.Equal(CameraMode.Off, off.Mode);

        var editing = new SessionState();
        editing.Edit();
        Assert.Equal(PlayOutcome.Refused, editing.Play());
        Assert.Equal(CameraMode.Editing, editing.Mode);
        Assert.False(editing.Director.IsLive);
    }

    [Fact]
    public void PlayFromEditingGoesLive()
    {
        var state = EditingWithTrack();
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.True(state.Director.IsLive);
        Assert.True(state.LocksInput);
    }

    [Fact]
    public void PlayFromOffSaysItStartedFromOff()
    {
        var state = EditingWithTrack();
        state.Release();
        Assert.Equal(PlayOutcome.StartedFromOff, state.Play());
        Assert.Equal(CameraMode.Live, state.Mode);
    }

    [Fact]
    public void PlayWhilePlayingOnlyReHides()
    {
        var state = Live();
        state.Director.Tick(1f);
        Assert.Equal(PlayOutcome.ReHid, state.Play());
        Assert.Equal(1.0, state.Director.Elapsed, 5);
    }

    [Fact]
    public void PlayWhilePausedResumes()
    {
        var state = Live();
        state.Director.Tick(1f);
        state.Stop();
        Assert.Equal(PlayOutcome.Resumed, state.Play());
        Assert.False(state.Director.IsPaused);
        Assert.Equal(1.0, state.Director.Elapsed, 5);
    }

    [Fact]
    public void PlayWhenFinishedStartsAgainFromZero()
    {
        var state = Live();
        state.Director.Tick(6f);
        Assert.True(state.Director.IsFinished);
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.Equal(0.0, state.Director.Elapsed);
    }

    [Fact]
    public void PlayWhenPausedAndFinishedStartsAgainFromZero()
    {
        var state = Live();
        state.Director.Tick(6f);
        state.Stop();
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.False(state.Director.IsPaused);
        Assert.Equal(0.0, state.Director.Elapsed);
    }

    [Fact]
    public void RestartWhileLiveStartsFromZero()
    {
        var state = Live();
        state.Director.Tick(2f);
        Assert.Equal(PlayOutcome.Started, state.Restart());
        Assert.Equal(0.0, state.Director.Elapsed);
    }

    [Fact]
    public void StopPausesOnlyWhileLive()
    {
        Assert.False(new SessionState().Stop());
        Assert.False(EditingWithTrack().Stop());

        var live = Live();
        Assert.True(live.Stop());
        Assert.True(live.Director.IsPaused);
        Assert.Equal(CameraMode.Live, live.Mode);
    }

    [Fact]
    public void ReleaseTurnsEverythingOff()
    {
        Assert.False(new SessionState().Release());

        var editing = EditingWithTrack();
        Assert.True(editing.Release());
        Assert.Equal(CameraMode.Off, editing.Mode);

        var live = Live();
        Assert.True(live.Release());
        Assert.Equal(CameraMode.Off, live.Mode);
        Assert.False(live.Director.IsLive);
        Assert.Null(live.Director.Tick(1f / 60f));
    }

    [Fact]
    public void ReleaseKeepsTheTrack()
    {
        var state = EditingWithTrack();
        state.Release();
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void TheTrackChangesOnlyWhileEditing()
    {
        const string refused = "The track can only change while editing.";

        var off = new SessionState();
        Assert.Equal(refused, off.ChangeTrack(t => TrackEditing.Append(t, Point(0f))));
        Assert.Empty(off.Track.Points);

        var live = Live();
        Assert.Equal(refused, live.ChangeTrack(t => TrackEditing.Append(t, Point(20f))));
        Assert.Equal(2, live.Track.Points.Count);

        live.Stop();
        Assert.Equal(refused, live.ChangeTrack(t => TrackEditing.Append(t, Point(20f))));
    }

    [Fact]
    public void ChangeTrackAppliesWhileEditing()
    {
        var state = EditingWithTrack();
        Assert.Null(state.ChangeTrack(t => TrackEditing.SetLeg(t, 1, 8f)));
        Assert.Equal(8f, TrackEditing.LegSeconds(state.Track, 1));
    }

    [Fact]
    public void ChangeTrackReturnsTheRefusalAndKeepsTheTrack()
    {
        var state = EditingWithTrack();
        var before = state.Track;

        Assert.Equal("leg seconds must be > 0", state.ChangeTrack(t => TrackEditing.SetLeg(t, 1, 0f)));
        Assert.Same(before, state.Track);
    }

    [Fact]
    public void ChangeTrackRefusesATrackThatCannotBePlayed()
    {
        var state = EditingWithTrack();
        var before = state.Track;
        var backwards = new[]
        {
            new TimingKey(5f, 0f, TangentMode.Auto, 0f, 0f),
            new TimingKey(0f, 1f, TangentMode.Auto, 0f, 0f),
        };

        Assert.Equal("timing keys must have strictly increasing times", state.ChangeTrack(t => t with { Timing = backwards }));
        Assert.Same(before, state.Track);
    }
}
