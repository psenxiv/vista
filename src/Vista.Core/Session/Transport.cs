using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;

namespace Vista.Core.Session;

/// <summary>The Edit preview and the scrub head: what plays while editing, and where the scrub bar stands in any mode.</summary>
public sealed class Transport
{
    private readonly SessionState session;
    private TrackPlayback? preview;
    private double scrubTime;
    private bool resumeAfterScrub;

    internal Transport(SessionState session) => this.session = session;

    /// <summary>True while an Edit preview is playing.</summary>
    public bool Previewing => preview is not null;

    /// <summary>True between <see cref="BeginScrub"/> and <see cref="EndScrub"/>.</summary>
    public bool Scrubbing { get; private set; }

    /// <summary>Seconds under the scrub head: shot time while live or previewing, otherwise the last scrubbed or jumped-to time.</summary>
    public double ScrubHead => session.Mode == CameraMode.Live ? session.Director.ShotTime : preview?.ShotTime ?? Math.Min(scrubTime, session.Duration);

    /// <summary>The scrub bar's length: the playing entry's while live, otherwise the edited track's.</summary>
    public double ScrubLength => session.Mode == CameraMode.Live ? session.Director.ShotLength : session.Duration;

    /// <summary>Advances an Edit preview, stopping it at the end of a cycle that doesn't loop. Returns its frame, or null when not previewing.</summary>
    public CameraState? AdvancePreview(float dt)
    {
        if (preview is not { } playback) return null;
        var frame = playback.Advance(dt);
        if (playback.IsFinished) StopPreview();
        return frame;
    }

    /// <summary>Stops an Edit preview, leaving the scrub head at its shot time. Returns false if none was playing.</summary>
    public bool StopPreview()
    {
        if (preview is not { } playback) return false;
        scrubTime = playback.ShotTime;
        preview = null;
        return true;
    }

    /// <summary>Starts dragging the scrub head; live, playback holds until <see cref="EndScrub"/>. No effect in Off or View.</summary>
    public void BeginScrub()
    {
        StopPreview();
        if (session.Released || Scrubbing) return;
        Scrubbing = true;
        resumeAfterScrub = session.Mode == CameraMode.Live && !session.Director.IsPaused;
        if (session.Mode == CameraMode.Live) session.Director.Pause();
    }

    /// <summary>Moves the scrub head to <paramref name="time"/> within the track; live, playback seeks there. No effect in Off or View.</summary>
    public void ScrubTo(double time)
    {
        if (session.Mode == CameraMode.Editing) StopPreview();
        if (session.Released) return;
        scrubTime = Math.Clamp(time, 0.0, ScrubLength);
        if (session.Mode == CameraMode.Live) session.Director.Seek(scrubTime);
    }

    /// <summary>Stops dragging the scrub head; live, playback carries on as it was before.</summary>
    public void EndScrub()
    {
        if (!Scrubbing) return;
        Scrubbing = false;
        if (session.Mode == CameraMode.Live && resumeAfterScrub) session.Director.Resume();
    }

    /// <summary>Plays <paramref name="playback"/> as the Edit preview, from the scrub head unless <paramref name="fromStart"/> or the scrub head is where the shot finishes.</summary>
    internal void StartPreview(TrackPlayback playback, bool fromStart)
    {
        if (!fromStart)
        {
            playback.Seek(ScrubHead);
            if (playback.IsFinished) playback.Restart();
        }

        preview = playback;
    }

    /// <summary>Stops a scrub drag without resuming anything, as changing mode does.</summary>
    internal void DropScrub() => Scrubbing = false;

    /// <summary>Leaves the scrub head at <paramref name="time"/> for when no preview plays.</summary>
    internal void Park(double time) => scrubTime = time;
}
