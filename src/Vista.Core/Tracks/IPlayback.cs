using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>A shot the Director plays frame by frame: where it is, how long the current shot is, and moving through it.</summary>
public interface IPlayback
{
    /// <summary>Where the camera is in the current shot, in seconds.</summary>
    double ShotTime { get; }

    /// <summary>The current shot's length in seconds.</summary>
    double ShotLength { get; }

    /// <summary>True once playback has reached its end and holds its last frame.</summary>
    bool IsFinished { get; }

    /// <summary>Moves on by <paramref name="dt"/> seconds and returns the frame.</summary>
    CameraState? Advance(float dt);

    /// <summary>Jumps to <paramref name="time"/> within the current shot.</summary>
    void Seek(double time);

    /// <summary>Goes back to the start.</summary>
    void Restart();
}
