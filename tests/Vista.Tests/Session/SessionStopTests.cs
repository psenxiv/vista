using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

public class SessionStopTests
{
    // Live with a playlist of one two-point track.
    private static SessionState Live()
    {
        var state = new SessionState();
        state.Edit();
        state.ChangeTrack(t => TrackEditing.Append(TrackEditing.Append(t, Point(0f)), Point(10f)));
        state.AddToPlaylist([state.EditedTrackId]);
        state.Cue();
        state.Play();
        return state;
    }

    [Fact]
    public void AFaultStopsVistaAndReleasesToOff()
    {
        var state = Live();
        Assert.Equal(CameraMode.Live, state.Mode);

        Assert.True(state.ReportFault("camera update hook"));

        Assert.True(state.Stopped);
        Assert.Equal("fault in camera update hook", state.StopReason);
        Assert.Equal(CameraMode.Off, state.Mode);
        Assert.False(state.Director.IsLive);
    }

    [Fact]
    public void AFailedTouchPointStopsVista()
    {
        var state = new SessionState();

        Assert.True(state.ReportTouchPoint("movement lock", passed: false));

        Assert.True(state.Stopped);
        Assert.Equal("movement lock unavailable", state.StopReason);
    }

    [Fact]
    public void APassedTouchPointLeavesVistaRunning()
    {
        var state = Live();

        Assert.False(state.ReportTouchPoint("movement lock", passed: true));

        Assert.False(state.Stopped);
        Assert.Null(state.StopReason);
        Assert.Equal(CameraMode.Live, state.Mode);
    }

    [Fact]
    public void OnlyTheFirstStopTellsThePlayerAndItsReasonIsKept()
    {
        var state = new SessionState();

        Assert.True(state.ReportTouchPoint("mouse wheel hook", passed: false));
        Assert.False(state.ReportFault("framework update"));
        Assert.False(state.ReportTouchPoint("input query hooks", passed: false));

        Assert.Equal("mouse wheel hook unavailable", state.StopReason);
    }

    [Fact]
    public void EditAndLiveAreRefusedOnceStopped()
    {
        var state = Live();
        state.ReportFault("draw");

        Assert.False(state.CanGoLive);
        Assert.Equal(EditOutcome.Refused, state.Edit());
        Assert.Equal(PlayOutcome.Refused, state.Play());
        Assert.Equal(PlayOutcome.Refused, state.Restart());
        Assert.Equal(PlayOutcome.Refused, state.Cue());
        Assert.Equal(CameraMode.Off, state.Mode);
        Assert.False(state.Director.IsLive);
    }

    [Fact]
    public void ViewIsStillAllowedOnceStopped()
    {
        var state = new SessionState();
        state.ReportFault("command");

        state.Release(CameraMode.View);

        Assert.Equal(CameraMode.View, state.Mode);
    }

    [Fact]
    public void TheStopMessageIsTheSpecsWording() =>
        Assert.Equal(
            "Vista has stopped. Reload it in /xlplugins, or check for an update if that doesn't help.",
            SessionState.StopMessage
        );
}
