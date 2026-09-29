using System.Globalization;
using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks.Playback;

namespace Vista.Core.Session;

/// <summary>The Edit preview, its tools and the edited track's scrub head: what plays while editing, and where the scrub bar stands in any mode.</summary>
public sealed class Transport
{
    /// <summary>The slowest playback rate, as a multiple of normal speed.</summary>
    public const float MinPlaybackRate = 0.01f;

    /// <summary>The fastest playback rate, as a multiple of normal speed.</summary>
    public const float MaxPlaybackRate = 2f;

    /// <summary>The step a dragged playback rate moves in.</summary>
    public const float PlaybackRateStep = 0.1f;

    private readonly SessionState session;
    private TrackPlayback? preview;
    private CameraState? previewFrame;
    private bool previewShown;
    private double scrubTime;

    internal Transport(SessionState session) => this.session = session;

    /// <summary>True while an Edit preview is playing.</summary>
    public bool Previewing => preview is not null;

    /// <summary>True between <see cref="BeginScrub"/> and <see cref="EndScrub"/>.</summary>
    public bool Scrubbing { get; private set; }

    /// <summary>True while Edit previews play on the ghost camera and the game camera stays with the free-cam. Never saved.</summary>
    public bool Ghost { get; private set; }

    /// <summary>How fast Edit previews play, as a multiple of normal speed, from <see cref="MinPlaybackRate"/> to <see cref="MaxPlaybackRate"/>. Never saved.</summary>
    public float PlaybackRate { get; private set; } = 1f;

    /// <summary>Seconds into the edited track under the scrub head: the preview's shot time while one plays, otherwise the last scrubbed or jumped-to time.</summary>
    public double ScrubHead => preview?.ShotTime ?? Math.Min(scrubTime, session.Duration);

    /// <summary>Where the ghost camera is: the preview's frame while one plays, otherwise the scrub head's; null unless the ghost is on in Edit.</summary>
    public CameraState? GhostFrame =>
        GhostShown ? (Previewing ? previewFrame : null) ?? session.World.FrameAt(ScrubHead) : null;

    /// <summary>Where the ghost's look-ahead is aimed, or null without a ghost or a look-ahead.</summary>
    public Vector3? GhostLookAhead => GhostShown ? session.World.Evaluator.LookAheadSpot(ScrubHead) : null;

    /// <summary>The frame new points come from while a preview plays on the game camera, or null when they come from the camera.</summary>
    public CameraState? FrameForNewPoints => Previewing && !Ghost ? previewFrame : null;

    /// <summary>Turns the ghost camera on or off. Returns why it was refused, or null.</summary>
    public string? SetGhost(bool on)
    {
        if (session.Mode != CameraMode.Editing)
            return "The ghost camera is only for Edit.";
        Ghost = on;
        return null;
    }

    /// <summary>Sets how fast Edit previews play. Returns why it was refused, or null.</summary>
    public string? SetPlaybackRate(float rate)
    {
        if (session.Mode != CameraMode.Editing)
            return "The playback rate is only for Edit previews.";
        if (!(rate >= MinPlaybackRate && rate <= MaxPlaybackRate))
            return $"The playback rate must be from {MinPlaybackRate.ToString(CultureInfo.InvariantCulture)} to {MaxPlaybackRate.ToString(CultureInfo.InvariantCulture)}.";
        PlaybackRate = rate;
        return null;
    }

    /// <summary>A dragged playback rate: to the nearest <see cref="PlaybackRateStep"/>, then from <see cref="MinPlaybackRate"/> to <see cref="MaxPlaybackRate"/>.</summary>
    public static float SnapPlaybackRate(float rate) =>
        Math.Clamp(MathF.Round(rate / PlaybackRateStep) * PlaybackRateStep, MinPlaybackRate, MaxPlaybackRate);

    /// <summary>Advances an Edit preview by <paramref name="dt"/> at the playback rate, stopping it at the end of a cycle that doesn't loop. Returns its frame, or null when not previewing.</summary>
    public CameraState? AdvancePreview(float dt)
    {
        if (preview is not { } playback)
            return null;
        var frame = playback.Advance(dt * PlaybackRate);
        previewFrame = frame;
        if (playback.IsFinished)
            StopPreview();
        return frame;
    }

    /// <summary>What the editor camera does this frame, <paramref name="dt"/> seconds on, with <paramref name="flying"/> true while the flight keys are held.</summary>
    public EditFrame EditingFrame(float dt, bool flying)
    {
        if (Ghost)
        {
            previewShown = false;
            AdvancePreview(dt);
            return default;
        }

        if (Previewing && flying)
            StopPreview();
        var frame = AdvancePreview(dt);

        if (previewShown && !Previewing)
        {
            previewShown = false;
            return new EditFrame(null, previewFrame ?? session.World.FrameAt(ScrubHead));
        }

        previewShown = Previewing;
        if (frame is { } previewing)
            return new EditFrame(previewing, null);
        return Scrubbing && session.World.FrameAt(ScrubHead) is { } scrubbed ? new EditFrame(scrubbed, null) : default;
    }

    /// <summary>Stops an Edit preview, leaving the scrub head at its shot time. Returns false if none was playing.</summary>
    public bool StopPreview()
    {
        if (preview is not { } playback)
            return false;
        scrubTime = playback.ShotTime;
        preview = null;
        return true;
    }

    /// <summary>Starts dragging the scrub head, stopping any preview. Edit only.</summary>
    public void BeginScrub()
    {
        StopPreview();
        if (session.Mode == CameraMode.Editing)
            Scrubbing = true;
    }

    /// <summary>Moves the scrub head to <paramref name="time"/> within the edited track, stopping any preview. Edit only.</summary>
    public void ScrubTo(double time)
    {
        if (session.Mode != CameraMode.Editing)
            return;
        StopPreview();
        scrubTime = Math.Clamp(time, 0.0, session.Duration);
    }

    /// <summary>Stops dragging the scrub head. Returns the scrub head's frame for the free-cam without the ghost, otherwise null.</summary>
    public CameraState? EndScrub()
    {
        if (!Scrubbing)
            return null;
        Scrubbing = false;
        if (session.Mode != CameraMode.Editing || Ghost)
            return null;
        previewShown = false;
        return session.World.FrameAt(ScrubHead);
    }

    /// <summary>Plays <paramref name="playback"/> as the Edit preview, from the scrub head unless <paramref name="fromStart"/> or the scrub head is where the shot finishes.</summary>
    internal void StartPreview(TrackPlayback playback, bool fromStart)
    {
        if (!fromStart)
        {
            playback.Seek(ScrubHead);
            if (playback.IsFinished)
                playback.Restart();
        }

        previewFrame = null;
        preview = playback;
    }

    /// <summary>Stops a scrub drag without resuming anything, as changing mode does.</summary>
    internal void DropScrub() => Scrubbing = false;

    /// <summary>Drops a waiting hand-off, when something else places the free-cam or Edit is entered afresh.</summary>
    internal void DropHandOff() => previewShown = false;

    /// <summary>Leaves the scrub head at <paramref name="time"/> for when no preview plays.</summary>
    internal void Park(double time) => scrubTime = time;

    private bool GhostShown => Ghost && session.Mode == CameraMode.Editing;
}
